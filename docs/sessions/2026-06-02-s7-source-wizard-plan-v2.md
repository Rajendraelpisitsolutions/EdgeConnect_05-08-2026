# Siemens S7 source wizard (M.2b.2) — implementation plan v2 (LOCKED for build)

**Date:** 2026-06-02
**Status:** Plan v2 — incorporates the ChatGPT review of v1. **Ready to build,
starting with M1 (static mockup → operator sign-off).**
**Supersedes:** `2026-06-02-s7-source-wizard-plan-v1.md`
**Governing:** ADR-0015 (wizard contract); MTConnect plan-trail as the process
template; the **Modbus wizard** (`AddModbusSource.razor` / `ModbusSourceWizardModel`)
as the implementation template.

## Frame (unchanged, approved)

S7 can't self-describe → the wizard is a **Modbus-style manual tag-address
editor**, not a browse flow. Backend (`Sources.S7` + Host DI) exists and is
tested; this is purely the missing Studio surface. M1–M5 mockup-first; tile
flips to Available only after the wizard is real (M4).

## Backend facts confirmed (drive the UI copy)

- **Supported datatypes (`S7Datatype`, 14):** Bool, Byte, SInt, USInt, Int,
  Word, DInt, DWord, Real, LReal, LInt, ULInt, String, Char. The datatype
  dropdown offers exactly these; validation rejects anything else.
- **Address parser (`S7AddressParser.Parse` in `S7Address.cs`) covers:** DB
  (`DBX`/`DBB`/`DBW`/`DBD` + bit), Markers `M`, Inputs `I` (and German `E` =
  Eingang), Outputs `Q` (and German `A` = Ausgang), Timers `T`, Counters `C`.
  → The editor accepts German `E`/`A` mnemonics too (nice for EU operators).
  → **T/C parse, but the adapter read path for Timer/Counter is unconfirmed** —
  M2 must verify end-to-end before the UI lists T/C in examples (else hide them).
- **`OptimizedDbAccess`:** the config field exists, but the adapter's optimized-DB
  walk is flagged "future Milestone N" in code. So **v1's supported path is
  absolute addressing on standard (non-optimized) DBs.** Keep the toggle
  (advanced, default off) with neutral help text (change #4); M2 confirms whether
  it currently changes read behavior before finalizing copy.

## Decisions (Q1–Q6, locked per review)

| Q | Decision |
|---|---|
| **Q1 CSV import** | **Defer to v1.1.** v1 ships manual editor only. Mockup shows "Import CSV — coming next" (disabled) or omits it — never an enabled v1 feature. Add a follow-up for `S7TagCsvImporter` (schema `Name,Address,Datatype,ScanRateMs,Unit,Scale,Offset`). |
| **Q2 Datatype suggest** | **Suggest, never coerce.** Width hint → suggestion text (`DBX→Bool`, `DBW→Int/Word`, `DBD→DInt/DWord/Real`). Auto-fill the datatype **only while the field is blank / at its untouched default**; once the operator sets it, address edits update the suggestion *text* but never overwrite their choice. |
| **Q3 Validation** | **Server-side, parser-only application service** (endpoint is the transport). Contract below. No network access; debounced; never surface raw parser exceptions. |
| **Q4 Test connection** | **Two separate actions:** (a) *Test connection* (reachability: host/port/rack/slot/connection-type/timeout + cancellation); (b) *Test selected tag read* (enabled only for a valid selected row; read-only; distinguishes read-ok / read-failed / DB-access-denied / datatype-mismatch). A broken tag must never look like a connection failure. |
| **Q5 Verification** | **Tiered M5 bar** (below). Real hardware is NOT a hard gate for "operator-available"; the handoff records which tier was achieved. |
| **Q6 Demo mode** | **Defer** — follow-up ticket only, not in M1–M5. |

## Validation rules (the editor enforces these)

**Tag name:** required · trimmed · **unique within the source** (block Save on dup).
**Address:** required · parsed by `S7AddressParser` · normalized preview shown ·
**invalid rows block Save**. Duplicate *addresses* **warn, not block** (unless M2
finds the backend forbids them).
**Datatype:** required · must be one of the 14 `S7Datatype` values ·
width/address compatibility **warning** where derivable (e.g. `Bool` on a `DBW`
address).
**ScanRateMs:** required · positive · enforce a project minimum if one exists.
**Scale/Offset:** numeric · default Scale = 1, Offset = 0.

## Address validation contract (M2)

```csharp
record S7AddressValidationRequest { string Address; string? Datatype; }
record S7AddressValidationResult {
    bool IsValid;
    string? NormalizedAddress;        // S7Address.ToString()
    string? Area;                     // DataBlock/Marker/Input/Output/Timer/Counter
    int? DbNumber; int? ByteOffset; int? BitOffset;
    string? WidthHint;                // Bit/Byte/Word/DWord
    IReadOnlyList<string> SuggestedDatatypes;
    string? ErrorCode; string? Message;
}
```
Parser-only. Debounced from the UI. Wraps `S7AddressParser.Parse`; converts its
`ArgumentException` into a structured `IsValid=false` + friendly message (no raw
exception text).

## Test-connection design (M2/M3) — two statuses

```
[ Test connection ]            → "PLC reachable at host:port, rack/slot accepted"
                                 / "refused" / "timeout" (cancellable)
[ Test selected tag read ]     → enabled only for a valid selected row; read-only
                                 → "read ok (value …)" / "read failed" /
                                   "DB access denied" / "datatype mismatch"
```
Mirrors `ModbusProbeService` / `MqttTestConnectionService` (fake `IS7Client` in
tests). **Save is strictly separate**: Save validates shape + applies via
draft→validate→apply; a failed Test-connection **warns, does not block Save**
(offline staging must work) unless project policy says otherwise.

## Milestones (mockup-first; expanded per review)

### M1 — Static HTML mockup (sign-off gate)
Sections: **Source identity** (display name, optional description; protocol fixed
to Siemens S7) → **Connection** (Host/Rack/Slot/Port/ConnectionType, advanced-
collapsed timeouts, OptimizedDbAccess toggle w/ neutral help) → **Tag editor**
table (Name · Address · Datatype · Scan · Unit · Scale · Offset) with the parsed
preview + datatype suggestion. **Must show failure states**, not just happy path:
- empty initial wizard
- one valid tag with parsed preview (`DB1.DBW0 → DB 1 · Word · byte 0`)
- one invalid tag with inline parser error
- Test-connection **success** and **timeout/refused**
- optional selected-tag **read failure**
- **Save blocked** by row errors
- CSV shown only as "coming next" (disabled) or omitted.
**Gate:** operator sign-off.

### M2 — Backend glue
- `S7SourceWizardModel` POCO + map to/from `SourceInstanceConfig` (mirror
  `ModbusSourceWizardModel`) + round-trip tests.
- Address-validation service + `S7AddressValidationResult` (+ endpoint).
- Datatype-suggestion helper (width-hint → suggestions).
- Test-connection service (reachability) + tag-read service, with **fake
  `IS7Client`** outcome tests.
- **Address parser test matrix** (deliverable, below).
- Tag-list validation tests (name unique, address required/parsed, datatype in
  set, scan positive, scale/offset defaults).
- Confirm: T/C adapter read-support (→ keep or hide in UI), OptimizedDbAccess
  read semantics, duplicate-address backend behavior.
**Gate:** tests green.

### M3 — `AddS7Source.razor` + edit
- Connection + tag-editor + **debounced** live validation + row-level validation
  summary + datatype suggestion (no overwrite of a manual choice) + **cancellable**
  Test-connection + Test-selected-read.
- **Edit mode** (`SourceEditRouter` +s7): existing S7 source opens; all
  connection fields + all tag rows round-trip; removing a tag persists; an
  invalid edit blocks apply and leaves the existing config untouched.
**Gate:** build + tests green.

### M4 — Flip tile + picker test
`SourceProtocolPickerModel` s7 → **Available** + picker test; confirm onboarding/
standalone placement (OPC-UA-client precedent). **Do not flip until M3 tests pass
and the required M5 non-hardware path is defined.**
**Gate:** tests green.

### M5 — End-to-end verification (tiered)
**Required (non-hardware) for v1:**
- Add a new S7 source from the picker; Save a valid config via draft→validate→apply.
- Reload + edit the source (round-trips).
- The adapter accepts the config and reaches Running/Connecting **without schema
  errors**.
- An **invalid address blocks Save**.
- Test-connection exercised through success/failure (fake `IS7Client` or simulator).
- Address parser matrix green.

**Preferred (if available):** Snap7/Sharp7 **server simulator** proving ≥1
successful read.
**Hardware:** real S7-1200/1500 when available.
The handoff records which tier was achieved.
**Gate:** Done.

## Address parser test matrix (M2 deliverable)

**Valid:** `DB1.DBX0.0`, `DB1.DBX0.7`, `DB1.DBB0`, `DB1.DBW0`, `DB1.DBD0`,
`M0.0`, `MB4`, `MW4`, `MD4`, `I0.0`, `IB2`, `IW2`, `ID2`, `Q0.1`, `QB0`, `QW0`,
`QD0` (+ German `E…`/`A…` forms; + `T5`/`C3` **iff** adapter read-supported).
**Invalid:** ``, `DB.DBW0`, `DB1.DBX0.8`, `DB1.DBW-1`, `DB1.DBZ0`, `X1.DBW0`,
`DB1.DBW`, `DB1.DBX0` (bit addr missing bit), `DB1.DBX0.a`.

## Deferred follow-ups (out of v1)

- **`S7TagCsvImporter`** (v1.1) — bulk import, schema `Name,Address,Datatype,
  ScanRateMs,Unit,Scale,Offset`; mirror `ModbusTagCsvImporter`.
- **S7 demo mode** — static config + synthetic values (mirror FOCAS2/MTConnect),
  for no-hardware demos.
- **Optimized-DB walk** — already a backend "future Milestone N"; the wizard's
  OptimizedDbAccess toggle stays advanced/neutral until that lands.

## v2 stance (the summary)

> S7 v1 ships as a manual Modbus-style wizard with **source identity**,
> **connection settings**, **row-based tag entry**, **server-side address
> validation**, **datatype suggestions**, **optional connection/read tests**,
> **edit support**, and normal wizard apply/rollback semantics. CSV import and
> demo mode are deferred. The tile flips to Available only after the wizard,
> mapping, validation, edit route, picker test, and the **required non-hardware
> e2e config-apply verification** are green.

## M1 mockup review — UI punch list (binds M3)

The M1 mockup (`8-s7-source-wizard.html`) was reviewed and **approved after
edits** (all applied to the mockup). These are operator-UX requirements for the
real `AddS7Source.razor` (M3):

**Applied / required in M3:**
- **Operator copy is task-first** — header: "Enter the PLC connection details
  and the Siemens S7 addresses to read" + examples (`DB1.DBW0`, `DB1.DBX0.0`,
  `M10.2`, `IW4`, `Q0.1`) + "validates as you type." **No plan-trail / process
  language in the live UI.**
- **Result-state reference card is design-only** — the live wizard shows those
  states *only when they happen* (never a static reference panel).
- **Optimized DB access lives under Advanced** (not the main connection row),
  with the explicit "leave off for absolute DB addresses; standard/non-optimized
  DBs are the supported path this release" hint.
- **No T/C examples** in v1 UI (until M2 proves the adapter read path).
- **Rack/Slot hints** ("Usually 0." / "Commonly 1 or 2.").
- **Tag table default columns:** Name · Address · Datatype · Scan · **Status** ·
  Actions. Unit/Scale/Offset behind a per-row **"More"** expander (advanced).
- **Status column** per row (✓ Valid / ⚠ Warning / ✗ Error) in addition to the
  inline message under Address.
- **Datatype suggestion copy:** "**Address suggests:** Int or Word" / "… Real or
  DInt" / "… Bool"; auto-fill only while the datatype field is untouched, never
  overwrite a manual choice.
- **Shorter error copy** (e.g. "DBZ is not a valid DB width. Use DBX, DBB, DBW,
  or DBD. Examples: DB1.DBW0, DB1.DBX0.0."; bit "Bit offset must be 0–7."; missing
  DB "DB number is missing.").
- **Test selected tag read** button states: disabled+hint when no row / invalid
  row selected; enabled+("reads once, does not save") when a valid row is selected.
- **CSV affordance:** "Import CSV — planned" (disabled) in the mockup; **remove
  entirely in production v1** (re-add with the `S7TagCsvImporter` in v1.1).
- **Save bar copy** is operator-facing ("3 tags · 1 invalid. Fix the highlighted
  row before saving.") — no internal draft→validate→apply wording in the UI.

**Kept (unchanged):** source name/description/protocol; host/port/rack/slot/
connection-type; Test connection placement; manual tag table; live validation;
datatype suggestions; invalid-row-blocks-Save; test-selected-read.

## M2 build amendments (2026-06-03) — LOCKED, narrows the validation rules above

M2 (backend glue) is **built and green**. Two validation rules above were
narrowed during the build after confirming backend behaviour; these supersede
the looser wording in "Validation rules" / "M1 mockup review" and **bind M3**.

**1. Planner-rejected datatype/address combos BLOCK Save (not warnings).**
The S7 scan planner (`S7ScanPlanner.ValidateAgainstAddress`) deterministically
**throws at adapter Initialize** for two combinations, so the wizard treats them
as Save-blocking **errors**, never warnings:

```
1. Bit-form addresses (DBX / dot-bit) require Bool.
2. A datatype may not be wider than the parsed address width.

The wizard's live validation, Save validation, and Test-selected-read
enablement all use the same rule as the scan planner, so a config the wizard
accepts never fails adapter Initialize for a deterministic compatibility
reason.
```

Only **planner-accepted-but-suspicious** combos warn — specifically a datatype
**narrower** than the address width (e.g. `Bool` on a `DBW`), which the planner
permits. Implemented as the shared pure checker **`S7TagCompatibility`** in
`Sources.S7`, pinned to the planner by a **parity test**
(`S7TagCompatibilityTests.Check_BlockingError_MatchesPlannerRejection`).

**2. Timer/Counter addresses are unsupported and BLOCK Save.** They parse, but
the adapter read/decode path is unproven (Sharp7 reads raw bytes; the decoder
treats T/C as Word — special timer/counter encoding is not handled). The parser
stays permissive; the **wizard support layer** rejects T/C with
`S7.UNSUPPORTED_TIMER_COUNTER_ADDRESS` ("recognized but not supported this
release — use DB, M, I, or Q"). Flip this only when a real read/decode path +
tests + UI examples land.

**Confirmed open items (from the handoff):**
- **OptimizedDbAccess** — confirmed **informational only** (the adapter never
  branches on it; `S7SourceConfiguration` doc says "informational at MVP"). UI
  keeps it Advanced + neutral, default off.
- **Duplicate tag names** — backend does not forbid them, but canonical
  `TagName` identity must be unique → wizard **blocks** duplicates.
- **Duplicate addresses** — backend coalesces them (legal) → wizard **warns**,
  never blocks.

**3. "Datatype mismatch" is NOT a Test-read outcome.** S7 reads are raw bytes,
so a successful read always decodes — a datatype mismatch is not detectable at
read time. The `S7ProbeService` test-read outcomes are **read-ok / read-failed /
DB-access-denied** (+ connect failures); datatype/address correctness is
enforced **pre-Save** by `S7TagCompatibility`, not by the probe. The M1 mockup's
result-state card was updated accordingly (mismatch moved out of the read card
into a dedicated compatibility card showing the error-vs-warning split).

## M3 UI refinements (2026-06-03 mockup review) — bind M3

Targeted tweaks from the post-M2 mockup review (no redesign; layout/copy from the
M1 punch list unchanged). Applied to `8-s7-source-wizard.html`; **M3 must honor:**

1. Test connection result **defaults to "Not tested yet"** (neutral), not a
   pre-filled success chip.
2. **Advanced collapsed by default** in the live wizard (timeouts, retries,
   circuit breaker, OptimizedDbAccess all inside).
3. Column label is **"Scan interval (ms)"** (not "Scan (ms)").
4. **Explicit row-selection** for Test selected tag read: no selection → disabled
   ("select a tag row"); invalid row selected → disabled ("fix the row's errors");
   valid row selected → enabled, naming the tag ("reads <addr> once, does not save").
5. Unsupported design state covers **both Timer and Counter**.
6. **Planner-rejected datatypes are disabled** in the per-row datatype dropdown
   (e.g. Real/DInt hidden-as-disabled for a 2-byte word address, with the reason
   in the option text) — or clearly errored if disabling isn't feasible. Backed by
   `S7TagCompatibility` + `S7SourceWizardModel.SuggestDatatypes`.
7. **Duplicate tag-name** validation state (block) shown alongside the
   duplicate-address warning.
8. **Real empty-state CTA** ("+ Add your first tag") when the tag list is empty.
9. **No "Import CSV — planned"** affordance in production v1 — disabled roadmap
   buttons are not a convention here (only a lone Tap.razor instance); "not yet
   available" is the picker Pending tile. Re-add with `S7TagCsvImporter` in v1.1.
10. **Accessibility wiring in Razor:** label-for/aria-label on every field;
    `aria-invalid` + `aria-describedby` on error rows; `role="status"` +
    `aria-live="polite"` on Test connection/read results; Status column conveyed by
    text (✓/⚠/✗) not colour alone; disabled datatype options keyboard-skippable
    with reason text; `aria-selected` on the selected tag row, keyboard-selectable.

## Reference

- Plan v1 + this review — `2026-06-02-s7-source-wizard-plan-v1.md`
- Backend: `Sources.S7/{S7SourceConfiguration,S7TagDefinition,S7Address(+Parser),
  S7Datatype,IS7Client,S7ConnectionManager,S7SourceAdapter}.cs`
- Templates: `AddModbusSource.razor`, `ModbusSourceWizardModel.cs`,
  `ModbusProbeService.cs`, `ModbusTagCsvImporter.cs`
- ADR-0015 (wizard contract); `SourceProtocolPickerModel.cs` (s7 → Pending today)
