# 2026-05-27 — Follow-up chip prompts

Captured per user-memory convention: every `spawn_task` chip must also be committed to a markdown doc, because CCD chip notifications block the user's main window and dismissed chips don't reappear.

---

## Chip 1 — Warn against 0.0.0.0 in OPC UA endpoint URL

**Discovered during M.2d.4 smoke testing.** The OPC UA Server wizard default endpoint URL uses `0.0.0.0` as the host. UaExpert and other OPC UA clients cannot connect to `opc.tcp://0.0.0.0:4840/edgeconnect` — they hit `BadCommunicationError`. `0.0.0.0` is a bind address, not a connect address.

### Context for the spawned session

In `src/ElpisEdgeConnect.Management/Wizards/OpcUaServerSinkWizardModel.cs`, the default `EndpointUrl` is `"opc.tcp://0.0.0.0:4840/edgeconnect"`. This is a bind address — fine for the server to listen on. But this same URL is also what OPC UA clients see during the discovery handshake, and most OPC UA clients cannot connect to `0.0.0.0:4840` from outside the server — they get BadCommunicationError.

Two possible fixes:

**Option A — Wizard warning:** Add a validation Warning (non-blocking) when the endpoint URL host resolves to `0.0.0.0` or `*`. Surface via `WizardValidationBanner` per ADR-0015 Rule 5 with kebab-case anchor `#field-endpoint.url`. Copy: "Endpoint URL uses 0.0.0.0 as host. Clients (e.g. UaExpert) cannot connect to 0.0.0.0 — use the machine's hostname or IP. 0.0.0.0 is only a server-side bind address."

**Option B — Split bind from advertised host:** Two fields — `BindAddress` (0.0.0.0 is fine here) + `AdvertisedHostname` (the hostname clients see during discovery, defaulting to `Dns.GetHostName()` or `localhost`). This requires Core-side adapter changes — `OpcUaServerConfiguration` would need both fields, and `OpcUaServerSinkAdapter.cs` would need to bind on one and advertise the other.

**Recommendation:** Option A as a follow-up wizard pass (wizard-layer only, small change), Option B as a tracked enhancement for the OPC UA Server runtime itself.

---

## Chip 2 — "Connect a device" guided flow (one wizard, one Apply)

**Discovered during real-world smoke-testing with the user.** The current bootstrap UX requires three separate wizards + three Apply ceremonies + tab-jumping to land a single Source→Sink→Route pipeline. Operators doing the common "I want to connect a new device" task end up bouncing between 4 pages.

### Context for the spawned session

UX problem to solve: the current bootstrap flow for a new device requires the operator to:

1. Add Source wizard → Save draft → Config page → Validate → Apply
2. Add Destination wizard → Save draft → Config page → Validate → Apply
3. Add Route wizard → Save draft → Config page → Validate → Apply
4. Sometimes: manually Enable each one if the wizard saved with `Enabled=false`

That's 6–9 clicks across 4 pages to wire one pipeline. Most operators are doing all three in one session — three Apply ceremonies is overhead.

### Design — new guided "Connect a device" flow at `/connect`

Multi-step wizard sequence:

- **Step 1:** Pick source protocol (re-use `ChooseSourceProtocol.razor`'s picker cards)
- **Step 2:** Pick destination protocol (re-use `ChooseDestinationProtocol.razor`'s picker cards)
- **Step 3:** Configure source (re-use the protocol-specific source wizard's sections, embedded as steps)
- **Step 4:** Configure destination (re-use the protocol-specific sink wizard's sections, embedded as steps)
- **Step 5:** Configure route (auto-populated: source = step-3 instance, sink = [step-4 instance], all tags wildcard, default buffer/delivery)
- **Step 6:** Review summary + ONE Apply button
- **Step 7:** Success screen with live tag counter

### Key design constraints (from ADR-0015 and ADR-0014)

- Re-use the existing protocol wizard primitives (`WizardShell`, `WizardSection`, `WizardValidationBanner`, `WizardActions`). Each step IS one of the existing wizards, embedded.
- The ONE Apply creates source + sink + route as a single draft and applies it atomically. `WizardConfigMerger` needs a new `BuildBundledDraft(source, sink, route)` method.
- All three entities land `Enabled=true` (no defensive `Enabled=false` override — wiring is explicit, route exists, no fault risk).
- Existing single-entity wizards stay untouched. Operators editing one thing later use them. The guided flow is the new-device / first-run path.
- Plan trail under `docs/sessions/<date>-guided-connect-flow-plan*.md` per the project's planning cadence (v1→v2 with locked decisions).
- ADR-amendment may be needed: ADR-0015 currently describes single-entity wizards. If the guided flow is a new "wizard kind," document it in an ADR-0016 or as an amendment to 0015.

### Open questions to surface in v1 plan

- **Q1** Do we re-use the existing Add wizards as embedded steps, or duplicate-and-tailor? Re-use is DRY but adds parameterisation complexity.
- **Q2** What does the URL look like — `/connect`, `/onboard`, `/quick-start`, `/devices/new`?
- **Q3** Does the empty-state Overview page suggest this flow ("No sources configured. [Connect a device]") instead of the current "[Add source]"?
- **Q4** For protocols where Test Connection is supported, does the guided flow run it automatically as part of the step, or leave it as an opt-in click?
- **Q5** Does the guided flow support multiple sinks (Source → MQTT + OPC UA together)? Or strictly 1-to-1 for v1?

### Reference docs

- ADR-0015 wizard contract: `docs/decisions/0015-wizard-contract.md`
- ADR-0014 config vs runtime state: `docs/decisions/0014-config-state-vs-runtime-state.md`
- M.2d.4 v2.1 plan: `docs/sessions/2026-05-27-m2d4-cross-wizard-sweep-plan-v2.1.md`
- Existing single-entity wizards: `src/ElpisEdgeConnect.Management/Components/Pages/{SourceWizards,SinkWizards,RouteWizards}/`

### Size

Roadmap-level — probably 3–5 days work. v2 plan with locked decisions should be reviewed by the user before implementation.
