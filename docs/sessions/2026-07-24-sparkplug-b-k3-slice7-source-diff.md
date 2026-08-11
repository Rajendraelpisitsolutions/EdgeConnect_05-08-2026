# K3 Slice 7 — Exact Source Diff (health, diagnostics, counters, redaction)

**Commit:** `c136883` — *feat(sparkplug): K3 slice 7 - health, diagnostics, counters, redaction*
**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master)

Full `git show` (4 files: 1 new type + actor + 2 test files, 0 elision) for line-level sign-off.

```diff
commit c1368837544604daf71e4d171e98fde07af84b74
Author: Sudhakar <sudhakar@elpisitsolutions.com>
Date:   Fri Jul 24 16:07:58 2026 +0530

    feat(sparkplug): K3 slice 7 - health, diagnostics, counters, redaction
    
    The final K3 slice: a complete, coherent, redacted, versioned actor diagnostic
    surface (plan v3 §8), with no Core change.
    
    - SparkplugActorDiagnostics: one immutable, atomically-published, monotonically
      VERSIONED snapshot so health/diagnostics can never report a field combination
      that never existed. Carries lifecycle + protocol substate, the immediately
      preceding transition (previous states, lastStateChangeAt, reason code), the
      session authority, recovery progress, lifetime counters, last-event timestamps,
      and a SANITIZED last error (code/category only, Message cleared). Never a
      credential, endpoint, client id, topic, payload byte, or metric value.
    - Monotonic lifetime counters (Interlocked; never reset on rebirth): stale
      disconnect/NCMD callbacks, rebirth requests queued vs coalesced, healthy
      rebirths, transport-recovery starts/attempts/successes/exhaustions, and
      publish/birth/death-publish failures. currentRecoveryAttempt (ordinal in the
      active episode, 0 when none) and recoveryAttemptBudget are surfaced as current
      state, distinct from the lifetime attempts tally.
    - CheckHealthAsync now reads one coherent snapshot, maps §8 health levels,
      populates AdapterHealth.LastError (sanitized) + LastSuccessAt, uses the injected
      clock, and emits stable metric keys (Studio/support/K4 meters may depend on
      them). System.Diagnostics.Metrics wiring is deferred to K4, projecting from this
      same counter source.
    - Recovery backoff now reports the honest RecoveringTransport substate during the
      delay window instead of the failed attempt's stale substate.
    
    Tests: health-level matrix (ready-no-session/Live/replaying-degraded/faulted),
    coherent session fields, monotonic version, last-transition fields, each counter
    at its trigger, currentRecoveryAttempt ordinal + reset, recovery starts/attempts/
    successes/exhaustion, sanitized last error, and a redaction guard proving no
    credential/endpoint leaks through the health snapshot. All broker-free and
    deterministic (injected clock).
    
    Regressions green: Core 1250, Host 225, Management 1149 (full project), SparkplugB
    556. Solution builds 0 errors; SparkplugB 0 warnings under warnings-as-errors.
    
    Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>

diff --git a/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugActorDiagnostics.cs b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugActorDiagnostics.cs
new file mode 100644
index 0000000..bcf0eab
--- /dev/null
+++ b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugActorDiagnostics.cs
@@ -0,0 +1,166 @@
+// ============================================================================
+// File: Session/SparkplugActorDiagnostics.cs
+// Purpose: The complete, redacted, point-in-time diagnostic state of one
+//          SparkplugSessionActor (K3 slice 7 — health/diagnostics). A single
+//          immutable, atomically-published, monotonically-VERSIONED snapshot so
+//          health and diagnostics can never report a combination of fields that
+//          never existed. This is NOT a historical transition log — it is the
+//          actor's coherent state at one observation point: lifecycle + protocol
+//          substate, the immediately preceding transition, the session authority,
+//          recovery progress, lifetime counters, and the last-event timestamps.
+//          It carries ONLY safe values (ids, generations, bdSeq, epoch, counts,
+//          timestamps, sanitized error code/category) — never credentials, broker
+//          endpoint, client id, topics, payload bytes, or metric values.
+// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md §8, §11.2.
+// ============================================================================
+
+using System;
+using ElpisEdgeConnect.Core.Adapters;
+using ElpisEdgeConnect.Core.Errors;
+
+namespace ElpisEdgeConnect.Sinks.SparkplugB.Session;
+
+/// <summary>
+/// A coherent, redacted, versioned snapshot of the Sparkplug session actor's state
+/// (plan v3 §8). Published atomically at each transition; read as a single reference
+/// so no field combination is ever torn. Contains no secret-bearing values.
+/// </summary>
+internal sealed record SparkplugActorDiagnostics
+{
+    /// <summary>Monotonic snapshot version — strictly increases with each republish.</summary>
+    public required long Version { get; init; }
+
+    // --- lifecycle + the immediately preceding transition ---
+
+    /// <summary>Coarse adapter lifecycle state (the <c>ISinkAdapter</c> contract surface).</summary>
+    public required AdapterState State { get; init; }
+
+    /// <summary>Fine protocol substate (internal diagnostics; never the contract surface).</summary>
+    public required SparkplugProtocolState ProtocolState { get; init; }
+
+    /// <summary>The coarse state immediately before the last transition.</summary>
+    public required AdapterState PreviousState { get; init; }
+
+    /// <summary>The fine substate immediately before the last transition.</summary>
+    public required SparkplugProtocolState PreviousProtocolState { get; init; }
+
+    /// <summary>When the last transition was published (injected clock).</summary>
+    public required DateTimeOffset LastStateChangeAt { get; init; }
+
+    /// <summary>A short, stable, secret-free reason code for the last transition.</summary>
+    public required string LastTransitionReasonCode { get; init; }
+
+    /// <summary>True once disposal has won (terminal, non-resurrectable).</summary>
+    public required bool TerminalDisposed { get; init; }
+
+    // --- session authority (present only when an authority exists) ---
+
+    /// <summary>True when an authoritative session is currently promoted.</summary>
+    public required bool HasSession { get; init; }
+
+    /// <summary>The authoritative replay session id, or null when none.</summary>
+    public long? SessionId { get; init; }
+
+    /// <summary>The authoritative replay epoch, or null when none.</summary>
+    public long? Epoch { get; init; }
+
+    /// <summary>The authoritative route id, or null when none.</summary>
+    public string? RouteId { get; init; }
+
+    /// <summary>The authoritative connection generation, or null when none.</summary>
+    public long? ConnectionGeneration { get; init; }
+
+    /// <summary>The most recently ISSUED connection generation (whether or not it birthed).</summary>
+    public required long LastIssuedGeneration { get; init; }
+
+    /// <summary>The authoritative bdSeq wire value, or null when none.</summary>
+    public int? BdSeq { get; init; }
+
+    /// <summary>The next DATA wire sequence, or null when no session.</summary>
+    public int? NextSeq { get; init; }
+
+    // --- recovery / rebirth progress (current state) ---
+
+    /// <summary>True when the authoritative transport is suspect (a drop or uncertain send).</summary>
+    public required bool SuspectTransport { get; init; }
+
+    /// <summary>True when a Core rebirth is pending (control episode open).</summary>
+    public required bool PendingRebirth { get; init; }
+
+    /// <summary>The pending rebirth's reason, or null when none pending.</summary>
+    public string? PendingRebirthReason { get; init; }
+
+    /// <summary>The ordinal within the CURRENTLY active recovery episode; 0 when none is running.</summary>
+    public required int CurrentRecoveryAttempt { get; init; }
+
+    /// <summary>The configured recovery-attempt budget.</summary>
+    public required int RecoveryAttemptBudget { get; init; }
+
+    /// <summary>The sanitized code of the last recovery failure, or null.</summary>
+    public string? LastRecoveryFailureCode { get; init; }
+
+    // --- last-event timestamps (injected clock; null until first occurrence) ---
+
+    /// <summary>When the last successful birth (NBIRTH promotion) occurred.</summary>
+    public DateTimeOffset? LastSuccessfulBirthAt { get; init; }
+
+    /// <summary>When the last successful DATA publish occurred.</summary>
+    public DateTimeOffset? LastDataPublishAt { get; init; }
+
+    /// <summary>When the last Core rebirth request was queued.</summary>
+    public DateTimeOffset? LastRebirthRequestAt { get; init; }
+
+    /// <summary>When the last error was recorded.</summary>
+    public DateTimeOffset? LastErrorAt { get; init; }
+
+    /// <summary>The sanitized code of the last recorded error (no message/endpoint/payload), or null.</summary>
+    public string? LastErrorCode { get; init; }
+
+    /// <summary>The category of the last recorded error, or null.</summary>
+    public string? LastErrorCategory { get; init; }
+
+    /// <summary>
+    /// The last recorded error as a sanitized <see cref="AdapterError"/> (Message cleared so no endpoint,
+    /// credential, or payload value can leak through health/diagnostics), or null. Feeds
+    /// <see cref="AdapterHealth.LastError"/> so both come from one coherent read.
+    /// </summary>
+    public AdapterError? LastError { get; init; }
+
+    // --- monotonic lifetime counters (per actor instance; NEVER reset on rebirth) ---
+
+    /// <summary>Delayed disconnect callbacks from a retired transport generation, ignored.</summary>
+    public required long StaleDisconnectCallbacks { get; init; }
+
+    /// <summary>Delayed NCMD callbacks from a retired transport generation, ignored.</summary>
+    public required long StaleNodeCommandCallbacks { get; init; }
+
+    /// <summary>Core rebirth requests actually queued (a fresh episode woke Core).</summary>
+    public required long RebirthRequestsQueued { get; init; }
+
+    /// <summary>Rebirth signals coalesced into an already-open episode (no second Core request).</summary>
+    public required long RebirthRequestsCoalesced { get; init; }
+
+    /// <summary>Healthy in-place rebirths committed (NBIRTH re-emitted on the same connection).</summary>
+    public required long HealthyRebirths { get; init; }
+
+    /// <summary>Transport-suspect recovery episodes started.</summary>
+    public required long TransportRecoveryStarts { get; init; }
+
+    /// <summary>Complete CONNECT/SUBSCRIBE/NBIRTH recovery attempts made (lifetime).</summary>
+    public required long TransportRecoveryAttempts { get; init; }
+
+    /// <summary>Recovery episodes that succeeded within budget.</summary>
+    public required long TransportRecoverySuccesses { get; init; }
+
+    /// <summary>Recovery episodes that exhausted the budget and faulted.</summary>
+    public required long TransportRecoveryExhaustions { get; init; }
+
+    /// <summary>Observable/uncertain DATA publish failures that requested a rebirth.</summary>
+    public required long PublishFailures { get; init; }
+
+    /// <summary>NBIRTH publish failures (initial or rebirth).</summary>
+    public required long BirthFailures { get; init; }
+
+    /// <summary>NDEATH publish failures during graceful end.</summary>
+    public required long DeathPublishFailures { get; init; }
+}
diff --git a/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs
index 086ca86..b186195 100644
--- a/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs
+++ b/src/ElpisEdgeConnect.Sinks.SparkplugB/Session/SparkplugSessionActor.cs
@@ -82,6 +82,44 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
     // semantics (the documented synchronization mechanism for this cross-thread field).
     private volatile ActiveSession? _activeSession;
 
+    // --- Slice 7 diagnostics (plan v3 §8): monotonic lifetime counters, per actor instance, NEVER
+    // reset on rebirth. Interlocked for the ones touched from asynchronous MQTT callbacks; the rest are
+    // gate-owned but use Interlocked consistently so the health snapshot reads them coherently. ---
+    private long _staleDisconnectCallbacks;
+    private long _staleNodeCommandCallbacks;
+    private long _rebirthRequestsQueued;
+    private long _rebirthRequestsCoalesced;
+    private long _healthyRebirths;
+    private long _transportRecoveryStarts;
+    private long _transportRecoveryAttempts;
+    private long _transportRecoverySuccesses;
+    private long _transportRecoveryExhaustions;
+    private long _publishFailures;
+    private long _birthFailures;
+    private long _deathPublishFailures;
+
+    // Last-event timestamps as UtcTicks (0 = never); Interlocked.Exchange/Read for 64-bit atomicity.
+    private long _lastSuccessfulBirthAtTicks;
+    private long _lastDataPublishAtTicks;
+    private long _lastRebirthRequestAtTicks;
+    private long _lastErrorAtTicks;
+
+    // The coherent, versioned diagnostic snapshot (plan v3 §8): rebuilt under _diagLock at each transition
+    // and published as a single volatile reference, so a reader never sees a torn field combination. The
+    // persistent "current-state" fields below are mutated ONLY under _diagLock (inside RepublishDiagnostics).
+    private readonly object _diagLock = new();
+    private long _diagVersion;
+    private int _currentRecoveryAttempt;
+    private string? _lastRecoveryFailureCode;
+    private AdapterError? _lastError; // sanitized (Message cleared) — the last recorded error, or null
+    private AdapterState _priorState = AdapterState.Created;                          // state before the last transition
+    private SparkplugProtocolState _priorProtocol = SparkplugProtocolState.Stopped;
+    private AdapterState _lastObservedState = AdapterState.Created;                   // most recent state (change detector)
+    private SparkplugProtocolState _lastObservedProtocol = SparkplugProtocolState.Stopped;
+    private DateTimeOffset _lastStateChangeAt;
+    private string _lastTransitionReasonCode = "created";
+    private volatile SparkplugActorDiagnostics? _diagnostics;
+
     /// <summary>Construct a lifecycle-only actor (no identity store — cannot begin a session). Test/internal.</summary>
     /// <param name="instanceId">The sink instance id (non-empty).</param>
     internal SparkplugSessionActor(string instanceId)
@@ -193,9 +231,10 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
             var validation = SparkplugSinkConfigurationValidator.Validate(config);
             if (!validation.IsValid)
             {
-                SetFaulted();
                 var issue = validation.Errors[0];
-                throw AdapterException.Configuration(issue.Code, issue.Message);
+                var configError = AdapterException.Configuration(issue.Code, issue.Message);
+                SetFaulted(configError.Error); // records the sanitized code/category only, never the message
+                throw configError;
             }
 
             _config = (SparkplugSinkConfiguration)config;
@@ -229,9 +268,9 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
 
                 SetAdapterState(AdapterState.Running);
             }
-            catch
+            catch (Exception ex)
             {
-                SetFaulted();
+                SetFaulted(AsAdapterError(ex));
                 throw;
             }
         }
@@ -295,44 +334,87 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
             return Task.FromCanceled<AdapterHealth>(cancellationToken);
         }
 
-        var snapshot = _snapshot;
-        var active = _activeSession;
-        var protocol = snapshot.ProtocolState;
-        var hasSession = active is not null;
+        // One coherent, versioned read (plan v3 §8): every field below comes from the same snapshot, so
+        // health can never report a field combination that never existed.
+        var diag = DiagnosticsSnapshot;
 
-        var level = snapshot.State switch
+        var level = diag.State switch
         {
             AdapterState.Failed => HealthLevel.Unhealthy,
-            AdapterState.Running when protocol is SparkplugProtocolState.Stopped or SparkplugProtocolState.Live
+            AdapterState.Running when diag.ProtocolState is SparkplugProtocolState.Stopped or SparkplugProtocolState.Live
                 => HealthLevel.Healthy,
             AdapterState.Running => HealthLevel.Degraded,
             AdapterState.Degraded => HealthLevel.Degraded,
             _ => HealthLevel.Unknown,
         };
 
+        // Stable metric keys — Studio, support bundles, and K4 meters may depend on them. Redacted by
+        // construction: only ids/generations/bdSeq/epoch/counts/timestamps/sanitized-codes, never a
+        // credential, endpoint, client id, topic, payload byte, or metric value.
         var metrics = new Dictionary<string, object>
         {
-            ["protocolState"] = protocol.ToString(),
-            ["hasSession"] = hasSession,
-            ["lastIssuedGeneration"] = _lastIssuedConnectionGeneration,
+            ["protocolState"] = diag.ProtocolState.ToString(),
+            ["hasSession"] = diag.HasSession,
+            ["lastIssuedGeneration"] = diag.LastIssuedGeneration,
+            ["diagnosticsVersion"] = diag.Version,
+            ["lastTransitionReasonCode"] = diag.LastTransitionReasonCode,
+            ["previousState"] = diag.PreviousState.ToString(),
+            ["previousProtocolState"] = diag.PreviousProtocolState.ToString(),
+            ["terminalDisposed"] = diag.TerminalDisposed,
+            ["suspectTransport"] = diag.SuspectTransport,
+            ["pendingRebirth"] = diag.PendingRebirth,
+            ["currentRecoveryAttempt"] = diag.CurrentRecoveryAttempt,
+            ["recoveryAttemptBudget"] = diag.RecoveryAttemptBudget,
+            ["staleDisconnectCallbacks"] = diag.StaleDisconnectCallbacks,
+            ["staleNodeCommandCallbacks"] = diag.StaleNodeCommandCallbacks,
+            ["rebirthRequestsQueued"] = diag.RebirthRequestsQueued,
+            ["rebirthRequestsCoalesced"] = diag.RebirthRequestsCoalesced,
+            ["healthyRebirths"] = diag.HealthyRebirths,
+            ["transportRecoveryStarts"] = diag.TransportRecoveryStarts,
+            ["transportRecoveryAttempts"] = diag.TransportRecoveryAttempts,
+            ["transportRecoverySuccesses"] = diag.TransportRecoverySuccesses,
+            ["transportRecoveryExhaustions"] = diag.TransportRecoveryExhaustions,
+            ["publishFailures"] = diag.PublishFailures,
+            ["birthFailures"] = diag.BirthFailures,
+            ["deathPublishFailures"] = diag.DeathPublishFailures,
         };
-        if (active is not null)
-        {
-            metrics["sessionId"] = active.SessionId.Value;
-            metrics["epoch"] = active.Epoch.Value;
-            metrics["connectionGeneration"] = active.TransportGeneration;
-            metrics["bdSeq"] = active.BdSeq.Value;
-        }
+        if (diag.HasSession)
+        {
+            metrics["sessionId"] = diag.SessionId!.Value;
+            metrics["epoch"] = diag.Epoch!.Value;
+            metrics["connectionGeneration"] = diag.ConnectionGeneration!.Value;
+            metrics["bdSeq"] = diag.BdSeq!.Value;
+            metrics["nextSeq"] = diag.NextSeq!.Value;
+        }
+        if (diag.PendingRebirthReason is { } reason) { metrics["pendingRebirthReason"] = reason; }
+        if (diag.LastRecoveryFailureCode is { } recoveryFailure) { metrics["lastRecoveryFailureCode"] = recoveryFailure; }
+        if (diag.LastErrorCode is { } errorCode) { metrics["lastErrorCode"] = errorCode; }
+        if (diag.LastErrorCategory is { } errorCategory) { metrics["lastErrorCategory"] = errorCategory; }
+        AddInstant(metrics, "lastSuccessfulBirthAt", diag.LastSuccessfulBirthAt);
+        AddInstant(metrics, "lastDataPublishAt", diag.LastDataPublishAt);
+        AddInstant(metrics, "lastRebirthRequestAt", diag.LastRebirthRequestAt);
+        AddInstant(metrics, "lastErrorAt", diag.LastErrorAt);
 
         return Task.FromResult(new AdapterHealth
         {
-            State = snapshot.State,
+            State = diag.State,
             Level = level,
-            CheckedAt = DateTime.UtcNow,
+            CheckedAt = _clock().UtcDateTime,
+            LastSuccessAt = diag.LastSuccessfulBirthAt?.UtcDateTime,
+            LastError = diag.LastError,
             Metrics = metrics,
+            Detail = diag.LastTransitionReasonCode,
         });
     }
 
+    private static void AddInstant(IDictionary<string, object> metrics, string key, DateTimeOffset? instant)
+    {
+        if (instant is { } value)
+        {
+            metrics[key] = value.UtcDateTime;
+        }
+    }
+
     /// <summary>
     /// Begin a new replay session (slice 4). See the file header for the full ordered contract.
     /// </summary>
@@ -356,9 +438,9 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         {
             throw; // disposed: fail closed without faulting/mutating the terminal state
         }
-        catch
+        catch (Exception ex)
         {
-            SetFaulted(); // promote nothing; the driver faults the route; the previous epoch stands
+            SetFaulted(AsAdapterError(ex)); // promote nothing; the driver faults the route; the previous epoch stands
             throw;
         }
         finally
@@ -477,6 +559,7 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
                 .ConfigureAwait(false);
             if (!published)
             {
+                Interlocked.Increment(ref _birthFailures); // NBIRTH publish failed (plan v3 §8)
                 throw BirthPublishFailed();
             }
 
@@ -537,6 +620,7 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
 
         _activeSession = candidate; // volatile publish
         _nextSeq = 1;               // NBIRTH consumed seq 0
+        Interlocked.Exchange(ref _lastSuccessfulBirthAtTicks, _clock().UtcTicks); // NBIRTH succeeded (plan v3 §8)
         // Normalize the diagnostic substate: a candidate that became suspect between promotion and
         // publication reports Suspect, not Replaying (pass-1 r3 carry-forward).
         SetProtocolState(candidate.Handoff.SuspectAfterPromotion
@@ -572,9 +656,9 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
             {
                 throw;
             }
-            catch
+            catch (Exception ex)
             {
-                SetFaulted();
+                SetFaulted(AsAdapterError(ex));
                 throw;
             }
         }
@@ -633,6 +717,7 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
             .ConfigureAwait(false);
         if (!published && !session.Handoff.SuspectAfterPromotion)
         {
+            Interlocked.Increment(ref _birthFailures); // a genuine local NBIRTH failure (plan v3 §8)
             throw BirthPublishFailed(); // a genuine local NBIRTH failure with no transport loss is fatal (§4.5)
         }
 
@@ -663,6 +748,8 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         // against the new epoch.
         _activeSession = session with { Epoch = rebirth.Epoch, Manifest = resolved, Baseline = baseline };
         _nextSeq = 1; // the re-birth NBIRTH consumed seq 0
+        Interlocked.Increment(ref _healthyRebirths); // a healthy in-place NBIRTH re-announcement committed (plan v3 §8)
+        Interlocked.Exchange(ref _lastSuccessfulBirthAtTicks, _clock().UtcTicks);
 
         // Finish the commit (RebirthCommitting -> Active, or leave Suspect if a drop raced) and drain any
         // fresh episode a control event opened during the commit — against the new authoritative epoch.
@@ -699,6 +786,7 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
                 "a transport recovery is already in flight for this actor (single-recovery invariant).");
         }
 
+        Interlocked.Increment(ref _transportRecoveryStarts); // one recovery episode started (token claimed)
         try
         {
             ValidateRecoveryOwnership(token); // a disposal that won BEFORE we claimed the token aborts here
@@ -726,19 +814,28 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
             {
                 ValidateRecoveryOwnership(token); // and before EACH attempt — no CONNECT/bdSeq/generation after loss
 
+                // One complete CONNECT/SUBSCRIBE/NBIRTH attempt is about to run — count it and surface the
+                // current ordinal within this episode (distinct from the lifetime attempts tally, plan v3 §8).
+                Interlocked.Increment(ref _transportRecoveryAttempts);
+                RepublishDiagnostics("recovery-attempt", currentRecoveryAttempt: attempt);
+
                 try
                 {
                     var candidate = await AttemptConnectionAsync(
                         prepared, sessionId, epoch, previous.RouteId, previous.Host, token, cancellationToken).ConfigureAwait(false);
                     await PromoteAndDrainAsync(candidate).ConfigureAwait(false);
+                    Interlocked.Increment(ref _transportRecoverySuccesses);
+                    RepublishDiagnostics("recovery-success", currentRecoveryAttempt: 0);
                     return; // recovered within budget — no route fault
                 }
                 catch (Exception ex)
                     when (ex is not OperationCanceledException && attempt < maxAttempts && IsRetryableEstablishmentFailure(ex))
                 {
                     // A retryable transport failure consumed this attempt's distinct generation + bdSeq;
-                    // back off (gate released) and retry. A superseding lifecycle call during the delay
-                    // throws (aborts) here. A non-retryable/last-attempt failure propagates → terminal fault.
+                    // back off (gate released) and retry. Reflect the honest substate during the delay window
+                    // (the failed attempt left it at Connecting/Subscribing/Birthing). A superseding lifecycle
+                    // call during the delay throws (aborts) here; a non-retryable/last-attempt failure faults.
+                    SetProtocolState(SparkplugProtocolState.RecoveringTransport);
                     await BackoffWithGateReleasedAsync(TimeSpan.FromMilliseconds(delayMs), token, cancellationToken).ConfigureAwait(false);
                     delayMs = (int)Math.Min((long)delayMs * 2, maxDelayMs);
                 }
@@ -756,11 +853,21 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
 
             throw;
         }
+        catch (Exception ex) when (IsRetryableEstablishmentFailure(ex))
+        {
+            // A RETRYABLE transport failure reaching here means the budget was exhausted (had attempts
+            // remained, the loop's inner filter would have retried it) → terminal fault (plan v3 §8 envelope).
+            // A non-retryable/fatal-prep failure does not match this filter and propagates without counting.
+            Interlocked.Increment(ref _transportRecoveryExhaustions);
+            RepublishDiagnostics("recovery-exhausted", AsAdapterError(ex), recoveryFailureCode: AsAdapterError(ex)?.Code);
+            throw;
+        }
         finally
         {
             // Clear ONLY if the token is still ours — a superseding lifecycle call may already have replaced
             // or nulled it, and we must not stomp a newer owner.
             Interlocked.CompareExchange(ref _activeRecoveryToken, null, token);
+            RepublishDiagnostics("recovery-ended", currentRecoveryAttempt: 0); // no episode running once we exit
         }
     }
 
@@ -802,6 +909,8 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         {
             if (droppedGeneration != generation)
             {
+                Interlocked.Increment(ref _staleDisconnectCallbacks); // ignore + diagnostic counter (plan v3 §7)
+                RepublishDiagnostics("stale-disconnect");
                 return Task.CompletedTask; // stale generation: ignore authoritatively (the generation gate)
             }
 
@@ -822,6 +931,8 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         {
             if (receivedGeneration != generation)
             {
+                Interlocked.Increment(ref _staleNodeCommandCallbacks); // ignore + diagnostic counter (plan v3 §7)
+                RepublishDiagnostics("stale-ncmd");
                 return Task.CompletedTask; // stale generation: ignore
             }
 
@@ -843,8 +954,21 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
     private async Task DrainRebirthAsync(AttemptHandoff handoff)
     {
         var session = _activeSession;
-        if (session is null || !ReferenceEquals(session.Handoff, handoff) || !handoff.TryTakeForQueue())
+        if (session is null || !ReferenceEquals(session.Handoff, handoff))
         {
+            return; // authority not yet published or superseded — the drain runs again after publication
+        }
+
+        if (!handoff.TryTakeForQueue())
+        {
+            // Could not claim the queue. If the episode is still pending, a Core request is already in flight
+            // (or committing) for it → this signal coalesces into that one, no second Core request (plan v3 §7).
+            if (handoff.RebirthPending)
+            {
+                Interlocked.Increment(ref _rebirthRequestsCoalesced);
+                RepublishDiagnostics("rebirth-coalesced");
+            }
+
             return;
         }
 
@@ -861,6 +985,9 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         {
             var request = RebirthRequest.Create(session.SessionId, session.Epoch, reason, detail);
             await session.Host.RequestRebirthAsync(request, CancellationToken.None).ConfigureAwait(false);
+            Interlocked.Increment(ref _rebirthRequestsQueued); // exactly one Core request queued for this episode
+            Interlocked.Exchange(ref _lastRebirthRequestAtTicks, _clock().UtcTicks);
+            RepublishDiagnostics("rebirth-queued");
         }
         catch
         {
@@ -899,9 +1026,9 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
             {
                 throw; // cancellation is not a fault
             }
-            catch
+            catch (Exception ex)
             {
-                SetFaulted(); // a hard fail-closed violation (no session, session/epoch mismatch, material mutation)
+                SetFaulted(AsAdapterError(ex)); // a hard fail-closed violation (no session, session/epoch mismatch, material mutation)
                 throw;
             }
         }
@@ -1006,12 +1133,16 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
             session, SparkplugTopicFactory.NData(NodeIdentity()), payload, cancellationToken).ConfigureAwait(false);
         if (!published)
         {
+            Interlocked.Increment(ref _publishFailures); // observable/uncertain DATA send failure (plan v3 §8)
             return await FailWithRebirthAsync(
                 session, RebirthReason.Other, "the DATA batch did not complete at the local transport boundary.",
                 latchSuspect: true, started, cancellationToken).ConfigureAwait(false);
         }
 
         _nextSeq = (_nextSeq + 1) & 0xFF; // advance ONLY after local success
+        // Hot path: update the liveness timestamp cheaply (Interlocked only) — the full coherent record is
+        // rebuilt lazily when diagnostics/health is actually READ (infrequent), not on every DATA batch.
+        Interlocked.Exchange(ref _lastDataPublishAtTicks, _clock().UtcTicks); // liveness (plan v3 §8)
         foreach (var (key, state) in observed)
         {
             session.Baseline.Observe(key, state); // dirtySinceBirth — reuses the pre-built states (no fallible work post-send)
@@ -1045,9 +1176,9 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
             {
                 throw;
             }
-            catch
+            catch (Exception ex)
             {
-                SetFaulted();
+                SetFaulted(AsAdapterError(ex));
                 throw;
             }
         }
@@ -1141,6 +1272,7 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
                 session, SparkplugTopicFactory.NData(NodeIdentity()), payload, cancellationToken).ConfigureAwait(false);
             if (!published)
             {
+                Interlocked.Increment(ref _publishFailures); // final-update DATA send failure (plan v3 §8)
                 await RequestRebirthAsync(
                     session, RebirthReason.Other, "the final update did not complete at the local transport boundary.",
                     latchSuspect: true, cancellationToken).ConfigureAwait(false);
@@ -1148,6 +1280,7 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
             }
 
             _nextSeq = (_nextSeq + 1) & 0xFF;
+            Interlocked.Exchange(ref _lastDataPublishAtTicks, _clock().UtcTicks);
         }
 
         // Deterministic race barrier immediately before the atomic Live commit (review r1 B4).
@@ -1227,6 +1360,11 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
                 // Uncertain NDEATH (false/exception/in-transport cancellation) → do NOT clean-disconnect.
             }
 
+            if (!deathConfirmed)
+            {
+                Interlocked.Increment(ref _deathPublishFailures); // NDEATH unconfirmed → Will-only end (plan v3 §8)
+            }
+
             if (deathConfirmed)
             {
                 try { await session.Transport.DisconnectAsync(cancellationToken).ConfigureAwait(false); }
@@ -1282,6 +1420,7 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
             {
                 await RetireActiveSessionAsync().ConfigureAwait(false);
                 _snapshot = new ActorSnapshot(AdapterState.Stopped, SparkplugProtocolState.Stopped); // terminal
+                RepublishDiagnostics("disposed"); // reflect the terminal Stopped/Stopped + TerminalDisposed
             }
             finally
             {
@@ -1498,8 +1637,11 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
             Retryable = false,
         });
 
-    private void SetProtocolState(SparkplugProtocolState protocol) =>
+    private void SetProtocolState(SparkplugProtocolState protocol)
+    {
         _snapshot = _snapshot with { ProtocolState = protocol };
+        RepublishDiagnostics($"protocol:{protocol}");
+    }
 
     private void SetAdapterState(AdapterState target)
     {
@@ -1511,6 +1653,7 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         }
 
         _snapshot = _snapshot with { State = target };
+        RepublishDiagnostics($"state:{target}");
     }
 
     private void SetState(AdapterState target, SparkplugProtocolState protocolState)
@@ -1523,18 +1666,138 @@ public sealed class SparkplugSessionActor : IAsyncDisposable
         }
 
         _snapshot = new ActorSnapshot(target, protocolState);
+        RepublishDiagnostics($"state:{target}/{protocolState}");
     }
 
-    private void SetFaulted()
+    // Extract a sanitized AdapterError from a caught exception for diagnostics (last-error code/category).
+    // Only the structured error carries safe fields; a raw exception's Message is NOT recorded (it could
+    // echo an endpoint/credential), so a non-AdapterException yields null and only the fault state is set.
+    private static AdapterError? AsAdapterError(Exception ex) => (ex as AdapterException)?.Error;
+
+    private void SetFaulted(AdapterError? error = null)
     {
         if (AdapterStateTransitions.IsAllowed(_snapshot.State, AdapterState.Failed))
         {
             _snapshot = new ActorSnapshot(AdapterState.Failed, SparkplugProtocolState.Faulted);
         }
+
+        RepublishDiagnostics("faulted", error);
     }
 
     private sealed record ActorSnapshot(AdapterState State, SparkplugProtocolState ProtocolState);
 
+    private static DateTimeOffset? TicksToOffset(long ticks) =>
+        ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
+
+    /// <summary>
+    /// The current coherent diagnostic snapshot (health/test accessor). Rebuilt on read so counters and
+    /// liveness timestamps updated on the hot path (Interlocked-only) surface freshly, without paying the
+    /// full-record rebuild on every DATA batch. A read does not count as a state transition (the
+    /// "immediately preceding transition" fields are preserved).
+    /// </summary>
+    internal SparkplugActorDiagnostics DiagnosticsSnapshot
+    {
+        get
+        {
+            RepublishDiagnostics("observed");
+            return _diagnostics!;
+        }
+    }
+
+    // Rebuild and atomically publish the coherent, versioned diagnostic snapshot (plan v3 §8). All persistent
+    // "current-state" diagnostic fields are mutated ONLY here, under _diagLock, and the record is captured from
+    // one consistent read of the volatile authority (_snapshot, _activeSession) plus the Interlocked counters —
+    // so a reader of _diagnostics never sees a torn combination, and Version strictly increases. Optional
+    // parameters carry forward when null (a counter-only republish keeps the last error / recovery progress).
+    private void RepublishDiagnostics(
+        string reasonCode,
+        AdapterError? newError = null,
+        int? currentRecoveryAttempt = null,
+        string? recoveryFailureCode = null)
+    {
+        lock (_diagLock)
+        {
+            var snapshot = _snapshot;      // volatile read: coarse+fine are atomic together
+            var active = _activeSession;   // volatile read: the immutable authority
+            var now = _clock();
+
+            if (currentRecoveryAttempt is { } attempt) { _currentRecoveryAttempt = attempt; }
+            if (recoveryFailureCode is not null) { _lastRecoveryFailureCode = recoveryFailureCode; }
+            if (newError is { } error)
+            {
+                // Store a SANITIZED copy: keep only the safe structured fields (code/category/retryable),
+                // clear the Message so no endpoint/credential/payload detail can leak through diagnostics.
+                _lastError = new AdapterError
+                {
+                    Code = error.Code,
+                    Category = error.Category,
+                    Message = string.Empty,
+                    Retryable = error.Retryable,
+                };
+                Interlocked.Exchange(ref _lastErrorAtTicks, now.UtcTicks);
+            }
+
+            // Update the "immediately preceding transition" fields only on an ACTUAL state change.
+            if (snapshot.State != _lastObservedState || snapshot.ProtocolState != _lastObservedProtocol)
+            {
+                _priorState = _lastObservedState;
+                _priorProtocol = _lastObservedProtocol;
+                _lastObservedState = snapshot.State;
+                _lastObservedProtocol = snapshot.ProtocolState;
+                _lastStateChangeAt = now;
+                _lastTransitionReasonCode = reasonCode;
+            }
+
+            var handoff = active?.Handoff;
+            var pending = handoff?.RebirthPending ?? false;
+
+            _diagnostics = new SparkplugActorDiagnostics
+            {
+                Version = Interlocked.Increment(ref _diagVersion),
+                State = snapshot.State,
+                ProtocolState = snapshot.ProtocolState,
+                PreviousState = _priorState,
+                PreviousProtocolState = _priorProtocol,
+                LastStateChangeAt = _lastStateChangeAt,
+                LastTransitionReasonCode = _lastTransitionReasonCode,
+                TerminalDisposed = DisposalWon,
+                HasSession = active is not null,
+                SessionId = active?.SessionId.Value,
+                Epoch = active?.Epoch.Value,
+                RouteId = active?.RouteId,
+                ConnectionGeneration = active?.TransportGeneration,
+                LastIssuedGeneration = Interlocked.Read(ref _lastIssuedConnectionGeneration),
+                BdSeq = active is null ? null : active.BdSeq.Value,
+                NextSeq = active is null ? null : Volatile.Read(ref _nextSeq),
+                SuspectTransport = handoff?.SuspectAfterPromotion ?? false,
+                PendingRebirth = pending,
+                PendingRebirthReason = pending ? handoff!.PendingReason.ToString() : null,
+                CurrentRecoveryAttempt = _currentRecoveryAttempt,
+                RecoveryAttemptBudget = _config is null ? 0 : Math.Max(1, _config.TransportRecoveryMaxAttempts),
+                LastRecoveryFailureCode = _lastRecoveryFailureCode,
+                LastSuccessfulBirthAt = TicksToOffset(Interlocked.Read(ref _lastSuccessfulBirthAtTicks)),
+                LastDataPublishAt = TicksToOffset(Interlocked.Read(ref _lastDataPublishAtTicks)),
+                LastRebirthRequestAt = TicksToOffset(Interlocked.Read(ref _lastRebirthRequestAtTicks)),
+                LastErrorAt = TicksToOffset(Interlocked.Read(ref _lastErrorAtTicks)),
+                LastErrorCode = _lastError?.Code,
+                LastErrorCategory = _lastError?.Category.ToString(),
+                LastError = _lastError,
+                StaleDisconnectCallbacks = Interlocked.Read(ref _staleDisconnectCallbacks),
+                StaleNodeCommandCallbacks = Interlocked.Read(ref _staleNodeCommandCallbacks),
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
+        }
+    }
+
     /// <summary>The single immutable session authority, promoted atomically on NBIRTH success.</summary>
     private sealed record ActiveSession(
         ISparkplugMqttTransport Transport,
diff --git a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorRebirthTests.cs b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorRebirthTests.cs
index 5cbb3a5..39a2656 100644
--- a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorRebirthTests.cs
+++ b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorRebirthTests.cs
@@ -1258,6 +1258,255 @@ public sealed class SparkplugSessionActorRebirthTests : IDisposable
         actor.HasSession.Should().BeTrue();
     }
 
+    // ==== Slice 7: health / diagnostics / counters / redaction (plan v3 §8, §11) ====
+
+    [Fact]
+    public async Task Health_ReadyNoSession_IsHealthy()
+    {
+        var fake = new FakeTransport();
+        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fake, () => Clock, InstantDelay);
+        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
+        await actor.StartAsync(CancellationToken.None);
+
+        var health = await actor.CheckHealthAsync(CancellationToken.None);
+
+        health.Level.Should().Be(HealthLevel.Healthy);
+        health.State.Should().Be(AdapterState.Running);
+        health.Metrics!["hasSession"].Should().Be(false);
+        health.Metrics["protocolState"].Should().Be(SparkplugProtocolState.Stopped.ToString());
+    }
+
+    [Fact]
+    public async Task Health_Live_IsHealthy()
+    {
+        var (actor, _, _) = await Born();
+        await actor.CompleteCatchUpAsync(Cutover(), CancellationToken.None); // cutover → Live
+
+        var health = await actor.CheckHealthAsync(CancellationToken.None);
+
+        health.Level.Should().Be(HealthLevel.Healthy);
+        health.Metrics!["protocolState"].Should().Be(SparkplugProtocolState.Live.ToString());
+        health.Metrics["hasSession"].Should().Be(true);
+    }
+
+    [Fact]
+    public async Task Health_ReplayingBeforeCutover_IsDegraded()
+    {
+        var (actor, _, _) = await Born(); // promoted, Replaying — an active transitional session
+
+        var health = await actor.CheckHealthAsync(CancellationToken.None);
+
+        health.Level.Should().Be(HealthLevel.Degraded); // active session not yet Live
+        health.State.Should().Be(AdapterState.Running);
+    }
+
+    [Fact]
+    public async Task Health_AfterFatalBirthFailure_IsUnhealthy_WithSanitizedError()
+    {
+        var (actor, fake, _) = await Born();
+        fake.PublishReturnsFalse = true; // the healthy-rebirth NBIRTH will fail locally (fatal)
+
+        await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None)).Should().ThrowAsync<Exception>();
+
+        var health = await actor.CheckHealthAsync(CancellationToken.None);
+        health.Level.Should().Be(HealthLevel.Unhealthy);
+        health.State.Should().Be(AdapterState.Failed);
+        health.Metrics!["birthFailures"].Should().Be(1L);
+        health.LastError.Should().NotBeNull();
+        health.LastError!.Message.Should().BeEmpty();                 // sanitized — no message leaks
+        health.LastError.Code.Should().Be(SparkplugErrors.BirthPublishFailed);
+        health.Metrics["lastErrorCode"].Should().Be(SparkplugErrors.BirthPublishFailed);
+    }
+
+    [Fact]
+    public async Task Diagnostics_SessionFields_CoherentWhenBorn()
+    {
+        var (actor, _, _) = await Born();
+
+        var diag = actor.DiagnosticsSnapshot;
+
+        diag.HasSession.Should().BeTrue();
+        diag.SessionId.Should().Be(1);
+        diag.Epoch.Should().Be(0);
+        diag.RouteId.Should().Be("route-1");
+        diag.ConnectionGeneration.Should().Be(actor.CurrentGeneration);
+        diag.BdSeq.Should().Be(actor.CurrentBdSeq.Value);
+        diag.NextSeq.Should().Be(1); // NBIRTH consumed seq 0
+    }
+
+    [Fact]
+    public async Task Diagnostics_Version_IsStrictlyMonotonicAcrossTransitions()
+    {
+        var fake = new FakeTransport();
+        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fake, () => Clock, InstantDelay);
+        var v0 = actor.DiagnosticsSnapshot.Version;
+        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
+        var v1 = actor.DiagnosticsSnapshot.Version;
+        await actor.StartAsync(CancellationToken.None);
+        var v2 = actor.DiagnosticsSnapshot.Version;
+        await actor.BeginReplaySessionAsync(Start(new CapturingHost()), CancellationToken.None);
+        var v3 = actor.DiagnosticsSnapshot.Version;
+
+        v1.Should().BeGreaterThan(v0);
+        v2.Should().BeGreaterThan(v1);
+        v3.Should().BeGreaterThan(v2);
+    }
+
+    [Fact]
+    public async Task Diagnostics_LastTransition_ReflectsPrecedingStateChange()
+    {
+        var (actor, _, _) = await Born();
+
+        var diag = actor.DiagnosticsSnapshot;
+
+        diag.LastStateChangeAt.Should().Be(Clock);              // injected clock, deterministic
+        diag.PreviousProtocolState.Should().NotBe(diag.ProtocolState); // an actual transition preceded this state
+        diag.LastTransitionReasonCode.Should().NotBeNullOrEmpty();
+    }
+
+    [Fact]
+    public async Task Diagnostics_StaleDisconnectCallback_IncrementsCounter()
+    {
+        var (actor, fake, _) = await Born();
+
+        await fake.RaiseDisconnected(actor.CurrentGeneration + 99); // a retired client's delayed callback
+
+        actor.DiagnosticsSnapshot.StaleDisconnectCallbacks.Should().Be(1);
+        (await actor.CheckHealthAsync(CancellationToken.None)).Metrics!["staleDisconnectCallbacks"].Should().Be(1L);
+    }
+
+    [Fact]
+    public async Task Diagnostics_StaleNodeCommandCallback_IncrementsCounter()
+    {
+        var (actor, fake, _) = await Born();
+
+        await fake.RaiseNodeCommand(actor.CurrentGeneration + 99, RebirthCommand());
+
+        actor.DiagnosticsSnapshot.StaleNodeCommandCallbacks.Should().Be(1);
+    }
+
+    [Fact]
+    public async Task Diagnostics_RebirthRequest_QueuedThenCoalesced()
+    {
+        var (actor, fake, _) = await Born();
+
+        await fake.RaiseDisconnected(actor.CurrentGeneration);                  // queues one Core request
+        await fake.RaiseNodeCommand(actor.CurrentGeneration, RebirthCommand()); // coalesces into the open episode
+
+        var diag = actor.DiagnosticsSnapshot;
+        diag.RebirthRequestsQueued.Should().Be(1);
+        diag.RebirthRequestsCoalesced.Should().Be(1);
+        diag.LastRebirthRequestAt.Should().Be(Clock);
+    }
+
+    [Fact]
+    public async Task Diagnostics_HealthyRebirth_IncrementsCounter_AndBirthTimestamp()
+    {
+        var (actor, _, _) = await Born();
+
+        await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None); // healthy in-place rebirth
+
+        var diag = actor.DiagnosticsSnapshot;
+        diag.HealthyRebirths.Should().Be(1);
+        diag.LastSuccessfulBirthAt.Should().Be(Clock);
+        diag.Epoch.Should().Be(1);
+    }
+
+    [Fact]
+    public async Task Diagnostics_TransportRecovery_CountsStartsAttemptsSuccesses()
+    {
+        var fake0 = new FakeTransport();
+        var failing = new FakeTransport { FailConnect = true }; // attempt 1 fails (retryable)
+        var good = new FakeTransport();
+        var fakes = new Queue<ISparkplugMqttTransport>(new ISparkplugMqttTransport[] { fake0, failing, good });
+        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fakes.Dequeue(), () => Clock, InstantDelay);
+        await actor.InitializeAsync(ValidConfig(), CancellationToken.None);
+        await actor.StartAsync(CancellationToken.None);
+        await actor.BeginReplaySessionAsync(Start(new CapturingHost()), CancellationToken.None);
+        await fake0.RaiseDisconnected(actor.CurrentGeneration);
+
+        await actor.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None); // recovers on attempt 2
+
+        var diag = actor.DiagnosticsSnapshot;
+        diag.TransportRecoveryStarts.Should().Be(1);
+        diag.TransportRecoveryAttempts.Should().Be(2);   // one failed + one good, lifetime
+        diag.TransportRecoverySuccesses.Should().Be(1);
+        diag.TransportRecoveryExhaustions.Should().Be(0);
+        diag.CurrentRecoveryAttempt.Should().Be(0);      // no episode running after success
+    }
+
+    [Fact]
+    public async Task Diagnostics_TransportRecovery_Exhaustion_CountsExhaustionAndFaults()
+    {
+        var recording = new List<TimeSpan>();
+        var fake0 = new FakeTransport();
+        var actor = new SparkplugSessionActor(
+            "spb-1", NewStore(), () => fake0.Connected ? new FakeTransport { FailConnect = true } : fake0, () => Clock,
+            Recording(recording));
+        await actor.InitializeAsync(ValidConfig() with { TransportRecoveryMaxAttempts = 2 }, CancellationToken.None);
+        await actor.StartAsync(CancellationToken.None);
+        await actor.BeginReplaySessionAsync(Start(new CapturingHost()), CancellationToken.None);
+        await fake0.RaiseDisconnected(actor.CurrentGeneration);
+
+        await actor.Invoking(a => a.RebirthAsync(Rebirth(epoch: 1), CancellationToken.None)).Should().ThrowAsync<Exception>();
+
+        var diag = actor.DiagnosticsSnapshot;
+        diag.TransportRecoveryExhaustions.Should().Be(1);
+        diag.CurrentRecoveryAttempt.Should().Be(0);            // reset after the episode ends
+        diag.State.Should().Be(AdapterState.Failed);
+        diag.LastRecoveryFailureCode.Should().NotBeNullOrEmpty();
+        (await actor.CheckHealthAsync(CancellationToken.None)).Level.Should().Be(HealthLevel.Unhealthy);
+    }
+
+    [Fact]
+    public async Task Diagnostics_CurrentRecoveryAttempt_TracksOrdinalDuringBackoff()
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
+        await entered.Task; // parked in backoff after attempt 1 failed
+
+        var during = actor.DiagnosticsSnapshot;
+        during.CurrentRecoveryAttempt.Should().Be(1);
+        during.ProtocolState.Should().Be(SparkplugProtocolState.RecoveringTransport);
+        (await actor.CheckHealthAsync(CancellationToken.None)).Level.Should().Be(HealthLevel.Degraded);
+
+        release.SetResult();
+        await rebirth; // recovers on attempt 2
+        actor.DiagnosticsSnapshot.CurrentRecoveryAttempt.Should().Be(0);
+    }
+
+    [Fact]
+    public async Task Diagnostics_HealthSnapshot_NeverExposesCredentialsOrEndpoint()
+    {
+        const string secret = "sup3r-s3cret-pw";
+        var fake = new FakeTransport();
+        var config = ValidConfig() with { BrokerHost = "broker.internal.example", Username = "operator", Password = secret };
+        var actor = new SparkplugSessionActor("spb-1", NewStore(), () => fake, () => Clock, InstantDelay);
+        await actor.InitializeAsync(config, CancellationToken.None);
+        await actor.StartAsync(CancellationToken.None);
+        await actor.BeginReplaySessionAsync(Start(new CapturingHost()), CancellationToken.None);
+
+        var health = await actor.CheckHealthAsync(CancellationToken.None);
+
+        var rendered = string.Join("|", health.Metrics!.Select(kv => $"{kv.Key}={kv.Value}"))
+            + "|" + (health.LastError?.Message ?? "") + "|" + (health.Detail ?? "");
+        rendered.Should().NotContain(secret);
+        rendered.Should().NotContain("operator");
+        rendered.Should().NotContain("broker.internal.example");
+    }
+
     // ==== Helpers ====
 
     private static Func<TimeSpan, CancellationToken, Task> Recording(List<TimeSpan> sink) =>
diff --git a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorReplayTests.cs b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorReplayTests.cs
index 52ae0db..a7e78af 100644
--- a/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorReplayTests.cs
+++ b/tests/ElpisEdgeConnect.Sinks.SparkplugB.Tests/Session/SparkplugSessionActorReplayTests.cs
@@ -66,6 +66,7 @@ public sealed class SparkplugSessionActorReplayTests : IDisposable
         result.Success.Should().BeTrue();
         result.AcceptedCount.Should().Be(1);
         actor.NextSeq.Should().Be(2); // seq 1 consumed by this NDATA
+        actor.DiagnosticsSnapshot.LastDataPublishAt.Should().Be(Clock); // slice 7: liveness timestamp set on success
 
         var expected = SparkplugPayloadEncoder.EncodeNData(
             SparkplugSequenceNumber.Create(1), Clock, new[] { Sample("srcA", 2) },
@@ -132,6 +133,7 @@ public sealed class SparkplugSessionActorReplayTests : IDisposable
         actor.NextSeq.Should().Be(1);            // send failure consumes no seq
         actor.CurrentSessionSuspect.Should().BeTrue();
         host.Requests.Should().ContainSingle().Which.Reason.Should().Be(RebirthReason.Other);
+        actor.DiagnosticsSnapshot.PublishFailures.Should().Be(1); // slice 7: the DATA send failure is counted
     }
 
     [Fact]
```
