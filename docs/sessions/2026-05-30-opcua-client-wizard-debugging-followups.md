# 2026-05-30 — OPC UA Client wizard debugging session: follow-ups

After PR 7c-4 landed and the OPC UA Client wizard shipped end-to-end (Add
+ Edit + Browse + Test Connection + Save), a live-debugging session with
the user surfaced **six real product gaps**. None of them block the
adapter series itself — the v1 OPC UA Client adapter is functional and
the wizard works — but each one cost the user real triage time during
the first integration attempt (3rd-party OPC server → EdgeConnect OPC
Client source → route → EdgeConnect OPC UA Server sink → UaExpert).

This doc captures each gap, its root cause, the moment it surfaced
during the session, and the recommended next step. Each is also tracked
as a backlog task in `.claude/projects/.../tasks.json`.

## Branch dependency note

This handoff lands on `feat/opcua-client-wizard` (PR #65). Until PR #65
+ its cascade (#62 → #63 → #64 → #65) merge to master, anyone starting
fresh from master will not see this doc. **Verify merges have landed
before pointing a cold session at it.**

---

## Hotfixes already shipped (informational — no follow-up needed)

These four ran out of the live debugging session in real time. Listing
them for narrative completeness; each is in the PR #65 commit history.

| # | Bug | Commit |
|---|-----|---|
| 1 | `WizardActions` parameter names — passed `Saving` / `SaveDisabled` instead of `Busy` / `CanSave` | `7da772e` |
| 2 | Browse `SourceConfigJson` shape — sent inner Connection JsonElement only; deserialiser needed full config | `7da772e` |
| 3 | Save endpoint path — used `/api/v1/config/current` (made up); real path is `/api/v1/config` | `98ceb59` |
| 4 | Wiring section UX — free-form route-id text field that bypassed Modbus's `none` / `newRoute` radio pattern, surfaced engineer-language `BuildNewSourceDraft` validator errors | `9839311` |
| 5 | **Eager startup registration missing** — `EdgeConnectComposition` didn't call `AddOpcUaClientSourcesFromGatewayConfig`. Wizard worked while process stayed up (hot-reload uses `RegistrationFactory` dispatcher which WAS wired), but fresh restart skipped opcua-client sources entirely → route's `MISSING_SOURCE` fault | `533c764` |
| 6 | **`SecurityPolicyUri` not honoured** — `DefaultOpcUaClientConnectionEstablisher` passed only `useSecurity: bool` to `CoreClientUtils.SelectEndpoint`. Config's `securityPolicyUri` field was stored but never consulted; OPC stack picked the highest-security endpoint the server advertised. Could land on `Aes256_Sha256_RsaPss` even when config said `Basic256Sha256`, breaking cert trust on simulators that only trusted the client cert for the requested policy slot. | `3a3366b` |
| 7 | **Per-MonitoredItem status silently swallowed** — subscription factory called `subscription.ApplyChanges` but never inspected `MonitoredItem.Status.Error`. Items rejected server-side (`BadNodeIdUnknown` / `BadUserAccessDenied` / `BadSecurityChecksFailed`) looked identical to healthy quiet subscriptions. | `33e3f15` |
| 8 | **🔥 SourceSupervisor never called `SubscribeAsync` — THE actual data-flow blocker.** Adapter advertised `SourceCapabilities.Subscription` since PR 1; supervisor's pump unconditionally called `adapter.PollAsync(ct)`, which OPC UA Client throws `NotSupportedException` on. Exception wasn't an `AdapterException`, so the loop terminated silently. Notifications arrived from the OPC stack into the adapter's bounded channel and **sat there forever with no consumer**. State stayed Running, `pointsObserved=0`, zero error in the UI, hours of bisecting consumed. | `8c3778b` |

The user bisected #5 with one sentence — *"If license is the problem,
how it worked before our edit to 'none'?"* — that immediately ruled out
license gate (which fires on both paths) and pointed at something only
the restart path runs. Worth pinning as a debugging exemplar.

The user surfaced #8 by sharing the host console output that showed
the `NotSupportedException` stack trace on shutdown. Without that one
log line, this would still be unsolved. **The crash on stop saved us
from a silent "everything looks fine but no data" failure mode that
could have shipped to production.** Worth a debugging exemplar of its
own: *always read the stop-time logs even when the start-time logs
look healthy*.

---

## Backlog items the session surfaced

### 1. New-protocol adoption checklist + startup self-check

**Symptom.** PR 7c-3 shipped `OpcUaClientSourceConfiguration.FromSourceInstance`
+ `OpcUaClientRegistrationExtensions` + `RegistrationFactory.BuildSource`
dispatch case — three of four required pieces. The fourth — the eager
call in `EdgeConnectComposition.cs` alongside the other five protocols
— was missed. The hot-reload path worked (because it uses the
dispatcher) so live testing inside one process never triggered the bug.
Only a fresh restart did.

**Root cause.** No checklist or test pins the four-piece protocol
adoption requirement. Each protocol's wiring is correct only because
contributors copy-pasted from the previous protocol.

**Proposal.**

- ARCHITECTURE_BLUEPRINT.md addition documenting the four pieces with
  pointers to file paths to mirror
- Startup self-check that walks `gatewayConfig.Sources` and for each
  unique `ProtocolName`, asserts that `RegistrationFactory` knows about
  it AND at least one `Add*FromGatewayConfig` extension has been called.
  Mismatch → loud `[startup]` warning. Cheap insurance.

**Tracked as:** task #49.

### 2. Per-wizard razor smoke test convention

**Symptom.** Four distinct razor wiring bugs (#1–#4 above) all surfaced
only at user runtime, all with the model-level tests passing. The
bUnit-free testing convention this codebase uses tests model behaviour
but never renders the razor.

**Proposal.** Minimal per-wizard smoke test:

1. Render the component (`bUnit` `TestContext.RenderComponent<T>()`)
2. Drive `OnSave` via a click on the WizardActions Save button
3. Assert against a fake `HttpMessageHandler` that the right
   endpoint + JSON shape went out

~50 LOC per wizard. Would have caught every razor-side regression in
this session — the WizardActions param-name throw, the Save endpoint
404, the empty-sinks-list 500.

**Tracked as:** task #50.

### 3. Surface per-MonitoredItem status from the OPC server

**Symptom.** Adapter reported `AdapterState=Running` and
`monitoredItemsActive=N` (matching configured) with zero
`notificationsReceived` for ~hour of debugging. Turns out the simulator
had dormant tags (`Tag1` / `Tag2`) that exist as Variable nodes but
aren't driven — every subscription publish came back empty. There was
no observable signal distinguishing "subscribed but never firing" from
"healthy with rare changes".

**Proposal.** After `subscription.Create()`, iterate
`subscription.MonitoredItems` and read `item.Status.Error` per item. If
any return `Bad*` (most notably `BadNodeIdUnknown` or
`BadAttributeIdInvalid`), raise an `OPCUA.MONITORED_ITEM_REJECTED`
adapter error with the offending NodeId list. Surface
`monitoredItemsWithBadStatus` as a new health metric distinct from
`monitoredItemsActive`.

Adapter scope: ~50 LOC + 2–3 tests. Locked-behaviour candidate.

**Tracked as:** task #51.

### 4. Split `CONFIG.ROUTE_REFERENCES_MISSING_SOURCE` into sub-codes

**Symptom.** This single fault code is registered when a route's
`sourceInstanceId` doesn't match any registered source. The message
text reads *"may be disabled, faulted, or absent from the config"* —
three different remediation paths under one code. The session spent
nontrivial time chasing the wrong cause because the message didn't
distinguish "license-skipped" (the actual cause initially suspected)
from "eager-registration not wired" (the actual cause).

**Proposal.**

- `CONFIG.ROUTE_REFERENCES_LICENSE_SKIPPED_SOURCE` — license module disabled
- `CONFIG.ROUTE_REFERENCES_FAULTED_SOURCE` — registration extension caught an error
- `CONFIG.ROUTE_REFERENCES_DISABLED_SOURCE` — source is in config but `Enabled=false`
- `CONFIG.ROUTE_REFERENCES_ABSENT_SOURCE` — source isn't in config at all

The route validator already has the information needed to discriminate
— it can inspect the registered-sources list, the fault registry, and
the original config. The four sub-codes carry their own remediation
hints.

**Tracked as:** task #52.

### 5. OPC UA cert-trust UX surfacing in Studio

**Symptom.** When SecurityMode is `SignAndEncrypt`, the client cert
must be trusted by the server. Most OPC servers (including the
simulator the user is testing against) silently drop the cert into a
`rejected/` folder on first connect. Operator has no way to know this
from the Studio — they see `Running` + zero notifications + no errors.

**Proposal.**

- On every successful `Session.Create()` against a `Sign*` endpoint,
  log a one-time hint to the SourceDetail page: *"If subscriptions
  don't fire: your client certificate (subject CN=urn:elpis:edgeconnect:opcua-client)
  may need to be moved from `rejected/` to `trusted/` in the server's
  PKI store."*
- When the server returns `BadCertificateUntrusted` during a reconnect,
  capture that explicitly into `_lastError` instead of the generic
  `OPCUA.CONNECT_FAILED`.

**Tracked as:** task #53.

### 6. SourceDetail page — surface protocol-specific metrics

**Symptom.** The SourceDetail page currently shows only generic
`pointsObserved` / `lastPoint`. The OPC UA Client adapter publishes 14
diagnostic metrics via `CheckHealthAsync` (subscriptions / monitored
items active vs configured, all four notification counters, queue
depth, reconnect counters and timestamps, reconfigure counters). They're
reachable via `GET /api/v1/sources/{id}/health` but not rendered.

A protocol-aware metrics panel — even a JSON dump pretty-printed —
would have turned today's hour-long bisect into a 30-second triage.
The exact metric the operator needed (`notificationsReceived`) wasn't
visible in the UI at all.

This likely applies to every adapter, not just OPC UA Client. Modbus,
Focas2, Brother HTTP each publish their own per-protocol metrics that
the UI doesn't surface.

**Proposal.** Add a "Diagnostics" expansion panel on SourceDetail that
calls `/api/v1/sources/{id}/health` and renders the `Metrics`
dictionary as a key/value table. Phase 1 — generic table. Phase 2 —
per-protocol nicely-grouped layouts.

**Tracked as:** task #54.

---

## Severity / ordering recommendation

| Priority | Item | Why |
|---|---|---|
| **High** | #1 New-protocol adoption checklist + self-check | One missing line in PR 7c-3 hid for 4 PRs of work because no test pins it. The exact same trap will catch the EtherNet/IP and MELSEC adapters when they land. |
| **High** | #6 Adapter-specific metrics panel | Closes 80% of "is data flowing?" triage. Reusable across every protocol. |
| Medium | #3 Per-MonitoredItem status | Closes the most common OPC UA "configured but silent" failure mode. ~1 day of work. |
| Medium | #4 Fault-code sub-codes | Stops the "is it license, faulted, or missing?" guessing game. ~1 day. |
| Medium | #2 Per-wizard smoke test | Long-tail durability. Pays dividends every time a wizard ships. |
| Lower | #5 OPC UA cert-trust hint | Specific to one protocol; the metrics panel + per-item status would together get most of the way there. |

## Cross-cutting observation

The four wizard runtime bugs (#1–#4 in the hotfixes section) plus the
missing eager-registration line (#5) shared a structural cause:
**model-side test coverage is comprehensive; integration-side test
coverage is sparse**. The bUnit-free convention assumes razor wiring
mistakes will be caught at code review. Five separate review passes
across PR #62 through PR #65 missed them all. That's a process signal,
not a "be more careful in review" signal — the harness needs to catch
this class.

Tasks #1, #2, #6 together close that gap. Worth treating as a coherent
testing-strategy ADR rather than three independent items.
