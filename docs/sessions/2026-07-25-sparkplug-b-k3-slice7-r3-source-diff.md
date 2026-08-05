# K3 Slice 7 — Exact Source Diff r3 (atomic authority-bound handoff overlay + symmetric attempt evidence)

**Commit:** `b3b19a9` — *fix(sparkplug): K3 slice 7 review r3*
**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master)

Full `git show` (2 files, 0 elision) for line-level sign-off.

```diff
commit b3b19a9ca5c79946dcd35e47131fa36a6de33f9c
Author: Sudhakar <sudhakar@elpisitsolutions.com>
Date:   Sat Jul 25 00:18:01 2026 +0530

    fix(sparkplug): K3 slice 7 review r3 - atomic authority-bound handoff overlay + symmetric attempt evidence
    
    Folds the R3.1 coherence blocker + R3.2 test completions. No Core change.
    
    R3.1 — one atomic, authority-bound control read. The live control overlay read
    the handoff through three separate lock acquisitions (SuspectAfterPromotion /
    RebirthPending / PendingReason), so a disconnect racing between them could expose
    a torn triple that never existed atomically (e.g. suspect=false with reason
    Other). New AttemptHandoff.ReadDiagnostics() returns the suspect/pending/reason
    triple under ONE lock; both PublishTransition and the live overlay use it.
    
    The overlay was also bound on connection generation alone. A healthy
    same-connection rebirth retains the generation while advancing the epoch, so a
    new-epoch control condition could be attached to the old-epoch semantic root
    during the authority-swap window. The overlay now binds on the FULL authority —
    SessionId + Epoch + ConnectionGeneration must all match — else the root's coherent
    baked values stand. A PostAuthorityPublishBarrier test seam makes that window
    deterministically observable.
    
    R3.2 — symmetric attempt-boundary + coalescing evidence. Establishment failure
    during backoff is now a Theory over connect/subscribe/nbirth (each: one attempt +
    its code visible during backoff). Added: transport-factory failure records no
    attempt and ordinal zero; a cutover re-drain while pending does not inflate
    rebirthRequestsCoalesced. Generation/store failures already assert no attempt.
    
    581 SparkplugB tests; regressions green: Core 1250, Host 225, Management 1149
    (full project). Solution 0 errors; SparkplugB 0 warnings under warnings-as-errors.
    
    Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>

diff --git a/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs
index 34da092..e83868c 100644
--- a/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs
+++ b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs
@@ -201,6 +201,14 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
     /// </summary>
     internal Func<Task>? PostRebirthCommitBarrier { get; set; }
 
+    /// <summary>
+    /// Test seam awaited once in the healthy-rebirth authority-swap window: AFTER the new-epoch
+    /// <c>_activeSession</c> is published but BEFORE the semantic diagnostic root is republished. Lets a test
+    /// prove the live control overlay binds on the full authority (session+epoch+generation), so a new-epoch
+    /// control condition is not attached to the old-epoch semantic root (slice-7 review r3 R3.1).
+    /// </summary>
+    internal Func<Task>? PostAuthorityPublishBarrier { get; set; }
+
     /// <summary>The coarse adapter lifecycle state (the ISinkAdapter contract surface).</summary>
     public AdapterState State => _snapshot.State;
 
@@ -806,6 +814,14 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         Interlocked.Increment(ref _healthyRebirths); // a healthy in-place NBIRTH re-announcement committed (plan v3 §8)
         Interlocked.Exchange(ref _lastSuccessfulBirthAtTicks, _clock().UtcTicks);
 
+        // Deterministic seam INSIDE the authority-swap window: _activeSession is now the NEW epoch but the
+        // semantic root is not yet republished (slice-7 review r3 R3.1 — proves the diagnostic overlay binds on
+        // the full authority and does not attach new-epoch control to the old semantic root).
+        if (PostAuthorityPublishBarrier is { } authorityBarrier)
+        {
+            await authorityBarrier().ConfigureAwait(false);
+        }
+
         // Finish the commit (RebirthCommitting -> Active, or leave Suspect if a drop raced) and drain any
         // fresh episode a control event opened during the commit — against the new authoritative epoch.
         var freshPending = session.Handoff.FinishRebirthCommit();
@@ -1817,6 +1833,9 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
 
     private sealed record ActorSnapshot(AdapterState State, SparkplugProtocolState ProtocolState);
 
+    /// <summary>The atomic operational-control triple read from a handoff under one lock (r3 R3.1).</summary>
+    private readonly record struct HandoffDiagnostics(bool Suspect, bool Pending, RebirthReason? Reason);
+
     private static DateTimeOffset? TicksToOffset(long ticks) =>
         ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
 
@@ -1837,17 +1856,21 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
             var semantic = _semantic ?? BuildInitialSemantic();
             var lastError = _lastError;
 
-            // Live control overlay bound to the semantic authority. Only overlay the handoff when it belongs to
-            // the SAME connection generation as the published semantic root (else a promotion-window swap could
-            // pair a new handoff with an old lifecycle root); otherwise keep the root's coherent baked values.
+            // Live control overlay bound to the FULL semantic authority (slice-7 review r3 R3.1): session +
+            // epoch + connection generation must ALL match, so a healthy same-generation epoch promotion (which
+            // retains the generation but advances the epoch) can never attach new-epoch control state to the old
+            // semantic root. When bound, the control triple is captured in ONE atomic handoff read (never torn);
+            // otherwise the root's coherent baked values stand.
             var active = _activeSession;
             var bound = active is not null && semantic.HasSession
+                && active.SessionId.Value == semantic.SessionId
+                && active.Epoch.Value == semantic.Epoch
                 && active.TransportGeneration == semantic.ConnectionGeneration;
-            var handoff = bound ? active!.Handoff : null;
-            var suspect = handoff?.SuspectAfterPromotion ?? semantic.SuspectTransport;
-            var pending = handoff?.RebirthPending ?? semantic.PendingRebirth;
+            var control = bound ? active!.Handoff.ReadDiagnostics() : (HandoffDiagnostics?)null;
+            var suspect = control?.Suspect ?? semantic.SuspectTransport;
+            var pending = control?.Pending ?? semantic.PendingRebirth;
             var pendingReason = pending
-                ? (handoff is not null ? handoff.PendingReason.ToString() : semantic.PendingRebirthReason)
+                ? (control is { } c ? c.Reason?.ToString() : semantic.PendingRebirthReason)
                 : null;
 
             return semantic with
@@ -1912,8 +1935,9 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
                 _lastTransitionReasonCode = reasonCode;
             }
 
-            var handoff = active?.Handoff;
-            var pending = handoff?.RebirthPending ?? false;
+            // ONE atomic handoff read (slice-7 review r3 R3.1): suspect/pending/reason captured under a single
+            // handoff-lock acquisition, so the baked control triple is never internally torn.
+            var control = active?.Handoff.ReadDiagnostics();
 
             _semantic = new SparkplugActorDiagnostics
             {
@@ -1933,9 +1957,9 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
                 LastIssuedGeneration = Interlocked.Read(ref _lastIssuedConnectionGeneration),
                 BdSeq = active is null ? null : active.BdSeq.Value,
                 NextSeq = active is null ? null : Volatile.Read(ref _nextSeq),
-                SuspectTransport = handoff?.SuspectAfterPromotion ?? false,
-                PendingRebirth = pending,
-                PendingRebirthReason = pending ? handoff!.PendingReason.ToString() : null,
+                SuspectTransport = control?.Suspect ?? false,
+                PendingRebirth = control?.Pending ?? false,
+                PendingRebirthReason = (control?.Pending ?? false) ? control!.Value.Reason?.ToString() : null,
                 CurrentRecoveryAttempt = _currentRecoveryAttempt,
                 RecoveryAttemptBudget = _config is null ? 0 : Math.Max(1, _config.TransportRecoveryMaxAttempts),
                 // A stable baseline for the last-event/counter fields — overlaid live on read.
@@ -2022,6 +2046,22 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         /// <summary>The diagnostic cause of the current episode; transport suspicion always reports Other and wins (r3 R3.3).</summary>
         public RebirthReason PendingReason { get { lock (_sync) { return _state == Suspect ? RebirthReason.Other : _reason; } } }
 
+        /// <summary>
+        /// Read the operational-control triple (suspect / pending / reason) under a SINGLE lock acquisition, so
+        /// the diagnostic overlay never observes a torn combination that never existed atomically — e.g.
+        /// suspect=false with reason=Other, which a disconnect racing between separate property reads could
+        /// otherwise produce (slice-7 review r3 R3.1).
+        /// </summary>
+        public HandoffDiagnostics ReadDiagnostics()
+        {
+            lock (_sync)
+            {
+                var suspect = _state == Suspect;
+                var reason = _pending ? (suspect ? RebirthReason.Other : _reason) : (RebirthReason?)null;
+                return new HandoffDiagnostics(suspect, _pending, reason);
+            }
+        }
+
         /// <summary>Claim the initial promotion. Returns false if a disconnect already invalidated the attempt.</summary>
         public bool TryPromote()
         {
diff --git a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorRebirthTests.cs b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorRebirthTests.cs
index cc5a25b..05c4305 100644
--- a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorRebirthTests.cs
+++ b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorRebirthTests.cs
@@ -1694,11 +1694,21 @@ public sealed class SparkplugSessionActorRebirthTests : IDisposable
 
     // ==== Slice 7 r2: recovery-attempt counted only at the real attempt boundary ====
 
-    [Fact]
-    public async Task Diagnostics_ConnectFailureDuringBackoff_ShowsFailureCode_CountsOneAttempt()
+    [Theory]
+    [InlineData("connect", SparkplugErrors.TransportConnectFailed)]
+    [InlineData("subscribe", SparkplugErrors.TransportSubscribeFailed)]
+    [InlineData("nbirth", SparkplugErrors.BirthPublishFailed)]
+    public async Task Diagnostics_EstablishmentFailureDuringBackoff_ShowsFailureCode_CountsOneAttempt(string failAt, string expectedCode)
     {
         var fake0 = new FakeTransport();
-        var failing = new FakeTransport { FailConnect = true };
+        var failing = new FakeTransport();
+        switch (failAt)
+        {
+            case "connect": failing.FailConnect = true; break;
+            case "subscribe": failing.FailSubscribe = true; break;
+            case "nbirth": failing.PublishReturnsFalse = true; break;
+        }
+
         var fakes = new Queue<ISparkplugMqttTransport>(
             new ISparkplugMqttTransport[] { fake0, failing, new FakeTransport() });
         var entered = new TaskCompletionSource();
@@ -1711,11 +1721,11 @@ public sealed class SparkplugSessionActorRebirthTests : IDisposable
         await fake0.RaiseDisconnected(actor.CurrentGeneration);
 
         var rebirth = actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None);
-        await entered.Task; // attempt 1 (CONNECT) failed → parked in backoff
+        await entered.Task; // attempt 1 (CONNECT/SUBSCRIBE/NBIRTH) failed → parked in backoff
 
         var diag = actor.DiagnosticsSnapshot;
         diag.TransportRecoveryAttempts.Should().Be(1);                       // exactly one admitted attempt
-        diag.LastRecoveryFailureCode.Should().Be(SparkplugErrors.TransportConnectFailed); // visible DURING backoff
+        diag.LastRecoveryFailureCode.Should().Be(expectedCode);              // the causing code, visible DURING backoff
         diag.CurrentRecoveryAttempt.Should().Be(1);
 
         release.SetResult();
@@ -1723,6 +1733,86 @@ public sealed class SparkplugSessionActorRebirthTests : IDisposable
         actor.DiagnosticsSnapshot.TransportRecoveryAttempts.Should().Be(2);
     }
 
+    [Fact]
+    public async Task Diagnostics_FactoryFailureDuringRecovery_CountsNoAttempt_OrdinalZero()
+    {
+        var recording = new List<TimeSpan>();
+        var fake0 = new FakeTransport();
+        var factoryCalls = 0;
+        var actor = new SparkplugSessionActor(
+            "spb-1", NewStore(),
+            () => { factoryCalls++; return factoryCalls == 1 ? fake0 : throw new InvalidOperationException("factory boom"); },
+            () => Clock, Recording(recording));
+        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
+        await actor.StartAsync(CancellationToken.None);
+        await actor.BeginReplaySessionAsync(Start(new CapturingHost()), CancellationToken.None);
+        await fake0.RaiseDisconnected(actor.CurrentGeneration);
+
+        await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None)).Should().ThrowAsync<Exception>();
+
+        var diag = actor.DiagnosticsSnapshot;
+        diag.TransportRecoveryAttempts.Should().Be(0); // the client was never created → no complete attempt (r3 R3.2)
+        diag.CurrentRecoveryAttempt.Should().Be(0);
+        recording.Should().BeEmpty();                  // a non-retryable factory failure does not back off
+    }
+
+    [Fact]
+    public async Task Diagnostics_CutoverRedrainWhilePending_DoesNotInflateCoalesced()
+    {
+        var (actor, fake, _) = await Born();
+        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // opens episode: queued 1, coalesced 0
+        var coalescedBefore = actor.DiagnosticsSnapshot.RebirthRequestsCoalesced;
+
+        await actor.CompleteCatchUpAsync(Cutover(), CancellationToken.None); // pending → re-drain (no new signal, no Live)
+
+        actor.DiagnosticsSnapshot.RebirthRequestsCoalesced.Should().Be(coalescedBefore); // a re-drain is NOT a coalesce
+    }
+
+    // ==== Slice 7 r3: atomic authority-bound handoff overlay ====
+
+    [Fact]
+    public async Task Diagnostics_HostCommandThenDisconnect_ControlTripleIsCoherent()
+    {
+        var (actor, fake, _) = await BornLive();
+        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // pending HostCommand, NOT suspect
+        await fake.RaiseDisconnected(actor.CurrentGeneration);                  // now suspect → reason resolves to Other
+
+        var diag = actor.DiagnosticsSnapshot;
+        // The atomic read yields a combination that actually existed — suspect implies reason Other. The torn
+        // combination suspect=false with reason=Other (a race between separate reads) can never appear.
+        diag.SuspectTransport.Should().BeTrue();
+        diag.PendingRebirth.Should().BeTrue();
+        diag.PendingRebirthReason.Should().Be(RebirthReason.Other.ToString());
+        (diag is { SuspectTransport: false, PendingRebirthReason: "Other" }).Should().BeFalse();
+    }
+
+    [Fact]
+    public async Task Diagnostics_HealthyEpochPromotion_DoesNotLeakNewEpochControlOntoOldRoot()
+    {
+        var (actor, fake, _) = await BornLive();
+        var atBarrier = new TaskCompletionSource();
+        var release = new TaskCompletionSource();
+        // Fire INSIDE the authority-swap window: _activeSession is epoch 1, _semantic is still epoch 0.
+        actor.PostAuthorityPublishBarrier = async () => { atBarrier.TrySetResult(); await release.Task; };
+
+        var rebirth = actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None); // healthy: epoch 0→1, SAME generation
+        await atBarrier.Task;
+
+        // A control event now opens a FRESH episode on the (epoch-1) handoff. With generation-only binding this
+        // would leak onto the epoch-0 semantic root; full-authority (session+epoch+generation) binding rejects it.
+        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand());
+
+        var duringPromotion = actor.DiagnosticsSnapshot;
+        duringPromotion.Epoch.Should().Be(0);                 // the semantic root is still the old epoch
+        duringPromotion.PendingRebirth.Should().BeFalse();    // epoch-1 control is NOT attached to the epoch-0 root
+
+        release.SetResult();
+        await rebirth;
+        var afterPublish = actor.DiagnosticsSnapshot;
+        afterPublish.Epoch.Should().Be(1);                    // new authority published
+        afterPublish.PendingRebirth.Should().BeTrue();        // and its live control overlay is now coherent + visible
+    }
+
     [Fact]
     public async Task Diagnostics_StoreReserveFailureDuringRecovery_CountsNoAttempt()
     {
```
