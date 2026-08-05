// ============================================================================
// File: IStartupSequenceObserver.cs
// Purpose: Test seam used to observe the host startup/shutdown sequence.
//          The host calls OnStartupPhase(...) immediately before doing the
//          work for that phase, and OnShutdownPhase(...) immediately before
//          doing the corresponding teardown work. The production default is
//          a no-op; tests inject a recording implementation.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone D — startup ordering contract
// Milestone: D — phase 2.
// ============================================================================

namespace ElpisEdgeConnect.Host;

/// <summary>
/// Observation hook called by the host as it walks through the locked
/// <see cref="StartupPhase"/> sequence. Implementations must be sync-void
/// and non-throwing — the host will not catch exceptions from the observer.
/// </summary>
public interface IStartupSequenceObserver
{
    /// <summary>Called immediately BEFORE the work for <paramref name="phase"/> begins on startup.</summary>
    void OnStartupPhase(StartupPhase phase);

    /// <summary>Called immediately BEFORE the teardown work for <paramref name="phase"/> begins on shutdown.</summary>
    void OnShutdownPhase(StartupPhase phase);
}

/// <summary>
/// Production no-op observer. Registered by default; tests substitute a
/// recording implementation.
/// </summary>
public sealed class NullStartupSequenceObserver : IStartupSequenceObserver
{
    /// <summary>Shared singleton.</summary>
    public static NullStartupSequenceObserver Instance { get; } = new();

    private NullStartupSequenceObserver() { }

    /// <inheritdoc/>
    public void OnStartupPhase(StartupPhase phase) { /* no-op */ }

    /// <inheritdoc/>
    public void OnShutdownPhase(StartupPhase phase) { /* no-op */ }
}
