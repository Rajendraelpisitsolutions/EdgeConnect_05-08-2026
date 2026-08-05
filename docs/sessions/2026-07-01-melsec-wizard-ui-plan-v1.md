# MELSEC Wizard + Diagnostics UI — Plan v1

**Date:** 2026-07-01
**Status:** Plan v1 — for ChatGPT review pass (→ v2 → reality-check → v3).
**Depends on:** the MELSEC **backend slice** (PR #163, `feat/melsec-source`) —
this UI slice builds on it and turns the adapter **operator-available**.
**Cadence note:** UI work follows the standing rule — **static HTML mockup for
operator sign-off BEFORE any Razor**. This plan's first build artifact is the
mockup (`2026-07-01-melsec-wizard-ui-mockup.html`); Razor implementation begins
only after mockup approval.

## 0. Goal & guardrails

Make MELSEC **easy to configure and easy to diagnose in the field**: an Available
source tile, a guided wizard mirroring the S7 wizard, inline validation driven by
the real backend parser, a test-connection/test-read probe, and a diagnostics
panel that explains health, failures (incl. MELSEC end codes), and the planner's
scan blocks.

**Out of scope this slice (unchanged from backend):** no browse, no writes, no
UDP, no 4E, no 1E, no ASCII, no demo mode, **no CSV import** (S7 has it; MELSEC
defers it unless separately approved). Slice-1 runtime stays MC 3E binary / TCP /
read-only.

**Reuse over invent (S7 consistency, req #5):** use the existing shared primitives
— `WizardShell`, `WizardSection`, `WizardValidationBanner`, `WizardActions` — and
mirror the S7 probe-API + save-flow patterns. Do not roll custom layout.

## 1. Source tile (req #1)

Add one `SourceProtocolTile` to `SourceProtocolPickerModel.Tiles`
(`src/…Management/Wizards/SourceProtocolPickerModel.cs`):

```
Key = "melsec"
DisplayName = "Mitsubishi MELSEC"
Description = "iQ-R / iQ-F / Q / L via SLMP / MC 3E binary (TCP)."
IconSvg = Icons.Material.Filled.DeveloperBoard   // same family as S7
Status = Available
TargetHref = "/sources/new/melsec"
```

Guarded by `SourceProtocolPickerModelTests` (tile enumerated + Available).

## 2. Guided wizard (req #2) — page `/sources/new/melsec` (`AddMelsecSource.razor`)

Four `WizardSection`s inside a `WizardShell`, mirroring `AddS7Source.razor`.

### Section 1 — Source
Source id (immutable in Edit), name, description, **device class** (default `plc`),
**enabled**. Live unique-id check (inline error), same as S7.

### Section 2 — PLC connection
- **Host / IP** (required).
- **TCP port** (required — **no universal Mitsubishi default**; helper text lists
  common values 5000/5001/5006/6000 per the discovery package Q2).
- **Protocol summary** (fixed, not editable): a read-only line + Info alert —
  *"Slice 1: MC Protocol 3E, Binary, TCP, read-only."* This is the ADR-0015 Rule 6
  carve-out (no browse; other modes/writes not offered in this slice). Because the
  mode is fixed in the UI, `CONFIG_MODE_NOT_IMPLEMENTED` normally can't be
  produced from the wizard — it only surfaces on **hydrate** of a hand-edited
  gateway.json (see §4), where the banner shows it.
- **Test connection** button + inline result panel (§3).
- **Advanced (expansion panel):**
  - **Route header** (discovery Q4 wording): Network No. (0x00), PC No. (0xFF),
    Destination module I/O No. (0x03FF), Destination station No. (0x00). Hex-aware
    inputs with the default shown.
  - **Timing / reliability:** ConnectTimeoutMs, **RequestTimeoutMs**,
    **MonitoringTimerMs** (helper: "encoded in 250 ms units, rounded up"),
    **MaxPointsPerRequest** (1–960), **MaxGapWords** (≥0), and the S7-parity
    reconnect/backoff set: MaxTransactionRetries, InitialBackoffMs, MaxBackoffMs,
    BackoffMultiplier, CircuitBreakerThreshold, CircuitBreakerResetMs, KeepAlive.

### Section 3 — Tags ({count})
Row-edited table (no modal), mirroring S7:

| Column | Notes |
|--------|-------|
| Name | canonical tag name |
| Address | `D100`, `W1A`, `M200`, `D100.3` — inline-validated (§2.4) |
| Datatype | Bool / Int16 / UInt16 / Int32 / UInt32 / Float32 |
| **Word order** | shown **only** when datatype is Int32/UInt32/Float32; LowWordFirst default |
| ScanRateMs | default 1000 |
| Status | ✓ Valid / ⚠ Warning / ✗ Error |
| Actions | remove |

**More/Less** row toggle reveals **Unit / Scale / Offset** (S7 pattern). Empty
state = "Add your first tag". **No CSV import** this slice.

### 2.4 Inline validation (req #2 last bullet) — driven by the real backend
The wizard calls the backend so operators see the *same* verdicts the runtime
will enforce (no reimplemented rules in the UI):
- **Address** (debounced, on change) → `MelsecAddressParser` (via the model /
  probe-validate endpoint). Surfaces typed codes:
  - `MELSEC.DEVICE_NOT_IMPLEMENTED` — e.g. `SM0`, `SB0`, `T0`, `Z0` → "device
    recognized but not supported in this release (supported: D, W, R, ZR, M, X, Y, B)".
  - `MELSEC.CONFIG_INVALID_ADDRESS` — malformed / unknown device / bad bit suffix.
  - Radix helper text on the field: **`ZR`, `W`, `X`, `Y`, `B` are hexadecimal;
    `D`, `R`, `M` are decimal** (the corrected ZR=hex, ADR-0033).
- **Datatype vs address** → `MELSEC.CONFIG_DATATYPE_MISMATCH` (Bool needs a
  word-bit/bit device; word device needs non-Bool).
- **Whole-config** on Save/validate → the adapter's `ValidateConfigAsync`, which
  also yields `CONFIG_MODE_NOT_IMPLEMENTED` / `CONFIG_POINTS_CAP` /
  `CONFIG_TIMEOUT_INCOHERENT` / `CONFIG_INVALID_SCANRATE` into the
  `WizardValidationBanner` (errors block Save; scroll-to-field on click, Rule 5).

## 3. Test connection / test read (req #3)
New **`MelsecProbeApi`** mirroring `S7ProbeApi`:
- `POST /api/v1/sources/browse/melsec/test-connection` — `SlmpClient` connect
  probe (idempotent, separate short-lived connection, no side effects — ADR-0015
  Rule 6). Result: Success + elapsed ms, or failure code + message.
- `POST /api/v1/sources/browse/melsec/test-read` — read **one or more** configured
  tags once and return per-tag: **Good/Bad quality**, **decoded value**, and — in
  an **Advanced (expandable) panel** — the **raw word payload** (hex) for
  diagnostics. Protocol failures show the **MELSEC end code** + description.

Both render as inline `MudAlert` panels (not snackbars), disabled while busy.

## 4. Diagnostics panel (req #4)
A per-source MELSEC diagnostics view (extend the existing diagnostics surface /
`SourceDetail` activity card; consumes a diagnostics DTO). Fields:
- **Adapter health / state** (Created…Running/Degraded/Failed/Stopped) + level.
- **Connected / disconnected**; **last successful poll time**; **last error**
  (code + category + message); **MELSEC end code** when the last failure was a
  protocol error.
- **Reconnect count** (consecutiveFailures) / **circuit-breaker state**
  (Closed/Open/HalfOpen); **last request latency**.
- **Configured route fields** (network / PC / module I/O / station).
- **Scan blocks generated by the planner** — device, head device, word count,
  scan-rate bucket, mapped-tag count (proves coalescing to the field engineer).
- **Per-tag quality summary**; for a failed block, the **affected tags** list.

> **Backend note (surfacing gap):** `AdapterHealth.Metrics` already carries
> connected/consecutiveFailures/breakerState/poll+read+decode counts. The
> **scan-block list, route fields, MELSEC end code, and per-block affected-tags**
> are not in the generic health snapshot yet — this slice adds a small MELSEC
> diagnostics DTO (or extends the metrics map) to expose them. Flagged for the
> reality-check pass; keep it observational (platform principle P1).

## 5. Save / load (S7 parity, req #5)
- **Add:** `MelsecSourceWizardModel.BuildSourceInstance()` emits `SourceInstanceConfig`
  with the opaque Connection JSON assembled via **`MelsecConnectionKeys`** constants
  (single source of truth — already exists from the backend), then POST
  `/api/v1/config/drafts` → Configuration page (validate → apply).
- **Edit:** `HydrateFromExisting()` populates the wizard from an existing source;
  PUT `/api/v1/sources/{id}` (draft+apply, 409 → `StaleEditWarningBanner`).
- Round-trip (`Hydrate` → `Build`) must be stable.

## 6. Discovery-package wording (req #8)
Field labels/help reuse the customer-facing terms from
`2026-06-30-melsec-discovery-package.md`: **CPU model / Ethernet module**
(device name/class hints), **protocol/encoding/transport** (the fixed summary
line), **route fields** (Q4 names verbatim), **tag list** (address/datatype/word
order columns), and the **read-only** confirmation (stated in the protocol
summary). Keeps the wizard and the field questionnaire speaking the same language.

## 7. module-catalog.md row (req #7)
The `source-melsec` catalog row is still deferred (that file has an unrelated
pre-existing OPC-membership edit). Plan: attempt to isolate **only** the MELSEC
row; **if it can't be cleanly separated, land it as its own tiny docs-cleanup
commit** (not mixed into the UI code). The functional license key already ships
in `LicenseModuleKeys`. Same for the CLAUDE.md §8 "operator-available" update —
done when this slice completes.

## 8. Deliverables & sequencing
1. **This plan** (v1 → review → v2 …).
2. **Static HTML mockup** (`2026-07-01-melsec-wizard-ui-mockup.html`) — tile,
   the 4 wizard sections with inline-validation states (incl. ZR-hex hint,
   `DEVICE_NOT_IMPLEMENTED`, `CONFIG_MODE_NOT_IMPLEMENTED`), test-connection/read
   panel (Good/Bad + decoded value + raw-word advanced panel), and the diagnostics
   panel. **Operator sign-off gate.**
3. **Razor implementation** — only after mockup approval: tile, `AddMelsecSource.razor`
   (+ edit routing), `MelsecSourceWizardModel`, `MelsecProbeApi`, diagnostics DTO/view.
4. **Tests** (mirror `ModbusSourceWizardModelTests` / S7 wizard tests):
   - **config validation** — inline codes (ZR-hex accepted; unsupported device →
     `DEVICE_NOT_IMPLEMENTED`; unsupported mode → `CONFIG_MODE_NOT_IMPLEMENTED`).
   - **wizard save/load** — `BuildSourceInstance` pins Connection JSON keys via
     `MelsecConnectionKeys`; `Hydrate`→`Build` round-trip.
   - **diagnostics rendering** — health/end-code/scan-blocks/affected-tags render.
   - **unsupported-mode messaging** — banner shows the typed error.
   - **tile** — `SourceProtocolPickerModelTests` enumerates `melsec` Available.
   - Gate: run the **entire** Management.Tests project (not topic-filtered).

## 9. Open questions for the review pass
1. **Diagnostics DTO shape** — extend `AdapterHealth.Metrics` (stringly-typed) vs
   a typed MELSEC diagnostics DTO for scan-blocks/route/end-code? Recommend a
   small typed DTO surfaced via the diagnostics API. Confirm.
2. **Test-read scope** — one selected tag (S7 parity) vs "read all configured
   tags" (the req says "one or more"). Recommend: selected tag by default + an
   optional "read all" that batches through the planner blocks. Confirm.
3. **Where the diagnostics panel lives** — embedded in `SourceDetail` vs the
   global `/diagnostics` page vs both. Recommend: per-source detail (field
   engineer's entry point) + surface faults on `/diagnostics`.
4. **CSV import** — confirmed out this slice (defer to a follow-up like S7's).

---
*Next: user/ChatGPT review → v2. Build the static HTML mockup now for early
visual sign-off; hold Razor until mockup approval.*
