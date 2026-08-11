# K3 Slice 7 — Exact Source Diff r2 (live control overlay, real attempt boundary, NCMD duplicate hardening)

**Commit:** `4ba868a` — *fix(sparkplug): K3 slice 7 review r2*
**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master)

Full `git show` (4 files, 0 elision) for line-level sign-off.

```diff
commit 4ba868a3e20f761f6f1c3feac0a9da25eaf374e5
Author: Sudhakar <sudhakar@elpisitsolutions.com>
Date:   Fri Jul 24 23:54:30 2026 +0530

    fix(sparkplug): K3 slice 7 review r2 - live control overlay, real attempt boundary, NCMD duplicate hardening
    
    Folds the two r1 re-review blockers. No Core change.
    
    R2.1 — live operational-control overlay. The semantic root (lifecycle/protocol/
    session) still publishes only at gated transitions, but DiagnosticsSnapshot now
    overlays the operational-control state — suspect / pending-rebirth / reason /
    terminal-disposed — read LIVE from the authoritative handoff (bound by connection
    generation so it only applies when the handoff still matches the semantic
    authority) and from DisposalWon. So an ASYNCHRONOUS disconnect/NCMD that latches
    suspect or pending, or a disposal that has won while retirement is still blocked,
    is visible immediately. Health is reordered: Failed→Unhealthy; Running +
    (disposal | suspect | pending | transitional protocol)→Degraded; Running +
    no-session + Stopped→Healthy; Running + session + Live→Healthy. A Live session
    with a pending NCMD rebirth (DATA blocked) is now correctly Degraded.
    
    R2.2 — recovery attempt counted at the real attempt boundary. transportRecovery-
    Attempts and currentRecoveryAttempt are now incremented/published INSIDE
    AttemptConnectionAsync after the bdSeq reservation, request build and client
    creation, immediately before CONNECT. A fatal preparation (generation exhaustion,
    store/bdSeq failure, factory failure) or a disposal-rejected admission therefore
    records NO attempt; a CONNECT/SUBSCRIBE/NBIRTH failure counts one. LastRecovery-
    FailureCode is now recorded on each failed retryable attempt before backoff, so
    the failure that caused the delay is visible during it.
    
    Focused hardening: duplicate Node Control/Rebirth metrics classify as
    IgnoredAmbiguous (order-independent) rather than actioning the first.
    
    575 SparkplugB tests; regressions green: Core 1250, Host 225, Management 1149
    (full project). Solution 0 errors; SparkplugB 0 warnings under warnings-as-errors.
    
    Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>

diff --git a/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugNodeCommand.cs b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugNodeCommand.cs
index c1f948d..5a169a4 100644
--- a/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugNodeCommand.cs
+++ b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugNodeCommand.cs
@@ -42,6 +42,12 @@ internal enum SparkplugNodeCommandKind
 
     /// <summary>The Rebirth metric was present, boolean, but false.</summary>
     IgnoredFalse,
+
+    /// <summary>
+    /// More than one <c>Node Control/Rebirth</c> metric was present — an ambiguous command whose meaning would
+    /// depend on metric ordering. Fail-safe: ignored regardless of order (slice-7 review r2, focused hardening).
+    /// </summary>
+    IgnoredAmbiguous,
 }
 
 /// <summary>Classifies an inbound NCMD payload (rebirth-command detection with redacted diagnostics).</summary>
@@ -67,12 +73,14 @@ internal static class SparkplugNodeCommand
         }
 
         Payload.Types.Metric? rebirth = null;
+        var rebirthCount = 0;
         var hasOtherMetrics = false;
         foreach (var metric in parsed.Metrics)
         {
             if (string.Equals(metric.Name, SparkplugPayloadEncoder.NodeControlRebirthMetricName, StringComparison.Ordinal))
             {
                 rebirth ??= metric;
+                rebirthCount++;
             }
             else
             {
@@ -80,6 +88,12 @@ internal static class SparkplugNodeCommand
             }
         }
 
+        if (rebirthCount > 1)
+        {
+            // Ambiguous: multiple Rebirth metrics — do NOT action one representation (order-dependence).
+            return SparkplugNodeCommandKind.IgnoredAmbiguous;
+        }
+
         if (rebirth is null)
         {
             return SparkplugNodeCommandKind.IgnoredMissing; // no rebirth metric (includes unknown-only commands)
@@ -119,6 +133,7 @@ internal static class SparkplugNodeCommand
         SparkplugNodeCommandKind.IgnoredNull => "ignored:null",
         SparkplugNodeCommandKind.IgnoredWrongType => "ignored:wrong-type",
         SparkplugNodeCommandKind.IgnoredFalse => "ignored:false",
+        SparkplugNodeCommandKind.IgnoredAmbiguous => "ignored:ambiguous",
         _ => "ignored:unknown",
     };
 }
diff --git a/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs
index a9882c7..34da092 100644
--- a/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs
+++ b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs
@@ -351,15 +351,17 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         // existed.
         var diag = DiagnosticsSnapshot;
 
-        // Health uses BOTH the protocol substate AND HasSession (slice-7 review B3): Healthy is ONLY
-        // ready-no-session (Running + Stopped + no session) or active-Live (Running + Live + session). Every
-        // other Running establishment/recovery/transitional/suspect state is Degraded.
+        // Health uses the lifecycle state, HasSession, AND the live operational-control overlay (slice-7 review
+        // r2 R2.1). Any active control condition — disposal in progress, a suspect transport, or a pending
+        // rebirth (e.g. a valid NCMD that latched the control episode and is blocking DATA) — is Degraded, even
+        // over a Live/Stopped protocol. Healthy is ONLY ready-no-session or active-Live with no such condition.
+        var degradedControl = diag.TerminalDisposed || diag.SuspectTransport || diag.PendingRebirth;
         var level = diag.State switch
         {
             AdapterState.Failed => HealthLevel.Unhealthy,
-            AdapterState.Running when diag.ProtocolState is SparkplugProtocolState.Stopped && !diag.HasSession
+            AdapterState.Running when !degradedControl && diag.ProtocolState is SparkplugProtocolState.Stopped && !diag.HasSession
                 => HealthLevel.Healthy,
-            AdapterState.Running when diag.ProtocolState is SparkplugProtocolState.Live && diag.HasSession
+            AdapterState.Running when !degradedControl && diag.ProtocolState is SparkplugProtocolState.Live && diag.HasSession
                 => HealthLevel.Healthy,
             AdapterState.Running => HealthLevel.Degraded,
             AdapterState.Degraded => HealthLevel.Degraded,
@@ -481,7 +483,8 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         LatestValueSnapshot snapshot, CancellationToken cancellationToken)
     {
         var prepared = PrepareBirth(snapshot);
-        return await AttemptConnectionAsync(prepared, sessionId, epoch, routeId, host, recoveryToken: null, cancellationToken)
+        return await AttemptConnectionAsync(
+            prepared, sessionId, epoch, routeId, host, recoveryToken: null, recoveryAttemptOrdinal: 0, cancellationToken)
             .ConfigureAwait(false);
     }
 
@@ -512,7 +515,7 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
     // TRANSPORT failure of this method is retryable (see IsRetryableEstablishmentFailure).
     private async Task<ActiveSession> AttemptConnectionAsync(
         PreparedBirth prepared, ReplaySessionId sessionId, ReplayEpochId epoch, string routeId, IReplaySessionHost host,
-        object? recoveryToken, CancellationToken cancellationToken)
+        object? recoveryToken, int recoveryAttemptOrdinal, CancellationToken cancellationToken)
     {
         var config = _config!;
         var node = prepared.Node;
@@ -532,9 +535,6 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         else
         {
             ValidateRecoveryOwnership(recoveryToken);
-            // Count the lifetime tally only AFTER admission passed — a disposal/supersession that rejects
-            // admission must NOT record a "complete establishment attempt" (slice-7 review B4).
-            Interlocked.Increment(ref _transportRecoveryAttempts);
         }
 
         // Generation exhaustion is checked BEFORE reserving a durable bdSeq (carry-forward 2 / B1), so the
@@ -567,6 +567,18 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         var nodeCommandHandler = MakeNodeCommandHandler(generation, handoff);
         attempt.Disconnected += disconnectHandler;
         attempt.NodeCommandReceived += nodeCommandHandler;
+
+        // The REAL recovery-attempt boundary (slice-7 review r2 R2.2): count the lifetime tally and publish the
+        // ordinal only AFTER a bdSeq was reserved, the request built, and the client created — right before
+        // CONNECT. A store/generation/factory failure (fatal preparation) or a disposal-rejected admission
+        // therefore records NO "complete CONNECT/SUBSCRIBE/NBIRTH attempt"; a CONNECT failure counts as one.
+        if (recoveryToken is not null)
+        {
+            Interlocked.Increment(ref _transportRecoveryAttempts);
+            _currentRecoveryAttempt = recoveryAttemptOrdinal;
+            PublishTransition("recovery-attempt");
+        }
+
         try
         {
             SetProtocolState(SparkplugProtocolState.Connecting);
@@ -857,15 +869,14 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
             {
                 ValidateRecoveryOwnership(token); // and before EACH attempt — no CONNECT/bdSeq/generation after loss
 
-                // Surface the current ordinal within this episode (the lifetime attempts tally is incremented
-                // inside AttemptConnectionAsync, AFTER admission, so a rejected admission records no attempt, B4).
-                _currentRecoveryAttempt = attempt;
-                PublishTransition("recovery-attempt");
-
+                // The lifetime attempts tally + the current ordinal are published INSIDE AttemptConnectionAsync
+                // at the real attempt boundary (after bdSeq/request/client, before CONNECT), so a fatal
+                // preparation (store/generation/factory) or a disposal-rejected admission records no attempt
+                // and advertises no ordinal (slice-7 review r2 R2.2).
                 try
                 {
                     var candidate = await AttemptConnectionAsync(
-                        prepared, sessionId, epoch, previous.RouteId, previous.Host, token, cancellationToken).ConfigureAwait(false);
+                        prepared, sessionId, epoch, previous.RouteId, previous.Host, token, attempt, cancellationToken).ConfigureAwait(false);
                     await PromoteAndDrainAsync(candidate).ConfigureAwait(false);
                     Interlocked.Increment(ref _transportRecoverySuccesses);
                     _currentRecoveryAttempt = 0;
@@ -875,10 +886,11 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
                 catch (Exception ex)
                     when (ex is not OperationCanceledException && attempt < maxAttempts && IsRetryableEstablishmentFailure(ex))
                 {
-                    // A retryable transport failure consumed this attempt's distinct generation + bdSeq;
-                    // back off (gate released) and retry. Reflect the honest substate during the delay window
-                    // (the failed attempt left it at Connecting/Subscribing/Birthing). A superseding lifecycle
-                    // call during the delay throws (aborts) here; a non-retryable/last-attempt failure faults.
+                    // A retryable transport failure consumed this attempt's distinct generation + bdSeq; record
+                    // its code (visible during the backoff, r2 R2.2), then back off (gate released) and retry.
+                    // Reflect the honest RecoveringTransport substate during the delay window. A superseding
+                    // lifecycle call during the delay throws (aborts) here; a non-retryable/last-attempt faults.
+                    _lastRecoveryFailureCode = AsAdapterError(ex)?.Code;
                     SetProtocolState(SparkplugProtocolState.RecoveringTransport);
                     await BackoffWithGateReleasedAsync(TimeSpan.FromMilliseconds(delayMs), token, cancellationToken).ConfigureAwait(false);
                     delayMs = (int)Math.Min((long)delayMs * 2, maxDelayMs);
@@ -1810,10 +1822,13 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
 
     /// <summary>
     /// The current coherent diagnostic snapshot (health/test accessor). Returns the last SEMANTIC record
-    /// published at a completed transition, with the independent monotonic counters, timestamps, and
-    /// last-event codes overlaid live — so lifecycle/protocol/session are always one mutually-consistent set
-    /// (never reconstructed from a torn read) while counters/timestamps stay current. A read is NOT a
-    /// transition: <c>Version</c> does not advance (slice-7 review B3).
+    /// (lifecycle/protocol/session) published at a completed gated transition, with two overlays applied live:
+    /// the independent monotonic counters/timestamps/last-event codes, AND the operational-control state
+    /// (suspect / pending-rebirth / reason / terminal-disposed). The control overlay is read from the
+    /// authoritative handoff — bound by connection generation so it is only applied when the handoff still
+    /// matches the semantic authority — so an ASYNCHRONOUS disconnect/NCMD that latches suspect or pending is
+    /// visible immediately, without reconstructing lifecycle/session from a torn read (slice-7 review r2 R2.1).
+    /// A read is NOT a transition: <c>Version</c> does not advance.
     /// </summary>
     internal SparkplugActorDiagnostics DiagnosticsSnapshot
     {
@@ -1821,8 +1836,26 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         {
             var semantic = _semantic ?? BuildInitialSemantic();
             var lastError = _lastError;
+
+            // Live control overlay bound to the semantic authority. Only overlay the handoff when it belongs to
+            // the SAME connection generation as the published semantic root (else a promotion-window swap could
+            // pair a new handoff with an old lifecycle root); otherwise keep the root's coherent baked values.
+            var active = _activeSession;
+            var bound = active is not null && semantic.HasSession
+                && active.TransportGeneration == semantic.ConnectionGeneration;
+            var handoff = bound ? active!.Handoff : null;
+            var suspect = handoff?.SuspectAfterPromotion ?? semantic.SuspectTransport;
+            var pending = handoff?.RebirthPending ?? semantic.PendingRebirth;
+            var pendingReason = pending
+                ? (handoff is not null ? handoff.PendingReason.ToString() : semantic.PendingRebirthReason)
+                : null;
+
             return semantic with
             {
+                TerminalDisposed = DisposalWon,           // live: true the instant disposal wins (before retirement)
+                SuspectTransport = suspect,
+                PendingRebirth = pending,
+                PendingRebirthReason = pendingReason,
                 LastError = lastError,
                 LastErrorCode = lastError?.Code,
                 LastErrorCategory = lastError?.Category.ToString(),
diff --git a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugNodeCommandTests.cs b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugNodeCommandTests.cs
index a9b735f..adb5c39 100644
--- a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugNodeCommandTests.cs
+++ b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugNodeCommandTests.cs
@@ -108,6 +108,27 @@ public sealed class SparkplugNodeCommandTests
             .Should().Be(SparkplugNodeCommandKind.IgnoredMalformed);
     }
 
+    [Fact]
+    public void Classify_DuplicateRebirthMetrics_IsIgnoredAmbiguous_OrderIndependent()
+    {
+        // Two Node Control/Rebirth metrics with conflicting value arms — the meaning would depend on which
+        // one "wins" by ordering, so it must be ignored regardless of order (fail-safe, review r2).
+        var payload = new Payload();
+        payload.Metrics.Add(new Payload.Types.Metric { Name = Rebirth, BooleanValue = true });
+        payload.Metrics.Add(new Payload.Types.Metric { Name = Rebirth, IntValue = 0 });
+
+        var forward = SparkplugNodeCommand.Classify(Encode(payload));
+
+        var reversed = new Payload();
+        reversed.Metrics.Add(new Payload.Types.Metric { Name = Rebirth, IntValue = 0 });
+        reversed.Metrics.Add(new Payload.Types.Metric { Name = Rebirth, BooleanValue = true });
+
+        forward.Should().Be(SparkplugNodeCommandKind.IgnoredAmbiguous);
+        SparkplugNodeCommand.Classify(Encode(reversed)).Should().Be(SparkplugNodeCommandKind.IgnoredAmbiguous);
+        forward.IsActionableRebirth().Should().BeFalse();
+        forward.DiagnosticCode().Should().Be("ignored:ambiguous");
+    }
+
     [Fact]
     public void DiagnosticCode_IsSecretFree_ForEveryKind()
     {
diff --git a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorRebirthTests.cs b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorRebirthTests.cs
index c410d4a..cc5a25b 100644
--- a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorRebirthTests.cs
+++ b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorRebirthTests.cs
@@ -933,6 +933,7 @@ public sealed class SparkplugSessionActorRebirthTests : IDisposable
 
         recording.Should().BeEmpty();                       // overflow is fatal — no backoff
         store.ReserveCalls.Should().Be(reservesAfterBirth); // the overflow check precedes bdSeq reservation
+        actor.DiagnosticsSnapshot.TransportRecoveryAttempts.Should().Be(0); // fatal preflight = NO attempt (r2 R2.2)
         actor.State.Should().Be(AdapterState.Failed);
     }
 
@@ -1603,6 +1604,147 @@ public sealed class SparkplugSessionActorRebirthTests : IDisposable
         actor.DiagnosticsSnapshot.BirthFailures.Should().Be(0); // aborted at the gate, never entered the transport
     }
 
+    // ==== Slice 7 r2: live operational-control overlay (health reflects async suspect/pending) ====
+
+    private async Task<(SparkplugSessionActor Actor, FakeTransport Fake, CapturingHost Host)> BornLive()
+    {
+        var born = await Born();
+        await born.Actor.CompleteCatchUpAsync(Cutover(), CancellationToken.None); // Replaying → Live
+        return born;
+    }
+
+    [Fact]
+    public async Task Health_LiveThenAsyncDisconnect_IsDegraded_SuspectAndPending()
+    {
+        var (actor, fake, _) = await BornLive();
+        (await actor.CheckHealthAsync(CancellationToken.None)).Level.Should().Be(HealthLevel.Healthy); // baseline
+
+        await fake.RaiseDisconnected(actor.CurrentGeneration); // async transport drop while Live
+
+        var health = await actor.CheckHealthAsync(CancellationToken.None);
+        health.Level.Should().Be(HealthLevel.Degraded);                    // waiting-for-rebirth is NOT healthy
+        health.Metrics!["suspectTransport"].Should().Be(true);
+        health.Metrics["pendingRebirth"].Should().Be(true);
+        health.Metrics["pendingRebirthReason"].Should().Be(RebirthReason.Other.ToString());
+    }
+
+    [Fact]
+    public async Task Health_LiveThenValidNodeCommand_IsDegraded_PendingNotSuspect()
+    {
+        var (actor, fake, _) = await BornLive();
+
+        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // host command, transport healthy
+
+        var health = await actor.CheckHealthAsync(CancellationToken.None);
+        health.Level.Should().Be(HealthLevel.Degraded);                    // the control latch blocks DATA
+        health.Metrics!["pendingRebirth"].Should().Be(true);
+        health.Metrics["pendingRebirthReason"].Should().Be(RebirthReason.HostCommand.ToString());
+        health.Metrics["suspectTransport"].Should().Be(false);             // a host command does not mark suspect
+    }
+
+    [Fact]
+    public async Task Health_RepeatedBlockedNodeCommands_DoNotChangeControlStateOrHealth()
+    {
+        var (actor, fake, _) = await BornLive();
+        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());
+        var afterFirst = actor.DiagnosticsSnapshot;
+
+        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());
+        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());
+
+        var health = await actor.CheckHealthAsync(CancellationToken.None);
+        health.Level.Should().Be(HealthLevel.Degraded);
+        actor.DiagnosticsSnapshot.PendingRebirth.Should().BeTrue();
+        actor.DiagnosticsSnapshot.RebirthRequestsQueued.Should().Be(afterFirst.RebirthRequestsQueued); // no new request
+    }
+
+    [Fact]
+    public async Task Health_AfterHealthyRebirth_ClearsPendingAndSuspect()
+    {
+        var (actor, fake, _) = await BornLive();
+        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // pending
+        (await actor.CheckHealthAsync(CancellationToken.None)).Level.Should().Be(HealthLevel.Degraded);
+
+        await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None); // healthy rebirth fulfils the episode
+
+        var diag = actor.DiagnosticsSnapshot;
+        diag.PendingRebirth.Should().BeFalse();
+        diag.SuspectTransport.Should().BeFalse();
+        diag.Epoch.Should().Be(1);
+    }
+
+    [Fact]
+    public async Task Health_DisposalWinsWhileRetirementBlocked_TerminalDisposed_NotHealthy()
+    {
+        var (actor, fake, _) = await BornLive();
+        (await actor.CheckHealthAsync(CancellationToken.None)).Level.Should().Be(HealthLevel.Healthy); // baseline Live
+
+        var block = new TaskCompletionSource();
+        fake.DisposeGate = block.Task;                 // hold the transport retirement
+        var dispose = actor.DisposeAsync().AsTask();   // wins ownership (marker), blocks in retirement
+
+        var health = await actor.CheckHealthAsync(CancellationToken.None);
+        health.Metrics!["terminalDisposed"].Should().Be(true);   // disposal has won, even before retirement completes
+        health.Level.Should().NotBe(HealthLevel.Healthy);        // must NOT keep reporting healthy Live
+
+        block.SetResult();
+        await dispose;
+        (await actor.CheckHealthAsync(CancellationToken.None)).State.Should().Be(AdapterState.Stopped);
+    }
+
+    // ==== Slice 7 r2: recovery-attempt counted only at the real attempt boundary ====
+
+    [Fact]
+    public async Task Diagnostics_ConnectFailureDuringBackoff_ShowsFailureCode_CountsOneAttempt()
+    {
+        var fake0 = new FakeTransport();
+        var failing = new FakeTransport { FailConnect = true };
+        var fakes = new Queue<ISparkplugMqttTransport>(
+            new ISparkplugMqttTransport[] { fake0, failing, new FakeTransport() });
+        var entered = new TaskCompletionSource();
+        var release = new TaskCompletionSource();
+        Func<TimeSpan, CancellationToken, Task> delay = async (_, __) => { entered.TrySetResult(); await release.Task; };
+        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fakes.Dequeue(), () => Clock, delay);
+        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
+        await actor.StartAsync(CancellationToken.None);
+        await actor.BeginReplaySessionAsync(Start(new CapturingHost()), CancellationToken.None);
+        await fake0.RaiseDisconnected(actor.CurrentGeneration);
+
+        var rebirth = actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);
+        await entered.Task; // attempt 1 (CONNECT) failed → parked in backoff
+
+        var diag = actor.DiagnosticsSnapshot;
+        diag.TransportRecoveryAttempts.Should().Be(1);                       // exactly one admitted attempt
+        diag.LastRecoveryFailureCode.Should().Be(SparkplugErrors.TransportConnectFailed); // visible DURING backoff
+        diag.CurrentRecoveryAttempt.Should().Be(1);
+
+        release.SetResult();
+        await rebirth; // recovers on attempt 2
+        actor.DiagnosticsSnapshot.TransportRecoveryAttempts.Should().Be(2);
+    }
+
+    [Fact]
+    public async Task Diagnostics_StoreReserveFailureDuringRecovery_CountsNoAttempt()
+    {
+        var recording = new List<TimeSpan>();
+        var fake0 = new FakeTransport();
+        var store = new ScriptableStore(NewStore());
+        var actor = new SparkplugSessionActor(
+            "spb-1", store, () => fake0.Connected ? new FakeTransport() : fake0, () => Clock, Recording(recording));
+        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
+        await actor.StartAsync(CancellationToken.None);
+        await actor.BeginReplaySessionAsync(Start(new CapturingHost()), CancellationToken.None);
+        await fake0.RaiseDisconnected(actor.CurrentGeneration);
+        store.ThrowOnReserve = true; // bdSeq reservation fails (fatal preparation, before the attempt boundary)
+
+        await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None))
+            .Should().ThrowAsync<Core.Errors.AdapterException>();
+
+        var diag = actor.DiagnosticsSnapshot;
+        diag.TransportRecoveryAttempts.Should().Be(0); // reservation failure is not a "complete attempt" (r2 R2.2)
+        recording.Should().BeEmpty();                  // fatal preparation, no backoff
+    }
+
     // ==== Helpers ====
 
     private static Func<TimeSpan, CancellationToken, Task> Recording(List<TimeSpan> sink) =>
@@ -1715,6 +1857,7 @@ public sealed class SparkplugSessionActorRebirthTests : IDisposable
         public ScriptableStore(ISparkplugIdentityStateStore inner) => _inner = inner;
 
         public bool ThrowOnResolve { get; set; }
+        public bool ThrowOnReserve { get; set; }
         public int ReserveCalls { get; private set; }
         public int ResolveCalls { get; private set; }
         public Task? ResolveGate { get; set; }                 // if set, ResolveAliases blocks (synchronously) until it completes
@@ -1723,6 +1866,17 @@ public sealed class SparkplugSessionActorRebirthTests : IDisposable
         public SparkplugBirthDeathSequence ReserveNextBdSeq(SparkplugStoreIdentity identity)
         {
             ReserveCalls++;
+            if (ThrowOnReserve)
+            {
+                throw new Core.Errors.AdapterException(new Core.Errors.AdapterError
+                {
+                    Code = SparkplugErrors.IdentityStoreUnavailable,
+                    Category = Core.Errors.ErrorCategory.Internal,
+                    Message = "bdSeq reservation failed (test)",
+                    Retryable = false,
+                });
+            }
+
             return _inner.ReserveNextBdSeq(identity);
         }
 
```
