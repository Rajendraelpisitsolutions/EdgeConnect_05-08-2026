// ============================================================================
// File: Configuration/ConfigurationChangeEventArgs.cs
// Purpose: Event payload for IConfigurationManager.CurrentChanged.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone B2
// ============================================================================

using System;
using System.Collections.Generic;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Event payload raised by <c>IConfigurationManager.CurrentChanged</c> after
/// a successful apply or rollback. Subscribers (the routing engine in C3,
/// the diagnostics collector in C4, etc.) use this to react to live config
/// changes.
/// </summary>
/// <remarks>
/// No subscribers exist in B2. The event is defined and emitted so that
/// later milestones can wire up subscribers without changing the manager.
/// Per the Q4 design decision, no initial event is emitted during
/// initialisation; subscribers must call <c>GetCurrentAsync</c> on the
/// manager for their baseline.
/// </remarks>
public sealed class ConfigurationChangeEventArgs : EventArgs
{
    /// <summary>
    /// Construct a change event payload.
    /// </summary>
    public ConfigurationChangeEventArgs(
        ConfigurationVersionId previousVersionId,
        ConfigurationVersionId newVersionId,
        GatewayConfiguration newConfiguration,
        IReadOnlyList<ConfigurationChange> changes)
    {
        ArgumentNullException.ThrowIfNull(newConfiguration);
        ArgumentNullException.ThrowIfNull(changes);
        PreviousVersionId = previousVersionId;
        NewVersionId = newVersionId;
        NewConfiguration = newConfiguration;
        Changes = changes;
    }

    /// <summary>The previous current version id.</summary>
    public ConfigurationVersionId PreviousVersionId { get; }

    /// <summary>The new current version id.</summary>
    public ConfigurationVersionId NewVersionId { get; }

    /// <summary>The new current configuration. Subscribers can read this directly.</summary>
    public GatewayConfiguration NewConfiguration { get; }

    /// <summary>The structured diff between previous and new configurations.</summary>
    public IReadOnlyList<ConfigurationChange> Changes { get; }
}
