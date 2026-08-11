# Handoff — Runtime reconfigure systemic fix (→ Sony)

**Date:** 2026-06-23
**From:** Sudhakar's session
**To:** Sony (`Sony_Development` branch)
**Status:** Planning, mid-cadence. **v2 done; next step is the reality-check pass → v3.** No code yet.
**Read this first, then open the v2 plan — it is the live working document.**

---

## 1. TL;DR for the next Claude session (cold start)

We are diagnosing and planning a fix for a field report: **"adding Modbus tags at runtime makes
EdgeConnect stop pushing data; sometimes a server restart is needed."** Investigation showed the bug
is **not Modbus-specific** — it is in the shared host reconcile path, so **every source module is
exposed**.

Two planning artifacts exist (read in order):
1. `docs/sessions/2026-06-23-runtime-reconfigure-systemic-plan-v1.md` — original analysis + recon
   evidence table (F1–F7). **Frozen as the review artifact; do not edit.**
2. `docs/sessions/2026-06-23-runtime-reconfigure-systemic-plan-v2.md` — **the live plan.** Folds in a
   ChatGPT review pass. This is where work continues.

**Your next task:** produce **v3** by working through the six reality-check items in **v2 §12**,
verifying them against the actual code, then locking the design for implementation. Follow the
planning cadence (v1 → review → v2 → **reality-check → v3**); v3 is its own dated file.

---

## 2. The core finding (so you don't re-derive it)

A runtime source edit (add/remove a tag, change a poll rate, etc.) flows:

```
Studio edit → PUT /api/v1/sources/{id}        (SourcesUpdateApi.cs:172)
            → CreateDraft + ApplyDraft         → fires IConfigurationManager.CurrentChanged
            → RuntimeReloadCoordinator          (RuntimeReloadCoordinator.cs)
            → classifier maps Modified → Restart (RuntimeReloadClassifier.cs:96)
            → coordinator does RemoveAsync + AddAsync  (RuntimeReloadCoordinator.cs:322 / :352)
              = full adapter teardown + rebuild: drops the device connection AND
                recreates the source's intake channel.
```

Three load-bearing facts:
- **`ISourceAdapter.ReconfigureAsync` has ZERO callers.** It is defined (default = Stop+Init+Start)
  and overridden by OPC UA Client, but the live edit path never invokes it. (`ISourceAdapter.cs:168`)
- **ADR-0009 §Decision 3 ("Modify == Restart, no reconfigure path") directly contradicts ADR-0015
  Rule 11 ("edit-mode tag changes MUST go through `ReconfigureAsync`").** Code followed 0009; Rule 11
  is unwired aspiration.
- A source restart recreates the intake channel; a route bound to the old channel can freeze
  ("Running but pointsIn = 0"). There is a reactive guard-conditional cascade fix already in
  (`RuntimeReloadCoordinator.cs:585-649`, commit `78646e4`). This **route/channel orphan (M1)** is the
  **leading hypothesis** for the field incident — **NOT yet confirmed** (needs repro or diagnostics).

## 3. The v2 design direction (what v3 must validate, not relitigate)

- **Central fix first:** a **stable, supervisor-owned ingress endpoint per source ID** that survives
  adapter generations, so route binding is correct regardless of whether an adapter supports live
  reconfigure. This is stronger than the route-rebind cascade. (v2 §4, §5-A1)
- **At-most-one-live-generation** invariant + generation tokens + lifecycle serialization;
  **teardown-timeout ⇒ quarantine, never evict-and-re-add.** (v2 §5-A2)
- **Adapter-owned, fail-closed delta classification** (`AppliedLive / RequiresInPlaceRestart /
  RequiresReplacement / UnsupportedOrInvalid`), unknown ⇒ replacement. No central field whitelist.
  (v2 §5-B1)
- **Live reconfigure is an optimization** to avoid device churn, layered on restart-in-place — it is
  NOT the mechanism that keeps routes bound. (v2 §2, §4)

## 4. v3 to-do — the six reality-check items (from v2 §12)

1. **Validate the stable-ingress design (highest priority).** Read `RouteDefinitionFactory.BuildOne`
   and the routing engine's intake resolution. Does a route bind the stable `ISourceIntake`, or does
   it snapshot a `ChannelReader` that would still go stale across a source restart? Today
   `SourceSupervisor.RegisterInternal` (`SourceSupervisor.cs:390`) creates a fresh channel per
   `AddAsync` — v3 must specify how to make the ingress survive generations.
2. Generation-token mechanics (where it lives; how late writes from an abandoned pump are dropped at
   the ingress boundary).
3. Quarantine UX/recovery for a teardown-timeout source (tie into fault-registry precedence, ADR-0007).
4. Atomic active-set swap for polling adapters — poll-batch boundary vs an explicit pump barrier.
5. Quantify the restart-in-place acquisition gap per protocol (so v2 §7 "bounded" gets a number).
6. **Does the stable ingress (Layer A) alone resolve the field incident** without Layer B/C? If yes,
   B/C become pure optimizations and can be deprioritized.

## 5. Constraints & guardrails (do NOT trip these)

- **This changes a LOCKED ADR (0009 §D3).** Per CLAUDE.md, locked decisions are not relitigated
  silently. The plan's resolution is a **new superseding ADR** (preserve 0009 + 0015 as historical;
  supersede only 0009 §D3; mark 0015 Rule 11 implementable). Surface this explicitly; don't work
  around it.
- **No code before v3 is locked.** This is planning cadence.
- **Static HTML mockup rule** does not apply yet (no UI in scope) — but if any Studio surface gets
  touched later, a static mockup needs operator sign-off first.
- **PR gate:** run the **full** `Management.Tests` project, never a filtered subset (filtered runs
  bypass cross-cutting isolation/schema guards — has shipped a broken PR before).
- **Cross-project boundary:** this is EdgeConnect-only (does not touch the EREMOS V2 MQTT contract),
  so no `C:\dev\shared-knowledge\` edits expected. Confirm if scope drifts.

## 6. Key files (verified this session)

| File | Role |
|------|------|
| `src/ElpisEdgeConnect.Host/RuntimeReloadCoordinator.cs` | The reconcile orchestrator. Remove+Add path, route-rebind cascade. |
| `src/ElpisEdgeConnect.Host/Adapters/SourceSupervisor.cs` | Per-source lifecycle, channel creation, pump loop. Ingress lives here. |
| `src/ElpisEdgeConnect.Core/Configuration/RuntimeReloadClassifier.cs` | Modified → Restart mapping. |
| `src/ElpisEdgeConnect.Core/Configuration/ConfigurationReloadPlan.cs` | `ReloadOp` enum (would gain a Reconfigure op). |
| `src/ElpisEdgeConnect.Core/Adapters/ISourceAdapter.cs` | The dormant `ReconfigureAsync` contract (line 168). |
| `src/ElpisEdgeConnect.Management/Api/SourcesUpdateApi.cs` | PUT edit path — proves it goes through apply pipeline, not ReconfigureAsync. |
| `src/ElpisEdgeConnect.Sources.ModbusTcp/ModbusTcpSourceAdapter.cs` | The reported adapter; `ScanPlan` rebuild lives in `InitializeAsync`. |
| `src/ElpisEdgeConnect.Sources.OpcUaClient/IOpcUaReconfigureExecutor.cs` | The one real live-reconfigure implementation — reference pattern for Layer C. |
| `docs/decisions/0009-runtime-hot-reload-instance-granularity.md` | Locked ADR being superseded (§D3). |
| `docs/decisions/0015-wizard-contract.md` | Rule 11 (the unwired hot-config invariant). |

## 7. Verification approach (for when code starts, v3+)

Mock-source regression proves the **shared** invariant (not all adapters). Use deterministic barriers,
not "pointsIn resumes". Per-adapter delta-case matrix is a **gate before enabling live reconfigure**
for that adapter. Full assertion list + delta cases in v2 §8.

## 8. State on disk / git

- Branch when authored: `master`. Sony works on `Sony_Development`.
- The three docs (v1, v2, this handoff) must be **committed and pushed to master** (or merged into
  Sony's branch) before a cold session can pick up — they are untracked at authoring time. **Confirm
  they are present on your branch before starting** (see human instructions).
- Nothing else is in-flight for this task; no code changed.
