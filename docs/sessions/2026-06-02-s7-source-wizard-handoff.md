# Siemens S7 source wizard (M.2b.2) — mid-flight handoff (resume at M2)

**Date:** 2026-06-02
**Status:** **M0 (plan) + M1 (mockup) DONE and signed off. Resume at M2 (backend
glue).** Goal: make S7 operator-available (wizard + tile flip Pending→Available).
This doc is written so the next session starts M2 **cold** — everything needed
is below; no re-discovery required.

## Where it stands

| M | State | Artifact / commit |
|---|---|---|
| Plan v1 | done | `2026-06-02-s7-source-wizard-plan-v1.md` (`eea8f37`) |
| Plan v2 (LOCKED) | done | `2026-06-02-s7-source-wizard-plan-v2.md` (`ab1b602`) — **read this first** |
| M1 mockup | **signed off** (review = approve-after-edits, all applied) | `docs/sessions/2026-05-30-ux-mockups/8-s7-source-wizard.html` (`70c9947`, `7e91858`) |
| **M2 backend** | **TODO — start here** | — |
| M3 razor + edit | TODO | — |
| M4 tile flip + picker test | TODO | — |
| M5 e2e verify (tiered) + handoff | TODO | — |

Open task list items: **#55 (M2), #56 (M3), #57 (M4), #58 (M5).**

## The frame (don't relitigate)

S7 **cannot self-describe** → the wizard is a **Modbus-style manual tag-address
editor**, NOT a browse flow. Backend + Host DI already exist and are tested; this
is purely the missing Studio surface. Mirror Modbus, not MTConnect.

**Templates to copy from:**
- `Management/Wizards/ModbusSourceWizardModel.cs` — model + `BuildSourceInstance()`
  + `HydrateFromExisting()` + `ValidateTag()` + static dropdown lists.
- `Management/Components/Pages/SourceWizards/AddModbusSource.razor` — the razor.
- `Management/Api/ModbusProbeService.cs` — the test-connection service pattern.
- `Sources.ModbusTcp/Import/ModbusTagCsvImporter.cs` — CSV import (for the v1.1 follow-up only).

## The exact backend contract (already verified — build the mapping to this)

**Protocol name:** `"s7"`. **Tile:** `SourceProtocolPickerModel.cs` s7 = Pending today.

**Connection JSON object** (keys from `Sources.S7/S7ConnectionKeys.cs`) — the
wizard's `BuildSourceInstance()` emits `SourceInstanceConfig.Connection` with:
`host, port (102), rack, slot, connectionType ("Basic"), optimizedDbAccess (bool),
connectTimeoutMs (2000), requestTimeoutMs (1000), keepAlive, maxTransactionRetries
(2), initialBackoffMs (2000), maxBackoffMs (60000), backoffMultiplier (2.0),
circuitBreakerThreshold (5), circuitBreakerResetMs (30000), maxGapBytes (16),
maxReadBytes (200), deviceId, deviceName, deviceClass ("plc")`.

**Tags** live under the connection key **`"tags"`** (NOT `"tagDefinitions"` —
that's Modbus). `S7SourceConfiguration.ReadTagDefinitions` reads `conn["tags"]`,
each object: `name, address, datatype, scanRateMs (1000), unit, scale, offset`.

**`S7TagDefinition`:** `Name, Address (string, e.g. "DB1.DBW0"), Datatype?,
ScanRateMs=1000, Unit?, Scale?, Offset?, Semantics?`.

**Polling:** `SourceInstanceConfig.Polling` is **optional** (default supplied).
S7 drives **per-tag** scan rates (`scanRateMs`), not a top-level interval — so
leave `Polling` at default (don't set it from the wizard).

**Datatypes (`S7Datatype`, the 14 dropdown values):** Bool, Byte, SInt, USInt,
Int, Word, DInt, DWord, Real, LReal, LInt, ULInt, String, Char.

**Address parser** (`S7AddressParser.Parse(string)` in `Sources.S7/S7Address.cs`,
**throws `ArgumentException` on invalid** — wrap, don't surface raw). Covers:
- DB: `DB1.DBX0.0` (bit), `DB1.DBB0` (byte), `DB1.DBW0` (word), `DB1.DBD0` (dword)
- Markers `M…`, Inputs `I…` (and German `E…`), Outputs `Q…` (and German `A…`)
- Timers `T5`, Counters `C3`
- Returns `S7Address(Area, DbNumber, ByteOffset, BitOffset, WidthHint)`;
  `WidthHint` ∈ Bit/Byte/Word/DWord; `.ToString()` gives the normalized form.

## M2 deliverables (task #55)

1. **`S7SourceWizardModel` + `S7TagWizardRow`** (mirror Modbus):
   - Identity: InstanceId, DeviceId, DeviceName (display), DeviceClass="plc",
     Enabled, optional Description (store under connection `"description"` —
     harmless, FromSourceInstance ignores unknown keys; the mockup shows it).
   - Connection fields (above) with the listed defaults.
   - `Tags: List<S7TagWizardRow>` (Name, Address string, Datatype string? = null
     so "untouched" is detectable, ScanRateMs=1000, Unit?, Scale?, Offset?).
   - Static `Datatypes` (14) + `ConnectionTypes` ("Basic","OP","PG") lists.
   - `BuildSourceInstance()` → connection JSON (keys above) + `"tags"` array,
     `ProtocolName="s7"`. `HydrateFromExisting(source)` inverse (throw if
     ProtocolName != "s7"). **Round-trip invariant:** re-emit after hydrate is
     byte-equivalent (test it, like Modbus).
2. **`ValidateTag(row)`** + model `Validate()`: name required/trimmed/**unique**;
   address required + parses via `S7AddressParser` (invalid blocks Save);
   datatype required + ∈ the 14; scanRateMs required + positive; Scale default 1,
   Offset default 0. **Duplicate addresses warn, not block** (confirm backend
   doesn't forbid — see open items). (Find the shared `ValidationIssue` type the
   Modbus model uses — same one.)
3. **Address-validation service** (parser-only, no network) returning
   `S7AddressValidationResult { IsValid, NormalizedAddress, Area, DbNumber,
   ByteOffset, BitOffset, WidthHint, SuggestedDatatypes, ErrorCode, Message }`
   + a debounced endpoint the editor calls. Wrap `S7AddressParser.Parse`'s
   exception into `IsValid=false` + friendly message.
4. **Datatype-suggestion helper**: WidthHint → suggestions (Bit→Bool;
   Byte→Byte/SInt/USInt/Char; Word→Int/Word; DWord→DInt/DWord/Real). Returned in
   `SuggestedDatatypes`.
5. **Test-connection service** (reachability: host/port/rack/slot/connType/timeout
   + cancellation) and **test-selected-read service** (read-only single read →
   ok/failed/DB-access-denied/datatype-mismatch). Tests use a **fake `IS7Client`**
   (mirror `ModbusProbeService`/`MqttTestConnectionService`). Test-conn failure
   **warns, never blocks Save**.
6. **Address parser test matrix** (valid + invalid lists are in plan v2 §"Address
   parser test matrix") + tag-list validation tests + round-trip tests.

**Confirm during M2 (open items):**
- **Timer/Counter (T/C) adapter read path** — parser handles them, but does
  `S7SourceAdapter`/`S7Decoder` actually read T/C? If yes, add to UI examples +
  tests; if no, keep them out of the UI (plan v2 already keeps them out of v1).
- **`OptimizedDbAccess`** — adapter comment says the optimized-DB walk is "future
  Milestone N." Confirm whether the flag currently changes read behavior before
  finalizing the (already-cautious) UI copy.
- **Duplicate addresses** — does the backend reject duplicate tag addresses? If
  so, validation should block, not warn.

## M3+ (after M2) — bound by the plan-v2 UI punch list

The M1 review's UI requirements are codified in **plan v2 §"M1 mockup review — UI
punch list (binds M3)"** — read it before M3. Highlights: task-first copy (no
process language in the live UI); result-state card is design-only; Optimized DB
under Advanced; no T/C examples; Rack/Slot hints; tag table default columns
Name·Address·Datatype·Scan·**Status**·Actions with Unit/Scale/Offset behind a
per-row "More"; "Address suggests:" copy; datatype auto-fill only while untouched;
shorter error copy; test-read button states; **remove the CSV affordance in prod
v1**; operator-facing save-bar copy.

M4: flip `SourceProtocolPickerModel` s7 → Available + picker test (only after M3
green). M5: tiered verify — **required non-hardware bar** in plan v2 §M5
(config-apply + reload/edit + adapter Running + invalid-blocks-Save + mocked
IS7Client + parser matrix); simulator/PLC preferred; record the tier in the
final handoff.

## Build / environment notes

- Building the **Management** project requires the operator's gateway
  (`ElpisEdgeConnect.Management.exe`) **stopped** (build lock on its bin DLLs).
  Ask the operator to stop it, or `taskkill` the PID — config + buffer are
  durable on disk.
- `dotnet test … --no-build` runs against the **test project's stale copy** of a
  freshly-built dependency dll — always run `dotnet test` **with build** after
  editing a referenced project, or the change won't be exercised.
- Adding a config field regenerates two snapshots: the bundle schema snapshot
  (`ConfigSchemaModelTests` golden file) and watch the `ConfigurationDiffer` for
  collection-property reference-equality (record `Equals`); S7 adds no new
  GatewaySettings field, so this shouldn't recur — but the tag list is a
  collection, so apply the same care if any new config record gets a list.

## Deferred (NOT in v1)

`S7TagCsvImporter` (v1.1, schema `Name,Address,Datatype,ScanRateMs,Unit,Scale,
Offset`); S7 demo mode; optimized-DB walk.

## Reference

- **Plan v2** (`2026-06-02-s7-source-wizard-plan-v2.md`) — the build contract + UI punch list
- Mockup `2026-05-30-ux-mockups/8-s7-source-wizard.html`
- Backend `Sources.S7/{S7SourceConfiguration,S7ConnectionKeys,S7TagDefinition,
  S7Address(+Parser),S7Datatype,IS7Client,S7ConnectionManager,S7SourceAdapter}.cs`
- ADR-0015 wizard contract
