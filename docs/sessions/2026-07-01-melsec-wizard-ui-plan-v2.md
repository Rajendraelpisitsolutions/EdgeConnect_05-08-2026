# MELSEC Wizard + Diagnostics UI — Plan v2

**Date:** 2026-07-01
**Status:** v2 — incorporates the ChatGPT review of v1. Direction approved;
**Razor still gated** on revised-mockup sign-off.
**Supersedes:** `2026-07-01-melsec-wizard-ui-plan-v1.md` (read v1 for unchanged
scope: tile §1, wizard sections §2, save/load §5, discovery wording §6,
deliverables §8). v2 records the review deltas + open-question resolutions.
**Revised mockup:** `2026-07-01-melsec-wizard-ui-mockup.html` (updated to match v2).

## 0. Open-question resolutions (from review)
- **Diagnostics DTO shape:** small **typed `MelsecDiagnosticsDto`** (not stringly metrics).
- **Test-read scope:** **selected tag by default + optional "read all valid tags"**.
- **Diagnostics location:** **SourceDetail first**; global page summarizes faults later.
- **CSV import:** still **out of scope**.

## 1. Changes from v1 (the review deltas)

### Δ1 — Premium license UX
`source-melsec` is **Premium-gated**. The UI is **explanatory only**; the real
gate stays in the backend (`MelsecRegistrationExtensions` license check +
apply-time enforcement — already shipped).
- **Tile:** `Available` when the license enables `source-melsec`; otherwise a
  **"Requires Premium"** locked tile (disabled, dashed, upgrade tooltip/link).
  The picker derives status from the license service (`IsModuleEnabled("source-melsec")`),
  the same way any Premium tile does.
- **Wizard:** if opened while unlicensed (deep link), show a non-blocking
  **Premium banner** explaining the module is not enabled; **Save still relies on
  backend gating** (draft apply / registration returns the license skip) — the UI
  never fakes a licensed save.
- Tests: tile shows locked state when module disabled; wizard save path does not
  bypass the backend gate.

### Δ2 — UI defaults sourced from backend (confirmed)
All wizard defaults are the **`MelsecSourceConfiguration` record defaults** (verified):
`MonitoringTimerMs=4000`, `ConnectTimeoutMs=3000`, `RequestTimeoutMs=5000`,
`KeepAlive=true`, `MaxTransactionRetries=2`, `InitialBackoffMs=2000`,
`MaxBackoffMs=60000`, `BackoffMultiplier=2.0`, `CircuitBreakerThreshold=5`,
`CircuitBreakerResetMs=30000`, `MaxGapWords=8`, `NetworkNo=0x00`, `PcNo=0xFF`,
`RequestDestModuleIoNo=0x03FF`, `RequestDestModuleStationNo=0x00`, `Port` = none.
- **`MaxPointsPerRequest=480` is confirmed the backend default** (hard cap 960).
  It is an **intentional conservative default** and is **written into the config**
  on save (not just a placeholder). The wizard model must **source defaults from
  the config record**, not re-declare divergent literals, so they can never drift.

### Δ3 — Typed `MelsecDiagnosticsDto` (not stringly metrics)
- **Summary counters** stay in the generic `AdapterHealth`/metrics (pollAttempts,
  successes, failures, reads, decodeFailures, connected, consecutiveFailures,
  breakerState) — already present.
- **MELSEC-specific nested detail** goes in a small **typed `MelsecDiagnosticsDto`**:
  route header fields, planner **scan blocks** (device, head, word count, scan
  rate, mapped-tag names, last block result), **last MELSEC end code** + description,
  **per-tag quality** + **affected-tags** for a failed block, last request latency.
- The DTO is **observational only** — it never controls adapter behavior (platform
  principle P1). Surfaced via the diagnostics API for `SourceDetail`.

### Δ4 — Probe API route (no browse)
MELSEC has **no browse**, so the S7-style `/sources/browse/...` naming is wrong
for it. Each protocol defines its own probe API class (S7 has `S7ProbeApi`), so we
are free to use a cleaner route:
- **`POST /api/v1/sources/probe/melsec/test-connection`**
- **`POST /api/v1/sources/probe/melsec/test-read`**
Both are **probe-only** — no tag discovery / browse. Add a test asserting **no
browse endpoint exists** for `melsec` and that the probe endpoints do not return a
tag list. (If the shared probe plumbing turns out to force the `/browse/` prefix,
document it as probe-only and keep the same no-browse test — but prefer `/probe/`.)

### Δ5 — Test-read behavior (precise)
- **"Test read selected"** (default) — one tag.
- **"Read all valid tags"** (optional) — batches through the **scan planner**;
  **shows the planned blocks before executing** (device/head/count) so the operator
  confirms the read shape; large reads are capped/confirmed.
- **Invalid tags are skipped with a clear warning** and **never sent to the PLC**.
- Probe uses a **short-lived `SlmpClient`** (connect → read → close); it **does not
  mutate source runtime state** (idempotent, ADR-0015 Rule 6).

### Δ6 — Raw diagnostics safe by design
Raw request/response words can expose production data. Therefore:
- **Behind an Advanced reveal** (collapsed by default).
- **Not persisted in normal logs.**
- **Not included in exported diagnostics / bundles** unless explicitly requested,
  and then subject to **ADR-0020 redaction** (raw payload tier gated).

### Δ7 — Hydrate/edit must not silently normalize unsupported config
If an existing `gateway.json` source has `Udp`/`Mc4EBinary`/`Mc1EBinary`/`ASCII`
etc., the wizard **surfaces `MELSEC.CONFIG_MODE_NOT_IMPLEMENTED` and blocks Save**
until corrected — it **never silently rewrites** the mode to TCP/3E. Behavior:
- On hydrate, the model **preserves and displays** the unsupported values (read as
  the fixed protocol summary showing a mismatch) and raises the banner.
- The Connection JSON is rebuilt from the **known Slice-1 keys** on save, so
  **unsupported/future fields are discarded only when the operator saves a
  corrected Slice-1 config** — an explicit, operator-initiated normalization, never
  silent. This is documented in the wizard's save summary.

### Δ8 — Route-field validation ranges (backend-typed)
Hex-aware inputs validate against the **backend field types** (not duplicated UI
rules), keyed by `MelsecConnectionKeys`:
- Network No. `0x00–0xFF` (byte) · PC No. `0x00–0xFF` (byte) · Dest. module I/O
  No. `0x0000–0xFFFF` (ushort) · Dest. station No. `0x00–0xFF` (byte).
Out-of-range → inline field error before Save (and the backend `FromSourceInstance`
byte/ushort readers enforce the same bounds).

### Δ9 — Diagnostics placement (approved)
**Per-source `SourceDetail` MELSEC diagnostics panel first.** The global
`/diagnostics` page later carries a **fault summary / link** (not the full panel).
Field engineers start from the source detail page.

### Δ10 — Mockup updated
The revised `…-mockup.html` now shows: the **licensed vs "Requires Premium" tile
states**, the **confirmed 480 default (with cap-960 note)**, the **`/probe/melsec/…`
wording**, **"Read all valid tags"** with a planned-blocks preview + skipped-invalid
warning, the **raw-words safety note**, **route-field range hints/validation**, and
the **typed `MelsecDiagnosticsDto`** shape. Still static HTML; no Razor.

## 2. Test plan additions (on top of v1 §8.4)
- **License:** tile locked when `source-melsec` disabled; wizard save honors
  backend gate (no client-side bypass).
- **Probe route:** `/probe/melsec/test-connection` + `/test-read` exist; **no
  browse endpoint** for melsec; probe returns no tag-discovery list.
- **Read-all:** invalid tags skipped (not sent); planned blocks surfaced.
- **Hydrate:** unsupported-mode config → `CONFIG_MODE_NOT_IMPLEMENTED` + Save
  blocked; no silent normalization.
- **Route-field ranges:** out-of-range network/pc/io/station rejected inline.
- **Diagnostics DTO:** `MelsecDiagnosticsDto` renders scan-blocks / end-code /
  affected-tags; raw payload absent from exports unless requested+redacted.
- Gate: entire Management.Tests (not topic-filtered).

## 3. Unchanged from v1
Tile catalogue integration, 4-section wizard structure, inline validation via the
real backend parser (ZR=hex etc.), S7-consistent shared primitives, save via
draft/apply (Add) or PUT (Edit), discovery-package wording, module-catalog row +
CLAUDE.md §8 update on slice completion, out-of-scope list (no browse/writes/UDP/
4E/1E/ASCII/demo/CSV).

---
*Next: user review of v2 + revised mockup → sign-off → Razor implementation + tests.
Do not implement Razor until sign-off.*
