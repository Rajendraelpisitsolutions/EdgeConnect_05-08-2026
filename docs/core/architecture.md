# Core Runtime — Architecture Overview

**Status:** Starter — built out alongside each Phase 1 milestone.
**Last updated:** 2026-04-08 (Week 2 — B2 Configuration Manager landed)

This document is the internal architecture reference for `ElpisEdgeConnect.Core` — the protocol-agnostic runtime foundation of Elpis EdgeConnect. It is intended as a quick orientation guide, not a replacement for the master `ARCHITECTURE_BLUEPRINT.md`. When the two disagree, the blueprint is authoritative.

---

## 1. What Core Is

`ElpisEdgeConnect.Core` is the foundation every protocol module depends on. It contains:

- The **canonical data model** every adapter produces and consumes
- The **adapter contracts** (`ISourceAdapter`, `ISinkAdapter`) every protocol module implements
- The **configuration models** loaded from `config/current.json` (Phase 1 Milestone B1 — landed Week 2)
- The **configuration manager** with draft / apply / rollback (Phase 1 Milestone B2 — landed Week 2)
- The **transform pipeline** steps and runner (Phase 1 Milestone C1)
- The **routing engine** that fans data from sources to sinks (Phase 1 Milestone C3)
- The **store-and-forward buffer** for durable delivery (Phase 1 Milestones C2a + C2b)
- The **license manager** that gates module activation (Phase 1 Milestone B3 — landed Week 3)
- The **diagnostics collector** with three-way observability (Phase 1 Milestone C4)
- The **error taxonomy** every failure path must conform to (Milestone A3)

Core does not contain any protocol-specific code. It does not reference any protocol module. The dependency direction is strictly `Core ← Sources.* / Sinks.*`.

---

## 2. Guiding Principles

These are enforced in reviews by `REVIEW.md`. All come from `ARCHITECTURE_BLUEPRINT.md` Appendix A as LOCKED decisions:

1. **Protocol-agnostic.** Core knows nothing about any specific protocol.
2. **Canonical data model.** All data flowing through the runtime is `CanonicalDataPoint`.
3. **Per-adapter isolation.** One failing adapter must never affect another.
4. **Deterministic data path.** No AI in the pipeline. All routing and delivery behaviour is reproducible.
5. **Forward-compatible sink contract.** The sink contract supports both Push and Pull modes from day one so that OPC UA Server (Phase 5) can be added without a contract break.
6. **Every public member is documented.** `CS1591` is not suppressed; the compiler enforces XML docs.
7. **Strict nullable.** `CS8600/CS8602/CS8603/CS8604/CS8618/CS8625` are promoted to errors in Core (via the nested `.editorconfig`) so null-reference bugs cannot land.

---

## 3. Namespace Layout

```
ElpisEdgeConnect.Core/
├── Model/          — CanonicalDataPoint, value type catalog, quality, factory, builder
├── Adapters/       — ISourceAdapter, ISinkAdapter, state machine, capabilities, health
├── Errors/         — AdapterError, AdapterException, error subclasses, CoreErrors catalog
├── Configuration/  — Config models (B1 ✅) + ConfigurationManager + draft/validate/apply/rollback + audit log + history (B2 ✅, Week 2)
├── Licensing/      — LicenseManager, signature validation, enforcement policy, LicenseGate (B3 ✅, Week 3)
├── Pipeline/       — ITransformStep, pipeline runner, transform steps (Week 4)
├── Buffer/         — IMessageBuffer, InMemoryBuffer, SqliteBuffer (Weeks 4–5)
├── Routing/        — RoutingEngine, RouteWorker, FanoutDispatcher, SinkPublisher (Week 6)
├── Diagnostics/    — IDiagnosticsCollector, source / pipeline / sink dimensions (Week 7)
└── Security/       — ISecretProvider (Phase 2+)
```

Each namespace sub-folder owns its subsystem. Cross-subsystem references are explicit and documented in this file as they get added.

---

## 4. Data Flow (high level)

```
┌──────────────────────┐
│  Device / Protocol   │
└──────────┬───────────┘
           │ protocol-specific reads
           ▼
┌──────────────────────┐
│ ISourceAdapter       │  ←  Lives in ElpisEdgeConnect.Sources.{Protocol}
│ (polling or sub)     │     Emits CanonicalDataPoint via CanonicalDataPointFactory
└──────────┬───────────┘
           │ IReadOnlyList<CanonicalDataPoint>
           ▼
┌──────────────────────┐
│ Transform Pipeline   │  ←  Core/Pipeline/ (C1, Week 4)
│  • Tag mapping       │
│  • Filter            │
│  • Deadband          │
│  • Rate limit        │
└──────────┬───────────┘
           │ transformed points
           ▼
┌──────────────────────┐
│  Route Worker        │  ←  Core/Routing/ (C3, Week 6)
│  • Per-route task    │
│  • Fanout dispatcher │
└──────────┬───────────┘
           │
    ┌──────┴──────┐
    ▼             ▼
┌────────┐  ┌────────────┐
│ Buffer │  │ Buffer     │  ←  Core/Buffer/ (C2a + C2b, Weeks 4–5)
│ (Sink1)│  │ (Sink2)    │     Per-route SQLite storage, per-sink cursors
└────┬───┘  └────┬───────┘
     ▼           ▼
┌────────┐  ┌────────┐
│ Sink 1 │  │ Sink 2 │  ←  Lives in ElpisEdgeConnect.Sinks.{Protocol}
│ (push) │  │ (pull) │     PublishAsync or UpdateCurrentValuesAsync
└────────┘  └────────┘
```

At every arrow, the payload is `CanonicalDataPoint`. Protocol-specific formats exist only inside adapter implementations, never in Core.

---

## 5. Concurrency Model

- **Per-source tasks.** Each source adapter runs in its own async task with its own `CancellationTokenSource`. One failing source cannot block another.
- **Per-route tasks.** Each `Route` has a dedicated `RouteWorker` task that pulls from its source's output channel, runs the transform pipeline, and dispatches to sinks.
- **Per-sink publishers.** Within a route, each target sink has an independent `SinkPublisher` with its own cursor and retry state. Sinks advance independently (blueprint §19.2).
- **Bounded channels between stages.** Backpressure is absorbed by bounded channels and the per-route SQLite buffer; sources are never blocked by sinks.
- **Monotonic sequence allocation** per source via `Interlocked.Increment` on `CanonicalDataPointFactory`. Proven by a 1M-point concurrent test (`SequenceNumbers_MonotonicUnderConcurrentLoad`).

Full semantics are in `ARCHITECTURE_BLUEPRINT.md` §19 "Route Execution Semantics."

---

## 6. Error Model

Every failure the runtime observes is represented as an `AdapterError` carried by an `AdapterException` (or one of its subclasses: `ConfigurationException`, `LicenseException`, `RouteException`, `BufferException`). Errors carry:

- A stable `Code` following `MODULE.CATEGORY_SUBCATEGORY` (e.g., `CORE.LICENSE_EXPIRED`, `FOCAS2.HANDLE_EXHAUSTED`)
- A `Category` (Configuration, Authentication, Network, Protocol, DeviceState, ResourceExhausted, License, Internal)
- A `Retryable` flag that drives the retry state machine
- An optional `SuggestedBackoff` and free-text `Context`

Core's own error codes are catalogued in `Errors/CoreErrors.cs`. Protocol modules define their own catalogues in a parallel file (`Focas2Errors.cs`, `ModbusErrors.cs`, etc.).

See `docs/adapter-sdk/source-adapter-contract.md` §5 for the full error taxonomy guidance.

---

## 7. Lifecycle and State Machine

Adapters move through ten lifecycle states (`Created → Initializing → Initialized → Starting → Running → Degraded / Stopping → Stopped`, plus `Failed` and `Blocked` for error and license paths). Legal transitions are declared in `Adapters/AdapterStateTransitions.cs` as a `FrozenDictionary<AdapterState, FrozenSet<AdapterState>>`, which makes the table fast to check and impossible to cast-mutate.

See `ARCHITECTURE_BLUEPRINT.md` §19.9 for the route lifecycle and `docs/adapter-sdk/source-adapter-contract.md` §6 for the adapter lifecycle diagram and transition table.

---

## 7a. Configuration Models — B1 Notes

The B1 milestone landed a set of immutable, JSON-loadable record types in `Core/Configuration/` that mirror the structure of `config/current.json`. Key design points:

- **Pure JSON DTOs.** B1 records (`SourceInstanceConfig`, `RouteConfig`, etc.) are not the same as the runtime types the routing engine consumes (`Route`, `BufferPolicy`, etc., landing in C3). They are 1:1 mapped — the host project translates DTO → runtime during route activation. This separation keeps validation and JSON concerns out of hot-path code.
- **Opaque `Connection` blocks.** Per the B1 design, source and sink `Connection` fields are typed as `JsonElement?`. The protocol module parses its own connection schema in `InitializeAsync`. Core never knows what's inside.
- **Universal vs protocol-specific publishing fields.** `PublishingSettings` has typed `BatchSize` / `BatchIntervalMs`, plus a `[JsonExtensionData]` `Extras` dictionary that captures any protocol-specific properties (MQTT QoS, HTTP headers, etc.).
- **`required` modifier enforcement.** All mandatory fields use the C# `required` keyword, which `System.Text.Json` honours — missing required properties throw `JsonException` at deserialization. This is stronger than `[Required]` (which only fires during runtime validation).
- **DataAnnotations attributes shipped, not yet enforced at apply time.** B1 declares `[Required]`, `[Range]`, `[RegularExpression]` attributes on records. They are exercised in B1 tests via `Validator.TryValidateObject` but not yet wired into a runtime apply pipeline. B2's `ConfigurationValidator` walks the full graph and runs them at apply time.

### Blueprint vs sample clarification — `BufferPolicy.MaxAge`

Blueprint §4.4 declares `BufferPolicy.MaxAge` as a `TimeSpan`. Blueprint §8.1 sample uses `"MaxAgeDays": 7` (an integer day count). For B1 we adopted the §8.1 sample shape: `BufferPolicyConfig.MaxAgeDays` is an `int`. The runtime types in C3 will translate this into a `TimeSpan` internally. This is documented as a known blueprint/sample drift item to reconcile during the C3 type-mapping work.

### B2 — Configuration Manager (lifecycle + audit log)

The configuration manager (`IConfigurationManager` / `ConfigurationManager`) owns the draft → validate → apply → rollback lifecycle for the gateway's persistent configuration. Key design points:

- **Atomic apply.** `ApplyDraftAsync` runs the full validation pipeline inside an async mutex, then writes the previous current to history, appends an audit entry with a SHA-256 hash of the previous line, atomically replaces `current.json`, deletes the draft file, runs retention pruning, and emits `CurrentChanged`. A crash mid-sequence leaves the previous state intact (the atomic-write pattern in `FileSystemConfigurationStore` writes to a `.tmp` file and renames over the destination).
- **Concurrent applies serialize.** Two callers attempting `ApplyDraftAsync` concurrently are serialized through the mutex; the second caller revalidates against the new current state from the first caller, so a draft that was valid against an older base may fail with a fresh error.
- **Tamper-evident audit log.** Every audit entry carries a `PreviousHash` (SHA-256 of the previous line, lowercase hex). Reading the log with `verifyChain: true` recomputes the chain and throws `ConfigurationException` (`CORE.CONFIG_AUDIT_CORRUPT`) on the first mismatch. Per blueprint §17.7. The audit log file lives at `data/config/history/audit.log` and is append-only JSONL.
- **History retention.** After every successful apply or rollback, `HistoryRetentionPolicy` (default: keep 20 most recent) prunes oldest history files. Audit log is never pruned.
- **Rollback creates a new version, never overwrites.** Rolling back to version V0 produces a fresh version V_new whose content matches V0; the audit log entry is marked `RolledBack`. Future rollbacks can roll back the rollback.
- **Cross-record validation.** `CrossRecordValidator` enforces 10 invariants that span multiple records (route source/sink references, duplicate ids, AtMostOnce ↔ buffer mode compatibility, tag mapping target validity). Each rule is documented inline with its blueprint reference.
- **Schema validation interface.** `IConfigurationSchemaValidator` is defined in Core with a no-op default. The real NJsonSchema-backed implementation lives in `ElpisEdgeConnect.SchemaValidation` so Core stays free of external package dependencies.
- **License gate hand-off.** `ILicenseGate` defines the contract that B3 will fulfil. B2 ships `AllowAllLicenseGate` which always permits everything, so the validator pipeline shape is locked now and B3 ships as a drop-in implementation.
- **No `IOptionsMonitor` dependency on Core.** Per the Q2 design decision, Core exposes only a plain `CurrentChanged` event. The host project (Phase 2+) wraps it in an `IOptionsMonitor` adapter if needed.
- **No initial event at startup.** Per the Q4 design decision, `InitializeAsync` does NOT raise `CurrentChanged`. Subscribers call `GetCurrentAsync` for their baseline.

### B3 — License Manager (signed file, fast-path checks, gate)

The licensing subsystem (`Licensing/`) loads an RSA-PSS signed JSON license file, exposes an immutable in-memory snapshot, and enforces module / instance / expiry rules through `LicenseGate` (which slots into B2's `ILicenseGate` seam without any change to `ConfigurationValidator`).

- **Algorithm (locked).** RSA-PSS, SHA-256, MGF1-SHA256, salt length = hash length, minimum 2048-bit key. The full rationale and the canonical-JSON rules live in `docs/licensing/license-file-format.md`.
- **Canonical JSON.** `CanonicalJson.cs` implements one — and only one — canonicalizer used by both LicenseGen and the runtime, so signer/verifier drift is impossible. Property keys are sorted lexicographically at every depth, the top-level `signature` field is stripped, no whitespace, raw numeric text preserved.
- **Embedded public key.** `EmbeddedPublicKey.cs` carries a PEM-encoded RSA public key compiled into the binary. **The current key is a DEV key**, marked as such in the file header; `LicenseSignatureValidatorTests.EmbeddedKey_FingerprintMatchesExpectedDevValue` pins its SHA-256 fingerprint so accidental key swaps fail the build. Production rotation is documented in §10 of the license format doc.
- **Date semantics.** `expiresAt` is a date with no time component and is interpreted as **23:59:59.999 UTC** of that day. A license dated 2026-04-07 remains `Valid` throughout 2026-04-07 UTC. This is pinned by `LicenseExpirationTrackerTests.EndOfDay_StillValid_ThroughoutLastDay`, not just prose.
- **Lifecycle states.** `NotLoaded` → `Valid` → `InGracePeriod` (30 days) → `Expired`. `Invalid` is reached when a load fails verification; the previously-loaded snapshot is preserved on failed reloads.
- **Grace period behaviour.** While in grace, configuration changes are still permitted, but `LicenseGate` attaches a `Warnings` entry to the result. Once grace is exhausted, `LicenseGate` blocks all configuration changes regardless of content. **Data flow is never blocked by the license check** (blueprint §7.4).
- **Fail-closed when not loaded.** `LicenseGate` allows an empty configuration (zero sources, sinks, routes) for first-boot bootstrapping but rejects any non-empty configuration submitted before a license has been loaded.
- **Fast-path checks.** `LicenseInfo.Modules` is a `FrozenDictionary<string, LicenseModule>` so `IsModuleEnabled` is allocation-free and fits the sub-100-ns budget.
- **Concurrency model.** Loads are serialized by a `SemaphoreSlim`. Reads (`Current`, `IsModuleEnabled`, `CheckInstanceLimit`) read a `volatile` snapshot reference with no lock. The snapshot itself is fully immutable and is replaced atomically.
- **Warning channel.** `event EventHandler<LicenseWarning> WarningRaised` on `ILicenseManager` fires once per (`Status`, `DaysUntilExpiry`) boundary; the dedupe set is reset on every successful `LoadAsync`. Core never logs — the host subscribes and decides where the warning lands (Serilog, event log, REST endpoint, etc.).
- **`LicenseGen` tool.** `tools/LicenseGen/` is an internal CLI that generates RSA-2048 keypairs and signs license files. It references Core for canonicalization to prevent signer/verifier drift. Not shipped to customers.

`AllowAllLicenseGate` from B2 is still wired by default in `ConfigurationValidator`'s parameterless constructor, so existing tests are unaffected. Hosts (Phase 2+) opt in to `LicenseGate` by registering it explicitly in DI.

### C1 — Transform Pipeline

The transform subsystem (`Pipeline/`) applies per-route data transformations to batches of `CanonicalDataPoint` between a source and the sinks. It is deterministic, allocation-frugal on the hot path, and runs on a single route-worker thread — it does not need internal synchronization.

- **Fixed execution order (LOCKED by blueprint §5):** `TagMapping → Filter → Deadband → RateLimit`. Users cannot reorder; "config-driven" means presence/absence only. `TransformPipelineBuilder` walks this order via the `TransformStepKind` enum and omits any step whose config is empty (or, for `FilterStep`, the include-* identity case).
- **Batch contract.** `ITransformStep.Apply(input, output, context)` takes the current batch as an `IReadOnlyList<CanonicalDataPoint>` and writes surviving points into a caller-owned `List<CanonicalDataPoint>`. Steps must not mutate input entries; to change a field they produce a new record via `with`. `TransformPipeline` double-buffers two lists internally and swaps them between steps, so a pipeline invocation does not allocate a new list per step.
- **Short-circuit.** `TransformPipeline.Execute` returns immediately when the input batch is empty OR when a step empties the current buffer — downstream steps do not run. This is pinned by `TransformPipelineTests.EmptyAfterStep_ShortCircuits_LaterStepsNotInvoked` using an "explode" step registered after a drop-all step.
- **Metrics.** `ITransformMetricsRecorder` is the test seam; `MeterTransformMetricsRecorder` is the production implementation backed by a `System.Diagnostics.Metrics.Meter` named **`ElpisEdgeConnect.Core.Pipeline`** (LOCKED). Per-step counters: `pipeline.step.input`, `pipeline.step.output`, `pipeline.step.suppressed`, plus a `pipeline.step.duration_us` histogram. All four carry `route_id` and `step` tags. Exporter wiring is Phase 2 host responsibility; C1 only ships the meter and the abstraction.
- **FilterStep target (D5 locked).** Glob matches against `CanonicalDataPoint.TagPath`, not `TagName`, so renames from the preceding `TagMappingStep` do not disturb filter decisions. A null/whitespace `TagPath` is a contract violation (A1) and causes `FilterStep` to throw `ConfigurationException(PipelineInvalidFilterPattern)` — fail-loud rather than silent mismatch.
- **Glob dialect (D6 locked).** `*` matches any run of non-`/` characters within one path segment; `?` matches exactly one non-`/` character; `**` matches any run of characters including `/`. Path separator is `/`. No regex, ever — prevents ReDoS and keeps patterns predictable. The compiler lives in `Pipeline/Steps/GlobMatcher.cs` and is exhaustively tested.
- **DeadbandStep.** Absolute and percentage modes, per-tag state keyed by `TagPath`. First observation per tag always passes (no baseline). Null values always pass (they cannot establish a baseline). Non-numeric values always pass and clear the stored last-numeric so a future numeric reading starts fresh. Percentage mode is `|current - last| / max(|last|, ε) ≥ fraction`. A tag may only use one mode; the cross-record validator rejects a tag appearing in both `Deadband` and `DeadbandPercent` at config-apply time with `PipelineDeadbandConflict`.
- **RateLimitStep.** Per-tag minimum interval, state keyed by `TagPath`. Wall clock comes from `TransformContext.UtcNow` (injectable) so tests are deterministic. First observation always passes; subsequent observations pass once the interval has elapsed. Zero or negative intervals are rejected at construction and at validation time (`PipelineInvalidRateLimit`).
- **EnrichmentTags is intentionally DORMANT in C1.** `TransformProfileConfig.EnrichmentTags` remains a valid config field (validated and persisted by B1/B2) but has NO runtime effect in C1. No enrichment step exists. A later milestone will activate it. This is called out in the config XML doc and here to prevent silent confusion when setting values has no observable outcome.
- **Cross-record validation (new in C1).** `CrossRecordValidator.CheckTransformProfiles` adds four rules: absolute deadband thresholds must be finite and ≥ 0, percentage thresholds must be in (0, 1], a tag cannot be in both deadband maps, and rate-limit values must be strictly positive.

### C2a — InMemoryBuffer + Serializer + Compression Codec

The buffer subsystem (`Buffer/`) is the per-route durability layer between the transform pipeline and the sinks. C2a delivers the in-memory implementation, the FINAL `IMessageBuffer` contract that C2b's SQLite buffer will satisfy unchanged, and the serialization + compression primitives whose benchmark numbers anchor the Phase 1 baseline. **The full contract spec lives in `docs/core/buffer-contract.md`** — that document is authoritative; this section is the architectural overview.

- **Final contract (LOCKED).** `IMessageBuffer` is sink-aware: `DequeueBatchAsync(sinkId, n)` and `AckAsync(sinkId, upTo)` operate against per-sink cursors so a single buffer instance can fan out to many sinks without duplicating storage. `RegisterSinkAsync` / `DeregisterSinkAsync` model sink lifecycle so eviction math is correct as sinks come and go. `DequeueBatchAsync` returns a `BufferBatch` that carries the sequence range so callers ack precisely. These three refinements (D1, D6, D7) concretize the simplified blueprint §6 sketch and are the same shape C2b's SQLite buffer must satisfy.
- **Per-sink cursors via `SinkCursorTracker`.** A monotonic cursor per sink id, lock-guarded, with `TryAdvance` (cursors can never regress), `Min()` for eviction math, and `FastForwardBelow()` for the `DropOldest` case where slow sinks must be bumped past evicted slots. Reused as-is by C2b's SQLite buffer — it is the seam between the two implementations.
- **`InMemoryBuffer`** is backed by a fixed-capacity `CanonicalDataPoint?[]` ring (index = `seq % capacity`) plus a `SinkCursorTracker`. The `Channel<T>` mentioned in the plan was rejected (D2) because `Channel<T>` is consume-once and cannot support per-sink cursors without duplicating storage. The buffer is multi-producer / multi-consumer-safe via a single object lock; the hot path is short. Block-mode enqueue uses a `TaskCompletionSource<bool>` re-created on every space release; producers re-check after creating the waiter and respect cancellation.
- **`BufferPolicy` vs `BufferPolicyConfig`.** Two distinct types — the B1 `BufferPolicyConfig` is the JSON DTO (forgiving, optional, user-edited); the C2a `BufferPolicy` is the strict required-init runtime record consumed by the buffer factory. `BufferPolicy.FromConfig(...)` is the only place the conversion happens. Tests, code, and docs must keep these layers separate; confusing them is a class of bug we deliberately make impossible by giving them different namespaces and required fields.
- **Reuses `Configuration.BufferMode` and `Configuration.DropPolicy` (D3).** The Buffer subsystem does NOT duplicate these enums under `Buffer/`. The B1 enum is the single source of truth, referenced by Buffer code via a using directive — preventing drift between the JSON DTO and the runtime types.
- **Two serializers ship in C2a (D4).** `BinaryWriterFormat` is a hand-rolled compact binary format (1-byte version header + length-prefixed UTF-8 strings + value-tagged value encoding + tagged metadata primitives). `MessagePackFormat` routes through a buffer-internal `CanonicalDataPointDto` so Core records stay free of serialization attributes. Both round-trip every `CanonicalValueType` losslessly including null, ByteArray, Array, Object, and metadata; both preserve `DateTime.Kind == Utc`. The **winner is locked at the C2a gate review** by benchmark numbers (`SerializerBenchmarks`); the loser is removed in C2b. The format facade `CanonicalDataPointSerializer` lets the rest of the runtime stay format-agnostic until the lock is committed.
- **`CompressionCodec`** is a stateless LZ4 wrapper (`K4os.Compression.LZ4`). Wire format: `[int32 originalLength][lz4 block]`. The in-memory buffer in C2a does NOT compress (waste for in-RAM references); the codec ships in C2a so its benchmark numbers land in the Phase 1 baseline. C2b's SQLite buffer is the consumer. Pinned: ≥ 5× ratio on the realistic CNC payload fixture in `CompressionCodecTests`.
- **Benchmark gate (NEW for this milestone).** Unlike B1/B2/B3/C1, C2a has explicit benchmark pass criteria. `tests/ElpisEdgeConnect.Benchmarks/Buffer/` ships `InMemoryBufferBenchmarks`, `SerializerBenchmarks`, and `CompressionBenchmarks` with `[MemoryDiagnoser]`. Targets: 100k pts/sec enqueue and round-trip, 500k pts/sec serializer round-trip, ≥5× compression ratio, ≥200 MB/sec compressed throughput, zero allocations on steady-state enqueue. Numbers are captured at the C2a gate review to `docs/benchmarks/phase1-baseline.md` and become the regression floor for C2b.
- **C2b contract-stability proof.** `docs/core/buffer-contract.md` §6 walks through the SQLite mapping for every method on `IMessageBuffer` and demonstrates that no contract change is required to land C2b. This is the C2a gate-review answer to "does the contract accommodate SQLite without changes?" — yes.

### Schema generation

A separate `tools/SchemaGen` project (not part of Core) emits JSON Schemas from the configuration records via NJsonSchema. Run with:

```bash
dotnet run --project tools/SchemaGen -c Release
```

Output is written to `docs/config-schemas/`. The generated schemas are checked into the repo so they can be referenced by editors and pre-validation tooling without requiring developers to run the generator first.

---

## 8. Where to Read More

- **Canonical data model:** `docs/core/canonical-data-model.md`
- **Source adapter contract:** `docs/adapter-sdk/source-adapter-contract.md`
- **Sink adapter contract:** `docs/adapter-sdk/sink-adapter-contract.md`
- **Master architecture reference:** `docs/ARCHITECTURE_BLUEPRINT.md` (19 sections, the authoritative source of truth)
- **Phase 1 execution plan:** `docs/PHASE1_EXECUTION_PLAN.md` (milestones, deliverables, exit criteria)
- **Review checklist:** `REVIEW.md` (walked by every PR review)
- **Session context:** `CLAUDE.md` (rules, anti-patterns, decisions locked in Week 1)

---

## 9. This Document's Status and Growth Plan

This file is a **starter overview** written during Week 1 close. It will grow as each Phase 1 milestone lands:

| Section | When it gets fleshed out |
|---------|--------------------------|
| §2 Guiding Principles | Stable now (Week 1) |
| §3 Namespace Layout | Updated as each subsystem lands; Configuration/ models marked landed in B1 (Week 2) |
| §4 Data Flow | Stable high-level now; concrete details added in §5 of the blueprint |
| §5 Concurrency Model | Partially stable now; full detail lands with Routing Engine (Week 6) |
| §6 Error Model | Stable now (Week 1 A3); CORE.CONFIG_* codes will be exercised by B2's validator (Week 2-3) |
| §7 Lifecycle | Adapter lifecycle stable; Route lifecycle lands Week 6 |
| Configuration models (B1) | Stable now; types are JSON-loadable DTOs. Validation runner in B2. |

Every Phase 1 milestone that lands in Core should include an update to this document touching the relevant section. Treat it as living documentation, not a one-time write. If a milestone lands without updating this file, that is a 🟡 finding per `REVIEW.md` §9 "Docs-to-code mismatch."
