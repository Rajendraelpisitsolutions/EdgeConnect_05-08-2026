# K3 Slice 4 — Review Bundle r2 (atomic handoff, reachable pre-epoch, transport-double)

**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master)
**Commit:** `4a3cc1d` — *fix(sparkplug): K3 slice 4 review r2 — atomic promotion handoff, reachable pre-epoch, transport-double evidence*
**Plan (frozen):** `docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md` (§1.1, §1.2, §4, §5, §6, §9)
**Build:** SparkplugB src `0/0` (warnings-as-errors); tests project `0/0`.
**Tests:** `423 passed / 0 failed / 0 skipped`, broker-free.

r2 folds the three narrow findings. Both r1 pushbacks are withdrawn — the reviewer was right on each. The actual source diff is embedded below (§Source diff); no Core change.

---

## R1 — the reachable planning-failure test (pushback withdrawn)

The r1 unreachability claim was wrong: `LatestMetricValue.Create` permits a pre-Unix-epoch acquisition timestamp, and `SparkplugBirthPlanner.Plan` rejects it through the shared mapper (`SparkplugMetricState.FromLatestValue` → `SparkplugMetricValueMapper.Map` → `SparkplugTimestamp`) with `SPARKPLUG.ENCODE_TIMESTAMP_PRE_EPOCH` — the same reachable case slice 3 already uses.

**Evidence** — `Begin_PreEpochSnapshot_FailsBeforeAliasBdSeqGenerationOrTransport`: builds a valid `LatestValueSnapshot` with a pre-epoch `LatestMetricValue`, drives Begin against a `RecordingStore`, asserts:
- `ResolveAliases` never called; `ReserveNextBdSeq` never called;
- `LastIssuedGeneration == 0`; transport factory never invoked;
- no session promoted; actor `Failed/Faulted`; exception code `EncodeTimestampPreEpoch`; `host.RebirthRequests == 0`.

This locks that **planning stays before alias resolution** (the alias-failure test alone only locked bdSeq-after-alias).

---

## R2 — atomic disconnect/promotion handoff (check-then-promote race fixed)

The flag-then-final-read had the exact interleaving the reviewer described (final read false → disconnect → promote → detach), which could promote a dead transport. Fixed with an **atomic establishment→authority handoff** — a 3-state compare-exchange machine (`Establishing / Invalidated / Promoted`):

- The disconnect handler calls `handoff.OnDisconnect()`: `CompareExchange(Invalidated, Establishing)`. If the prior state was `Promoted`, it sets `SuspectAfterPromotion` instead.
- Promotion calls `handoff.TryPromote()`: `CompareExchange(Promoted, Establishing)`; false means a disconnect already won.
- The candidate `ActiveSession` is built **before** the CAS and **holds the handoff**, so a disconnect landing in the window between a successful `TryPromote` and the `_activeSession` publish still marks the *promoted* reference suspect — never lost.
- The Begin-time handler **stays attached** through the handoff (ownership transfers to the `ActiveSession`); it is no longer detached-then-nulled before promotion. Slice 6 consumes `SuspectAfterPromotion` for operational recovery.
- `_activeSession` remains `volatile` (documented acquire/release for the async-callback reader).

**Invariant proven for both orderings:**
- disconnect-wins → `TryPromote` returns false → **no promoted session** (`SESSION_SUSPECT_DURING_BEGIN`);
- promotion-wins-then-disconnect → promoted session **already flagged suspect** via the shared handoff.

**Evidence**
- `Begin_DisconnectRacesPromotion_PromotesNothing_FaultsSuspect` — a deterministic `PrePromotionBarrier` seam raises `Disconnected(generation)` in the window immediately before the CAS (NBIRTH already locally succeeded); Begin faults `SESSION_SUSPECT_DURING_BEGIN`, promotes nothing, `host.RebirthRequests == 0`, attempt aborted.
- `Begin_PostPromotionDisconnect_MarksSessionSuspect_NotCleanReplaying` — after a clean promotion, a genuine drop for the session's generation flips `CurrentSessionSuspect` true while the authority remains (never left as clean `Replaying` on a dead transport).

---

## R3 — concrete transport-double evidence + exception normalization

**Code change** — `SparkplugMqttTransport` now normalizes framework CONNECT/SUBSCRIBE throws: a non-cancellation exception from `client.ConnectAsync` becomes `SPARKPLUG.TRANSPORT_CONNECT_FAILED`, from `client.SubscribeAsync` becomes `SPARKPLUG.TRANSPORT_SUBSCRIBE_FAILED` (type name only — never the message, which could echo endpoint/credentials); `OperationCanceledException` is re-thrown unwrapped. The two result-validators and the two catch blocks share one `TransportFailure` factory.

**Evidence (`SparkplugMqttTransportBehaviorTests`, controlled `IMqttClient` double, no broker/socket)** — maps 1:1 to the review's required cases:
1. `Dispose_AbortsWithoutCleanDisconnect_AndSuppressesCallback` — client disposed, `DisconnectAsync` never called, actor-facing callback suppressed.
2. `DisconnectAsync_IssuesOneCleanDisconnect_SuppressesCallback_NoSecondOnDispose` — exactly one clean DISCONNECT, callback suppressed, no second disconnect on dispose.
3. `GenuineDisconnect_SurfacesOnce_CarryingCapturedGeneration` — an unsuppressed drop raises the transport event once with the attempt's captured generation.
4. `NewClient_ResetsSuppression_AndDelayedRetiredCallbackKeepsItsGeneration` — after retiring client A, client B's genuine drop is not suppressed and surfaces as gen 2; A's delayed callback still carries gen 1.
5. `ConnectAsync_FrameworkException_NormalizedToTransportConnectFailed`, `ConnectAsync_Cancellation_StaysCancellation_NotWrapped`, `SubscribeExactAsync_FrameworkException_NormalizedToTransportSubscribeFailed`, `SubscribeExactAsync_Cancellation_StaysCancellation_NotWrapped`.

K6 remains responsible for real socket/broker interop (an ungraceful loss actually producing a broker-published NDEATH) and independent-host interoperability.

---

## NBIRTH parity note (reviewer's caveat)

The byte-parity tests construct the expected payload from `actor.CurrentManifest.Metrics`/`.AliasMap` (the resolved manifest), independently re-invoking `EncodeNBirth` with independently-supplied `seq=0`, `bdSeq`, `bdSeqAlias` (1 for the empty route; `max(app aliases)+1 = 3` for the populated route) and the fixed test clock — it is **not** a comparison of the captured payload to itself. The manifest is the resolved input to the encoder, not the encoded output. If you consider reading the manifest post-promotion too indirect, I can pre-build the expected inputs entirely test-side; flag it and I will.

---

## Source diff (as required)

```diff
COMMIT 4a3cc1d — src/ElpisEdgeConnect.Sinks.SparkplugB (transport + actor)

--- SparkplugMqttTransport.cs -------------------------------------------------
@@ ConnectAsync — normalize framework CONNECT throws (cancellation passes through)
-        var result = await client.ConnectAsync(BuildConnectOptions(request), cancellationToken).ConfigureAwait(false);
+        MqttClientConnectResult result;
+        try
+        {
+            result = await client.ConnectAsync(BuildConnectOptions(request), cancellationToken).ConfigureAwait(false);
+        }
+        catch (OperationCanceledException)
+        {
+            throw; // cancellation stays cancellation — never normalized to a transport failure
+        }
+        catch (Exception ex)
+        {
+            throw TransportFailure(
+                SparkplugErrors.TransportConnectFailed, $"CONNECT failed ({ex.GetType().Name}).");
+        }
+
         RequireConnectSuccess(result.ResultCode == MqttClientConnectResultCode.Success, result.ResultCode.ToString());

@@ SubscribeExactAsync — normalize framework SUBSCRIBE throws
-        var result = await client.SubscribeAsync(BuildSubscribeOptions(topicFilter), cancellationToken).ConfigureAwait(false);
+        MqttClientSubscribeResult result;
+        try
+        {
+            result = await client.SubscribeAsync(BuildSubscribeOptions(topicFilter), cancellationToken).ConfigureAwait(false);
+        }
+        catch (OperationCanceledException) { throw; }
+        catch (Exception ex)
+        {
+            throw TransportFailure(
+                SparkplugErrors.TransportSubscribeFailed, $"SUBSCRIBE failed ({ex.GetType().Name}).");
+        }

@@ RequireConnectSuccess / RequireExactNcmdGrant — share one factory
+    private static AdapterException TransportFailure(string code, string message) =>
+        new(new AdapterError { Code = code, Category = ErrorCategory.Network, Message = message, Retryable = false });

--- SparkplugSessionActor.cs ---------------------------------------------------
@@ new suspect accessor
+    internal bool CurrentSessionSuspect => _activeSession?.Handoff.SuspectAfterPromotion ?? false;

@@ BeginReplaySessionAsync — atomic handoff replaces the flag + check-then-promote
-            var invalidated = false;
+            var handoff = new AttemptHandoff(generation);
             disconnectHandler = droppedGeneration =>
             {
-                if (droppedGeneration == generation) { invalidated = true; }
+                if (droppedGeneration == generation) { handoff.OnDisconnect(); }
                 return Task.CompletedTask;
             };
             ...
-            RequireNotInvalidated(invalidated);   // after CONNECT / SUBSCRIBE
+            RequireNotInvalidated(handoff);        // after CONNECT / SUBSCRIBE
             ...
-            RequireNotInvalidated(invalidated); // before promotion (a final volatile read — RACY)
-            attempt.Disconnected -= disconnectHandler;
-            disconnectHandler = null;
-            _activeSession = new ActiveSession(attempt, generation, ..., baseline);
+            if (PrePromotionBarrier is { } barrier) { await barrier().ConfigureAwait(false); } // race seam
+            var candidate = new ActiveSession(attempt, generation, ..., baseline, handoff);
+            if (!handoff.TryPromote()) { throw SessionSuspectDuringBegin(); } // atomic decision
+            _activeSession = candidate; // handler stays attached (ownership transferred)
             attempt = null;

@@ new nested type — the atomic handoff
+    private sealed class AttemptHandoff
+    {
+        private const int Establishing = 0, Invalidated = 1, Promoted = 2;
+        private int _state = Establishing;
+        private volatile bool _suspectAfterPromotion;
+        public bool IsInvalidated => Volatile.Read(ref _state) == Invalidated;
+        public bool SuspectAfterPromotion => _suspectAfterPromotion;
+        public void OnDisconnect()
+        {
+            var prev = Interlocked.CompareExchange(ref _state, Invalidated, Establishing);
+            if (prev == Promoted) { _suspectAfterPromotion = true; }
+        }
+        public bool TryPromote() =>
+            Interlocked.CompareExchange(ref _state, Promoted, Establishing) == Establishing;
+    }
```

(Full unified diff: `git show 4a3cc1d -- src/ElpisEdgeConnect.Sinks.SparkplugB`.)

---

## Approval path status
1. ✅ reachable pre-epoch actor test (R1);
2. ✅ atomic disconnect/promotion handoff + deterministic race tests (R2);
3. ✅ concrete `IMqttClient`-double tests for abort, graceful disconnect, suppression reset, generation capture, exception normalization (R3);
4. ✅ actual source diff embedded above (not only the narrative).

One open caveat surfaced for your ruling: the NBIRTH-parity note above (expected payload built from the resolved manifest vs. fully test-side inputs). Everything else from r1 stands. Slice 5 remains paused pending final sign-off.
