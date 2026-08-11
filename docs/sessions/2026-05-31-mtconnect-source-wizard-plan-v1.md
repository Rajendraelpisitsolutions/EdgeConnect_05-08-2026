# MTConnect source wizard — implementation plan v1 (M.2b.4)

**Date:** 2026-05-31
**Goal:** make the **MTConnect** source protocol *operator-shippable* — i.e. flip
its add-source wizard tile from **Pending** to **Available**
(`SourceProtocolPickerModel`). The MTConnect *adapter* (collection, polling,
stream parse, redaction rules, Host DI registration, tests) already exists; what's
missing is the Studio onboarding wizard and its discovery backend.

**Cadence (this user's standing process):**
1. This is **plan v1** → user reviews with ChatGPT → I fold feedback into **v2**.
2. **Static HTML mockup first.** Build and get sign-off on a static HTML mockup of
   the wizard (shared `_styles.css`) *before* wiring any Blazor — same gate we used
   for the diagnostic bundle (G3).
3. Only after mockup sign-off do we touch the wizard component.

---

## 1. Why MTConnect first (vs S7)

| | MTConnect | Siemens S7 |
|---|---|---|
| Connection | one `agentBaseUrl` (HTTP) | host/rack/slot + tuning |
| Self-describing? | **Yes** — `/probe` lists every device + dataItem | No — symbol table lives in the TIA/Step7 project |
| Wizard shape | **browse-driven** (discover → pick), mirrors OPC UA Client / FOCAS2 | manual tag-address editor (DB#, type, byte offset) — bespoke, error-prone |
| Backend reuse | `IMTConnectClient.GetProbeAsync()` already exists | connect-probe possible, no discovery |

MTConnect reuses a proven wizard pattern and existing backend plumbing → faster to a
quality ship. S7 stays Pending (M.2b.2) for a later pass.

## 2. What already exists (confirmed in tree)

- **Adapter** `src/ElpisEdgeConnect.Sources.MTConnect/`: `MTConnectSourceAdapter`,
  `MTConnectHttpClient` (`GetProbeAsync` → raw `/probe` XML; `GetAsync("current"/"sample")`),
  `MTConnectStreamParser` (parses the **/sample|/current** stream, NOT /probe),
  `MTConnectTagMap` (`MTConnectTagMapEntry { TagName, TagPath, ValueType, Unit?, Description? }`),
  `MTConnectSourceConfiguration` (`AgentBaseUrl` required, `AgentDeviceName?`, timeouts/backoff),
  redaction rules, Host registration, adapter tests.
- **Gap:** `TryProbeAsync` only extracts device `uuid`/`manufacturer`. There is **no
  parser that walks `/probe` into the full dataItem tree** — that is net-new and is
  the heart of the discovery backend.

## 3. Reference pattern (what we mirror)

The **OPC UA Client** browse wizard is the closest analog:
- Backend: `OpcUaClientBrowseApiService.BrowseAsync` + `POST /api/v1/sources/browse/opcua-client`
  (request `{ SourceConfigJson, … }` → outcome `{ Status, Result }`), with a
  `…BrowseStatus` enum and a status→HTTP-code mapping (`OpcUaClientBrowseStatusMapping`).
- UI: `AddOpcUaClientSource.razor` (~894 lines) — connection step → browse → select
  nodes → name tags → routing → save. Plus a testable wizard-model POCO
  (cf. `Focas2SourceWizardModelTests`, `SourceProtocolPickerModel`).

## 4. Operator flow (the wizard UX — to be drawn in the mockup)

1. **Connect** — enter Agent base URL (e.g. `http://agent.local:5000`), optional
   device name, timeout. "Test & discover" button.
2. **Discover** — call the browse endpoint → `/probe` parsed → show the device →
   components → dataItems tree. Group by category (**Sample** / **Event** /
   **Condition**), show id, type, subType, units.
3. **Select** — operator ticks the dataItems to ingest (select-all / by-category
   helpers). Default tag name derived from dataItem `name`/`id`; editable. Default
   `ValueType`/`Unit` inferred from the dataItem.
4. **Review & route** — name the source, optionally attach a route, confirm.
5. **Save** — persists through the **draft → validate → apply** flow (never bypass
   it, per CLAUDE.md anti-pattern #10), same as the other source wizards.

## 5. Work breakdown

### M1 — Static HTML mockup (sign-off gate) ⟵ first
- `docs/sessions/2026-05-30-ux-mockups/7-mtconnect-source-wizard.html` (shared
  `_styles.css`). Show all steps, the dataItem discovery tree (Sample/Event/Condition
  grouping), tag-naming defaults, and the empty/error states (agent unreachable,
  no dataItems). **Pause for user sign-off before M2/M3.**

### M2 — Discovery backend
- **`/probe` parser** (net-new): walk `MTConnectDevices → Device → (DataItems |
  Components→…→DataItems)` recursively; emit a flat list of discovered dataItems
  `{ DeviceName, Path, DataItemId, Name?, Category, Type, SubType?, Units? }`.
  Namespace-agnostic (default-namespace aware, like `TryProbeAsync`).
  **Open: where does this parser live?** (see Q-1).
- **Browse service + API:** `POST /api/v1/sources/browse/mtconnect` accepting
  `{ agentBaseUrl, agentDeviceName?, timeoutSeconds? }` (or `SourceConfigJson` to match
  OPC UA), calling `GetProbeAsync` → parse → return `{ status, devices[…dataItems] }`.
  Status enum + status→HTTP mapping mirroring OPC UA (Reachable / Unauthorized /
  Unreachable / Timeout / InvalidResponse). DTOs are Management-owned (OpenAPI
  independence, as in OPC UA).
- Tests: parser unit tests over captured `/probe` fixtures (incl. nested components,
  conditions, multiple devices); browse-service status-mapping tests.

### M3 — Wizard UI
- `AddMTConnectSource.razor` (browse-driven, mirroring `AddOpcUaClientSource`):
  steps from §4; calls the M2 endpoint; builds the `connection` + tag map; saves via
  draft/apply. Edit support wired through `SourceEditRouter`.
- Testable wizard-model POCO (`MTConnectSourceWizardModel`) + model tests
  (cf. `Focas2SourceWizardModelTests`): default tag naming, category handling,
  validation (URL required, ≥1 dataItem selected).

### M4 — Flip the tile
- `SourceProtocolPickerModel`: `mtconnect` → `Available`, `TargetHref =
  "/sources/new/mtconnect"`, drop `PendingMilestone`. Update the picker-model test,
  the `OnboardingFlow.razor` comment, and any "coming soon" copy. **S7 stays Pending.**

### M5 — End-to-end verify + close
- Drive the wizard against a real/demo agent (see Q-2): discover → select → save →
  confirm the source comes up and emits canonical points to a sink.
- Full build + tests green (0 warnings/errors); session handoff doc; update
  `CLAUDE.md` §8 to move MTConnect to operator-available.

## 6. Open questions for review (ChatGPT pass)

- **Q-1 — Probe parser location.** Put the `/probe` parser in the **adapter assembly**
  (`Sources.MTConnect`, protocol knowledge lives with the protocol; expose via
  `IMTConnectClient` or a static `MTConnectProbeParser`) and have the Management
  browse service call it? Or keep it **in Management** (as in OPC UA, where browse logic
  is Management-side)? Need to confirm whether `Management` already references
  `Sources.MTConnect` (the redaction rules registration suggests Host does, not
  necessarily Management). Lean: parser in the adapter (reusable, testable, correct
  layering), DTOs + service in Management.
- **Q-2 — Test/demo agent.** Is there a reachable MTConnect agent for end-to-end?
  Options: public demo (`https://demo.mtconnect.org`), the NIST SMS testbed, a local
  `mtconnect-agent`, or add a **demo mode** to the adapter like FOCAS2 had
  (`Focas2DemoMode`) so sales/dev can exercise the wizard with no hardware. Demo mode
  is the most self-contained — worth its own small milestone if we want it.
- **Q-3 — Condition dataItems.** MTConnect `Condition` dataItems emit
  Normal/Warning/Fault (not scalar samples). Do we ingest them in v1 (mapped to a
  status/string canonical point) or filter them out of the selectable list with a
  "conditions not yet supported" note? Affects the parser + tag map + mockup.
- **Q-4 — Tag-map editing depth.** How much per-tag editing in the wizard (just name,
  or name + valueType + unit override)? Sensible defaults from the dataItem vs full
  editor. Keep v1 lean (name editable, type/unit inferred, read-only) or allow override?
- **Q-5 — Selection ergonomics.** Select-all / by-category / by-component helpers;
  default-selected set (all Samples+Events, Conditions off) vs nothing selected.

## 7. Risks / notes

- The wizard component will be large (~OPC UA's ~900 lines). Keep logic in the
  testable POCO; the `.razor` stays a thin shell.
- `/probe` documents vary across agent versions/vendors — fixture-driven parser tests
  with real captured docs are essential.
- Don't bypass draft→validate→apply on save (anti-pattern #10).
- No Core changes expected; this is Adapter (parser) + Management (browse + wizard) +
  one picker-model flip.

## 8. Proposed sequence

M1 (mockup + sign-off) → M2 (backend, reviewable on its own) → M3 (wizard) →
M4 (tile flip) → M5 (verify + close). M2 can start in parallel with M1 sign-off since
it has no UI, but the **tile flip (M4) is the last step** — nothing goes Available
until the wizard is verified end-to-end.
