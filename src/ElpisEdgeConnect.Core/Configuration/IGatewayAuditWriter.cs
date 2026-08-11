// ============================================================================
// File: Configuration/IGatewayAuditWriter.cs
// Purpose: The write surface for NON-configuration events that belong in the
//          gateway's audit chain (ADR-0020 Rule 5; bundle plan v2 §6). The same
//          hash-linked chain the configuration manager owns also records
//          gateway-wide events like "a diagnostic bundle was generated" — so a
//          customer can answer "what happened on this gateway?" from one log.
//
//          Kept separate from IConfigurationManager so non-config subsystems
//          (e.g. the diagnostic bundle) can append audit events without
//          depending on the whole config-management surface — and so the many
//          IConfigurationManager test fakes are unaffected. Implemented by
//          ConfigurationManager (the chain owner); over time both interfaces
//          converge on a GatewayAuditLog.
// Reference: docs/sessions/2026-05-31-adr0020-bundle-generation-plan-v2.md §6.
// ============================================================================

using System.Threading;
using System.Threading.Tasks;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Appends non-configuration events to the gateway's hash-linked audit chain.
/// </summary>
public interface IGatewayAuditWriter
{
    /// <summary>
    /// Record that a diagnostic bundle was generated (ADR-0020 Rule 5). The
    /// entry is chained into the same audit log as config changes.
    /// </summary>
    /// <param name="actor">The operator / session that initiated generation.</param>
    /// <param name="summary">
    /// One-line operator-readable summary, including the manifest summary hash so
    /// a later reviewer can correlate the event with a specific bundle.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The appended audit entry.</returns>
    ValueTask<ConfigurationAuditEntry> AppendBundleGeneratedAsync(
        string actor,
        string summary,
        CancellationToken cancellationToken);
}
