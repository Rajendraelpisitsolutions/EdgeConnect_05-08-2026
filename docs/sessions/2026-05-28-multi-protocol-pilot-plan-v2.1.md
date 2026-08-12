# Multi-protocol pilot expansion — plan v2.1 (reality-check)

**Status:** v2.1 — reality-check pass folding library audits + existing-codebase inspection into v2. Implementation contract locked here.
**Date:** 2026-05-28.
**Supersedes:** [v2](2026-05-28-multi-protocol-pilot-plan-v2.md) (kept for reference). v1 archived.
**Audit inputs:**
- OPC Foundation .NET stack audit (Axes 1+4) — full report in agent transcript.
- libplctag.NET audit (Axis 1, EtherNet/IP track) — full report in agent transcript.
- Existing-codebase inspection (Axes 2+3+5+6) — `Core/Model`, `Core/Buffer`, `Core/Routing`, `Core/Pipeline`, `Core/Adapters` — read directly.

---

## §0 What v2.1 changes from v2

| § | v2 said | v2.1 reality |
|---|---|---|
| §1.3.1 (CDP pooling) | "Object pooling for CanonicalDataPoint + metadata dictionaries — strict zero-alloc hot path" | **NOT NEEDED.** CDP is already a `sealed record` documented "designed to be immutable and thread-safe so the same instance can be dispatched to multiple sinks concurrently without copying." At 30K/sec with `IntakeBatchSize=256` we have ~117 `List<CDP>` allocations/sec — trivial. Pool only if measurement shows a problem. |
| §1.3.2 (bypass) | "Pipeline calls `IdentityTransform` IF zero transforms configured" | **ALREADY IMPLEMENTED.** `TransformPipeline.Execute` line 58–61: `if (_steps.Count == 0 \|\| input.Count == 0) return input;`. v2 invariants 1–6 still hold as documentation locks, not as new code. |
| §1.3.3 (SQLite batched commits) | "New `BufferMode = Batched \| WriteThrough` per-source policy" | **REFRAMED.** The existing `BufferMode` enum is orthogonal (storage location: None / InMemory / StoreAndForward). The existing `IMessageBuffer.EnqueueAsync` already takes `IReadOnlyList<CanonicalDataPoint>` — one transaction per batch. At 30K/sec → ~117 SQLite commits/sec, well within SSD fsync capacity. **No new dimension needed.** WAL + `synchronous=FULL` is locked in `SqliteBufferSchema.WriterConnectionPragmas`. |
| §1.3.4 (per-sink bounded channels) | "Each sink has its OWN bounded `Channel<CanonicalDataPointBatch>`" | **NOT NEEDED — the existing design is better.** `FanoutDispatcher` is wake-only (carries no payloads); each sink has its own cursor in the buffer + its own publisher Task + its own retry budget. A slow sink only delays its own cursor advance. Locked design rule in `FanoutDispatcher.cs`: "The dispatcher NEVER carries CanonicalDataPoint payloads. Points live in the route buffer; the dispatcher only signals 'go look again'." This is the same property v2 §1.3.4 was trying to construct. |
| §1.3.5 (ReconfigureAsync) | "New `ReconfigureAsync` on `ISourceAdapter`" | **STANDS — only genuinely new platform work.** Confirmed not present on `ISourceAdapter`. Default interface method with stop+initialize+start fallback adds it non-breakingly to the 5 existing adapters. |
| §1.3.6 (P4 cross-check) | Audit table | **STANDS — documentation lock, no code change.** |
| Overall §1.3 effort | "~3 weeks of cross-cutting performance hardening" | **~1 week** — benchmark, identify actual hot spot, fix only what's measurably slow. The platform is more ready than v2 assumed. |
| Pilot timeline | "10–12 weeks (Option B hybrid)" | **7–9 weeks (Option B hybrid)** — perf hardening shrinks from 3w to 1w; OPC UA Client + EtherNet/IP at full quality still 4w + 4w, parallel from week 3. |
| 50K stretch target | "Gated on stack-ceiling measurement" | **Re-locked as gated.** OPC UA audit found issue #2276 documenting performance degradation starting at ~30K monitored items on a single client against Kepware. 50K is "genuinely uncertain" without our measurement. **Week-1 measurement is a hard gate**, not soft check. |

---

## §1 Existing codebase audit (Axes 2/3/5/6)

### 1.1 What we already have

| Concern | Existing answer |
|---|---|
| **CDP immutability** | `sealed record` + `IReadOnlyDictionary<string, object>? Metadata` + `init` properties + documented "designed for concurrent fan-out without copying." Already shippable for 30K/sec. |
| **Type-runtime consistency** | `IsConsistent` / `TryValidateConsistency` exist for tests + diagnostics. Hot path does NOT validate (documented locked decision — "per-point type check would double allocation cost at sustained throughput targets"). Aligns with v2 §1.3 hot-path principle. |
| **Batched buffer enqueue** | `IMessageBuffer.EnqueueAsync(IReadOnlyList<CDP>, ct)` is the contract. Buffer treats each call as one transaction. |
| **SQLite durability** | WAL mode + `synchronous=FULL` + single writer connection w/ mutex + separate read connection. `quick_check` on open. Reclaim loop separate from ack handlers — ack latency bounded by one row UPDATE + COMMIT. D1–D14 locked. |
| **Per-sink isolation** | Per-sink cursor in buffer + per-sink publisher Task + per-sink retry state machine + per-sink degraded/draining lifecycle. `FanoutDispatcher` is wake-only — payload-free signals. |
| **Backpressure policy** | `BackpressureController` (pure policy, Pass/Spill/Drop) + `RouteChannelSpillover` (pure mechanism, writes overflow to buffer). Policy/mechanism split is locked. |
| **Pipeline double-buffering** | `TransformPipeline._bufferA` / `_bufferB` swap between steps — no list alloc per invocation. |
| **Transform purity (§1.3.2 invariant 6)** | The 4 existing steps (Filter, Deadband, RateLimit, TagMapping) read CDP fields only — never mutate input. Internal per-tag bookkeeping (Deadband's `_last`, RateLimit's `_lastEmitted`) is per-route state, not input mutation. **Invariant 6 already holds.** TransformPipeline is documented single-thread-only, so the bookkeeping is safe. |
| **Route lifecycle hot-config foundation** | `RouteLifecycleManager` is the single authority on route state. Locked: "RouteWorker, SinkPublisher, and RoutingEngine NEVER decide a route state on their own — they call TryTransitionTo / NotifySink*." Solid foundation for `ReconfigureAsync` to hook into. |

### 1.2 What §1.3 actually needs (post-audit)

Reduced from 6 subsections to **3 actual deliverables**:

1. **`ReconfigureAsync` on `ISourceAdapter`** (§1.3.5) — new interface member with default-impl. Estimated 2–3 days including default-impl + tests across the 5 existing adapters.
2. **Performance benchmark suite + targeted fixes** (replaces §1.3.1 / §1.3.3 / §1.3.4) — build benchmarks, measure, fix only what's measurably slow. Estimated ~1 week. Likely no code changes needed beyond OPC UA Client tuning if benchmarks confirm the existing pipeline handles 30K/sec cleanly.
3. **Invariant documentation** (§1.3.2 / §1.3.6) — write the locked invariants into `docs/core/canonical-data-model.md` + the relevant module docs. No code change.

### 1.3 §1.3 invariants re-locked against existing code

Each invariant from v2 §1.3.1–§1.3.6 is re-examined against actual existing code:

**§1.3.1 (CDP ownership):** The v2 risk was "pool returns CDP while sink still holds it." With CDPs NOT pooled, this risk disappears entirely. Existing immutable-record design + per-sink-cursor architecture means each sink consumes from the buffer at its own pace; there's no shared in-memory CDP that one sink could return while another holds it. **Invariant downgraded to: "CDPs flowing through the runtime are owned by the route buffer; sinks read but do not mutate; sinks may retain references freely because nothing recycles the underlying memory."**

**§1.3.2 (bypass):** Already implemented (line 58–61 of `TransformPipeline`). Invariants 1–6 from v2 are validated:
- Invariant 1 (CDP immutable post-publication): ✅ — `sealed record`, `init` properties, `IReadOnlyDictionary` metadata.
- Invariant 2 (sinks read but not mutate): ✅ — type system enforces.
- Invariant 3 (bypass preserves diagnostics): ✅ — bypass is at the pipeline level; diagnostics live in `RouteWorker` + `SinkPublisher`, downstream of the pipeline.
- Invariant 4 (determinism): ✅ — bypass returns the input list reference; output is byte-identical to a copy. Test surface: add `Determinism_BypassedRoute_VsIterating_IdenticalOutput` to pin.
- Invariant 5 (P4 explainability): ✅ — bypass does not skip per-CDP diagnostics counters because those fire on `SinkPublisher.PublishWithRetryAsync`, post-pipeline.
- Invariant 6 (transform purity): ✅ — audited 4 steps; none mutate input. Pin in v2.1 doc.

**§1.3.3 (SQLite batched commits):** Reframed entirely. The existing buffer batches by definition (`EnqueueAsync` takes a list). Per-CDP commits were never the design. The v2 concern doesn't apply. **No new `BufferMode` dimension needed.** Existing `BufferMode { None | InMemory | StoreAndForward }` stays as-is.

**§1.3.4 (per-sink isolation):** The existing wake-only `FanoutDispatcher` + per-sink-cursor architecture already provides isolation. The v2 §1.3.4 invariants 1–5 are validated:
- Invariant 1 (per-sink bounded channels): NOT NEEDED — buffer cursors do the same job durably.
- Invariant 2 (drop accounting): ✅ — `BufferStats.TotalDropped` + per-sink `DroppedByCapacity` / `DroppedByRetention` already exist.
- Invariant 3 (backpressure does not propagate to source): ✅ — `BackpressureController.Decide` returns `Drop` rather than blocking; `RouteChannelSpillover` writes to buffer non-blockingly. Documented in `BackpressureController.cs`: "Sources are never blocked indefinitely per blueprint §19.8."
- Invariant 4 (refcount handles cross-sink decoupling): NOT NEEDED — no refcounting, no shared in-memory batches. Each sink's cursor advances independently.
- Invariant 5 (failed batches retry without blocking siblings): ✅ — `SinkPublisher.PublishWithRetryAsync` retries within its own loop; other sinks unaffected.

**§1.3.5 (ReconfigureAsync atomicity):** STANDS. New work. Implementation locked here:

```csharp
// In ISourceAdapter:
Task ReconfigureAsync(SourceConfiguration newConfig, CancellationToken ct)
{
    // Default implementation — safe fallback for adapters that don't
    // implement true hot-reconfigure. New adapters (OPC UA Client,
    // EtherNet/IP) override with the real atomic-active-set-swap.
    return ReconfigureViaRestartAsync(newConfig, ct);

    async Task ReconfigureViaRestartAsync(SourceConfiguration cfg, CancellationToken ct)
    {
        await StopAsync(ct).ConfigureAwait(false);
        await InitializeAsync(cfg, ct).ConfigureAwait(false);
        await StartAsync(ct).ConfigureAwait(false);
    }
}
```

Active-set snapshot, reconfigure-during-reconfigure guard, and validation-before-swap rules from v2 §1.3.5 stay as the OPC UA Client and EtherNet/IP override contract.

**§1.3.6 (P4 cross-check):** Documentation-only. Lock the audit table from v2 in `docs/core/performance-hot-path.md` (new file).

### 1.4 Test surface adjustments

| Test | v2 said | v2.1 says |
|---|---|---|
| `Pool_AcquireRelease_AllSinksAndBuffer_ReturnsToPool` | Required | **Removed** — no pool. |
| `Pool_SinkThrows_BatchStillReturnsToPool` | Required | **Removed.** |
| `Pool_SinkRetainsReferenceAfterPublishReturns_DetectedByLifecycleAssertion` | Required | **Removed.** |
| `Replay_BufferReadProducesNonPooledCdps` | Required | **Removed.** |
| `Pool_BatchHandleNeverEscapesChannel` | Required | **Removed.** |
| `Bypass_TwoFanOutSinks_BothSeeIdenticalCdpReferences` | Required | **Kept** — pin bypass behaviour. |
| `Bypass_VsIterating_ByteIdenticalOutput` | Required | **Kept.** |
| `Bypass_PerCdpDiagnosticsCountersIncrementIdentically` | Required | **Kept.** |
| `Pipeline_AnyTransformConfigured_DoesNotBypass` | Required | **Kept.** |
| `Determinism_BypassedRoute_ReplayedTwice_IdenticalOutput` | Required | **Kept.** |
| `Reconfigure_UnderSustainedLoad_NoDataLoss` | Required | **Kept** — new test for §1.3.5. |
| `Reconfigure_DuringReconfigure_ThrowsInProgressError` | Implied | **Kept** — explicit. |
| `Reconfigure_ValidationFails_LeavesActiveSetUnchanged` | Implied | **Kept** — explicit. |

Net new test count for §1.3 work: **~12** (down from v2's estimated ~25).

---

## §2 OPC UA Foundation stack audit (Axes 1+4)

### 2.1 API surface — all green for §1.1

Every §1.1 functional surface item is in the box without forking:
- `Session.CreateAsync` via `DefaultSessionFactory` (reference: `ConsoleReferenceClient/UAClient.cs`).
- `Subscription.AddItems` / `RemoveItems` / `ApplyChanges` / `ModifyItems` for §1.3.5 hot config.
- `SessionReconnectHandler` with `TransferSubscriptions` first / recreate fallback. Set `DeleteSubscriptionsOnClose=false`.
- `Session.Browse` with continuation points for lazy traversal.
- `DataTypeSystem` + complex-type fetcher for ExtensionObject decoding.
- `UserIdentity` for Anonymous / UserName / X.509.
- `netstandard2.1` + `net8.0` NuGet target — Linux clean.

### 2.2 The critical hot-path bridge

The OPC UA stack does NOT block the network thread on callbacks. BUT:

- With `SequentialPublishing=true` (REQUIRED for ordering), each subscription processes callbacks single-threaded on a worker task.
- A slow `FastDataChangeCallback` blocks the next message in that subscription's queue.
- **The callback MUST do nothing but enqueue into our `Channel<NotificationBatch>` and return.** Channel-based dispatch is not optional.
- `SequentialPublishing=false` runs callbacks concurrently with out-of-order delivery → violates §19.6 ordering. Not acceptable.

This validates v2 §1.3 channel-based dispatch as a hard requirement for OPC UA Client.

### 2.3 Subscription limits

- **1,000 monitored items per subscription** is the universal server-side ceiling (Issue #564).
- 30K target → **30 subscriptions per session**; 50K → **50 subscriptions**.
- `MinPublishRequestCount` MUST be ≥ subscription count + 2, or notifications drop under burst.

### 2.4 Stack ceiling — the 30K/50K reality

**Issue #2276** documents real performance degradation starting at **~30K monitored items** on a single client against Kepware. Read latency climbed to 10–30s with eventual session-watchdog timeouts. No maintainer root-cause. **This is the most directly comparable data point and it sits right at our primary target.**

Implications:
- **30K is achievable but close to the documented degradation point.**
- **50K stretch is genuinely uncertain** — would not commit without measurement.
- **Week-1 stack-ceiling measurement is a hard gate.** Lock whatever measurement returns.
- If measurement shows the stack tops out at 25K on our hardware, we lock 25K as primary and revise downstream messaging. **We do NOT partially re-implement the stack.**

### 2.5 OPC UA Client tuning knobs — LOCKED defaults

| Knob | Locked default | Rationale |
|---|---|---|
| `PublishingInterval` | **50 ms** | Plan §1.1 target. |
| `SamplingInterval` | **50 ms** (= publish) or per-tag override | Don't sub-sample below publish without reason. |
| `KeepAliveCount` | **20** (= 1s wall) | Reference-client value. Stack default 10 is too tight. |
| `LifetimeCount` | **60** (≥ 3× keepalive, = 3s wall) | Stack default 1000 too lenient for edge. |
| `MaxNotificationsPerPublish` | **1,000** | Cap single message size; prevents one fat publish stalling the channel. |
| `MinPublishRequestCount` | **= subscription count + 2** | Prevents notification loss under burst. |
| `MaxMessageCount` (queue) | **10** per subscription | Bounded backlog before stack drops oldest. |
| `QueueSize` (per item) | **2** analog, **10** discrete/events | Discard oldest on overflow. |
| `DiscardOldest` | **true** | Edge prefers fresh over backfill. |
| `SequentialPublishing` | **true** | Required for §19.6 ordering. |
| `DeleteSubscriptionsOnClose` | **false** | Enables zero-loss transfer on reconnect. |
| `KeepAliveInterval` (session) | **5,000 ms** | Reference-client default. |
| `ReconnectPeriod` / `ReconnectPeriodExponentialBackoff` | **1,000 / 15,000 ms** | Reference-client defaults. |
| `SessionTimeout` | **60,000 ms** | Reference-client default. |
| .NET GC | `ServerGarbageCollection=true` + `ConcurrentGarbageCollection=true` | Non-negotiable at 30K/sec. Add to `ElpisEdgeConnect.Host.csproj`. |

### 2.6 Notification queue back-pressure metric

The stack's `m_messageCache` grows unboundedly under back-pressure (the worker task can't drain). The stack does not expose cache depth cleanly. **Action:** wrap subscription access in a thin facade that exposes notification-queue depth via reflection. Surface it through `AdapterHealth.Metrics["opcua.notificationQueueDepth"]` so operators can see the pressure. ~50 LOC. Required.

---

## §3 libplctag.NET audit (Axis 1, EtherNet/IP track)

### 3.1 API surface — all green for §1.2

- **Connection lifecycle**: native API auto-multiplexes sessions per `gateway+path`. Hot tag add/remove is the design — create a new `Tag(...)` against same gateway, joins existing session.
- **`@connection` pseudo-tag** with 6-state lifecycle (`UP/DOWN/CONNECTING/DISCONNECTING/IDLE_WAIT/ERR_WAIT`) + `plc_tag_register_callback_ex` for state-change events. Maps directly to our adapter state machine.
- **CPU coverage CONFIRMED**: ControlLogix L55/L61/L71/L73/L82ES, CompactLogix L16ER/L23E/L30ERMS/L32E, GuardLogix 5570/5380, MicroLogix 1100/1400. **L8x (5580) requires `path=1,0` for the front port — bake into wizard defaults.** L1x / L4x / Micro800 untested by community; if pilot has these, smoke-test before commitment.
- **Atomics + arrays**: BOOL, SINT, INT, DINT, LINT, REAL, LREAL, STRING via typed accessors. Array reads via `Tag<TMapper, TValue>` generic.
- **Block-read packing**: **library does it for us on ControlLogix/CompactLogix since v2.0.** Queue all reads → check status → library merges into Forward-Open packets up to negotiated connection size (default 500B, up to 4000B). **No coalescer needed at adapter level** — just use "queue all, await all" pattern.
- **Per-tag errors**: `Tag.GetStatus()` returns negative-status enum. Tag-level failures don't tear down the session — other tags keep flowing. Clean separation from `@connection`-level failures.

### 3.2 Mapper deprecation hedge — REQUIRED

`TagInfoPlcMapper` + `UdtInfoPlcMapper` are `[Obsolete]` per **open issue #406** (Jul-2024, no replacement merged). The maintainer's stance: mappers obscure byte-layout details users should own.

**Plan B locked:** vendor ~250 LOC of our own decoders for `TagInfo` + `UdtInfo` decoded from raw byte accessors. MPL-2.0 compatible. Avoids future libplctag.NET major version removing the mappers and breaking us. Lives in `Sources.EthernetIp.TagDictionary`.

### 3.3 UDT recursive walker — REQUIRED

The library returns raw `TagInfo[]` / `UdtInfo` structures, NOT a browse tree. We build the walker:

```
1. Read @tags → TagInfo[] for controller scope
2. For each program tag: read Program:Name@tags → program-scope TagInfo[]
3. For each tag whose type-bit indicates a UDT: read @udt/<id> → UdtInfo + UdtFieldInfo[]
4. Recurse into UDT fields whose type is another UDT
5. Produce BrowseResult tree
```

~300 LOC. Lives in `Sources.EthernetIp.Browse.UdtTreeWalker`.

### 3.4 String type adapter

AB STRINGs are `{LEN:DINT, DATA:SINT[82]}` UDT instances. Decode to canonical `string`. ~50 LOC. Lives in `Sources.EthernetIp.Types.AllenBradleyString`.

### 3.5 Linux deployment — HIGH confidence

`libplctag.NativeImport/runtimes/` ships native binaries for win-x64/x86/arm64, linux-x64/x86/arm/arm64, osx-x64/arm64. Auto-extracted at first use. Tier-1 CI on Alpine + Ubuntu 24.04 across architectures. Targets `netstandard2.0` — .NET 8 clean.

**Gotcha for read-only deployments**: extraction writes to binary's directory at startup. For locked-filesystem scenarios (rare in our model), set `plctag.ForceExtractLibrary = false` and ship the `.so` alongside. Document but no code change needed for default deployments.

### 3.6 Licensing — GO

- libplctag (native): dual-licensed MPL-2.0 OR LGPL-2+.
- libplctag.NET wrapper: MPL-2.0.
- MPL is file-level copyleft. We can link statically/dynamically into proprietary EdgeConnect. Must publish modifications to libplctag source files themselves (we have none planned). Library binary-replaceable — already satisfied by separate native DLL deployment.
- Cite MPL-2.0 in third-party notices.

### 3.7 EtherNet/IP red flags — pin in v2.1

1. **Mapper deprecation (issue #406)** — vendor decoders per §3.2.
2. **No published throughput numbers** — must benchmark on pilot hardware. The v2 §1.2 per-controller numbers (3K–6K for L7x/L8x) are estimates from community reports, not stack benchmarks. **Pilot-hardware calibration is required, not optional.**
3. **L8x `path=1,0` default** — bake into wizard.
4. **L1x / L4x / Micro800 untested matrix** — confirm pilot CPU families before commitment.
5. **`@tags.STRUCT_TYPE` wording in v2 §1.2 was inaccurate** — actual flow is `@tags` (lists tags with type bits) → `@udt/<numeric_id>` (per-type details). Updated above.
6. **Native-extraction-on-startup** — document in deployment notes for locked-filesystem operators.

---

## §4 ADR-0015 amendments — final wording

Draft for explicit lock alongside v2.1. To be committed as `docs/decisions/0015-wizard-contract.md` amendment.

### Rule 9 — Browse capability

> **Rule 9.** Wizards for protocols that expose a browse service MUST surface a "Connect & Browse" button in the tag-selection section. The button MUST call the wizard's `IBrowseService` implementation (per-protocol) and render results in the shared `TagBrowseTreeView` component. Browse implementations MAY be lazy (children fetched on node expansion).
>
> **Inheritance:** Protocols whose physical contract does not support browse (e.g. MELSEC native — operator-defined tag lists only) are exempt; their wizard documents the absence in an Info alert per the existing Rule 6 carve-out pattern.

### Rule 10 — Auto-load action

> **Rule 10.** Wizards that implement Rule 9 MUST also surface an "Add all" / "Auto-load" action that bulk-imports browsed tags into the source. Implementations MUST confirmation-prompt if the resulting count exceeds 500 (operator-configurable max-tag-count safety cap). Failure modes: partial-failure during bulk-add reports per-tag errors in the wizard's validation banner; the source is not left in a half-populated state.

### Rule 11 — Hot-config invariant

> **Rule 11.** Edit-mode changes to the tag list, polling rate, or subscription tuning on a browse-capable wizard MUST go through `ISourceAdapter.ReconfigureAsync` rather than full Stop+Initialize+Start. Adapters that don't implement true hot-reconfigure fall back to the default implementation (which IS Stop+Init+Start). Wizard surfaces "reconfigure in progress" via the standard busy spinner; concurrent reconfigure-during-reconfigure produces `OPCUA.RECONFIGURE_IN_PROGRESS` (or protocol-specific equivalent) and the wizard shows the standard error snackbar with retry guidance.

### Rule 11.1 — Reconfigure validation precedence

> **Rule 11.1.** New configurations MUST pass full validation (`ValidateConfigAsync`) BEFORE the adapter's active set changes. A reconfigure that fails validation leaves the adapter in its previous running state with no operator-visible disruption beyond the wizard's error report.

---

## §5 Implementation contract

### 5.1 File-by-file deliverables — OPC UA Client (~4 weeks)

**New project: `src/ElpisEdgeConnect.Sources.OpcUaClient/`**

| File | Purpose | Approx LOC |
|---|---|---|
| `OpcUaClientSourceAdapter.cs` | `ISourceAdapter` implementation. Hosts the `Session`, owns reconnect lifecycle, drives subscriptions. Implements `ReconfigureAsync` override (atomic active-set swap via `Subscription.AddItems`/`RemoveItems`). | ~600 |
| `OpcUaClientSourceConfiguration.cs` | `SourceConfiguration` subtype. Endpoint, security, monitored-items list, tuning knobs. | ~200 |
| `OpcUaClientBrowseService.cs` | `ITagBrowseService` implementation. Lazy `Session.Browse` + `BrowseNext` traversal. | ~250 |
| `OpcUaClientSubscriptionFactory.cs` | Creates 30+ subscriptions per session (1K items each), applies locked tuning defaults from §2.5. | ~150 |
| `NotificationDispatcher.cs` | The §1.3-required channel-based dispatch. `FastDataChangeCallback` enqueues into bounded `Channel<NotificationBatch>`; worker drains. Includes queue-depth metric for `AdapterHealth.Metrics`. | ~200 |
| `OpcUaTypeMapper.cs` | UA `Variant` → `CanonicalDataPoint.Value` + `ValueType` translator. Scalar + array + ExtensionObject cases. | ~300 |
| `OpcUaReconnectCoordinator.cs` | Wraps `SessionReconnectHandler` with our state machine + exponential backoff defaults. | ~150 |
| `OpcUaCertTrustManager.cs` | Reuses `OpcUaCertManager` from `Sinks.OpcUaServer`; wraps for client-side trust chain. | ~80 |
| **Test project: `tests/ElpisEdgeConnect.Sources.OpcUaClient.Tests/`** | ~80 tests per §5 of v2 | ~2,000 (test code) |

**Adapter SDK touch points (Core):**

| File | Change | Approx LOC |
|---|---|---|
| `Adapters/ISourceAdapter.cs` | Add `ReconfigureAsync` default member | ~30 (incl. doc comments) |
| `Browse/ITagBrowseService.cs` (new) | Browse contract per §1.3 (existing v2) | ~50 |
| `Browse/BrowseResult.cs` (new) | Browse data shape | ~50 |
| `Browse/BrowseNode.cs` (new) | Tree node shape | ~30 |

**Wizard work (Management):**

| File | Purpose | Approx LOC |
|---|---|---|
| `Wizards/OpcUaClientSourceWizardModel.cs` | Wizard state, validation, `BuildSourceInstance` | ~400 |
| `Components/Pages/SourceWizards/AddOpcUaClientSource.razor` | The wizard UI per ADR-0015 + Rule 9/10/11 | ~600 |
| `Components/Shared/TagBrowseTreeView.razor` (new) | Shared MudTreeView wrapper with multi-select + auto-load | ~300 |
| `Wizards/SourceProtocolPickerModel.cs` | New tile entry | ~10 |
| **Tests** | ~40 wizard tests | ~800 |

**OPC UA Client total: ~3,000 LOC + ~2,800 test LOC**

### 5.2 File-by-file deliverables — EtherNet/IP (~4 weeks)

**New project: `src/ElpisEdgeConnect.Sources.EthernetIp/`**

| File | Purpose | Approx LOC |
|---|---|---|
| `EthernetIpSourceAdapter.cs` | `ISourceAdapter` implementation. Manages `@connection` lifecycle, drives polling loop, implements `ReconfigureAsync` override (hot tag add/remove). | ~500 |
| `EthernetIpSourceConfiguration.cs` | Host, slot, CPU family, tag list, poll interval, deadband. | ~200 |
| `Browse/UdtTreeWalker.cs` | Recursive walker per §3.3 — `@tags` → `Program:X@tags` → `@udt/<id>` → tree. | ~300 |
| `Browse/EthernetIpBrowseService.cs` | `ITagBrowseService` wrapping `UdtTreeWalker`. | ~150 |
| `TagDictionary/TagInfoDecoder.cs` | Vendored mapper per §3.2 (replaces deprecated `TagInfoPlcMapper`). | ~150 |
| `TagDictionary/UdtInfoDecoder.cs` | Vendored mapper for UDT introspection. | ~150 |
| `Types/AllenBradleyString.cs` | `{LEN:DINT, DATA:SINT[82]}` → canonical string. | ~50 |
| `Types/AtomicTypeMapper.cs` | BOOL/SINT/INT/DINT/LINT/REAL/LREAL → `CanonicalValueType`. | ~150 |
| `Polling/PollLoop.cs` | "Queue all, await all" pattern for libplctag block-read optimization. | ~200 |
| `Cov/ClientSideCovLayer.cs` | Per-tag last-value tracking; emit only on change > deadband. | ~150 |
| `ConnectionLifecycle/ConnectionStateMachine.cs` | `@connection` event subscription + adapter state transitions. | ~200 |
| **Tests** | ~70 tests | ~2,000 |

**Wizard work:**

| File | Purpose | Approx LOC |
|---|---|---|
| `Wizards/EthernetIpSourceWizardModel.cs` | Wizard state, validation (including L8x `path=1,0` default), `BuildSourceInstance` | ~400 |
| `Components/Pages/SourceWizards/AddEthernetIpSource.razor` | Wizard UI per ADR-0015 + Rules 9/10/11. Reuses `TagBrowseTreeView` from OPC UA Client. | ~550 |
| `Wizards/SourceProtocolPickerModel.cs` | New tile entry | ~10 |
| **Tests** | ~35 tests | ~700 |

**EtherNet/IP total: ~2,400 LOC + ~2,700 test LOC**

### 5.3 §1.3 platform work (~1 week)

| Task | Files | Approx LOC |
|---|---|---|
| `ReconfigureAsync` default member on `ISourceAdapter` + tests across 5 existing adapters | `ISourceAdapter.cs` + 5 adapter test files | ~150 (incl. tests) |
| Benchmark suite — OPC UA Client subscribe at 30K/50K + buffer enqueue + pipeline-throughput + sink fan-out | `tests/ElpisEdgeConnect.Benchmarks/OpcUaClientBenchmarks.cs` (new) | ~600 |
| Workload profile docs per benchmark | `docs/benchmarks/multi-protocol-workload-profiles.md` (new) | doc only |
| GC config in Host | `src/ElpisEdgeConnect.Host/ElpisEdgeConnect.Host.csproj` | ~5 |
| Invariant documentation lock | `docs/core/performance-hot-path.md` (new) | doc only |
| ADR-0015 amendments | `docs/decisions/0015-wizard-contract.md` (edit) | doc only |

### 5.4 Sequencing (Option B hybrid, calibrated)

| Week | OPC UA Client track | EtherNet/IP track | Platform work |
|---|---|---|---|
| **1** | Stack-ceiling measurement (HARD GATE); session lifecycle + reconnect skeleton; subscription factory | — | `ReconfigureAsync` default member |
| **2** | Browse service + lazy tree walker; type mapper; notification dispatcher | — | Benchmark suite skeleton + GC config |
| **3** | UA Tree picker (`TagBrowseTreeView`); wizard model + validation; reconnect coordinator | KICK OFF — connection lifecycle skeleton; libplctag NuGet integration | OPC UA Client benchmarks running, tuning |
| **4** | Wizard UI; `ReconfigureAsync` override; per-tag diagnostics; OPC UA Client integration tests | UDT walker + vendored decoders; type mapper; poll loop | Benchmark gates added to CI |
| **5** | OPC UA Client BUFFER (wrap-up, QA hardening) | Wizard model + UI; COV layer; `ReconfigureAsync` override | EtherNet/IP benchmark suite |
| **6** | — | EtherNet/IP integration tests; per-tag diagnostics; reliability soak | EtherNet/IP benchmarks tuning |
| **7** | — | QA hardening; wizard polish; documentation | Final benchmark regression gates |
| **8** | Combined QA cycle on the pilot zip | Combined QA cycle | Final perf report |
| **9** | Pilot start (week 8 if QA clean) | Pilot start | — |

**Pilot start: end of week 7 or week 8** (vs v2's 10–12). Driven by:
- Perf work shrinking from 3w to 1w (existing platform more ready).
- Two protocols running in parallel from week 3 (Option B hybrid).
- One QA cycle at the end rather than per-protocol QA cycles.

### 5.5 Benchmark validity rules (LOCKED)

> *Why this section exists:* without explicit validity rules, benchmark regressions degrade into synthetic-throughput games. A 30K/sec "win" on identical-value payloads with no MQTT serialization tells us nothing about pilot-time behaviour. ChatGPT review on 2026-05-28 surfaced this gap; locking it here before implementation kicks off.

**The benchmark-realism invariants:**

1. **Realistic value-change distribution.** Workloads MUST model independent per-tag change rates. NOT all 30K tags changing simultaneously every cycle. Reference profile: 10% of tags change per publish cycle (typical industrial COV rate); long-tail distribution where 1% of tags change every cycle, 9% change every 5 cycles, the rest are quasi-static.
2. **Heterogeneous payload mix.** Workloads MUST mix value types: 40% BOOL, 30% DINT/INT, 20% REAL/LREAL, 10% STRING. No "all-DINT" or "all-BOOL" runs allowed as the primary regression gate. STRINGs MUST have realistic lengths (typical 8–32 chars; reference profile includes 5% at 256+ chars for ControlLogix-style string arrays).
3. **End-to-end serialization enabled.** MQTT sink runs through full JSON serialization (Batch mode) AND raw scalar serialization (PerTag mode), not a no-op sink. OPC UA Server sink runs the full `EdgeConnectNodeManager.UpsertTagValue` path. No "/dev/null sinks" in the primary gate.
4. **Realistic sink cadence.** Sinks acknowledge at realistic latencies — MQTT broker round-trip ≥ 1ms, OPC UA Server publish ≥ 50ms. No zero-latency sink mocks in the primary gate.
5. **Soak-test reconnect noise.** Performance soak tests (24-hour runs at sustained throughput) MUST inject periodic noise: simulated network blips (5s drops every 10 minutes), subscription churn (add/remove 1% of monitored items every 30 minutes), and configuration changes via `ReconfigureAsync` every 2 hours. A system that hits 30K/sec under sterile lab conditions but degrades under real-world noise is NOT shipping at 30K/sec.
6. **Realistic OPC UA value semantics.** Notifications MUST carry valid `ServerTimestamp` + `SourceTimestamp` + `StatusCode` per the UA spec — not zeroed timestamps. The encoder/decoder overhead in the stock stack is part of the perf reality we're measuring.

**Sustained-vs-peak definition (LOCKED):**

> The benchmark ceiling is defined at **sustained stable operation for ≥30 minutes**, not peak burst. A workload that hits 50K/sec for 30 seconds then dies under GC pressure or notification-queue backlog accumulation is NOT 50K/sec. The number we lock is the **highest throughput sustainable indefinitely with stable latency, stable reconnect behaviour, bounded queue growth, and no degradation drift.**

Concrete sustainability gates that MUST all hold simultaneously for the duration:

| Gate | Threshold |
|---|---|
| Throughput drift | <5% across the run window |
| p99 latency drift | <20% across the run window |
| OPC UA notification queue depth | Bounded; <500 deep at any sample point |
| Buffer depth | Bounded; not monotonically growing (a stable working-set is fine) |
| Gen-2 GC frequency | <1 per 60 seconds |
| Reconnect injections | Recover within `ReconnectPeriodExponentialBackoff` ceiling (15s), no compounding |
| `ReconfigureAsync` injection | Apply within 100ms; no data loss; no degradation drift after |

A run that fails ANY of these gates does NOT count toward the locked number. We report "30K/sec for 28 minutes then drifted" honestly, NOT "30K/sec sustained" with a footnote.

**Where these live:**
- Locked in `docs/benchmarks/multi-protocol-workload-profiles.md` (created in week-1 work).
- BenchmarkDotNet job parameters: `[SimpleJob(RunStrategy.Monitoring, iterationCount: 30, warmupCount: 3, invocationCount: 1)]` for soak variants.
- Documented in `docs/benchmarks/phase2-multi-protocol-baseline.md` at the end of week 7 alongside the locked numbers.

### 5.6 Risk register

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| OPC UA stack ceiling below 30K on pilot hardware | Medium (Issue #2276 evidence) | High — primary target slips | Week-1 measurement HARD GATE; lock measured number, revise messaging honestly |
| L1x / Micro800 in pilot scope (untested matrix) | Low — pilot CPUs likely L7x/L8x | Medium — late-pilot smoke surprise | Confirm pilot CPU family in v2.1 sign-off |
| libplctag mapper removed in next major | Low — issue open since Jul-2024 | Low — we've vendored decoders | Vendored from day one (§3.2) |
| libplctag throughput below 3K/sec on L7x | Medium — no published benchmarks | High — affects per-controller positioning | Pilot-hardware calibration during week 6–7; honest revision if reality bites |
| Wizard tree-view performance at 5K+ tags | Medium | Medium — operator UX | Lazy expansion + virtualization; benchmark at 10K nodes |
| Channel-based dispatch race conditions under load | Low (well-understood pattern) | High — data loss | Channel write must be non-blocking; drop accounting must surface in diagnostics; integration test pins behaviour |
| FactoryTalk endpoint specifics unknown | High — assumed Anonymous/None for default | Low — Anonymous is a sensible default; configure per pilot | Get pilot endpoint config in v2.1 sign-off |
| EtherNet/IP L8x missing `path=1,0` default | High if not surfaced | Medium — operator hits "ErrorBadData" | Wizard default LOCKED here (§3.1) |

---

## §6 Open questions — LOCKED at v2.1 sign-off (2026-05-28)

User-locked answers (incorporating ChatGPT review pass + final user calls):

### Q7 — Pilot OPC UA endpoint specifics — ALL SPECS COMMITMENT + LAB DEFAULT LOCKED

**Customer-facing commitment (user lock 2026-05-29):** OPC UA Client adapter MUST support all endpoint specs the customer site may present — the full combination matrix of auth (Anonymous + UserName + X.509 Certificate) × security mode (None + Sign + SignAndEncrypt) × security policy (Basic256Sha256 baseline; newer policies forward-compatible). This is NOT a baseline-with-narrowing — it's the locked surface the adapter ships against.

**v2.1 §1.1 already commits this scope.** This lock confirms the customer-facing intent and removes any "lab-only" framing.

**Lab-work default (for benchmark measurement only):**

| Setting | Default for week-1 lab measurement |
|---|---|
| Auth | Anonymous |
| SecurityMode | SignAndEncrypt |
| SecurityPolicy | Basic256Sha256 |
| Cert trust | Auto-trust in lab; explicit trust-store in pilot |
| Endpoint style | `opc.tcp://` |
| Session timeout | 60s (per §2.5) |

The lab default is the harness benchmark configuration. It is NOT the only configuration the adapter is tested against. The full combination matrix gets explicit test surface per v2.1 §5.1.

**Test surface expansion (user lock 2026-05-29):**
- Full auth × security combination matrix in `OpcUaClientSourceAdapter` integration tests (~12 sensible combinations after pruning nonsensical ones like `UserName + SecurityMode=None` which exposes credential on the wire — that combo is REJECTED with a clear error message, not silently accepted).
- Wizard UX exposes all options without favoring the lab default (per ADR-0015 — wizards present the full schema).
- Customer-facing throughput numbers MAY publish per-security-mode (Anonymous + None separately from UserName + SignAndEncrypt) — different CPU profiles are honestly different numbers.

### Q8 — Pilot Rockwell CPU families — FULL FAMILY COMMITMENT + TIMELINE EXTENSION AUTHORIZED

User call (overrides ChatGPT narrower recommendation; reaffirmed 2026-05-29 with explicit timeline-extension authorization): **Include all CPU families. Extend the milestone if smoke fails on the libplctag-untested ones.**

| CPU family | Commitment | Engineering caveat |
|---|---|---|
| ControlLogix L7x | Supported + benchmarked | In libplctag tested matrix |
| ControlLogix L8x | Supported + benchmarked | `path=1,0` default; in tested matrix |
| CompactLogix L3x/L4x | Supported + benchmarked | In tested matrix |
| ControlLogix L1x | Supported | **NOT in libplctag community test matrix** — pre-week-5 smoke against Studio 5000 Logix Emulate. If smoke fails: build adapter-level adjustments in-milestone (extension authorized). |
| MicroLogix 1100/1400 | Supported + benchmarked | In tested matrix |
| Micro800 (Micro820/850) | Supported | **NOT in libplctag community test matrix** — uses different CIP variant; pre-week-5 smoke against CCW simulator. If smoke fails: build adapter-level adjustments in-milestone (extension authorized). |
| GuardLogix 5570/5380 | Supported + benchmarked | In tested matrix (Rockwell safety controllers) |

**Engineering implications (updated 2026-05-29):**
- L1x + Micro800 carry smoke-test risk — but **the milestone extends rather than narrowing scope** if smoke fails.
- Worst-case extension: ~1–2 weeks if BOTH L1x and Micro800 need adapter-level adjustments. Pilot start moves to week 9 (vs locked 7–8) in that scenario.
- Pre-week-5 smoke targets locked: L1x (any model) + Micro820 + Micro850 at minimum.

### Q11 — Simulator license procurement (new — user lock 2026-05-29)

The Q8 commitment requires simulator coverage for the pre-week-5 EtherNet/IP smoke + ongoing nightly regression runs. Procurement plan:

| Need | Simulator | License | Cost (indicative) |
|---|---|---|---|
| ControlLogix L7x/L8x + CompactLogix L1x/L3x/L4x | **Studio 5000 Logix Emulate** (one product covers all five families) | Rockwell subscription, per seat | ~$500–1,500/yr |
| Micro800 (Micro820/850) | **Connected Components Workbench** with built-in simulator | Rockwell free download | **$0** |
| MicroLogix 1100/1400 | **RSLogix Emulate 500** (legacy) OR real dev hardware (~$300) | Rockwell paid (~$200/yr) OR hardware one-time | Either path |
| GuardLogix 5570/5380 safety | Logix Emulate + safety add-on | Add-on to Emulate | +~$500/yr |

**Procurement decision (deferred to user):** Studio 5000 Logix Emulate primary subscription is the single highest-leverage purchase — covers 5 of the 7 family scope in one license. CCW handles Micro800 at zero cost. MicroLogix 1100/1400 and GuardLogix safety carry the optional secondary cost decision; reasonable to defer until pilot customer confirms they actually use those families.

**Alternative**: real hardware dev kits (Micro820 ~$500 one-time, CompactLogix L18 ~$1,500 one-time, pre-owned ControlLogix L73 ~$2-3K on industrial resale) — slower to provision but more authoritative for pilot calibration.

### Q8 — Pilot Rockwell CPU families — FULL FAMILY COMMITMENT

User call (overrides ChatGPT narrower recommendation): **Include all CPU families.** Pilot commitment covers:

| CPU family | Commitment | Engineering caveat |
|---|---|---|
| ControlLogix L7x | Supported + benchmarked | In libplctag tested matrix |
| ControlLogix L8x | Supported + benchmarked | `path=1,0` default; in tested matrix |
| CompactLogix L3x/L4x | Supported + benchmarked | In tested matrix |
| ControlLogix L1x | Supported | **NOT in libplctag community test matrix** — schedule pre-week-5 smoke against borrowed hardware or vendor simulator |
| MicroLogix 1100/1400 | Supported + benchmarked | In tested matrix |
| Micro800 | Supported | **NOT in libplctag community test matrix** — uses different CIP variant; schedule pre-week-5 smoke and risk-flag if it fails |
| GuardLogix 5570/5380 | Supported + benchmarked | In tested matrix (Rockwell safety controllers) |

**Engineering implications added to risk register §5.6:**
- L1x + Micro800 carry medium-impact smoke-test risk. If pre-week-5 smoke fails on either family, we surface as a pilot-customer conversation: either narrow that family's commitment, or push the milestone to add explicit support (potentially adding 1–2 weeks).
- Pre-week-5 smoke targets locked: L1x (any model) + Micro820 + Micro850 at minimum.

### Q9 — Stack-ceiling measurement workload — LOCKED

| Parameter | Locked value |
|---|---|
| Profiles | **30K / 50K / 75K** monitored items |
| Publish interval | 50ms |
| Sampling interval | 50ms |
| Distribution | Industrial-COV profile (workload-profiles §1 Rule 1) |
| Payload mix | Industrial mix (workload-profiles §1 Rule 2) |
| Security mode | SignAndEncrypt baseline (per Q7) |
| Sink fanout | MQTT + OPC UA Server |
| Duration | 30 min sustained gate per profile |
| Queue gate | OPC UA notification queue <500 deep |
| Gen-2 gate | <1 collection per 60s |

**Locked measurement sequence (in this exact order):**
1. **15K warmup for 5 minutes** — avoids measuring cold-JIT/transient-allocator noise as "steady-state." (ChatGPT addition.)
2. **30K sustained gate** (primary target — must pass all 7 sustainability gates)
3. **50K stretch attempt** (informational unless all gates green)
4. **75K exploratory ceiling** (informational only; documents where the stack actually falls over)

### Q10 — CI benchmark host — LOCKED

**Dedicated Linux benchmark host** for nightly official numbers. Shared CI infra allowed for PR smoke only.

| Component | Locked value |
|---|---|
| OS | Linux |
| Power profile | `tuned-adm profile throughput-performance` |
| CPU affinity | Pinned benchmark worker cores; OS isolated to non-benchmark cores |
| Broker | Local Mosquitto on `localhost:1883` |
| SQLite storage | Local NVMe SSD |
| Runtime | Pinned .NET 8 SDK version (capture exact build in phase2-baseline §7) |
| CI cadence | Nightly dedicated host |
| PR smoke | Shared infra acceptable (30s smoke variant only) |

**Locked governance rule:** Only **nightly dedicated-host numbers** may update the official baseline docs. PR-smoke shared-infra numbers are informational only and never overwrite locked baselines.

---

### Items NOT included in §6 lock (defer to in-flight pre-week-1 logistics)

These are operational, not scope:

- **Pilot OPC UA endpoint specifics** (the customer's actual values) — needed before week-1 close per Q7. Engineering can run with the baseline above until then.
- **Pre-week-5 L1x + Micro800 smoke hardware/simulator availability** — operations work. Confirm before week 4 starts.
- **Dedicated benchmark host procurement** — operations work. Order/provision in week 0.

### Sink throughput audit (was Q6 in v2.1 draft)

Stays in scope as week-1 work alongside the OPC UA stack-ceiling measurement. Measures current MQTT + OPC UA Server sink ceilings as the reference point for "30K/sec end-to-end achievable." If either sink can't drain at 30K/sec under Profile A workload, that becomes the new gate.

### CDP deferred-pooling decision

**Locked condition for revisit:** ANY of `Gen0/sec > 1000` OR `pipeline p99 > 5ms` at 30K/sec sustained → revisit. Otherwise NEVER pool. Measurement informs; no speculative work.

---

## §7 Lock criteria for v2.1 — ALL GREEN (2026-05-28)

For v2.1 to be locked and implementation to start, the following must be true:

1. ✅ Library audits complete (OPC UA Foundation + libplctag.NET) — both green-with-conditions.
2. ✅ Existing-codebase inspection complete — Phase 1 platform is more ready than v2 assumed; §1.3 scope reduces accordingly.
3. ✅ ADR-0015 Rules 9/10/11 drafted to final wording.
4. ✅ File-by-file deliverables sized (~5,400 production LOC + ~5,500 test LOC across both protocols + platform work).
5. ✅ Sequencing locked (Option B hybrid, 7–8 week pilot start).
6. ✅ Risk register specific + actionable.
7. ✅ Q7 OPC UA endpoint — ALL endpoint specs committed (user lock 2026-05-29); lab-work default = Anonymous + SignAndEncrypt + Basic256Sha256; test surface expanded to full auth × security matrix.
8. ✅ Q8 Rockwell CPU families — ALL families committed + timeline extension authorized (user lock 2026-05-29) for L1x + Micro800 if smoke fails.
9. ✅ Q9 stack-ceiling workload locked (15K warmup → 30K sustained → 50K stretch → 75K exploratory).
10. ✅ Q10 CI benchmark host locked (dedicated Linux host for nightly; shared infra for PR smoke only; nightly-only baseline updates).
11. ✅ Q11 Simulator license procurement plan locked (Studio 5000 Logix Emulate primary + free CCW for Micro800; user-deferred decisions for optional MicroLogix Emulate 500 + GuardLogix safety add-on).

**v2.1 is LOCKED.** Implementation may begin per the sign-off path in §9.

---

## §8 What we ship at the end of week 7–8

1. **OPC UA Client source adapter** — Kepware-class on all §1.1 capabilities.
2. **EtherNet/IP source adapter** — Kepware-class on all §1.2 capabilities, including vendored UDT decoders.
3. **Three ADR-0015 amendments locked** (Rules 9 / 10 / 11) + the platform principle P4 audit doc.
4. **`ReconfigureAsync` on `ISourceAdapter`** with default impl + overrides on the two new adapters.
5. **`ITagBrowseService` + `TagBrowseTreeView`** shared infrastructure.
6. **Benchmark suite** with regression gates on:
   - OPC UA Client at 30K/sec primary (50K stretch if measurement supports).
   - EtherNet/IP at calibrated per-controller numbers from pilot-hardware measurement.
   - Pipeline + buffer + sink fan-out end-to-end.
7. **Three new tiles in `SourceProtocolPickerModel`** (`opcua-client`, `ethernet-ip`, with MELSEC still Pending for future).
8. **Two new license-feature keys**: `source.opcua-client`, `source.ethernet-ip` (Pro+ tier).
9. **QA test plan addenda** (~60 new cases across both protocols) + pilot QA zip.
10. **Documentation deltas**: `docs/core/performance-hot-path.md` (new), `docs/adapter-sdk/browse-capability.md` (new), `docs/decisions/0015-wizard-contract.md` (amended), `docs/benchmarks/multi-protocol-workload-profiles.md` (new — locks §5.5 benchmark validity rules), `docs/benchmarks/phase2-multi-protocol-baseline.md` (new — captures the locked sustained numbers at end of week 7).

**MELSEC** stays deferred to a separate plan trail. **S7** stays Pending in the picker. Customer is OK with both per 2026-05-28 user sign-off.

---

## §9 Sign-off

This v2.1 supersedes v2 as the implementation contract. Three items remain (the Q7–Q10 logistics from §7) — those need answers in the next ~3 working days to kick off week 1 cleanly.

After your sign-off on this v2.1:

1. PR #45 merge (OPC UA Server port diagnostics) — prerequisite, queued.
2. Tag the in-flight QA baseline `v0.2.0-qa-baseline`.
3. Branch `feat/multi-protocol-pilot-expansion` off master.
4. Week 1 starts with the **OPC UA Foundation stack-ceiling measurement** as the literal first work item.

The pilot starts at week 7–8.
