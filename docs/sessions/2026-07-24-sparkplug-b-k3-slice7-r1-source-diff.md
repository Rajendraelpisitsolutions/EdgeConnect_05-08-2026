# K3 Slice 7 — Exact Source Diff r1 (NCMD classification, counter semantics, coherent snapshot, failure diagnostics)

**Commit:** `7df9e86` — *fix(sparkplug): K3 slice 7 review r1*
**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master)

Full `git show` (7 files, 0 elision) for line-level sign-off.

```diff
commit 7df9e86ed27725b763d180841ef779de2d047744
Author: Sudhakar <sudhakar@elpisitsolutions.com>
Date:   Fri Jul 24 23:26:53 2026 +0530

    fix(sparkplug): K3 slice 7 review r1 - NCMD classification, counter semantics, coherent snapshot, failure diagnostics
    
    Folds the four review blockers. No Core change.
    
    B1 — NCMD classification. SparkplugNodeCommand.Classify returns a redacted
    SparkplugNodeCommandKind (RebirthRequested / RebirthRequestedWithUnknownExtras /
    IgnoredMalformed / IgnoredMissing / IgnoredNull / IgnoredWrongType / IgnoredFalse)
    instead of a bool. The actor actions a rebirth once (even with unknown extras),
    tallies ignored kinds (nodeCommandsIgnored), and surfaces a sanitized
    lastNodeCommandDiagnosticCode (no metric names or payload bytes).
    
    B2 — stale + coalesced counters. Stale is now decided by handoff IDENTITY
    (IsStaleCallback: neither the authoritative session's handoff nor the in-progress
    _establishingHandoff), catching a replaced client's delayed callback that carries
    its own real generation — not merely arg != captured-generation. Coalescing is
    counted at MarkRebirthNeeded (which now reports opened-new vs folded), so a
    re-drain from a blocked DATA/cutover path no longer inflates the tally.
    
    B3 — coherent semantic snapshot + health mapping. Lifecycle/protocol/session are
    published together only at a gated transition (PublishTransition, under _diagLock);
    a read overlays live Interlocked counters/timestamps onto that last coherent record
    rather than reconstructing semantic state from independent field reads, so no torn
    combination is observable and diagnosticsVersion does not advance on a read. Health
    now maps using BOTH HasSession and ProtocolState (Healthy = ready-no-session OR
    active-Live), and lastStateChangeAt is exposed in Metrics.
    
    B4 — complete failure diagnostics. An untyped failure (illegal transition /
    actor-loop) records a sanitized fallback SPARKPLUG.ACTOR_FAILURE code + time, so a
    Faulted actor always has a last error. publishFailures/birthFailures now also count
    in-transport (uncertain) send cancellations; a pre-send cancellation (aborted at the
    gate) counts nothing. transportRecoveryAttempts is incremented AFTER admission, so a
    disposal-rejected admission records no attempt.
    
    567 SparkplugB tests; regressions green: Core 1250, Host 225, Management 1149 (full
    project). Solution 0 errors; SparkplugB 0 warnings under warnings-as-errors.
    
    Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>

diff --git a/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugActorDiagnostics.cs b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugActorDiagnostics.cs
index bcf0eab..e0eeb7d 100644
--- a/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugActorDiagnostics.cs
+++ b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugActorDiagnostics.cs
@@ -99,6 +99,9 @@ internal sealed record SparkplugActorDiagnostics
     /// <summary>The sanitized code of the last recovery failure, or null.</summary>
     public string? LastRecoveryFailureCode { get; init; }
 
+    /// <summary>The sanitized diagnostic code of the last inbound NCMD classification (no names/bytes), or null.</summary>
+    public string? LastNodeCommandDiagnosticCode { get; init; }
+
     // --- last-event timestamps (injected clock; null until first occurrence) ---
 
     /// <summary>When the last successful birth (NBIRTH promotion) occurred.</summary>
@@ -134,6 +137,9 @@ internal sealed record SparkplugActorDiagnostics
     /// <summary>Delayed NCMD callbacks from a retired transport generation, ignored.</summary>
     public required long StaleNodeCommandCallbacks { get; init; }
 
+    /// <summary>Inbound NCMDs that classified as a non-actionable (ignored) kind.</summary>
+    public required long NodeCommandsIgnored { get; init; }
+
     /// <summary>Core rebirth requests actually queued (a fresh episode woke Core).</summary>
     public required long RebirthRequestsQueued { get; init; }
 
diff --git a/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugNodeCommand.cs b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugNodeCommand.cs
index 81c614f..c1f948d 100644
--- a/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugNodeCommand.cs
+++ b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugNodeCommand.cs
@@ -1,14 +1,16 @@
 // ============================================================================
 // File: Session/SparkplugNodeCommand.cs
 // Purpose: The pure, fail-safe NCMD classifier (plan v3 §1.6, ADR-0036 Rule 4).
-//          Decodes an inbound NCMD payload and reports ONLY whether it is a valid
-//          Node Control/Rebirth = true command. Every other NCMD case — a malformed
-//          payload, a rebirth metric that is not a boolean or is false, an unknown
-//          metric — is a NO-OP (returns false), so a bad or hostile NCMD can never
-//          cause a side effect. The actor turns a true result into a coalesced,
-//          non-reentrant RequestRebirthAsync(HostCommand); the parser itself never
-//          publishes, mutates protocol counters, or touches the store.
-// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md §1.6, §9.
+//          Decodes an inbound NCMD payload into a REDACTED classification: a valid
+//          Node Control/Rebirth = true command (optionally accompanied by unknown
+//          extra metrics), or one of the ignored kinds (malformed, missing rebirth
+//          metric, explicit null, wrong value type, false). Every non-actionable
+//          kind is a NO-OP so a bad or hostile NCMD can never cause a side effect,
+//          but each kind is now DISTINGUISHABLE so the actor can tally it and
+//          surface a sanitized diagnostic code (slice-7 review B1). The classifier
+//          never publishes, mutates protocol counters, touches the store, or exposes
+//          a raw metric name or payload byte.
+// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md §1.6, §9, §11.
 // ============================================================================
 
 using System;
@@ -17,17 +19,42 @@ using Org.Eclipse.Tahu.Protobuf;
 
 namespace ElpisEdgeConnect.Sinks.SparkplugB.Session;
 
-/// <summary>Classifies an inbound NCMD payload (rebirth command detection only).</summary>
+/// <summary>The redacted classification of an inbound NCMD payload (plan v3 §1.6, §11 acceptance matrix).</summary>
+internal enum SparkplugNodeCommandKind
+{
+    /// <summary>A well-formed Node Control/Rebirth = true, with no other metrics present.</summary>
+    RebirthRequested,
+
+    /// <summary>A well-formed Node Control/Rebirth = true, accompanied by unknown extra metrics.</summary>
+    RebirthRequestedWithUnknownExtras,
+
+    /// <summary>The payload did not parse as a Sparkplug NCMD.</summary>
+    IgnoredMalformed,
+
+    /// <summary>No Node Control/Rebirth metric was present (includes an unknown-only command).</summary>
+    IgnoredMissing,
+
+    /// <summary>The Rebirth metric was present but explicitly null.</summary>
+    IgnoredNull,
+
+    /// <summary>The Rebirth metric was present but not a boolean value.</summary>
+    IgnoredWrongType,
+
+    /// <summary>The Rebirth metric was present, boolean, but false.</summary>
+    IgnoredFalse,
+}
+
+/// <summary>Classifies an inbound NCMD payload (rebirth-command detection with redacted diagnostics).</summary>
 internal static class SparkplugNodeCommand
 {
     /// <summary>
-    /// Return <c>true</c> only when <paramref name="payload"/> is a well-formed Sparkplug NCMD
-    /// carrying a <c>Node Control/Rebirth</c> metric whose boolean value is <c>true</c>. A malformed
-    /// payload or any other content is a no-op (<c>false</c>) — never a side effect.
+    /// Classify <paramref name="payload"/> into a redacted <see cref="SparkplugNodeCommandKind"/>. A valid
+    /// <c>Node Control/Rebirth = true</c> is actionable (optionally flagged as carrying unknown extras); every
+    /// other kind is a no-op with a distinguishable diagnostic. Never throws, never has a side effect.
     /// </summary>
     /// <param name="payload">The raw inbound NCMD payload bytes.</param>
-    /// <returns><c>true</c> for a valid rebirth command; otherwise <c>false</c>.</returns>
-    public static bool IsRebirthRequest(ReadOnlyMemory<byte> payload)
+    /// <returns>The classification.</returns>
+    public static SparkplugNodeCommandKind Classify(ReadOnlyMemory<byte> payload)
     {
         Payload parsed;
         try
@@ -36,20 +63,62 @@ internal static class SparkplugNodeCommand
         }
         catch (Google.Protobuf.InvalidProtocolBufferException)
         {
-            return false; // malformed NCMD — ignore with no side effect
+            return SparkplugNodeCommandKind.IgnoredMalformed;
         }
 
+        Payload.Types.Metric? rebirth = null;
+        var hasOtherMetrics = false;
         foreach (var metric in parsed.Metrics)
         {
-            if (string.Equals(metric.Name, SparkplugPayloadEncoder.NodeControlRebirthMetricName, StringComparison.Ordinal)
-                && !metric.IsNull // an explicitly-null Rebirth metric carries no command (frozen acceptance matrix)
-                && metric.ValueCase == Payload.Types.Metric.ValueOneofCase.BooleanValue
-                && metric.BooleanValue)
+            if (string.Equals(metric.Name, SparkplugPayloadEncoder.NodeControlRebirthMetricName, StringComparison.Ordinal))
+            {
+                rebirth ??= metric;
+            }
+            else
             {
-                return true;
+                hasOtherMetrics = true;
             }
         }
 
-        return false;
+        if (rebirth is null)
+        {
+            return SparkplugNodeCommandKind.IgnoredMissing; // no rebirth metric (includes unknown-only commands)
+        }
+
+        if (rebirth.IsNull)
+        {
+            return SparkplugNodeCommandKind.IgnoredNull;
+        }
+
+        if (rebirth.ValueCase != Payload.Types.Metric.ValueOneofCase.BooleanValue)
+        {
+            return SparkplugNodeCommandKind.IgnoredWrongType;
+        }
+
+        if (!rebirth.BooleanValue)
+        {
+            return SparkplugNodeCommandKind.IgnoredFalse;
+        }
+
+        return hasOtherMetrics
+            ? SparkplugNodeCommandKind.RebirthRequestedWithUnknownExtras
+            : SparkplugNodeCommandKind.RebirthRequested;
     }
+
+    /// <summary>True when the classification is an actionable rebirth request (with or without extras).</summary>
+    public static bool IsActionableRebirth(this SparkplugNodeCommandKind kind) =>
+        kind is SparkplugNodeCommandKind.RebirthRequested or SparkplugNodeCommandKind.RebirthRequestedWithUnknownExtras;
+
+    /// <summary>A short, stable, secret-free diagnostic code for the classification (no metric names/bytes).</summary>
+    public static string DiagnosticCode(this SparkplugNodeCommandKind kind) => kind switch
+    {
+        SparkplugNodeCommandKind.RebirthRequested => "rebirth",
+        SparkplugNodeCommandKind.RebirthRequestedWithUnknownExtras => "rebirth+unknown-extras",
+        SparkplugNodeCommandKind.IgnoredMalformed => "ignored:malformed",
+        SparkplugNodeCommandKind.IgnoredMissing => "ignored:missing",
+        SparkplugNodeCommandKind.IgnoredNull => "ignored:null",
+        SparkplugNodeCommandKind.IgnoredWrongType => "ignored:wrong-type",
+        SparkplugNodeCommandKind.IgnoredFalse => "ignored:false",
+        _ => "ignored:unknown",
+    };
 }
diff --git a/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs
index b186195..a9882c7 100644
--- a/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs
+++ b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs
@@ -82,11 +82,19 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
     // semantics (the documented synchronization mechanism for this cross-thread field).
     private volatile ActiveSession? _activeSession;
 
+    // The handoff of the CURRENTLY in-progress establishment attempt (set when the attempt's client+handoff
+    // are created, cleared on promotion or abort). A transport callback is STALE unless its handoff is either
+    // the authoritative session's handoff OR this in-progress one — the concrete transport echoes its own
+    // captured generation, so a retired client's delayed callback matches its own handler and must be
+    // recognised by identity, not by the generation argument alone (slice-7 review B2).
+    private volatile AttemptHandoff? _establishingHandoff;
+
     // --- Slice 7 diagnostics (plan v3 §8): monotonic lifetime counters, per actor instance, NEVER
     // reset on rebirth. Interlocked for the ones touched from asynchronous MQTT callbacks; the rest are
     // gate-owned but use Interlocked consistently so the health snapshot reads them coherently. ---
     private long _staleDisconnectCallbacks;
     private long _staleNodeCommandCallbacks;
+    private long _nodeCommandsIgnored;
     private long _rebirthRequestsQueued;
     private long _rebirthRequestsCoalesced;
     private long _healthyRebirths;
@@ -104,21 +112,25 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
     private long _lastRebirthRequestAtTicks;
     private long _lastErrorAtTicks;
 
-    // The coherent, versioned diagnostic snapshot (plan v3 §8): rebuilt under _diagLock at each transition
-    // and published as a single volatile reference, so a reader never sees a torn field combination. The
-    // persistent "current-state" fields below are mutated ONLY under _diagLock (inside RepublishDiagnostics).
+    // The coherent SEMANTIC snapshot (plan v3 §8, slice-7 review B3): the lifecycle/protocol/session fields
+    // that must be mutually consistent, published as a single volatile reference ONLY at a completed gated
+    // transition (under _diagLock). A reader NEVER reconstructs semantic state from independent field reads;
+    // it takes this last coherent record and overlays the independent, monotonic counters/timestamps/last-event
+    // values (each read atomically). So no torn combination is ever observed, and Version is a transition
+    // change-token that does NOT advance on a mere read.
     private readonly object _diagLock = new();
     private long _diagVersion;
-    private int _currentRecoveryAttempt;
-    private string? _lastRecoveryFailureCode;
-    private AdapterError? _lastError; // sanitized (Message cleared) — the last recorded error, or null
+    private int _currentRecoveryAttempt;                                             // current-state (semantic; gated)
+    private volatile string? _lastRecoveryFailureCode;                               // last-event overlay (read live)
+    private volatile string? _lastNodeCommandDiagnosticCode;                         // last-event overlay (read live)
+    private volatile AdapterError? _lastError;                                       // sanitized; last-event overlay
     private AdapterState _priorState = AdapterState.Created;                          // state before the last transition
     private SparkplugProtocolState _priorProtocol = SparkplugProtocolState.Stopped;
     private AdapterState _lastObservedState = AdapterState.Created;                   // most recent state (change detector)
     private SparkplugProtocolState _lastObservedProtocol = SparkplugProtocolState.Stopped;
     private DateTimeOffset _lastStateChangeAt;
     private string _lastTransitionReasonCode = "created";
-    private volatile SparkplugActorDiagnostics? _diagnostics;
+    private volatile SparkplugActorDiagnostics? _semantic;
 
     /// <summary>Construct a lifecycle-only actor (no identity store — cannot begin a session). Test/internal.</summary>
     /// <param name="instanceId">The sink instance id (non-empty).</param>
@@ -334,14 +346,20 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
             return Task.FromCanceled<AdapterHealth>(cancellationToken);
         }
 
-        // One coherent, versioned read (plan v3 §8): every field below comes from the same snapshot, so
-        // health can never report a field combination that never existed.
+        // One coherent read (plan v3 §8): the lifecycle/protocol/session fields come from a single semantic
+        // snapshot (never reconstructed from a torn read), so health can never report a combination that never
+        // existed.
         var diag = DiagnosticsSnapshot;
 
+        // Health uses BOTH the protocol substate AND HasSession (slice-7 review B3): Healthy is ONLY
+        // ready-no-session (Running + Stopped + no session) or active-Live (Running + Live + session). Every
+        // other Running establishment/recovery/transitional/suspect state is Degraded.
         var level = diag.State switch
         {
             AdapterState.Failed => HealthLevel.Unhealthy,
-            AdapterState.Running when diag.ProtocolState is SparkplugProtocolState.Stopped or SparkplugProtocolState.Live
+            AdapterState.Running when diag.ProtocolState is SparkplugProtocolState.Stopped && !diag.HasSession
+                => HealthLevel.Healthy,
+            AdapterState.Running when diag.ProtocolState is SparkplugProtocolState.Live && diag.HasSession
                 => HealthLevel.Healthy,
             AdapterState.Running => HealthLevel.Degraded,
             AdapterState.Degraded => HealthLevel.Degraded,
@@ -365,8 +383,10 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
             ["pendingRebirth"] = diag.PendingRebirth,
             ["currentRecoveryAttempt"] = diag.CurrentRecoveryAttempt,
             ["recoveryAttemptBudget"] = diag.RecoveryAttemptBudget,
+            ["lastStateChangeAt"] = diag.LastStateChangeAt.UtcDateTime,
             ["staleDisconnectCallbacks"] = diag.StaleDisconnectCallbacks,
             ["staleNodeCommandCallbacks"] = diag.StaleNodeCommandCallbacks,
+            ["nodeCommandsIgnored"] = diag.NodeCommandsIgnored,
             ["rebirthRequestsQueued"] = diag.RebirthRequestsQueued,
             ["rebirthRequestsCoalesced"] = diag.RebirthRequestsCoalesced,
             ["healthyRebirths"] = diag.HealthyRebirths,
@@ -387,6 +407,7 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
             metrics["nextSeq"] = diag.NextSeq!.Value;
         }
         if (diag.PendingRebirthReason is { } reason) { metrics["pendingRebirthReason"] = reason; }
+        if (diag.LastNodeCommandDiagnosticCode is { } ncmdCode) { metrics["lastNodeCommandDiagnosticCode"] = ncmdCode; }
         if (diag.LastRecoveryFailureCode is { } recoveryFailure) { metrics["lastRecoveryFailureCode"] = recoveryFailure; }
         if (diag.LastErrorCode is { } errorCode) { metrics["lastErrorCode"] = errorCode; }
         if (diag.LastErrorCategory is { } errorCategory) { metrics["lastErrorCategory"] = errorCategory; }
@@ -511,6 +532,9 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         else
         {
             ValidateRecoveryOwnership(recoveryToken);
+            // Count the lifetime tally only AFTER admission passed — a disposal/supersession that rejects
+            // admission must NOT record a "complete establishment attempt" (slice-7 review B4).
+            Interlocked.Increment(ref _transportRecoveryAttempts);
         }
 
         // Generation exhaustion is checked BEFORE reserving a durable bdSeq (carry-forward 2 / B1), so the
@@ -538,6 +562,7 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
 
         ISparkplugMqttTransport? attempt = _transportFactory!();
         var handoff = new AttemptHandoff(generation);
+        _establishingHandoff = handoff; // this attempt is now the in-progress one (stale-callback identity, B2)
         var disconnectHandler = MakeDisconnectHandler(generation, handoff);
         var nodeCommandHandler = MakeNodeCommandHandler(generation, handoff);
         attempt.Disconnected += disconnectHandler;
@@ -555,8 +580,17 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
             SetProtocolState(SparkplugProtocolState.Birthing);
             var nbirth = SparkplugPayloadEncoder.EncodeNBirth(
                 SparkplugSequenceNumber.Create(0), bdSeq, prepared.BdSeqAlias, _clock(), prepared.Resolved.Metrics, prepared.Resolved.AliasMap);
-            var published = await attempt.PublishAsync(SparkplugTopicFactory.NBirth(node), nbirth, cancellationToken)
-                .ConfigureAwait(false);
+            bool published;
+            try
+            {
+                published = await attempt.PublishAsync(SparkplugTopicFactory.NBirth(node), nbirth, cancellationToken)
+                    .ConfigureAwait(false);
+            }
+            catch (OperationCanceledException)
+            {
+                Interlocked.Increment(ref _birthFailures); // in-transport NBIRTH cancellation (uncertain send, B4)
+                throw;
+            }
             if (!published)
             {
                 Interlocked.Increment(ref _birthFailures); // NBIRTH publish failed (plan v3 §8)
@@ -583,6 +617,8 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         {
             if (attempt is not null)
             {
+                // This attempt failed/aborted: it is no longer the in-progress establishment.
+                Interlocked.CompareExchange(ref _establishingHandoff, null, handoff);
                 attempt.Disconnected -= disconnectHandler;
                 attempt.NodeCommandReceived -= nodeCommandHandler;
                 // ABORT: dispose without a clean DISCONNECT so the broker publishes the Will (NDEATH).
@@ -619,6 +655,7 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         }
 
         _activeSession = candidate; // volatile publish
+        Interlocked.CompareExchange(ref _establishingHandoff, null, candidate.Handoff); // now authoritative, not in-progress
         _nextSeq = 1;               // NBIRTH consumed seq 0
         Interlocked.Exchange(ref _lastSuccessfulBirthAtTicks, _clock().UtcTicks); // NBIRTH succeeded (plan v3 §8)
         // Normalize the diagnostic substate: a candidate that became suspect between promotion and
@@ -713,12 +750,18 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         // Uncertain-send boundary (r2 R2.4): an in-transport cancellation/exception marks the reused handoff
         // suspect (Rebirthing -> Suspect) and never strands it in Rebirthing. A clean local false with no
         // transport loss stays a genuine (fatal) NBIRTH failure.
-        var published = await SendAsync(session, SparkplugTopicFactory.NBirth(node), nbirth, cancellationToken)
-            .ConfigureAwait(false);
-        if (!published && !session.Handoff.SuspectAfterPromotion)
+        var published = await SendAsync(
+            session, SparkplugTopicFactory.NBirth(node), nbirth,
+            () => Interlocked.Increment(ref _birthFailures), cancellationToken).ConfigureAwait(false);
+        if (!published)
         {
-            Interlocked.Increment(ref _birthFailures); // a genuine local NBIRTH failure (plan v3 §8)
-            throw BirthPublishFailed(); // a genuine local NBIRTH failure with no transport loss is fatal (§4.5)
+            // Count ANY non-completed NBIRTH — a genuine local failure (fatal below) OR an uncertain send that
+            // pivots to transport recovery when the drop already latched suspect (slice-7 review B4).
+            Interlocked.Increment(ref _birthFailures);
+            if (!session.Handoff.SuspectAfterPromotion)
+            {
+                throw BirthPublishFailed(); // a genuine local NBIRTH failure with no transport loss is fatal (§4.5)
+            }
         }
 
         // Deterministic race barrier immediately before the atomic rebirth-completion compare-exchange.
@@ -814,10 +857,10 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
             {
                 ValidateRecoveryOwnership(token); // and before EACH attempt — no CONNECT/bdSeq/generation after loss
 
-                // One complete CONNECT/SUBSCRIBE/NBIRTH attempt is about to run — count it and surface the
-                // current ordinal within this episode (distinct from the lifetime attempts tally, plan v3 §8).
-                Interlocked.Increment(ref _transportRecoveryAttempts);
-                RepublishDiagnostics("recovery-attempt", currentRecoveryAttempt: attempt);
+                // Surface the current ordinal within this episode (the lifetime attempts tally is incremented
+                // inside AttemptConnectionAsync, AFTER admission, so a rejected admission records no attempt, B4).
+                _currentRecoveryAttempt = attempt;
+                PublishTransition("recovery-attempt");
 
                 try
                 {
@@ -825,7 +868,8 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
                         prepared, sessionId, epoch, previous.RouteId, previous.Host, token, cancellationToken).ConfigureAwait(false);
                     await PromoteAndDrainAsync(candidate).ConfigureAwait(false);
                     Interlocked.Increment(ref _transportRecoverySuccesses);
-                    RepublishDiagnostics("recovery-success", currentRecoveryAttempt: 0);
+                    _currentRecoveryAttempt = 0;
+                    PublishTransition("recovery-success");
                     return; // recovered within budget — no route fault
                 }
                 catch (Exception ex)
@@ -859,7 +903,7 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
             // remained, the loop's inner filter would have retried it) → terminal fault (plan v3 §8 envelope).
             // A non-retryable/fatal-prep failure does not match this filter and propagates without counting.
             Interlocked.Increment(ref _transportRecoveryExhaustions);
-            RepublishDiagnostics("recovery-exhausted", AsAdapterError(ex), recoveryFailureCode: AsAdapterError(ex)?.Code);
+            _lastRecoveryFailureCode = AsAdapterError(ex)?.Code; // overlaid on read; the fault below publishes state
             throw;
         }
         finally
@@ -867,7 +911,8 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
             // Clear ONLY if the token is still ours — a superseding lifecycle call may already have replaced
             // or nulled it, and we must not stomp a newer owner.
             Interlocked.CompareExchange(ref _activeRecoveryToken, null, token);
-            RepublishDiagnostics("recovery-ended", currentRecoveryAttempt: 0); // no episode running once we exit
+            _currentRecoveryAttempt = 0; // no episode running once we exit
+            PublishTransition("recovery-ended");
         }
     }
 
@@ -909,9 +954,16 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         {
             if (droppedGeneration != generation)
             {
-                Interlocked.Increment(ref _staleDisconnectCallbacks); // ignore + diagnostic counter (plan v3 §7)
-                RepublishDiagnostics("stale-disconnect");
-                return Task.CompletedTask; // stale generation: ignore authoritatively (the generation gate)
+                return Task.CompletedTask; // inconsistent transport argument — defensive ignore (not a stale event)
+            }
+
+            // A genuine delayed callback from a REPLACED client (its own real generation) is stale: its handoff
+            // is neither authoritative nor the in-progress attempt. Count it and ignore, never touching the
+            // current authority (slice-7 review B2).
+            if (IsStaleCallback(handoff))
+            {
+                Interlocked.Increment(ref _staleDisconnectCallbacks);
+                return Task.CompletedTask;
             }
 
             handoff.OnDisconnect(); // atomic: invalidate a pre-promotion attempt OR mark the authority suspect
@@ -920,9 +972,13 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
                 return Task.CompletedTask; // pre-promotion drop -> Begin's establishment handles it, no rebirth
             }
 
-            handoff.MarkRebirthNeeded(RebirthReason.Other);
-            // Queue ONE coalesced Core rebirth now if the authority is published; otherwise establishment
-            // drains it after publication (so an idle drop always wakes Core).
+            if (!handoff.MarkRebirthNeeded(RebirthReason.Other))
+            {
+                Interlocked.Increment(ref _rebirthRequestsCoalesced); // folded into an open episode (B2)
+            }
+
+            // Queue ONE Core rebirth now if the authority is published; otherwise establishment drains it after
+            // publication (so an idle drop always wakes Core).
             return DrainRebirthAsync(handoff);
         };
 
@@ -931,19 +987,30 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         {
             if (receivedGeneration != generation)
             {
-                Interlocked.Increment(ref _staleNodeCommandCallbacks); // ignore + diagnostic counter (plan v3 §7)
-                RepublishDiagnostics("stale-ncmd");
-                return Task.CompletedTask; // stale generation: ignore
+                return Task.CompletedTask; // inconsistent transport argument — defensive ignore (not a stale event)
+            }
+
+            if (IsStaleCallback(handoff))
+            {
+                Interlocked.Increment(ref _staleNodeCommandCallbacks); // a replaced client's delayed NCMD (B2)
+                return Task.CompletedTask;
             }
 
-            // Only a valid Node Control/Rebirth = true is actioned; every other NCMD is a no-op. A host
-            // command marks the control episode pending (blocking new DATA) but does NOT mark suspect.
-            if (!SparkplugNodeCommand.IsRebirthRequest(payload))
+            // Classify every NCMD into a redacted kind: an actionable rebirth (with/without unknown extras) is
+            // actioned; every other kind is a no-op but now DISTINGUISHABLE — tallied + a sanitized diagnostic
+            // code, never a raw metric name or payload byte (slice-7 review B1).
+            var kind = SparkplugNodeCommand.Classify(payload);
+            RecordNodeCommand(kind);
+            if (!kind.IsActionableRebirth())
             {
                 return Task.CompletedTask;
             }
 
-            handoff.MarkRebirthNeeded(RebirthReason.HostCommand);
+            if (!handoff.MarkRebirthNeeded(RebirthReason.HostCommand))
+            {
+                Interlocked.Increment(ref _rebirthRequestsCoalesced); // folded into an open episode (B2)
+            }
+
             return DrainRebirthAsync(handoff);
         };
 
@@ -961,14 +1028,9 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
 
         if (!handoff.TryTakeForQueue())
         {
-            // Could not claim the queue. If the episode is still pending, a Core request is already in flight
-            // (or committing) for it → this signal coalesces into that one, no second Core request (plan v3 §7).
-            if (handoff.RebirthPending)
-            {
-                Interlocked.Increment(ref _rebirthRequestsCoalesced);
-                RepublishDiagnostics("rebirth-coalesced");
-            }
-
+            // Could not claim the queue (already queued/committing, or nothing pending). A re-drain carries NO
+            // new signal, so it is NOT coalescing — coalescing is counted at MarkRebirthNeeded, where a genuine
+            // new signal folds into an open episode (slice-7 review B2).
             return;
         }
 
@@ -986,8 +1048,7 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
             var request = RebirthRequest.Create(session.SessionId, session.Epoch, reason, detail);
             await session.Host.RequestRebirthAsync(request, CancellationToken.None).ConfigureAwait(false);
             Interlocked.Increment(ref _rebirthRequestsQueued); // exactly one Core request queued for this episode
-            Interlocked.Exchange(ref _lastRebirthRequestAtTicks, _clock().UtcTicks);
-            RepublishDiagnostics("rebirth-queued");
+            Interlocked.Exchange(ref _lastRebirthRequestAtTicks, _clock().UtcTicks); // overlaid live on read (B3)
         }
         catch
         {
@@ -1130,7 +1191,8 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         var payload = SparkplugPayloadEncoder.EncodeNData(
             SparkplugSequenceNumber.Create(_nextSeq), _clock(), samples, session.Manifest.AliasMap, isHistorical);
         var published = await SendAsync(
-            session, SparkplugTopicFactory.NData(NodeIdentity()), payload, cancellationToken).ConfigureAwait(false);
+            session, SparkplugTopicFactory.NData(NodeIdentity()), payload,
+            () => Interlocked.Increment(ref _publishFailures), cancellationToken).ConfigureAwait(false);
         if (!published)
         {
             Interlocked.Increment(ref _publishFailures); // observable/uncertain DATA send failure (plan v3 §8)
@@ -1269,7 +1331,8 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
             var payload = SparkplugPayloadEncoder.EncodeNData(
                 SparkplugSequenceNumber.Create(_nextSeq), _clock(), samples, session.Manifest.AliasMap, isHistorical: false);
             var published = await SendAsync(
-                session, SparkplugTopicFactory.NData(NodeIdentity()), payload, cancellationToken).ConfigureAwait(false);
+                session, SparkplugTopicFactory.NData(NodeIdentity()), payload,
+                () => Interlocked.Increment(ref _publishFailures), cancellationToken).ConfigureAwait(false);
             if (!published)
             {
                 Interlocked.Increment(ref _publishFailures); // final-update DATA send failure (plan v3 §8)
@@ -1420,7 +1483,7 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
             {
                 await RetireActiveSessionAsync().ConfigureAwait(false);
                 _snapshot = new ActorSnapshot(AdapterState.Stopped, SparkplugProtocolState.Stopped); // terminal
-                RepublishDiagnostics("disposed"); // reflect the terminal Stopped/Stopped + TerminalDisposed
+                PublishTransition("disposed"); // reflect the terminal Stopped/Stopped + TerminalDisposed
             }
             finally
             {
@@ -1496,7 +1559,11 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
 
         // Mark the control episode (with its cause) and drain it: coalesces with any async disconnect/NCMD
         // so only the first caller emits the Core request for this episode (slice-6 review r1 B1 → r2 R2.3).
-        session.Handoff.MarkRebirthNeeded(reason);
+        if (!session.Handoff.MarkRebirthNeeded(reason))
+        {
+            Interlocked.Increment(ref _rebirthRequestsCoalesced); // folded into an open episode (B2)
+        }
+
         await DrainRebirthAsync(session.Handoff).ConfigureAwait(false);
     }
 
@@ -1523,7 +1590,8 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
     // a local failure (false → the caller requests a rebirth, NOT a terminal fault); cancellation is
     // rethrown (still suspect) so it is never mistaken for cancellation BEFORE the send.
     private async Task<bool> SendAsync(
-        ActiveSession session, string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
+        ActiveSession session, string topic, ReadOnlyMemory<byte> payload, Action onSuspectSendFailure,
+        CancellationToken cancellationToken)
     {
         try
         {
@@ -1531,15 +1599,19 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         }
         catch (OperationCanceledException)
         {
+            // IN-TRANSPORT cancellation: the transport call was entered, so we cannot prove no bytes were
+            // queued — mark suspect AND count the observable/uncertain send failure (a PRE-send cancellation
+            // aborts at the gate and never reaches here, so it counts nothing, slice-7 review B4).
             session.Handoff.MarkSuspect();
             SetProtocolState(SparkplugProtocolState.Suspect);
+            onSuspectSendFailure();
             throw;
         }
         catch
         {
             session.Handoff.MarkSuspect();
             SetProtocolState(SparkplugProtocolState.Suspect);
-            return false;
+            return false; // normalized to a local failure; the caller counts it on the !published path
         }
     }
 
@@ -1640,7 +1712,7 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
     private void SetProtocolState(SparkplugProtocolState protocol)
     {
         _snapshot = _snapshot with { ProtocolState = protocol };
-        RepublishDiagnostics($"protocol:{protocol}");
+        PublishTransition($"protocol:{protocol}");
     }
 
     private void SetAdapterState(AdapterState target)
@@ -1653,7 +1725,7 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         }
 
         _snapshot = _snapshot with { State = target };
-        RepublishDiagnostics($"state:{target}");
+        PublishTransition($"state:{target}");
     }
 
     private void SetState(AdapterState target, SparkplugProtocolState protocolState)
@@ -1666,22 +1738,69 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         }
 
         _snapshot = new ActorSnapshot(target, protocolState);
-        RepublishDiagnostics($"state:{target}/{protocolState}");
+        PublishTransition($"state:{target}/{protocolState}");
     }
 
     // Extract a sanitized AdapterError from a caught exception for diagnostics (last-error code/category).
     // Only the structured error carries safe fields; a raw exception's Message is NOT recorded (it could
-    // echo an endpoint/credential), so a non-AdapterException yields null and only the fault state is set.
+    // echo an endpoint/credential), so a non-AdapterException yields null and SetFaulted records the fallback.
     private static AdapterError? AsAdapterError(Exception ex) => (ex as AdapterException)?.Error;
 
     private void SetFaulted(AdapterError? error = null)
     {
+        // Every fault records a last error (with a sanitized FALLBACK code for an untyped failure — an illegal
+        // transition or actor-loop exception carries no AdapterException), so a Faulted actor always exposes a
+        // last-error code + time per §8 (slice-7 review B4).
+        RecordError(error);
         if (AdapterStateTransitions.IsAllowed(_snapshot.State, AdapterState.Failed))
         {
             _snapshot = new ActorSnapshot(AdapterState.Failed, SparkplugProtocolState.Faulted);
         }
 
-        RepublishDiagnostics("faulted", error);
+        PublishTransition("faulted");
+    }
+
+    // Record a SANITIZED last error (code/category/retryable only — Message cleared so no endpoint/credential/
+    // payload can leak) and its time. A null error (untyped failure) records the stable fallback code (B4).
+    private void RecordError(AdapterError? error)
+    {
+        var source = error ?? new AdapterError
+        {
+            Code = SparkplugErrors.ActorFailure,
+            Category = ErrorCategory.Internal,
+            Message = string.Empty,
+            Retryable = false,
+        };
+        _lastError = new AdapterError
+        {
+            Code = source.Code,
+            Category = source.Category,
+            Message = string.Empty,
+            Retryable = source.Retryable,
+        };
+        Interlocked.Exchange(ref _lastErrorAtTicks, _clock().UtcTicks);
+    }
+
+    // A transport callback is STALE unless its handoff is the authoritative session's handoff OR the current
+    // in-progress establishment handoff. The concrete transport echoes its own captured generation, so a
+    // retired client's delayed callback matches its own handler by generation — identity is the authoritative
+    // staleness test, not the generation argument alone (slice-7 review B2).
+    private bool IsStaleCallback(AttemptHandoff handoff)
+    {
+        var active = _activeSession;
+        if (active is not null && ReferenceEquals(active.Handoff, handoff)) { return false; }
+        return !ReferenceEquals(_establishingHandoff, handoff);
+    }
+
+    // Record an inbound NCMD classification: tally the ignored kinds and surface a sanitized diagnostic code
+    // (never a raw metric name or payload byte). Overlaid on read — no semantic republish from the callback.
+    private void RecordNodeCommand(SparkplugNodeCommandKind kind)
+    {
+        _lastNodeCommandDiagnosticCode = kind.DiagnosticCode();
+        if (!kind.IsActionableRebirth())
+        {
+            Interlocked.Increment(ref _nodeCommandsIgnored);
+        }
     }
 
     private sealed record ActorSnapshot(AdapterState State, SparkplugProtocolState ProtocolState);
@@ -1690,30 +1809,58 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
 
     /// <summary>
-    /// The current coherent diagnostic snapshot (health/test accessor). Rebuilt on read so counters and
-    /// liveness timestamps updated on the hot path (Interlocked-only) surface freshly, without paying the
-    /// full-record rebuild on every DATA batch. A read does not count as a state transition (the
-    /// "immediately preceding transition" fields are preserved).
+    /// The current coherent diagnostic snapshot (health/test accessor). Returns the last SEMANTIC record
+    /// published at a completed transition, with the independent monotonic counters, timestamps, and
+    /// last-event codes overlaid live — so lifecycle/protocol/session are always one mutually-consistent set
+    /// (never reconstructed from a torn read) while counters/timestamps stay current. A read is NOT a
+    /// transition: <c>Version</c> does not advance (slice-7 review B3).
     /// </summary>
     internal SparkplugActorDiagnostics DiagnosticsSnapshot
     {
         get
         {
-            RepublishDiagnostics("observed");
-            return _diagnostics!;
+            var semantic = _semantic ?? BuildInitialSemantic();
+            var lastError = _lastError;
+            return semantic with
+            {
+                LastError = lastError,
+                LastErrorCode = lastError?.Code,
+                LastErrorCategory = lastError?.Category.ToString(),
+                LastRecoveryFailureCode = _lastRecoveryFailureCode,
+                LastNodeCommandDiagnosticCode = _lastNodeCommandDiagnosticCode,
+                LastSuccessfulBirthAt = TicksToOffset(Interlocked.Read(ref _lastSuccessfulBirthAtTicks)),
+                LastDataPublishAt = TicksToOffset(Interlocked.Read(ref _lastDataPublishAtTicks)),
+                LastRebirthRequestAt = TicksToOffset(Interlocked.Read(ref _lastRebirthRequestAtTicks)),
+                LastErrorAt = TicksToOffset(Interlocked.Read(ref _lastErrorAtTicks)),
+                StaleDisconnectCallbacks = Interlocked.Read(ref _staleDisconnectCallbacks),
+                StaleNodeCommandCallbacks = Interlocked.Read(ref _staleNodeCommandCallbacks),
+                NodeCommandsIgnored = Interlocked.Read(ref _nodeCommandsIgnored),
+                RebirthRequestsQueued = Interlocked.Read(ref _rebirthRequestsQueued),
+                RebirthRequestsCoalesced = Interlocked.Read(ref _rebirthRequestsCoalesced),
+                HealthyRebirths = Interlocked.Read(ref _healthyRebirths),
+                TransportRecoveryStarts = Interlocked.Read(ref _transportRecoveryStarts),
+                TransportRecoveryAttempts = Interlocked.Read(ref _transportRecoveryAttempts),
+                TransportRecoverySuccesses = Interlocked.Read(ref _transportRecoverySuccesses),
+                TransportRecoveryExhaustions = Interlocked.Read(ref _transportRecoveryExhaustions),
+                PublishFailures = Interlocked.Read(ref _publishFailures),
+                BirthFailures = Interlocked.Read(ref _birthFailures),
+                DeathPublishFailures = Interlocked.Read(ref _deathPublishFailures),
+            };
         }
     }
 
-    // Rebuild and atomically publish the coherent, versioned diagnostic snapshot (plan v3 §8). All persistent
-    // "current-state" diagnostic fields are mutated ONLY here, under _diagLock, and the record is captured from
-    // one consistent read of the volatile authority (_snapshot, _activeSession) plus the Interlocked counters —
-    // so a reader of _diagnostics never sees a torn combination, and Version strictly increases. Optional
-    // parameters carry forward when null (a counter-only republish keeps the last error / recovery progress).
-    private void RepublishDiagnostics(
-        string reasonCode,
-        AdapterError? newError = null,
-        int? currentRecoveryAttempt = null,
-        string? recoveryFailureCode = null)
+    private SparkplugActorDiagnostics BuildInitialSemantic()
+    {
+        PublishTransition("observed");
+        return _semantic!;
+    }
+
+    // Publish the coherent SEMANTIC snapshot at a completed gated transition, under _diagLock. Captures the
+    // lifecycle/protocol/session fields as ONE mutually-consistent set (from the gated, atomic _snapshot and
+    // the immutable _activeSession authority) and advances the Version change-token. Counters/timestamps/
+    // last-events are not the coherence concern — they are overlaid live on read (slice-7 review B3). Called
+    // only from GATED transitions, never from an un-gated callback (so it never reads a mid-write state).
+    private void PublishTransition(string reasonCode)
     {
         lock (_diagLock)
         {
@@ -1721,22 +1868,6 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
             var active = _activeSession;   // volatile read: the immutable authority
             var now = _clock();
 
-            if (currentRecoveryAttempt is { } attempt) { _currentRecoveryAttempt = attempt; }
-            if (recoveryFailureCode is not null) { _lastRecoveryFailureCode = recoveryFailureCode; }
-            if (newError is { } error)
-            {
-                // Store a SANITIZED copy: keep only the safe structured fields (code/category/retryable),
-                // clear the Message so no endpoint/credential/payload detail can leak through diagnostics.
-                _lastError = new AdapterError
-                {
-                    Code = error.Code,
-                    Category = error.Category,
-                    Message = string.Empty,
-                    Retryable = error.Retryable,
-                };
-                Interlocked.Exchange(ref _lastErrorAtTicks, now.UtcTicks);
-            }
-
             // Update the "immediately preceding transition" fields only on an ACTUAL state change.
             if (snapshot.State != _lastObservedState || snapshot.ProtocolState != _lastObservedProtocol)
             {
@@ -1751,7 +1882,7 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
             var handoff = active?.Handoff;
             var pending = handoff?.RebirthPending ?? false;
 
-            _diagnostics = new SparkplugActorDiagnostics
+            _semantic = new SparkplugActorDiagnostics
             {
                 Version = Interlocked.Increment(ref _diagVersion),
                 State = snapshot.State,
@@ -1774,7 +1905,9 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
                 PendingRebirthReason = pending ? handoff!.PendingReason.ToString() : null,
                 CurrentRecoveryAttempt = _currentRecoveryAttempt,
                 RecoveryAttemptBudget = _config is null ? 0 : Math.Max(1, _config.TransportRecoveryMaxAttempts),
+                // A stable baseline for the last-event/counter fields — overlaid live on read.
                 LastRecoveryFailureCode = _lastRecoveryFailureCode,
+                LastNodeCommandDiagnosticCode = _lastNodeCommandDiagnosticCode,
                 LastSuccessfulBirthAt = TicksToOffset(Interlocked.Read(ref _lastSuccessfulBirthAtTicks)),
                 LastDataPublishAt = TicksToOffset(Interlocked.Read(ref _lastDataPublishAtTicks)),
                 LastRebirthRequestAt = TicksToOffset(Interlocked.Read(ref _lastRebirthRequestAtTicks)),
@@ -1784,6 +1917,7 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
                 LastError = _lastError,
                 StaleDisconnectCallbacks = Interlocked.Read(ref _staleDisconnectCallbacks),
                 StaleNodeCommandCallbacks = Interlocked.Read(ref _staleNodeCommandCallbacks),
+                NodeCommandsIgnored = Interlocked.Read(ref _nodeCommandsIgnored),
                 RebirthRequestsQueued = Interlocked.Read(ref _rebirthRequestsQueued),
                 RebirthRequestsCoalesced = Interlocked.Read(ref _rebirthRequestsCoalesced),
                 HealthyRebirths = Interlocked.Read(ref _healthyRebirths),
@@ -1951,7 +2085,12 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         /// overwrite the accepted cause — first cause wins; transport suspicion is applied at read time via
         /// <see cref="PendingReason"/> (slice-6 review r3 R3.3).
         /// </summary>
-        public void MarkRebirthNeeded(RebirthReason reason)
+        /// <returns>
+        /// <c>true</c> if this opened a NEW episode; <c>false</c> if the signal folded into an already-open
+        /// episode (a genuine coalesce). The caller counts coalescing here — never on a mere re-drain, which
+        /// carries no new signal (slice-7 review B2).
+        /// </returns>
+        public bool MarkRebirthNeeded(RebirthReason reason)
         {
             lock (_sync)
             {
@@ -1960,7 +2099,10 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
                     _pending = true;
                     _queued = false;
                     _reason = reason; // fresh episode installs its cause
+                    return true;      // opened a new episode
                 }
+
+                return false;         // a new signal folded into the open episode → coalesced
             }
         }
 
diff --git a/src/ElpisEdgeConnect.Sinks.SparkplugB/SparkplugErrors.cs b/src/ElpisEdgeConnect.Sinks.SparkplugB/SparkplugErrors.cs
index 39d7c6a..531c159 100644
--- a/src/ElpisEdgeConnect.Sinks.SparkplugB/SparkplugErrors.cs
+++ b/src/ElpisEdgeConnect.Sinks.SparkplugB/SparkplugErrors.cs
@@ -111,6 +111,14 @@ public static class SparkplugErrors
     /// <summary>The transport dropped (or CONNECT failed) during initial Begin, before an authoritative birth.</summary>
     public const string SessionSuspectDuringBegin = "SPARKPLUG.SESSION_SUSPECT_DURING_BEGIN";
 
+    /// <summary>
+    /// A sanitized FALLBACK code for an untyped actor failure — an illegal lifecycle transition or an
+    /// unexpected actor-loop exception that carried no structured <c>AdapterError</c>. Ensures a Faulted
+    /// actor always exposes a last-error code + time (plan v3 §8; slice-7 review B4). Carries no message,
+    /// exception type, or customer data.
+    /// </summary>
+    public const string ActorFailure = "SPARKPLUG.ACTOR_FAILURE";
+
     // ==== Transport (K3 slice 4) ====
 
     /// <summary>CONNECT did not return a success CONNACK.</summary>
diff --git a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugNodeCommandTests.cs b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugNodeCommandTests.cs
index 1ba64d4..a9b735f 100644
--- a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugNodeCommandTests.cs
+++ b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugNodeCommandTests.cs
@@ -1,8 +1,11 @@
 // ============================================================================
 // File: Session/SparkplugNodeCommandTests.cs
-// Purpose: Locks the fail-safe NCMD classifier (plan v3 §1.6): ONLY a well-formed
-//          Node Control/Rebirth = true payload is a rebirth request; every other
-//          case (false, wrong datatype, wrong name, empty, malformed) is a no-op.
+// Purpose: Locks the fail-safe NCMD classifier (plan v3 §1.6, slice-7 review B1):
+//          a well-formed Node Control/Rebirth = true is RebirthRequested (or
+//          RebirthRequestedWithUnknownExtras when other metrics are present); every
+//          other case classifies to a distinct, redacted Ignored* kind (false,
+//          null, wrong-type, missing/unknown-only, malformed) — a no-op that is now
+//          DISTINGUISHABLE for diagnostics, never throwing.
 // ============================================================================
 
 using ElpisEdgeConnect.Sinks.SparkplugB.Payloads;
@@ -28,68 +31,90 @@ public sealed class SparkplugNodeCommandTests
     }
 
     [Fact]
-    public void IsRebirthRequest_RebirthTrue_ReturnsTrue()
+    public void Classify_RebirthTrue_IsRebirthRequested()
     {
         var bytes = Encode(WithMetric(new Payload.Types.Metric { Name = Rebirth, BooleanValue = true }));
 
-        SparkplugNodeCommand.IsRebirthRequest(bytes).Should().BeTrue();
+        var kind = SparkplugNodeCommand.Classify(bytes);
+
+        kind.Should().Be(SparkplugNodeCommandKind.RebirthRequested);
+        kind.IsActionableRebirth().Should().BeTrue();
+        kind.DiagnosticCode().Should().Be("rebirth");
     }
 
     [Fact]
-    public void IsRebirthRequest_RebirthFalse_ReturnsFalse()
+    public void Classify_RebirthTruePlusUnknownExtras_IsRebirthRequestedWithUnknownExtras()
+    {
+        var payload = new Payload();
+        payload.Metrics.Add(new Payload.Types.Metric { Name = Rebirth, BooleanValue = true });
+        payload.Metrics.Add(new Payload.Types.Metric { Name = "Some/Other", IntValue = 7 });
+
+        var kind = SparkplugNodeCommand.Classify(Encode(payload));
+
+        kind.Should().Be(SparkplugNodeCommandKind.RebirthRequestedWithUnknownExtras);
+        kind.IsActionableRebirth().Should().BeTrue(); // still actionable: rebirth once, extras diagnosed
+        kind.DiagnosticCode().Should().Be("rebirth+unknown-extras");
+    }
+
+    [Fact]
+    public void Classify_RebirthFalse_IsIgnoredFalse()
     {
         var bytes = Encode(WithMetric(new Payload.Types.Metric { Name = Rebirth, BooleanValue = false }));
 
-        SparkplugNodeCommand.IsRebirthRequest(bytes).Should().BeFalse();
+        var kind = SparkplugNodeCommand.Classify(bytes);
+
+        kind.Should().Be(SparkplugNodeCommandKind.IgnoredFalse);
+        kind.IsActionableRebirth().Should().BeFalse();
+        kind.DiagnosticCode().Should().Be("ignored:false");
     }
 
     [Fact]
-    public void IsRebirthRequest_RebirthTrueButExplicitlyNull_ReturnsFalse()
+    public void Classify_RebirthExplicitlyNull_IsIgnoredNull()
     {
-        // A Node Control/Rebirth metric marked IsNull carries no command, even with BooleanValue=true.
         var bytes = Encode(WithMetric(new Payload.Types.Metric { Name = Rebirth, BooleanValue = true, IsNull = true }));
 
-        SparkplugNodeCommand.IsRebirthRequest(bytes).Should().BeFalse();
+        SparkplugNodeCommand.Classify(bytes).Should().Be(SparkplugNodeCommandKind.IgnoredNull);
     }
 
     [Fact]
-    public void IsRebirthRequest_RebirthWrongValueArm_ReturnsFalse()
+    public void Classify_RebirthWrongValueArm_IsIgnoredWrongType()
     {
-        // The protobuf oneof value arm is authoritative: a "Node Control/Rebirth" whose value is set on
-        // the Int arm (not the Boolean arm) is not a valid rebirth command, regardless of any Datatype field.
+        // The protobuf oneof value arm is authoritative: a "Node Control/Rebirth" whose value is on the Int
+        // arm (not the Boolean arm) is not a valid rebirth command, regardless of any Datatype field.
         var bytes = Encode(WithMetric(new Payload.Types.Metric { Name = Rebirth, IntValue = 1 }));
 
-        SparkplugNodeCommand.IsRebirthRequest(bytes).Should().BeFalse();
+        SparkplugNodeCommand.Classify(bytes).Should().Be(SparkplugNodeCommandKind.IgnoredWrongType);
     }
 
     [Fact]
-    public void IsRebirthRequest_DifferentMetricName_ReturnsFalse()
+    public void Classify_UnknownOnlyCommand_IsIgnoredMissing()
     {
         var bytes = Encode(WithMetric(new Payload.Types.Metric { Name = "Node Control/Reboot", BooleanValue = true }));
 
-        SparkplugNodeCommand.IsRebirthRequest(bytes).Should().BeFalse();
+        SparkplugNodeCommand.Classify(bytes).Should().Be(SparkplugNodeCommandKind.IgnoredMissing);
     }
 
     [Fact]
-    public void IsRebirthRequest_MultipleMetrics_OneRebirthTrue_ReturnsTrue()
+    public void Classify_EmptyPayload_IsIgnoredMissing()
     {
-        var payload = new Payload();
-        payload.Metrics.Add(new Payload.Types.Metric { Name = "Some/Other", IntValue = 7 });
-        payload.Metrics.Add(new Payload.Types.Metric { Name = Rebirth, BooleanValue = true });
-
-        SparkplugNodeCommand.IsRebirthRequest(Encode(payload)).Should().BeTrue();
+        SparkplugNodeCommand.Classify(Encode(new Payload())).Should().Be(SparkplugNodeCommandKind.IgnoredMissing);
     }
 
     [Fact]
-    public void IsRebirthRequest_EmptyPayload_ReturnsFalse()
+    public void Classify_MalformedBytes_IsIgnoredMalformed()
     {
-        SparkplugNodeCommand.IsRebirthRequest(Encode(new Payload())).Should().BeFalse();
+        // Random bytes that are not a valid protobuf Payload must classify as malformed, never throw.
+        SparkplugNodeCommand.Classify(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F })
+            .Should().Be(SparkplugNodeCommandKind.IgnoredMalformed);
     }
 
     [Fact]
-    public void IsRebirthRequest_MalformedBytes_ReturnsFalse()
+    public void DiagnosticCode_IsSecretFree_ForEveryKind()
     {
-        // Random bytes that are not a valid protobuf Payload must be ignored, never throw.
-        SparkplugNodeCommand.IsRebirthRequest(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F }).Should().BeFalse();
+        // No classification code carries a raw metric name or payload byte — only stable, redacted labels.
+        foreach (SparkplugNodeCommandKind kind in System.Enum.GetValues(typeof(SparkplugNodeCommandKind)))
+        {
+            kind.DiagnosticCode().Should().MatchRegex("^[a-z:+-]+$");
+        }
     }
 }
diff --git a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorRebirthTests.cs b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorRebirthTests.cs
index 39a2656..c410d4a 100644
--- a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorRebirthTests.cs
+++ b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorRebirthTests.cs
@@ -992,6 +992,7 @@ public sealed class SparkplugSessionActorRebirthTests : IDisposable
         var callsAtBackoff = factoryCalls;                     // 2
         var genAtBackoff = actor.LastIssuedGeneration;         // one generation per attempt so far
         var reservesAtBackoff = store.ReserveCalls;            // one bdSeq per attempt so far
+        var attemptsAtBackoff = actor.DiagnosticsSnapshot.TransportRecoveryAttempts; // one admitted attempt so far
 
         // Drive the exact ordering: disposal wins ownership, its retirement is held on a transport-dispose
         // barrier, THEN the recovery backoff is released — recovery must abort, never begin another attempt.
@@ -1006,6 +1007,7 @@ public sealed class SparkplugSessionActorRebirthTests : IDisposable
         factoryCalls.Should().Be(callsAtBackoff);              // no next transport created
         actor.LastIssuedGeneration.Should().Be(genAtBackoff);  // no next generation issued
         store.ReserveCalls.Should().Be(reservesAtBackoff);     // no additional bdSeq reserved
+        actor.DiagnosticsSnapshot.TransportRecoveryAttempts.Should().Be(attemptsAtBackoff); // rejected admission = no attempt (B4)
         actor.State.Should().Be(AdapterState.Stopped);         // disposal terminal
         actor.ProtocolState.Should().Be(SparkplugProtocolState.Stopped);
         actor.HasSession.Should().BeFalse();                   // no candidate authority promoted
@@ -1365,26 +1367,35 @@ public sealed class SparkplugSessionActorRebirthTests : IDisposable
     }
 
     [Fact]
-    public async Task Diagnostics_StaleDisconnectCallback_IncrementsCounter()
+    public async Task Diagnostics_StaleCallbacks_FromReplacedClient_Counted_LiveUnaffected()
     {
-        var (actor, fake, _) = await Born();
+        // Birth client A, replace it with client B via a suspect recovery, then deliver A's DELAYED disconnect
+        // and NCMD carrying A's OWN REAL generation (the concrete transport echoes it). Both must count as
+        // stale by handoff identity, and the live session B must be untouched (slice-7 review B2).
+        var fake0 = new FakeTransport();
+        var fake1 = new FakeTransport();
+        var call = 0;
+        var actor = new SparkplugSessionActor(
+            "spb-1", NewStore(), () => call++ == 0 ? (ISparkplugMqttTransport)fake0 : fake1, () => Clock, InstantDelay);
+        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
+        await actor.StartAsync(CancellationToken.None);
+        await actor.BeginReplaySessionAsync(Start(new CapturingHost()), CancellationToken.None); // A = fake0
+        var aGeneration = actor.CurrentGeneration;
+        await fake0.RaiseDisconnected(aGeneration);                             // legit suspect (A authoritative)
+        await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);    // recover → B = fake1 authoritative
+        var epochAfter = actor.CurrentEpoch;
 
-        await fake.RaiseDisconnected(actor.CurrentGeneration + 99); // a retired client's delayed callback
+        await fake0.RaiseDisconnected(aGeneration);                             // A's delayed disconnect, real gen
+        await fake0.RaiseNodeCommand(aGeneration, RebirthCommand());            // A's delayed NCMD, real gen
 
-        actor.DiagnosticsSnapshot.StaleDisconnectCallbacks.Should().Be(1);
+        var diag = actor.DiagnosticsSnapshot;
+        diag.StaleDisconnectCallbacks.Should().Be(1);   // ONLY the post-replacement one (the first was authoritative)
+        diag.StaleNodeCommandCallbacks.Should().Be(1);
+        actor.CurrentEpoch.Should().Be(epochAfter);     // B is unaffected
+        actor.CurrentSessionSuspect.Should().BeFalse();  // the live session B was not marked suspect
         (await actor.CheckHealthAsync(CancellationToken.None)).Metrics!["staleDisconnectCallbacks"].Should().Be(1L);
     }
 
-    [Fact]
-    public async Task Diagnostics_StaleNodeCommandCallback_IncrementsCounter()
-    {
-        var (actor, fake, _) = await Born();
-
-        await fake.RaiseNodeCommand(actor.CurrentGeneration + 99, RebirthCommand());
-
-        actor.DiagnosticsSnapshot.StaleNodeCommandCallbacks.Should().Be(1);
-    }
-
     [Fact]
     public async Task Diagnostics_RebirthRequest_QueuedThenCoalesced()
     {
@@ -1507,6 +1518,91 @@ public sealed class SparkplugSessionActorRebirthTests : IDisposable
         rendered.Should().NotContain("broker.internal.example");
     }
 
+    // ==== Slice 7 r1: NCMD classification, coalescing, failure diagnostics ====
+
+    [Fact]
+    public async Task NodeCommand_RebirthWithUnknownExtras_RequestsOnce_DiagnosesExtras()
+    {
+        var (actor, fake, host) = await Born();
+
+        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthWithUnknownExtrasCommand());
+
+        host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.HostCommand); // rebirth once
+        actor.DiagnosticsSnapshot.LastNodeCommandDiagnosticCode.Should().Be("rebirth+unknown-extras"); // extras diagnosed
+        actor.DiagnosticsSnapshot.NodeCommandsIgnored.Should().Be(0); // an actionable rebirth is not "ignored"
+    }
+
+    [Theory]
+    [InlineData("false", "ignored:false")]
+    [InlineData("null", "ignored:null")]
+    [InlineData("wrong-type", "ignored:wrong-type")]
+    [InlineData("missing", "ignored:missing")]
+    public async Task NodeCommand_IgnoredKind_TallyAndDiagnostic_NoRequest(string kind, string code)
+    {
+        var (actor, fake, host) = await Born();
+
+        await fake.RaiseNodeCommand(actor.CurrentGeneration, IgnoredNodeCommand(kind));
+
+        host.Requests.Should().BeEmpty();                                     // never a side effect
+        actor.DiagnosticsSnapshot.NodeCommandsIgnored.Should().Be(1);         // tallied
+        actor.DiagnosticsSnapshot.LastNodeCommandDiagnosticCode.Should().Be(code); // distinguishable + redacted
+    }
+
+    [Fact]
+    public async Task Diagnostics_RepeatedCoalescingNodeCommands_DoNotInflateBeyondFolds()
+    {
+        var (actor, fake, host) = await Born();
+
+        await fake.RaiseDisconnected(actor.CurrentGeneration);                  // opens the episode + queues one
+        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // folds (coalesced #1)
+        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // folds (coalesced #2)
+
+        var diag = actor.DiagnosticsSnapshot;
+        diag.RebirthRequestsQueued.Should().Be(1);      // exactly one Core request for the episode
+        diag.RebirthRequestsCoalesced.Should().Be(2);   // only the two genuine new signals that folded
+    }
+
+    [Fact]
+    public async Task Diagnostics_UntypedFailure_RecordsSanitizedFallbackErrorCodeAndTime()
+    {
+        var fake = new FakeTransport();
+        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fake, () => Clock, InstantDelay);
+        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
+        // NOT started → Begin hits RequireReadyForSession, an UNTYPED InvalidOperationException.
+
+        await actor.Invoking(a => a.BeginReplaySessionAsync(Start(new CapturingHost()), CancellationToken.None))
+            .Should().ThrowAsync<InvalidOperationException>();
+
+        var diag = actor.DiagnosticsSnapshot;
+        diag.LastErrorCode.Should().Be(SparkplugErrors.ActorFailure); // sanitized fallback (no exception message/type)
+        diag.LastErrorAt.Should().Be(Clock);
+        diag.LastError!.Message.Should().BeEmpty();
+    }
+
+    [Fact]
+    public async Task Diagnostics_InTransportNBirthCancellation_IncrementsBirthFailures()
+    {
+        var (actor, fake, _) = await Born();
+        using var cts = new CancellationTokenSource();
+        fake.OnPublishOnce = () => { cts.Cancel(); cts.Token.ThrowIfCancellationRequested(); return Task.CompletedTask; };
+
+        await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), cts.Token)).Should().ThrowAsync<OperationCanceledException>();
+
+        actor.DiagnosticsSnapshot.BirthFailures.Should().Be(1); // in-transport NBIRTH cancel = uncertain send (B4)
+    }
+
+    [Fact]
+    public async Task Diagnostics_PreSendNBirthCancellation_CountsNoBirthFailure()
+    {
+        var (actor, _, _) = await Born();
+        using var cts = new CancellationTokenSource();
+        await cts.CancelAsync();
+
+        await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), cts.Token)).Should().ThrowAsync<OperationCanceledException>();
+
+        actor.DiagnosticsSnapshot.BirthFailures.Should().Be(0); // aborted at the gate, never entered the transport
+    }
+
     // ==== Helpers ====
 
     private static Func<TimeSpan, CancellationToken, Task> Recording(List<TimeSpan> sink) =>
@@ -1685,6 +1781,32 @@ public sealed class SparkplugSessionActorRebirthTests : IDisposable
         return payload.ToByteArray();
     }
 
+    private static byte[] RebirthWithUnknownExtrasCommand()
+    {
+        var payload = new Payload();
+        payload.Metrics.Add(new Payload.Types.Metric
+        {
+            Name = SparkplugPayloadEncoder.NodeControlRebirthMetricName,
+            BooleanValue = true,
+        });
+        payload.Metrics.Add(new Payload.Types.Metric { Name = "Some/Other", IntValue = 7 });
+        return payload.ToByteArray();
+    }
+
+    private static byte[] IgnoredNodeCommand(string kind)
+    {
+        var payload = new Payload();
+        var name = SparkplugPayloadEncoder.NodeControlRebirthMetricName;
+        payload.Metrics.Add(kind switch
+        {
+            "false" => new Payload.Types.Metric { Name = name, BooleanValue = false },
+            "null" => new Payload.Types.Metric { Name = name, BooleanValue = true, IsNull = true },
+            "wrong-type" => new Payload.Types.Metric { Name = name, IntValue = 1 },
+            _ => new Payload.Types.Metric { Name = "Some/Other", IntValue = 1 }, // missing/unknown-only
+        });
+        return payload.ToByteArray();
+    }
+
     private static byte[] NonRebirthCommand()
     {
         var payload = new Payload();
diff --git a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorReplayTests.cs b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorReplayTests.cs
index a7e78af..e7e7239 100644
--- a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorReplayTests.cs
+++ b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorReplayTests.cs
@@ -136,6 +136,32 @@ public sealed class SparkplugSessionActorReplayTests : IDisposable
         actor.DiagnosticsSnapshot.PublishFailures.Should().Be(1); // slice 7: the DATA send failure is counted
     }
 
+    [Fact]
+    public async Task Diagnostics_InTransportDataCancellation_IncrementsPublishFailures()
+    {
+        var (actor, fake, _) = await BornActorWithHost();
+        using var cts = new CancellationTokenSource();
+        fake.FailPublish = ct => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; };
+
+        await actor.Invoking(a => a.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), cts.Token))
+            .Should().ThrowAsync<OperationCanceledException>();
+
+        actor.DiagnosticsSnapshot.PublishFailures.Should().Be(1); // in-transport DATA cancel = uncertain send (B4)
+    }
+
+    [Fact]
+    public async Task Diagnostics_PreSendDataCancellation_CountsNoPublishFailure()
+    {
+        var (actor, _, _) = await BornActorWithHost();
+        using var cts = new CancellationTokenSource();
+        await cts.CancelAsync();
+
+        await actor.Invoking(a => a.PublishAsync(new[] { Point("srcA", 2) }, Ctx(ReplayPhase.Replay), cts.Token))
+            .Should().ThrowAsync<OperationCanceledException>();
+
+        actor.DiagnosticsSnapshot.PublishFailures.Should().Be(0); // aborted at the gate, never entered the transport
+    }
+
     [Fact]
     public async Task Publish_WhenRebirthPendingFromNodeCommand_AcceptsNothing_NoSeq_NoPublish()
     {
```
