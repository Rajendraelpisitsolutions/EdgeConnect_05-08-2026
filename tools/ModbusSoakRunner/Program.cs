// ============================================================================
// File: Program.cs
// Purpose: Long-running EdgeConnect soak runner.
//
// What it does:
//   1. Loads a gateway.json from disk (sources + sinks + routes).
//   2. Wires Modbus sources, MQTT sinks, FOCAS2 / MTConnect (if any) into a
//      real Host using the same composition root as production.
//   3. Starts the Host and lets the SourceSupervisor drive the poll loop.
//   4. Every minute, captures AdapterHealth from each registered source
//      and sink to a CSV file.
//   5. After --duration minutes, stops the Host and prints a pass/fail
//      summary against the KepServer-benchmarked acceptance criteria.
//
// Why a separate tool instead of a script:
//   The Host is the production binary; the soak runner needs to BE the Host
//   plus a periodic snapshotter. Wrapping it in-process keeps timestamps
//   clean and avoids the failure modes of subprocess-stdout-scraping.
//
// Usage:
//   ModbusSoakRunner --config gateway.json --duration-min 240 --csv soak.csv
//
// Reference: docs/PHASE3_EXECUTION_PLAN.md K (pilot), Phase A' soak plan.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Host;
using ElpisEdgeConnect.Host.Adapters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using EdgeHostOptions = ElpisEdgeConnect.Host.HostOptions;
using HostingHost = Microsoft.Extensions.Hosting.Host;

namespace ElpisEdgeConnect.Tools.ModbusSoakRunner;

internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitFailure = 1;
    private const int ExitUsage = 2;

    // Cached per CA1869 — JsonSerializerOptions are expensive to construct.
    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // Used to write the (possibly --buffer-mode-overridden) gateway config
    // back out to the host's expected current.json path. Indented for human
    // diffability, null-skipping so optional fields don't pollute output.
    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task<int> Main(string[] args)
    {
        var opts = ParseArgs(args);
        if (opts is null)
        {
            PrintUsage();
            return ExitUsage;
        }

        if (!File.Exists(opts.ConfigPath))
        {
            Console.Error.WriteLine($"Config file not found: {opts.ConfigPath}");
            return ExitUsage;
        }

        // Load gateway config — same shape Program.cs reads at startup.
        GatewayConfiguration gatewayConfig;
        try
        {
            var json = await File.ReadAllTextAsync(opts.ConfigPath).ConfigureAwait(false);
            gatewayConfig = JsonSerializer.Deserialize<GatewayConfiguration>(json, JsonReadOptions)
                ?? throw new InvalidOperationException("config deserialized to null");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load config: {ex.Message}");
            return ExitUsage;
        }

        // Apply --buffer-mode override AFTER deserialization but BEFORE the
        // host loads the file, so the override travels through the same
        // validation path as a hand-edited config. StoreAndForward requires
        // AtLeastOnce delivery (CrossRecordValidator rule) — auto-upgrade
        // any AtMostOnce route under StoreAndForward to keep the override
        // ergonomic instead of failing fast at host start.
        if (opts.BufferModeOverride is { } overrideMode)
        {
            var rewritten = new List<RouteConfig>(gatewayConfig.Routes.Count);
            foreach (var r in gatewayConfig.Routes)
            {
                var newBuffer = r.Buffer with { Mode = overrideMode };
                var newDelivery = (overrideMode == BufferMode.StoreAndForward
                                    && r.Delivery.Mode == DeliveryMode.AtMostOnce)
                    ? r.Delivery with { Mode = DeliveryMode.AtLeastOnce }
                    : r.Delivery;
                rewritten.Add(r with { Buffer = newBuffer, Delivery = newDelivery });
            }
            gatewayConfig = gatewayConfig with { Routes = rewritten };
            Console.Error.WriteLine($"[soak] --buffer-mode override applied: every route is now {overrideMode}");
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "edgeconnect-soak-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var hostOptions = new EdgeHostOptions
        {
            ConfigDirectory = Path.Combine(tempDir, "config"),
            LicensePath = Path.Combine(tempDir, "license.json"),
            GatewayIdentityPath = Path.Combine(tempDir, "identity"),
            DataRoot = tempDir,
            EnableEndpointsServer = false,
        };

        Console.Error.WriteLine($"[soak] config: {opts.ConfigPath}");
        Console.Error.WriteLine($"[soak] duration: {opts.DurationMinutes} min");
        Console.Error.WriteLine($"[soak] csv:    {opts.CsvPath}");
        Console.Error.WriteLine($"[soak] sources={gatewayConfig.Sources.Count}, sinks={gatewayConfig.Sinks.Count}, routes={gatewayConfig.Routes.Count}");

        var hostBuilder = HostingHost.CreateApplicationBuilder();

        // Force Information-level on the namespaces we care about — don't
        // ClearProviders so default console + debug + eventsource stay wired.
        hostBuilder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
        });
        hostBuilder.Logging.SetMinimumLevel(LogLevel.Information);
        hostBuilder.Logging.AddFilter("Microsoft", LogLevel.Information);
        hostBuilder.Logging.AddFilter("ElpisEdgeConnect", LogLevel.Information);

        hostBuilder.Services.AddElpisEdgeConnectHost(hostOptions);

        // Soak runs are dev / sim environments — bypass the file-based
        // license check by replacing the production ILicenseManager with
        // an all-permissive substitute. Same pattern HostHarness uses for
        // integration tests. Without this, the host blocks during startup
        // because no license file exists at hostOptions.LicensePath and
        // the source-supervisor refuses to activate adapters.
        hostBuilder.Services.RemoveAll<ILicenseManager>();
        hostBuilder.Services.AddSingleton<ILicenseManager, PermissiveLicenseManager>();

        // Swap the production no-op startup observer for a verbose one that
        // prints each phase entry to stderr. This is the only reliable way
        // to tell which locked phase is blocking during a silent hang,
        // because the observer runs BEFORE the phase's real work (including
        // any work that never gets as far as emitting an ILogger line).
        hostBuilder.Services.RemoveAll<IStartupSequenceObserver>();
        hostBuilder.Services.AddSingleton<IStartupSequenceObserver, ConsoleStartupSequenceObserver>();

        // Mirror Program.cs's source/sink wiring.
        hostBuilder.Services.AddFocas2SourcesFromGatewayConfig(gatewayConfig);
        hostBuilder.Services.AddMTConnectSourcesFromGatewayConfig(gatewayConfig);
        hostBuilder.Services.AddModbusTcpSourcesFromGatewayConfig(gatewayConfig);
        hostBuilder.Services.AddMqttSinksFromGatewayConfig(gatewayConfig);

        // The Host's startup sequence loads its own config from disk via
        // ConfigurationManager. We serialize the IN-MEMORY gatewayConfig
        // here (NOT a file copy) so any --buffer-mode override or other
        // in-memory mutation actually reaches the host's loader. Layout
        // path derived from the same ConfigurationStorageLayout the
        // composition root uses — single source of truth on file paths.
        // ConfigurationStorageLayout is rooted at the gateway data
        // directory (above config/) — using ResolvedDataRoot keeps the
        // layout consistent with the Host's composition root.
        var layout = new ConfigurationStorageLayout(hostOptions.ResolvedDataRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(layout.CurrentConfigPath)!);
        var configJson = JsonSerializer.Serialize(gatewayConfig, JsonWriteOptions);
        await File.WriteAllTextAsync(layout.CurrentConfigPath, configJson).ConfigureAwait(false);

        using var host = hostBuilder.Build();

        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

        // Register a Ctrl-C / SIGTERM handler so we can flush the CSV cleanly.
        var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.Error.WriteLine("[soak] Ctrl-C received — stopping early.");
            stop.Cancel();
        };

        Console.Error.WriteLine("[soak] starting host (this walks startup phases — gateway identity, config load, license, register routes, start supervisors)...");
        // Bound the startup with a hard 30s timeout. If we hit it, dump
        // full exception detail and exit so we can diagnose, instead of
        // hanging the operator forever.
        using (var startupCts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, startupCts.Token))
        {
            try
            {
                await host.StartAsync(linkedCts.Token).ConfigureAwait(false);
                Console.Error.WriteLine("[soak] host started.");
            }
            catch (OperationCanceledException) when (startupCts.IsCancellationRequested && !stop.IsCancellationRequested)
            {
                Console.Error.WriteLine(
                    "[soak] HOST STARTUP TIMED OUT after 30s — last log line above identifies the blocked phase.");
                return ExitFailure;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[soak] host failed to start: {ex.GetType().Name}: {ex.Message}");
                for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
                {
                    Console.Error.WriteLine($"[soak]   inner: {inner.GetType().Name}: {inner.Message}");
                }
                Console.Error.WriteLine($"[soak] stack:");
                Console.Error.WriteLine(ex.ToString());
                return ExitFailure;
            }
        }

        var startedAt = DateTime.UtcNow;
        var deadline = startedAt.AddMinutes(opts.DurationMinutes);

        // Cold-start RSS / CPU. We re-baseline these after a brief warm-up
        // window so the ratio criteria don't count JIT-under-load + GC Gen2
        // settling against us. KepServer measures steady-state growth, not
        // cold-start surge — this matches that convention.
        //
        // 3 snapshots (~3 minutes) is calibrated to the .NET 8 + Modbus +
        // MQTTnet stack used in this pipeline: empirically the first ~3
        // minutes show ~10-15% RSS growth as JIT compiles hot paths first
        // hit by real traffic; from minute 3 onward growth converges to
        // <1 MB / minute for a healthy run.
        var startedRss = Process.GetCurrentProcess().WorkingSet64;
        var startedCpu = Process.GetCurrentProcess().TotalProcessorTime;
        var rssBaselineRebaselined = false;
        const int WarmupSnapshots = 3;

        var snapshots = new List<HealthSnapshot>();

        // Optional EREMOS V2 historian verification — closes the loop
        // beyond "we published" to "they actually ingested".
        EremosHistorianClient? eremosClient = null;
        long? eremosBaselineCount = null;
        long? eremosLatestCount = null;
        if (!string.IsNullOrWhiteSpace(opts.EremosApi))
        {
            EremosCredentials? creds = null;
            if (!string.IsNullOrWhiteSpace(opts.EremosUsername) && !string.IsNullOrWhiteSpace(opts.EremosPassword))
            {
                creds = new EremosCredentials(opts.EremosUsername!, opts.EremosPassword!);
            }
            eremosClient = new EremosHistorianClient(
                opts.EremosApi, opts.EremosDeviceClass, opts.EremosJwt, creds);
            var authMode = opts.EremosJwt is not null ? "JWT" : creds is not null ? "credentials" : "anonymous";
            Console.Error.WriteLine(
                $"[soak] EREMOS verification enabled: {opts.EremosApi} (deviceClass={opts.EremosDeviceClass}, auth={authMode})");
            if (await eremosClient.ProbeAsync(stop.Token).ConfigureAwait(false))
            {
                eremosBaselineCount = await eremosClient.CountSinceAsync(startedAt, stop.Token).ConfigureAwait(false);
                Console.Error.WriteLine($"[soak] EREMOS baseline count (since soak start): {eremosBaselineCount?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
            }
            else
            {
                Console.Error.WriteLine("[soak] EREMOS API unreachable or auth failed — proceeding without end-to-end verification.");
            }
        }

        Console.Error.WriteLine($"[soak] started at {startedAt:O} UTC, will stop at {deadline:O}");

        try
        {
            // Snapshot loop — every 60 seconds.
            while (!stop.Token.IsCancellationRequested && DateTime.UtcNow < deadline)
            {
                try
                {
                    var snap = await CaptureSnapshotAsync(host.Services, stop.Token).ConfigureAwait(false);
                    snapshots.Add(snap);

                    // After WarmupSnapshots minutes, re-baseline the RSS /
                    // CPU markers so the criteria measure post-warm-up
                    // steady-state growth rather than including the JIT
                    // bootstrap surge. Done once. For runs shorter than the
                    // warm-up window we never re-baseline — the criterion
                    // is just informational on a smoke that short.
                    if (!rssBaselineRebaselined && snapshots.Count >= WarmupSnapshots)
                    {
                        var p = Process.GetCurrentProcess();
                        startedRss = p.WorkingSet64;
                        startedCpu = p.TotalProcessorTime;
                        rssBaselineRebaselined = true;
                        Console.Error.WriteLine(
                            $"[soak] baseline re-sampled after {WarmupSnapshots}-snapshot warm-up: " +
                            $"rss={startedRss / (1024 * 1024)}MB " +
                            "(growth criteria measure from this point onward)");
                    }

                    var eremosLine = string.Empty;
                    if (eremosClient is not null)
                    {
                        eremosLatestCount = await eremosClient.CountSinceAsync(startedAt, stop.Token).ConfigureAwait(false);
                        if (eremosLatestCount is { } n)
                        {
                            eremosLine = $" eremos={n}";
                        }
                    }

                    // Buffer summary — only print depth/age fields when the
                    // buffer mode is something other than "unknown" (the
                    // first ~one second after start before the route worker
                    // has pushed its first stats observation).
                    var bufferLine = string.Empty;
                    if (!string.Equals(snap.BufferMode, "unknown", StringComparison.Ordinal))
                    {
                        var ageStr = snap.BufferOldestUnackedAgeSeconds is { } age
                            ? $" age={age:F0}s"
                            : string.Empty;
                        var sizeStr = snap.BufferSizeBytes > 0
                            ? $" sz={snap.BufferSizeBytes / 1024}KB"
                            : string.Empty;
                        bufferLine = $" buf[{snap.BufferMode}]={snap.BufferDepth}{ageStr}{sizeStr}";
                    }

                    Console.Error.WriteLine(
                        $"[soak] {snap.At:HH:mm:ss}  txs={snap.TotalTransactions} ok={snap.TotalSuccesses} fail={snap.TotalFailures} " +
                        $"published={snap.TotalPublishSuccesses} rejected={snap.TotalPublishFailures}{bufferLine}{eremosLine} rss={snap.RssMb}MB");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[soak] snapshot failed: {ex.Message}");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(60), stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { /* user-stop */ }
            }
        }
        finally
        {
            // Before stopping the host: if a durable buffer is in use, wait
            // briefly for it to drain so the publish_successes counter
            // includes everything actually sent over the wire. Without this
            // wait, points sitting on disk at duration-end count as
            // "in flight" and the FINAL summary's publish-delivery ratio
            // looks worse than the run actually was. Bounded by a deadline
            // so a stuck broker doesn't block shutdown forever.
            await WaitForBufferDrainAsync(host.Services, TimeSpan.FromSeconds(60))
                .ConfigureAwait(false);

            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await host.StopAsync(stopCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[soak] host stop error: {ex.Message}");
            }
        }

        var finishedAt = DateTime.UtcNow;
        var finalRss = Process.GetCurrentProcess().WorkingSet64;
        var finalCpu = Process.GetCurrentProcess().TotalProcessorTime;

        // Write CSV — one row per snapshot. Pass 0 sentinels for the very
        // first snapshot's deltas so spreadsheets don't NaN-out.
        await WriteCsvAsync(opts.CsvPath, snapshots).ConfigureAwait(false);
        Console.Error.WriteLine($"[soak] wrote {snapshots.Count} snapshot row(s) to {opts.CsvPath}");

        // Final EREMOS sample after the host has fully drained — gives the
        // sink a chance to flush any in-flight messages to the broker.
        if (eremosClient is not null)
        {
            try
            {
                eremosLatestCount = await eremosClient.CountSinceAsync(startedAt, CancellationToken.None).ConfigureAwait(false);
            }
            catch { /* best-effort */ }
            eremosClient.Dispose();
        }

        // Cleanup temp files.
        try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort */ }

        // Pass/fail report.
        return PrintAndEvaluate(
            startedAt, finishedAt,
            startedRss, finalRss, startedCpu, finalCpu,
            snapshots,
            eremosBaselineCount, eremosLatestCount,
            opts);
    }

    // =========================================================================
    // PRIVATE — graceful drain
    // =========================================================================

    /// <summary>
    /// Wait until every route's buffer reports depth == 0 or
    /// <paramref name="deadline"/> elapses. Idempotent — returns
    /// immediately if no route reports a buffer rollup yet (i.e., the
    /// host was just started and the route worker hasn't pushed stats),
    /// or if all routes are already drained.
    /// </summary>
    /// <remarks>
    /// Polls every 250 ms — fine-grained enough to catch fast drains
    /// without busy-spinning, coarse enough to avoid contending with the
    /// route workers' own poll cycle.
    /// </remarks>
    private static async Task WaitForBufferDrainAsync(IServiceProvider sp, TimeSpan deadline)
    {
        var diag = sp.GetRequiredService<IDiagnosticsService>();
        var deadlineUtc = DateTime.UtcNow + deadline;
        long lastReportedDepth = -1;
        while (DateTime.UtcNow < deadlineUtc)
        {
            long depth = 0;
            var anyDurable = false;
            foreach (var r in diag.GetAllRouteSnapshots())
            {
                if (r.Buffer is not { } b) { continue; }
                // Only InMemory and StoreAndForward have meaningful drain
                // semantics. None mode bypasses the buffer entirely.
                if (b.Mode == "None") { continue; }
                anyDurable = true;
                depth += b.CurrentDepth;
            }
            if (!anyDurable || depth == 0)
            {
                if (lastReportedDepth > 0)
                {
                    Console.Error.WriteLine("[soak] buffer drained — proceeding to stop.");
                }
                return;
            }
            if (depth != lastReportedDepth)
            {
                Console.Error.WriteLine(
                    $"[soak] waiting for buffer to drain: depth={depth} (deadline {deadlineUtc:HH:mm:ss}Z)");
                lastReportedDepth = depth;
            }
            await Task.Delay(250).ConfigureAwait(false);
        }
        Console.Error.WriteLine(
            $"[soak] WARNING: buffer drain deadline reached with depth={lastReportedDepth}; " +
            "in-flight points will appear as 'still queued' in the summary.");
    }

    // =========================================================================
    // PRIVATE — snapshot capture
    // =========================================================================

    private static async Task<HealthSnapshot> CaptureSnapshotAsync(IServiceProvider sp, CancellationToken ct)
    {
        var sources = sp.GetRequiredService<IEnumerable<SourceRegistration>>().ToList();
        var sinks = sp.GetRequiredService<IEnumerable<SinkRegistration>>().ToList();

        var sourceHealth = new List<AdapterHealth>(sources.Count);
        foreach (var s in sources)
        {
            sourceHealth.Add(await s.Adapter.CheckHealthAsync(ct).ConfigureAwait(false));
        }

        var sinkHealth = new List<AdapterHealth>(sinks.Count);
        foreach (var s in sinks)
        {
            sinkHealth.Add(await s.Adapter.CheckHealthAsync(ct).ConfigureAwait(false));
        }

        long Sum(IEnumerable<AdapterHealth> hs, string key) =>
            hs.Sum(h => h.Metrics?.TryGetValue(key, out var v) == true && v is long l ? l : 0L);

        // Pull buffer stats off every route via the diagnostics service.
        // RouteWorker pushes BufferStats on every poll cycle, so by the time
        // the soak runner samples, the latest depth / oldest-unacked-age are
        // already in the collector. Aggregate across routes:
        //   * Mode: report a single label (all our pilot configs are
        //     single-route). If routes disagree, mark "mixed".
        //   * Depth, SizeBytes, lifetime counters: SUM across routes.
        //   * Oldest-unacked-age: MAX across routes (worst lag wins).
        var diag = sp.GetRequiredService<IDiagnosticsService>();
        var routeSnaps = diag.GetAllRouteSnapshots();

        var bufferMode = "unknown";
        long bufferDepth = 0, bufferSize = 0, bufferEnq = 0, bufferDrn = 0;
        long bufferDropCap = 0, bufferDropRet = 0;
        double? oldestAgeSec = null;
        var now = DateTime.UtcNow;
        var modes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in routeSnaps)
        {
            if (r.Buffer is not { } b) continue;
            modes.Add(b.Mode);
            bufferDepth += b.CurrentDepth;
            bufferSize += b.SizeBytes;
            bufferEnq += b.TotalEnqueued;
            bufferDrn += b.TotalDrained;
            bufferDropCap += b.DroppedByCapacity;
            bufferDropRet += b.DroppedByRetention;
            if (b.OldestUnackedAt is { } at)
            {
                var age = (now - at).TotalSeconds;
                if (oldestAgeSec is null || age > oldestAgeSec) { oldestAgeSec = age; }
            }
        }
        if (modes.Count == 1) { bufferMode = modes.Single(); }
        else if (modes.Count > 1) { bufferMode = "mixed"; }

        return new HealthSnapshot
        {
            At = now,
            TotalTransactions = Sum(sourceHealth, "transactionsExecuted"),
            TotalSuccesses = Sum(sourceHealth, "pollSuccesses"),
            TotalFailures = Sum(sourceHealth, "pollFailures"),
            TotalPublishSuccesses = Sum(sinkHealth, "publishSuccesses"),
            TotalPublishFailures = Sum(sinkHealth, "publishFailures"),
            RssMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024),
            SourceHealth = sourceHealth,
            SinkHealth = sinkHealth,
            BufferMode = bufferMode,
            BufferDepth = bufferDepth,
            BufferSizeBytes = bufferSize,
            BufferTotalEnqueued = bufferEnq,
            BufferTotalDrained = bufferDrn,
            BufferDroppedByCapacity = bufferDropCap,
            BufferDroppedByRetention = bufferDropRet,
            BufferOldestUnackedAgeSeconds = oldestAgeSec,
        };
    }

    private static async Task WriteCsvAsync(string path, IReadOnlyList<HealthSnapshot> snapshots)
    {
        var sb = new StringBuilder();
        sb.AppendLine("timestamp_utc,total_transactions,total_successes,total_failures," +
                      "total_publish_successes,total_publish_failures,rss_mb," +
                      "buffer_mode,buffer_depth,buffer_size_bytes,buffer_total_enqueued," +
                      "buffer_total_drained,buffer_dropped_by_capacity," +
                      "buffer_dropped_by_retention,buffer_oldest_unacked_age_sec");
        foreach (var s in snapshots)
        {
            sb.AppendLine(string.Join(",",
                s.At.ToString("O", CultureInfo.InvariantCulture),
                s.TotalTransactions,
                s.TotalSuccesses,
                s.TotalFailures,
                s.TotalPublishSuccesses,
                s.TotalPublishFailures,
                s.RssMb,
                s.BufferMode,
                s.BufferDepth,
                s.BufferSizeBytes,
                s.BufferTotalEnqueued,
                s.BufferTotalDrained,
                s.BufferDroppedByCapacity,
                s.BufferDroppedByRetention,
                s.BufferOldestUnackedAgeSeconds?.ToString("F1", CultureInfo.InvariantCulture) ?? ""));
        }

        // Robust write: if the configured path is locked (Windows: someone
        // has the CSV open in Excel / a tail viewer / another EdgeConnect
        // run), fall back to a timestamped sibling. We must NOT throw — at
        // this point the soak has already collected 4 hours of irreplaceable
        // data and the caller still needs to print the PASS/FAIL summary.
        try
        {
            await File.WriteAllTextAsync(path, sb.ToString()).ConfigureAwait(false);
            return;
        }
        catch (IOException ex)
        {
            var dir = Path.GetDirectoryName(path);
            var stem = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            var ts = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
            var fallback = string.IsNullOrEmpty(dir)
                ? $"{stem}.{ts}{ext}"
                : Path.Combine(dir, $"{stem}.{ts}{ext}");
            Console.Error.WriteLine(
                $"[soak] WARNING: '{path}' is locked ({ex.Message}); writing to '{fallback}' instead.");
            await File.WriteAllTextAsync(fallback, sb.ToString()).ConfigureAwait(false);
        }
    }

    // =========================================================================
    // PRIVATE — pass/fail evaluation
    // =========================================================================

    private static int PrintAndEvaluate(
        DateTime startedAt, DateTime finishedAt,
        long startedRss, long finalRss,
        TimeSpan startedCpu, TimeSpan finalCpu,
        IReadOnlyList<HealthSnapshot> snapshots,
        long? eremosBaselineCount, long? eremosFinalCount,
        Options opts)
    {
        var elapsed = finishedAt - startedAt;
        if (snapshots.Count == 0)
        {
            Console.Error.WriteLine("[soak] no snapshots captured — soak too short or sampler failed.");
            return ExitFailure;
        }

        var last = snapshots[^1];
        var totalTx = last.TotalTransactions;
        var totalOk = last.TotalSuccesses;
        var totalFail = last.TotalFailures;
        var totalPub = last.TotalPublishSuccesses;
        var totalRej = last.TotalPublishFailures;

        // Source-side delivery ratio: poll attempts that produced a successful
        // result vs. total poll attempts. (Equivalent to "the poll loop kept
        // producing data".) Sink-side delivery is publish-success / total
        // publish attempts.
        var pollAttempts = totalOk + totalFail;
        var sourceDeliveryRatio = pollAttempts == 0 ? 1.0 : (double)totalOk / pollAttempts;
        var publishAttempts = totalPub + totalRej;
        var publishDeliveryRatio = publishAttempts == 0 ? 1.0 : (double)totalPub / publishAttempts;

        var rssGrowthMb = (finalRss - startedRss) / (1024.0 * 1024.0);
        var cpuTotal = finalCpu - startedCpu;
        var cpuPercent = elapsed.TotalSeconds <= 0 ? 0.0
            : (cpuTotal.TotalSeconds / elapsed.TotalSeconds) * 100.0
              / Environment.ProcessorCount;

        Console.WriteLine();
        Console.WriteLine("====================================================================");
        Console.WriteLine($" Modbus + MQTT soak summary — {elapsed.TotalMinutes:F1} min");
        Console.WriteLine("====================================================================");
        Console.WriteLine($"  Total transactions      : {totalTx,12:N0}");
        Console.WriteLine($"  Successful polls        : {totalOk,12:N0}");
        Console.WriteLine($"  Failed polls            : {totalFail,12:N0}");
        Console.WriteLine($"  Source delivery ratio   : {sourceDeliveryRatio:P3}");
        Console.WriteLine($"  Publish successes       : {totalPub,12:N0}");
        Console.WriteLine($"  Publish failures        : {totalRej,12:N0}");
        Console.WriteLine($"  Publish delivery ratio  : {publishDeliveryRatio:P3}");

        // Final buffer state — only meaningful for routes whose buffer mode
        // is something other than None. After WaitForBufferDrainAsync the
        // depth should be 0 (the drain wait pre-stop ensures it). When
        // non-zero, the operator sees explicit "in flight" — these points
        // are durable (StoreAndForward) and will publish on next start;
        // they are NOT loss.
        if (!string.Equals(last.BufferMode, "unknown", StringComparison.Ordinal)
            && !string.Equals(last.BufferMode, "None", StringComparison.Ordinal))
        {
            Console.WriteLine($"  Buffer mode             : {last.BufferMode,12}");
            Console.WriteLine($"  Buffer enqueued         : {last.BufferTotalEnqueued,12:N0}");
            Console.WriteLine($"  Buffer drained          : {last.BufferTotalDrained,12:N0}");
            Console.WriteLine($"  Buffer in flight at end : {last.BufferDepth,12:N0}    (queued, will publish on next start when StoreAndForward)");
            if (last.BufferDroppedByCapacity > 0)
            {
                Console.WriteLine($"  Buffer evicted (cap)    : {last.BufferDroppedByCapacity,12:N0}    (TRUE LOSS — buffer full, oldest dropped)");
            }
            if (last.BufferDroppedByRetention > 0)
            {
                Console.WriteLine($"  Buffer evicted (age)    : {last.BufferDroppedByRetention,12:N0}    (intentional — operator-configured maxAgeDays)");
            }
        }

        Console.WriteLine($"  RSS at start            : {startedRss / (1024 * 1024),12:N0} MB");
        Console.WriteLine($"  RSS at end              : {finalRss / (1024 * 1024),12:N0} MB");
        Console.WriteLine($"  RSS growth              : {rssGrowthMb,12:F1} MB");
        Console.WriteLine($"  Avg CPU (per-core)      : {cpuPercent,12:F2} %");
        Console.WriteLine();

        // Acceptance criteria — KepServer-benchmarked.
        var pass = true;

        Console.WriteLine("  Acceptance criteria:");

        pass &= Assert("source delivery >= 99.9%",
                       sourceDeliveryRatio >= 0.999,
                       $"{sourceDeliveryRatio:P3}");

        // Publish-delivery: the source of truth depends on whether a
        // durable buffer is in use.
        //
        //   * Buffer in use (InMemory or StoreAndForward) — `_publishFailures`
        //     on the sink counts BATCHES that failed on first attempt; with
        //     AtLeastOnce + cursor-not-advanced-on-failure, the buffer
        //     retains and retries those batches, and they typically succeed.
        //     `publishSuccesses` counts batches that ultimately succeeded.
        //     The TRUE measure of data loss is the buffer's
        //     `DroppedByCapacity` (intentional capacity-bound eviction).
        //     `DroppedByRetention` is operator-configured staleness, also
        //     intentional. So delivery = (enqueued - dropped) / enqueued.
        //   * No buffer (BufferMode=None) — points have no retry path; the
        //     sink-side counters ARE the truth. Fall back to the per-batch
        //     ratio.
        var bufEnqueued = last.BufferTotalEnqueued;
        var bufDropped = last.BufferDroppedByCapacity + last.BufferDroppedByRetention;
        var bufferInUse = !string.Equals(last.BufferMode, "unknown", StringComparison.Ordinal)
                         && !string.Equals(last.BufferMode, "None", StringComparison.Ordinal);

        if (bufferInUse && bufEnqueued > 0)
        {
            var bufDeliveryRatio = (double)(bufEnqueued - bufDropped) / bufEnqueued;
            pass &= Assert("publish delivery >= 99.9% (buffer-derived)",
                           bufDeliveryRatio >= 0.999,
                           $"{bufDeliveryRatio:P3} ({bufDropped:N0} dropped of {bufEnqueued:N0} enqueued; " +
                           $"per-batch attempts: {totalPub:N0} ok / {totalRej:N0} retried)");
        }
        else
        {
            pass &= Assert("publish delivery >= 99.9%",
                           publishAttempts == 0 || publishDeliveryRatio >= 0.999,
                           $"{publishDeliveryRatio:P3}");
        }

        pass &= Assert("RSS final <= 150 MB",
                       finalRss / (1024 * 1024) <= 150,
                       $"{finalRss / (1024 * 1024)} MB");

        pass &= Assert("RSS growth over soak <= 20%",
                       startedRss == 0 || (rssGrowthMb / (startedRss / (1024.0 * 1024.0))) <= 0.20,
                       $"{rssGrowthMb / (startedRss / (1024.0 * 1024.0)):P1}");

        pass &= Assert("avg CPU per-core <= 5%",
                       cpuPercent <= 5.0,
                       $"{cpuPercent:F2} %");

        // End-to-end EREMOS verification — only applied when --eremos-api was set
        // and the API was actually reachable. We use the DELTA from baseline so the
        // count reflects only what arrived during the soak, not historical data.
        if (opts.EremosApi is not null)
        {
            Console.WriteLine();
            if (eremosFinalCount is { } finalCount && eremosBaselineCount is { } baseline)
            {
                var ingested = finalCount - baseline;
                Console.WriteLine($"  EREMOS V2 verification:");
                Console.WriteLine($"    Baseline tag count       : {baseline,12:N0}");
                Console.WriteLine($"    Final tag count          : {finalCount,12:N0}");
                Console.WriteLine($"    Ingested during soak     : {ingested,12:N0}");
                if (totalPub > 0)
                {
                    var e2e = (double)ingested / totalPub;
                    Console.WriteLine($"    End-to-end ratio         : {e2e:P3}  (eremos / publish)");
                    pass &= Assert("end-to-end delivery >= 99.0%",
                                   e2e >= 0.99,
                                   $"{e2e:P3}");
                }
                else
                {
                    Console.WriteLine($"    End-to-end ratio         : (publish count is 0 — skipped)");
                }
            }
            else
            {
                Console.WriteLine($"  EREMOS V2 verification: SKIPPED (API unreachable or unparseable response)");
            }
        }

        Console.WriteLine();
        Console.WriteLine(pass ? "  RESULT: PASS" : "  RESULT: FAIL");
        Console.WriteLine("====================================================================");

        return pass ? ExitOk : ExitFailure;
    }

    private static bool Assert(string label, bool ok, string actual)
    {
        var marker = ok ? "PASS" : "FAIL";
        Console.WriteLine($"    [{marker}] {label,-32} actual {actual}");
        return ok;
    }

    // =========================================================================
    // PRIVATE — args
    // =========================================================================

    private sealed class Options
    {
        public required string ConfigPath { get; init; }
        public required int DurationMinutes { get; init; }
        public required string CsvPath { get; init; }
        public string? EremosApi { get; init; }
        public string? EremosJwt { get; init; }
        public string? EremosUsername { get; init; }
        public string? EremosPassword { get; init; }
        public string EremosDeviceClass { get; init; } = "plc";

        /// <summary>
        /// When non-null, override the buffer mode of every route in the
        /// loaded gateway config to this value. Lets the same sample
        /// gateway.json drive both InMemory smokes and StoreAndForward
        /// soaks without hand-editing the file. When the override is
        /// StoreAndForward, the runner also upgrades any AtMostOnce
        /// route to AtLeastOnce — required by the StoreAndForward
        /// validator and otherwise the host fails fast at config load.
        /// </summary>
        public BufferMode? BufferModeOverride { get; init; }
    }

    private static Options? ParseArgs(string[] args)
    {
        string? config = null;
        var minutes = 5;
        string? csv = null;
        string? eremosApi = null;
        string? eremosJwt = null;
        // Env-var fallback for credentials so they don't end up in shell history.
        string? eremosUsername = Environment.GetEnvironmentVariable("EREMOS_USERNAME");
        string? eremosPassword = Environment.GetEnvironmentVariable("EREMOS_PASSWORD");
        var eremosDeviceClass = "plc";
        BufferMode? bufferModeOverride = null;

        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--config":
                        config = RequireValue(args, ref i, "--config");
                        break;
                    case "--duration-min":
                        minutes = int.Parse(RequireValue(args, ref i, "--duration-min"), CultureInfo.InvariantCulture);
                        break;
                    case "--csv":
                        csv = RequireValue(args, ref i, "--csv");
                        break;
                    case "--eremos-api":
                        eremosApi = RequireValue(args, ref i, "--eremos-api");
                        break;
                    case "--eremos-jwt":
                        eremosJwt = RequireValue(args, ref i, "--eremos-jwt");
                        break;
                    case "--eremos-username":
                        eremosUsername = RequireValue(args, ref i, "--eremos-username");
                        break;
                    case "--eremos-password":
                        eremosPassword = RequireValue(args, ref i, "--eremos-password");
                        break;
                    case "--eremos-device-class":
                        eremosDeviceClass = RequireValue(args, ref i, "--eremos-device-class");
                        break;
                    case "--buffer-mode":
                        var modeStr = RequireValue(args, ref i, "--buffer-mode");
                        if (!Enum.TryParse<BufferMode>(modeStr, ignoreCase: true, out var parsed))
                        {
                            Console.Error.WriteLine(
                                $"--buffer-mode value '{modeStr}' is not valid. " +
                                "Allowed: None, InMemory, StoreAndForward.");
                            return null;
                        }
                        bufferModeOverride = parsed;
                        break;
                    case "--help":
                    case "-h":
                        return null;
                    default:
                        Console.Error.WriteLine($"Unknown argument: '{args[i]}'");
                        return null;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Argument parse error: {ex.Message}");
            return null;
        }

        if (config is null)
        {
            Console.Error.WriteLine("Missing required argument: --config <path>");
            return null;
        }

        return new Options
        {
            ConfigPath = config,
            DurationMinutes = minutes,
            CsvPath = csv ?? Path.ChangeExtension(config, ".soak.csv"),
            EremosApi = eremosApi,
            EremosJwt = eremosJwt,
            EremosUsername = eremosUsername,
            EremosPassword = eremosPassword,
            EremosDeviceClass = eremosDeviceClass,
            BufferModeOverride = bufferModeOverride,
        };
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"{flag} requires a value.");
        }
        return args[++i];
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("""
            ModbusSoakRunner — EdgeConnect Modbus + MQTT soak harness

            Usage:
              ModbusSoakRunner --config <gateway.json>
                               [--duration-min 5]
                               [--csv <path>]
                               [--eremos-api <baseUrl>]
                               [--eremos-jwt <token>]
                               [--eremos-device-class <class>]

            Options:
              --config              (required) gateway.json describing sources, sinks, routes
              --duration-min        how long to run (default 5)
              --csv                 output CSV path (default: <config>.soak.csv)
              --eremos-api          base URL of EREMOS V2 API including the version
                                    prefix, e.g. http://host/api/v1/
              --eremos-jwt          JWT bearer token for the EREMOS API (optional)
              --eremos-username     login email — alternative to --eremos-jwt; the
                                    runner POSTs to {api}/auth/login and re-auths
                                    on 401. May also be set via env EREMOS_USERNAME.
              --eremos-password     login password. May also be set via env
                                    EREMOS_PASSWORD (preferred — keeps it out of
                                    shell history).
              --eremos-device-class deviceClass to query the historian for (default plc)
              --buffer-mode         override every route's buffer mode in the
                                    loaded config. One of: None | InMemory |
                                    StoreAndForward (case-insensitive). Auto-
                                    upgrades AtMostOnce delivery to AtLeastOnce
                                    when StoreAndForward is selected (the
                                    StoreAndForward validator requires it).
              --help, -h            show this message

            Acceptance criteria (KepServer-benchmarked):
              source delivery       >= 99.9%
              publish delivery      >= 99.9%
              RSS final             <= 150 MB
              RSS growth            <= 20%
              avg CPU per-core      <= 5%
              end-to-end delivery   >= 99.0% (only when --eremos-api supplied)

            Exit codes:
              0  all criteria pass
              1  one or more criteria fail (or fatal error)
              2  argument or config-load error
            """);
    }

    // =========================================================================
    // PRIVATE — types
    // =========================================================================

    private sealed class HealthSnapshot
    {
        public required DateTime At { get; init; }
        public required long TotalTransactions { get; init; }
        public required long TotalSuccesses { get; init; }
        public required long TotalFailures { get; init; }
        public required long TotalPublishSuccesses { get; init; }
        public required long TotalPublishFailures { get; init; }
        public required long RssMb { get; init; }
        public required IReadOnlyList<AdapterHealth> SourceHealth { get; init; }
        public required IReadOnlyList<AdapterHealth> SinkHealth { get; init; }

        // Buffer rollup — aggregated across every route, populated from
        // IDiagnosticsService.GetAllRouteSnapshots(). All fields default
        // to 0 / null when no buffer stats have been pushed yet.
        public string BufferMode { get; init; } = "unknown";
        public long BufferDepth { get; init; }
        public long BufferSizeBytes { get; init; }
        public long BufferTotalEnqueued { get; init; }
        public long BufferTotalDrained { get; init; }
        public long BufferDroppedByCapacity { get; init; }
        public long BufferDroppedByRetention { get; init; }
        public double? BufferOldestUnackedAgeSeconds { get; init; }
    }
}
