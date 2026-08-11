# Multi-protocol pilot expansion — plan v2 (Kepware-class)

**Status:** v2 — rewrites v1 at a higher quality bar. v1's MVP scope is **rejected**; this plan is the active design.
**Date:** 2026-05-28.
**Supersedes:** [v1](2026-05-28-multi-protocol-pilot-plan-v1.md) (kept for reference, not as the active design).
**Driver:** User reframe — "I don't want to cut features. Our benchmark is KepServerEX, Matrikon, etc. I'm OK to reduce protocols, not features, ease of use, reliability or performance."

---

## §0 What changed from v1

| Dimension | v1 (MVP) | v2 (Kepware-class) |
|---|---|---|
| Protocols in scope | 3 (OPC UA Client + EtherNet/IP + MELSEC) | **2** (OPC UA Client + EtherNet/IP). **MELSEC deferred** to a later release. |
| Tag selection | Static list, operator types NodeId / tag path | **Interactive browse** — the wizard talks to the live device and presents a tree picker |
| Auto-load tags | Out of scope | **In scope** — operator clicks "Auto-load all" → all tags from device land in the source |
| Subscription / COV | Out of scope (polling only) | **In scope** — OPC UA monitored items; EtherNet/IP client-side COV layer |
| Block read optimization | Out of scope | **In scope** — multi-tag coalescing where the transport supports it |
| Struct / UDT / array support | Atomic scalars only | **In scope** — UDT expansion (AB), complex type expansion (OPC UA) |
| Auto-reconnect | Basic (state machine only) | **In scope** — exponential backoff, recovery without operator action, per-tag diagnostics |
| Edit-mode hot config | Out of scope | **In scope** — add/remove tags + change polling rate without dropping the connection |
| Performance | "Pilot-acceptable" | **Benchmarked** — explicit targets per protocol with BenchmarkDotNet regression gates. **OPC UA Client: 30K tags/sec primary, 50K+ stretch.** EtherNet/IP: per-controller honest numbers (3K–6K) with aggregate scaling story. |
| Pilot timeline | 2–4 weeks | **10–12 weeks** — pilot follows engineering quality. Includes ~3 weeks of cross-cutting performance hardening. |
| Roadmap v2.3 §1.1 (no new shared abstractions) | Honored | **Explicitly escalated** — browse + auto-load workflows require new shared abstractions. See §4. |

This is a substantially bigger plan than v1. The deliverable count is **roughly 3x** but the product positioning becomes "competes with Kepware/Matrikon on protocol depth" rather than "has a protocol checkbox."

---

## §1 Per-protocol scope at Kepware quality

### 1.1 OPC UA Client — full

**Stack:** `Opc.Ua.Client` from the OPC Foundation stack (already vendored for the Server sink). Reuses `OpcUaCertManager`, `OpcUaSecurityConfig`, `OpcUaCredential`.

**Functional surface:**

| Capability | Detail |
|---|---|
| **Connect** | Endpoint discovery via `GetEndpoints`; auto-pick most-secure endpoint compatible with operator's chosen SecurityMode; explicit endpoint override. |
| **Auth** | Anonymous + UserName + X.509 Certificate user-token policies. Certificate trust chain reuses `OpcUaCertManager`. |
| **Browse** | Full `Browse` / `BrowseNext` traversal. Wizard renders the tree as a `MudTreeView`-based picker. Lazy expansion (no full address-space download on open). |
| **Auto-load** | "Add all tags under this folder" → recurse + filter (Variable nodes only) + bulk-add to source. Optional max-tag-count safety cap. |
| **Subscribe** | Monitored items with configurable PublishingInterval (default 100ms), SamplingInterval, QueueSize, DiscardOldest. KeepAlive + Lifetime tuning. |
| **Data types** | Scalar + Array + Structure types via the UA type system. Struct members expand to canonical points. ExtensionObject decoding for vendor types. |
| **Reconnect** | `SessionReconnectHandler` with exponential backoff (initial 1s, max 60s). Lifetime token survives reconnect; monitored items re-register transparently. |
| **Per-tag diag** | Last-update-at, last-good-value, last-error, success/failure counts. Surfaces in `AdapterHealth.Metrics`. |
| **Hot config** | Add / remove / re-configure monitored items without tearing down the Session. Subscription modify-items API. |
| **Performance target** | **Primary: 30,000 tags/sec sustained**, 50ms publishing interval, **50,000 monitored items per source**. **Stretch: 50,000+ tags/sec sustained.** Requires the cross-cutting performance-hardening work in §1.4 — object pooling, worker-thread notification dispatch, batched sink fan-out, batched SQLite commits. The 50K stretch is gated on early reality-check measurement of the OPC Foundation .NET stack's ceiling against a representative workload (see §8 Q11). |

**Wizard UX:**

| Section | Content |
|---|---|
| 1 Identity | InstanceId, Enabled |
| 2 Endpoint | Server URL, Discovery, Application URI |
| 3 Security | SecurityMode (None / Sign / SignAndEncrypt), Policy, UserTokenPolicy, credential entry, cert path |
| 4 **Browse + select tags** | "Connect & Browse" button → live tree view → multi-select → "Auto-load all under [folder]" button |
| 5 Subscription tuning | Publishing interval, sampling interval, queue size, discard policy (advanced — sensible defaults) |
| 6 Routing | Standard pattern |

**Out of scope:**
- HDA (historical data access)
- Method calls / write capability
- Server diagnostics endpoint consumption
- Custom binary encoding for vendor types beyond what the stack handles

### 1.2 EtherNet/IP (Allen-Bradley) — full

**Stack:** `libplctag.NET` (MPL-2.0) wrapping the native `libplctag` library. Native DLL deployed per RID (win-x64, linux-x64).

**Functional surface:**

| Capability | Detail |
|---|---|
| **Connect** | CompactLogix / ControlLogix / MicroLogix 1400. Connection per CPU; reuse across all tag reads. |
| **Auth** | None — EtherNet/IP CIP doesn't include auth in the v1 protocol; relies on network segregation. (PCCC/EtherNet/IP-Security is a later release.) |
| **Browse** | Read `@tags` and `@tags.STRUCT_TYPE` system tags. Parse the returned dictionary into controller tag list + UDT definitions. Wizard renders as a tree (Program → Routine → Tags). |
| **Auto-load** | "Add all controller tags" + "Add all program tags from MainProgram" buttons. UDT members expand inline. |
| **Subscribe** | EtherNet/IP doesn't have native COV — we layer client-side change-detection on top of fast polling. Configurable poll rate (default 100ms), deadband per tag. |
| **Data types** | BOOL, SINT, INT, DINT, LINT, REAL, LREAL, STRING, BOOL[], DINT[], REAL[], STRING[], UDT (recursive expansion). |
| **Reconnect** | libplctag handles transport-level retry. We layer an adapter-level state machine for repeated failures (degraded → failed transitions with exponential backoff). |
| **Per-tag diag** | Same metric surface as OPC UA Client. |
| **Hot config** | Add / remove tags without dropping the connection; libplctag supports per-tag lifecycle. |
| **Performance target** | **Per-controller, physics-bound by the Rockwell CPU.** Engineering honesty — we publish per-controller numbers, NOT a single aggregate. **MicroLogix 1400: ~500 tags/sec.** **CompactLogix L3x/L4x: 1,500–3,000 tags/sec.** **ControlLogix L7x/L8x: 3,000–6,000 tags/sec.** Aggregate scales linearly with controller count (a site with 10 ControlLogix L7x controllers → ~30–60K tags/sec aggregate). CIP-level block packing handled by `libplctag` internally; our hot-path work in §1.4 lifts whatever ceiling the PLC imposes by removing client-side bottlenecks. |

**Wizard UX:**

| Section | Content |
|---|---|
| 1 Identity | InstanceId, Enabled |
| 2 Connection | Host, Slot (default 0 for CompactLogix), CPU family selector |
| 3 **Browse + select tags** | "Connect & Browse" → controller tag dictionary → tree picker → multi-select → auto-load |
| 4 Polling tuning | Poll interval, deadband per tag (advanced) |
| 5 Routing | Standard pattern |

**Out of scope:**
- Class-1 UDP I/O messaging (we use explicit messaging only)
- PLC-5 / SLC-500 legacy controllers
- Write capability (source is read-only)
- EtherNet/IP Security (later release)

### 1.3 Cross-cutting performance hardening (~3 weeks)

Driver: the 30K-primary / 50K-stretch OPC UA Client target requires hot-path work across the WHOLE data flow, not just inside the OPC UA adapter. Every stage from source-notification-callback through pipeline through buffer through sink must handle 30K+ CDPs/sec without becoming the bottleneck. This work benefits every existing and future adapter — it's a one-time investment in the platform's performance ceiling.

| Area | Change | Why |
|---|---|---|
| **CanonicalDataPoint allocation** | Object pooling via `ObjectPool<T>` for CDPs + metadata dictionaries. Strict zero-allocation on the hot path between source-callback and sink-enqueue. | At 30K/sec a per-CDP allocation is 30K Gen-0 allocations/sec → measurable Gen-0 GC pressure. Pooling eliminates it. |
| **Notification dispatch** | OPC UA stack's publish-callback enqueues into a `Channel<NotificationBatch>`; a dedicated worker thread drains it. Stack thread never blocks on pipeline / buffer / sink. | Default stack behaviour invokes the publish callback synchronously — a slow sink blocks every subscription. Channel + worker decouples them. |
| **Sink fan-out batching** | Sinks receive `PublishAsync(IReadOnlyList<CanonicalDataPoint>)` calls of **50–200 CDPs per batch**, not per-CDP. Existing sinks already accept lists; we tune the batching cadence (every 50ms or 200 CDPs, whichever first). | One method call per 200 CDPs vs 200 calls per 200 CDPs — saves marshalling + lock acquisitions inside the sink. |
| **SQLite buffer batched commits** | Buffer flushes on a 100ms or 500-CDP cadence (whichever first) inside a single SQLite transaction. Per-CDP commits become per-batch commits. | SQLite per-commit cost is ~1ms even on fast SSDs. At 30K/sec uncommitted that's 30 commits/sec batched = 30ms total commit overhead/sec instead of 30 seconds. |
| **Transform pipeline bypass** | If a route has no transforms configured (Filter/Deadband/RateLimit/Aggregation all default), the pipeline calls a fast-path `IdentityTransform` that copies the input batch reference instead of iterating. | Eliminates per-CDP overhead for routes that don't need transforms — the common case for many integrations. |
| **String interning for tag names** | Common tag-name strings (per source) are interned at adapter level via `string.Intern` or a custom dictionary. | A 30K/sec source publishing 5K unique tags produces 6 references per tag per sec. Without interning, the metadata dictionaries hold 30K new string references/sec. |
| **GC tuning** | Validate `<ServerGarbageCollection>true</ServerGarbageCollection>` + `<ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>`. Run a pinned-object-heap audit if Gen-2 pressure appears under sustained load. | Server GC handles high-throughput workloads dramatically better than workstation GC. POH audit catches retention bugs that scale-test would otherwise mask. |
| **Stack ceiling measurement (early)** | Build a throwaway benchmark within the first 3 days of OPC UA Client work: subscribe a Beckhoff/UA Sample Server publishing 100K monitored items at 50ms; measure stack's ceiling on this machine. | Determines whether the 50K stretch is achievable, or whether the stack itself caps us below that. Either way we get a real number to lock as the stretch target. |

**Effort:** ~3 weeks elapsed. Two of those weeks happen inside the OPC UA Client adapter window (~weeks 2–4 of Option B) as the perf work consumes the same code paths the adapter touches. One additional week lands between OPC UA Client wrap and EtherNet/IP kickoff to bed in benchmarks + regression gates.

**Risk:** The 50K stretch target is gated on the stack-ceiling measurement. If the stock OPC Foundation .NET stack caps at, say, 35K on this workload, we lock 35K as the stretch and ship. We do NOT partially re-implement the OPC UA stack to chase the last 15K — that's a separate engineering decision well outside this expansion's scope.

### 1.3.1 Pooled CDP ownership invariants (LOCKED)

> *Why this section exists:* once `CanonicalDataPoint` is pooled, ownership lifetime becomes safety-critical. A CDP returned to the pool while another sink still holds a reference is a memory-corruption bug under high throughput — exactly the kind of Heisenbug the platform must not ship.

**The ownership rule:**

> A pooled `CanonicalDataPoint` batch is acquired from the pool by the source notification dispatcher and is NOT returned to the pool until **every sink publish task AND the buffer persistence operation** for that batch has completed (success, failure, or terminal error).

**Concrete invariants this rule implies:**

1. **Reference-count tracker per batch.** The pool wrapper holds an atomic refcount initialized to (sink_count + 1) at acquire time (+1 for the buffer). Each sink calls `Release()` when its publish task completes (regardless of success/failure). The buffer calls `Release()` when persistence completes. The batch returns to the pool when refcount hits zero.
2. **Sinks must not retain CDP references after `PublishAsync` returns.** If a sink needs to retain a CDP (e.g. for an in-flight retry), it must **clone the CDP into non-pooled memory** before the method returns. Documented in the adapter SDK as a hard rule.
3. **Buffer reads issue non-pooled CDPs.** Replay path reconstructs CDPs from SQLite-serialized form into freshly-allocated objects (NOT from the pool). This avoids ownership ambiguity between the live-publish pool and the replay path. Slight memory cost; large safety benefit.
4. **No CDP escapes the channel.** The bounded channel between publish-callback and worker holds batch handles, not individual CDPs. The handle is the unit of ownership transfer.
5. **Failure-path discipline.** A sink that throws inside `PublishAsync` still owes a `Release()` — guaranteed via `try/finally` in the dispatcher, not the sink itself. Sink author cannot accidentally leak the batch by misusing exception handling.

**Test surface to lock these invariants:**

- `Pool_AcquireRelease_AllSinksAndBuffer_ReturnsToPool` — happy path
- `Pool_SinkThrows_BatchStillReturnsToPool` — exception-path discipline
- `Pool_SinkRetainsReferenceAfterPublishReturns_DetectedByLifecycleAssertion` — runtime detection via debug-builds-only assertion
- `Replay_BufferReadProducesNonPooledCdps` — pin the replay-path rule
- `Pool_BatchHandleNeverEscapesChannel` — analyzer or runtime check

### 1.3.2 Transform pipeline bypass invariants (LOCKED)

> *Why this section exists:* "copy the input batch reference instead of iterating" is correct ONLY if a strict set of immutability conditions hold. Get any of them wrong and we silently corrupt downstream sinks under concurrent access. Locked here.

**The bypass rule:**

> The pipeline calls `IdentityTransform` (which returns the input batch reference directly) IF AND ONLY IF the route has zero configured transforms (Filter / Deadband / RateLimit / Aggregation / any future stage). Otherwise the pipeline iterates and copies as today.

**Concrete invariants this rule implies:**

1. **CDPs are immutable post-publication.** Once a CDP is enqueued into the source-side channel, neither the source adapter nor any pipeline stage may mutate its value, metadata dictionary, or timestamps. The metadata dictionary is `IReadOnlyDictionary` at the type level; runtime checks (debug-build assertions on the dict's concrete type) catch any adapter that smuggled in a mutable dict.
2. **Sinks may read but not mutate CDP fields.** Already implied by `IReadOnlyDictionary`; locked explicitly here for the bypass case. If a sink needs to modify a CDP (e.g. add a sink-specific tag for downstream routing), it must clone into a sink-local copy.
3. **Bypass does NOT skip the per-CDP `Released` invariant from §1.3.1.** Bypass skips transform iteration; it does not skip lifecycle accounting.
4. **Determinism preserved.** Bypass produces byte-identical output to the iterating path for routes with zero transforms — pinned by a determinism test that runs both paths on the same input batch and `Should().BeEquivalentTo()`s the outputs.
5. **Explainability preserved (Platform Principle P4).** Bypass does NOT bypass the diagnostics emission — per-CDP diagnostics counters increment identically whether we bypassed or iterated. If we ever skip diagnostics in bypass for perf reasons, P4 fires and the bypass becomes invalid. **Locked: bypass is a hot-path optimization, NOT a diagnostics-skip optimization.**
6. **No global state mutation in transforms.** Existing transforms must not rely on side effects (e.g. updating a class-level counter that changes future per-CDP behaviour). Audit existing 4 transforms (Filter / Deadband / RateLimit / Aggregation) to confirm they're pure-on-input — recorded as a v2.1 reality-check item.

**Failure mode if invariant 1 is violated:**

A sink mutates a CDP value field. Under bypass, BOTH sinks (in a fan-out scenario) see the mutation because they share the input batch reference. Under iteration, only the iterating sink sees its own copy. Result: silent data corruption in one of two sinks. This is exactly the class of bug that scale-tests hide because it requires concurrent fan-out + mutation pattern to manifest.

**Test surface to lock these invariants:**

- `Bypass_TwoFanOutSinks_BothSeeIdenticalCdpReferences` — happy path
- `Bypass_VsIterating_ByteIdenticalOutput` — determinism gate
- `Bypass_PerCdpDiagnosticsCountersIncrementIdentically` — P4 lock
- `Pipeline_AnyTransformConfigured_DoesNotBypass` — gate the fast-path
- `Determinism_BypassedRoute_ReplayedTwice_IdenticalOutput` — replay invariant

### 1.3.3 SQLite batched commit durability semantics (LOCKED)

> *Why this section exists:* moving from per-CDP commits to batched commits changes the durability granularity. If we don't articulate this against the existing AtLeastOnce delivery contract (Locked Decision §19.7), we accidentally weaken it.

**The durability rule:**

> Batched commits flush to SQLite every **100ms** or every **500 CDPs**, whichever comes first. A crash between commits loses **up to 100ms or 500 CDPs of unbuffered data** from the source-to-buffer hop. The cursor advances **atomically with each commit** — the buffer never claims to have persisted data it hasn't.

**Concrete invariants:**

1. **At-least-once delivery unchanged.** AtLeastOnce promises "no message lost OR a duplicate delivered after recovery." Batched commits preserve this: if the crash happens before a batch commits, those CDPs never entered the buffer → the source is responsible for re-emitting them on recovery (which it already does for stateful sources via lifecycle re-init). For sources without re-emit capability (e.g. event-driven pure-push sources), the source MUST run with `BufferMode = WriteThrough` (per-CDP commit) — a per-source policy flag.
2. **Cursor atomicity.** Each commit transaction does (a) insert N CDPs into the queue table AND (b) advance the source's lastWrittenSeq cursor — same transaction. No partial commits.
3. **Replay correctness.** Replay reads from a committed cursor position. Uncommitted CDPs are not visible to replay (SQLite isolation).
4. **WriteThrough escape hatch.** Each source declares its tolerance for crash-window loss via `BufferMode` enum: `Batched` (new default, 100ms / 500 CDPs) or `WriteThrough` (existing per-CDP behaviour). Sources whose data is unique-per-emission and not re-readable from the device MUST set `WriteThrough`. Documented per-protocol in the adapter SDK.
5. **Performance fall-back.** If batched commits hit any unforeseen issue under load (lock contention, WAL growth), per-source `BufferMode = WriteThrough` is the operator-visible fallback.

**Customer-facing implication:** the gateway data sheet documents "up to 100ms / 500 events of crash-window loss per source in Batched mode; zero crash-window loss in WriteThrough mode at a throughput cost." This is honest and matches how every batched-write industrial buffer (KEPServer's iotgateway, Matrikon's MOAS) documents the same trade-off.

### 1.3.4 Backpressure and sink isolation under batching (LOCKED)

> *Why this section exists:* Locked Decision §19.2 says "fanout is independent per sink — a failing sink never blocks a healthy sink." Batched fan-out must preserve this; a naive shared-channel design would couple sinks together.

**The isolation rule:**

> Each sink has its OWN bounded `Channel<CanonicalDataPointBatch>` with sink-specific capacity. The source-side dispatcher enqueues the same batch into every sink's channel (refcount handles the ownership math from §1.3.1). A sink's slow drain backs up its OWN channel only; other sinks drain at their own rate.

**Concrete invariants:**

1. **Per-sink bounded channels.** Capacity defaults to 1,000 batches per sink (operator-configurable). Channel full → channel writer applies backpressure (`ChannelFullMode.Wait`) OR drops oldest (`ChannelFullMode.DropOldest`) per sink policy.
2. **Drop accounting.** Dropped batches increment a per-sink diagnostics counter; do NOT silently disappear. P4 lock — operators see the drops.
3. **Backpressure does NOT propagate to the source.** A sink filling its channel cannot stall the source adapter. The source dispatcher must use `ChannelWriter.TryWrite` (non-blocking) and increment the sink's drop counter on failure. (Exception: `BufferMode = WriteThrough` sources may opt into source-side blocking when the buffer falls behind. That's a per-source policy.)
4. **Refcount handles cross-sink decoupling.** Sink A failing/slow doesn't prevent sink B's `Release()` from being called when B finishes. Refcount-zero return-to-pool happens whenever the LAST holder releases, regardless of which holder it is.
5. **Failed batches.** A sink that returns `PublishResult.Failed` triggers retry per the route's delivery policy. The batch reference remains alive across retries (refcount held by the retry queue). On terminal failure, the batch is buffered for replay AND released from the in-memory ownership.

### 1.3.5 ReconfigureAsync atomicity (LOCKED)

> *Why this section exists:* hot-reconfigure happens concurrently with publishes. Ambiguity in "when does the change take effect" produces operator-confusing behaviour ("I added tag X but it didn't appear for 10 seconds").

**The reconfigure rule:**

> `ReconfigureAsync(newConfig)` atomically swaps the adapter's **active subscription set** at the next batch boundary. Tags in the new set but not the old start publishing at the next OPC UA publish notification (typically <50ms). Tags in the old set but not the new stop publishing immediately; any in-flight CDPs for removed tags are dropped from new batches but already-enqueued CDPs continue through the pipeline (no retroactive scrubbing).

**Concrete invariants:**

1. **Active-set snapshot at batch boundary.** The dispatcher reads the active subscription set ONCE per batch. Mid-batch reconfigures don't take effect until the next batch.
2. **No subscription tear-down.** Adding/removing tags uses the OPC UA `Subscription.AddItems` / `RemoveItems` APIs — the Session stays alive, the Subscription stays alive, only the monitored items change.
3. **Tag-removal in-flight tolerance.** A removed tag's already-enqueued CDPs are delivered to sinks (NOT scrubbed). The sink sees them; if the sink's downstream system has its own tag list, it's the sink's job to filter. Documented per-protocol — MQTT just publishes the topic; OPC UA Server holds the last value forever until restart.
4. **Reconfigure-during-reconfigure.** A second `ReconfigureAsync` call while the first is in flight throws `InvalidOperationException("RECONFIGURE_IN_PROGRESS")`. Operators get a clear error; the wizard's Edit flow surfaces it as a snackbar with retry guidance.
5. **Validation BEFORE swap.** New config validates fully (all referenced NodeIds resolve; cert trust chain holds; etc.) before the active-set swap. A reconfigure that fails validation leaves the active set unchanged. Operator sees the error; nothing breaks.

### 1.3.6 Platform Principle P4 (explainability) cross-check (LOCKED)

> *Why this section exists:* every performance optimization in §1.3 has the potential to weaken explainability — the platform principle that "every canonical point flows through diagnostics in a way an operator can later attribute" (per `docs/platform-principles.md`).

**The audit:**

| §1.3 change | P4 risk | Mitigation locked in this plan |
|---|---|---|
| Object pooling | Pooled CDPs lose individual identity → diagnostics counters may aggregate wrong | Per-tag counters keyed by `stableTagId`, NOT CDP instance identity. No regression. |
| Worker-thread dispatch | Notification timing skews diagnostics's "time-received" stamp | Stamp is set in the publish-callback BEFORE channel write. Worker thread reads the pre-stamped time. No skew. |
| Sink fan-out batching | A batch failure obscures which CDPs failed | Sinks return per-CDP success/failure within `PublishResult`. Failure diagnostics retain per-CDP granularity. |
| SQLite batched commits | Crash-window loss may be invisible to diagnostics | Source-side counters increment on enqueue (pre-commit). Recovery surfaces "uncommitted-on-crash" count as a startup diagnostic. |
| Transform bypass | Per-CDP diagnostics could be skipped | **Already locked in §1.3.2 invariant 5**: diagnostics emit identically whether bypassed or iterated. |
| String interning | Tag names become identity-shared → memory-attribution off | Diagnostics doesn't measure CDP memory cost; affects only memory profilers (not operator-visible). No P4 impact. |
| Channel-based dispatch | Backpressure drops may go unseen | **Already locked in §1.3.4 invariant 2**: drops increment per-sink counters; operators see them. |

**Conclusion:** none of the §1.3 optimizations weaken P4 IF the per-§ locks above are honored. Validated cross-check completed at v2 authoring time.

### 1.4 MELSEC — **deferred** (recorded for tracking)

Not in this expansion. Recorded here so a future plan trail can pick up the scope:

- Mitsubishi iQ-R / Q-Series / FX5U via SLMP binary frame.
- Browse requires a GX Works3 export-file importer (CSV/XML) — MELSEC doesn't expose tag names in the PLC.
- We'd write the SLMP transport ourselves (HslCommunication is LGPL, incompatible).
- Effort estimate: **5–7 weeks at Kepware quality** including the GX Works3 importer.
- Suggested name for the future plan: `multi-protocol-melsec-expansion-plan-v1.md`.

---

## §2 Sequencing

With 2 protocols at full quality + the cross-cutting performance hardening from §1.3:

### Option A — Strict sequential

OPC UA Client + perf hardening (weeks 1–6) → EtherNet/IP (weeks 7–11). Pilot start: **week 11**.

**Pro:** lowest risk; OPC UA Client + perf hardening fully bedded in before EtherNet/IP consumes the shared abstractions and the new fast paths.
**Con:** wall-clock slowest.

### Option B — Hybrid (recommended)

OPC UA Client adapter spine + shared browse abstractions weeks 1–4. Stack-ceiling measurement in **week 1** (gates the 50K stretch target). Performance hardening weeks 3–6 (overlaps with OPC UA Client UX polish; benefits both protocols). EtherNet/IP **kicks off week 5**, wraps week 10. Pilot start: **week 10–12**.

**Pro:** ~1–2 weeks faster than strict sequential. Shared abstractions + perf hardening de-risked on OPC UA Client first.
**Con:** Weeks 3–6 carry two concurrent tracks (OPC UA Client UX polish + perf hardening). Weeks 5–6 add EtherNet/IP on top. Requires sharp coordination but is the standard plan-trail pattern at this scale.

### Option C — Parallel from day one

Both tracks start week 1. Pilot start: **week 8–10** (faster wall-clock; higher risk).

**Pro:** fastest.
**Con:** **high risk** — shared abstractions get built twice if the agents diverge. ADR-0015 amendments may need retrofitting on both branches. Perf hardening has nowhere clean to land. Net effect usually adds time, not saves it.

### Recommendation

**Option B (hybrid).** Builds the shared abstractions once, runs the stack-ceiling measurement early (week 1, before we've committed to 50K stretch), and amortises the perf-hardening work over the OPC UA Client adapter window. **~10–12 week pilot start.** Matches "very flexible timeline" comfortably without burning quality.

---

## §3 Wizard contract — ADR-0015 amendments needed

The current wizard contract (ADR-0015) doesn't cover browse-driven workflows because none of the existing wizards needed it. New rules to add:

| Proposed rule | Detail |
|---|---|
| **Rule 9 — Browse capability** | Wizards for protocols that expose a browse service MUST surface a "Connect & Browse" button in the tag-selection section. The button calls the wizard's `IBrowseService` implementation and renders results in the shared `TagBrowseTreeView` component. |
| **Rule 10 — Auto-load button** | Wizards with browse MUST also surface an "Add all" / "Auto-load" action that bulk-imports browsed tags into the source. Confirmation prompt if count > 500 (configurable). |
| **Rule 11 — Hot-config invariant** | Edit-mode changes to tag lists in browse-capable wizards MUST go through the adapter's `ReconfigureAsync` rather than full restart. The adapter contract gains a `ReconfigureAsync(SourceConfiguration, CancellationToken)` method. |

These get drafted alongside the v2.1 reality-check pass and locked as an ADR-0015 amendment before implementation starts.

---

## §4 Shared abstractions we need (and the v2.3 §1.1 escalation)

**Roadmap v2.3 §1.1** prohibited new shared abstractions during the Option-B implementation window. That window's locks have expired (per the rule's "until the 7 dedicated plan trails are ratified" clause), so this plan can call for shared abstractions where they're justified. Each one below is explicitly justified.

### 4.1 `ITagBrowseService` (Core)

```csharp
namespace ElpisEdgeConnect.Core.Browse;

/// <summary>Protocol-agnostic tag-browse contract. Implementations live with
/// their adapter (e.g. OpcUaClientBrowseService in Sources.OpcUaClient).</summary>
public interface ITagBrowseService
{
    Task<BrowseResult> BrowseAsync(BrowseRequest request, CancellationToken ct);
}

public sealed record BrowseRequest(string SourceConfigJson, string? StartingNodeId);
public sealed record BrowseResult(BrowseNode Root, bool Truncated);
public sealed record BrowseNode(string NodeId, string DisplayName, BrowseNodeKind Kind,
    string? DataType, IReadOnlyList<BrowseNode> Children, bool HasMoreChildren);
public enum BrowseNodeKind { Folder, Variable, Method, Object }
```

**Why shared:** the wizard `TagBrowseTreeView` component renders any `BrowseResult` regardless of protocol — the rendering logic shouldn't have a `switch` on protocol type. Same shape for OPC UA + EtherNet/IP + (future) MELSEC.

### 4.2 `TagBrowseTreeView` Razor component (Management.Components.Shared)

Lazy-loading tree view bound to `BrowseResult`. Multi-select with checkbox column. "Add selected" + "Add all under this node" actions. Reusable across all browse-capable wizards.

### 4.3 `ISourceAdapter.ReconfigureAsync` (Core adapter contract)

```csharp
// New on ISourceAdapter — Default implementation does Stop + Initialize + Start.
// Browse-capable adapters override with a hot-reconfigure path.
Task ReconfigureAsync(SourceConfiguration newConfig, CancellationToken ct);
```

**Why shared:** Edit-mode wizard saves shouldn't tear down a connected adapter. Default implementation preserves existing semantics; opt-in override for the new browse-capable adapters.

### 4.4 Per-tag diagnostics surface

Already present on `AdapterHealth.Metrics`. We just need to standardise the **metric key shape** across protocols so the diagnostics UI can render uniform per-tag tables:

- `tag.{stableTagId}.lastUpdateAt`
- `tag.{stableTagId}.lastError`
- `tag.{stableTagId}.successCount`
- `tag.{stableTagId}.failureCount`

No code abstraction — just a documented convention in `docs/adapter-sdk/`.

### 4.5 Conflict surface to flag

Adding `ReconfigureAsync` to `ISourceAdapter` is a **breaking change** for the existing 5 source adapters (FOCAS2, Brother HTTP, Modbus, MTConnect, S7). Default interface implementation in C# 8+ lets us add it non-breakingly (default body: stop + initialize + start). But it deserves an explicit decision call: **default-impl now, override later** vs **make every adapter implement it now**.

Recommendation: default-impl now. Existing adapters get the safe "stop + restart" behavior unchanged.

---

## §5 Test surface

### 5.1 Adapter-level tests

| Project | New tests |
|---|---|
| `ElpisEdgeConnect.Sources.OpcUaClient.Tests` (new) | ~80 tests — lifecycle, browse, subscribe, struct types, reconnect, hot config, per-tag diag |
| `ElpisEdgeConnect.Sources.EthernetIp.Tests` (new) | ~70 tests — lifecycle, browse (mocked libplctag), UDT expansion, polling, reconnect, hot config |
| `ElpisEdgeConnect.Core.Tests` | ~15 new tests — `ITagBrowseService` contract, `ReconfigureAsync` default impl |
| `ElpisEdgeConnect.Management.Tests` | ~40 new tests — `OpcUaClientSourceWizardModel`, `EthernetIpSourceWizardModel`, `TagBrowseTreeView` model |

### 5.2 Performance benchmarks (new test surface)

| Benchmark | Target | Regression gate |
|---|---|---|
| `OpcUaClient_SubscribeAndPublish_30kTagsAt50ms` | **≥30,000 tags/sec sustained**, p99 latency <100ms | -10% triggers CI failure |
| `OpcUaClient_SubscribeAndPublish_50kTagsAt50ms` (stretch) | **≥50,000 tags/sec sustained** (or whatever the stack-ceiling measurement found — see §1.3) | Informational; no gate until stretch target is locked |
| `OpcUaClient_ColdStart_50kMonitoredItems` | <30s from Initialize to first publish | -20% triggers CI failure |
| `OpcUaClient_BrowseAddressSpace_LazyExpansion` | <500ms per node expansion at depth 5 | -20% triggers CI failure |
| `EthernetIp_PollControlLogix_L7x_3000Tags` | ≥3,000 tags/sec sustained per source | -10% triggers CI failure |
| `EthernetIp_BrowseControllerTags_50TagsPlusUDTs` | <2s end-to-end | -20% triggers CI failure |
| `Pipeline_NoTransforms_30kCdpPerSecond` | **≥30,000 CDPs/sec end-to-end** (source → sink, no transforms) | -10% triggers CI failure |
| `Buffer_BatchedCommit_30kCdpSustained` | **≥30,000 CDPs/sec sustained** through SQLite buffer (batched commits) | -10% triggers CI failure |
| `SinkFanOut_BatchedPublish_2Sinks_30kCdp` | **≥30,000 CDPs/sec sustained** to two sinks in parallel | -10% triggers CI failure |

Real-PLC integration tests are still **out of CI scope** — pilot hardware validates that. Benchmarks run against mocked transports + a UA Sample Server (the OPC Foundation's reference server) with realistic frame timing. Benchmarks run nightly, not per-PR, because BenchmarkDotNet warmup alone takes 5–10 minutes; PR gates use a 30-second smoke variant.

### 5.3 Integration test additions

`ElpisEdgeConnect.Integration.Tests`: end-to-end "OPC UA Client → MQTT sink" and "EtherNet/IP → MQTT sink" pipelines using mocked transports. ~6 new tests.

### 5.4 Total new test surface

**~210 new tests + 4 benchmark suites.** Compared to v1's 150–240 MVP estimate, the count is similar but the per-test depth is much higher (lifecycle + browse + struct + reconnect coverage, not just happy-path).

---

## §6 QA implications

- Branch the multi-protocol work off `master` **after** PR #45 merges. Tag the in-flight QA baseline as `v0.2.0-qa-baseline`.
- The 67-case QA plan grows by **~30 new cases** per protocol: connect / browse / auto-load / subscribe / reconnect / hot-config / error-recovery / performance smoke / value-type coverage / wizard validation / edit-mode change.
- New QA publish zip at end of week 7 (Option B). Tagged `v0.3.0-multi-protocol-pilot`.
- Pilot hardware validation is the final gate — neither protocol ships without a successful real-PLC + real-FactoryTalk smoke against pilot hardware.

---

## §7 License gating

| Protocol | Feature key | Edition tier |
|---|---|---|
| OPC UA Client | `source.opcua-client` | Pro+ |
| EtherNet/IP | `source.ethernet-ip` | Pro+ |
| (Deferred) MELSEC | `source.melsec` | Pro+ |

License-file change: add the three keys to the pilot customer's license payload. RSA-signed per existing licensing flow (no phone-home, per Locked Decision #6).

---

## §8 Open questions for the v2.1 reality check

The reality-check pass before implementation must confirm:

1. **`Opc.Ua.Client` API surface** — does it cover everything we listed (browse, subscribe, monitored items lifecycle, ReconnectHandler, struct type decoding)? Or are there gaps we'd need to wrap?
2. **`libplctag.NET` API surface** — confirm UDT browse via `@tags` is exposed cleanly; confirm hot tag add/remove; confirm Linux native deployment story.
3. **Default-impl `ReconfigureAsync`** — does it work for ALL existing 5 source adapters with the safe stop+restart fallback? Or does any one of them have stateful behaviour that breaks under stop+restart?
4. **`Sources.S7` skeleton state** — is it half-built? If so, does it ALSO need browse support (Siemens S7 has a published tag dictionary via OPC UA wrap, but native S7 doesn't expose it)? Decide whether S7 stays Pending or gets a separate plan trail.
5. **`MudTreeView` lazy-expansion + multi-select** — confirm the component supports lazy-loaded children + checkbox multi-select natively. If not, we build wrapper logic.
6. **ADR-0015 amendments wording** — draft the three new rules (browse / auto-load / hot-config) for explicit ADR amendment ahead of implementation.
7. **Performance benchmark harness** — does `ElpisEdgeConnect.Benchmarks` already exercise async/IO scenarios, or do we need a transport-mock infrastructure first?
8. **FactoryTalk endpoint specifics** — auth (Anonymous / UserName), security mode (None / Sign / SignAndEncrypt), cert chain, namespace structure. Drives OPC UA Client wizard defaults and the pilot-acceptance test plan.
9. **EtherNet/IP CPU family at the pilot** — CompactLogix / ControlLogix / MicroLogix? Drives default CPU selector + smoke-test target.
10. **Deferring MELSEC** — confirm the pilot customer is OK without MELSEC data at pilot start, OR confirm there's a non-EdgeConnect data path for the Mitsubishi PLCs during the pilot. This is the load-bearing assumption of the K1 pick. **Locked: customer is buying us time to ship MELSEC later.**
11. **OPC Foundation .NET stack ceiling measurement** — within the first 3 days of OPC UA Client work, build a throwaway benchmark subscribing 100K monitored items at 50ms against the UA Sample Server, measure peak sustained throughput on the pilot-class hardware spec. Use the measured ceiling to lock the 50K stretch (or whatever lower number the stack tops out at). This is a **prerequisite gate** before we commit to the 50K+ messaging in product collateral. If the stack tops out at 35K, we lock 35K as stretch and message that; we do NOT partially re-implement the stack.
12. **Sink throughput audit** — at 30K source-side, each sink must drain at 30K. Audit current MQTT sink + OPC UA Server sink throughput; identify whatever batched/streamlined paths need bedding-in to keep up.
13. **`ObjectPool<CanonicalDataPoint>` design** — does CDP's current shape (record with metadata `IReadOnlyDictionary`) pool cleanly, or does it need a re-shape (mutable struct + interned metadata)? Decision call before perf hardening starts.

---

## §9 Out of scope (explicit)

- MELSEC SLMP (deferred — separate plan trail).
- HDA / historical data on either protocol.
- Write capability on either source adapter.
- OPC UA method calls.
- EtherNet/IP class-1 UDP I/O messaging.
- PLC-5 / SLC-500 legacy Allen-Bradley.
- M.2c Runtime Tap integration — adapters expose the standard diagnostics surface; live-watch lands when M.2c lands.
- AI assistance in browse (e.g. AI-suggested tag mappings) — deferred to Phase 4.5 agents.
- Real-PLC integration tests in CI.

---

## §10 What v2.1 (reality check) needs to add

After your review of v2, reality-check runs along **seven specific axes** (per ChatGPT review pass on 2026-05-28). These framings produce higher-value v2.1 content than a generic feature review.

### Axis 1 — Throughput architecture review

- Inspect `Opc.Ua.Client` API surface against §1.1 functional surface (browse, subscribe, monitored items lifecycle, ReconnectHandler, struct decoding). File the gaps.
- Inspect `libplctag.NET` API surface against §1.2; confirm UDT browse + Linux deployment.
- **Stack-ceiling benchmark** — throwaway measurement against UA Sample Server publishing 100K monitored items at 50ms on the pilot-class hardware. Lock the 50K stretch target (or the measured number, whichever is lower). Document the result in v2.1.
- Map each §1.3 hot-path change to the specific code paths it touches. Identify any path that requires architectural change rather than optimization (e.g. CDP shape change for pooling).

### Axis 2 — Pooling and lifetime safety review

- Inspect `CanonicalDataPoint` shape — does it pool cleanly, or does the metadata `IReadOnlyDictionary` force allocation per acquire? Decide: pool the dictionary too, intern keys, or re-shape CDP.
- Design the batch-handle refcount mechanism. Concrete type, atomic ops, debug-build leak detection.
- Lock the "sinks must not retain CDPs after `PublishAsync` returns" rule in the adapter SDK doc.
- Add the test surface from §1.3.1 to the implementation checklist.

### Axis 3 — SQLite durability under batching review

- Validate §1.3.3 invariants against the existing buffer implementation. Cursor atomicity already holds? If not, what changes.
- Document the `BufferMode = Batched | WriteThrough` per-source policy. Default? Per-protocol recommendation?
- Identify which existing sources need `WriteThrough` (event-driven sources that can't re-read).
- WAL growth under sustained 30K/sec — bound it, document the checkpoint cadence.

### Axis 4 — OPC Foundation stack realism

- Identify the stack's specific hot paths (publish-callback overhead, subscription manager locks, encoder/decoder allocations).
- Confirm we can do the §1.3 work WITHOUT forking the stack. Lock: "We do NOT partially re-implement the stack" still holds after audit.
- Map any stack-imposed ceilings (notification queue depth, session manager throughput) against the 30K primary / 50K stretch targets.
- Identify safe tuning knobs (`MaxNotificationsPerPublish`, `KeepAliveCount`, `LifetimeCount`) and lock our defaults.

### Axis 5 — Backpressure and sink isolation review

- Validate §1.3.4 isolation rule against the current dispatcher implementation.
- Per-sink channel capacity defaults (1,000 batches) — is this right? Memory cost analysis.
- `ChannelFullMode.Wait` vs `ChannelFullMode.DropOldest` — per-sink config or global?
- Confirm Locked Decision §19.2 (fanout independence) survives every §1.3 change.

### Axis 6 — Concurrency hazards in hot-reconfigure

- Validate §1.3.5 atomicity rule against the current adapter state machine.
- Active-set snapshot point — locked at batch boundary, but is the "batch boundary" well-defined under the §1.3 channel architecture?
- Reconfigure-during-reconfigure error code — `OPCUA.RECONFIGURE_IN_PROGRESS` vs a Core-level code. Decide naming.
- Test surface — what scale + concurrency exposes hot-reconfigure bugs? Add a `Reconfigure_UnderSustainedLoad_NoDataLoss` test.

### Axis 7 — Benchmark validity review

- Workload profile per benchmark — tag count, value type mix, COV rate, publishing interval. Document BEFORE running benchmarks; otherwise BenchmarkDotNet numbers become un-attributable.
- Calibration against a real OPC UA Server (FactoryTalk or KEPServer) — ONE-TIME, NOT in CI. Compare against UA Sample Server numbers to validate our mock isn't lying.
- Per-controller EtherNet/IP benchmarks — calibrate against a real CompactLogix and a real ControlLogix L7x at the customer site OR in our lab.
- Lock the "nightly benchmark + 30s PR smoke variant" cadence from §5.2.

### Implementation contract — to lock in v2.1

10. Draft ADR-0015 amendments (Rules 9 / 10 / 11) for explicit lock.
11. File-by-file deliverable list per protocol (now sized at Kepware quality + §1.3 hardening, not MVP).
12. Final effort estimate post-reality-check.
13. Decide hybrid kickoff date for EtherNet/IP within the OPC UA Client window (Option B specifics).
14. Audit existing 4 transforms (Filter / Deadband / RateLimit / Aggregation) for purity (§1.3.2 invariant 6).
15. Confirm `MudTreeView` for lazy-load + multi-select; or design replacement.
16. Inspect `Sources.S7` skeleton — fold-in decision (locked: stays Pending per user 2026-05-28).
17. Inspect existing 5 adapters for `ReconfigureAsync` default-impl compatibility.

v2.1 is where the implementation contract gets locked.

---

## §11 Sign-off path

1. **You review v2.** Push back on quality bar, ask about the Kepware-class capabilities table, answer §8 question 10 (MELSEC deferral confirmation with the customer).
2. **(Optional) ChatGPT review pass** — fresh eyes on v2, especially §1 capability tables, §4 shared abstractions, and the realistic 7-week timeline.
3. **I run v2.1 reality-check** — library audits, ADR-0015 amendment drafting, existing-code inspection.
4. **You lock v2.1.**
5. **I implement** — Option B (hybrid) sequencing per §2.

**Locked:** no code branches off `master` until v2.1 is locked. PR #45 merge is a prerequisite (cleaner QA baseline).
