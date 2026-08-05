// ============================================================================
// File: Api/Focas2BrowseService.cs
// Purpose: Drives the "Browse Controller" probe behind POST
//          /api/v1/sources/browse/focas2 — Initialize → Start → connected-check
//          → BrowseTagsAsync → bounded(Stop + Dispose) on a throwaway
//          Focas2SourceAdapter. The "discovery is management-plane
//          ephemeral" principle (ADR-0011 / Locked L) is the load-bearing
//          architectural framing: nothing here touches the runtime
//          SourceSupervisor or the registration helpers in Host.
//
// LOCKED behaviour pinned by tests:
//   * Locked I — license-gated by `source-focas2`.
//   * Locked J — single-flight per IpAddress:Port, returns busy (not queue).
//   * Locked M — fixed 15s probe budget (NOT derived from config.TimeoutSeconds).
//   * Locked N — ValidateConfigAsync BEFORE InitializeAsync.
//   * Locked P — combined Stop+Dispose bounded at 12s; abandon-and-warn on hang.
//   * Locked Q — correlation ProbeId on every log line and in the DTO.
//   * Locked S — probe forces MaxConnectRetries=1 and TimeoutSeconds=min(8, req).
//
// Adapter post-Start invariant (Step 1 §14 observation, elevated by user):
//   StartAsync is FAIL-SOFT in the FOCAS2 adapter — it transitions to Running
//   even when the initial connect failed. The probe service explicitly checks
//   the adapter's connected metric BEFORE calling BrowseTagsAsync; without
//   this, a soft-failed connect would silently return the fallback ["X","Y","Z"]
//   axis names with no error code.
// Reference: docs/sessions/2026-05-17-mp2b3-focas2-wizard-plan-v3.md §6.4 + §14
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Identity;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sources.Focas2;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Executes a one-shot probe of a FOCAS2 controller — used by the wizard's
/// "Browse Controller" button to surface real axis names and tag definitions
/// before the operator commits a draft.
/// </summary>
public sealed class Focas2BrowseService
{
    private const string LicenseModuleKey = "source-focas2";

    /// <summary>Locked M — fixed probe budget. Tests may override via the internal ctor.</summary>
    internal static readonly TimeSpan DefaultProbeBudget = TimeSpan.FromSeconds(15);

    /// <summary>Locked P — bounded combined Stop+Dispose cleanup phase. Tests may override.</summary>
    internal static readonly TimeSpan DefaultCleanupBudget = TimeSpan.FromSeconds(12);

    /// <summary>Locked S — cap on per-attempt TimeoutSeconds for the probe override.</summary>
    internal const int ProbeMaxTimeoutSeconds = 8;

    private readonly Func<string, ISourceAdapter> _adapterFactory;
    private readonly Func<string, bool> _isModuleEnabled;
    private readonly ILogger<Focas2BrowseService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _leases = new(StringComparer.Ordinal);
    private readonly TimeSpan _probeBudget;
    private readonly TimeSpan _cleanupBudget;

    /// <summary>
    /// Production constructor — builds a real <see cref="Focas2SourceAdapter"/>
    /// per probe, gated by the supplied <see cref="ILicenseManager"/>.
    /// </summary>
    /// <param name="loggerFactory">Logger factory; one logger is created per probe call.</param>
    /// <param name="identity">Optional gateway identity — propagated to the throwaway adapter for canonical-point construction symmetry.</param>
    /// <param name="license">Optional license manager. When null OR no license loaded, the gate is permissive (matches the existing registration semantic in <c>Focas2RegistrationExtensions.ResolveSourceRegistrationInputs</c>).</param>
    public Focas2BrowseService(
        ILoggerFactory loggerFactory,
        IGatewayIdentity? identity = null,
        ILicenseManager? license = null)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _adapterFactory = instanceId => new Focas2SourceAdapter(
            instanceId,
            loggerFactory.CreateLogger<Focas2SourceAdapter>(),
            identity);
        _isModuleEnabled = module =>
            license is null || license.Current is null || license.IsModuleEnabled(module);
        _logger = loggerFactory.CreateLogger<Focas2BrowseService>();
        _probeBudget = DefaultProbeBudget;
        _cleanupBudget = DefaultCleanupBudget;
    }

    /// <summary>
    /// Test constructor — accepts seam delegates so tests inject a fake
    /// <see cref="ISourceAdapter"/> without instantiating
    /// <see cref="Focas2SourceAdapter"/> (which would require the
    /// internal test ctor + a <c>FakeFocas2Api</c>). License gating is
    /// also delegated so tests don't need an <see cref="ILicenseManager"/>.
    /// Probe and cleanup budgets are overridable for fast tests; the
    /// PRODUCTION values are still pinned by <see cref="DefaultProbeBudget"/>
    /// and <see cref="DefaultCleanupBudget"/>.
    /// </summary>
    internal Focas2BrowseService(
        Func<string, ISourceAdapter> adapterFactory,
        Func<string, bool> isModuleEnabled,
        ILogger<Focas2BrowseService> logger,
        TimeSpan? probeBudget = null,
        TimeSpan? cleanupBudget = null)
    {
        _adapterFactory = adapterFactory ?? throw new ArgumentNullException(nameof(adapterFactory));
        _isModuleEnabled = isModuleEnabled ?? throw new ArgumentNullException(nameof(isModuleEnabled));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _probeBudget = probeBudget ?? DefaultProbeBudget;
        _cleanupBudget = cleanupBudget ?? DefaultCleanupBudget;
    }

    /// <summary>
    /// Run a Browse Controller probe against the supplied source instance.
    /// </summary>
    /// <param name="request">Wizard-produced canonical config (the same shape <c>/api/v1/config/drafts</c> accepts).</param>
    /// <param name="ct">External cancellation — flows through to the adapter calls in addition to the 15s probe budget.</param>
    /// <returns>A populated result DTO; never throws for expected probe failures.</returns>
    public async Task<Focas2BrowseResultDto> BrowseAsync(
        SourceInstanceConfig request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var probeId = Guid.NewGuid().ToString("N")[..8];
        var stopwatch = Stopwatch.StartNew();

        // 1. License gate (Locked I).
        if (!_isModuleEnabled(LicenseModuleKey))
        {
            _logger.LogInformation(
                "BrowseProbeId={ProbeId} rejected — license module '{Module}' is disabled",
                probeId, LicenseModuleKey);
            return Failure(probeId, "LICENSE.MODULE_DISABLED",
                $"License module '{LicenseModuleKey}' is not enabled. The Browse Controller feature requires an active FOCAS2 source license.",
                stopwatch);
        }

        // 2. Schema parse — fast-fail (Locked G).
        Focas2SourceConfiguration requested;
        try
        {
            requested = Focas2SourceConfiguration.FromSourceInstance(request);
        }
        catch (ArgumentException ex)
        {
            _logger.LogInformation(
                "BrowseProbeId={ProbeId} schema-parse failed: {Message}",
                probeId, ex.Message);
            return Failure(probeId, "FOCAS2.CONFIG_INVALID", ex.Message, stopwatch);
        }

        // 3. Single-flight per IP:Port (Locked J) — Wait(0) so a held lease
        //    returns BUSY rather than queuing the second probe.
        var leaseKey = $"{requested.IpAddress}:{requested.Port}";
        var lease = _leases.GetOrAdd(leaseKey, _ => new SemaphoreSlim(1, 1));
        // Wait(0) is intentional — busy means "another probe in flight",
        // not "queue mine". CancellationToken.None makes this explicit.
        if (!lease.Wait(0, CancellationToken.None))
        {
            _logger.LogInformation(
                "BrowseProbeId={ProbeId} rejected — another probe is in flight for {LeaseKey}",
                probeId, leaseKey);
            return Failure(probeId, "FOCAS2.BROWSE_IN_FLIGHT",
                $"Another Browse Controller probe is already running against {leaseKey}. Wait for it to finish before retrying.",
                stopwatch);
        }

        try
        {
            return await BrowseUnderLeaseAsync(probeId, requested, stopwatch, ct).ConfigureAwait(false);
        }
        finally
        {
            lease.Release();
        }
    }

    private async Task<Focas2BrowseResultDto> BrowseUnderLeaseAsync(
        string probeId,
        Focas2SourceConfiguration requested,
        Stopwatch stopwatch,
        CancellationToken ct)
    {
        // 4. Probe-only override (Locked S). The wizard's MaxConnectRetries
        //    and TimeoutSeconds describe RUNTIME behaviour; Browse is for
        //    interactive discovery and must finish inside the 15s budget.
        //    Without this override, an uncancellable retry loop in
        //    Focas2ConnectionManager.TryConnect could spend ~57s of native
        //    work even after the await is cancelled.
        var probeConfig = requested with
        {
            MaxConnectRetries = 1,
            TimeoutSeconds = Math.Min(ProbeMaxTimeoutSeconds, Math.Max(1, requested.TimeoutSeconds)),
        };

        _logger.LogInformation(
            "BrowseProbeId={ProbeId} starting probe — endpoint={Endpoint}, probeTimeoutS={ProbeTimeoutS}",
            probeId, $"{probeConfig.IpAddress}:{probeConfig.Port}", probeConfig.TimeoutSeconds);

        // 5. Construct adapter — direct construction, NO SourceSupervisor /
        //    SourceRegistration involvement (Locked H + Locked L).
        ISourceAdapter? adapter = null;
        var warnings = new List<string>();
        try
        {
            adapter = _adapterFactory(probeConfig.InstanceId);

            // 6. Probe budget (Locked M, default 15s, now enforceable thanks to Locked S).
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeCts.CancelAfter(_probeBudget);
            var probeCt = probeCts.Token;

            // 7. ValidateConfigAsync BEFORE Initialize (Locked N — confirmed pure).
            _logger.LogDebug("BrowseProbeId={ProbeId} validating config", probeId);
            var validation = await adapter.ValidateConfigAsync(probeConfig, probeCt).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                _logger.LogInformation(
                    "BrowseProbeId={ProbeId} config-invalid: {ErrorCount} error(s)",
                    probeId, validation.Errors.Count);
                return Failure(probeId, "FOCAS2.CONFIG_INVALID",
                    "Configuration validation failed before connection was attempted.",
                    stopwatch, validation.Errors);
            }

            // 8. Initialize → Start.
            _logger.LogDebug("BrowseProbeId={ProbeId} initializing adapter", probeId);
            await adapter.InitializeAsync(probeConfig, probeCt).ConfigureAwait(false);

            _logger.LogDebug("BrowseProbeId={ProbeId} starting adapter", probeId);
            await adapter.StartAsync(probeCt).ConfigureAwait(false);

            // 9. Post-Start connected check. Focas2SourceAdapter.StartAsync is
            //    fail-soft: a connect failure logs a warning and transitions
            //    to Running anyway. Without this check, BrowseTagsAsync would
            //    return the fallback ["X","Y","Z"] axis list and the probe
            //    would report a misleading success.
            var health = await adapter.CheckHealthAsync(probeCt).ConfigureAwait(false);
            var metrics = health.Metrics;
            var connected = metrics is not null
                            && metrics.TryGetValue("connected", out var connectedObj)
                            && connectedObj is true;
            if (!connected)
            {
                _logger.LogInformation(
                    "BrowseProbeId={ProbeId} connect-failed — adapter Running but not connected",
                    probeId);
                return Failure(probeId, "FOCAS2.CONNECT_FAILED",
                    $"Could not reach FOCAS2 controller at {probeConfig.IpAddress}:{probeConfig.Port}. " +
                    "Check IP/port, network reachability, and that the controller is powered on.",
                    stopwatch);
            }

            // 10. Browse — synchronous read of cached SystemInfo + tag map.
            _logger.LogDebug("BrowseProbeId={ProbeId} browsing tags", probeId);
            var tags = await adapter.BrowseTagsAsync(probeCt).ConfigureAwait(false);

            var axisNames = ExtractAxisNames(tags);
            // metrics is non-null here — the connected-true branch above requires it —
            // but the compiler doesn't track the conjunction across branches.
            var cncSeries = metrics!.TryGetValue("cncSeries", out var sObj) ? sObj as string : null;
            var cncType = metrics.TryGetValue("cncType", out var tObj) ? tObj as string : null;

            _logger.LogInformation(
                "BrowseProbeId={ProbeId} success — tags={TagCount}, axes=[{Axes}], series={Series}, elapsedMs={ElapsedMs}",
                probeId, tags.Count, string.Join(",", axisNames), cncSeries, stopwatch.ElapsedMilliseconds);

            return new Focas2BrowseResultDto
            {
                ProbeId = probeId,
                Success = true,
                AxisNames = axisNames,
                Tags = tags,
                CncSeries = cncSeries,
                CncType = cncType,
                Warnings = warnings,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Probe budget expired (15s); external caller cancellation is re-thrown below.
            _logger.LogInformation(
                "BrowseProbeId={ProbeId} budget-exceeded ({Budget}) — abandoning",
                probeId, _probeBudget);
            return Failure(probeId, "FOCAS2.BROWSE_TIMEOUT",
                $"Browse Controller did not complete within {_probeBudget.TotalSeconds:0.#} seconds. The controller may be unreachable or the connection is slower than the probe budget.",
                stopwatch, warnings: warnings);
        }
        catch (AdapterException ex)
        {
            _logger.LogInformation(
                "BrowseProbeId={ProbeId} adapter-error {Code}: {Message}",
                probeId, ex.Error.Code, ex.Message);
            return Failure(probeId, ex.Error.Code, ex.Message, stopwatch, warnings: warnings);
        }
        catch (DllNotFoundException ex)
        {
            _logger.LogError(ex,
                "BrowseProbeId={ProbeId} FOCAS2 native library could not be loaded",
                probeId);
            return Failure(probeId, "FOCAS2.NATIVE_LIBRARY_MISSING",
                "The FOCAS2 native library (Fwlib64.dll / libfwlib32.so) could not be loaded. " +
                "See docs/adapter-sdk/focas2-adapter.md for installation instructions.",
                stopwatch, warnings: warnings);
        }
        finally
        {
            // 11. Bounded Stop+Dispose (Locked P — revised v3). Capture the
            //     pattern in Task.Run so the WaitAsync wrap genuinely bounds
            //     the combined cleanup, not just the await-shape of Dispose.
            //     Focas2Thread.Join internally caps at 10s; we add 2s margin.
            if (adapter is not null)
            {
                try
                {
                    var localAdapter = adapter;
                    // CancellationToken.None on both Run and WaitAsync is
                    // intentional: cleanup must run even when the caller
                    // cancelled the request. The 12s WaitAsync bound is
                    // what guarantees we don't block the response thread.
                    await Task.Run(
                        async () =>
                        {
                            try { await localAdapter.StopAsync(CancellationToken.None).ConfigureAwait(false); }
                            catch { /* cleanup; swallow */ }
                            try { await localAdapter.DisposeAsync().ConfigureAwait(false); }
                            catch { /* cleanup; swallow */ }
                        },
                        CancellationToken.None)
                        .WaitAsync(_cleanupBudget, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning(
                        "BrowseProbeId={ProbeId} cleanup hung beyond {Budget} — adapter abandoned (native handle reclaimed at process exit)",
                        probeId, _cleanupBudget);
                    warnings.Add("FOCAS2.DISPOSE_TIMEOUT");
                }
            }
        }
    }

    /// <summary>
    /// Extract distinct axis-name segments from <c>"Axes/&lt;name&gt;/..."</c>
    /// tag paths. The adapter's <c>BrowseTagsAsync</c> doesn't expose axis
    /// names directly; deriving them from tag paths keeps this code in
    /// Management without modifying the adapter contract.
    /// </summary>
    private static List<string> ExtractAxisNames(IReadOnlyList<TagDefinition> tags)
    {
        const string axesPrefix = "Axes/";
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            if (tag.Path is null || !tag.Path.StartsWith(axesPrefix, StringComparison.Ordinal))
            {
                continue;
            }
            var rest = tag.Path[axesPrefix.Length..];
            var slash = rest.IndexOf('/');
            // "Axes/X/Absolute" → "X"; skip "Axes/FeedRate" (no axis-name segment).
            if (slash <= 0)
            {
                continue;
            }
            var name = rest[..slash];
            if (seen.Add(name))
            {
                result.Add(name);
            }
        }
        return result;
    }

    private static Focas2BrowseResultDto Failure(
        string probeId,
        string code,
        string message,
        Stopwatch stopwatch,
        IReadOnlyList<ValidationIssue>? validationErrors = null,
        IList<string>? warnings = null) =>
        new()
        {
            ProbeId = probeId,
            Success = false,
            ErrorCode = code,
            ErrorMessage = message,
            ValidationErrors = validationErrors ?? [],
            Warnings = warnings ?? new List<string>(),
            ElapsedMs = stopwatch.ElapsedMilliseconds,
        };
}
