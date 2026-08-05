// ============================================================================
// File: Profiles/MelsecProfiles.cs
// Purpose: Data-driven MELSEC profile registry (A-2 Gate A-2I) — the first code
//          artifact of the general profile-matrix strategy (ADR-0034). Profiles
//          are STATIC TYPED RECORDS, not scattered conditionals: adding a family
//          is "fill a record + tests", not codec surgery.
//
//          Entries: Modern (iQ-R/Q/L — the shipped Slice-1 pin, values sourced
//          from the Phase A-1 audit) and IqF (FX5 — from the Gate A-2D audit).
//          The iQ-F profile is INTERNAL/TESTABLE ONLY: runtime configuration
//          keeps resolving to Modern (the adapter validation-rejects every
//          non-Modern DeviceProfile), and the UI profile selector is a
//          separately-gated deliverable (Gate A-2O). NOTHING here alters the
//          shipped Modern behavior — byte-identity is pinned by tests.
//
//          Wire shape is IDENTICAL across both profiles (3E binary, subcommand
//          0000, 1-byte device codes, 3-byte LE heads, 960-word cap) per the
//          A-2D audit; the iQ-F deltas are pure data: X/Y operator-address
//          radix = octal, and ZR excluded from the device set.
// Reference: docs/sessions/2026-07-03-melsec-a2d-fx5-audit.md
//            docs/decisions/0034-melsec-profile-matrix-strategy.md
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using ElpisEdgeConnect.Sources.Melsec.Wire;

namespace ElpisEdgeConnect.Sources.Melsec.Profiles;

/// <summary>
/// One MELSEC family profile: the complete, manual-sourced data bundle that
/// parameterizes address parsing, planning limits, and provenance for a PLC
/// family. Wire-shape fields document the frame this profile rides on; Slice 1
/// implements exactly one shape (3E binary / subcommand 0000).
/// </summary>
public sealed record MelsecProfileDefinition
{
    // ─── Identity / envelope ─────────────────────────────────────────────
    /// <summary>Registry key — the config-level <see cref="MelsecDeviceProfile"/> value.</summary>
    public required MelsecDeviceProfile Key { get; init; }

    /// <summary>Operator-readable profile name (e.g. <c>"Modern (iQ-R/Q/L)"</c>).</summary>
    public required string DisplayName { get; init; }

    /// <summary>PLC family covered (e.g. <c>"iQ-R / iQ-L / Q / L"</c>).</summary>
    public required string Family { get; init; }

    /// <summary>CPU/module model families covered (e.g. <c>"FX5S / FX5UJ / FX5U / FX5UC (built-in Ethernet)"</c>).</summary>
    public required string ModelFamilies { get; init; }

    /// <summary>Frame mode this profile rides on (Slice 1: <see cref="MelsecFrameMode.Mc3EBinary"/>).</summary>
    public required MelsecFrameMode FrameMode { get; init; }

    /// <summary>Transport (Slice 1: <see cref="MelsecTransportProtocol.Tcp"/>).</summary>
    public required MelsecTransportProtocol Transport { get; init; }

    /// <summary>Default 3E route header for a directly connected CPU.</summary>
    public required Slmp3ERoute RouteDefaults { get; init; }

    // ─── Wire shape (documented per profile; identical across Slice-1 profiles) ──
    /// <summary>Device-code field width in bytes (3E binary 2-digit-code form: 1).</summary>
    public required int DeviceCodeWidthBytes { get; init; }

    /// <summary>Head-device-number field width in bytes (3E binary 6-digit-number form: 3).</summary>
    public required int HeadDeviceFieldWidthBytes { get; init; }

    /// <summary>Commands this profile supports (documentation of scope, e.g. <c>"0401/0000 batch read (word units)"</c>).</summary>
    public required string SupportedCommands { get; init; }

    // ─── Devices & limits ────────────────────────────────────────────────
    /// <summary>Supported devices by symbol, with per-profile radix.</summary>
    public required IReadOnlyDictionary<string, MelsecDeviceDescriptor> Devices { get; init; }

    /// <summary>Comma-separated supported symbols, for operator-facing error messages.</summary>
    public required string SupportedList { get; init; }

    /// <summary>Max word points per 0401/0000 batch read.</summary>
    public required int MaxWordsPerBatchRead { get; init; }

    /// <summary>Bit-device points packed per returned word (word-units bit read).</summary>
    public required int BitPointsPerWord { get; init; }

    /// <summary>True when the family requires 16-multiple bit-head alignment (A-series only; false for Slice-1 profiles).</summary>
    public required bool RequiresBitHeadAlignment { get; init; }

    /// <summary>Default word order for 32-bit values (per-tag overridable).</summary>
    public required MelsecWordOrder DefaultWordOrder { get; init; }

    // ─── Gating & provenance ─────────────────────────────────────────────
    /// <summary>True only when operators may select this profile in the Studio.
    /// The iQ-F profile stays false until Gate A-2O (profile selector) is
    /// separately approved and implemented.</summary>
    public required bool IsOperatorSelectable { get; init; }

    /// <summary>Pinned manual provenance — document number + revision + date per source.</summary>
    public required IReadOnlyList<string> ManualProvenance { get; init; }

    /// <summary>Evidence links (audit docs; later: capture records).</summary>
    public required IReadOnlyList<string> EvidenceLinks { get; init; }

    /// <summary>Resolve a device descriptor by symbol within this profile.</summary>
    public bool TryGetDevice(string symbol, [MaybeNullWhen(false)] out MelsecDeviceDescriptor descriptor) =>
        Devices.TryGetValue(symbol, out descriptor);
}

/// <summary>Static registry of MELSEC family profiles (profiles-as-data, ADR-0034).</summary>
public static class MelsecProfiles
{
    private static MelsecDeviceDescriptor ModernDevice(string symbol)
    {
        if (!MelsecDevices.TryGet(symbol, out var d))
        {
            throw new InvalidOperationException($"Slice-1 device '{symbol}' missing from MelsecDevices.");
        }
        return d;
    }

    /// <summary>
    /// Modern (iQ-R / iQ-L / Q / L) — the shipped Slice-1 pin. Every value here
    /// mirrors the shipped constants; the registry test suite fails if this entry
    /// ever drifts from <see cref="MelsecDevices"/> / <see cref="SlmpFrameCodec"/>.
    /// </summary>
    public static MelsecProfileDefinition Modern { get; } = new()
    {
        Key = MelsecDeviceProfile.Modern,
        DisplayName = "Modern (iQ-R/Q/L)",
        Family = "MELSEC iQ-R / iQ-L / Q / L",
        ModelFamilies = "iQ-R, iQ-L, Q, L series modules (E71 / built-in Ethernet)",
        FrameMode = MelsecFrameMode.Mc3EBinary,
        Transport = MelsecTransportProtocol.Tcp,
        RouteDefaults = Slmp3ERoute.LocalCpu,
        DeviceCodeWidthBytes = 1,
        HeadDeviceFieldWidthBytes = 3,
        SupportedCommands = "0401/0000 batch read (word units), read-only",
        Devices = new Dictionary<string, MelsecDeviceDescriptor>(StringComparer.Ordinal)
        {
            ["D"] = ModernDevice("D"),
            ["W"] = ModernDevice("W"),
            ["R"] = ModernDevice("R"),
            ["ZR"] = ModernDevice("ZR"),
            ["M"] = ModernDevice("M"),
            ["X"] = ModernDevice("X"),
            ["Y"] = ModernDevice("Y"),
            ["B"] = ModernDevice("B"),
            // A-3a special devices (audit: 2026-07-03-melsec-a3a-audit.md).
            ["SM"] = ModernDevice("SM"),
            ["SD"] = ModernDevice("SD"),
            ["SB"] = ModernDevice("SB"),
            ["SW"] = ModernDevice("SW"),
            // A-3b timers/counters (audit 2026-07-03-melsec-a3b0-…; both profiles).
            ["TS"] = ModernDevice("TS"),
            ["TC"] = ModernDevice("TC"),
            ["TN"] = ModernDevice("TN"),
            ["STS"] = ModernDevice("STS"),
            ["STC"] = ModernDevice("STC"),
            ["STN"] = ModernDevice("STN"),
            ["CS"] = ModernDevice("CS"),
            ["CC"] = ModernDevice("CC"),
            ["CN"] = ModernDevice("CN"),
        },
        SupportedList = MelsecDevices.SupportedList,
        MaxWordsPerBatchRead = SlmpFrameCodec.MaxWordPoints,
        BitPointsPerWord = 16,
        RequiresBitHeadAlignment = false,
        DefaultWordOrder = MelsecWordOrder.LowWordFirst,
        IsOperatorSelectable = true,
        ManualProvenance = new[]
        {
            "SH(NA)-080008-AB (May 2022) — MELSEC Communication Protocol Reference Manual",
            "SH(NA)-080956ENG-N (October 2025) — SLMP Reference Manual",
        },
        EvidenceLinks = new[]
        {
            "docs/sessions/2026-07-03-melsec-phase-a1-audit.md",
        },
    };

    /// <summary>
    /// iQ-F / FX5 (built-in Ethernet) — INTERNAL/TESTABLE ONLY (Gate A-2I).
    /// Same 3E-binary wire shape as Modern per the A-2D audit; deltas are pure
    /// data: X/Y operator addresses are OCTAL labels (binary wire stays numeric),
    /// and ZR is not accessible on the FX5 CPU.
    /// </summary>
    public static MelsecProfileDefinition IqF { get; } = new()
    {
        Key = MelsecDeviceProfile.IqF,
        DisplayName = "iQ-F / FX5",
        Family = "MELSEC iQ-F",
        ModelFamilies = "FX5S, FX5UJ, FX5U, FX5UC (CPU built-in Ethernet)",
        FrameMode = MelsecFrameMode.Mc3EBinary,
        Transport = MelsecTransportProtocol.Tcp,
        RouteDefaults = Slmp3ERoute.LocalCpu,
        DeviceCodeWidthBytes = 1,
        HeadDeviceFieldWidthBytes = 3,
        SupportedCommands = "0401/0000 batch read (word units), read-only",
        Devices = new Dictionary<string, MelsecDeviceDescriptor>(StringComparer.Ordinal)
        {
            ["D"] = ModernDevice("D"),
            ["W"] = ModernDevice("W"),
            ["R"] = ModernDevice("R"),
            ["M"] = ModernDevice("M"),
            // FX5 X/Y: operator labels are OCTAL (0-1777); the binary head field
            // carries the numeric value ([COM] §38.2 footnote: "Binary code:
            // Hexadecimal"). Same wire code bytes as Modern.
            ["X"] = new() { Symbol = "X", Code = MelsecDeviceCode.X, Radix = 8, Kind = MelsecDeviceKind.Bit },
            ["Y"] = new() { Symbol = "Y", Code = MelsecDeviceCode.Y, Radix = 8, Kind = MelsecDeviceKind.Bit },
            ["B"] = ModernDevice("B"),
            // A-3a special devices — present on FX5 per [COM] accessible list
            // (SM 0-9999, SD 0-11999, SB/SW 0-7FFF; a3a audit).
            ["SM"] = ModernDevice("SM"),
            ["SD"] = ModernDevice("SD"),
            ["SB"] = ModernDevice("SB"),
            ["SW"] = ModernDevice("SW"),
            // A-3b timers/counters — present on FX5 per [COM] SLMP device table
            // (incl. retentive ST*); same codes as Modern.
            ["TS"] = ModernDevice("TS"),
            ["TC"] = ModernDevice("TC"),
            ["TN"] = ModernDevice("TN"),
            ["STS"] = ModernDevice("STS"),
            ["STC"] = ModernDevice("STC"),
            ["STN"] = ModernDevice("STN"),
            ["CS"] = ModernDevice("CS"),
            ["CC"] = ModernDevice("CC"),
            ["CN"] = ModernDevice("CN"),
            // ZR deliberately absent: not accessible on the FX5 CPU (A-2D audit).
        },
        SupportedList = "D, W, R, M, X, Y, B, SM, SD, SB, SW, TS, TC, TN, STS, STC, STN, CS, CC, CN",
        MaxWordsPerBatchRead = SlmpFrameCodec.MaxWordPoints, // 960 — confirmed for FX5 in SH(NA)-082625ENG-J §38.1 + SLMP processing table
        BitPointsPerWord = 16,
        RequiresBitHeadAlignment = false,
        DefaultWordOrder = MelsecWordOrder.LowWordFirst, // pending explicit golden-vector confirmation (A-2D open item)
        IsOperatorSelectable = true, // Gate A-2O flip (final operator-support commit): wizard tiles, probe, and diagnostics are wired
        ManualProvenance = new[]
        {
            "SH(NA)-082625ENG-J (April 2026) — MELSEC iQ-F FX5 User's Manual (Communication) — AUTHORITATIVE for CPU built-in Ethernet",
            "JY997D60801-G (April 2022) — FX5 User's Manual (MELSEC Communication Protocol) — cross-check",
            "JY997D56201-U (April 2023) — FX5 User's Manual (Ethernet Communication) — cross-check",
            "SH(NA)-080956ENG-N (October 2025) — SLMP Reference Manual — cross-check",
        },
        EvidenceLinks = new[]
        {
            "docs/sessions/2026-07-03-melsec-a2d-fx5-audit.md",
        },
    };

    /// <summary>All registered profiles.</summary>
    public static IReadOnlyList<MelsecProfileDefinition> All { get; } = new[] { Modern, IqF };

    /// <summary>
    /// Resolve a profile definition for a config-level profile value. Returns
    /// false for families with no registry entry yet (QnA, ACpu).
    /// </summary>
    public static bool TryResolve(MelsecDeviceProfile key, [MaybeNullWhen(false)] out MelsecProfileDefinition profile)
    {
        profile = All.FirstOrDefault(p => p.Key == key);
        return profile is not null;
    }
}
