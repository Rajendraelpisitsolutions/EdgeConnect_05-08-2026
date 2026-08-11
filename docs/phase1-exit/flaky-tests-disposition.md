# Flaky Tests — Disposition for Phase 1 Exit

**Milestone:** D phase 1 (stabilization)
**Locked policy:** Convert to deterministic where possible; quarantine only when the assertion fundamentally depends on wall-clock throughput. Quarantined tests carry `[Trait("Category","Flaky")]` and run in CI but do NOT block the Phase 1 exit gate. The gate filter is `dotnet test --filter "Category!=Flaky"`.

---

## Discovery summary

Five tests were initially observed to flake intermittently under full-suite load during C3 and C4 development. All pass cleanly in isolation. After extended stabilization runs, additional flakes were observed in sibling tests, converging on a single structural root cause:

**Thread-pool starvation under parallel test-class execution.** The routing- and diagnostics-engine integration tests drive real `Task.Run` workers, real `Task.Delay` backoffs, and real cursor-based buffer loops. When xUnit runs multiple such classes in parallel, the shared thread pool saturates and individual tests' polling waits get pushed past their timeouts.

**Combined fix (applied in D phase 1):**

1. **Per-test stabilization** (deterministic control where feasible) — replaced wall-clock `PublishDelay` / `FailPermanently` toggles with `SemaphoreSlim` gates and `TaskCompletionSource` failure windows on `FakeSinkAdapter`.
2. **Timeout relaxation** (for "eventually" assertions) — bumped `EventuallyAsync` / `WaitForAsync` timeouts from 2–5 s to 10–15 s where the assertion is about eventual convergence, not throughput.
3. **xUnit collection serialization** — the integration-heavy test classes now share an `xUnit` collection (`RoutingIntegrationCollection`, `DisableParallelization = true`). Tests within the collection run sequentially; unit tests in other collections still run in parallel.
4. **Quarantine** — one genuinely wall-clock-bound throughput test is quarantined via `[Trait("Category","Flaky")]` with a deterministic companion pinning the same ordering/zero-loss invariants.

## Disposition table

| # | Test | Root cause | Approach | Disposition |
|---|---|---|---|---|
| 1 | `RoutingEngineFanoutTests.SlowSink_DoesNotBlockFastSink` | `PublishDelay = 40 ms` on slow sink + `fastElapsed < 2 s` stopwatch assertion. Thread starvation under parallel load breaks the 2 s budget. | Replace `PublishDelay` with a `ManualResetEventSlim` gate. Fast sink drains deterministically; slow sink blocks on the gate; test asserts `slow.PublishedCount == 0` *before* signaling. No stopwatch. | **Stabilize** — convert to gate-based control. |
| 2 | `RuntimeDiagnosticsCollectorEngineWiringTests.RoutingEngine_EmitsAllEventsIntoCollector` | `FailPermanently = true` toggle race with sink publish loop. Transitions may be missed if heal lands between a retry backoff and the next publish attempt. | Replace the flag toggle with a `FailUntilSignaled` gate driven by a `TaskCompletionSource`. Heal is deterministic (signal the TCS). | **Stabilize** — TCS-based failure gate. |
| 3 | `RoutingEngineEndToEndTests.RoutingEngine_EndToEnd_5kPtsPerSec_30sOutage_ZeroLossAndOrdered` | Asserts `TotalPoints / produceElapsed > 5000 pts/sec`. Genuine wall-clock throughput pin. Cannot be made deterministic without abandoning the throughput assertion (the whole point of the test). | Quarantine with `[Trait("Category","Flaky")]` and a scaled-down deterministic companion test that pins ordering + zero-loss without the throughput assertion. | **Quarantine** + add deterministic companion. |
| 4 | `SqliteBufferCursorAndRetentionTests.MultiSink_Divergence_FastDrains_SlowPinsTail` | `EventuallyAsync(..., 2 s)` waits for the internal SqliteBuffer reclaim loop, which runs on its own timer. Under load the reclaim loop doesn't get CPU in time. | Relax the polling timeout to 10 s. Same treatment applied to sibling SqliteBuffer tests (`DeregisterSink_ReleasesPinnedData`, retention-based eviction waits) which share the same reclaim-loop dependency. | **Stabilize** — timeout relaxation (all EventuallyAsync calls in the file). |
| 5 | `RoutingEngineRetryTests.Retry_RouteRestart_ResetsState` | `WaitForAsync(() => sink.AttemptCount >= 3, 2 s)`. Retry backoff + CPU starvation can exceed 2 s under load. The test is asserting "retries happened", not "retries happened within 2 s". | Relax the polling timeout to 10 s. | **Stabilize** — timeout relaxation. |

## Summary by disposition

- **Stabilized (4):** #1, #2, #4, #5 — converted to deterministic or relaxed-timeout form
- **Quarantined (1):** #3 — the 5 k pts/sec throughput test. Genuine wall-clock assertion; scaled-down deterministic companion added.
- **Collection serialization:** integration classes `RoutingEngineBackpressureTests`, `RoutingEngineConcurrencyTests`, `RoutingEngineEndToEndTests`, `RoutingEngineFanoutTests`, `RoutingEngineHappyPathTests`, `RoutingEngineReplayTests`, `RoutingEngineRetryTests`, `SqliteBufferCursorAndRetentionTests`, `DiagnosticsEndToEndTests`, `RuntimeDiagnosticsCollectorEngineWiringTests` are all marked `[Collection(RoutingIntegrationCollection.Name)]`, forcing sequential execution across the thread-pool-heavy tests.

## Stability verification

After all fixes, **15 consecutive full-suite runs** passed with the Phase 1 gate filter (`Category!=Flaky`). Run durations 10–26 s (versus ~4 s before collection serialization — the extra cost is acceptable). Zero flakes observed across 15 runs.

## Phase 1 exit gate

The Phase 1 exit gate CI command is:

```
dotnet test --filter "Category!=Flaky"
```

Every test not carrying the `Flaky` category trait MUST pass. Currently exactly one test (`RoutingEngine_EndToEnd_5kPtsPerSec_30sOutage_ZeroLossAndOrdered`) carries the trait, with the rationale documented in its header comment and cross-referenced to row 3 of this table.

## Hard rule for future quarantine additions

A test may be quarantined **only if** its assertion fundamentally depends on wall-clock throughput, real CPU timing, or observation of asynchronous pipeline flow in real time. Any test that can be made deterministic via explicit signaling, virtual time, or a longer polling timeout **MUST** be fixed rather than quarantined. Additions to the quarantine set require an updated row in this file with an explicit rationale.
