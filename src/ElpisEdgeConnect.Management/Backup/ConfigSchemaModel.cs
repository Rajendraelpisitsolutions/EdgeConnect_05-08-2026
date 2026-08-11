// ============================================================================
// File: Backup/ConfigSchemaModel.cs
// Purpose: The reflection-derived "config schema model" the redaction engine
//          walks in lockstep with a JSON document to decide each property's
//          world (ADR-0020 Amendment 1). A property is an OPAQUE BOUNDARY
//          (World 2) iff it is typed JsonElement? or carries [JsonExtensionData];
//          every other property is a TYPED node (World 1). Reflection makes the
//          boundary self-maintaining — a new JsonElement? property becomes
//          World 2 automatically (M-B plan v2, Q-B1 locked).
//
// Reference: docs/decisions/0020-diagnostic-bundle-redaction-spec.md
//            docs/sessions/2026-05-31-adr0020-mb-implementation-plan-v2.md §1
//
// DETERMINISM: the model is built once per type and cached; Dump() emits a
// canonical (alphabetically ordered) representation so the B1 snapshot test
// surfaces any structural change (e.g. a field changing from JsonElement? to a
// typed dictionary) as a reviewable PR diff.
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Management.Backup;

/// <summary>Base type for nodes in the config schema model.</summary>
public abstract class SchemaNode
{
    internal abstract void Dump(StringBuilder sb, int indent);
}

/// <summary>A scalar leaf (string, number, bool, enum, date, etc.).</summary>
public sealed class LeafSchemaNode : SchemaNode
{
    /// <summary>Shared instance — leaves carry no state.</summary>
    public static readonly LeafSchemaNode Instance = new();
    private LeafSchemaNode() { }
    internal override void Dump(StringBuilder sb, int indent) => sb.Append("Leaf");
}

/// <summary>
/// An opaque boundary — a <see cref="JsonElement"/> connection block. Inside
/// it the engine switches to name-based World 2 classification; the protocol is
/// read from the enclosing object's <c>protocolName</c> sibling.
/// </summary>
public sealed class OpaqueBoundarySchemaNode : SchemaNode
{
    /// <summary>Shared instance — boundaries carry no state.</summary>
    public static readonly OpaqueBoundarySchemaNode Instance = new();
    private OpaqueBoundarySchemaNode() { }
    internal override void Dump(StringBuilder sb, int indent) => sb.Append("Opaque");
}

/// <summary>A collection node — each element is classified by <see cref="Element"/>.</summary>
public sealed class ArraySchemaNode : SchemaNode
{
    /// <summary>Schema applied to every element of the collection.</summary>
    public required SchemaNode Element { get; init; }

    internal override void Dump(StringBuilder sb, int indent)
    {
        sb.Append("Array[ ");
        Element.Dump(sb, indent);
        sb.Append(" ]");
    }
}

/// <summary>A typed object/record. Each known property maps to its tier + child schema.</summary>
public sealed class TypedObjectSchemaNode : SchemaNode
{
    /// <summary>CLR type name (for the snapshot dump only).</summary>
    public required string TypeName { get; init; }

    /// <summary>
    /// True when the record carries a <see cref="JsonExtensionDataAttribute"/>
    /// member — i.e. unknown JSON keys on this object are operator overflow
    /// (World 2b, fail-open) rather than World 1.
    /// </summary>
    public required bool HasExtensionData { get; init; }

    /// <summary>
    /// True when the CLR type is an EdgeConnect application type (namespace
    /// under <c>ElpisEdgeConnect</c>). The redaction drift guard requires a
    /// <c>[BundleTier]</c> on every property of an application type; framework
    /// types (e.g. <see cref="System.Collections.Generic.KeyValuePair{TKey,TValue}"/>)
    /// are exempt because their members cannot be attributed.
    /// </summary>
    public required bool IsApplicationType { get; init; }

    /// <summary>Known properties, keyed case-insensitively by CLR property name.</summary>
    public required IReadOnlyDictionary<string, SchemaProperty> Properties { get; init; }

    internal override void Dump(StringBuilder sb, int indent)
    {
        sb.Append("Object(").Append(TypeName).Append(')');
        if (HasExtensionData)
        {
            sb.Append(" [+extensionData]");
        }

        var pad = new string(' ', (indent + 1) * 2);
        foreach (var prop in Properties.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append('\n').Append(pad).Append(prop.Name).Append(": ")
              .Append(prop.Tier?.ToString() ?? "-").Append(" -> ");
            prop.Child.Dump(sb, indent + 1);
        }
    }
}

/// <summary>A single property of a <see cref="TypedObjectSchemaNode"/>.</summary>
public sealed class SchemaProperty
{
    /// <summary>CLR property name (matched case-insensitively against JSON keys).</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The <see cref="BundleTier"/> declared by <c>[BundleTier]</c> on the
    /// property, or <see langword="null"/> when unattributed. (Attributes are
    /// placed in M-B sub-milestone B2; in B1 every typed property is null.)
    /// </summary>
    public BundleTier? Tier { get; init; }

    /// <summary>The schema node the property's value is classified by.</summary>
    public required SchemaNode Child { get; init; }
}

/// <summary>
/// Builds a <see cref="SchemaNode"/> tree from a CLR type by reflection.
/// Results are cached per type. Pure and deterministic.
/// </summary>
public static class ConfigSchemaModelBuilder
{
    private static readonly ConcurrentDictionary<Type, SchemaNode> Cache = new();

    private static readonly HashSet<Type> LeafTypes = new()
    {
        typeof(string), typeof(decimal), typeof(DateTime), typeof(DateTimeOffset),
        typeof(TimeSpan), typeof(Guid), typeof(Uri), typeof(object),
    };

    /// <summary>Build (or fetch the cached) schema model for <paramref name="type"/>.</summary>
    public static SchemaNode Build(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Cache.GetOrAdd(type, t => BuildInternal(t, new HashSet<Type>()));
    }

    /// <summary>Render the canonical, deterministic dump of a schema model.</summary>
    public static string Dump(SchemaNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var sb = new StringBuilder();
        node.Dump(sb, 0);
        return sb.ToString();
    }

    private static SchemaNode BuildInternal(Type type, HashSet<Type> stack)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            type = underlying;
        }

        // JsonElement -> opaque boundary (World 2).
        if (type == typeof(JsonElement))
        {
            return OpaqueBoundarySchemaNode.Instance;
        }

        if (IsLeaf(type))
        {
            return LeafSchemaNode.Instance;
        }

        // Dictionaries (transform maps etc.) serialize as JSON OBJECTS, not
        // arrays, and their values are benign typed config — model them as a
        // leaf so the walker does not descend (and so the drift guard never
        // demands a [BundleTier] on framework KeyValuePair members). If a future
        // dictionary can hold secrets, revisit with a dedicated node type.
        if (IsDictionary(type))
        {
            return LeafSchemaNode.Instance;
        }

        if (TryGetEnumerableElement(type, out var elementType))
        {
            return new ArraySchemaNode { Element = BuildInternal(elementType, stack) };
        }

        // Defensive cycle guard — config records are acyclic, but never loop.
        if (!stack.Add(type))
        {
            return LeafSchemaNode.Instance;
        }

        try
        {
            var properties = new Dictionary<string, SchemaProperty>(StringComparer.OrdinalIgnoreCase);
            var hasExtensionData = false;

            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0)
                {
                    continue;
                }
                if (p.GetCustomAttribute<JsonExtensionDataAttribute>() is not null)
                {
                    hasExtensionData = true;
                    continue;
                }
                if (p.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
                {
                    continue;
                }

                var tier = p.GetCustomAttribute<BundleTierAttribute>()?.Tier;
                var child = BuildInternal(p.PropertyType, stack);
                properties[p.Name] = new SchemaProperty { Name = p.Name, Tier = tier, Child = child };
            }

            return new TypedObjectSchemaNode
            {
                TypeName = type.Name,
                HasExtensionData = hasExtensionData,
                IsApplicationType = type.Namespace?.StartsWith("ElpisEdgeConnect", StringComparison.Ordinal) == true,
                Properties = properties,
            };
        }
        finally
        {
            stack.Remove(type);
        }
    }

    private static bool IsLeaf(Type type) =>
        type.IsPrimitive || type.IsEnum || LeafTypes.Contains(type);

    private static bool IsDictionary(Type type)
    {
        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            if (def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>) || def == typeof(Dictionary<,>))
            {
                return true;
            }
        }

        return type.GetInterfaces().Any(i =>
            i.IsGenericType &&
            (i.GetGenericTypeDefinition() == typeof(IDictionary<,>) ||
             i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));
    }

    /// <summary>
    /// True when <paramref name="type"/> is an enumerable collection (and not a
    /// string); yields the element type. Arrays and any
    /// <see cref="IEnumerable{T}"/> implementation are handled.
    /// </summary>
    private static bool TryGetEnumerableElement(Type type, out Type elementType)
    {
        elementType = typeof(object);

        if (type == typeof(string))
        {
            return false;
        }

        if (type.IsArray)
        {
            elementType = type.GetElementType() ?? typeof(object);
            return true;
        }

        var enumerable = (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ? type
            : type.GetInterfaces().FirstOrDefault(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

        if (enumerable is not null)
        {
            elementType = enumerable.GetGenericArguments()[0];
            return true;
        }

        return false;
    }
}
