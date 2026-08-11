// ============================================================================
// File: Hosting/ManagementStartupAnnouncer.cs
// Purpose: Emits the first-start log message + repeats a security
//          WARNING every 60s when the Studio is exposed on the
//          network without authentication. Also surfaces an
//          observability gauge so the unsafe configuration shows
//          up on Prometheus dashboards.
//
//          Wording locked at plan-review time:
//            "[mgmt] Connectivity Studio listening on http://<host>:<port>
//             (localhost-only). To expose it on the network, configure
//             management.bindAddress and management.auth. See
//             docs/ops-runbook.md § 9."
//
//          And the warning variant:
//            "[mgmt] WARNING: Connectivity Studio is exposed on the network
//             without authentication. Set management.auth=basic before
//             production use."
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1a
//            docs/ops-runbook.md § 9
// ============================================================================

using System;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Management.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Management.Hosting;

/// <summary>
/// IHostedService that announces the Studio's listen address on
/// startup and (when applicable) reminds the operator every 60s
/// that the unsafe network-without-auth combination is active.
/// </summary>
public sealed class ManagementStartupAnnouncer : BackgroundService
{
    /// <summary>
    /// Prometheus instrument name surfacing the
    /// "network-exposed without auth" state. <c>1</c> means
    /// unsafe; <c>0</c> means safe (loopback or auth enabled).
    /// </summary>
    public const string InsecureExposureInstrumentName =
        DiagnosticsConstants.InstrumentPrefix + "management_insecure_exposed";

    /// <summary>
    /// Default cadence for the repeated warning. Loud enough to
    /// kill the "I'll fix it tomorrow" pattern, quiet enough not
    /// to drown legitimate logs.
    /// </summary>
    public static readonly TimeSpan WarningCadence = TimeSpan.FromSeconds(60);

    private readonly ManagementOptions _options;
    private readonly ILogger<ManagementStartupAnnouncer> _logger;
    private readonly Meter _meter;
    private readonly bool _ownsMeter;
    private readonly TimeSpan _warningCadence;

    /// <summary>
    /// Construct the announcer. The meter is optional — when null a
    /// dedicated <see cref="Meter"/> is created and disposed with
    /// this instance.
    /// </summary>
    public ManagementStartupAnnouncer(
        ManagementOptions options,
        ILogger<ManagementStartupAnnouncer> logger,
        Meter? meter = null,
        TimeSpan? warningCadence = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _logger = logger;
        _ownsMeter = meter is null;
        _meter = meter ?? new Meter(DiagnosticsConstants.MeterName);
        _warningCadence = warningCadence ?? WarningCadence;

        // ObservableGauge — read live every scrape. Returns 1 when
        // the current configuration is unsafe, 0 when safe.
        _meter.CreateObservableGauge<int>(
            InsecureExposureInstrumentName,
            () => _options.IsNetworkExposedWithoutAuth ? 1 : 0,
            unit: "boolean",
            description: "1 when the Studio is bound to a non-loopback address with auth=None; 0 otherwise.");
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // First-start announcement — always emitted, even when safe.
        if (_options.IsLoopbackBinding)
        {
            _logger.LogInformation(
                "[mgmt] Connectivity Studio listening on {BaseUrl} (localhost-only). " +
                "To expose it on the network, configure management.bindAddress and " +
                "management.auth. See docs/ops-runbook.md § 9.",
                _options.BaseUrl);
        }
        else
        {
            _logger.LogInformation(
                "[mgmt] Connectivity Studio listening on {BaseUrl} (network-exposed; auth={AuthMode}).",
                _options.BaseUrl, _options.Auth.Mode);
        }

        // Repeated WARNING when the unsafe combo is active. Stays
        // loud until the operator either restricts the binding or
        // enables auth.
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_options.IsNetworkExposedWithoutAuth)
            {
                _logger.LogWarning(
                    "[mgmt] WARNING: Connectivity Studio is exposed on the network without " +
                    "authentication ({BaseUrl}). Set management.auth=basic before production use.",
                    _options.BaseUrl);
            }

            try
            {
                await Task.Delay(_warningCadence, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        base.Dispose();
        if (_ownsMeter)
        {
            _meter.Dispose();
        }
    }
}
