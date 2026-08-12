// ============================================================================
// File: LicenseTrialEnforcer.cs
// Purpose: Enforce a runtime cutoff when the gateway is NOT running under a valid
//          license. The gateway gets a fixed DEMO BUDGET (default 2 hours) of
//          ACTIVE DATA COLLECTION; the budget is consumed only while a source is
//          actually collecting data (points flowing), and pauses whenever nothing
//          is connected/reading. Once the budget is spent, DATA COLLECTION is
//          stopped (sources stop reading, sinks stop publishing) — but the
//          host/UI keeps running so an operator can still activate a license.
// Reference: docs/decisions/0035-unlicensed-runtime-cutoff.md
//
// LOCKED-DECISION OVERRIDE: This deliberately OVERRIDES ARCHITECTURE_BLUEPRINT
// Appendix A locked decision #7 ("Never cut customer data to enforce licensing")
// for the non-Valid license case, per ADR-0035 (owner-approved). The pipeline
// itself stays license-free and deterministic; the cutoff is an outer supervisor
// that stops the source + sink supervisors, not a check inside the data path.
// The host is NOT shut down.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Host.Adapters;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Host;

/// <summary>
/// Background supervisor that STOPS DATA COLLECTION (sources + sinks) once the
/// gateway has consumed <see cref="TrialDuration"/> of ACTIVE data collection
/// without a <see cref="LicenseStatus.Valid"/> license. The demo budget only
/// counts down while a source is actually collecting data, so an idle or
/// disconnected gateway never burns it. The host keeps running. See ADR-0035.
/// Dormant whenever the license is valid.
/// </summary>
public sealed class LicenseTrialEnforcer : BackgroundService
{
    /// <summary>Default demo budget of active data collection before it is stopped.</summary>
    public static readonly TimeSpan DefaultTrialDuration = TimeSpan.FromHours(2);

    /// <summary>
    /// Default cadence at which the license status is re-checked. Kept short so a
    /// system-clock rollback (and its recovery) is detected within a few seconds and
    /// the demo/tamper banner appears without waiting or a manual refresh.
    /// </summary>
    public static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Minimum gap between anchor WRITES, independent of <see cref="DefaultCheckInterval"/>.
    /// Detecting a rollback only needs a read and a comparison, but advancing the
    /// high-water mark writes a file to every anchor location. Ticking the check
    /// at 1s without this would multiply that disk churn fivefold on a gateway
    /// that runs for months. Reads stay per-tick; writes stay at the old cadence.
    /// </summary>
    private static readonly TimeSpan AnchorWriteInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Environment variable that overrides the demo budget, in minutes.
    /// Primarily for testing and tuning; falls back to
    /// <see cref="DefaultTrialDuration"/> when unset or unparseable.
    /// </summary>
    public const string TrialMinutesEnvVar = "EDGECONNECT_LICENSE_TRIAL_MINUTES";

    private readonly ILicenseManager _license;
    private readonly SourceSupervisor _sourceSupervisor;
    private readonly SinkSupervisor _sinkSupervisor;
    private readonly LicenseTrialState _state;
    private readonly IDiagnosticsService _diagnostics;
    private readonly ILogger<LicenseTrialEnforcer> _logger;
    private readonly string? _licensePath;
    private readonly TimeSpan _trialDuration;
    private readonly TimeSpan _checkInterval;
    private readonly Func<DateTime> _utcNow;

    // Last-seen stamp of the license file, so a delete / replace / edit is
    // detected between checks.
    private DateTime? _licenseFileWriteUtc;
    private long _licenseFileLength = -1;

    // Clock-rollback state. _minValidUtc makes a "before the license was issued"
    // detection sticky so the status doesn't flap once the license is unloaded.
    private readonly ClockAnchorStore? _clockAnchors;
    private readonly TimeSpan _clockTolerance;
    private DateTime? _minValidUtc;
    private bool _clockTamperLogged;
    private long _lastMonotonicMs = -1;

    // Monotonic stamp of the last anchor WRITE, so writes stay on their own
    // slower cadence than the rollback check. -1 = never written this run.
    private long _lastAnchorWriteMs = -1;

    /// <summary>Construct the enforcer.</summary>
    public LicenseTrialEnforcer(
        ILicenseManager license,
        SourceSupervisor sourceSupervisor,
        SinkSupervisor sinkSupervisor,
        LicenseTrialState state,
        IDiagnosticsService diagnostics,
        ILogger<LicenseTrialEnforcer> logger,
        string? licensePath = null,
        ClockAnchorStore? clockAnchors = null,
        TimeSpan? trialDuration = null,
        TimeSpan? checkInterval = null,
        Func<DateTime>? utcNow = null,
        TimeSpan? clockTolerance = null)
    {
        ArgumentNullException.ThrowIfNull(license);
        ArgumentNullException.ThrowIfNull(sourceSupervisor);
        ArgumentNullException.ThrowIfNull(sinkSupervisor);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(logger);

        _license = license;
        _sourceSupervisor = sourceSupervisor;
        _sinkSupervisor = sinkSupervisor;
        _state = state;
        _diagnostics = diagnostics;
        _logger = logger;
        _licensePath = licensePath;
        _clockAnchors = clockAnchors;
        _clockTolerance = clockTolerance ?? ClockRollbackPolicy.DefaultTolerance;
        _trialDuration = trialDuration ?? DefaultTrialDuration;
        _checkInterval = checkInterval ?? DefaultCheckInterval;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>The demo budget of active data collection this instance enforces.</summary>
    public TimeSpan TrialDuration => _trialDuration;

    /// <summary>
    /// Resolve the demo budget from <see cref="TrialMinutesEnvVar"/>, falling
    /// back to <see cref="DefaultTrialDuration"/>. Non-positive or unparseable
    /// values are ignored.
    /// </summary>
    public static TimeSpan ResolveTrialDuration()
    {
        var raw = Environment.GetEnvironmentVariable(TrialMinutesEnvVar);
        if (!string.IsNullOrWhiteSpace(raw)
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes)
            && minutes > 0)
        {
            return TimeSpan.FromMinutes(minutes);
        }
        return DefaultTrialDuration;
    }

    /// <summary>
    /// Pure decision function: given the current status, the time elapsed since
    /// the last check, and whether a source was actively collecting data during
    /// that interval, accumulate the consumed demo budget and decide whether to
    /// stop. <paramref name="demoConsumed"/> is updated in place (reset to zero
    /// whenever the license is Valid, incremented by <paramref name="elapsed"/>
    /// only while collecting). Deterministic and side-effect free so the cutoff
    /// logic is fully unit-testable without timers.
    /// </summary>
    internal static bool ShouldStop(
        LicenseStatus status,
        TimeSpan elapsed,
        bool collectingThisInterval,
        ref TimeSpan demoConsumed,
        TimeSpan trialDuration)
    {
        if (status == LicenseStatus.Valid)
        {
            demoConsumed = TimeSpan.Zero;
            return false;
        }

        if (collectingThisInterval && elapsed > TimeSpan.Zero)
        {
            demoConsumed += elapsed;
        }

        return demoConsumed >= trialDuration;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Version-upgrade check AT LAUNCH: EdgeConnect upgrades are MSI reinstalls,
        // so a new version's first startup is the upgrade checkpoint. Detect and
        // log whether this build differs from the one that last ran here; the
        // license is then validated against it by the version-restriction check
        // in the loop below (blocked only when the license does not cover it —
        // i.e. an expired license older than this build).
        CheckVersionUpgradeAtStartup();

        var demoConsumed = TimeSpan.Zero;
        var lastPointsTotal = SumObservedPoints();
        var lastTickUtc = _utcNow();
        var warnedThisSpell = false;
        // True once data has been torn down (demo budget consumed or version
        // restricted). We STOP the sources/sinks once, but keep looping so the
        // monitor stays alive — a later recovery (clock corrected, license
        // renewed/reactivated) clears the tamper/demo banners without a restart.
        var dataHalted = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            // Clock-rollback guard FIRST. Expiry is judged against the system
            // clock, so an expired license would come back to life if the date
            // were moved back. If the clock is behind the highest time we have
            // ever seen, refuse to honour the license and do NOT reload it from
            // disk — otherwise the sync below would just resurrect it.
            var rolledBack = CheckClockRollback();

            if (!rolledBack)
            {
                // Re-sync with the license FILE: if it was deleted, edited, or
                // replaced since the last check, the in-memory license must
                // follow — otherwise deleting the file would have no effect
                // until a restart.
                await SyncLicenseFileAsync(stoppingToken).ConfigureAwait(false);
            }

            // Refresh expiry status against the wall clock, then evaluate.
            _license.Tick();
            var status = _license.Status;

            // Demo budget is consumed only while data is actively collected:
            // detect collection this interval by a rise in total observed points.
            var now = _utcNow();
            var elapsed = now - lastTickUtc;
            lastTickUtc = now;

            // Charge at most one tick's worth of budget per tick. `elapsed` is a
            // WALL-CLOCK difference, so anything that moves the clock — an NTP
            // correction, a VM or laptop resume, an operator changing the date —
            // lands here as time the gateway supposedly spent collecting. A single
            // forward jump of two hours consumed the entire demo budget in one tick
            // and stopped sources and sinks instantly, which is exactly the window
            // an operator needs to put a replacement license in place. Observed in
            // the field: "01:59:58 remains" followed seconds later by "the
            // 120-minute demo budget has been consumed".
            //
            // The cap is deliberately generous (a few ticks) rather than exact: it
            // only has to reject jumps, not measure them. A backward jump yields a
            // negative span and is floored to zero — CheckClockRollback above is
            // what handles rollback as a tamper condition; it must not also be paid
            // for out of the budget. Both directions therefore err toward giving the
            // customer more demo time, never less, which is the safe side of a rule
            // whose only failure mode is cutting live data.
            var maxChargePerTick = _checkInterval * 4;
            if (elapsed > maxChargePerTick)
            {
                _logger.LogWarning(
                    "System clock moved forward by {Elapsed} between demo-budget checks; charging {Capped} " +
                    "instead. The demo budget measures data-collection time, not clock movement.",
                    elapsed, maxChargePerTick);
                elapsed = maxChargePerTick;
            }
            else if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            var pointsTotal = SumObservedPoints();
            var collecting = pointsTotal > lastPointsTotal;
            lastPointsTotal = pointsTotal;

            // Version restriction: a build NEWER than an expired license covers is
            // not licensed to run. Demo cutoff (ADR-0035): the demo budget of ACTIVE
            // collection has been consumed. Either stops data — but we do NOT exit
            // the loop, so the clock-rollback / license re-sync checks keep running
            // and the operator's later fix (correct the clock, renew/reactivate the
            // license) is picked up and the banners clear on their own.
            var versionRestricted = LicenseVersionPolicy.IsVersionRestricted(
                status, _license.Current, ElpisEdgeConnect.Core.ProductVersion.BuildDateUtc);
            var demoExhausted = ShouldStop(status, elapsed, collecting, ref demoConsumed, _trialDuration);

            // Clock rollback stops data IMMEDIATELY — it does not spend the demo
            // budget first. The budget exists to let an honestly-unlicensed
            // gateway keep running while an operator sorts out a licence; a clock
            // wound back behind a time this gateway has already recorded is not
            // that. It is the one condition here that is deliberate rather than
            // administrative, and the usual way to exploit it is to buy two more
            // hours of collection per restart, which is exactly what the budget
            // would have granted.
            //
            // OWNER DECISION (2026-08-11), narrowing ADR-0035: that ADR gives every
            // non-Valid status the same demo window. This carves the tamper case
            // out of it. Note the cost, which is real — StopDataAsync disposes the
            // adapters, so correcting the clock restores Valid but does NOT restart
            // collection; that needs a service restart, and every surface says so.
            if (versionRestricted || demoExhausted || rolledBack)
            {
                if (!dataHalted)
                {
                    if (rolledBack)
                    {
                        _logger.LogCritical(
                            "System clock rollback detected. STOPPING data collection (sources + destinations) " +
                            "immediately — the demo budget is not granted for a tampered clock. Correct the " +
                            "system date and time, then restart the service to resume data.");
                    }
                    else if (versionRestricted)
                    {
                        _logger.LogCritical(
                            "Software version {Version} (released {BuildDate:yyyy-MM-dd}) is NEWER than the " +
                            "expired license covers. STOPPING data collection — renew or upgrade the license to " +
                            "run this version.",
                            ElpisEdgeConnect.Core.ProductVersion.Version, ElpisEdgeConnect.Core.ProductVersion.BuildDateUtc);
                    }
                    else
                    {
                        _logger.LogCritical(
                            "No valid license (status {Status}). The {Minutes:0}-minute demo budget of ACTIVE data " +
                            "collection has been consumed; STOPPING data collection (sources + destinations) per " +
                            "ADR-0035. The gateway keeps running — activate a valid license and restart the service " +
                            "to resume data.",
                            status, _trialDuration.TotalMinutes);
                    }

                    await StopDataAsync(stoppingToken).ConfigureAwait(false);
                    dataHalted = true;
                }

                if (versionRestricted)
                {
                    _state.MarkVersionRestricted();
                }
                else
                {
                    _state.MarkDataStopped();
                }
            }
            else if (dataHalted)
            {
                // The license/clock has recovered but the sources/sinks were already
                // torn down, so data won't flow again until the service is restarted.
                // Keep the "data stopped — restart to resume" state (honest), while the
                // separate clock-tamper flag has already been cleared by
                // CheckClockRollback above now that the clock is sane.
                _state.MarkDataStopped();
            }
            else if (status == LicenseStatus.Valid)
            {
                _state.SetDemo(null, counting: false);
                warnedThisSpell = false;
            }
            else
            {
                var remaining = _trialDuration - demoConsumed;
                if (remaining < TimeSpan.Zero)
                {
                    remaining = TimeSpan.Zero;
                }

                // Publish the demo budget left + whether it is currently counting
                // (a source is collecting) for the UI banner.
                _state.SetDemo(remaining, counting: collecting);

                // Log once loudly at the start of an unlicensed spell, then keep a
                // steady (debug-level) heartbeat so logs aren't flooded.
                if (!warnedThisSpell)
                {
                    _logger.LogWarning(
                        "Running WITHOUT a valid license (status {Status}). {Remaining} of demo data-collection " +
                        "time remains; it is consumed only while a source is actively collecting data (ADR-0035).",
                        status, remaining);
                    warnedThisSpell = true;
                }
                else
                {
                    _logger.LogDebug(
                        "Unlicensed runtime (status {Status}); ~{Remaining} demo time left, counting={Counting}.",
                        status, remaining, collecting);
                }
            }

            try
            {
                await Task.Delay(_checkInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return; // expected on shutdown
            }
        }
    }

    /// <summary>
    /// Detect a system-clock rollback and, if found, refuse to honour the
    /// license (revert to unlicensed / demo mode).
    ///
    /// <para>Expiry is evaluated against the system clock, so an expired license
    /// would simply start working again if the machine's date were wound back
    /// before its <c>expiresAt</c>. We defeat that by persisting a monotonic
    /// high-water mark of the latest time ever observed: time may only move
    /// forward. If the clock is now meaningfully behind that mark, it has been
    /// set back.</para>
    ///
    /// <para>While rolled back we deliberately do NOT advance the anchor and do
    /// NOT reload the license file, so the state is stable (no flapping) until
    /// the clock is corrected — at which point the license reloads and its real
    /// status (Valid / Expired) applies again.</para>
    /// </summary>
    /// <returns>true when the clock is rolled back and the license was refused.</returns>
    private bool CheckClockRollback()
    {
        if (_clockAnchors is null)
        {
            return false; // no anchor store configured (tests / embedded use)
        }

        var now = _utcNow();
        var anchor = _clockAnchors.Read();

        // Real time elapsed since the last anchor WRITE, measured from a MONOTONIC
        // clock (Environment.TickCount64) so it is immune to wall-clock changes.
        // Used to cap how far forward the anchor may move.
        //
        // Measured from the last write, NOT the last check: those are now separate
        // cadences (see AnchorWriteInterval). Timing it from the check would cap
        // each write to a single tick's worth of movement, so the anchor would
        // creep at a fraction of real time and eventually sit far enough behind
        // that genuine rollbacks stopped registering.
        var nowMonotonic = Environment.TickCount64;
        var realElapsed = _lastAnchorWriteMs < 0
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(Math.Max(0, nowMonotonic - _lastAnchorWriteMs));
        _lastMonotonicMs = nowMonotonic;

        // A license can never be in use before the day it was issued — catches
        // gross date tampering even on a fresh install with no anchor yet. Sticky,
        // so the state doesn't flap once the license has been unloaded.
        if (ClockRollbackPolicy.IsBeforeIssue(now, _license.Current))
        {
            _minValidUtc = _license.Current!.IssuedAt;
        }
        var beforeIssue = _minValidUtc is { } min && now.Date < min.Date;

        var rolledBack = ClockRollbackPolicy.IsRollback(now, anchor, _clockTolerance) || beforeIssue;

        // Captured before the sane-branch clears it, so a recovery can force the
        // anchor write immediately rather than waiting out the write interval.
        var wasTampered = _state.ClockTampered;

        if (!rolledBack)
        {
            // Clock is sane — clear any sticky before-issue / tamper state. This
            // is what makes correcting the clock restore the banner-free state on
            // the very next tick, so recovery is as prompt as detection.
            _minValidUtc = null;
            _clockTamperLogged = false;
            _state.SetClockTampered(false);

            // Advancing the high-water mark (capped to real elapsed time so a
            // forward jump can't poison it) writes a file per anchor location, so
            // it runs on its own slower cadence — see AnchorWriteInterval. Always
            // write the FIRST sane tick of a run, and always right after clearing
            // a tamper, so the recovered anchor is durable immediately rather
            // than up to five seconds later.
            var firstWriteThisRun = _lastAnchorWriteMs < 0;
            var dueForWrite = firstWriteThisRun
                || TimeSpan.FromMilliseconds(nowMonotonic - _lastAnchorWriteMs) >= AnchorWriteInterval;
            if (dueForWrite || wasTampered)
            {
                // CATCH-UP ON START. Advance() caps forward movement to the time
                // this PROCESS has observed, which is right while running — it is
                // what stops a forward clock jump poisoning the anchor. But it
                // also means every hour the gateway is stopped, the anchor falls
                // an hour behind real time and can never make it up: after a
                // night off, the anchor sat ~20h in the past and the operator
                // could wind the clock back most of a day without tripping
                // anything. The detector had a dead zone the size of its
                // downtime, which is precisely when tampering is easiest.
                //
                // On the first sane tick of a run we therefore adopt `now`
                // outright. That only ever RAISES the high-water mark — it is
                // reached solely when the clock is already sane (not rolled back
                // versus the stored anchor), so it cannot be used to lower the
                // bar. Winding the clock forward, restarting, then winding back
                // is caught harder than before, because the anchor took the
                // forward time with it.
                var advanced = firstWriteThisRun && now > (anchor ?? DateTime.MinValue)
                    ? now
                    : ClockRollbackPolicy.Advance(now, anchor, realElapsed, _clockTolerance);
                _clockAnchors.Write(advanced);
                _lastAnchorWriteMs = nowMonotonic;
            }

            return false;
        }

        if (!_clockTamperLogged)
        {
            _logger.LogCritical(
                "SYSTEM CLOCK ROLLBACK DETECTED. Current time {Now:u} is earlier than the latest time this " +
                "gateway has already observed ({Anchor:u}). The license will NOT be honoured while the clock " +
                "is set back — reverting to UNLICENSED (demo) mode. Correct the system clock to restore " +
                "licensed operation.",
                now, anchor);
            _clockTamperLogged = true;
        }

        // Surface the tamper condition to the UI so the operator is told WHY the
        // gateway dropped to demo mode.
        _state.SetClockTampered(true);

        if (_license.Current is not null)
        {
            _license.Unload();
        }
        return true;
    }

    /// <summary>
    /// Keep the in-memory license in step with the license FILE on disk.
    ///
    /// <para>Without this, the license is only ever read at startup, so deleting
    /// (or tampering with) the file would leave the gateway running licensed
    /// until the next restart — a service could stay licensed for months after
    /// the file is gone. Re-checking every cycle means:</para>
    /// <list type="bullet">
    ///   <item>file deleted → the license is <see cref="ILicenseManager.Unload"/>ed
    ///         → status becomes NotLoaded → demo mode takes over;</item>
    ///   <item>file edited / tampered → the signature no longer verifies, the load
    ///         throws → unloaded → demo mode;</item>
    ///   <item>file added / replaced with a valid one → picked up automatically,
    ///         no restart needed.</item>
    /// </list>
    /// </summary>
    internal async Task SyncLicenseFileAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_licensePath))
        {
            return; // no path configured (tests / embedded use) — nothing to watch
        }

#pragma warning disable CA1031 // a bad license file must never crash the host
        try
        {
            if (!File.Exists(_licensePath))
            {
                if (_license.Current is not null)
                {
                    _logger.LogWarning(
                        "License file '{Path}' is no longer present. Discarding the in-memory license and " +
                        "reverting to UNLICENSED (demo) mode.",
                        _licensePath);
                    _license.Unload();
                }
                _licenseFileWriteUtc = null;
                _licenseFileLength = -1;
                return;
            }

            var info = new FileInfo(_licensePath);

            // Deliberately NOT gated on _license.Current being non-null. The
            // failure path below calls Unload(), so including that clause left
            // `unchanged` false on every later tick for a file that cannot load —
            // re-reading and re-logging an error once per second, forever, even
            // though the stamping further down exists precisely to stop that.
            // Measured against a tampered license: ~19 error-level lines per 20s,
            // roughly 86,000 a day from one bad file, which buries everything else
            // in the log.
            //
            // The stamps alone answer the only question being asked here — "is
            // this the same file I already processed?" — and the delete branch
            // above clears them, so a restored or replaced file is still picked up
            // on the next tick.
            var unchanged = _licenseFileWriteUtc == info.LastWriteTimeUtc
                && _licenseFileLength == info.Length;
            if (unchanged)
            {
                return;
            }

            // New, restored, or modified file — (re)load it. This re-verifies the
            // signature and the gateway binding, so a tampered file fails here.
            try
            {
                await _license.LoadFromFileAsync(_licensePath, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "License file '{Path}' loaded; status is now {Status}.", _licensePath, _license.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "License file '{Path}' could not be verified (missing, corrupt, tampered, or bound to a " +
                    "different gateway). Reverting to UNLICENSED (demo) mode.",
                    _licensePath);
                _license.Unload();
            }

            // Stamp either way, so an unreadable file isn't retried every cycle.
            _licenseFileWriteUtc = info.LastWriteTimeUtc;
            _licenseFileLength = info.Length;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while re-checking the license file '{Path}'.", _licensePath);
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Detect and log a product-version change since the last run (a version
    /// upgrade/downgrade), by comparing the running product version
    /// to the value persisted next to the license. Records the current version
    /// for the next launch. Purely observational — the licence coverage of the
    /// (possibly new) version is enforced by the version-restriction check.
    /// </summary>
    private void CheckVersionUpgradeAtStartup()
    {
        if (string.IsNullOrWhiteSpace(_licensePath))
        {
            return; // no data-root path configured (tests / embedded use)
        }

        var dir = Path.GetDirectoryName(_licensePath);
        if (string.IsNullOrEmpty(dir))
        {
            return;
        }

        var path = Path.Combine(dir, ".edgeconnect-version");
        var current = ElpisEdgeConnect.Core.ProductVersion.Version;

#pragma warning disable CA1031 // version tracking must never crash the host
        try
        {
            var previous = File.Exists(path) ? File.ReadAllText(path).Trim() : null;

            if (string.IsNullOrEmpty(previous))
            {
                _logger.LogInformation(
                    "Version check at launch: first run recorded at version {Version}.", current);
            }
            else if (!string.Equals(previous, current, StringComparison.Ordinal))
            {
                var upgrade = string.CompareOrdinal(current, previous) > 0;
                _logger.LogInformation(
                    "Version {Kind} detected at launch: {Previous} -> {Current}. Validating the license " +
                    "against the new version.",
                    upgrade ? "UPGRADE" : "change", previous, current);
            }
            else
            {
                _logger.LogDebug("Version check at launch: unchanged ({Version}).", current);
            }

            Directory.CreateDirectory(dir);
            File.WriteAllText(path, current);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not record the product version for launch-time upgrade detection.");
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Total data points observed across all sources right now (deduped by
    /// source instance). A rise between two checks means a source is actively
    /// collecting data during the interval.
    /// </summary>
    private long SumObservedPoints()
    {
        long total = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var route in _diagnostics.GetAllRouteSnapshots())
        {
            var src = route.Source;
            if (src is not null && seen.Add(src.SourceInstanceId))
            {
                total += src.PointsObserved;
            }
        }
        return total;
    }

    /// <summary>
    /// Stop reading (sources) and publishing (sinks). Never lets an error crash
    /// the host — the whole point is that the gateway keeps running.
    /// </summary>
    private async Task StopDataAsync(CancellationToken cancellationToken)
    {
#pragma warning disable CA1031 // must not let a stop failure crash the host
        try
        {
            await _sourceSupervisor.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping source data collection on demo cutoff.");
        }

        try
        {
            await _sinkSupervisor.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping destination publishing on demo cutoff.");
        }
#pragma warning restore CA1031
    }
}
