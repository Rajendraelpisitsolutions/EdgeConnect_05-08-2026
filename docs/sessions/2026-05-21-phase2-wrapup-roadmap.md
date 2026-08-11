# Phase 2 wrap-up — roadmap (v1 plan)

**Status:** v1 — DRAFT, OPEN QUESTIONS BELOW, pending ChatGPT review pass.
**Date:** 2026-05-21
**Scope:** four pre-deployment items (Chip 3, Chip 4, Chip 5, EREMOS V2 revalidation) + three operator-selected optionals (M.2c Live Tag Watch, offline-scenario parity test, M.2d Edit-via-Wizard sweep).
**Predecessor state:** master at `a1ea1aa` (PR #16 + PR #19 merged this session). 2263 tests across 12 projects. Phase 2's data-path foundation complete; this roadmap covers the remaining work between today and the 7-day in-house soak gate.

---

## 1. Sequencing + dependencies

Items in implementation order, with their hard dependencies and whether they can run in parallel:

```
┌─────────── Tracks ────────────┐
│                                │
│  TRACK A (small fixes,         │
│  serial, ~2 days total):       │
│    Chip 4  →  Chip 5           │
│                                │
│  TRACK B (small, independent,  │
│  ~half day):                   │
│    Offline-scenario test       │
│                                │
│  TRACK C (substantial, plan-   │
│  trail discipline, ~1 week):   │
│    Chip 3 bulk-provision       │
│       ↓                        │
│    EREMOS V2 revalidation      │
│    (uses bulk-generated config)│
│                                │
│  TRACK D (substantial, ~2-3    │
│  weeks total):                 │
│    M.2c Live Tag Watch         │
│       ↓                        │
│    M.2d Edit-via-Wizard        │
│    (sweeps the new Live Tag    │
│    Watch UX into all wizards)  │
│                                │
└────────────────────────────────┘
```

**Hard dependencies:**
- Chip 5 → Chip 4 (locked by the original chip prompt: "Bug 1 fix must land first, otherwise wiring CONFIG_DIR would accidentally move buffer locations alongside config locations")
- EREMOS V2 revalidation → Chip 3 (revalidation harness uses a bulk-generated `gateway.json` to mirror customer-install posture)
- M.2d → M.2c (M.2d's wizard sweep needs to land the Live Tag Watch UX into the source/sink/route wizards; cleaner if M.2c lands first so M.2d picks up the new contract)
- 7-day in-house soak → Chip 3 + EREMOS V2 revalidation (soak profile uses bulk-generated config + real EREMOS V2 consumer per §7-Q8 lock)

**Soft / no dependencies:**
- Track A and Track B can both run alongside Track C and Track D
- Within Track A: Chip 4 first, Chip 5 immediately after
- Offline-scenario test (Track B) is independent of everything

**Recommended pacing:**
- **Sessions 1-2:** Track A + Track B in one session each (or combined — they're small)
- **Sessions 3-4:** Chip 3 plan trail (v1 → v2 → v3) + implementation
- **Session 5:** EREMOS V2 revalidation (uses Chip 3 output)
- **Sessions 6-9:** M.2c Live Tag Watch (substantial; full plan-trail discipline)
- **Sessions 10-12:** M.2d Edit-via-Wizard sweep (substantial; full plan-trail discipline)

Cumulative estimate: ~4-6 weeks of focused work before the 7-day soak starts.

---

## 2. Plan-trail discipline per item

Different items warrant different planning depth.

| Item | Plan-trail style | Why |
|---|---|---|
| Chip 4 (Bug 1 P3) | **Inline in this roadmap only** | Small, well-scoped in the original chip prompt. No open architectural questions. |
| Chip 5 (CONFIG_DIR) | **Inline in this roadmap only** | Small, mostly a delete/wire-through decision (Option A vs B in original chip). |
| Offline-scenario parity test | **Inline in this roadmap only** | Half-day, tests one specific lifecycle divergence. |
| Chip 3 (bulk-provision) | **Full v1 → review → v2 → reality-check → v3 in dedicated files** | Substantial; multiple open questions (PowerShell vs Python, CSV column layout, provenance format, schema validation strategy). |
| EREMOS V2 revalidation | **Brief v1 → v2 in dedicated files** | Small but novel — first time we exercise the real EREMOS V2 consumer; some integration-shape questions. |
| M.2c Live Tag Watch | **Full v1 → review → v2 → reality-check → v3 in dedicated files** | Substantial UX + data-path work; multiple open architectural questions (subscription model, scope, history vs current). |
| M.2d Edit-via-Wizard sweep | **Full v1 → review → v2 → reality-check → v3 in dedicated files** | Substantial; touches every wizard; needs to land after M.2c so the wizard contract reflects the new Live Tag Watch UX. |

The "inline only" items collapse into single commits with brief plan-and-go execution. The "full discipline" items follow the M.P2.4 cadence (separate v1/v2/v3 files under `docs/sessions/`, ChatGPT review pass between each).

---

## 3. Item-by-item plans

### 3.1 Chip 4 — Bug 1 P3 buffer path realignment

**Scope (from original chip prompt):** `DefaultRouteBufferFactory` is wired with `options.ConfigDirectory` at `CompositionRoot.cs:104`, but the class doc + blueprint §19.3 + the field name all say it should receive the **data root**. Buffer SQLite files end up at `<dataRoot>/config/buffer/` instead of `<dataRoot>/buffer/`. P3 not because the bug doesn't matter, but because backup/restore can work around it; the realignment is correctness + clean coupling with the EDGECONNECT_CONFIG_DIR follow-up (Chip 5).

**Deliverables:**

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Host/CompositionRoot.cs:104` | One-line fix: `options.ConfigDirectory` → `options.ResolvedDataRoot` |
| `src/ElpisEdgeConnect.Core/Routing/DefaultRouteBufferFactory.cs` | Add legacy-path migration shim before `SqliteBuffer.OpenAsync`: if `<dataRoot>/buffer/{routeId}.db` does NOT exist AND `<dataRoot>/config/buffer/{routeId}.db` DOES exist, move the `.db + .db-shm + .db-wal` triplet. Log `Information` on migration. |
| `tests/ElpisEdgeConnect.Core.Tests/Routing/DefaultRouteBufferFactoryTests.cs` | New migration test: pre-place a `.db` at `{dataPath}/config/buffer/`, call `CreateAsync`, assert file is now at `{dataPath}/buffer/` and openable. Test the `.shm + .wal` siblings move together. |
| `docs/ARCHITECTURE_BLUEPRINT.md` §19.3 | Verify already says `{dataPath}/buffer/{routeId}.db`; doc was correct, code was wrong — confirm no edit needed. |
| `docs/ops-runbook.md` | If exists, update backup/restore guidance to reference the canonical buffer location. |

**Open questions:**
- None major. Chip prompt + locked discipline + reality-check from M.P2.4 reading covers it.

**Risks:**
- The `.shm + .wal` triplet must move atomically. If only `.db` moves without siblings, SQLite WAL recovery on next open could corrupt data. **Mitigation:** the migration test explicitly pre-places all three files and asserts all three move.
- Existing deployments with pre-Chip-4 buffer files at the old path would silently lose data on upgrade if migration shim has a bug. **Mitigation:** test the migration shim against a populated `.db` + non-trivial `.shm` and `.wal` files; assert the database can be opened and queried after migration.

**Definition of done:**
- [ ] One-line `CompositionRoot.cs` fix lands.
- [ ] Migration shim handles `.db + .db-shm + .db-wal` triplet correctly.
- [ ] Migration test asserts pre-existing buffers move to canonical path on first open after upgrade.
- [ ] Full test sweep clean.
- [ ] Deployment-readiness §5 updated to mark Bug 1 as RESOLVED with the PR link.

**Estimate:** 1 commit, ~50 LOC + ~120 LOC tests. ~1 day.

---

### 3.2 Chip 5 — `EDGECONNECT_CONFIG_DIR` inertness resolution

**Scope (from original chip prompt):** `EDGECONNECT_CONFIG_DIR` is read into `HostOptions.ConfigDirectory` but nothing actually consumes that field for resolving the active config-file path. Setting it does nothing today.

**Locked decision needed before implementation: Option A vs Option B.**

| Option | What | When to pick |
|---|---|---|
| **A — Wire it through** | `HostOptions` gets a `ConfigOverridePath` field; `ConfigurationStorageLayout` honors it; Studio's `CurrentConfigVersionDto.Override` (M.2b.6.2 §3.B) reports both `EDGECONNECT_DATA_ROOT` and `EDGECONNECT_CONFIG_DIR`; startup-log banner updates. | Operators have a real need for a narrower config-only override (e.g., shared data root + per-instance config dirs). Worth ~3-4 hours of work. |
| **B — Delete the inert env-var read + the misleading `HostOptions.ConfigDirectory` field** | Document that `EDGECONNECT_DATA_ROOT` is the only path-controlling env var. ~30 minutes. | No operator has asked for finer-grained override. Simpler is better. **Recommended.** |

**Recommendation: Option B.** Per the §7 locked operator profile ("maintenance staff, not developers, not IT"), reducing the env-var surface area is more valuable than adding a finer override that nobody has asked for. If a customer ever needs it, we can add Option A in a future milestone.

**Deliverables (assuming Option B):**

| File | Change |
|---|---|
| `src/ElpisEdgeConnect.Host/EdgeConnectComposition.cs:95` | Remove the `EDGECONNECT_CONFIG_DIR` env-var read. |
| `src/ElpisEdgeConnect.Host/HostOptions.cs` | Remove or repurpose `ConfigDirectory` field. Note: `ResolvedDataRoot`'s fallback chain may reference it; trace + remove carefully. |
| Docs / inline comments referencing `EDGECONNECT_CONFIG_DIR` | Remove or replace with `EDGECONNECT_DATA_ROOT` references. |
| `tests/ElpisEdgeConnect.Management.Tests/ConfigApiConfigPathTests.cs` | Update tests that asserted `CONFIG_DIR` does NOT show up — flip them to assert `CONFIG_DIR` is no longer recognised at all. |
| Studio Config page override chip | Already only surfaces `EDGECONNECT_DATA_ROOT` (M.2b.6.2 §3.B locked this); no UI change needed. |

**Open questions:**
- **Q1 — Confirm Option A vs B before any code lands.** v2 plan locks this.
- **Q2 — Is there any persisted config or saved gateway state that references `CONFIG_DIR`?** Probably not (env vars aren't persisted), but reality-check the audit chain + license file paths.
- **Q3 — Backwards compat:** if a customer's deployment script currently sets `EDGECONNECT_CONFIG_DIR=...`, after Option B the env var is silently ignored. Should we log a deprecation warning if it's set? **Recommendation: yes — log `Warning` at startup if `EDGECONNECT_CONFIG_DIR` is set with a hint that it's been removed; takes 2 lines of code.**

**Risks:**
- Removing the field could break a deployment script we don't know about. Mitigation: the deprecation warning (Q3) gives operators a clear signal.

**Definition of done:**
- [ ] `EDGECONNECT_CONFIG_DIR` env-var read removed.
- [ ] `HostOptions.ConfigDirectory` removed or clearly deprecated.
- [ ] Deprecation warning at startup if the env var is set (Q3).
- [ ] Tests updated.
- [ ] Docs reference only `EDGECONNECT_DATA_ROOT`.

**Estimate:** 1 commit, ~30 LOC + ~50 LOC tests. ~0.5 day.

**Sequencing:** lands immediately after Chip 4 in the same session.

---

### 3.3 Offline-scenario lifecycle parity test (M.P2.4 deferred follow-up)

**Scope:** add a 6th sample scenario to the parity test corpus (`offline`) that pins the lifecycle divergence between legacy and new adapters when the health endpoint (`HTTPD_MCNINFO`) is unreachable.

**Recall from M.P2.4 v3 §7:** the offline scenario was excluded from per-scenario parity because legacy and new diverge at the LIFECYCLE level (legacy `CollectDataAsync` returns `Status=Offline`; new adapter fails `StartAsync` via the Q4 health probe). This test adds the missing coverage.

**Deliverables:**

| File | Change |
|---|---|
| `tests/ElpisEdgeConnect.Sources.BrotherHttp.Tests/Parity/Samples/offline/` | New folder. Only the `HTTPD_MCNINFO.txt` file actually has bytes — it returns an empty/malformed response so the parser sees `null`. The other 5 endpoint files are absent (`BrotherHttpTestServer` returns 404 for them). |
| `tests/ElpisEdgeConnect.Sources.BrotherHttp.Tests/Parity/ParityTests.cs` | New test: `LegacyOffline_AndNewAdapterStartFailure_DivergeAsDocumented`. Asserts: (a) legacy `BrotherHttpDataSource.CollectDataAsync` returns `CncMachineData` with `Status == MachineStatus.Offline`; (b) new `BrotherHttpSourceAdapter.StartAsync` throws `InvalidOperationException` and transitions to `AdapterState.Failed`. Explicitly NOT a subset assertion — the divergence IS the contract. |

**Open questions:**
- **Q1 — Should the offline `HTTPD_MCNINFO.txt` be empty (server returns 200 with no body) or absent (server returns 404)?** Legacy code: `if (mcnInfo == null) { ... Offline ... }`. The legacy code's `FetchTextAsync` returns null on HTTP failure OR empty response. Both should trigger the same behavior. **Recommendation: test both** — one variant empty-body, one variant absent. Adds 1 more test method.

**Risks:**
- Trivial. The infrastructure works; this just adds a new fixture + a divergence-asserting test.

**Definition of done:**
- [ ] Offline scenario fixture in place.
- [ ] Parity test for the lifecycle divergence green.
- [ ] M.P2.4 handoff doc updated to mark this follow-up CLOSED.

**Estimate:** half day. Bundle into the same commit as Chip 4 + Chip 5 OR a small standalone commit. **Recommendation: standalone commit so the M.P2.4 handoff doc edit is cohesive.**

---

### 3.4 Chip 3 — bulk-provision tooling (`tools/bulk-provision/`)

**Scope:** template + CSV + generator workflow under `tools/bulk-provision/` so 100 identical CNC sources can be provisioned by editing one CSV. Locks the **golden-source template discipline** from day one to prevent per-machine drift.

**Locked discipline (from deployment-readiness §3 Option A):**
- Every machine-type template is version-controlled and treated as the single source-of-truth.
- Operators NEVER hand-edit a generated 100-source `gateway.json` directly.
- The generator REFUSES to overwrite a hand-edited `gateway.json` (provenance header + content hash).

**Why this is full plan-trail discipline (v1 → review → v2 → reality-check → v3):**
- Multiple architectural decisions still open (PowerShell vs Python; CSV column layout; provenance format; schema validation strategy; how to compose with the Sources.ModbusTcp existing CSV importer).
- The Fanuc + Brother templates need real catalog content (tag definitions, polling, retry/backoff) — non-trivial to author and validate.
- 100-CNC customer install depends on this being correct.

**Deliverables (preliminary — refine in v2/v3):**

| Folder/file | Purpose |
|---|---|
| `tools/bulk-provision/templates/template-fanuc.json` | Golden Fanuc template. Full `SourceInstanceConfig` shape with placeholders for `instanceId`, `deviceId`, `deviceName`, `connection.ipAddress`. Tags + polling + retry/backoff locked. Polling cadence 3000 ms per §7-Q1; tag selection per §7-Q2 (~65 baseline). |
| `tools/bulk-provision/templates/template-brother.json` | Golden Brother template. Same shape with Brother-specific `connection.baseUrl` placeholder + `protocolName=brother-http`. Polling cadence 3000 ms per §7-Q1; same tag selection scope. |
| `tools/bulk-provision/templates/template-modbus.json` | Golden Modbus template. Different from existing per-tag Modbus CSV importer (which is per-tag, not per-instance). |
| `tools/bulk-provision/generate.ps1` *(or .py — Q1 below)* | Generator. Reads templates + machines.csv → emits `gateway.json` with provenance header + content hash. |
| `tools/bulk-provision/samples/machines.csv` | Sample CSV with the 100-CNC customer's intended profile (80 Fanuc + 20 Brother). |
| `tools/bulk-provision/samples/gateway-100-cnc.json` | Sample generated output for code-review sanity. |
| `tools/bulk-provision/README.md` | CSV format, template structure, golden-source rule, regeneration workflow, EREMOS V2 topic shape, "Import draft from JSON" via Studio. |
| `tools/bulk-provision/tests/` | Generator tests — see acceptance criteria. |

**Open questions for ChatGPT review:**

- **Q1 — PowerShell or Python?** Defaults from chip prompt: PowerShell (Windows toolchain, no Python dependency). If PS JSON-schema validation is painful, Python with `jsonschema`. **Recommend PS** unless ChatGPT review identifies a blocker. Decision locked in v2.
- **Q2 — CSV column layout?** Chip prompt suggests `make,instanceId,deviceId,deviceName,host,enabled`. **Open:** should it include `gatewayId`/site for multi-gateway customers? Or do we generate per-gateway CSVs separately? Recommend per-gateway CSV (operator runs the tool once per gateway with a smaller CSV); locks the 1-CSV-1-gateway-1-template-set discipline. Discuss in review.
- **Q3 — Provenance format.** Chip prompt: "provenance header (`Generated-By: bulk-provision` + content hash) and refuses to re-emit over a hand-edited file." **Open:** JSON-comment header (not valid JSON) vs a `_provenance` field in the gateway.json root (valid JSON but pollutes the schema)? Lean toward `_provenance` field; canonical config parser ignores unknown roots. Discuss in review.
- **Q4 — Schema validation strategy.** Reuse `src/ElpisEdgeConnect.SchemaValidation/` (if it exists) or `Microsoft.NJsonSchema` in PowerShell? Reality-check in v3.
- **Q5 — Composition with existing Modbus per-tag CSV importer.** That tool is per-tag (operator imports many tags into ONE instance); this tool is per-instance (operator stamps out many instances of the SAME tag template). They don't conflict but the docs should make the distinction clear. Discuss in review.
- **Q6 — Locked discipline enforcement.** Refusing to overwrite a hand-edited file — how is "edited since last generation" detected? Content-hash mismatch? Modified-time? File-presence of a `.bulk-provision-untouched` sentinel? **Recommend content-hash check** since it's resilient to file-system metadata loss (e.g., copy-paste across machines). Discuss in review.
- **Q7 — Template inheritance.** If a customer needs both Fanuc-A800 and Fanuc-A600 templates with slightly different tag sets, do we support inheritance (`template-fanuc-A600.json extends template-fanuc.json`)? **Chip prompt says: out for v1, revisit later.** Confirm in review.
- **Q8 — Studio integration.** Chip prompt says the operator uses Studio's "Import draft from JSON" button after generation. Verify that button exists today (M.2a Config page) and document the workflow precisely in the README.

**Risks:**
- Generator bugs corrupt customer-install configs. **Mitigation:** schema validation before write, end-to-end test (generate → `ConfigurationManager.CreateDraftAsync` → expected 100 sources resolve through Focas2/Brother `FromSourceInstance`).
- Provenance discipline breaks down if operators bypass the tool. **Mitigation:** README is explicit; tool refuses to overwrite hand-edited files; runbook (§8.6/§8.7 from PR #16) defers to bulk-provision for restoring config from CSV.
- §7 locked numbers shift later (customer's polling/tag-count assumptions evolve). **Mitigation:** the template is THE source-of-truth — changing it + re-running the generator propagates atomically. This is the discipline's whole point.

**Definition of done:**
- [ ] All Q1-Q8 resolved in v2; reality-check in v3.
- [ ] End-to-end test: 100-row CSV → generator → `gateway.json` → 100 source instances resolve correctly via Studio's draft path.
- [ ] Validation test: deliberate schema violation → clear error → non-zero exit → no file written.
- [ ] Provenance test: regenerating over an edited `gateway.json` is refused with a clear error.
- [ ] README walks an operator through the install-day workflow.
- [ ] Sample `gateway-100-cnc.json` committed for reference.
- [ ] Deployment-readiness §10 acceptance signal — "Bulk-provision generator + templates committed" row checked.

**Estimate:** 3-4 days focused work. v1 (this) → ChatGPT review → v2 → reality-check → v3 → implementation across 1-2 sessions.

---

### 3.5 EREMOS V2 contract revalidation

**Scope:** small end-to-end test that proves the new-arch MQTT emission shape is actually consumable by the customer's EREMOS V2 instance (already deployed per §7-Q8). Currently uncaptured as a chip; surface as a chip now.

**Why this matters:** M.P2.4's §9 manual smoke verified `eremos/+/cnc/+/+` topic shape against `mosquitto_sub` — that proved the MQTT broker delivers, but didn't prove EREMOS V2's consumer correctly parses the payloads. The §7-Q8 lock confirms EREMOS V2 is already at the customer; we should validate against it in-house before shipping.

**Deliverables (preliminary):**

| File | Purpose |
|---|---|
| `tests/ElpisEdgeConnect.Integration.Tests/EremosV2ContractTests.cs` (or new test project) | End-to-end harness: launches gateway with a bulk-generated config (Chip 3 dependency), runs a real Mosquitto broker locally, runs a real EREMOS V2 ingest subscriber, verifies the subscriber receives + parses + persists the canonical points. |
| `tools/eremos-v2-contract-harness/` | Optional standalone test harness if the integration-tests project boundary is wrong place. Includes a Docker-Compose to launch Mosquitto + EREMOS V2 ingest if EREMOS V2 packages exist. |
| `docs/sessions/2026-05-XX-eremos-v2-revalidation-plan.md` | v1 plan trail. |

**Open questions:**

- **Q1 — Where does the EREMOS V2 ingest run for the test?** EREMOS V2 is already deployed at the customer (per §7-Q8). Do we have a local EREMOS V2 instance we can run for the test, or do we mock the EREMOS V2 ingest based on its documented MQTT contract from `shared-knowledge/contracts/eremos-per-tag-mqtt.md`? **Recommendation: ideally a real local EREMOS V2 instance** (we control the deployment posture); fallback is a contract-test driven by the shared-knowledge contract docs.
- **Q2 — What does "validation pass" mean?** Subscribe to topics + parse payloads + assert structure matches expected? Or also verify EREMOS V2's internal storage shape matches our PerTag emission? Recommend the former (contract-level validation, not internals).
- **Q3 — Live or canned?** Could run continuously against a live MQTT broker, OR run as a one-shot integration test with a known finite emission. **Recommend one-shot integration test** — deterministic, can be added to the `Category!=Flaky` test gate, runs in CI.
- **Q4 — Does this become part of the 7-day soak?** Or runs separately as a pre-soak gate? Chip-prompt-implied: separately. **Recommendation: separately first (a few minutes), then as a sub-component of the 7-day soak's success criteria (verifying EREMOS V2 keeps consuming throughout the soak).**

**Risks:**
- EREMOS V2 contract drift detected — would mean MQTT sink emission shape needs adjustment. **Mitigation:** the test catches this early; smaller fix than discovering at customer install.
- No local EREMOS V2 instance available. **Mitigation:** fall back to Q1's contract-driven mock.

**Definition of done:**
- [ ] Test harness implemented per Q1/Q2 decisions.
- [ ] Pass against shared-knowledge MQTT contract OR real EREMOS V2 instance.
- [ ] Documented as runnable from `dotnet test` (or standalone harness).
- [ ] Deployment-readiness §10 acceptance signal — "EREMOS V2 contract validation pass" row checked.

**Estimate:** 1-2 days. Brief v1 → v2 plan trail.

---

### 3.6 M.2c Live Tag Watch (optional, HIGH value)

**Scope:** per-tag runtime inspection in Studio. Operator picks a source + tag (or group of tags), Studio shows the current value, quality, timestamp, and recent history. Real-time updates.

**Why HIGH value for this customer:** §7-Q6 locked operator profile as "maintenance staff" — they need answers in the UI, not in logs. When CNC #47 reports weird data, the maintenance tech needs an in-Studio answer to "what is machine #47 publishing right now?" — currently no such answer exists.

**Why substantial:** multiple architectural decisions to make.

**Deliverables (preliminary):**

| Component | Notes |
|---|---|
| Subscription model | How does the Studio UI subscribe to live tag values? Options: server-sent events (SSE) from the gateway, WebSocket, REST polling, or MQTT-tap from the data plane. (Q1 below) |
| Data-plane tap | Per platform principle P1 ("Runtime Tap is observational"), the tap MUST NOT alter the canonical data path. Read-only side-channel. |
| `LiveTagWatch.razor` page in Studio | Operator picks source(s), picks tags, sees real-time values + history (last N values, last 5 minutes, ...). |
| Tag-discovery integration | Reuse `BrowseTagsAsync` per source — already covered by `ISourceAdapter.BrowseTagsAsync`. |
| Per-tag history buffer | Bounded ring buffer in memory for "last N values" / "last M seconds" display. (Q2 below) |
| Tests | Live Tag Watch page model + subscription contract tests. |

**Open architectural questions for ChatGPT review:**

- **Q1 — Subscription model.** Four options:
  - **(a) SSE** — gateway exposes `/api/v1/live-tags?source=X&tags=Y,Z`, Studio's `EventSource` consumes. Simple, no extra deps.
  - **(b) WebSocket** — bidirectional, lower overhead at scale. But Studio is hosted IN-PROCESS with the gateway (M.1b.1 single-process), so WebSocket adds complexity for no real benefit.
  - **(c) REST polling** — Studio polls `/api/v1/live-tags/current?source=X&tags=Y,Z` every N ms. Simplest, but trades latency for simplicity. At 100-CNC scale, 1 second polling = 100 req/s — manageable.
  - **(d) MQTT-tap** — Studio subscribes to the same MQTT topics the sink publishes. Architecturally clean (data plane is already MQTT). But Studio shouldn't depend on MQTT availability for diagnostics.

  **Recommendation: SSE.** Lowest complexity, native browser support, works in-process. Discuss in review.

- **Q2 — History buffer scope + retention.** Where does "last N values per tag" live? Options:
  - **(a) Per-source supervisor** — bounded ring buffer per tag per source. Cheap.
  - **(b) Per-route worker** — closer to the canonical batch but worse locality.
  - **(c) Studio-only client-side** — operator gets history only while their browser is open.
  - **Recommend (a) per-source supervisor** with bounded ring (last 100 values OR last 5 minutes, whichever smaller). Allows historical view even on first browser open.

- **Q3 — Per-tag observational tap mechanism.** Platform principle P1 says the tap is observational. **How does the supervisor publish per-tag updates to subscribers?** Pattern: `IObservable<CanonicalDataPoint>` per source, with `Subject<>` or `Channel<>` backing? Lock the in-memory pub/sub mechanism. Reality-check in v3.

- **Q4 — UI shape.** Single source vs multi-source? Single tag vs multi-tag? Filtering by tag-path prefix (e.g., "show me all Tools/*")? Quality indicator? Stale-value indicator (last update > N seconds ago)? Lock UX in v2.

- **Q5 — Performance at 100-CNC scale.** Each gateway has ~25 sources × ~65 tags × 3-second cadence = ~540 points/sec. Subscriber sees all of them? Or filtered to operator-selected tags? **Recommend: subscriber-driven filtering at the server** — Studio sends a list of tag-paths it cares about, supervisor only emits matching points. Lock in v2.

- **Q6 — Authentication / authorization.** Live Tag Watch could expose sensitive process data. Today Studio is unauthenticated (localhost only); Phase 4 will add auth. **Recommendation: don't add auth here — defer to the Phase 4 auth story.** Note in handoff.

- **Q7 — Composition with M.2c-related fast-follow.** Does Live Tag Watch eventually need historical persistence ("last 24 hours" charting)? **Recommend: out for v1.** Last 5 minutes in-memory is fine. Historian is a separate Phase 5 milestone.

**Risks:**
- Performance impact on the supervisor's hot loop. **Mitigation:** Q3's tap mechanism is non-blocking (Channel with bounded buffer, dropped-oldest on overflow).
- UX complexity creep. **Mitigation:** lock minimum-viable scope in v2 (Q4).
- Phase 4 auth design conflicts with Live Tag Watch's current "no auth" posture. **Mitigation:** Q6 — defer to Phase 4 auth.

**Definition of done:**
- [ ] All Q1-Q7 resolved in v2; reality-check in v3.
- [ ] `LiveTagWatch.razor` page in Studio, accessible via main nav.
- [ ] Real-time updates to selected tags from at least one source.
- [ ] Per-tag history (last 5 minutes OR last 100 values) visible.
- [ ] Performance smoke at 100-CNC scale OK (single source × 65 tags × 3s subscribed → Studio renders without lag).
- [ ] Tests for the page model + subscription mechanism + filtering.
- [ ] Documentation: README or in-Studio help describing operator workflow.

**Estimate:** ~1 week. Full plan-trail discipline (v1 → review → v2 → reality-check → v3 → implementation).

---

### 3.7 M.2d Edit-via-Wizard sweep (optional)

**Scope:** add an "Edit" mode to each source/sink/route wizard so operators can modify existing configurations through the same UX they used to create them. Plus backfill the deferred Brother Test Connection button (M.P2.4 Q12) into the sweep.

**Why substantial:** touches every wizard. Needs standardisation across the four wizards (`AddBrotherHttpSource.razor`, `AddFocas2Source.razor`, `AddModbusSource.razor`, `AddRoute.razor`, `AddMqttDestination.razor`, `AddOpcUaServerDestination.razor`). Must come after M.2c Live Tag Watch so the wizard contract reflects any new UX patterns the Live Tag Watch milestone introduces.

**Deliverables (preliminary):**

| Component | Notes |
|---|---|
| Per-wizard "Edit" route | E.g. `/sources/edit/{instanceId}` loads the existing config into the wizard model and saves via PATCH semantics rather than draft-add. |
| Shared edit-vs-add discrimination logic | Pulled out of each wizard into shared base / mixin. |
| Test Connection button on Brother source wizard | Backfill of M.P2.4 Q12 deferred item. Posts to `/api/v1/sources/brother-http/probe` (new endpoint) — fires `HTTPD_MCNINFO` against the configured BaseUrl + Timeout. |
| Test Connection button on FOCAS2 source wizard | Already has Browse Controller probe — may overlap or need re-shaping for the standardised pattern. |
| Shared validation pattern across wizards | Composition pattern locked in M.2b.6.2 (`ModbusTagValidator` composition). Apply consistently across all wizards. |
| Tests | Per-wizard edit-mode tests + Test Connection endpoint tests. |

**Open architectural questions:**

- **Q1 — Edit vs new code reuse.** Lots of shared logic between Add and Edit modes. Shared base class for wizard pages? Shared service for "load existing config into wizard model"? Lock in v2.
- **Q2 — PATCH vs full-replace semantics.** When operator edits an existing source, do we PATCH the source instance (minimal diff) or full-replace (re-emit the entire source config + route)? **Recommend full-replace via the existing draft-config-then-apply workflow** — simpler, audit-friendly, consistent with the Add path.
- **Q3 — Test Connection generalisation.** Should the Test Connection pattern be generalised across all source/sink wizards? FOCAS2 has Browse Controller; Brother needs HTTPD_MCNINFO probe; Modbus could test the TCP socket; MQTT destination wizards have it already (M.2b.6); OPC UA Server destination has it. Lock the pattern in v2.
- **Q4 — Wizard contract changes that M.2c might surface.** If M.2c Live Tag Watch lands first, does its UX surface in the wizards (e.g., "preview live tag values before saving")? Likely yes; lock the cross-pollination in v2.
- **Q5 — Migration from M.2b.6.2's existing wizard hardening.** M.2b.6.2 already standardised `ModbusTagValidator` composition. M.2d should extend that to all protocols. **Recommendation: lock the composition pattern as a wizard-level invariant in v2.**

**Risks:**
- Scope creep. Touching every wizard tempts adding lots of polish work. Lock minimum-viable in v2.
- M.2c may force re-work if M.2c's UX patterns aren't agreed first. **Mitigation:** strict sequencing (M.2c first, M.2d second).

**Definition of done:**
- [ ] Edit mode works for every source / sink / route wizard.
- [ ] Test Connection button on Brother source wizard (M.P2.4 Q12 backfill).
- [ ] Standardised Test Connection pattern across all wizards per Q3.
- [ ] Shared validation composition pattern from M.2b.6.2 extended consistently.
- [ ] Tests for every wizard's edit mode + Test Connection endpoint.

**Estimate:** ~1-2 weeks. Full plan-trail discipline.

---

## 4. Cross-cutting concerns

### 4.1 Test posture goals

| After item | Expected test count | Note |
|---|---|---|
| Chip 4 + Chip 5 + offline-scenario test | ~2280 (current 2263 + ~17 new across the three items) | Small fixes; one new fixture + one new test + a few migration tests. |
| Chip 3 | ~2310 (+30 generator tests) | Multi-mode CSV → JSON → ConfigurationManager round-trip + provenance enforcement + schema-violation rejection. |
| EREMOS V2 revalidation | ~2315 (+5 contract-shape tests) | One end-to-end against a real (or mock) EREMOS V2 instance + topic-shape pinning tests. |
| M.2c Live Tag Watch | ~2400 (+85 page-model + subscription + history-buffer tests) | Substantial test surface; full plan-trail discipline locks the breakdown. |
| M.2d Edit-via-Wizard | ~2450 (+50 edit-mode + Test Connection tests) | Per-wizard edit-mode coverage + Test Connection endpoints. |

Cumulative: **~2450 tests** after the wrap-up + optionals. From today's 2263, that's ~+190 over the next 4-6 weeks of focused work.

### 4.2 Deployment-readiness §10 acceptance signal — projected final state after all items

```
- [x] §7 open questions locked with the customer — COMPLETE 2026-05-20 (PR #16)
- [x] M.P2.4 Brother HTTP migration kickoff + plan trail — COMPLETE 2026-05-21 (PR #19)
- [ ] Bulk-provision generator + templates committed — Chip 3
- [ ] 7-day in-house soak passes acceptance criteria
- [ ] 48-hour customer-site acceptance test plan agreed with customer engineering
- [ ] EREMOS V2 contract validation pass against the new-arch MQTT emission — addressed by §3.5 above
```

After Chip 3 + EREMOS V2 revalidation land, 3 of the 5 remaining checkboxes close. Soak + customer-site acceptance plan remain (the soak gates customer-side acceptance plan; the soak depends on Chip 3 + EREMOS V2).

### 4.3 §1 gap analysis edit (out-of-scope housekeeping)

The deployment-readiness §1 gap analysis still shows "Brother HTTP source adapter | ❌ **NOT migrated**" — that row should be updated to ✅ now that M.P2.4 is merged. Minor doc edit; could ride along with the Chip 4 + Chip 5 commit or stand alone. Flagged for completeness.

### 4.4 Plan-trail file naming conventions

Following the established M.P2.4 + PR #16 pattern:
- v1 plan: `docs/sessions/2026-MM-DD-<topic>.md`
- v2 plan: `docs/sessions/2026-MM-DD-<topic>-v2.md`
- v3 reality-check: `docs/sessions/2026-MM-DD-<topic>-v3.md`
- Handoff: `docs/sessions/2026-MM-DD-<topic>-handoff.md`

Items needing dedicated plan trails (per §2): Chip 3, EREMOS V2 revalidation, M.2c, M.2d. Items inline in this roadmap: Chip 4, Chip 5, offline-scenario test.

### 4.5 Worktree / branch posture during the wrap-up

Per memory ("Handoff docs must be on master"), each chip / milestone should land via its own PR on master. Suggested branch naming:
- Chip 4 + Chip 5: `claude/wrapup-bug1-and-configdir` (paired commit set)
- Offline-scenario test: `claude/mp24-offline-parity` (small, single commit)
- Chip 3: `claude/chip3-bulk-provision` (full milestone branch)
- EREMOS V2 revalidation: `claude/eremos-v2-revalidation`
- M.2c: `claude/m2c-live-tag-watch`
- M.2d: `claude/m2d-edit-via-wizard`

Each lands via squash-merge per the repo's convention.

---

## 5. Open questions for ChatGPT review pass

Numbered for response cross-reference.

| # | Item | Question |
|---|---|---|
| 1 | Chip 5 | Option A (wire CONFIG_DIR through) or Option B (delete it)? Recommend B. |
| 2 | Chip 5 | Add a startup deprecation warning if `EDGECONNECT_CONFIG_DIR` is set after Option B? Recommend yes. |
| 3 | Offline-scenario test | Test empty-body and absent-file variants of HTTPD_MCNINFO failure separately? Recommend yes. |
| 4 | Chip 3 | PowerShell vs Python for the generator? Recommend PowerShell. |
| 5 | Chip 3 | CSV column layout — what's the canonical set? Recommend `make,instanceId,deviceId,deviceName,host,enabled` (per-gateway CSV). |
| 6 | Chip 3 | Provenance format — JSON-comment header (invalid JSON but invisible to parser) vs `_provenance` field in root (valid JSON but pollutes schema)? Recommend `_provenance` field. |
| 7 | Chip 3 | Edited-file detection — content hash check, modified-time, or sentinel file? Recommend content hash. |
| 8 | Chip 3 | Template inheritance for variant CNC models — in or out for v1? Recommend out. |
| 9 | EREMOS V2 revalidation | Real local EREMOS V2 instance for the test, or contract-driven mock from shared-knowledge docs? Recommend real instance if available, mock as fallback. |
| 10 | EREMOS V2 revalidation | One-shot test or live-running validation? Recommend one-shot. |
| 11 | M.2c Live Tag Watch | Subscription model — SSE, WebSocket, REST polling, MQTT-tap? Recommend SSE. |
| 12 | M.2c Live Tag Watch | History buffer location — per-source supervisor / per-route worker / Studio-only? Recommend per-source supervisor with bounded ring. |
| 13 | M.2c Live Tag Watch | UI scope — single-source / multi-source / multi-tag filtering / stale indicator. Lock in v2. |
| 14 | M.2c Live Tag Watch | Server-side filtering by operator-selected tag paths? Recommend yes. |
| 15 | M.2c Live Tag Watch | Authentication — defer to Phase 4? Recommend yes. |
| 16 | M.2d Edit-via-Wizard | Shared base class / service for "load existing config into wizard model"? Lock in v2. |
| 17 | M.2d Edit-via-Wizard | PATCH vs full-replace semantics for edits? Recommend full-replace. |
| 18 | M.2d Edit-via-Wizard | Test Connection pattern generalised across all source/sink wizards? Lock in v2. |
| 19 | Sequencing | M.2c before M.2d — confirm. Recommend yes. |
| 20 | Sequencing | Offline-scenario test as a standalone commit (with M.P2.4 handoff doc edit) vs bundled with Chip 4/5? Recommend standalone. |

---

## 6. Next steps

1. **You ChatGPT-review this v1 roadmap.** Expect verdicts on Q1-Q20 + any additional issues.
2. **I produce v2** with locked verdicts. For Chip 3, M.2c, M.2d — also start dedicated v1 plan-trail files.
3. **Reality-check v3** for the substantial items (Chip 3, M.2c, M.2d) — re-read relevant code, confirm no false assumptions.
4. **Implementation begins.** Recommended first session: Chip 4 + Chip 5 + offline-scenario test (small, well-scoped, lowest risk).

---

**End of v1 wrap-up roadmap. Awaiting ChatGPT review pass.**
