// ============================================================================
// File: Eremos/GateResult.cs
// Purpose: Result type for a single revalidation gate. Each gate's
//          measurement methodology produces a GateResult that captures
//          pass/fail + bucket (Contract / Resilience / RealEremosOnly) +
//          human-readable evidence. The aggregate RevalidationReport
//          rolls these up.
// Reference: docs/sessions/2026-05-21-eremos-v2-revalidation-plan-v2.md §4
// ============================================================================

namespace ElpisEdgeConnect.Integration.Tests.Eremos;

/// <summary>
/// Which taxonomy bucket the gate belongs to. The v2 plan §4 splits the
/// 8 gates so a red gate identifies which layer broke:
/// </summary>
public enum GateBucket
{
    /// <summary>Validates EdgeConnect's emission matches the EREMOS V2 contract.</summary>
    Contract,

    /// <summary>Validates EdgeConnect's behaviour under stress (not EREMOS-specific).</summary>
    Resilience,

    /// <summary>Meaningful only with a real EREMOS V2 instance; SKIPPED under the mock-fallback path.</summary>
    RealEremosOnly,
}

/// <summary>Outcome of a single gate evaluation.</summary>
public enum GateOutcome
{
    /// <summary>Gate passed.</summary>
    Pass,

    /// <summary>Gate failed — see <see cref="GateResult.Evidence"/> for the reason.</summary>
    Fail,

    /// <summary>
    /// Gate intentionally skipped. Used for Gate 6 + Gate 7 under the
    /// mock-fallback path (no real EREMOS V2 instance to measure against).
    /// The Evidence carries the explicit skip reason — never an empty
    /// "skipped" placeholder.
    /// </summary>
    Skipped,
}

/// <summary>
/// A single gate's result: name + bucket + outcome + human-readable
/// evidence. Evidence is what gets written into the
/// <c>docs/contracts/eremos-v2-revalidation.md</c> snapshot per
/// implementation step 12.
/// </summary>
public sealed record GateResult(
    string GateName,
    GateBucket Bucket,
    GateOutcome Outcome,
    string Evidence)
{
    public static GateResult Pass(string gateName, GateBucket bucket, string evidence) =>
        new(gateName, bucket, GateOutcome.Pass, evidence);

    public static GateResult Fail(string gateName, GateBucket bucket, string evidence) =>
        new(gateName, bucket, GateOutcome.Fail, evidence);

    /// <summary>
    /// Construct a Skipped result. The <paramref name="reason"/> MUST
    /// explain why the gate was skipped — the v2 plan §4.3 requires
    /// explicit skip reasons rather than silent passes (e.g.,
    /// "real-EREMOS-only — running mock-fallback path").
    /// </summary>
    public static GateResult Skipped(string gateName, GateBucket bucket, string reason) =>
        new(gateName, bucket, GateOutcome.Skipped, reason);
}
