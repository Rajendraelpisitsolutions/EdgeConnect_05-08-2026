# Runtime reconfigure — systemic "data stops after a runtime edit" — Plan v1

**Date:** 2026-06-23
**Status:** v1 — draft for review pass (ChatGPT/user) before v2 lock
**Author:** session handoff
**Trigger:** Field report — *"If we add Modbus tags during runtime, EdgeConnect stops pushing
data properly; sometimes it stops and we must restart the server to recover."* Follow-up question:
**does the same issue exist in other source modules, and how do we guarantee it doesn't?**

> Cadence reminder: this is **v1**. Expected flow is v1 → review → v2 → reality-check → v3, each in
> its own dated file under `docs/sessions/`. **Do not start implementation off v1.**

---

## 1. One-paragraph answer to the question

**Yes — every source module is exposed, because the fragile machinery is not in the Modbus adapter;
it is in the shared host reconcile path.** Any runtime edit to a source (add/remove a tag, change a
poll rate, change a connection field) is diffed as a `Modified` entity, classified as `Restart`, and
executed by `RuntimeReloadCoordinator` as **`RemoveAsync(old)` + `AddAsync(new)`** — a full adapter
teardown and rebuild that drops the device connection and recreates the source's intake channel.
That path is identical for FOCAS2, S7, Brother HTTP, MTConnect, OPC UA Client and Modbus TCP. The
proper, contract-blessed escape hatch (`ISourceAdapter.ReconfigureAsync`) **exists but is never
called.** So the fix is mostly *central* (fix one path, protect all six adapters) plus *one piece of
dormant wiring* that was specified in ADR-0015 but never connected.

---

## 2. Recon findings (verified against `master` @ this session)

| # | Finding | Evidence |
|---|---------|----------|
| F1 | Adding a tag mutates `SourceInstanceConfig.TagDefinitions` → differ emits an entity-level **`Modified`** for the source only (route text unchanged, so the route is **not** in the diff). | `ConfigurationDiffer.cs:193` (coarse, entity-level diff) |
| F2 | Classifier maps `Modified → Restart` unconditionally. | `RuntimeReloadClassifier.cs:96`; `ConfigurationReloadPlan.cs:48` (`Restart` doc) |
| F3 | Coordinator executes a source `Restart` as `RemoveAsync` (teardown, drops socket, completes old channel) + `AddAsync` (brand-new adapter, new socket, **new channel**). | `RuntimeReloadCoordinator.cs:322` (A2) and `:352` (B1) |
| F4 | A source restart recreates the intake channel; a route bound to it keeps the **old, now-completed** channel reader and silently stops ingesting unless the route is also rebuilt. The coordinator synthesizes a route rebind for this — and it is the **most recent commit on the file** (`78646e4 fix(reload): cascade a route rebind when a source is restarted`). The cascade is **guard-conditional**. | `RuntimeReloadCoordinator.cs:585-649` (`ComputeSourceRestartRouteRebindActions`) |
| F5 | **`ISourceAdapter.ReconfigureAsync` has ZERO call sites.** It is defined (default = Stop+Init+Start) and overridden by OPC UA Client, but nothing invokes it. Same for the supervisors' `RestartAsync`. | grep `ReconfigureAsync\|RestartAsync` across `src/**/*.cs` → only definitions/overrides, no callers |
| F6 | The Edit-mode PUT path funnels straight into the apply pipeline → `CurrentChanged` → coordinator (i.e. F2/F3), **not** through `ReconfigureAsync`. | `SourcesUpdateApi.cs:172` — `BuildUpdatedSourceDraft → CreateDraftAsync → ApplyDraftAsync` |
| F7 | **Two ADRs contradict each other.** ADR-0009 Decision 3: *"there is no `ReconfigureAsync` path… any `Modified` resolves to `Restart`."* ADR-0015 Rule 11: *"Edit-mode changes to the tag list… MUST go through `ISourceAdapter.ReconfigureAsync` rather than full Stop+Initialize+Start."* The implementation followed ADR-0009; ADR-0015 Rule 11 is aspirational/unwired. | `docs/decisions/0009-…md` §Decision 3; `docs/decisions/0015-wizard-contract.md` Rule 11 |

**The headline:** F5 + F6 + F7 are the systemic gap. The careful hot-reconfigure surface specified
in ADR-0015 was built at the adapter layer (OPC UA) and tested in isolation, but the live edit flow
never routes through it — a textbook instance of the very drift ADR-0015 §Reasoning warns about
(the `WizardValidationBanner` that shipped unwired for four months).

---

## 3. Failure mechanisms — which are shared vs protocol-specific

| # | Mechanism | Permanent stall? | Needs restart? | Scope |
|---|-----------|------------------|----------------|-------|
| **M1** | **Route ↔ channel orphan.** New intake channel after source restart; route left bound to the old completed channel → route shows **Running but `pointsIn` frozen at 0**; new channel back-pressures the poll loop to a standstill. | **Yes** | **Yes** (clean boot uses a different, simpler path than reconcile) | **Shared** (coordinator/supervisor). Reactive fix `78646e4` exists but is guard-conditional → any gap re-opens it for *all* protocols. |
| **M2** | **Connection-drop fragility.** Teardown disposes the socket/handle/session; bring-up immediately re-establishes it. Many devices choke on the instant reconnect. | Sometimes | Sometimes (slower service restart lets the device release first) | **Protocol-specific** severity (see §4). Affects *all* sources; cost varies. |
| **M3** | **Teardown-timeout / duplicate-id race.** If teardown exceeds the coordinator's 30 s per-instance cap, the stale supervisor entry can linger and the re-`Add` throws "Duplicate source instance id." | Yes (edge) | Yes | **Shared.** *Lower probability* — `StopInternal` is internally bounded (10 s pump + 10 s stop) and swallows its own timeouts, so `RemoveAsync` normally completes and removes the entry. Real only if `StopAsync`/`DisposeAsync` itself hangs past its bound. Harden defensively, don't over-index. |

**Takeaway:** M1 and M3 are *one codebase*, not six. Fix them in the host and write the regression
test against a **mock source**, and every adapter — present and future — is covered. M2 is the only
genuinely per-protocol axis and is best neutralised by *not dropping the connection at all* for
tags-only edits (see §5).

---

## 4. Per-adapter restart-fragility matrix (M2 axis)

For the residual cases that legitimately still need a real restart (enable/disable, host/port/
credential changes), severity differs and each row earns one focused test:

| Source | Reconnect fragility | Why |
|--------|--------------------|-----|
| **FOCAS2** | **High** | FANUC libtop handle pool is small/fixed (`FOCAS2.HANDLE_EXHAUSTED` exists for a reason). Repeated drop/reopen risks leaking handles if `StopAsync` doesn't free cleanly. Verify handle is freed on every teardown. |
| **Siemens S7** | **Med-High** | PLCs expose a limited number of PG/connection slots; same single-socket hazard as Modbus. |
| **Modbus TCP** | **Med-High** | Many slaves and serial→TCP gateways allow exactly one TCP connection; instant reconnect after dispose is refused → backoff/circuit-breaker (`ModbusConnectionManager.cs:224`). Self-recovers eventually, but stacks badly with M1. |
| **OPC UA Client** | **Medium** | Secure-channel + session handshake + full subscription rebuild is expensive; the surgical `ReconfigureAsync` it already owns is bypassed at runtime (F5/F6). |
| **MTConnect / Brother HTTP** | **Low** | Stateless HTTP; reconnect is cheap. Still exposed to M1. |

> Note: these severities are analytical (from protocol nature + known error codes), **not yet
> measured**. The §7 harness turns them into pass/fail facts.

---

## 5. Proposed solution — three layers, priority order

### Layer A — Central hardening (fixes M1/M3 for all six adapters at once) — *fast safety net*
1. Make the route-rebind cascade **unconditional** for every route bound to a restarting source
   (close any gap in `ComputeSourceRestartRouteRebindActions`' guards; verify it cannot be skipped
   by rapid back-to-back applies).
2. Harden the teardown/duplicate-id path (M3): ensure a teardown timeout still evicts the stale
   supervisor map entry before any re-`Add`, so `AddAsync` can never hit duplicate-id.
3. **Regression test against a `MockSource`** so the proof is adapter-agnostic and covers all
   protocols by construction.

### Layer B — Wire the dormant `ReconfigureAsync` (the real systemic fix; resolves F5/F6/F7)
1. Add a `Reconfigure` op to `ReloadOp` (or a `ReconfigureSource` action) emitted by the classifier
   when the **only** thing changed on a source is its tag list / poll-rate / tuning — i.e. a
   "soft-reconfigurable" delta. Connection-defining fields (host/port/credentials/protocol) still
   classify as `Restart`.
2. Coordinator executes a `Reconfigure` op by calling **`adapter.ReconfigureAsync(newConfig)` on the
   live instance** — no `Remove`, no `Add`, **channel and route binding preserved** → M1 cannot occur.
3. Adapters with the safe default (Modbus/FOCAS2/S7/Brother/MTConnect) at minimum stop recreating
   the channel; OPC UA's surgical override finally executes for real (no session re-handshake).
4. This requires **reconciling ADR-0009 Decision 3 with ADR-0015 Rule 11.** Decision 3 said "no
   reconfigure path in v1" and explicitly named a future `ITryReconfigureLive` opt-in as the door;
   Rule 11 already mandates the path. Propose a **new ADR** that supersedes 0009-D3's "Modified ==
   Restart" with "tags-only ⇒ Reconfigure; connection-defining ⇒ Restart," and marks Rule 11 as
   *implemented*. (Per CLAUDE.md, locked decisions are not relitigated silently — this is the
   explicit revisit.)

### Layer C — Per-adapter true in-place overrides (incremental, after B) — *eliminates M2 for tags-only*
- Give Modbus a real `ReconfigureAsync` that rebuilds only the `ScanPlan` and leaves the TCP
  connection untouched → the device socket is never dropped on a tag add.
- Same pattern per protocol, prioritised by the §4 matrix (FOCAS2 and S7 first; HTTP ones last).
- Until a protocol overrides, the safe default still preserves the channel (Layer B already removed
  M1); the connection still briefly drops (M2) but that is the *pre-existing* behaviour, now scoped
  to one adapter at a time and measured by the harness.

**Recommendation:** ship **A** first for immediate stability, then **B** as the durable fix, then
**C** opportunistically. A alone stops the permanent stalls; B stops the connection churn for the
common edit; C removes the last drop for hot protocols.

---

## 6. The ADR question to settle in review

This plan changes a **locked** decision, so it must be surfaced, not assumed:

- **ADR-0009 Decision 3** ("Modify means stop-then-start in v1; no `ReconfigureAsync`") — Layer B
  supersedes this for the tags-only subset.
- **ADR-0015 Rule 11** ("Edit-mode tag changes MUST go through `ReconfigureAsync`") — Layer B makes
  this true for the first time.
- The future `ITryReconfigureLive` opt-in named in ADR-0009 §Decision 3 / §Reasoning 3 is, in
  effect, what we are now building. Decide whether to (a) author a new superseding ADR, or (b) amend
  0009 + mark 0015 Rule 11 implemented. (v1 author leans (a): a clean superseding ADR with a clear
  "supersedes 0009-D3" header reads better than a patched 0009.)

---

## 7. Verification strategy — turn "we hope" into "it fails loudly if it regresses"

1. **Adapter-agnostic reconcile regression (Layer A guarantee).** Drive a tag-add → `Modified` →
   reconcile against a `MockSource` + mock route; assert: route `pointsIn` resumes within N polls,
   no orphaned channel, no `HOST.RECONCILE_*` fault, supervisor map has exactly one entry. Because it
   exercises the shared path with a mock, it protects all real adapters.
2. **Parameterized "edit at runtime" integration harness (the systemic guarantee).** One test
   parameterized over **every operator-available source** (FOCAS2, S7, Brother, MTConnect, OPC UA
   Client, Modbus TCP) using each protocol's existing fake/demo client (`FakeModbusClient`,
   FOCAS2 demo mode, etc.). For each: start source+route, add a tag at runtime, assert data resumes
   within a bound and no connection/handle leak. New protocols join the matrix by adding one row.
3. **Per-adapter leak assertions (M2/§4).** FOCAS2: handle count returns to baseline after a
   restart. Modbus/S7: exactly one socket open after reconfigure (no double-connect). OPC UA: with
   Layer B, assert `ReconfigureAsync` is invoked (no new session created).
4. **PR gate discipline** (per prior breakage): run the **full** `Management.Tests` project, not a
   filtered subset — cross-cutting isolation/schema guards only run on the full project.

---

## 8. Phased execution (sketch — refine in v2)

- **Phase 0 — Decide & document.** This plan → review → v2. New/superseding ADR drafted after the
  ADR question (§6) is settled. No code before v2 lock.
- **Phase 1 — Layer A (central hardening + mock regression).** Smallest diff, biggest stability win,
  no ADR change required (it only hardens existing `Restart` behaviour). Ship as its own PR.
- **Phase 2 — Layer B (classifier `Reconfigure` op + coordinator wiring + ReconfigureAsync invoke).**
  Gated on the ADR. Default-fallback adapters keep working; OPC UA's override goes live.
- **Phase 3 — Build the parameterized harness (§7.2) and the per-adapter leak tests (§7.3).**
- **Phase 4 — Layer C overrides**, prioritised by §4 (FOCAS2, S7, then Modbus, then the rest).

---

## 9. Risks

- **Changing a locked ADR.** Mitigation: §6 makes it an explicit, reviewed revisit, not a silent
  workaround (CLAUDE.md anti-pattern guard).
- **`Reconfigure` classification correctness.** Mis-classifying a connection-defining change as
  "tags-only" would skip a needed reconnect. Mitigation: whitelist the soft-reconfigurable fields
  explicitly; everything else stays `Restart`. Unit-test the field partition.
- **Per-adapter `ReconfigureAsync` correctness (Layer C).** The override contract (active-set swap at
  batch boundary, validate-before-swap, reconfigure-during-reconfigure throws) is non-trivial — see
  the OPC UA implementation as the reference. Mitigation: Layer C is opt-in and per-protocol; the
  safe default remains until each is proven.
- **Harness fakes drifting from real device behaviour** (esp. single-connection devices). Mitigation:
  keep one manual smoke against a real device per hot protocol, recorded like `mp22-hot-reload.md`.

---

## 10. Open questions for the review pass

1. **ADR shape (§6):** new superseding ADR, or amend 0009 + mark 0015 Rule 11 implemented?
2. **Soft-reconfigurable field set:** is it exactly {tag list, poll rate, subscription tuning}, or do
   we also treat scale/offset/byte-order edits and per-tag scan-rate as soft? (They are tags-only by
   nature, so likely yes.)
3. **Ship Layer A alone first** as an immediate hotfix PR (no ADR), then B/C? Or hold for the full
   set? (v1 author: ship A first — operators are hitting this now.)
4. **Store-and-forward interaction:** during the brief reconfigure/restart, does the per-route SQLite
   buffer already hold points across the gap (ADR-0009 §Reasoning 3 claims it does)? Confirm so we
   can state the operator-visible data-loss window precisely (likely "none, just delayed").
5. **Does Modbus's per-group `_nextDueAt` scheduler need a warm-start** after a Layer C in-place
   `ScanPlan` rebuild, or is rebuilding it from scratch (as `InitializeAsync` does today) fine?
6. **Confirm-on-live (Option C from the prior turn):** do we still want to capture diagnostics on a
   live repro to prove M1 is the specific mechanism behind the field report, before Phase 1 lands?
