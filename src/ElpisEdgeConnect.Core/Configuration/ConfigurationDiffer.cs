// ============================================================================
// File: Configuration/ConfigurationDiffer.cs
// Purpose: Computes a structured diff between two GatewayConfiguration
//          instances. Used by the audit log and the future admin UI.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone B2
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Computes a deterministic, structured diff between two
/// <see cref="GatewayConfiguration"/> instances. The output is a list of
/// <see cref="ConfigurationChange"/> entries describing additions,
/// removals, and modifications across sources, sinks, routes, and the
/// gateway settings block.
/// </summary>
/// <remarks>
/// <para>
/// The differ is intentionally coarse-grained: it reports modifications at
/// the entity level (e.g., "Source 'focas-jyoti17' modified") rather than
/// emitting per-field deltas for every property. The audit log captures
/// the change kind and entity id; the full before/after content lives in
/// the history files for users who need a deeper diff.
/// </para>
/// <para>
/// Determinism: the output order is sources → sinks → routes → gateway,
/// with entries within each kind sorted by id. This makes the audit log
/// stable across runs and easy to compare in tests.
/// </para>
/// </remarks>
public sealed class ConfigurationDiffer
{
    /// <summary>Singleton instance.</summary>
    public static ConfigurationDiffer Instance { get; } = new();

    // Records that contain JsonElement? or IDictionary<string, JsonElement>
    // fields (e.g., SourceInstanceConfig.Connection, PublishingSettings.Extras)
    // do not compare structurally with default record equality — JsonElement
    // falls back to reference equality. Two configs round-tripped through
    // System.Text.Json will compare unequal even when their content is
    // identical. We work around this by serializing both items to JSON via
    // a fixed options instance and comparing the resulting strings, which is
    // both deterministic and structural.
    private static readonly JsonSerializerOptions ComparisonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    private static bool AreStructurallyEqual<T>(T a, T b)
    {
        if (a is null && b is null)
        {
            return true;
        }
        if (a is null || b is null)
        {
            return false;
        }
        if (ReferenceEquals(a, b))
        {
            return true;
        }
        var aJson = JsonSerializer.Serialize(a, ComparisonOptions);
        var bJson = JsonSerializer.Serialize(b, ComparisonOptions);
        return aJson == bJson;
    }

    /// <summary>
    /// Diff <paramref name="oldConfig"/> against <paramref name="newConfig"/>.
    /// Either may be null (treated as "empty configuration") to support the
    /// initial-apply case.
    /// </summary>
    public IReadOnlyList<ConfigurationChange> Diff(
        GatewayConfiguration? oldConfig,
        GatewayConfiguration newConfig)
    {
        System.ArgumentNullException.ThrowIfNull(newConfig);

        var changes = new List<ConfigurationChange>();

        DiffCollection(
            oldConfig?.Sources ?? [],
            newConfig.Sources,
            s => s.InstanceId,
            ConfigurationEntityKind.Source,
            "Source",
            changes);

        DiffCollection(
            oldConfig?.Sinks ?? [],
            newConfig.Sinks,
            s => s.InstanceId,
            ConfigurationEntityKind.Sink,
            "Sink",
            changes);

        DiffCollection(
            oldConfig?.Routes ?? [],
            newConfig.Routes,
            r => r.RouteId,
            ConfigurationEntityKind.Route,
            "Route",
            changes);

        DiffGateway(oldConfig?.Gateway, newConfig.Gateway, changes);

        return changes;
    }

    /// <summary>
    /// Build a short human-readable summary of a change list, suitable for
    /// audit log <see cref="ConfigurationAuditEntry.Summary"/> and admin UI
    /// list views.
    /// </summary>
    public static string Summarize(IReadOnlyList<ConfigurationChange> changes)
    {
        if (changes.Count == 0)
        {
            return "No changes";
        }

        var added = changes.Count(c => c.Kind == ConfigurationChangeKind.Added);
        var removed = changes.Count(c => c.Kind == ConfigurationChangeKind.Removed);
        var modified = changes.Count(c => c.Kind == ConfigurationChangeKind.Modified);

        var parts = new List<string>(3);
        if (added > 0)
        {
            parts.Add($"{added} added");
        }
        if (removed > 0)
        {
            parts.Add($"{removed} removed");
        }
        if (modified > 0)
        {
            parts.Add($"{modified} modified");
        }

        return string.Join(", ", parts);
    }

    private static void DiffCollection<T>(
        IReadOnlyList<T> oldItems,
        IReadOnlyList<T> newItems,
        System.Func<T, string> keySelector,
        ConfigurationEntityKind entityKind,
        string entityLabel,
        List<ConfigurationChange> changes)
        where T : notnull
    {
        var oldByKey = oldItems.ToDictionary(keySelector);
        var newByKey = newItems.ToDictionary(keySelector);

        var allKeys = new SortedSet<string>(oldByKey.Keys);
        foreach (var k in newByKey.Keys)
        {
            allKeys.Add(k);
        }

        foreach (var key in allKeys)
        {
            var inOld = oldByKey.TryGetValue(key, out var oldItem);
            var inNew = newByKey.TryGetValue(key, out var newItem);

            if (inNew && !inOld)
            {
                changes.Add(new ConfigurationChange
                {
                    Kind = ConfigurationChangeKind.Added,
                    EntityKind = entityKind,
                    EntityId = key,
                    Summary = $"{entityLabel} '{key}' added",
                });
            }
            else if (inOld && !inNew)
            {
                changes.Add(new ConfigurationChange
                {
                    Kind = ConfigurationChangeKind.Removed,
                    EntityKind = entityKind,
                    EntityId = key,
                    Summary = $"{entityLabel} '{key}' removed",
                });
            }
            else if (inOld && inNew && !AreStructurallyEqual(oldItem, newItem))
            {
                changes.Add(new ConfigurationChange
                {
                    Kind = ConfigurationChangeKind.Modified,
                    EntityKind = entityKind,
                    EntityId = key,
                    Summary = $"{entityLabel} '{key}' modified",
                });
            }
        }
    }

    private static void DiffGateway(
        GatewaySettings? oldGateway,
        GatewaySettings newGateway,
        List<ConfigurationChange> changes)
    {
        if (oldGateway is null)
        {
            changes.Add(new ConfigurationChange
            {
                Kind = ConfigurationChangeKind.Added,
                EntityKind = ConfigurationEntityKind.GatewaySettings,
                EntityId = newGateway.GatewayId,
                Summary = $"Gateway '{newGateway.GatewayId}' initialized",
            });
            return;
        }

        // GatewaySettings is a record, but its SensitiveTags collection has
        // REFERENCE equality under the synthesized Equals — a JSON round-trip
        // produces a new (content-equal) list instance, which would otherwise
        // register as a phantom "modified". Compare the collection by content,
        // and the rest via the synthesized comparison with the list normalised
        // to a shared reference (this automatically covers every scalar field,
        // present and future). If another collection field is ever added to
        // GatewaySettings, extend the content comparison here.
        var tagsEqual = oldGateway.SensitiveTags.SequenceEqual(
            newGateway.SensitiveTags, StringComparer.Ordinal);
        var scalarsEqual = (oldGateway with { SensitiveTags = newGateway.SensitiveTags })
            .Equals(newGateway);
        if (!tagsEqual || !scalarsEqual)
        {
            changes.Add(new ConfigurationChange
            {
                Kind = ConfigurationChangeKind.Modified,
                EntityKind = ConfigurationEntityKind.GatewaySettings,
                EntityId = newGateway.GatewayId,
                Summary = $"Gateway '{newGateway.GatewayId}' settings modified",
            });
        }
    }
}
