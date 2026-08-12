// ============================================================================
// File: S7TagCompatibility.cs
// Purpose: Single source of truth for the datatype/address-width
//          compatibility rules an S7 tag must satisfy. The S7 scan planner
//          (S7ScanPlanner.ValidateAgainstAddress) throws at adapter
//          Initialize for two deterministic combinations:
//             1. a bit-form address (DBX / dot-bit) with a non-Bool datatype;
//             2. a datatype wider than the address-width hint (e.g. Real on
//                a DBW word address).
//          Those two are Save-BLOCKING errors in the source wizard — a config
//          the wizard accepts must never fail adapter Initialize for a
//          deterministic compatibility reason (M.2b.2 v2 decision).
//
//          A datatype NARROWER than the address width (e.g. Bool on a DBW)
//          is accepted by the planner, so it is a non-blocking WARNING here:
//          surfaced for operator confirmation, never blocks Save.
//
//          Pure (no IO, no library deps) so it is unit-testable in isolation
//          and so a parity test can assert it agrees with the planner across
//          the full address/datatype matrix.
// Reference: docs/sessions/2026-06-02-s7-source-wizard-plan-v2.md
//            (validation rules; M2 backend glue)
// ============================================================================

using System;

namespace ElpisEdgeConnect.Sources.S7;

/// <summary>
/// Severity of a tag's datatype/address-width compatibility verdict.
/// </summary>
public enum S7CompatibilitySeverity
{
    /// <summary>Datatype and address width are an exact, sensible match.</summary>
    Compatible = 0,

    /// <summary>
    /// Planner-accepted but suspicious (datatype narrower than the address
    /// width). Surfaced for operator confirmation; does NOT block Save.
    /// </summary>
    Warning = 1,

    /// <summary>
    /// Planner-rejected. The adapter throws at Initialize for this
    /// combination, so the wizard must block Save.
    /// </summary>
    Error = 2,
}

/// <summary>
/// Verdict for one tag's datatype/address-width compatibility. Carries an
/// operator-facing message and a machine-readable code for the two
/// blocking cases and the one warning case.
/// </summary>
public readonly record struct S7CompatibilityVerdict(
    S7CompatibilitySeverity Severity,
    string? Code,
    string? Message)
{
    /// <summary>Error code: bit-form address requires the Bool datatype.</summary>
    public const string BitRequiresBoolCode = "S7.DATATYPE_BIT_REQUIRES_BOOL";

    /// <summary>Error code: datatype is wider than the address-width hint.</summary>
    public const string DatatypeTooWideCode = "S7.DATATYPE_TOO_WIDE_FOR_ADDRESS";

    /// <summary>Warning code: datatype is narrower than the address-width hint.</summary>
    public const string DatatypeNarrowerCode = "S7.DATATYPE_NARROWER_THAN_ADDRESS";

    /// <summary>True when this verdict must block Save (planner would reject).</summary>
    public bool BlocksSave => Severity == S7CompatibilitySeverity.Error;

    /// <summary>A clean, compatible verdict with no message.</summary>
    public static S7CompatibilityVerdict Ok { get; } =
        new(S7CompatibilitySeverity.Compatible, null, null);
}

/// <summary>
/// Pure checker for S7 datatype/address-width compatibility. Mirrors the
/// rules in <c>S7ScanPlanner.ValidateAgainstAddress</c> exactly (pinned by a
/// parity test) and is the shared rule used by the source wizard's live
/// validation, Save validation, and Test-selected-read enablement.
/// </summary>
public static class S7TagCompatibility
{
    /// <summary>
    /// Evaluate a parsed <paramref name="address"/> against a resolved
    /// datatype <paramref name="spec"/>. Returns a verdict whose
    /// <see cref="S7CompatibilityVerdict.Severity"/> is
    /// <see cref="S7CompatibilitySeverity.Error"/> for exactly the
    /// combinations the planner rejects, <see cref="S7CompatibilitySeverity.Warning"/>
    /// for a narrower-than-width datatype, and
    /// <see cref="S7CompatibilitySeverity.Compatible"/> otherwise.
    /// </summary>
    public static S7CompatibilityVerdict Check(S7Address address, S7DatatypeSpec spec)
    {
        // Rule 1 — bit-form addresses (DBX / dot-bit) read a single bit, so
        // only the Bool datatype is meaningful. Anything else is rejected
        // by the planner.
        if (address.WidthHint == S7AddressWidthHint.Bit)
        {
            if (spec.Datatype != S7Datatype.Bool)
            {
                return new S7CompatibilityVerdict(
                    S7CompatibilitySeverity.Error,
                    S7CompatibilityVerdict.BitRequiresBoolCode,
                    "Bit addresses can only be read as Bool. Change the datatype to Bool, " +
                    "or use a byte, word, or dword address.");
            }
            return S7CompatibilityVerdict.Ok;
        }

        // Rule 2 — width-prefixed addresses (DBB/DBW/DBD and B/W/D process
        // forms) carry an explicit byte width. Strings declare their own
        // length and ignore the address width, so they are exempt (the
        // planner allows them).
        if (address.WidthHint is S7AddressWidthHint.Byte
            or S7AddressWidthHint.Word
            or S7AddressWidthHint.DWord)
        {
            if (spec.Datatype == S7Datatype.String)
            {
                return S7CompatibilityVerdict.Ok;
            }

            var hintBytes = address.WidthHint switch
            {
                S7AddressWidthHint.Byte => 1,
                S7AddressWidthHint.Word => 2,
                S7AddressWidthHint.DWord => 4,
                _ => 0,
            };

            if (spec.ByteCount > hintBytes)
            {
                return new S7CompatibilityVerdict(
                    S7CompatibilitySeverity.Error,
                    S7CompatibilityVerdict.DatatypeTooWideCode,
                    $"{spec.Datatype} needs {spec.ByteCount} bytes, but {address} is a " +
                    $"{hintBytes}-byte {WidthWord(address.WidthHint)} address. Choose a " +
                    $"{hintBytes}-byte datatype, or widen the address (for example a dword " +
                    "address such as DB1.DBD0).");
            }

            if (spec.ByteCount < hintBytes)
            {
                return new S7CompatibilityVerdict(
                    S7CompatibilitySeverity.Warning,
                    S7CompatibilityVerdict.DatatypeNarrowerCode,
                    $"{spec.Datatype} is narrower than the {WidthWord(address.WidthHint)} " +
                    $"address {address}. Confirm this is intentional.");
            }

            return S7CompatibilityVerdict.Ok;
        }

        // Unspecified width (only reachable for non-width-bearing forms the
        // planner derives from datatype) — nothing to check here.
        return S7CompatibilityVerdict.Ok;
    }

    private static string WidthWord(S7AddressWidthHint hint) => hint switch
    {
        S7AddressWidthHint.Byte => "byte",
        S7AddressWidthHint.Word => "word",
        S7AddressWidthHint.DWord => "dword",
        _ => "byte",
    };
}
