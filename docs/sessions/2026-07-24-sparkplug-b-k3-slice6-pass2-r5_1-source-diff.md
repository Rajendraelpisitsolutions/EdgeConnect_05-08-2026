# K3 Slice 6 pass 2 — Exact Source Diff r5.1 (cumulative, on top of approved r5)

**Baseline:** `3513cdc` (r5, APPROVED) → **HEAD** `91298fb`
**r5.1 commits:** `7afae35` (in-attempt guard + test) and `91298fb` (approved ownership-contract wording).
**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master)

Full cumulative `git diff` of the complete r5.1 source change (2 files, 0 elision) for line-level sign-off.

```diff
diff --git a/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs
index 70d052f..086ca86 100644
--- a/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs
+++ b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs
@@ -378,7 +378,8 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         LatestValueSnapshot snapshot, CancellationToken cancellationToken)
     {
         var prepared = PrepareBirth(snapshot);
-        return await AttemptConnectionAsync(prepared, sessionId, epoch, routeId, host, cancellationToken).ConfigureAwait(false);
+        return await AttemptConnectionAsync(prepared, sessionId, epoch, routeId, host, recoveryToken: null, cancellationToken)
+            .ConfigureAwait(false);
     }
 
     // The NON-RETRYABLE birth preparation (slice-6 review r1 B1): snapshot planning, alias-store resolution,
@@ -408,11 +409,28 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
     // TRANSPORT failure of this method is retryable (see IsRetryableEstablishmentFailure).
     private async Task<ActiveSession> AttemptConnectionAsync(
         PreparedBirth prepared, ReplaySessionId sessionId, ReplayEpochId epoch, string routeId, IReplaySessionHost host,
-        CancellationToken cancellationToken)
+        object? recoveryToken, CancellationToken cancellationToken)
     {
         var config = _config!;
         var node = prepared.Node;
 
+        // Ownership contract (review r5 / r5.1 design ruling): disposal (or a superseding lifecycle call)
+        // prevents ADMISSION of a new establishment attempt — it does NOT interrupt one already admitted under
+        // the actor gate. This re-check rejects an attempt that has not yet passed this point; an attempt
+        // already past it may finish or abort, and any committed-but-unused bdSeq / generation gap is
+        // intentional (monotonic, never reused), with disposal retiring any resulting transport before it
+        // completes. This is defense-in-depth narrowing, NOT a hard no-gap linearization guarantee. Begin
+        // (null token) fails closed with ObjectDisposedException (its non-faulting passthrough); a recovery
+        // attempt validates its token (aborts with OperationCanceledException).
+        if (recoveryToken is null)
+        {
+            ThrowIfDisposed();
+        }
+        else
+        {
+            ValidateRecoveryOwnership(recoveryToken);
+        }
+
         // Generation exhaustion is checked BEFORE reserving a durable bdSeq (carry-forward 2 / B1), so the
         // terminal long.MaxValue case can never consume a bdSeq with no possible CONNECT, and it is fatal.
         if (_lastIssuedConnectionGeneration == long.MaxValue)
@@ -711,7 +729,7 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
                 try
                 {
                     var candidate = await AttemptConnectionAsync(
-                        prepared, sessionId, epoch, previous.RouteId, previous.Host, cancellationToken).ConfigureAwait(false);
+                        prepared, sessionId, epoch, previous.RouteId, previous.Host, token, cancellationToken).ConfigureAwait(false);
                     await PromoteAndDrainAsync(candidate).ConfigureAwait(false);
                     return; // recovered within budget — no route fault
                 }
diff --git a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorRebirthTests.cs b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorRebirthTests.cs
index 777b412..5cbb3a5 100644
--- a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorRebirthTests.cs
+++ b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorRebirthTests.cs
@@ -1056,6 +1056,42 @@ public sealed class SparkplugSessionActorRebirthTests : IDisposable
         actor.HasSession.Should().BeFalse();                   // no candidate authority promoted
     }
 
+    // ==== Pass 2 r5.1: the in-attempt guard aborts before the first durable allocation (bdSeq/generation/transport) ====
+
+    [Fact]
+    public async Task Begin_DisposalWinsDuringBirthPrep_FailsClosed_BeforeBdSeqOrTransport()
+    {
+        var fake = new FakeTransport();
+        var store = new ScriptableStore(NewStore());
+        var host = new CapturingHost();
+        var factoryCalls = 0;
+        var actor = new SparkplugSessionActor(
+            "spb-1", store, () => { factoryCalls++; return fake; }, () => Clock, InstantDelay);
+        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
+        await actor.StartAsync(CancellationToken.None);
+
+        // Park Begin INSIDE PrepareBirth's alias resolution — after the outer ThrowIfDisposed, before the
+        // in-attempt guard and the first durable allocation. Begin holds the gate throughout.
+        var resolveEntered = new TaskCompletionSource();
+        var resolveBarrier = new TaskCompletionSource();
+        store.ResolveEntered = resolveEntered;
+        store.ResolveGate = resolveBarrier.Task;
+        var begin = Task.Run(() => actor.BeginReplaySessionAsync(Start(host), CancellationToken.None));
+        await resolveEntered.Task;                     // Begin parked in birth prep, holding the gate
+
+        var dispose = actor.DisposeAsync().AsTask();   // wins ownership (installs the marker), then blocks on the gate
+        resolveBarrier.SetResult();                    // release birth prep → AttemptConnectionAsync's in-attempt guard sees disposal
+        await dispose;
+
+        await FluentActions.Awaiting(() => begin).Should().ThrowAsync<ObjectDisposedException>();
+        store.ReserveCalls.Should().Be(0);             // guard tripped BEFORE ReserveNextBdSeq — no durable bdSeq
+        factoryCalls.Should().Be(0);                   // no transport created
+        actor.LastIssuedGeneration.Should().Be(0);     // no generation issued
+        actor.State.Should().Be(AdapterState.Stopped); // disposal terminal
+        actor.ProtocolState.Should().Be(SparkplugProtocolState.Stopped);
+        actor.HasSession.Should().BeFalse();           // nothing promoted
+    }
+
     [Fact]
     public async Task Dispose_LeavesCoherentTerminalStoppedState()
     {
@@ -1336,6 +1372,8 @@ public sealed class SparkplugSessionActorRebirthTests : IDisposable
         public bool ThrowOnResolve { get; set; }
         public int ReserveCalls { get; private set; }
         public int ResolveCalls { get; private set; }
+        public Task? ResolveGate { get; set; }                 // if set, ResolveAliases blocks (synchronously) until it completes
+        public TaskCompletionSource? ResolveEntered { get; set; } // signalled when a gated ResolveAliases starts blocking
 
         public SparkplugBirthDeathSequence ReserveNextBdSeq(SparkplugStoreIdentity identity)
         {
@@ -1347,6 +1385,7 @@ public sealed class SparkplugSessionActorRebirthTests : IDisposable
             SparkplugStoreIdentity identity, IReadOnlyCollection<SparkplugAliasKey> manifest)
         {
             ResolveCalls++;
+            if (ResolveGate is { } gate) { ResolveEntered?.TrySetResult(); gate.GetAwaiter().GetResult(); }
             if (ThrowOnResolve)
             {
                 throw new Core.Errors.AdapterException(new Core.Errors.AdapterError
```
