# Bulk-Provision UI Phase 1 — FOCAS2 ↔ MTConnect current production baseline parity

**Date:** 2026-06-14
**Status:** **CURRENT CUSTOMER PRODUCTION BASELINE** — anchored on the operator's live `gateway.json` config.
**Per v3 §6 / v3.1 §5.**

---

## Wording correction

The original v3 plan referred to a "64-tag parity artifact." That figure was a rough estimate from the customer's earlier description. After grounding against the operator's live config:

```text
Current Phase 1 collection baseline:
    10 canonical EdgeConnect dataPoint paths (per FOCAS2 / MTConnect template)

EREMOS V2 receiver schema:
    65 nullable C# slot properties (exact count from the customer's class
    paste, 2026-06-14)

Relationship:
    The 10 dataPoint paths populate a SUBSET of the EREMOS V2 schema today.
    The remaining nullable slots are forward-compatibility headroom for
    Tooling enablement, expanded servo/spindle diagnostics, fan/battery
    status, and other collector groups that will be enabled per-CNC in
    Phase 2.
```

The filename retains `64-tag-parity` for stable cross-doc references; this artifact's CONTENT reflects the corrected wording.

---

## The 10 canonical dataPoint paths (Phase 1 baseline)

Anchored on the operator's current production `gateway.json` `Connection.dataPoints`:

```
Program/MainProgram
Status/RunState
Axes/Position/Absolute
Axes/Position/Machine
Axes/FeedRate
Spindle/Speed
Spindle/Load
Alarms/Active
CycleTime
PartsCount
```

These 10 paths ship in **both** `template-fanuc-v1.json` and `template-mtconnect-v1.json`. They are identical strings because both adapters emit the same canonical path namespace.

> **Note on `Status/RunState`:** the customer's original config had `Program/RunningStatus`. That exact path is not emitted by either the FOCAS2 or MTConnect adapter. After clarification (2026-06-14), the operator's intent was the execution state (RUNNING / STOPPED / INTERRUPTED / READY), which is emitted at `Status/RunState` by both adapters. The template uses `Status/RunState`.

> **Note on `Axes/Position/Absolute` and `Axes/Position/Machine`:** these are kept verbatim from the operator's config. The FOCAS2 adapter emits per-axis paths (`Axes/X/Absolute`, `Axes/Y/Absolute`, etc.), and gates the per-axis emit loop on `HasAnyDataPoint("Axes/")` (prefix match — see `Focas2SourceAdapter.cs:546`). So the literal `Axes/Position/Absolute` and `Axes/Position/Machine` strings DO activate the axis collection bucket via the `Axes/` prefix. The per-axis sub-filter (e.g., "emit only X absolute") is not a current adapter feature; it's Phase 2 territory if the customer ever needs it. Operational verification of axis-level filtering is open per the customer's 2026-06-14 acknowledgment.

---

## FOCAS2 ↔ MTConnect adapter parity (code-verified)

`MTConnectSemanticMap.cs` lines 1-7 declares the contract explicitly:

> *"THE single source of truth linking each canonical CNC tag to the MTConnect dataItem it is sourced from... Tag names deliberately mirror the FOCAS2 adapter's names where the semantic overlaps... so a downstream consumer can treat a 'Status/RunState' MQTT topic identically regardless of which CNC protocol produced it."*

Grounded by-path verification (against `Focas2TagMap.cs` and `MTConnectTagMap.cs` / `MTConnectSemanticMap.cs`):

| Canonical dataPoint        | FOCAS2 emit | MTConnect emit | EREMOS V2 receiver slot (best fit) |
|----------------------------|-------------|----------------|-------------------------------------|
| `Program/MainProgram`      | ✓ (TagMap)  | ✓ (TagMap `MainProgram`) | `MainProgram_path1_CNC` |
| `Status/RunState`          | ✓ (TagMap)  | ✓ (TagMap `RunState`, from MTConnect `Execution`) | `CncState_path1_CNC` (likely; needs EREMOS map confirmation) |
| `Axes/Position/Absolute`   | activates `Axes/` bucket; emits `Axes/X/Absolute`, `Axes/Y/Absolute`, `Axes/Z/Absolute` | activates `Axes/` bucket; same per-axis emit | per-axis slots if EREMOS has them; otherwise schema gap |
| `Axes/Position/Machine`    | same as above for `/Machine` | same | same |
| `Axes/FeedRate`            | ✓ (TagMap, exact) | ✓ (TagMap, exact, via MTConnect `PathFeedrate`) | feed-rate slot (TBC against EREMOS map) |
| `Spindle/Speed`            | ✓ (TagMap)  | ✓ (TagMap `SpindleSpeed`, from MTConnect `SpindleSpeed` / `RotaryVelocity`) | spindle-speed slot (TBC) |
| `Spindle/Load`             | ✓ (TagMap)  | ✓ (TagMap `SpindleLoad`, from MTConnect `SpindleLoad` / `Load`) | spindle-load slot (TBC) |
| `Alarms/Active`            | ✓ (Focas2 adapter line 560 + TagMap line 146) | indirect via MTConnect Condition stream (`AlarmTags` aggregate per semantic map line 96-99) | `CncWarning_path1_CNC` (likely) |
| `CycleTime`                | ✓ (Focas2 adapter line 577 with `Production/CycleTime` alias) | ✓ (TagMap, from MTConnect `CycleTime` / `ProcessTimer`) | cycle-time slot (TBC) |
| `PartsCount`               | ✓ (Focas2 adapter line 581 with `Production/PartsCount` alias) | ✓ (TagMap, from MTConnect `PartCount`) | `PartsCount` (direct match) |

**EREMOS V2 receiver slot column has TBC ("to be confirmed") entries** — the C# class the operator pasted has explicit `MainProgram_path1_CNC`, `CncState_path1_CNC`, `CncWarning_path1_CNC`, and `PartsCount` slots, but the per-axis, feed-rate, spindle-speed, spindle-load, and cycle-time slots aren't named in the snippet. The mapping table is provisional pending operator-side EREMOS schema confirmation. Bulk-provision works regardless; EREMOS-side schema gaps surface as null receiver properties on the consuming side, not as bulk-provision failures.

---

## EREMOS V2 receiver schema (65 slots, summary)

The full receiver class the operator pasted (2026-06-14) totals **65 nullable C# properties**:

| Group | Count | Notes |
|---|---|---|
| Top-level `PartsCount` | 1 | direct slot |
| Base CNC state, program, signal lines (`_path1_CNC` suffix) | ~18 | CncState, Mode, MainProgram, ActProgram, MainComment, ActComment, SigCUT, SigSBK, SigDM00, SigDM01, SigMDRN, CncWarning, CncFan1-4Status, Disconnect_CNC, PartsNum |
| Servo set 0 — batteries / temps / fan / amp / com-power statuses | 17 | `_0_path1_CNC` suffix |
| Servo set 1 — same 17 properties for the second servo | 17 | `_1_path1_CNC` suffix |
| Spindle diagnostics — temps, total revolutions, fan / amp / com-power statuses | 12 | `_0_path1_CNC` suffix on spindle properties |
| **Total** | **65** | All nullable; populated only when the gateway publishes the matching canonical tag. |

The 10 Phase 1 dataPoints populate a **subset** of these 65 slots — roughly the 4 base-CNC slots (MainProgram, PartsCount/PartsNum, CncWarning-ish via Alarms/Active, and CncState-ish via Status/RunState) plus the spindle-speed and spindle-load slots if the receiver has them under matching names. The remaining ~55+ slots remain null until Phase 2 enables additional collectors per-CNC.

---

## Brother HTTP — keeps current template baseline (DataPoints configurable but customer doesn't set one)

ChatGPT's pass assumed Brother might be adapter-fixed. **Verified false** — `BrotherHttpSourceConfiguration.cs:87` declares:

```csharp
public IReadOnlyList<string> DataPoints { get; init; } = Array.Empty<string>();
```

Plus a `NormalizeDataPoints` helper at line 159 (per v3.1 §B.6). Brother IS configurable.

**However**, the customer's current Brother config (operator pasted 2026-06-14):

```json
"DataSourceType": "BrotherHttp",
"BrotherHttp": { "BaseUrl": "http://192.168.2.110" },
"PollIntervalMs": 5000,
"Enabled": false,
"Tags": [ "brother", "speedio", "bay-1" ]
```

does NOT set a `DataPoints` array. So:

- `template-brother-v1.json` Phase 1 does **not** add a hardcoded `dataPoints` baseline.
- Operators wanting per-source Brother dataPoint customization add it post-generation in the Studio source-edit wizard.
- If a future Brother customer baseline emerges, a `template-brother-v2.json` can ship the customer-anchored list, side-by-side with v1.

This decision is documented in `templates/MANIFEST.md` under the Static-field invariants table.

---

## Modbus TCP — out of scope for this artifact

Modbus per-tag definitions are operator-driven, not template-driven. The chip-3 `template-modbus-v1.json` ships `Connection.tags = []` and points at the separate `tools/ModbusCsvImport` workflow for per-tag definitions. The parity artifact does not cover Modbus.

---

## Open items (for operator follow-up; not blocking)

1. **EREMOS V2 receiver schema mapping for per-axis, feed-rate, spindle-speed, spindle-load, cycle-time slots** — the C# class paste covered base CNC + servo + spindle-diagnostic slots but didn't enumerate axis or sensor receiver properties. Confirming the receiver-side names lets the parity artifact's mapping table replace TBC entries with real names.
2. **`Axes/Position/Absolute` and `Axes/Position/Machine`** — the operator hasn't yet confirmed whether these paths produce per-axis tag flows on their actual production gateway. The Phase 1 template ships them verbatim because (a) the adapter's prefix-match logic activates the right collection bucket and (b) operational mismatch surfaces as null receiver-side properties, not as bulk-provision failures.
3. **Brother per-source DataPoints baseline** — if a future Brother customer baseline emerges, a `template-brother-v2.json` ships the list. Phase 1 keeps v1 with no hardcoded list.

None of these block Phase 1's customer rollout.

---

## When this artifact gets refreshed

- Operator's EREMOS V2 mapping confirmation arrives → TBC entries get real slot names.
- A future template version (`-v2.json`) ships → new artifact section documents the changed baseline.
- Phase 2 enables Tooling / additional collectors → new section documents the expanded baseline.
