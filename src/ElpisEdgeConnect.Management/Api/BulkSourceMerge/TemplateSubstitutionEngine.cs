// ============================================================================
// File: Api/BulkSourceMerge/TemplateSubstitutionEngine.cs
// Purpose: JSON-safe placeholder substitution per v3.1 §2 lock.
//
//          Existing chip-3 templates contain BOTH string-position placeholders
//          (e.g. "{{ deviceId }}") AND raw-token-position placeholders (e.g.
//          "Enabled": {{ enabled }}). The raw form means the template text is
//          NOT valid JSON before substitution -- so parse-first approaches
//          don't work; we substitute text first then parse + validate.
//
//          v3.1 §2 mandatory safety rules (locked):
//            * String-position values are JSON-escaped (", \, controls).
//            * String-position values are also brace-encoded
//              ({ -> {, } -> }) so operator-supplied {{ doesn't
//              survive into the residual-marker scan.
//            * Raw-position values validated against strict grammar.
//            * Each placeholder replaces ALL expected occurrences;
//              mismatch -> throw.
//            * After substitution: scan for any residual {{...}};
//              throw if found.
//            * Final text MUST deserialize to the target type.
//
// Reference: docs/sessions/2026-06-14-bulk-provision-ui-phase1-v3.1-addendum.md §2
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ElpisEdgeConnect.Management.Contracts.BulkSourceMerge;

namespace ElpisEdgeConnect.Management.Api.BulkSourceMerge;

/// <summary>
/// Thrown by <see cref="TemplateSubstitutionEngine"/> to signal one of the
/// stable error codes in <see cref="BulkSourceMergeErrorCode"/>.
/// </summary>
public sealed class TemplateSubstitutionException : Exception
{
    /// <summary>Construct with a stable error code + operator-facing message.</summary>
    public TemplateSubstitutionException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>One of the stable codes in <see cref="BulkSourceMergeErrorCode"/>.</summary>
    public string ErrorCode { get; }
}

/// <summary>
/// Stateless substitution engine. Construct with a registry (per-template,
/// fixed); call <see cref="Render"/> once per CSV row.
/// </summary>
public sealed class TemplateSubstitutionEngine
{
    private static readonly Regex ResidualMarkerRegex = new(
        @"\{\{\s*[^\}]*\s*\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IReadOnlyDictionary<string, PlaceholderSpec> _registry;

    /// <summary>
    /// Build an engine for the given per-template placeholder registry. The
    /// registry is the contract -- if the template adds an unknown placeholder
    /// or drops an expected one, Render throws on first use.
    /// </summary>
    public TemplateSubstitutionEngine(IEnumerable<PlaceholderSpec> registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry.ToDictionary(s => s.Name, StringComparer.Ordinal);
        if (_registry.Count == 0)
        {
            throw new ArgumentException("Registry must contain at least one placeholder.", nameof(registry));
        }
    }

    /// <summary>
    /// Render the template by substituting <paramref name="values"/> for the
    /// configured placeholders. Per v3.1 §2: JSON-escape + brace-encode
    /// string-position values, validate raw-position values, replace ALL
    /// expected occurrences with count check, post-scan for residual markers.
    /// </summary>
    /// <param name="template">Template text containing <c>{{ name }}</c> markers.</param>
    /// <param name="values">Map from placeholder name to raw operator-supplied value.</param>
    /// <returns>Rendered text suitable for <c>JsonSerializer.Deserialize</c>.</returns>
    /// <exception cref="TemplateSubstitutionException">
    /// On count mismatch, missing value, invalid raw-position grammar, or
    /// residual marker.
    /// </exception>
    public string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);

        var rendered = new StringBuilder(template, capacity: template.Length + 256);

        foreach (var (name, spec) in _registry)
        {
            if (!values.TryGetValue(name, out var rawValue))
            {
                throw new TemplateSubstitutionException(
                    BulkSourceMergeErrorCode.TemplateSubstitutionCountMismatch,
                    $"No value supplied for placeholder '{name}'.");
            }

            var prepared = PrepareValue(name, rawValue, spec.Position);
            var marker = "{{ " + name + " }}";

            // StringBuilder.Replace returns the same instance; capture the
            // before-count so we can verify ALL expected occurrences were
            // substituted (per v3.1 §2: replace-all + count check).
            var beforeText = rendered.ToString();
            var actualCount = CountOccurrences(beforeText, marker);
            if (actualCount != spec.ExpectedOccurrences)
            {
                throw new TemplateSubstitutionException(
                    BulkSourceMergeErrorCode.TemplateSubstitutionCountMismatch,
                    $"Placeholder '{name}' expected {spec.ExpectedOccurrences} occurrences in template, found {actualCount}.");
            }

            rendered.Replace(marker, prepared);
        }

        var output = rendered.ToString();
        var residual = ResidualMarkerRegex.Match(output);
        if (residual.Success)
        {
            throw new TemplateSubstitutionException(
                BulkSourceMergeErrorCode.TemplateResidualMarker,
                $"Unknown marker remains after substitution: '{residual.Value}'. The template may declare a placeholder the registry does not know.");
        }

        return output;
    }

    private static string PrepareValue(string name, string rawValue, PlaceholderPosition position)
    {
        return position switch
        {
            PlaceholderPosition.StringValue      => EscapeForJsonStringPosition(rawValue),
            PlaceholderPosition.RawBoolean  => ValidateRawBoolean(name, rawValue),
            _ => throw new ArgumentOutOfRangeException(nameof(position), position, "Unknown placeholder position."),
        };
    }

    /// <summary>
    /// JSON-escape AND brace-encode a string-position value. Brace encoding
    /// (<c>{</c> -> <c>{</c>) prevents operator-supplied <c>{{</c> from
    /// surviving as a raw marker after substitution; after
    /// <c>JsonSerializer.Deserialize</c> the .NET string property recovers
    /// the original literal.
    /// </summary>
    private static string EscapeForJsonStringPosition(string value)
    {
        var sb = new StringBuilder(value.Length + 16);
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':  sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '/':  sb.Append('/'); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '{':  sb.Append("\\u007b"); break;
                case '}':  sb.Append("\\u007d"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    private static string ValidateRawBoolean(string name, string value)
    {
        if (value is "true" or "false")
        {
            return value;
        }
        throw new TemplateSubstitutionException(
            BulkSourceMergeErrorCode.EnabledValueInvalid,
            $"Raw-position placeholder '{name}' requires exactly 'true' or 'false', got '{value}'.");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        if (needle.Length == 0) return 0;
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
