# MTConnect source wizard — implementation plan v2 (M.2b.4) — LOCKED

**Date:** 2026-05-31
**Supersedes:** plan v1 + its review. Builds on the **premise-correction** note
(`2026-05-31-mtconnect-source-wizard-premise-correction.md`) and a second ChatGPT
review on the corrected basis. All decisions below are **locked**; next artifact is
the **M1 static HTML mockup** (sign-off gate) — no Blazor wiring until that's signed.

## 0. The model (locked)

**MTConnect wizard = FOCAS2-style semantic onboarding, NOT OPC-UA-style arbitrary
browse/select.** The wizard exposes the adapter's **fixed CNC semantic tag map**,
enriched by `/probe` only to make that fixed map accurate for the connected agent.
The operator never picks/renames/tunes arbitrary dataItems.

```
Agent URL → Test /probe → discover machine shape → show fixed semantic tags
          → grey out unavailable semantics → review → save source (+ optional route)
```

Template to mirror (already shipped): `AddFocas2Source.razor` + `Focas2BrowseService`
+ `POST /api/v1/sources/browse/focas2`.

## 1. Locked decisions

| Topic | Decision |
|---|---|
| Wizard model | FOCAS2-style fixed semantic tags |
| Parser/discovery location | **Adapter** (`Sources.MTConnect`) |
| API / DTO / UI | **Management** (references the adapter, as it already does for Modbus/OPC UA) |
| `/probe` parser scope | **Bounded**: semantic-tag availability + axis discovery. NOT a general dataItem tree, NO arbitrary selection |
| Axis discovery | `Linear`/`Rotary` components first; identity = component `name` → `nativeName` → `id` → (fallback) parent component of `Position` dataItems. **Cap = 12**. Only show axis tags the adapter can actually emit |
| Unavailable tags | **Grey out with a reason** (never hide) — "explain, don't hide" |
| Reachable-but-unrecognised | **Distinct status** from unreachable |
| Probe budget | **Fixed 10 s**, independent of runtime `TimeoutSeconds` (Advanced override later if needed) |
| Conditions | Auto-mapped to `alarms/count` + `alarms/first_fault`; shown as an informational note, **not selectable** |
| Demo mode | **Follow-up (M6), not a blocker** |
| Review summary | **Keep** a short pre-save summary |
| Tile flip | **Last step only**, after end-to-end verification. S7 stays Pending |
| Mockup | **First**, sign-off gate |

## 2. The shared semantic mapping (load-bearing guardrail)

Today the stream parser encodes element-name → canonical-tag **inline**
(`TryGet(flat, "Execution", …) → MTConnectTagMap.RunState`, etc.), with a few key
arrays (`SpindleSpeedKeys = ["SpindleSpeed","RotaryVelocity"]`, `FeedRateKeys`,
`PartCountKeys`). The probe availability checker **must reuse the same mapping** —
otherwise the wizard could claim a tag is available that the runtime never emits.

**M2 therefore extracts a single shared table** in `Sources.MTConnect`, e.g.:

```csharp
// One source of truth, consumed by BOTH the stream parser and the probe checker.
internal sealed record MTConnectSemanticMapping
{
    public required MTConnectTagMapEntry Tag { get; init; }      // canonical tag
    public required IReadOnlyList<string> SourceDataItemTypes { get; init; } // MTConnect element/type names
}

internal static class MTConnectSemanticMap
{
    // RunState ← Execution; ControllerMode ← ControllerMode; EmergencyStop ← EmergencyStop;
    // MainProgram/RunningProgram ← Program; SpindleSpeed ← SpindleSpeed|RotaryVelocity;
    // SpindleLoad ← Load(spindle); FeedRate ← PathFeedrate; PartsCount ← PartCount;
    // CycleTime ← (cycle-time source); AlarmCount/FirstFault ← Condition (Fault/Normal).
    public static readonly IReadOnlyList<MTConnectSemanticMapping> All = [ … ];
}
```

The stream parser is refactored to source its element-name knowledge from this table
(behaviour-preserving; its existing tests must stay green). A **parity test** asserts
every canonical tag the stream parser can emit has a mapping entry (and vice versa).

## 3. DTOs (Management-owned wire shapes)

```csharp
public enum MTConnectBrowseStatus
{
    ReachableWithRecognisedTags,  // /probe ok AND ≥1 known semantic source present
    ReachableNoRecognisedTags,    // /probe ok but none of our mappings present
    Unreachable,                  // connect failure / DNS / refused
    Timeout,                      // exceeded the 10 s budget
    Unauthorized,                 // 401/403
    InvalidProbeDocument,         // malformed XML / not MTConnectDevices
    UnsupportedAgent,             // valid-ish MTConnect, missing required structure
}

public sealed record MTConnectSemanticTagAvailability
{
    public required string CanonicalTag { get; init; }   // e.g. "spindle/load"
    public required bool   Available    { get; init; }
    public string? Reason { get; init; }                 // why unavailable (when !Available)
    public string? SourceDataItemType { get; init; }     // e.g. "Load"
    public string? SourceDataItemId { get; init; }       // matched dataItem id, when found
}

public sealed record MTConnectBrowseResult
{
    public required MTConnectBrowseStatus Status { get; init; }
    public string? DeviceName { get; init; }
    public string? DeviceUuid { get; init; }
    public string? Manufacturer { get; init; }
    public IReadOnlyList<MTConnectSemanticTagAvailability> Tags { get; init; } = [];
    public IReadOnlyList<string> DiscoveredAxes { get; init; } = []; // e.g. ["X","Y","Z","A"]
    public string? Message { get; init; }                // operator-readable detail
}
```

Status → HTTP mapping mirrors FOCAS2/OPC UA (reachable → 200; unreachable/timeout →
appropriate 4xx/5xx; unauthorized → 401/403; invalid/unsupported → 422/200-with-status).

## 4. UI copy rules (locked)

`/probe` proves **capability**, not live flow. Wording:

| Don't say | Do say |
|---|---|
| Receiving / Active | **Exposed by agent** / **Available in /probe** |
| Missing | **Not exposed by this agent** |

Review summary example:
```
Agent reachable · Device: Mazak VCN-530C
Standard tags: 12 · Available: 9 · Unavailable: 3
Axes discovered: X, Y, Z, A
Conditions: mapped automatically to alarms/count and alarms/first_fault
```

## 5. Work breakdown

- **M1 — Static HTML mockup (sign-off gate).**
  `docs/sessions/2026-05-30-ux-mockups/7-mtconnect-source-wizard.html` (shared
  `_styles.css`). Must show: connect step; **happy path** (recognised tags, some
  greyed with reasons, axes discovered); and the error/edge states —
  **agent unreachable**, **invalid /probe**, **reachable but no recognised tags**,
  **recognised with some unavailable**, **no axis components / all axes unavailable**;
  the conditions note; and the pre-save **review summary**. **Pause for sign-off.**

- **M2 — Browse backend.**
  (a) Extract `MTConnectSemanticMap` shared table + refactor stream parser to use it
      (behaviour-preserving) + parity test.
  (b) Bounded `/probe` parser (adapter): device + manufacturer; per-canonical-tag
      availability via the shared map; axis discovery (Linear/Rotary, cap 12).
  (c) `MTConnectBrowseService` (Management) ≈ `Focas2BrowseService`: real reachability
      probe (HTTP GET `/probe`, **fixed 10 s** budget) → parse → `MTConnectBrowseResult`.
  (d) `POST /api/v1/sources/browse/mtconnect` + status→HTTP mapping.
  Tests: parser over captured `/probe` fixtures (multi-axis, rotary, missing tags,
  no recognised tags, malformed, multi-device); status-mapping tests.

- **M3 — Wizard UI.**
  `AddMTConnectSource.razor` mirroring `AddFocas2Source.razor`: connection → browse →
  read-only tag list (available + greyed-with-reason) + discovered axes + conditions
  note → review summary → **save via draft→validate→apply** (never bypass). Testable
  `MTConnectSourceWizardModel` POCO + tests. Edit support via `SourceEditRouter`.

- **M4 — Tile flip.** `SourceProtocolPickerModel`: `mtconnect` → Available,
  `TargetHref = "/sources/new/mtconnect"`, drop `PendingMilestone`. Update picker test
  + `OnboardingFlow.razor` copy. **S7 stays Pending.**

- **M5 — End-to-end verify + close.** Drive against a real/demo agent (fixtures for
  CI; manual for live): browse → save → source comes up → emits canonical points to a
  sink. Full build + tests green (0/0). Handoff doc. `CLAUDE.md` §8 → MTConnect
  operator-available.

- **M6 (follow-up, not blocking Available) — Demo mode.** `MTConnectDemoMode` (static
  `/probe` + generated `/current`) so sales/dev exercise the wizard with no hardware,
  mirroring `Focas2DemoMode`.

## 6. Scope / layering / risks

- **No Core changes.** Adapter (shared map extraction + bounded probe parser) +
  Management (DTO/API/service/wizard) + one picker-model flip.
- Add `<ProjectReference Sources.MTConnect>` to Management if not present (precedent:
  ModbusTcp/OPC UA already referenced).
- `/probe` varies across agent versions/vendors → **fixture-driven parser tests** with
  real captured documents are essential; namespace-default-aware parsing.
- Keep wizard logic in the testable POCO; `.razor` stays a thin shell.
- The stream-parser refactor (M2a) is behaviour-preserving — guarded by existing
  adapter tests + the new parity test.

## 7. Test plan summary

- Parser fixtures: multi-axis (X/Y/Z/A), rotary, subset of tags, none recognised,
  malformed XML, non-MTConnect doc, multi-device, >12 axes (cap).
- Shared-map parity test (parser ↔ map).
- Browse-service status-mapping tests (each `MTConnectBrowseStatus` → HTTP code).
- Wizard-model tests: required URL, status handling, axis/tag rendering inputs,
  save payload shape.
- M5 end-to-end (real/demo).

## 8. M1 mockup sign-off revisions (locked into v2)

The M1 mockup was **approved in structure** with five refinements, now locked:

1. **Unavailable tags are NOT activated.** Tags the agent doesn't expose are shown for
   transparency but are *not* part of the saved active tag set — no empty/placeholder
   active tags are created. The wizard's **active set = available standard tags + axis
   tags** only. (Earlier copy "saved but empty" was wrong and is removed.)
2. **Explicit active-tag math** in the review summary and save bar:
   `standard (active) + axis = total active` (e.g. `10 + 8 = 18`).
3. **"Refresh discovery"** action on the discover step — re-runs the `/probe` browse
   without leaving the wizard (the browse endpoint is idempotent; just re-call it).
4. **Save-gating on `ReachableNoRecognisedTags`.** When the agent is reachable but 0
   standard tags are recognised, **Save is blocked by default**; the operator must
   click an explicit **"Save anyway"** to create an empty source. (Other reachable
   states save normally.)
5. **Multi-device handling.** `/probe` may list multiple devices. EdgeConnect onboards
   **one device per source**; the "Device name" field selects which (blank = first).
   The wizard shows a multi-device note ("N devices — onboarding X; add others
   separately").

### Implications for the build

- **DTO:** add `IReadOnlyList<string> AvailableDevices` to `MTConnectBrowseResult` so
  the wizard can render the multi-device note and validate the chosen device name.
  `DeviceName`/`DeviceUuid` remain the *targeted* device.
- **Wizard model:** computes `ActiveStandardTags` (available only) + `AxisTags`;
  exposes `TotalActiveTags`; `CanSave` is false for `ReachableNoRecognisedTags` unless
  an explicit `saveAnyway` flag is set; a `Refresh()` re-invokes browse.
- **Save payload:** persists only the active tag set; unavailable tags are not written
  as active. (Confirm during M3 how the MTConnect source config represents its active
  tag set — if it has no explicit per-tag list today, the "active set" is implicit in
  which tags the adapter emits; the wizard still must not imply unavailable tags are
  active. Resolve this concretely in M3.)
- **Tests:** add wizard-model tests for the save-gate (no-recognised → blocked →
  save-anyway), the tag math, and multi-device note rendering.
