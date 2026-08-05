# Phase 1 Engineering Execution Plan

**Scope:** Build the Core Platform Foundation for Elpis EdgeConnect
**Reference:** `ARCHITECTURE_BLUEPRINT.md` Sections 4, 5, 6, 8, 9, 14, 18, 19
**Status:** Ready to execute

---

## 1. Phase 1 Goals

Build the protocol-agnostic runtime engine. At the end of Phase 1, a mock source adapter and mock sink adapter must run end-to-end through a real route with real transforms, real diagnostics, real licensing, and real store-and-forward — without a single line of real protocol code.

**This is the hardest phase.** Everything downstream depends on these contracts being right. Resist shortcuts.

### Definition of Phase 1 Done

Phase 1 is complete **only when every item in Section 10 (Phase 1 Exit Criteria) is demonstrably true**. Section 10 is the authoritative checklist — this summary is a high-level pointer to it, not a substitute.

The summary version:

1. `ElpisEdgeConnect.Core` compiles with zero warnings and passes all tests — see Section 10 "Functional" and "Quality" checklists
2. `ElpisEdgeConnect.Host` runs as a Windows service and loads config — see Section 10 "Functional" checklist
3. Two mock adapters (`MockSource`, `MockSink`) live in `tests/` folder and implement real contracts — see Milestone D2
4. End-to-end integration test: mock source → route → 3 transform steps → store-and-forward buffer → mock sink, with induced failure and recovery — see Section 10 "Functional" and Milestone D3's 13 scenarios
5. **All benchmarks meet the targets specified in Section 10 "Performance"** — not "Medium-tier targets in general," but specifically the numeric thresholds in Section 10
6. License validation works with a signed sample license — see Section 10 "Functional"
7. Config draft → validate → apply → rollback works end-to-end — see Section 10 "Functional"
8. Diagnostics API returns data for source, pipeline, and sink dimensions — see Section 10 "Functional"
9. 7-day leak test shows no memory growth beyond 10% of initial — see Section 10 "Reliability"
10. Code coverage on `Core` ≥ 80% — see Section 10 "Quality"

**If there is any doubt whether Phase 1 is complete, consult Section 10. That checklist is the exit gate.**

---

## 2. Entry Conditions

Before writing code, these must be resolved:

| Question | Decision needed by | Decider |
|----------|-------------------|---------|
| SQLite library — `Microsoft.Data.Sqlite` vs `LiteDB` | Week 1 | Eng lead |
| JSON schema validation library — `NJsonSchema` vs `Json.Everything` | Week 1 | Eng lead |
| License signing key custody — HSM / vault / dev keypair for v1 | Week 2 | Security + Ops |
| Metrics export approach — `System.Diagnostics.Metrics` + Prometheus exporter | Week 2 | Eng lead |
| Test framework — xUnit (recommended) vs NUnit | Week 1 | Eng lead |
| Benchmark framework — BenchmarkDotNet (recommended) | Week 1 | Eng lead |
| Mock assertion library — FluentAssertions vs Shouldly | Week 1 | Eng lead |

**Recommended defaults** (decide at kickoff, don't relitigate):
- SQLite: `Microsoft.Data.Sqlite` (official, better tooling, predictable performance)
- JSON Schema: `NJsonSchema` (better C# integration, schema generation from types)
- Test: `xUnit` + `FluentAssertions` + `NSubstitute`
- Benchmark: `BenchmarkDotNet`
- License keys: Generate RSA keypair now, store private key in password manager, commit public key to repo. Migrate to HSM in Phase 4.

---

## 3. Work Stream Overview

Phase 1 is organized into **8 work streams** with explicit dependencies. Streams within a milestone can be parallelized if multiple developers are available.

```
┌─────────────────────────────────────────────────────────────┐
│  Milestone A — Foundations (no dependencies)                 │
│  ├── A1. Canonical Data Model                                │
│  ├── A2. Adapter Contracts                                   │
│  └── A3. Core Exceptions & Error Taxonomy                    │
└──────────────────────────┬──────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│  Milestone B — Configuration & Licensing                     │
│  ├── B1. Configuration Models                                │
│  ├── B2. Configuration Manager (draft/apply/rollback)        │
│  └── B3. License Manager                                     │
└──────────────────────────┬──────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│  Milestone C — Pipeline & Routing                            │
│  ├── C1. Transform Pipeline                                  │
│  ├── C2. Message Buffer (InMemory + SQLite)                  │
│  ├── C3. Routing Engine                                      │
│  └── C4. Diagnostics Collector                               │
└──────────────────────────┬──────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│  Milestone D — Host & Integration                            │
│  ├── D1. Service Host                                        │
│  ├── D2. Mock Adapters                                       │
│  ├── D3. End-to-End Integration Tests                        │
│  └── D4. Performance Benchmarks                              │
└─────────────────────────────────────────────────────────────┘
```

Milestones are sequential. Work streams within a milestone are parallelizable.

---

## 4. Milestone A — Foundations

### A1. Canonical Data Model

**Project:** `src/ElpisEdgeConnect.Core/Model/`

**Files:**

| File | Purpose |
|------|---------|
| `CanonicalDataPoint.cs` | Immutable record per Section 4.1 |
| `CanonicalValueType.cs` | Enum: Boolean, Integer, Long, Float, Double, String, DateTime, ByteArray, Array, Object, Null |
| `DataQuality.cs` | Enum: Good, Uncertain, Bad, Stale, Unknown |
| `TagDefinition.cs` | Discovered tag metadata (name, path, type, unit, description) |
| `DeviceInfo.cs` | Device identity (vendor, model, firmware, serial) |
| `CanonicalDataPointBuilder.cs` | Fluent builder for adapters to construct points |
| `CanonicalDataPointFactory.cs` | Factory with cached metadata (gateway id, source id, sequence gen) |

**Definition of Done:**
- All types are `sealed record` with `required` init properties
- `CanonicalDataPoint` is immutable and thread-safe
- `CanonicalDataPointFactory` issues monotonic per-source sequence numbers atomically
- Unit tests: equality, copy-with, builder correctness, factory thread safety
- Benchmark: construct 1M points in < 500 ms (builder overhead target)
- XML doc comments on every public member
- No dependencies outside `System.*`

**Test coverage:**
- `CanonicalDataPointTests.cs` — construction, equality, immutability
- `CanonicalDataPointFactoryTests.cs` — monotonic sequence under concurrent access (10 threads × 100k points)
- `CanonicalDataPointBenchmarks.cs` — construction throughput, memory allocation

### A2. Adapter Contracts

**Project:** `src/ElpisEdgeConnect.Core/Adapters/`

**Files:**

| File | Purpose |
|------|---------|
| `ISourceAdapter.cs` | Source contract per Section 4.2 |
| `ISinkAdapter.cs` | Sink contract per Section 4.3 |
| `SourceCapabilities.cs` | Flags enum |
| `SinkCapabilities.cs` | Flags enum |
| `AdapterState.cs` | Enum: Created, Initializing, Initialized, Starting, Running, Degraded, Stopping, Stopped, Failed, Blocked |
| `AdapterHealth.cs` | Record with state, lastCheck, lastError, metrics snapshot |
| `PublishResult.cs` | Record: success, acceptedCount, rejectedCount, error, latency |
| `ValidationResult.cs` | Record: valid, errors list, warnings list |
| `AdapterException.cs` | Base exception with code, category, retryable flag |
| `SourceConfiguration.cs` | Base class for source configs |
| `SinkConfiguration.cs` | Base class for sink configs |

**Definition of Done:**
- Contracts match Section 4 exactly
- `IAsyncEnumerable<CanonicalDataPoint> SubscribeAsync` for event-driven sources
- All async methods accept `CancellationToken`
- `IAsyncDisposable` properly implemented pattern documented
- Reference documentation: `docs/adapter-sdk/source-adapter-contract.md` and `sink-adapter-contract.md`
- Unit tests for state machine transitions (which states can transition to which)
- Compile-time checks: attempting to construct an `AdapterHealth` without required fields fails

### A3. Core Exceptions & Error Taxonomy

**Project:** `src/ElpisEdgeConnect.Core/Errors/`

**Files:**

| File | Purpose |
|------|---------|
| `AdapterError.cs` | Record per Section 13.3 |
| `ErrorCategory.cs` | Enum |
| `AdapterException.cs` | Throwable with AdapterError payload |
| `ConfigurationException.cs` | Thrown during config validation |
| `LicenseException.cs` | Thrown for license violations |
| `RouteException.cs` | Thrown by routing engine |
| `BufferException.cs` | Thrown by buffer operations |
| `CoreErrors.cs` | Static error code catalog for Core module (`CORE.CONFIG_INVALID`, etc.) |

**Definition of Done:**
- All exceptions carry structured `AdapterError` data
- Error code naming convention documented: `MODULE.CATEGORY_SUBCATEGORY`
- Retryable flag respected by retry logic (tested)
- Serialization of errors to diagnostics works correctly

---

## 5. Milestone B — Configuration & Licensing

### B1. Configuration Models

**Project:** `src/ElpisEdgeConnect.Core/Configuration/`

**Files:**

| File | Purpose |
|------|---------|
| `GatewaySettings.cs` | Top-level gateway config per Section 8.1 |
| `SourceInstanceConfig.cs` | One source instance definition |
| `SinkInstanceConfig.cs` | One sink instance definition |
| `RouteConfig.cs` | Route definition |
| `TransformProfileConfig.cs` | Transform profile (mapping, deadband, rate limit, filter) |
| `BufferPolicyConfig.cs` | Buffer mode, depth, age, drop policy |
| `DeliveryPolicyConfig.cs` | Mode, retries, backoff, fanout parallelism |
| `TagFilterConfig.cs` | Include/exclude patterns |
| `StoreAndForwardSettings.cs` | Global S&F settings |
| `ManagementApiSettings.cs` | API port, auth, TLS |
| `GatewayConfiguration.cs` | Root aggregate holding all of the above |

**Definition of Done:**
- All config types are immutable records with validation attributes
- Deserialization from JSON works via `System.Text.Json`
- Schema generation: `dotnet run --project src/ElpisEdgeConnect.Core -- generate-schema` produces JSON Schema files in `docs/config-schemas/`
- Unit tests: deserialize every example from `ARCHITECTURE_BLUEPRINT.md` Section 8.1
- Validation: missing required fields, invalid enum values, invalid regex patterns in tag filters

### B2. Configuration Manager

**Project:** `src/ElpisEdgeConnect.Core/Configuration/`

**Files:**

| File | Purpose |
|------|---------|
| `IConfigurationManager.cs` | Contract: get current, create draft, validate, apply, rollback, history |
| `ConfigurationManager.cs` | File-backed implementation |
| `ConfigurationValidator.cs` | Schema + semantic + license validation |
| `ConfigurationDiffer.cs` | Computes diff between two configs for audit log |
| `ConfigurationAuditLog.cs` | Append-only audit file writer |
| `ConfigurationChangeEvent.cs` | Event raised when config applied |
| `ConfigurationHistoryEntry.cs` | Record for history listing |

**Storage layout:**

```
config/
├── current.json                  # active config
├── license.json                  # active license
├── drafts/
│   └── {draftId}.json
├── history/
│   ├── 2026-04-07T10-30-00.json
│   ├── 2026-04-07T14-15-00.json
│   └── audit.log
└── schemas/
    └── gateway-schema.json
```

**Definition of Done:**
- Draft lifecycle: `CreateDraftAsync`, `ValidateDraftAsync`, `ApplyDraftAsync`, `DiscardDraftAsync`
- Apply is atomic: writes new `current.json`, copies old to history, appends audit entry — all-or-nothing
- Rollback reads from history and replays `ApplyAsync`
- History retention: 20 most recent kept, older pruned on apply
- `IOptionsMonitor<GatewayConfiguration>` integration for hot-reload subscribers
- Concurrent apply attempts are serialized (mutex)
- Unit tests:
  - Apply rolls back cleanly on validation failure
  - Rollback restores exact previous state
  - Concurrent apply from two tasks produces one winner, one failure
  - Audit log entries are consistent with applied state
  - History pruning works correctly
  - Corrupt `current.json` triggers startup failure with clear error

### B3. License Manager

**Project:** `src/ElpisEdgeConnect.Core/Licensing/`

**Files:**

| File | Purpose |
|------|---------|
| `ILicenseManager.cs` | Contract: current license, is module enabled, check instance limits, validate signature |
| `LicenseManager.cs` | Implementation |
| `LicenseInfo.cs` | Decoded license payload |
| `LicenseModule.cs` | Per-module license state (enabled, maxInstances) |
| `LicenseSignatureValidator.cs` | RSA signature verification |
| `LicenseExpirationTracker.cs` | Emits warnings at 30/7/1 days |
| `LicenseEnforcementPolicy.cs` | Defines behavior on expiration (grace period, block writes) |
| `LicenseException.cs` | Thrown on violations |
| `LicenseEvaluationResult.cs` | Record: allowed, reason, remainingGrace |

**Tooling files (not shipped to customers):**

| File | Purpose |
|------|---------|
| `tools/LicenseGen/Program.cs` | CLI to generate signed licenses |
| `tools/LicenseGen/KeyGenerator.cs` | Generate RSA keypair |

**Definition of Done:**
- RSA signature validation with public key embedded as compiled resource
- License file format matches Section 7.2 exactly
- `IsModuleEnabled(string licenseKey)` returns correct bool with fast in-memory lookup
- `CheckInstanceLimit(string moduleKey, int currentCount)` returns allow/deny with reason
- Expiration behavior: grace period of 30 days, warnings logged at day 30/7/1
- Post-expiration: `ApplyDraftAsync` rejects all changes but data flow continues
- Unit tests:
  - Valid signed license passes
  - Tampered license fails
  - Expired license enters grace
  - Grace exhausted blocks writes
  - Instance count enforcement
  - Module enable/disable enforcement
- Integration test: generate a test license with LicenseGen, load it, verify all fields
- Documentation: `docs/licensing/license-file-format.md`

---

## 6. Milestone C — Pipeline & Routing

### C1. Transform Pipeline

**Project:** `src/ElpisEdgeConnect.Core/Pipeline/`

**Files:**

| File | Purpose |
|------|---------|
| `ITransformStep.cs` | Contract per Section 5 |
| `TransformContext.cs` | Per-invocation context (route id, gateway id, stats sink) |
| `TransformPipeline.cs` | Ordered list of steps, runs in sequence |
| `TransformPipelineBuilder.cs` | Fluent builder from config |
| `Steps/TagMappingStep.cs` | Rename source tags to canonical names |
| `Steps/FilterStep.cs` | Include/exclude by pattern |
| `Steps/DeadbandStep.cs` | Suppress values that haven't changed by threshold |
| `Steps/RateLimitStep.cs` | Cap publish frequency per tag |
| `Steps/TransformStepRegistry.cs` | Registry of available steps (extensible for future) |

**Definition of Done:**
- Each step implements `Apply(input, context) -> output` — pure function semantics
- Steps are stateful between invocations (deadband remembers last value, rate limit remembers last publish time) via per-step state dictionary keyed by tag
- Steps are ordered; ordering is config-driven
- Pipeline short-circuits if output is empty
- Per-step metrics: input count, output count, suppressed count, duration
- Unit tests for each step:
  - Tag mapping: renames correctly, preserves unmapped
  - Filter: include/exclude semantics, glob patterns, regex
  - Deadband: absolute and percentage modes, suppression counts correct
  - Rate limit: correct throttling, resets after window
- Benchmark: 4-step pipeline, 10k points/sec throughput on single thread
- Integration test: full pipeline with all 4 steps from config

### C2. Message Buffer

Buffering is one of the highest-risk areas in Phase 1. It is split into two formal sub-gates. **C2b cannot start until C2a has passed its own Definition of Done and benchmarks.** This checkpoint prevents routing logic from accumulating around a slow or incorrect storage implementation.

---

### C2a. InMemoryBuffer + Serializer + Baseline Benchmarks

**Project:** `src/ElpisEdgeConnect.Core/Buffer/`

**Files:**

| File | Purpose |
|------|---------|
| `IMessageBuffer.cs` | Contract per Section 6 (full contract, including cursor semantics for C2b) |
| `BufferStats.cs` | Record |
| `BufferPolicy.cs` | Runtime policy (mode, depth, age, drop) |
| `BufferMode.cs` | Enum: None, InMemory, StoreAndForward |
| `DropPolicy.cs` | Enum: DropOldest, DropNewest, Block |
| `InMemoryBuffer.cs` | Bounded `Channel<T>` wrapper |
| `SinkCursorTracker.cs` | Per-sink cursor management (abstract, used by both in-memory and sqlite) |
| `CanonicalDataPointSerializer.cs` | Compact binary serialization |
| `CompressionCodec.cs` | LZ4 compression wrapper |
| `ISerializationFormat.cs` | Abstraction to allow swap (MessagePack vs custom) |
| `MessagePackFormat.cs` | MessagePack implementation |

**Definition of Done (C2a):**
- `IMessageBuffer` contract is final — no changes permitted after C2a gate passes
- `InMemoryBuffer` wraps `System.Threading.Channels.Channel<T>` with drop policy (DropOldest, DropNewest, Block)
- Per-sink cursor semantics implemented in `SinkCursorTracker` and tested against mock sinks with divergent progress
- `CanonicalDataPointSerializer` round-trips every value type in `CanonicalValueType`
- Serialization format prototyped: MessagePack measured against a custom BinaryWriter approach — winner locked for C2b
- LZ4 compression verified: round-trip correctness and size reduction for realistic CNC data (expect 5-10x compression on repetitive numeric values)
- Unit tests:
  - Bounded channel respects capacity limit
  - DropOldest evicts in FIFO order
  - DropNewest rejects new writes cleanly
  - Block waits for reader without deadlock
  - Multi-sink cursor divergence: fast sink advances, slow sink lags, buffer releases only when `min(cursors)` advances
  - Serializer round-trips all value types including null, arrays, nested metadata
  - Compression ratio on synthetic CNC data meets expectation
- Benchmarks (C2a gate — must pass before C2b starts):
  - `InMemoryBuffer_Enqueue`: ≥ 100k points/sec single-threaded
  - `InMemoryBuffer_EnqueueDequeue`: ≥ 100k points/sec round-trip
  - `Serializer_Roundtrip`: ≥ 500k points/sec
  - `Compression_Ratio`: ≥ 5x on realistic CNC payloads
  - `Compression_Throughput`: ≥ 200 MB/sec compressed write
  - Zero allocations on steady-state enqueue hot path

**C2a Gate Review:**
Before starting C2b, review:
1. Is the serialization format locked? (MessagePack vs custom — based on benchmarks)
2. Does the contract accommodate SQLite without changes? (If contract changes are needed for SQLite, C2a failed and must be revisited)
3. Are the benchmarks margin-of-safety above targets? (A 100k/sec in-memory buffer is not comfortable — we need ≥150k headroom to leave budget for SQLite overhead)

---

### C2b. SqliteBuffer + Cursor Semantics + Durability + Recovery

**Depends on:** C2a gate passed.

**Files:**

| File | Purpose |
|------|---------|
| `SqliteBuffer.cs` | SQLite-backed durable buffer |
| `SqliteBufferSchema.cs` | Table creation SQL + migrations |
| `SqliteBufferFactory.cs` | Creates per-route buffer files |
| `SqliteConnectionPool.cs` | Pooled connections per route for concurrent access |
| `BufferRetentionEvictor.cs` | Background task that evicts by age/size |
| `SqliteBufferRecovery.cs` | Startup recovery: verify integrity, rebuild cursors if needed |
| `CursorAdvancementBatcher.cs` | Batches cursor updates to reduce write amplification |
| `BufferCorruptionDetector.cs` | Detects corrupt buffer files, quarantines them |

**Schema (SQLite):**

```sql
CREATE TABLE points (
    sequence INTEGER PRIMARY KEY,
    payload BLOB NOT NULL,           -- compressed canonical point
    enqueued_at INTEGER NOT NULL,    -- unix ms
    expires_at INTEGER               -- unix ms, nullable
);
CREATE INDEX idx_points_expires ON points(expires_at);

CREATE TABLE sink_cursors (
    sink_instance_id TEXT PRIMARY KEY,
    committed_sequence INTEGER NOT NULL DEFAULT 0,
    last_attempt_at INTEGER,
    last_error TEXT
);

CREATE TABLE metadata (
    key TEXT PRIMARY KEY,
    value TEXT
);
```

**Definition of Done (C2b):**
- `SqliteBuffer` implements enqueue, dequeue-batch (per sink), ack (advance cursor), eviction
- Uses the serialization and compression primitives locked in C2a — no changes to `IMessageBuffer` contract
- Per-sink cursor semantics per Blueprint Section 19.3 exactly
- Retention: background task evicts rows where `expires_at < now` OR total size > limit (oldest first)
- Point is deletable only when `sequence < min(sink_cursors.committed_sequence)`
- `GetStatsAsync` returns current depth, oldest age, enqueue rate, drain rate, total bytes on disk
- WAL mode enabled for concurrent read/write
- Fsync policy: configurable (default: `NORMAL` — durable enough for edge, fast enough for throughput)
- Cursor updates batched to reduce write amplification (target: 1 cursor write per 100 point writes in steady state)
- Unit tests:
  - Enqueue → dequeue → ack cycle
  - Multiple sinks with independent cursors (one slow, one fast, one recovering)
  - Eviction by age
  - Eviction by size
  - Eviction blocked while any sink cursor still references the point
  - Durability: close buffer, reopen, verify cursor state recovered
  - Crash recovery: kill process mid-write, reopen, verify WAL rollback correctness
  - Corrupt file detection and quarantine
  - Concurrent enqueue from multiple writers
  - Concurrent drain from multiple sinks without cursor crosstalk
- Benchmarks (C2b gate):
  - `SqliteBuffer_Enqueue_SingleWriter`: ≥ 5,000 points/sec
  - `SqliteBuffer_Enqueue_Batched100`: ≥ 15,000 points/sec (batched writes)
  - `SqliteBuffer_DrainBatch_SingleSink`: ≥ 10,000 points/sec
  - `SqliteBuffer_Replay_MultiSinkCursors` (NEW): one route, three sinks (fast/medium/recovering), measure cursor advancement and cleanup cost under realistic fanout load — see detailed spec in Section 7 D4
  - `SqliteBuffer_RecoveryTime_1HourBacklog`: drain 18M queued points in ≤ 2 minutes
  - Storage overhead: < 200 bytes per point on disk (compressed)
  - WAL file size stays bounded under sustained load

**C2b Gate Review:**
Before starting C3 (Routing Engine):
1. Do benchmarks meet all targets with margin?
2. Does the multi-sink cursor replay benchmark show healthy cursor independence (no cross-contamination)?
3. Is crash recovery verified under realistic power-loss simulation?
4. Is WAL growth bounded? (If not, SQLite checkpoint tuning required before C3)

### C3. Routing Engine

**Project:** `src/ElpisEdgeConnect.Core/Routing/`

The Routing Engine is the **full implementation of Blueprint Section 19 — Route Execution Semantics**. Every subsection of Section 19 has a concrete implementation artifact and test. This traceability is enforced during code review.

#### Section 19 Traceability Table

| Blueprint Section | Requirement | Implementation File | Verification |
|-------------------|-------------|---------------------|--------------|
| **19.1** Execution Model | Per-route async worker with bounded channel and buffer | `RouteWorker.cs`, `RouteExecutionContext.cs` | Unit: worker runs independently; Integration: `HappyPath_SingleSourceSingleSink` |
| **19.2** Fanout Semantics | Independent per-sink commit; healthy sinks never wait on failing sinks | `SinkPublisher.cs`, `FanoutDispatcher.cs` | Unit: `Fanout_IndependentSinkProgress`; Integration: `FanoutPartialFailure` |
| **19.3** Buffer Granularity | Per-route storage, per-sink cursors | `RouteBufferBinding.cs` (wires route to `SqliteBuffer` + `SinkCursorTracker`) | Unit: `RouteBufferBinding_Isolation`; Benchmark: `SqliteBuffer_Replay_MultiSinkCursors` |
| **19.4** Retry Tracking | Per-sink, per-batch, in-memory retry state with exponential backoff | `SinkPublisher.cs`, `RetryStateMachine.cs` | Unit: `SinkPublisher_RetryBackoff`, `SinkPublisher_RetryStateResetOnRestart` |
| **19.5** Live vs Replay Ordering | Sequential per-sink drain; live points wait during recovery | `ReplayCoordinator.cs`, `SinkPublisher.cs` | Unit: `Replay_SequentialOrderMaintained`; Integration: `SinkOutageAndRecovery` |
| **19.6** Ordering Guarantees | Per-source monotonic sequence; per-sink in-order delivery | `SinkPublisher.cs` + `CanonicalDataPoint.SequenceNumber` | Unit: `Ordering_PerSourceMonotonic`, `Ordering_PerSinkInOrder` |
| **19.7** Delivery Guarantees | AtMostOnce and AtLeastOnce modes; ExactlyOnce rejected | `DeliveryPolicy.cs`, `DeliveryModeHandler.cs` | Unit: `AtMostOnce_DropsOnFailure`, `AtLeastOnce_RetriesUntilBufferExhausted`, `ExactlyOnce_Rejected` |
| **19.8** Backpressure | Channel → SQLite spillover → drop policy; sources never blocked by sinks | `BackpressureController.cs`, `RouteChannelSpillover.cs` | Unit: `Backpressure_ChannelToSqliteSpillover`; Integration: `BackpressureWithSlowSink` |
| **19.9** Lifecycle States | 9 states with explicit transitions, events emitted to diagnostics | `RouteState.cs`, `RouteLifecycleManager.cs`, `RouteStateTransitionValidator.cs` | Unit: `RouteLifecycle_AllValidTransitions`, `RouteLifecycle_InvalidTransitionsRejected` |

**Files:**

| File | Purpose | Blueprint Section |
|------|---------|-------------------|
| `IRoutingEngine.cs` | Contract: register routes, dispatch data points, get route state | 19.1 |
| `RoutingEngine.cs` | Top-level implementation orchestrating all routes | 19.1 |
| `Route.cs` | Runtime route record (config + live state) | 19.1 |
| `RouteWorker.cs` | Per-route background task | 19.1 |
| `RouteExecutionContext.cs` | Per-execution state (stats, buffer, sinks, pipeline) | 19.1 |
| `RouteBufferBinding.cs` | Wires route to its SQLite buffer and cursor tracker | 19.3 |
| `SinkPublisher.cs` | Per-sink publisher task with independent progress | 19.2, 19.4, 19.5, 19.6 |
| `FanoutDispatcher.cs` | Distributes a transformed batch to all sink publishers independently | 19.2 |
| `RetryStateMachine.cs` | Per-sink retry state with exponential backoff | 19.4 |
| `ReplayCoordinator.cs` | Orchestrates buffer replay during sink recovery | 19.5 |
| `DeliveryPolicy.cs` | Runtime delivery policy (mode, retries, backoff) | 19.7 |
| `DeliveryModeHandler.cs` | Implements AtMostOnce vs AtLeastOnce semantics | 19.7 |
| `BackpressureController.cs` | Channel-to-SQLite spillover logic | 19.8 |
| `RouteChannelSpillover.cs` | Bounded channel with overflow policy | 19.8 |
| `TagFilter.cs` | Compiled include/exclude matcher |  |
| `RouteState.cs` | Enum: Configured, Starting, Running, Draining, Degraded, Stopping, Stopped, Failed, Blocked | 19.9 |
| `RouteLifecycleManager.cs` | Orchestrates state transitions, emits events | 19.9 |
| `RouteStateTransitionValidator.cs` | Validates legal state transitions | 19.9 |

**Definition of Done:**
- Every row in the traceability table above has its implementation file and test(s) in place
- Code review checklist includes "does this PR trace to a Section 19 subsection?" for all Routing Engine changes
- One `RouteWorker` per route, runs as a long-lived `Task`
- Worker consumes from source's output channel, applies filter, runs pipeline, writes to buffer, dispatches to each sink's publisher via `FanoutDispatcher`
- Each `SinkPublisher` runs independently (per Section 19.2) with its own cursor, retry state, and publish loop
- Per-sink cursor advancement on successful publish; cursor updates batched via `CursorAdvancementBatcher` from C2b
- Retry with exponential backoff per Section 19.4; retry state in-memory only, reset on restart
- Live vs replay ordering per Section 19.5: `ReplayCoordinator` drains buffer sequentially; new live points enqueue normally and wait their turn in sequence order
- Delivery mode enforcement per Section 19.7: `AtMostOnce` bypasses buffer entirely, `AtLeastOnce` uses buffer; `ExactlyOnce` throws at config validation time
- Backpressure per Section 19.8: in-memory channel first, SQLite on overflow, drop policy on buffer exhaustion
- Lifecycle per Section 19.9: all 9 states implemented, state transitions validated, transitions emit events to diagnostics collector
- Graceful shutdown: drain in-flight batches, flush buffer, close cleanly within 30s
- Unit tests: every row in traceability table has at least one named test
- Integration test: mock source producing 5k pts/sec, mock sink with induced 30-sec outage, verify zero data loss and correct ordering

### C4. Diagnostics Collector

**Project:** `src/ElpisEdgeConnect.Core/Diagnostics/`

**Files:**

| File | Purpose |
|------|---------|
| `IDiagnosticsCollector.cs` | Contract |
| `DiagnosticsCollector.cs` | Implementation |
| `SourceDiagnostics.cs` | Per-source metrics record |
| `SinkDiagnostics.cs` | Per-sink metrics record |
| `RouteDiagnostics.cs` | Per-route metrics record |
| `GatewayDiagnostics.cs` | Aggregate gateway state |
| `HealthEvaluator.cs` | Computes Healthy/Degraded/Critical from metrics |
| `MetricsWindow.cs` | Sliding window metric computation (1m, 5m, 15m) |
| `DiagnosticEvent.cs` | Event record for audit trail |
| `DiagnosticsStore.cs` | In-memory ring buffer of recent events |

**Definition of Done:**
- Three dimensions per Section 9: Source, Pipeline (Route), Sink
- Metrics implementation via `System.Diagnostics.Metrics` (meters + counters + histograms)
- Sliding window averages over 1/5/15 minute windows
- Exposed via `IDiagnosticsCollector` API for query and via meters for Prometheus/OTel export
- Event ring buffer: last 1000 events per dimension, queryable by time range and filter
- Health evaluator: per-resource thresholds from Section 18.5 (70% warn, 90% critical)
- Thread-safe: high-rate counter updates without contention (use `Interlocked` or per-thread counters)
- Unit tests:
  - Counter accuracy under concurrent updates (100 threads × 10k increments)
  - Sliding window correctness
  - Health state transitions at threshold boundaries
  - Ring buffer wraparound
- Benchmark: 1M counter updates/sec without contention

---

## 7. Milestone D — Host & Integration

### D1. Service Host

**Project:** `src/ElpisEdgeConnect.Host/`

**Files:**

| File | Purpose |
|------|---------|
| `Program.cs` | Entry point, Serilog setup, host builder |
| `GatewayHostedService.cs` | Main `IHostedService` — orchestrates everything |
| `AdapterRegistration.cs` | DI registration of adapters (license-gated) |
| `ServiceCollectionExtensions.cs` | `AddElpisEdgeConnect()` registration |
| `GatewayStartup.cs` | Identity init, license load, config load, routing engine start |
| `GatewayShutdown.cs` | Graceful shutdown sequence |
| `appsettings.json` | Example config |
| `appsettings.Production.json` | Production overrides |
| `Properties/launchSettings.json` | Dev launch profile |

**Definition of Done:**
- Runs as Windows service (via `UseWindowsService()`) and as console app
- Startup sequence: load identity → load license → load config → validate → register adapters → start routing engine
- Startup failure on any step logs clearly and exits with code
- Shutdown sequence (on SIGTERM/Ctrl+C): stop routing engine → drain pipelines → flush buffers → close storage → exit
- Max shutdown time: 30 seconds (configurable)
- Health check endpoint on `:8080/health`
- Logs to console and rolling file (`logs/edgeconnect-.log`)
- Integration test: host starts with sample config + sample license, runs for 60 seconds, shuts down cleanly

### D2. Mock Adapters

**Project:** `tests/ElpisEdgeConnect.Core.Tests/Mocks/` and `tests/ElpisEdgeConnect.Integration.Tests/Mocks/`

These are **not** protocol modules. They live under `tests/` and are the test vehicle for Phase 1 validation.

**Files:**

| File | Purpose |
|------|---------|
| `MockSourceAdapter.cs` | Configurable mock implementing `ISourceAdapter` |
| `MockSourceConfiguration.cs` | Config: points/sec rate, tag count, value patterns, induced errors |
| `MockSinkAdapter.cs` | Configurable mock implementing `ISinkAdapter` |
| `MockSinkConfiguration.cs` | Config: latency, failure rate, induced outages, batch accept limits |
| `MockValueGenerator.cs` | Produces realistic synthetic values (sine waves, steps, random) |
| `MockAdapterScenarios.cs` | Preset scenarios (steady, bursty, flapping sink, slow sink) |

**Mock Source capabilities:**
- Configurable rate: 1 to 100,000 points/sec
- Configurable tag set: 1 to 10,000 tags
- Configurable value patterns per tag: constant, sine, step, random walk, counter
- Induced errors: set error rate, configure error codes, configure recovery
- Supports Polling and Subscription modes
- Supports BrowseTags (returns configured tag definitions)

**Mock Sink capabilities:**
- Configurable latency: fixed or distribution (normal, exponential)
- Configurable failure rate: percentage of publish calls that fail
- Induced outages: `SimulateOutage(duration)` programmatically
- Records all received points in memory (queryable for verification)
- Records all publish attempts with timestamp and result

**Definition of Done:**
- Both mocks implement the real contracts exactly
- Mocks are themselves unit tested
- Scenario library: `SteadyState`, `BurstyTraffic`, `FlappingSink`, `SlowSink`, `OutageRecovery`, `LicenseBlocked`
- Integration tests use mocks exclusively — no real protocols in Phase 1
- Mock adapters document how custom adapters should look (serve as reference)

### D3. End-to-End Integration Tests

**Project:** `tests/ElpisEdgeConnect.Integration.Tests/`

**Test scenarios (each is a full end-to-end test):**

| Scenario | Description |
|----------|-------------|
| `HappyPath_SingleSourceSingleSink` | Steady 1k pts/sec for 60s, verify all points received in order |
| `Fanout_OneSourceThreeSinks` | Verify all three sinks receive all points; verify independent progress |
| `FanoutPartialFailure` | Source → 3 sinks, one sink fails, others continue, verify buffer behavior |
| `SinkOutageAndRecovery` | Source steady, sink outage 5 min, verify buffer grows, recovery drains in order |
| `BufferOverflow_DropOldest` | Sink fails permanently, buffer fills, verify oldest dropped, newest kept |
| `BackpressureWithSlowSink` | Sink latency > source rate, verify channel→sqlite spillover |
| `ConfigHotReload_AddSource` | Running gateway, apply config adding a new source instance, verify it activates |
| `ConfigHotReload_RemoveRoute` | Running gateway, remove a route, verify clean shutdown of route worker |
| `LicenseExpiration_DataContinues` | Trigger license expiration, verify data flow continues, config changes blocked |
| `LicenseBlockedModule` | Attempt to start source for unlicensed module, verify Blocked state, no crash |
| `GracefulShutdown_InFlightBatches` | Stop during active publishing, verify pending batches complete |
| `CrashRecovery_BufferSurvives` | Kill process mid-stream, restart, verify buffer cursors correct, no duplicates |
| `ConcurrentConfigApply` | Two concurrent apply attempts, one wins, both have consistent state |

**Definition of Done:**
- Every scenario is a named xUnit test
- Each scenario uses `MockSource` and `MockSink` with a specific configuration
- Each scenario has explicit assertions on: point counts, ordering, buffer state, route state, diagnostics counters
- Tests are deterministic (no `Thread.Sleep` — use `TaskCompletionSource` or time abstractions)
- Tests complete in < 5 minutes total
- CI pipeline runs all integration tests on every PR

### D4. Performance Benchmarks

**Project:** `tests/ElpisEdgeConnect.Benchmarks/`

**Benchmarks:**

| Benchmark | Target (Medium tier) | Gate |
|-----------|---------------------|------|
| `CanonicalDataPoint_Construction` | 2M points/sec via builder | A1 |
| `Serializer_Roundtrip` | 500k points/sec | C2a |
| `Compression_Throughput` | ≥ 200 MB/sec | C2a |
| `Compression_Ratio` | ≥ 5x on CNC payloads | C2a |
| `InMemoryBuffer_Enqueue` | ≥ 100k points/sec | C2a |
| `InMemoryBuffer_EnqueueDequeue` | ≥ 100k points/sec | C2a |
| `SqliteBuffer_Enqueue_SingleWriter` | ≥ 5k points/sec | C2b |
| `SqliteBuffer_Enqueue_Batched100` | ≥ 15k points/sec | C2b |
| `SqliteBuffer_DrainBatch_SingleSink` | ≥ 10k points/sec | C2b |
| `SqliteBuffer_Replay_MultiSinkCursors` | See detailed spec below | C2b |
| `SqliteBuffer_RecoveryTime_1HourBacklog` | Drain 18M points in ≤ 2 min | C2b |
| `TransformPipeline_4Steps_SingleThread` | 10k points/sec | C1 |
| `RoutingEngine_SustainedThroughput` | 5k points/sec end-to-end | C3 |
| `RoutingEngine_PeakBurst` | 20k points/sec for 30 sec | C3 |
| `DiagnosticsCollector_CounterUpdates` | 1M updates/sec concurrent | C4 |
| `ConfigurationManager_Apply` | < 100 ms for 50-source config | B2 |
| `LicenseManager_IsModuleEnabled` | < 100 ns | B3 |

#### `SqliteBuffer_Replay_MultiSinkCursors` — Detailed Specification

Real route complexity comes from shared route storage with divergent sink progress, not from raw single-sink enqueue speed. This benchmark exercises the full cursor semantics under realistic fanout load.

**Setup:**
- One `SqliteBuffer` (one route)
- Three sinks with distinct characteristics:
  - **Fast sink**: acknowledges batches immediately (0 ms latency, always succeeds)
  - **Medium sink**: acknowledges with 20 ms latency, succeeds
  - **Recovering sink**: starts in failure state for first 30 seconds (buffer grows behind this cursor), then recovers and drains at full speed

**Load profile:**
- Sustained enqueue rate: 5,000 points/sec
- Duration: 90 seconds total
  - 0-30 sec: recovering sink offline, fast and medium sinks consuming normally
  - 30-60 sec: recovering sink online, draining backlog in parallel with live traffic
  - 60-90 sec: steady state with all three sinks caught up

**Metrics to capture:**
- Enqueue latency distribution (p50, p95, p99)
- Per-sink cursor advancement rate
- Buffer depth curve (expected: grows during 0-30s, peaks at ~150k points, drains during 30-60s, returns to near-zero by 90s)
- Point eviction count (expected: zero — retention must hold everything during the outage window at this rate)
- Cursor cleanup cost (how often the retention evictor runs and how much it scans)
- SQL write amplification (total DB writes per point enqueued)
- Memory footprint during peak backlog
- CPU usage per subsystem

**Targets:**
- Enqueue p99 latency during recovering-sink replay < 50 ms (the live path must not be gated by backlog drain)
- Recovering sink catches up to within 1,000 points of the write head by 60-second mark
- Zero points lost
- Zero cursor crosstalk (fast sink cursor must never wait on slow sink)
- Buffer depth at 90 sec mark < 500 points (fully caught up)
- Peak memory overhead (beyond baseline) < 100 MB
- Cursor update batching effective: total DB writes ≤ 1.2x total points enqueued

**Why this benchmark matters:**
This is the one benchmark that exercises Section 19.2 (fanout independence), 19.3 (per-route storage + per-sink cursors), and 19.5 (replay ordering) simultaneously. Raw single-sink enqueue speed can look great while cursor contention silently destroys production throughput. This benchmark catches that class of bug before it ships.

**Definition of Done:**
- BenchmarkDotNet project with `[MemoryDiagnoser]` on every benchmark
- Results published to `docs/benchmarks/phase1-baseline.md`
- Every benchmark meets or exceeds its target
- Memory allocation profiled: zero allocations on hot paths where feasible
- `SqliteBuffer_Replay_MultiSinkCursors` results include depth-over-time chart and cursor advancement timeline
- Baseline captured for regression detection in future phases

### D5. Long-Running Soak Test

**Project:** `tests/ElpisEdgeConnect.SoakTests/`

- 7-day continuous run at 1k pts/sec
- Verify: no memory growth beyond 10% initial
- Verify: no file handle leaks
- Verify: no degradation in throughput
- Verify: diagnostic counters remain consistent
- Run in CI nightly (not per PR)

---

## 8. Cross-Cutting Concerns

### 8.1 Logging Standards

- **Serilog** throughout (already in codebase)
- Structured logging only — no string concatenation in log messages
- Log levels: `Debug` for flow tracing, `Information` for lifecycle events, `Warning` for degraded health, `Error` for failures
- Every log has `SourceContext` (the class name)
- Sensitive data (passwords, tokens) never logged — enforced via `SensitiveDataRedactor`

### 8.2 Observability

- **Metrics:** `System.Diagnostics.Metrics` with named meters per subsystem (`elpis.core.pipeline`, `elpis.core.buffer`, etc.)
- **Traces:** Optional OpenTelemetry integration (instrumented, disabled by default)
- **Events:** `DiagnosticEvent` ring buffer accessible via API
- **Health:** `/health` endpoint with per-subsystem status

### 8.3 Testing Standards

- xUnit for unit tests
- FluentAssertions for readable assertions
- NSubstitute for mocking
- BenchmarkDotNet for benchmarks
- Test naming: `MethodName_Condition_ExpectedResult`
- Arrange-Act-Assert structure with blank lines
- No shared state between tests — each test creates its own fixtures
- Integration tests use temporary directories, cleaned up in `IDisposable.Dispose`

### 8.4 Code Style

- `.editorconfig` enforces style
- Nullable reference types enabled
- `TreatWarningsAsErrors` true for Core
- Analyzers: `Microsoft.CodeAnalysis.NetAnalyzers`, `SonarAnalyzer.CSharp`
- No `async void` except event handlers
- `ConfigureAwait(false)` on library async calls

### 8.5 Documentation

During Phase 1, the following docs must be written alongside code:

| Document | Location |
|----------|----------|
| Core architecture overview | `docs/core/architecture.md` |
| Source adapter contract guide | `docs/adapter-sdk/source-adapter-contract.md` |
| Sink adapter contract guide | `docs/adapter-sdk/sink-adapter-contract.md` |
| Canonical data model reference | `docs/core/canonical-data-model.md` |
| Route execution semantics reference | `docs/core/route-execution.md` (extract from blueprint Section 19) |
| License file format | `docs/licensing/license-file-format.md` |
| Configuration reference | `docs/configuration/config-reference.md` |
| Diagnostics API reference | `docs/diagnostics/api-reference.md` |
| Phase 1 benchmark baseline | `docs/benchmarks/phase1-baseline.md` |

---

## 9. Coding Order (Week by Week)

This is a **reference sequencing**, not a schedule commitment. Actual pace depends on team size.

### Week 1 — Foundations & Decisions
- Resolve entry condition decisions
- Milestone A1: Canonical Data Model + tests + benchmarks
- Milestone A2: Adapter Contracts (no implementation yet)
- Milestone A3: Error taxonomy

### Week 2 — Configuration Foundation
- Milestone B1: Configuration Models
- Milestone B2: Configuration Manager (draft/validate/apply/rollback)
- JSON schema generation tooling
- Unit tests for all config types

### Week 3 — Licensing
- Milestone B3: License Manager
- LicenseGen CLI tool
- Generate test license for use in other milestones
- RSA keypair generation and custody procedure documented

### Week 4 — Pipeline & C2a (InMemoryBuffer + Serializer)
- Milestone C1: Transform Pipeline with all 4 steps
- Milestone C2a: IMessageBuffer contract, InMemoryBuffer, SinkCursorTracker, serializer prototype, compression
- C2a benchmarks: in-memory enqueue, serializer roundtrip, compression throughput/ratio
- **C2a gate review at end of week** — contract locked, serialization format chosen

### Week 5 — C2b (SqliteBuffer + Durability + Recovery)
- Milestone C2b: SqliteBuffer, schema, connection pooling, WAL mode, cursor batching
- Retention evictor, corruption detection, recovery logic
- Durability tests (crash/restart, WAL rollback)
- C2b benchmarks including `SqliteBuffer_Replay_MultiSinkCursors`
- **C2b gate review at end of week** — full buffer subsystem proven before routing engine builds on it

### Week 6 — Routing Engine
- Milestone C3: RoutingEngine with full Section 19 traceability
- RouteWorker, FanoutDispatcher, SinkPublisher, ReplayCoordinator
- BackpressureController, RouteLifecycleManager, RouteStateTransitionValidator
- Unit tests for every row in the Section 19 traceability table
- Integration of pipeline + buffer + routing into working data flow

### Week 7 — Diagnostics & Host
- Milestone C4: Diagnostics Collector
- Milestone D1: Service Host skeleton
- Integration of all components via DI

### Week 8 — Mock Adapters & Integration
- Milestone D2: Mock source and sink adapters
- Milestone D3: All integration test scenarios
- Bug fixes discovered during integration

### Week 9 — Benchmarks & Tuning
- Milestone D4: Full benchmark suite
- Identify and fix hot paths
- Verify all targets met
- Update baseline docs

### Week 10 — Soak Test & Exit
- Milestone D5: Soak test setup and 7-day run
- Documentation finalization
- Phase 1 exit review
- Hand-off to Phase 2

**Actual elapsed time will depend on:** team size, how many decisions need iteration, whether an early design flaw forces rework of an earlier milestone. Budget 10-16 weeks for a single experienced developer, 6-10 weeks for two developers working in parallel across the milestones.

---

## 10. Phase 1 Exit Criteria (Measurable)

Every item must be demonstrably true before declaring Phase 1 complete.

### Functional

- [ ] All files in Section 4, 5, 6, 7 of this plan exist and compile
- [ ] `ElpisEdgeConnect.Core` unit test coverage ≥ 80% (line coverage)
- [ ] All 13 integration test scenarios pass
- [ ] `ElpisEdgeConnect.Host` runs as Windows service and as console app
- [ ] Sample config from blueprint Section 8.1 loads, validates, and runs
- [ ] Sample signed license loads and validates; tampered license rejected
- [ ] Config draft → validate → apply → rollback → reapply round-trip works
- [ ] Diagnostics API returns data for all three dimensions
- [ ] Graceful shutdown completes within 30 seconds
- [ ] Crash recovery test passes: kill process mid-stream, restart, no data loss, no duplicates

### Performance (Medium tier)

- [ ] Sustained throughput ≥ 5,000 points/sec for 24 hours
- [ ] Peak burst ≥ 20,000 points/sec for 30 seconds
- [ ] End-to-end p95 latency < 1 second
- [ ] SQLite buffer enqueue ≥ 5,000 points/sec
- [ ] SQLite buffer drain ≥ 10,000 points/sec
- [ ] Transform pipeline (4 steps) ≥ 10,000 points/sec single thread
- [ ] Diagnostics counter updates ≥ 1,000,000/sec concurrent
- [ ] Config apply < 100 ms for 50-source config
- [ ] License check < 100 ns per call

### Reliability

- [ ] 7-day soak test: RAM growth < 10%, no file handle leaks, no throughput degradation
- [ ] Sink outage 5-min recovery test: zero data loss, recovery drain < 2 min
- [ ] Fanout independence: failing sink does not affect healthy sinks
- [ ] Buffer retention enforcement: drop policy triggers at correct thresholds

### Documentation

- [ ] All 9 docs from Section 8.5 exist and are reviewed
- [ ] Every public API in Core has XML doc comments
- [ ] Phase 1 benchmark baseline captured
- [ ] Architecture blueprint Section 15 open questions resolved or explicitly deferred

### Quality

- [ ] Zero compiler warnings with `TreatWarningsAsErrors` true
- [ ] Zero analyzer warnings at Error level
- [ ] All integration tests deterministic (no flakes over 100 runs in CI)
- [ ] Code review sign-off on every file

---

## 11. Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| SQLite buffer throughput misses target | Medium | High | C2 split into C2a (in-memory + serializer) and C2b (SQLite) with a formal gate between them. C2a locks the contract and serialization format before any SQLite logic accumulates. If C2b misses targets, fallback options (LiteDB, custom append-only file format) are evaluated without rewriting routing engine. |
| Multi-sink cursor contention silently destroys throughput | Medium | High | Dedicated `SqliteBuffer_Replay_MultiSinkCursors` benchmark with realistic fanout load (fast/medium/recovering sinks) gates C2b completion before C3 starts. |
| Route worker backpressure logic is subtly wrong | High | High | Extensive integration tests with mock sinks at various speeds; write the tests before the implementation; Section 19 traceability table in C3 enforces every semantic requirement has a named test |
| License signing key custody unclear | Medium | Medium | Make a decision in Week 1; RSA keypair in password manager is acceptable for v1 |
| Contract changes late in Phase 1 force rework | Medium | High | Lock contracts in Milestone A before Milestone B starts; code reviews focused on contract stability |
| Integration test scenarios miss real production cases | Medium | High | Review scenarios against blueprint Section 19 explicitly; add more if gaps found |
| Memory leaks not caught until soak test | Medium | Medium | Run leak diagnostic nightly from Week 5, not just at Week 10 |
| Serialization format locks into bad choice | Medium | Medium | Prototype MessagePack vs custom in Week 4; measure size and speed |
| Team unfamiliar with BenchmarkDotNet / Channels / Polly | Low | Medium | Short spike at start of Week 4 to build familiarity |

---

## 12. Phase 1 → Phase 2 Handoff

At the end of Phase 1, Phase 2 starts with:

- Working `ElpisEdgeConnect.Core` that passes all exit criteria
- Working `ElpisEdgeConnect.Host` that runs as a service
- Complete adapter SDK documentation
- Mock adapters as reference implementations
- Signed sample license + LicenseGen tool
- Full benchmark baseline

Phase 2 can then:
1. Create `ElpisEdgeConnect.Sources.Focas2` as a new project
2. Implement `ISourceAdapter` against the existing Fanuc logic from the current codebase
3. Wire it via `AdapterRegistration` in `Host`
4. Test with existing Menon customer config
5. Repeat for MT-LINKi, MTConnect, BrotherHttp, MQTT

**Phase 2 cannot start Phase 1 components.** If Phase 2 needs something Core doesn't have, it's a Core deficit that must be fixed in Core, not worked around in the adapter.

---

## 13. Definition of Done (Summary)

Phase 1 is done when:

1. **Code compiles** — Core + Host + all tests + all benchmarks, zero warnings
2. **All tests pass** — unit, integration, benchmarks meet targets, soak test green
3. **All exit criteria checked** — Section 10 of this document, 100% of items
4. **Documentation complete** — Section 8.5 docs written and reviewed
5. **Decisions resolved** — Entry condition decisions made, open questions from blueprint Appendix answered for Phase 1 scope
6. **Handoff ready** — Phase 2 can start immediately with zero Core blockers

---

## Appendix: File Count Estimate

Rough scope estimate for planning:

| Module | Source Files | Test Files | Total |
|--------|--------------|------------|-------|
| `Core/Model` | 7 | 3 | 10 |
| `Core/Adapters` | 11 | 4 | 15 |
| `Core/Errors` | 8 | 2 | 10 |
| `Core/Configuration` | 18 | 8 | 26 |
| `Core/Licensing` | 9 | 5 | 14 |
| `Core/Pipeline` | 9 | 6 | 15 |
| `Core/Buffer` (C2a + C2b) | 19 | 10 | 29 |
| `Core/Routing` (Section 19 full coverage) | 18 | 12 | 30 |
| `Core/Diagnostics` | 10 | 4 | 14 |
| `Host` | 9 | 2 | 11 |
| `Mocks` | 6 | 2 | 8 |
| `Integration Tests` | 0 | 13 | 13 |
| `Benchmarks` | 17 | 0 | 17 |
| **Total** | **141** | **71** | **~212** |

Plus ~10 documentation files. A meaningful but scoped Phase 1. Growth over the original estimate reflects the C2 sub-gate split, the Section 19 traceability artifacts in Routing, and the additional benchmarks (serializer, compression, multi-sink replay).
