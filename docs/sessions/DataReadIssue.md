# "Data stops reading from the source after some time" — root cause & fix

**Date:** 2026-07-01
**Branch:** `Sony_Development_DataIssue`
**Status:** Two real fixes committed + verified. **Committed to the branch:** the RouteWorker idle-stats
deadlock fix, the source intake-write bound, the route-lifecycle shutdown-crash fix, the FOCAS2
robustness work (cnc_setdtimeout, backoff jitter, connection-limit advisory, source alerts, config
validation), and the NU1902 build-audit fix, with tests. **Deliberately NOT committed:** the temporary
`DataLogIssue` diagnostic (§5) and the three speculative `FluentModbusClient` guards (§6) — reverted to
master. One deeper question stays open (§8.1) but a stalled route worker can no longer freeze a source.

---

## 1. The issue (as reported)

A source (Modbus TCP `Modbustest11` / `Modbustest1`, but the class is protocol-agnostic) would read
data normally and then, **after some time, stop producing data**. In the Studio the source still showed
**`Running`**, `points observed` frozen, and **"last point N mins ago"** climbing. Only a restart
recovered it. Same silent-stall *shape* as the FOCAS2 production incident (`gw-desktop-019ln49`).

Concrete symptom in the temporary per-source log files:
```
2026-06-30 19:44:25.096  OK - reading data (1 point(s)): t1=62450  | last point: just now (19:44:25)
   ... then FROZE (no further lines) ...
```
while the UI showed `Modbustest1 · Running · last point 2m ago`.

### 1a. Observed artifacts (verbatim, as seen during the investigation)

**Studio (Sources / Source-detail page)** — the source kept reporting healthy while data had stopped:
```
Modbustest1   modbustcp · Running
1,194 points observed
last point  2m ago            <-- climbing, while still "Running"
```
The "Last point … ago" label is rendered on `/sources`, `/sources/{id}` and `/routes/{id}`, sourced from
`RuntimeDiagnosticsCollector.LastPointAtUtc` (set on each successful observation). It freezes exactly when
the source stops producing — the original silent-stall "smoking gun".

**Temporary `DataLogIssue` file — the freeze point** (`Modbustest11.txt`): the last real line was a
successful read, then no more per-poll lines, only the independent watchdog firing for minutes:
```
2026-06-30 22:30:41       OK - reading data (1 point(s)): t1=62450  | last point: just now
2026-06-30 22:30:58.780   >>> WATCHDOG - NO POLL ACTIVITY for 17s: source still 'Running' but the poll is
                          STUCK (silent stall). DATA READING IS NOT HAPPENING.  | last point: 17s ago (22:30:41)
2026-06-30 22:31:…        >>> WATCHDOG - NO POLL ACTIVITY for 19s … | last point: 3m ago (…)
   ... kept firing every ~15-20s for minutes, "last point" climbing ...
```
The watchdog text ("stuck in a blocking read") was a hardcoded guess — it only knows *"no poll activity"*,
not *where*; the dumps later showed the real location was the intake-channel `WriteAsync`.

**Earlier, related class (FOCAS2) — same family, but detected (not silent):**
```
ALERT — FOCAS2 source '1420309-source' STOPPED producing data:
        FOCAS2.SOCKET_ERROR (Socket communication lost.)
```
That one *surfaces* (retryable network error → adapter reconnects); the Modbus case here was the *silent*
variant where the source froze with health still green.

**Process-crash variant seen during the same session (separate Core bug, fixed):**
```
System.InvalidOperationException: [CORE.ROUTE_INVALID_LIFECYCLE_TRANSITION]
   Route 'route-1520318' cannot transition from Failed to Stopped.
   at RouteLifecycleManager.TryTransitionToLocked(...)  at RoutingEngine.StopRouteAsync(...)
   at RoutingEngine.StopAllAsync(...)  at HostStartup.StopAsync(...)
```
An illegal `Failed → Stopped` transition during shutdown crashed the whole host — fixed separately in
`RoutingEngine.StopRouteAsync` (route it via `Failed → Stopping → Stopped`) and made `StopAllAsync`
resilient so one route can't abort shutdown.

---

## 2. The decisive evidence (managed dumps)

Guesswork was replaced with live process dumps of the wedged app (`dotnet-dump` / `dotnet-stack`).

**Thread stacks:** 13 threads, all idle pool workers + `Main` — **no thread blocked in a Modbus read,
socket, or our code.** So it was NOT a blocking synchronous read and NOT thread-pool starvation.

**Async stacks (`dumpasync`):** the poll loop was *suspended at an await that never completes*:

- `SourceSupervisor.RunPollLoopAsync` (state 3) → `Awaiting: ConfiguredValueTaskAwaiter`
  = **`Channel.Writer.WriteAsync`** → the source's **intake channel is full** and the write is blocked.
- `RouteWorker.RunIntakePumpAsync` (state 5) → `Awaiting … UnwrapPromise` = **`await idleStatsTask`
  in its `finally`** → the pump had left its drain loop and was parked in the finally.
- Zero pending `PollAsync` / `GuardReadAsync` chains (the "141" seen earlier were *completed* garbage
  from the `--completed` flag).

That chain is the whole story: **the route worker's intake pump stopped draining → the bounded intake
channel filled → the source poll loop blocked on `WriteAsync` → "the source stopped reading."** The MQTT
broker (`20.197.8.189:1883`) was reachable, so it was not a dead sink.

---

## 3. Root cause

### 3a. Bug #1 — idle-stats `finally` deadlock in the intake pump

`RouteWorker.RunIntakePumpAsync` starts a parallel **idle-stats** task:
```csharp
var idleStatsTask = Task.Run(async () => {
    while (!ct.IsCancellationRequested) { await Task.Delay(IdleStatsInterval, ct); ... }
}, ct);
...
finally { await idleStatsTask; }   // <-- deadlock
```
The idle-stats task **only ends on cancellation**. If the pump's drain loop exits for any
*non-cancellation* reason, the `finally` does `await idleStatsTask` while `ct` is **not** cancelled →
the idle-stats task loops forever → **the `finally` deadlocks**. The pump never returns → never drains
the channel → the source blocks on `WriteAsync`. Route still reports `Running`.

### 3b. Bug #2 — the source freezes when the route worker stalls/exits

`RouteWorker.RunAsync` is designed to end when the intake pump returns (`await RunIntakePumpAsync(ct)`
then `await Task.WhenAll(sinkTasks)`). So **anything** that makes the pump return (the deadlock above, a
transient exit, a completed channel) strands the source: the per-source intake channel uses
`BoundedChannelFullMode.Wait`, and an **unbounded `WriteAsync`** then blocks the source **forever** — even
though store-and-forward is supposed to *decouple* the source from downstream. This is the actual
"source stops reading" mechanism, independent of *why* the pump stopped.

---

## 4. The fixes (real, production code)

### Fix #1 — idle-stats task must stop when the pump exits
`src/ElpisEdgeConnect.Core/Routing/RouteWorker.cs`
- Give the idle-stats task its **own linked cancellation token** and **`Cancel()` it in the `finally`**
  before awaiting, so the await always completes regardless of *how* the drain loop exits:
```csharp
using var idleStatsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
// idle-stats loops on idleStatsCts.Token …
finally { idleStatsCts.Cancel(); try { await idleStatsTask; } catch { } }
```

### Fix #2 — the source must never freeze on the intake write
`src/ElpisEdgeConnect.Host/Adapters/SourceSupervisor.cs`
- Bound the intake-channel write. It still waits for room (normal backpressure while the route worker
  drains), but if the channel stays full for `IntakeWriteStallMs` (10 s) — meaning the route worker has
  stalled/exited — the source **keeps reading**, drops the rest of the batch, and logs the real reason:
```csharp
var writeTask = sup.Channel.Writer.WriteAsync(point, ct).AsTask();
if (await Task.WhenAny(writeTask, Task.Delay(IntakeWriteStallMs, ct)) != writeTask) {
    _logger.LogError("Source {Source}: intake channel full and not draining for {Seconds}s — "
        + "the route worker has stalled/exited. Keeping source alive; dropping remaining points this batch.",
        adapter.InstanceId, IntakeWriteStallMs/1000);
    break;
}
```

Together: Fix #1 removes the deadlock; Fix #2 guarantees the source can never be frozen by a downstream
stall (the store-and-forward decoupling the architecture promises).

---

## 5. Temporary diagnostic added (marked for removal — grep `DataLogIssue`)

`src/ElpisEdgeConnect.Host/Adapters/DataLogIssueWriter.cs` + hooks in `SourceSupervisor`:
- One `<sourceName>.txt` per source under a **`DataLogIssue`** folder next to the app binaries.
- Per-poll status: `OK - reading data (N): tag=value` / `OK - connected, no new data` /
  `NOT READING - <reason>`, each with the **"last point … ago"** value (system-local time) matching the
  Studio Sources page; plus `>>> DATA STOPPED/RESUMED` transition markers.
- An **independent watchdog `Timer`** that logs `>>> WATCHDOG - NO POLL ACTIVITY for Ns …` when a source's
  poll loop produces no activity — this is what surfaced the wedge (the poll loop can't log its own stall
  because it's parked). This preview of "independent progress liveness" (RC-2) only *reports*; the real
  structural fix (slice-0 / 3.1) will *act*.

> This whole feature is a single-file delete plus the `TEMP DataLogIssue`-marked lines in
> `SourceSupervisor`. It must be removed before shipping.

---

## 6. Wrong turns (recorded honestly, and to revert)

Before the dumps, the source-side blocking-read hypothesis led to three **speculative changes to
`FluentModbusClient`** that turned out to be the **wrong component** (the dump proved the read was not
the bottleneck):
1. `GuardReadAsync` — bound the synchronous read with `Task.WhenAny(read, Task.Delay(...))`.
2. `GuardConnectAsync` — same for the connect path.
3. `SwapPoisonedClient` — swap in a fresh `ModbusTcpClient` instead of a (blocking) `Disconnect()`.

These are defensible for a *genuine* read wedge but did **not** fix this issue and add complexity/risk.
**Recommendation: revert them.** (They are still local/uncommitted.)

---

## 7. Verification

- `RouteWorker`/routing: **175** Core routing tests pass.
- `SourceSupervisor`: **29** tests pass.
- Modbus: 245 tests pass. Full solution build **0 warnings / 0 errors**.
- Re-verified live against the running published build after each fix; the second dump confirmed the
  pump no longer deadlocks, which is what motivated Fix #2.

---

## 8. Open items / follow-ups

1. **Why does the intake pump exit at all?** Fix #1 removed the deadlock and Fix #2 isolates the source,
   so a pump exit can no longer freeze the source — but the *trigger* for the pump leaving its drain loop
   is not yet identified (no "worker faulted" was logged, so it may be a clean channel-completion path or
   a swallowed condition). With Fix #2 in place, the next occurrence logs
   `intake channel full and not draining` and (if the pump faulted) `worker faulted: …` — capture that to
   close this out.
2. **Revert the three `FluentModbusClient` speculative changes** (§6).
3. **Remove the `DataLogIssue` temporary diagnostic** (§5) before shipping.
4. Consider promoting Fix #2's "downstream stalled" signal into the normal health/diagnostics surface
   (it currently logs to `ILogger` + the temp file).
5. Add regression tests: (a) intake pump does not deadlock when its drain loop exits non-cancellation;
   (b) source poll loop does not block indefinitely when the intake channel is not drained.

---

## 9. Key files

| Area | File |
|------|------|
| Fix #1 (deadlock) | `src/ElpisEdgeConnect.Core/Routing/RouteWorker.cs` (`RunIntakePumpAsync`) |
| Fix #2 (source bound) | `src/ElpisEdgeConnect.Host/Adapters/SourceSupervisor.cs` (`RunPollLoopAsync`, `IntakeWriteStallMs`) |
| Temp diagnostic | `src/ElpisEdgeConnect.Host/Adapters/DataLogIssueWriter.cs` (+ `TEMP DataLogIssue` hooks) |
| To revert | `src/ElpisEdgeConnect.Sources.ModbusTcp/FluentModbusClient.cs` (`GuardReadAsync`/`GuardConnectAsync`/`SwapPoisonedClient`) |
