// ============================================================================
// File: IHostReadinessGate.cs
// Purpose: Tiny readiness flag flipped by HostStartup once the locked
//          startup sequence reaches MarkReady. The Kestrel /health/ready
//          endpoint that lands in D phase 5 will read it; the same gate
//          is observable from tests so we can pin "ready" timing.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone D
// Milestone: D — phase 2.
// ============================================================================

namespace ElpisEdgeConnect.Host;

/// <summary>
/// Single source of truth for "is the host fully started". Flipped to true
/// at the <see cref="StartupPhase.MarkReady"/> step and back to false at the
/// mirror shutdown step.
/// </summary>
public interface IHostReadinessGate
{
    /// <summary>True after <see cref="MarkReady"/> and before <see cref="MarkNotReady"/>.</summary>
    bool IsReady { get; }

    /// <summary>Set readiness to true.</summary>
    void MarkReady();

    /// <summary>Set readiness to false.</summary>
    void MarkNotReady();
}

/// <summary>Default in-memory readiness gate. Thread-safe via volatile read/write.</summary>
public sealed class HostReadinessGate : IHostReadinessGate
{
    private volatile bool _ready;

    /// <inheritdoc/>
    public bool IsReady => _ready;

    /// <inheritdoc/>
    public void MarkReady() => _ready = true;

    /// <inheritdoc/>
    public void MarkNotReady() => _ready = false;
}
