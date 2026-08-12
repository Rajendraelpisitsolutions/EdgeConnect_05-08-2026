// ============================================================================
// File: Adapters/Retirement/ISourceRetirement.cs
// Purpose: The OPT-IN adapter capability for durable, adapter-owned retirement
//          attestation. Deliberately a SEPARATE capability interface — NOT a
//          default member on ISourceAdapter — so "unsupported" can never blur
//          into "method completed" (the exact inference the cutover eliminates).
//          An adapter that does not implement this fails closed at admission.
// Reference: docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md §4 (F2),
//            §0/§9a (fail-closed unsupported), §3 (capability known before init).
// Slice 0 — commit 3.0 (inert; no live supervisor wiring).
// ============================================================================

namespace ElpisEdgeConnect.Core.Adapters.Retirement;

/// <summary>
/// Opt-in capability: an adapter that can prove its own quiescence at retirement.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="BeginRetirement"/> PROMPTLY initiates the adapter-defined cleanup
/// (the adapter knows whether closing its transport is what releases its worker)
/// and returns a durable <see cref="AdapterRetirementOperation"/>. The host
/// observes the operation's <c>Completion</c> against an absolute monotonic
/// deadline; it MUST NOT manufacture proof from <c>StopAsync</c>,
/// <c>DisposeAsync</c>, or task completion alone.
/// </para>
/// <para>
/// Capability must be discoverable BEFORE resourceful initialization (a blocked
/// source must never open a second socket/handle/session). Because adapter
/// construction is non-resourceful in this codebase (transports open in
/// <c>InitializeAsync</c>/<c>StartAsync</c>, not the constructor), the host may
/// check this capability on the constructed-but-uninitialized instance. An
/// adapter that does NOT implement this interface is treated as
/// retirement-unsupported and fails closed at admission — never inferred proven.
/// </para>
/// </remarks>
public interface ISourceRetirement
{
    /// <summary>
    /// Begin the adapter-defined retirement cleanup and return a durable handle.
    /// Synchronous return carries the snapshot; the attestation arrives via the
    /// handle's <c>Completion</c>.
    /// </summary>
    /// <remarks>
    /// Contract:
    /// <list type="bullet">
    ///   <item><b>Non-blocking:</b> initiates cleanup promptly and returns without
    ///   waiting for the worker to exit.</item>
    ///   <item><b>Idempotent:</b> repeated calls return the same (or an equivalent
    ///   already-started) durable operation — never a second cleanup. Valid from
    ///   every partial lifecycle state (constructed, initialized, starting,
    ///   running, start-failed).</item>
    ///   <item><b>Async continuations:</b> any <c>TaskCompletionSource</c> behind
    ///   <c>Completion</c> MUST use
    ///   <see cref="System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously"/>
    ///   so host continuations never run inline on an adapter cleanup/socket/native
    ///   thread.</item>
    /// </list>
    /// </remarks>
    AdapterRetirementOperation BeginRetirement(AdapterRetirementContext context);
}
