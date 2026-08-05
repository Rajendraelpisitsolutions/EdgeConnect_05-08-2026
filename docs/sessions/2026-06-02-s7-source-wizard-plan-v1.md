# Siemens S7 source wizard (M.2b.2) — implementation plan v1

**Date:** 2026-06-02
**Status:** Plan v1 — DRAFT for ChatGPT review pass (→ v2). Not yet started.
**Governing:** ADR-0015 (wizard contract), CLAUDE.md §8 (S7 is backend-only,
wizard Pending), the MTConnect wizard plan-trail as a process template.
**Standing prefs enforced:** static HTML mockup → operator sign-off BEFORE any
UI wiring; plan-trail (v1 → review → v2); pause-and-report tradeoffs.

## Goal

Make **Siemens S7 operator-available**: build the `AddS7Source` wizard, flip the
`SourceProtocolPickerModel` tile from **Pending → Available**, and verify an
operator can add an S7 source end-to-end. The backend (`Sources.S7`: adapter,
`Sharp7Client`, connection manager, config, address parser) + Host DI
(`S7RegistrationExtensions`, wired in `EdgeConnectComposition`) already exist and
are tested — this is purely the missing Studio surface.

## The key framing: S7 is a Modbus-style manual editor, NOT a browse wizard

Unlike MTConnect (browse-driven semantic onboarding) or OPC UA (dataItem
picker), **an S7 PLC cannot self-describe** — there is no browse. The operator
must **manually enter tag addresses** in Siemens absolute-address syntax
(`DB1.DBW0`, `MB4`, `IW2`, `Q0.1`, `DB5.DBX0.1`, …). This is exactly the
**Modbus wizard pattern** (manual register table), which is already
operator-available. So the playbook is **Modbus, not MTConnect**:

- Template UI: `Management/Components/Pages/SourceWizards/AddModbusSource.razor`
- Template model: `Management/Wizards/ModbusSourceWizardModel.cs`
- Bulk-entry precedent: `Sources.ModbusTcp/Import/ModbusTagCsvImporter.cs`

## What the wizard must capture (from the real config shape)

**Connection** (`S7SourceConfiguration`): `Host`, `Port` (102), `Rack`, `Slot`
(1), `ConnectionType` (Basic/…), `OptimizedDbAccess` (bool — S7-1200/1500 with
optimized DBs), and the standard timeouts/retry/circuit-breaker knobs (sensible
defaults; advanced-collapsed).

**Per tag** (`S7TagDefinition`): `Name` (required), `Address` (required string,
e.g. `DB1.DBW0`), `Datatype` (e.g. int16/real/bool), `ScanRateMs` (1000), and
optional `Unit` / `Scale` / `Offset`. The structured `S7Address`
(Area/DbNumber/ByteOffset/BitOffset/WidthHint) is produced by
`S7AddressParser.Parse(string)` — so the operator types the human address and we
parse+validate it.

## Wizard design (mirrors Modbus, adds S7-specific validation)

1. **Connection section** — Host/Rack/Slot/Port/ConnectionType/OptimizedDbAccess
   + advanced (collapsed) timeouts. A **"Test connection"** button (S7 can
   connect even though it can't browse — `IS7Client`/`S7ConnectionManager`): a
   round-trip connect to host:port rack/slot, reporting reachable / refused /
   timeout. This is the *only* pre-save validation S7 gets, so it's high value.
2. **Tag editor section** — a table (add/remove rows): Name · Address · Datatype
   · Scan (ms) · Unit · Scale · Offset. **Live address validation**: as the
   operator types `Address`, parse via `S7AddressParser` and show the parsed
   interpretation inline (`DB1 · Word · byte 0`) or a red error. The address
   width hint can **auto-suggest the datatype** (DBW→int16/word, DBD→dint/real,
   DBX→bool) — explain, don't force.
3. **Save** — through the normal draft → validate → apply → rollback flow
   (`WizardConfigMerger` / `RouteWiring`), never a direct write.

## Milestones (mockup-first; mirrors MTConnect M1–M5)

| M | Deliverable | Gate |
|---|---|---|
| **M1** | Static HTML mockup of the S7 wizard (connection + manual tag editor + live address-validation preview + Test-connection + optional CSV import affordance). Operator sign-off. | **Sign-off** |
| **M2** | Backend glue: `S7SourceWizardModel` POCO + round-trip mapping to `SourceInstanceConfig` + tests (mirror `ModbusSourceWizardModel`). An address-validation surface (see Q3) and a Test-connection service/endpoint (mirror `ModbusProbeService` / `MqttTestConnection`). | Tests green |
| **M3** | `AddS7Source.razor` (connection + tag-editor + live validation + Test-connection) + edit support (`SourceEditRouter` +s7 case). | Build/tests green |
| **M4** | Flip `SourceProtocolPickerModel` s7 tile → **Available** + picker test; confirm it appears in the onboarding flow (or standalone, per OPC-UA-client precedent). | Tests green |
| **M5** | End-to-end verify (see Q5) + full build/tests + handoff. | **Done** |

## Open questions for the review pass (v1 → v2)

- **Q1 — CSV bulk import?** S7 deployments often have dozens–hundreds of tags;
  hand-typing is painful. Modbus has `ModbusTagCsvImporter`. Build an S7 CSV
  import for v1 (Name,Address,Datatype,Scan,…) or defer to a follow-up? *Lean:
  defer to v1.1 — ship the manual editor first; CSV is additive.*
- **Q2 — Datatype auto-suggest from the address width?** Suggest int16 for
  `DBW`, real/dint for `DBD`, bool for `DBX`, etc. *Lean: yes — it's the kind of
  "explain, don't hide" help that makes a manual editor tolerable; operator can
  override.*
- **Q3 — Where does live address validation run?** Client-side is impossible
  (`S7AddressParser` is server-side C#). Options: (a) a small validate endpoint
  the editor calls per address (debounced), or (b) validate the whole tag list
  on Save only. *Lean: (a) per-field validate endpoint for live feedback —
  matches the Modbus editor's inline validation.*
- **Q4 — Test-connection depth.** Just TCP+ISO-on-TCP connect (reachable), or
  also a trial read of one tag to confirm rack/slot/DB access? *Lean: connect +
  optional single trial read if the operator has entered ≥1 tag.*
- **Q5 — How to verify end-to-end (M5) without a guaranteed S7 PLC?** Options:
  (a) a Snap7 **server simulator** (Sharp7 has a server side / Snap7 server) to
  stand up a fake S7 with a couple of DBs; (b) a real S7-1200/1500 if available;
  (c) minimum bar — wizard → config apply → adapter goes Running and attempts
  connection (proving the wizard produces a valid, adapter-consumable config).
  *Need an operator steer: is an S7 PLC/sim available, or is (c) the bar?*
- **Q6 — Demo mode?** Like FOCAS2/MTConnect demo mode, an S7 demo (static
  config + synthetic values) would help sales/dev with no PLC. Defer (follow-up).

## Risks

- **No browse = data-entry burden.** Mitigations: live address validation +
  datatype auto-suggest (Q2/Q3), and CSV import as the fast-follow (Q1). The
  mockup must make the manual editor feel guided, not raw.
- **Verification realism** (Q5) — without a PLC/sim the e2e proof is weaker than
  MTConnect's (which had demo.mtconnect.org). Decide the bar up front.
- **Address-syntax coverage** — confirm `S7AddressParser` covers the address
  forms the mockup offers (DB bit/byte/word/dword, M/I/Q, T/C) so the editor
  never accepts a form the adapter can't read. (Quick test-matrix in M2.)

## Reference

- ADR-0015 wizard contract; MTConnect plan-trail (`2026-05-31-mtconnect-source-wizard-plan-*`) as the process template
- Backend: `Sources.S7/{S7SourceConfiguration,S7TagDefinition,S7Address,S7AddressParser,IS7Client,S7ConnectionManager}.cs`
- Templates: `AddModbusSource.razor`, `ModbusSourceWizardModel.cs`, `ModbusProbeService.cs`, `ModbusTagCsvImporter.cs`
- Tile: `Management/Wizards/SourceProtocolPickerModel.cs` (s7 → Pending today)
