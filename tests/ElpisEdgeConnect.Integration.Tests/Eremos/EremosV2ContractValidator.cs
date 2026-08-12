// ============================================================================
// File: Eremos/EremosV2ContractValidator.cs
// Purpose: Gate-by-gate measurement methodology per v2 plan §4. Each gate
//          is its own method returning a GateResult. The validator
//          assembles them into a RevalidationReport.
//
//          Mock-fallback path:
//            * Contract gates (3, 4, 2): measured against the mock
//              subscriber's receive log.
//            * Resilience gates (1, 5, 8): measured against gateway-side
//              diagnostics + broker-side outage injection.
//            * Real-EREMOS-only gates (6, 7): explicitly SKIPPED with
//              "real-EREMOS-only — running mock-fallback path" reason.
//              The v2 plan §4.3 requires explicit skip reasons rather
//              than silent passes.
//
// Reference: docs/sessions/2026-05-21-eremos-v2-revalidation-plan-v2.md §4
// ============================================================================

using System.Collections.Generic;
using System.Linq;

namespace ElpisEdgeConnect.Integration.Tests.Eremos;

/// <summary>
/// Roll-up of all gate evaluations + the path taken (real EREMOS V2
/// instance vs mock fallback). Used to assemble the
/// <c>docs/contracts/eremos-v2-revalidation.md</c> snapshot.
/// </summary>
public sealed record RevalidationReport(
    string Path,
    IReadOnlyList<GateResult> Gates)
{
    /// <summary>True iff all non-skipped gates passed.</summary>
    public bool AllPass => Gates.All(g => g.Outcome != GateOutcome.Fail);

    /// <summary>Failed gates only.</summary>
    public IReadOnlyList<GateResult> Failed => Gates.Where(g => g.Outcome == GateOutcome.Fail).ToList();

    /// <summary>Skipped gates only.</summary>
    public IReadOnlyList<GateResult> Skipped => Gates.Where(g => g.Outcome == GateOutcome.Skipped).ToList();
}

/// <summary>
/// Encapsulates the measurement methodology for all 8 gates. Stateless;
/// each method receives the captured observations + thresholds.
/// </summary>
public static class EremosV2ContractValidator
{
    /// <summary>
    /// Build a complete report under the mock-fallback path. Gates 6
    /// and 7 are explicitly SKIPPED — the mock subscriber cannot
    /// validate EREMOS V2's ingest pipeline or its replay-detection
    /// logic.
    /// </summary>
    public static RevalidationReport BuildMockFallbackReport(
        GateResult gate1Stability,
        GateResult gate2Parity,
        GateResult gate3Schema,
        GateResult gate4Determinism,
        GateResult gate5Reconnect,
        GateResult gate8Backpressure)
    {
        var skip6 = GateResult.Skipped(
            "Gate 6 — EREMOS ingestion (parsing drift)",
            GateBucket.RealEremosOnly,
            "real-EREMOS-only — running mock-fallback path. Gate 6 requires " +
            "EREMOS V2's ingest counter as the comparator; the mock subscriber " +
            "only sees what EdgeConnect emitted and cannot catch EREMOS-side " +
            "parsing regressions. See v2 plan §4.3.1.");

        var skip7 = GateResult.Skipped(
            "Gate 7 — Duplicate publish detection",
            GateBucket.RealEremosOnly,
            "real-EREMOS-only — running mock-fallback path. Gate 7's semantic " +
            "purpose is 'EREMOS V2 isn't replaying stale messages', which only " +
            "EREMOS V2's side can validate. The mock subscriber would only see " +
            "what EdgeConnect emitted. See v2 plan §4.3.2.");

        var gates = new List<GateResult>
        {
            gate3Schema,
            gate4Determinism,
            gate2Parity,
            gate1Stability,
            gate5Reconnect,
            gate8Backpressure,
            skip6,
            skip7,
        };

        return new RevalidationReport(Path: "mock-fallback", gates);
    }

    // ─── Contract gates ────────────────────────────────────────────────

    /// <summary>
    /// Gate 3 — Schema stability (PerTag-only scope). 100% pass rate
    /// against the PerTag scalar contract: UTF-8 string, no JSON
    /// wrapper, value-only emission.
    /// </summary>
    public static GateResult Gate3SchemaStability(IReadOnlyList<ReceivedMessage> messages)
    {
        if (messages.Count == 0)
        {
            return GateResult.Fail(
                "Gate 3 — Schema stability",
                GateBucket.Contract,
                "no PerTag messages received during the test window");
        }

        var violations = new List<string>();
        foreach (var m in messages)
        {
            // Reject JSON-wrapper shape (e.g. {"value":..., "ts":...}).
            // PerTag scalar contract emits the raw value as the UTF-8
            // payload — no wrapping object.
            var trimmed = m.Payload.TrimStart();
            if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            {
                violations.Add($"topic '{m.Topic}' has JSON-wrapper payload (first chars: '{Truncate(m.Payload, 32)}')");
            }

            // Reject non-UTF-8 encoding — if the bytes round-trip cleanly
            // through UTF-8 decode/encode they were valid UTF-8.
            var roundTrip = System.Text.Encoding.UTF8.GetBytes(m.Payload);
            if (!roundTrip.SequenceEqual(m.PayloadBytes))
            {
                violations.Add($"topic '{m.Topic}' payload is not valid UTF-8");
            }
        }

        if (violations.Count == 0)
        {
            return GateResult.Pass(
                "Gate 3 — Schema stability",
                GateBucket.Contract,
                $"100% pass rate over {messages.Count} messages. PerTag scalar contract honoured.");
        }

        return GateResult.Fail(
            "Gate 3 — Schema stability",
            GateBucket.Contract,
            $"{violations.Count} schema violation(s) out of {messages.Count} messages. " +
            $"First violations: {string.Join("; ", violations.Take(5))}");
    }

    /// <summary>
    /// Gate 4 — Topic determinism (regex + collision-free subgates per
    /// v2 plan §5.2 + §5.3).
    /// </summary>
    public static GateResult Gate4TopicDeterminism(
        IReadOnlyList<ReceivedMessage> messages,
        IEnumerable<string> sourceCanonicalTagPaths)
    {
        var nonMatching = messages
            .Select(m => m.Topic)
            .Distinct()
            .Where(t => !TopicShapeAnalyzer.IsValidTopicShape(t))
            .ToList();

        var collisions = TopicShapeAnalyzer.DetectCollisions(sourceCanonicalTagPaths);

        if (nonMatching.Count == 0 && collisions.Count == 0)
        {
            return GateResult.Pass(
                "Gate 4 — Topic determinism",
                GateBucket.Contract,
                $"all {messages.Select(m => m.Topic).Distinct().Count()} observed topics match " +
                $"the Phase 0 regex; zero canonical-tag-path collisions detected over " +
                $"{sourceCanonicalTagPaths.Count()} configured paths");
        }

        var evidence = new List<string>();
        if (nonMatching.Count > 0)
        {
            evidence.Add($"{nonMatching.Count} topic(s) outside Phase 0 regex: {string.Join(", ", nonMatching.Take(5))}");
        }
        if (collisions.Count > 0)
        {
            evidence.Add($"{collisions.Count} canonical-tag-path collision(s): " +
                string.Join("; ", collisions.Take(3).Select(c =>
                    $"MQTT segment '{c.MqttSegment}' produced by [{string.Join(", ", c.CollidingCanonicalPaths)}]")));
        }

        return GateResult.Fail(
            "Gate 4 — Topic determinism",
            GateBucket.Contract,
            string.Join(" | ", evidence));
    }

    /// <summary>
    /// Gate 2 — Emit/receive count parity per topic. gateway.emit_count
    /// must equal subscriber.receive_count for every topic over the
    /// test window.
    /// </summary>
    public static GateResult Gate2EmitReceiveParity(
        IReadOnlyDictionary<string, long> gatewayEmitCounts,
        IReadOnlyDictionary<string, long> subscriberReceiveCounts)
    {
        var allTopics = new HashSet<string>(gatewayEmitCounts.Keys);
        foreach (var t in subscriberReceiveCounts.Keys) allTopics.Add(t);

        var mismatches = new List<string>();
        foreach (var topic in allTopics)
        {
            var emit = gatewayEmitCounts.TryGetValue(topic, out var e) ? e : 0;
            var receive = subscriberReceiveCounts.TryGetValue(topic, out var r) ? r : 0;
            if (emit != receive)
            {
                mismatches.Add($"'{topic}' emit={emit} receive={receive}");
            }
        }

        if (mismatches.Count == 0)
        {
            return GateResult.Pass(
                "Gate 2 — Emit/receive count parity per topic",
                GateBucket.Contract,
                $"all {allTopics.Count} topic(s) have emit == receive over the test window");
        }

        return GateResult.Fail(
            "Gate 2 — Emit/receive count parity per topic",
            GateBucket.Contract,
            $"{mismatches.Count} topic(s) had emit != receive. " +
            $"First mismatches: {string.Join("; ", mismatches.Take(5))}");
    }

    // ─── Resilience gates ──────────────────────────────────────────────

    /// <summary>
    /// Gate 1 — MQTT stability. Gateway-side disconnect count primary;
    /// must be ≤3 over any 60-second window.
    /// </summary>
    public static GateResult Gate1MqttStability(long disconnectCountInWindow, int windowSeconds)
    {
        const int Threshold = 3;
        if (disconnectCountInWindow <= Threshold)
        {
            return GateResult.Pass(
                "Gate 1 — MQTT stability",
                GateBucket.Resilience,
                $"max gateway-client disconnects over a {windowSeconds}s window: " +
                $"{disconnectCountInWindow} (threshold ≤ {Threshold})");
        }

        return GateResult.Fail(
            "Gate 1 — MQTT stability",
            GateBucket.Resilience,
            $"disconnect storm: {disconnectCountInWindow} gateway-client disconnects within {windowSeconds}s " +
            $"(threshold ≤ {Threshold})");
    }

    /// <summary>
    /// Gate 5 — Reconnect behaviour. Adapter must reconnect within 5s
    /// of broker recovery; buffer depth must stay bounded during outage.
    /// </summary>
    public static GateResult Gate5ReconnectBehaviour(
        double timeToFirstPublishAfterRecoveryMs,
        long peakBufferDepthDuringOutage,
        long maxBufferDepthAllowed)
    {
        const double ReconnectThresholdMs = 5000;
        var reconnectOk = timeToFirstPublishAfterRecoveryMs <= ReconnectThresholdMs;
        var bufferOk = peakBufferDepthDuringOutage <= maxBufferDepthAllowed;

        if (reconnectOk && bufferOk)
        {
            return GateResult.Pass(
                "Gate 5 — Reconnect behaviour",
                GateBucket.Resilience,
                $"reconnect={timeToFirstPublishAfterRecoveryMs:F0}ms (threshold ≤ {ReconnectThresholdMs}ms); " +
                $"peak buffer depth during outage={peakBufferDepthDuringOutage} (bound={maxBufferDepthAllowed})");
        }

        var evidence = new List<string>();
        if (!reconnectOk)
        {
            evidence.Add($"reconnect took {timeToFirstPublishAfterRecoveryMs:F0}ms (>{ReconnectThresholdMs}ms)");
        }
        if (!bufferOk)
        {
            evidence.Add($"peak buffer depth {peakBufferDepthDuringOutage} exceeded bound {maxBufferDepthAllowed}");
        }

        return GateResult.Fail(
            "Gate 5 — Reconnect behaviour",
            GateBucket.Resilience,
            string.Join("; ", evidence));
    }

    /// <summary>
    /// Gate 8 — Backpressure behaviour. SQLite store-and-forward buffer
    /// must stay bounded; intake drops only after buffer is full;
    /// recovery within 60s of sink speedup.
    /// </summary>
    public static GateResult Gate8Backpressure(
        long peakBufferBytes,
        long maxBufferBytesAllowed,
        long intakeDroppedTotalBeforeBufferFull,
        double recoveryTimeMs)
    {
        const double RecoveryThresholdMs = 60_000;
        var bufferOk = peakBufferBytes <= maxBufferBytesAllowed;
        var dropsOk = intakeDroppedTotalBeforeBufferFull == 0;
        var recoveryOk = recoveryTimeMs <= RecoveryThresholdMs;

        if (bufferOk && dropsOk && recoveryOk)
        {
            return GateResult.Pass(
                "Gate 8 — Backpressure behaviour",
                GateBucket.Resilience,
                $"buffer peak={peakBufferBytes}B (bound={maxBufferBytesAllowed}B); " +
                $"drops-before-full=0; recovery={recoveryTimeMs:F0}ms (threshold ≤ {RecoveryThresholdMs}ms)");
        }

        var evidence = new List<string>();
        if (!bufferOk) evidence.Add($"peak buffer {peakBufferBytes}B exceeded bound {maxBufferBytesAllowed}B");
        if (!dropsOk) evidence.Add($"{intakeDroppedTotalBeforeBufferFull} intake drops occurred BEFORE buffer was full");
        if (!recoveryOk) evidence.Add($"recovery took {recoveryTimeMs:F0}ms (>{RecoveryThresholdMs}ms)");

        return GateResult.Fail(
            "Gate 8 — Backpressure behaviour",
            GateBucket.Resilience,
            string.Join("; ", evidence));
    }

    // ─── helpers ────────────────────────────────────────────────────────

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";
}
