// ============================================================================
// File: OpcUaQualityMapper.cs
// Purpose: Pure-logic translator from OPC UA StatusCode → canonical
//          DataQuality. Extracted from OpcUaTypeMapper for focused
//          testing — the StatusCode taxonomy is wide (hundreds of named
//          codes) and the Good/Bad/Uncertain split deserves its own
//          test surface.
//
// LOCKED behaviour:
//   * StatusCode.IsGood (severity bits 00) → DataQuality.Good, reason = null
//   * StatusCode.IsUncertain (severity bits 01) → DataQuality.Uncertain,
//     reason = StatusCode symbolic name (e.g. "Uncertain_LastUsableValue")
//   * StatusCode.IsBad (severity bits 10/11) → DataQuality.Bad,
//     reason = StatusCode symbolic name
//   * Severity is read via Opc.Ua.StatusCode helpers — no manual bit math
//   * Symbolic name resolved via Opc.Ua.StatusCodes.GetBrowseName which
//     returns "Good", "Uncertain", "Bad_NodeIdInvalid", etc. — the
//     operator-facing string operators see in OPC UA spec docs
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1
//            OPC UA Part 4 §7.34 (StatusCode)
// ============================================================================

using ElpisEdgeConnect.Core.Model;
using Opc.Ua;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Pure-logic StatusCode → DataQuality translator.
/// </summary>
internal static class OpcUaQualityMapper
{
    /// <summary>
    /// Translate the supplied <paramref name="statusCode"/> into the
    /// canonical <see cref="DataQuality"/> taxonomy plus an
    /// operator-readable reason string (null when quality is
    /// <see cref="DataQuality.Good"/>).
    /// </summary>
    public static (DataQuality Quality, string? Reason) Map(StatusCode statusCode)
    {
        if (StatusCode.IsGood(statusCode))
        {
            return (DataQuality.Good, null);
        }

        // SymbolicName surfaces the operator-readable spec name
        // ("Bad_NodeIdInvalid", "Uncertain_LastUsableValue", etc.) — the
        // canonical reason string downstream sinks render in alerts.
        var reason = StatusCode.LookupSymbolicId(statusCode.CodeBits);

        if (StatusCode.IsUncertain(statusCode))
        {
            return (DataQuality.Uncertain, NormaliseReason(reason, "Uncertain"));
        }

        // IsBad covers both Bad-severity (10) and the reserved 11
        // severity which the spec treats as Bad.
        return (DataQuality.Bad, NormaliseReason(reason, "Bad"));
    }

    /// <summary>
    /// Guard against empty / null symbolic names — happens when the
    /// supplied StatusCode is a non-spec custom code (e.g. vendor
    /// extension). Falls back to the severity-class string so the
    /// operator never sees a blank reason.
    /// </summary>
    private static string NormaliseReason(string? symbolicName, string fallback) =>
        string.IsNullOrWhiteSpace(symbolicName) ? fallback : symbolicName;
}
