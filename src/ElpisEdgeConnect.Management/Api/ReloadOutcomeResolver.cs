// ============================================================================
// File: Api/ReloadOutcomeResolver.cs
// Purpose: Bridges the Management apply / rollback endpoints to the
//          gateway's IReloadOutcomeRegistry. After a successful apply,
//          the endpoint calls ResolveAsync, which awaits the registry
//          for at most HostOptions.ReloadOutcomeWaitMs and returns
//          one of:
//
//             * a fully-populated ReloadOutcomeDto from the coordinator,
//             * an InProgress placeholder (wait window elapsed first), or
//             * null (no registry registered — Management standalone or
//               tests with no coordinator wired).
//
//          The null vs InProgress distinction is intentional and
//          load-bearing for the operator UX:
//             null       = runtime reload observation unavailable
//             InProgress = reload still executing; poll /diagnostics
//
//          The wait is bounded by HostOptions.ReloadOutcomeWaitMs
//          (default 10s, configurable; phase 3 plan v2 §2 Q4). The
//          endpoint NEVER blocks indefinitely.
// Reference: docs/sessions/2026-05-16-mp22-phase3-plan.md (v2 locked)
// Milestone: M.P2.2 phase 3
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Management.Configuration;
using ElpisEdgeConnect.Management.Contracts.Config;
using Microsoft.Extensions.DependencyInjection;
// Disambiguate ElpisEdgeConnect.Host.HostOptions from
// Microsoft.Extensions.Hosting.HostOptions (the latter ships in the
// hosting framework with the same name).
using EdgeHostOptions = ElpisEdgeConnect.Host.HostOptions;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Internal helper: resolves the M.P2.2 reload-outcome DTO for an
/// apply / rollback response. Static + cancellable; no state.
/// </summary>
internal static class ReloadOutcomeResolver
{
    /// <summary>
    /// Default wait window when <see cref="ElpisEdgeConnect.Host.HostOptions"/>
    /// is not registered (Management standalone / tests). Matches the
    /// <see cref="ElpisEdgeConnect.Host.HostOptions.ReloadOutcomeWaitMs"/>
    /// default so the behaviour is identical between hosted and
    /// standalone modes once a registry IS present.
    /// </summary>
    public const int DefaultWaitMs = 10_000;

    /// <summary>
    /// Resolve the reload outcome DTO for <paramref name="newVersionId"/>.
    /// Returns <c>null</c> when no <see cref="IReloadOutcomeRegistry"/>
    /// is registered (preserves the null / InProgress semantic
    /// distinction). When a registry IS present, awaits it for at most
    /// the configured wait window and returns either the mapped outcome
    /// or an <c>InProgress</c> placeholder.
    /// </summary>
    public static async Task<ReloadOutcomeDto?> ResolveAsync(
        IServiceProvider services,
        ConfigurationVersionId newVersionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        var registry = services.GetService<IReloadOutcomeRegistry>();
        if (registry is null)
        {
            return null;
        }

        var waitMs = services.GetService<EdgeHostOptions>()?.ReloadOutcomeWaitMs ?? DefaultWaitMs;
        var waitWindow = TimeSpan.FromMilliseconds(waitMs);

        var outcome = await registry
            .WaitForAsync(newVersionId, waitWindow, cancellationToken)
            .ConfigureAwait(false);

        if (outcome is not null)
        {
            return ConfigContractMapper.ToReloadOutcome(outcome);
        }

        return ConfigContractMapper.InProgressPlaceholder(newVersionId, waitMs);
    }
}
