# M.P2.2 Phase 2.a — Tactical implementation plan v2

**Date:** 2026-05-16
**Branch:** `claude/m-p2-2-hot-reload` (tip: `c81011e` after Phase 2 design v2)
**Status:** **Locked.** Implementation may proceed on this plan.
**Related docs:**
- `docs/decisions/0009-runtime-hot-reload-instance-granularity.md` (ADR-0009)
- `docs/sessions/2026-05-16-mp22-kickoff.md` (milestone plan + A-J decisions)
- `docs/sessions/2026-05-16-mp22-phase2-design.md` (full Phase 2 design v2)

This is the tactical plan for Phase 2.a only. It supersedes the inline
v1 plan from the design-review chat session — v1 was reviewed and four
required fixes plus six accepted improvements are folded in here.

---

## 1. Scope and milestone boundary

**Phase 2.a delivers:** a refactor of `SourceSupervisor` and
`SinkSupervisor` so they expose per-instance `AddAsync` / `RemoveAsync`
/ `RestartAsync` methods with lifecycle-serialized internals. Boot-time
external behavior is bit-identical to today's; the new methods exist
but nothing in production code calls them yet. The supervisors gain a
`SemaphoreSlim`-based lifecycle gate, a dispose-idempotency flag, and
bounded per-instance shutdown timeouts.

**Phase 2.a does NOT touch:**

- `RegistrationFactory` — extracted in Phase 2.b
- Protocol `*RegistrationExtensions.cs` files — touched in Phase 2.b
- `RuntimeReloadCoordinator` — built in Phase 2.c
- `IConfigurationManager.CurrentChanged` subscription — wired in 2.c
- `HostStartup` / `CompositionRoot` / `EdgeConnectComposition` — touched in 2.c
- `SinkRegistration.RouteId` rename — deferred past Phase 2 entirely
- `ApplyResultDto.Reload` block / Razor changes — Phase 3

### 1.1 Formal scope rebaseline

The original kickoff doc bundled supervisor refactor +
`RegistrationFactory` + coordinator into a single Phase 2 commit. The
Phase 2 design v2 (`c81011e`) split that into three commits but did
so quietly; the v1 2.a plan inherited the split without flagging it.

This plan is the formal record of that rebaseline:

```
Phase 2.a — Supervisor lifecycle refactor (this doc)
Phase 2.b — RegistrationFactory + protocol-extension extractions
Phase 2.c — RuntimeReloadCoordinator + HostStartup wire-up
```

Reasoning: supervisor refactor is enough churn for one commit;
`RegistrationFactory` is orthogonal; smaller blast radius gives a
cleaner regression gate after each commit.

---

## 2. Non-goals clarified

The `_lifecycleGate` introduced in §3 is the most operationally
significant change in this milestone. Future contributors must
understand its boundary precisely.

> The lifecycle gate guarantees supervisor-internal serialization only.
> It does not provide cross-supervisor or cross-component transactionality.
> Coordinator-level orchestration remains responsible for global ordering.

Concretely:

- The gate serializes `SourceSupervisor`'s mutating methods against
  each other. It does **not** serialize `SourceSupervisor` against
  `SinkSupervisor` or against the routing engine. If a caller invokes
  `_sourceSupervisor.RemoveAsync("plc-1")` and `_sinkSupervisor.
  RemoveAsync("mqtt-1")` concurrently, both will run in parallel.
- The gate does **not** know about the routing engine. A route worker
  reading from a source's intake while `RemoveAsync` runs against
  that source will see the channel writer complete and exit cleanly —
  but the gate does not block the supervisor's work until the route
  acknowledges teardown.
- Cross-component ordering (routes before sources before sinks on
  teardown; sources before sinks before routes on bring-up) is **the
  coordinator's responsibility** (Phase 2.c, per ADR-0009 Decision 2).
  The supervisor's gate is the inner lock; the coordinator's
  `_reconcileSemaphore` is the outer lock; together they form the
  full single-flight contract.

This split is intentional. It keeps supervisor responsibilities
narrow and testable, and lets the coordinator (which doesn't exist
yet) own the system-level orchestration without back-pressure from
supervisor internals.

---

## 3. SourceSupervisor changes

### 3.1 State refactor

| Field | Today | After 2.a |
|---|---|---|
| `_supervised` | `Dictionary<string, SupervisedSource>` | `ConcurrentDictionary<string, SupervisedSource>` |
| `_stopCts` | one shared CTS for all pumps | **removed** — each `SupervisedSource` owns its own `Cts` |
| `_started` | boot-time flag | kept |
| **NEW** `_lifecycleGate` | n/a | `SemaphoreSlim(1, 1)` — gates every mutating public method |
| **NEW** `_disposed` | n/a | `int` — `Interlocked.CompareExchange` flag; `0` = alive, `1` = disposed |
| `SupervisedSource.Cts` | n/a | **new** — per-instance CTS, linked from the `ct` parameter passed to `StartInternal` |

### 3.2 Private helpers (internal machinery — caller holds gate)

```csharp
// Adds channel + SupervisedSource entry to map. Throws on duplicate.
// Pure: no I/O, no adapter touch.
private void RegisterInternal(SourceRegistration reg);

// Initialize → Start → record Running → launch pump.
// Caller already holds the gate AND the map entry.
// Throws on adapter failure; caller is responsible for rollback.
private async Task StartInternal(SupervisedSource sup, CancellationToken parentCt);

// Cancel CTS → complete writer → await pump (bounded) →
// adapter.StopAsync (bounded) → adapter.DisposeAsync (best-effort).
// Does NOT remove from map — caller does that.
// Idempotent on already-stopped.
private async Task StopInternal(SupervisedSource sup, CancellationToken ct);
```

### 3.3 Bounded shutdown timeouts in `StopInternal`

```csharp
private const int PerInstanceStopTimeoutMs = 10_000;

// Step 1: cancel CTS and complete writer concurrently. Readers
// see EOF immediately; pump exits via cancellation. Idempotent
// against the pump's own finally { Writer.TryComplete() }.
try { sup.Cts.Cancel(); } catch (ObjectDisposedException) { /* race */ }
sup.Channel.Writer.TryComplete();

// Step 2: bounded await on the pump. If it doesn't exit within
// PerInstanceStopTimeoutMs, log and orphan the task. The pump's
// writer is already completed and refs to its intake are dead;
// GC reclaims when the task finally exits. Acceptable in steady
// state — the next reconcile won't create another orphan unless
// it also times out.
if (sup.PumpTask is not null)
{
    try
    {
        await sup.PumpTask
            .WaitAsync(TimeSpan.FromMilliseconds(PerInstanceStopTimeoutMs), ct)
            .ConfigureAwait(false);
    }
    catch (OperationCanceledException) { /* expected */ }
    catch (TimeoutException)
    {
        _logger.LogWarning(
            "Source pump for {Source} did not exit within {Timeout}ms; abandoning task.",
            sup.Registration.Adapter.InstanceId, PerInstanceStopTimeoutMs);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Source pump for {Source} threw on stop.",
            sup.Registration.Adapter.InstanceId);
    }
}

// Step 3: bounded adapter.StopAsync. Enforces the graceful-stop
// ceiling at the supervisor layer regardless of adapter compliance.
using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
stopCts.CancelAfter(TimeSpan.FromMilliseconds(PerInstanceStopTimeoutMs));
try
{
    await sup.Registration.Adapter.StopAsync(stopCts.Token).ConfigureAwait(false);
    _healthSink.RecordSourceState(/* Stopped */);
}
catch (OperationCanceledException) when (stopCts.IsCancellationRequested && !ct.IsCancellationRequested)
{
    _logger.LogWarning("Source adapter {Source} StopAsync exceeded {Timeout}ms.",
        sup.Registration.Adapter.InstanceId, PerInstanceStopTimeoutMs);
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "Source adapter {Source} StopAsync threw.",
        sup.Registration.Adapter.InstanceId);
}

// Step 4: best-effort dispose. Never blocks map removal.
if (sup.Registration.Adapter is IAsyncDisposable d)
{
    try { await d.DisposeAsync().ConfigureAwait(false); }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Source adapter {Source} DisposeAsync threw.",
            sup.Registration.Adapter.InstanceId);
    }
}

sup.Cts.Dispose();
```

Distinct from the coordinator's own 5s `DisposeAsync` semaphore drain
(Phase 2.c, design doc §0.5 correction #4). They compose: supervisor
gives each instance up to 10s; coordinator gives the whole reconcile
up to 5s on shutdown. Worst case during shutdown: coordinator times
out at 5s and proceeds; supervisor's own dispose pass enforces its
10s per instance independently.

### 3.4 Public surface after 2.a

Every public mutating method follows this skeleton:

```csharp
public async Task AddAsync(SourceRegistration reg, CancellationToken ct)
{
    ThrowIfDisposed();
    await _lifecycleGate.WaitAsync(ct).ConfigureAwait(false);
    try
    {
        ThrowIfDisposed();  // re-check after gate acquisition
        // ... actual work via private helpers ...
    }
    finally
    {
        _lifecycleGate.Release();
    }
}

private void ThrowIfDisposed()
{
    if (Volatile.Read(ref _disposed) != 0)
        throw new ObjectDisposedException(GetType().Name);
}
```

| Method | Body |
|---|---|
| ctor(registrations, healthSink, logger) | For each reg: `RegisterInternal(reg)`. No gate (no instance exists yet). |
| `StartAsync(ct)` | Gate. If `_started`, return. For each entry: `await StartInternal(sup, ct)` inside an inner try/catch that does NOT exist in source path today — keep no try/catch on the source side to preserve current "bubble on any source init failure" behavior. Set `_started`. |
| `StopAsync(ct)` | Gate. If not `_started`, return. For each entry: `await StopInternal(sup, ct)`. Clear `_started`. |
| **`AddAsync(reg, ct)`** | Gate + `ThrowIfDisposed`. Throws if id in map. `RegisterInternal(reg)`. Try `StartInternal`; on throw, rollback map entry then rethrow. |
| **`RemoveAsync(id, ct)`** | Gate + `ThrowIfDisposed`. If id not in map, silent no-op (release gate and return). Else `await StopInternal(sup, ct)`; `_supervised.TryRemove(id, out _)`. |
| **`RestartAsync(newReg, ct)`** | Gate + `ThrowIfDisposed`. Holds the gate for the WHOLE remove+add sequence (does NOT call public `RemoveAsync` / `AddAsync` — would deadlock). Calls `StopInternal` for the old entry, removes from map, `RegisterInternal(newReg)`, tries `StartInternal`, rolls back on throw. |
| `GetIntake(id)` | **No gate.** Read-only path; uses `ConcurrentDictionary.TryGetValue`. Returns live channel or null. After Remove → null. After Add → fresh intake. |
| `DisposeAsync()` | `Interlocked.CompareExchange(ref _disposed, 1, 0)`; if already 1, return. Gate. For each entry: `StopInternal` then remove. Clear map. Release gate. Dispose gate. |

### 3.5 The channel resurrection contract (the dangerous bit)

When `RemoveAsync("plc-1")` completes:
- The old `SupervisedSource` is gone from the map.
- The old `Intake` is referenced only by whatever route definition
  was built before the remove. That intake's reader sees end-of-stream.

When `AddAsync(newReg)` then runs with the same id:
- A **brand new** channel is built; a brand new `Intake` is exposed
  via `GetIntake("plc-1")`.
- The route definition referencing the OLD intake is still pointing
  at a dead channel.

**The coordinator (Phase 2.c) MUST unregister the route BEFORE
calling `RemoveAsync` on the source, and re-register the route AFTER
`AddAsync` completes.** Phase 2.a's job is to make sure the supervisor
honors its side: `GetIntake` always returns the live channel, never
a stale reference.

---

## 4. SinkSupervisor changes

Mirror of §3, with these differences (locked):

| Aspect | Source | Sink |
|---|---|---|
| Channel | per-instance bounded `Channel<CanonicalDataPoint>` | none — routing engine calls `PublishAsync` directly |
| Per-instance CTS | yes (poll loop) | no (no background loop owned by supervisor) |
| `StartInternal` throws | bubble | bubble |
| Boot-time `StartAsync` exception | bubble (preserves current `SourceSupervisor` behavior) | **catch + record Failed + continue** (preserves current `SinkSupervisor` per-adapter isolation) |
| `AddAsync` exception behavior | bubble (coordinator catches in 2.c) | bubble (coordinator catches in 2.c) |

The boot-path try/catch in `SinkSupervisor.StartAsync` is **kept
around the call to `StartInternal`** so existing boot-time per-adapter
isolation is preserved bit-for-bit. `AddAsync` calls `StartInternal`
without that try/catch — coordinator's `TryWithFaultAsync` is the
sole hot-reload error handler.

### 4.1 Sink supervisor public surface (after 2.a)

Same skeleton (`ThrowIfDisposed` + gate + try/finally) as source.

| Method | Body |
|---|---|
| ctor | For each reg: `RegisterInternal(reg)` (which does the duplicate-id check). |
| `StartAsync(ct)` | Gate. If `_started`, return. For each: try `StartInternal`, catch (preserving the existing isolation pattern), record Failed if it threw, continue. |
| `StopAsync(ct)` | Gate. For each: try `StopInternal`, log on throw, continue. |
| **`AddAsync(reg, ct)`** | Gate + ThrowIfDisposed. `RegisterInternal` + try `StartInternal`, rollback + rethrow on throw. |
| **`RemoveAsync(id, ct)`** | Gate + ThrowIfDisposed. Silent no-op on unknown. `StopInternal` + remove. |
| **`RestartAsync(newReg, ct)`** | Gate + ThrowIfDisposed. Remove-half + add-half under single gate hold. |
| `Registrations` | **No gate.** Returns snapshot of current map. Used by coordinator in 2.c. |
| `DisposeAsync()` | Idempotent flag + gate + walk + dispose. |

### 4.2 Sink reference-counting (NOT in 2.a)

The supervisor's `RemoveAsync` **trusts the caller**. It does not peek
at `IConfigurationManager` to decide "is this sink still referenced
by other routes." That decision belongs to the coordinator (2.c) per
ADR-0009 Decision 2 and Phase 2 design correction #2. Test #8 in
§6.2 pins this invariant explicitly.

---

## 5. Implementation order (within 2.a)

A single commit, worked in this internal sequence:

| Step | Files touched | Why this order |
|---|---|---|
| 1 | `src/ElpisEdgeConnect.Host/Adapters/SourceSupervisor.cs` — extract `RegisterInternal`, `StartInternal`, `StopInternal`; add `_lifecycleGate` + `_disposed` fields; rewire ctor + `StartAsync` + `StopAsync` to use the helpers + gate. **Do not add new public methods yet.** | Pure refactor + new internal mechanics. Boot path behavior must remain bit-identical externally. Run full test suite — should still be 1642/1642. |
| 2 | Same file — add `AddAsync`, `RemoveAsync`, `RestartAsync`, `ThrowIfDisposed`, idempotent `DisposeAsync`. | Pure addition. Existing tests unchanged. |
| 3 | `tests/ElpisEdgeConnect.Host.Tests/Adapters/SourceSupervisorAddRemoveRestartTests.cs` (new) — 19 + 3 = 22 new tests (see §6.1). | TDD-ish: tests fail-then-pass per method. |
| 4 | `src/ElpisEdgeConnect.Host/Adapters/SinkSupervisor.cs` — same refactor pattern (extract helpers; boot path try/catch wraps `StartInternal`; add gate + dispose flag). | Mirror set, no new architectural ground. |
| 5 | Same file — add public hot-reload methods + dispose idempotency. | |
| 6 | `tests/ElpisEdgeConnect.Host.Tests/Adapters/SinkSupervisorAddRemoveRestartTests.cs` (new) — 14 + 3 = 17 new tests (see §6.2). | |
| 7 | Build + full test sweep. Expect 1642 + 39 = 1681. | |

**Run the full test suite after step 1** before adding any new public
surface — that's the regression gate proving the refactor didn't
break the boot path.

---

## 6. Test list (39 tests, named)

### 6.1 `SourceSupervisorAddRemoveRestartTests` (22)

Per-method coverage:

1. `AddAsync_NewInstance_StartsAdapterAndPumpAndRecordsRunning`
2. `AddAsync_DuplicateId_Throws`
3. `AddAsync_AdapterInitializeAsyncThrows_PropagatesAsAdapterException`
4. `AddAsync_AdapterInitializeAsyncThrowsGeneric_PropagatesToCaller_AndRollsBackMap`
5. `AddAsync_AdapterStartAsyncThrows_PropagatesToCaller_AndRollsBackMap`
6. `RemoveAsync_RunningInstance_StopsAdapterAndCompletesChannel`
7. `RemoveAsync_UnknownId_IsSilentNoOp`
8. `RemoveAsync_DoesNotAffectOtherInstances`
9. `RemoveAsync_DuringActivePump_NoExceptionEscapes`
10. `RestartAsync_ConstructsBrandNewChannel`
11. `RestartAsync_NewIntakeIsNotEqualToOldIntake`
12. `RestartAsync_AdapterInitThrowsOnAddHalf_LeavesInstanceRemovedNotPartial`
13. `GetIntake_AfterRemove_ReturnsNull`
14. `GetIntake_AfterRestart_ReturnsLiveChannel`
15. `BootStartAsync_RegressionPin_StartsAllRegisteredSources`
16. `BootStopAsync_RegressionPin_StopsAllSources`
17. `DisposeAsync_RegressionPin_DrainsAndDisposesAdapters`
18. **`RestartAsync_DoesNotLeakOldPumpTask`** *(review item #9 — assert old pump observed-completed before new one exists)*
19. **`DisposeAsync_WhileAddAsyncInFlight_DoesNotCorruptState`** *(review item #10 — gate contract)*

Lifecycle-gate + dispose contract:

20. **`AfterDisposeAsync_AddAsync_ThrowsObjectDisposedException`** *(dispose flag guard)*
21. **`AfterDisposeAsync_RemoveAsync_ThrowsObjectDisposedException`** *(same)*
22. **`Concurrent_AddAsyncAndRemoveAsync_SameInstance_AreSerialised`** *(lifecycle gate — fire both, observe sequential health-event order)*

### 6.2 `SinkSupervisorAddRemoveRestartTests` (17)

Per-method coverage:

1. `AddAsync_NewInstance_StartsAdapterAndRecordsRunning`
2. `AddAsync_DuplicateId_Throws`
3. `AddAsync_AdapterInitializeAsyncThrows_PropagatesAsAdapterException`
4. `AddAsync_AdapterInitializeAsyncThrowsGeneric_PropagatesToCaller_AndRollsBackMap`
5. `RemoveAsync_RunningInstance_StopsAdapterAndRecordsStopped`
6. `RemoveAsync_UnknownId_IsSilentNoOp`
7. `RemoveAsync_DoesNotAffectOtherInstances`
8. `RemoveAsync_DoesNotPeekAtConfig_TrustsCaller` *(§4.2 invariant pin)*
9. `RestartAsync_ReplacesAdapter`
10. `RestartAsync_AdapterInitThrowsOnAddHalf_LeavesInstanceRemovedNotPartial`
11. `BootStartAsync_RegressionPin_PerAdapterIsolationPreserved`
12. `BootStopAsync_RegressionPin_StopsAllSinks`
13. `DisposeAsync_RegressionPin_DrainsAndDisposesAdapters`
14. **`DisposeAsync_WhileAddAsyncInFlight_DoesNotCorruptState`** *(review item #10)*

Lifecycle-gate + dispose contract:

15. **`AfterDisposeAsync_AddAsync_ThrowsObjectDisposedException`**
16. **`AfterDisposeAsync_RemoveAsync_ThrowsObjectDisposedException`**
17. **`Concurrent_AddAsyncAndRemoveAsync_SameInstance_AreSerialised`**

### 6.3 Test machinery

- `MockSourceAdapter` and `MockSinkAdapter` already exist in
  `tests/ElpisEdgeConnect.MockAdapters/`. Use them.
- New test files: `tests/ElpisEdgeConnect.Host.Tests/Adapters/{Source,Sink}SupervisorAddRemoveRestartTests.cs`.
- No new fixtures or test base classes needed.
- No integration tests in 2.a — those land in 2.c with the coordinator.

---

## 7. Risks & mitigations

| Risk | Mitigation in 2.a |
|---|---|
| Boot-path behavior drift from refactor | Step 1 is pure extraction. Step-1 test sweep is the regression gate. Tests #15-17 in §6.1 and #11-13 in §6.2 pin the boot path explicitly. |
| Channel resurrection: old `Intake` outliving the channel | Tests #10, #11, #13, #14, #18 in §6.1 pin the supervisor's side. **Coordinator side (route-unregister-before-source-remove) is 2.c's responsibility** — not addressed here. |
| Per-instance CTS leaking on `RemoveAsync` failure | `StopInternal` disposes `sup.Cts` at the end unconditionally. |
| Half-failed `AddAsync` leaving map entry | Tests #4, #5, #12 pin map rollback on `StartInternal` throw. |
| Existing per-adapter isolation regressing on sinks | Test #11 in §6.2 pins: `BootStartAsync` still records Failed for one sink and continues with others. The try/catch around `StartInternal` in `SinkSupervisor.StartAsync` is the regression-preserving line. |
| Pump task orphaned after StopInternal timeout — could linger in memory | Acceptable: writer is completed, intake refs are dead, GC reclaims when the task finally exits. No leak in steady state because the next reconcile won't create another orphan unless it also times out. Test #18 in §6.1 pins that the new pump after restart is the only live one. |
| Deadlock from public method calling another public method (e.g., `RestartAsync` calling `RemoveAsync` + `AddAsync`) | `RestartAsync` holds the gate for the whole sequence and invokes **private** helpers directly — never `RemoveAsync` / `AddAsync` public methods. |
| `DisposeAsync` racing in-flight `AddAsync` | Gate + idempotent flag. Tests #19/14 in §6.1/§6.2 pin the contract. |

---

## 8. Definition of done

1. `dotnet build ElpisEdgeConnect.sln --nologo` is **0 warnings, 0 errors**.
2. `dotnet test ElpisEdgeConnect.sln --filter "Category!=Flaky" --no-build` passes — total **1681/1681** (1642 baseline + 39 new).
3. All 22 + 17 = 39 named tests above exist by exact name and pass.
4. The boot-path regression pins (3 tests per supervisor) pass — proves the refactor didn't break the existing locked behavior pinned in those files' headers.
5. No file outside `src/ElpisEdgeConnect.Host/Adapters/{Source,Sink}Supervisor.cs` is modified (test files don't count).
6. The supervisor file headers gain an "M.P2.2 phase 2.a" line at the bottom of the existing LOCKED design rules block, naming the new public surface plus the lifecycle-gate and dispose-flag invariants.

---

## 9. Pause-point criteria before continuing to 2.b

Stop after the 2.a commit lands and report back if **any** of these
surface:

- Boot-path tests started failing after the refactor and required
  non-trivial fixes (more than 1-2 lines of behavior change).
- A channel-resurrection test revealed an architectural problem not
  accounted for (e.g., the route-definition layer holds the intake
  by-value in some way that escapes current analysis).
- An adapter type with a contract that surprises the
  `RegistrationFactory` extraction work in 2.b.
- Lifecycle-gate test reveals a deadlock or starvation scenario.

Otherwise: continue straight to 2.b on the same branch.

---

## 10. Review disposition log (v1 → v2)

ChatGPT review pass, 2026-05-16. All 10 items accepted with the
following dispositions:

| # | Item | Disposition | Where applied |
|---|---|---|---|
| 1 | RegistrationFactory scope drift | Accept rescope; document formally | §1.1 |
| 2 | Lifecycle semaphore (required fix) | Accept | §3.1 `_lifecycleGate`; §3.4 method skeleton; §4.1 |
| 3 | Bounded shutdown timeout (required fix) | Accept | §3.3 |
| 4 | Dispose idempotency (required fix) | Accept | §3.1 `_disposed`; §3.4 `DisposeAsync`; §3.4 `ThrowIfDisposed` |
| 5 | Forbid parallel restart | Accept (covered by #2) | §3.4 `RestartAsync` row; test #22 |
| 6 | Map-rollback invariant | Accept (already in plan) | §3.4 `AddAsync` + `RestartAsync` rows |
| 7 | StopInternal order | Accept | §3.3 step 1 |
| 8 | Trust caller (no config peeking) | Accept (already in plan) | §4.2; test #8 (sink) |
| 9 | `RestartAsync_DoesNotLeakOldPumpTask` test | Accept | §6.1 test #18 |
| 10 | `DisposeAsync_WhileAddAsyncInFlight_DoesNotCorruptState` test | Accept | §6.1 #19, §6.2 #14 |

Plus three derivative tests (§6.1 #20-22, §6.2 #15-17) for the
lifecycle-gate and dispose-flag contracts introduced by #2 and #4.

Net test count: 30 (v1) → 39 (v2).

---

**End of Phase 2.a v2 plan. Locked. Implementation may proceed.**
