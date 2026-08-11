// ============================================================================
// File: Program.cs
// Purpose: D phase 8 leak/soak harness. Boots a real EdgeConnect host via
//          the production composition root with mock adapters, drives two
//          sources at ~1000 pts/sec each (realistic CNC rate), and samples
//          memory / GC / diagnostics counters into a CSV artifact on a
//          fixed interval.
//
//          Intended usage:
//            * 4-hour kickoff run for Phase 1 exit gate
//            * 7-day continuous run for v1 ship gate
//          Smoke runs (≤ 2 minutes) are used during development to prove
//          the harness itself works end-to-end.
//
// CSV schema (v1 — pinned by docs/phase1-exit/leak-harness-csv-schema.md):
//   timestamp_utc,elapsed_sec,private_bytes,managed_heap_bytes,gen0,gen1,gen2,
//   total_points_delivered,route_event_log_live,route_event_log_dropped,
//   bp_event_log_live,bp_event_log_dropped,
//   sink_event_log_live,sink_event_log_dropped
//
// Reference: PHASE1_EXECUTION_PLAN.md §D5, docs/benchmarks/phase1-baseline.md
// Milestone: D — phase 8.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Host;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.MockAdapters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ElpisEdgeConnect.LeakHarness;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var options = HarnessOptions.Parse(args);
        Console.WriteLine(
            $"[leak-harness] duration={options.Duration} interval={options.SampleInterval} " +
            $"buffer-mode={options.BufferMode} output={options.OutputPath}");
        Console.WriteLine(
            $"[leak-harness] expected sustained throughput ceiling for buffer-mode={options.BufferMode}: " +
            $"{ExpectedThroughputCeiling(options.BufferMode):N0} pts/sec " +
            "(use this to interpret criterion 5 'Delivered >= target' against the right baseline)");

        // Prepare a temp directory with a seeded current.json so the real
        // FileSystemConfigurationStore can load it. ConfigurationStorageLayout
        // resolves CurrentConfigPath to `{root}/config/current.json`, so the
        // root we pass in HostOptions.ConfigDirectory is the PARENT of the
        // `config` folder. Create the nested `config` directory and drop
        // current.json inside it.
        var tempDir = Path.Combine(Path.GetTempPath(), "ecc-leak-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var configDir = Path.Combine(tempDir, "config");
        Directory.CreateDirectory(configDir);

        try
        {
            var seedConfig = BuildConfig(options.BufferMode);
            var seedJson = JsonSerializer.Serialize(seedConfig);
            File.WriteAllText(Path.Combine(configDir, "current.json"), seedJson);

            var hostOptions = new ElpisEdgeConnect.Host.HostOptions
            {
                ConfigDirectory = tempDir,
                LicensePath = Path.Combine(tempDir, "license.json"),
                GatewayIdentityPath = Path.Combine(tempDir, "identity"),
                // Do not bind a real HTTP port during long runs; the
                // readiness flag itself is observable via the collector.
                EnableEndpointsServer = false,
            };

            // Mock adapters — two sources driven at ~1000 pts/sec each via
            // a PollGate throttled by a background releaser task.
            using var srcGate1 = new SemaphoreSlim(0, int.MaxValue);
            using var srcGate2 = new SemaphoreSlim(0, int.MaxValue);
            var src1 = new MockSourceAdapter("src-1") { PollGate = srcGate1, PointsPerPoll = 100 };
            var src2 = new MockSourceAdapter("src-2") { PollGate = srcGate2, PointsPerPoll = 100 };
            // CaptureHistory=false: the leak harness must NOT retain
            // every delivered point in-memory (a 4-hour run at 2000 pts/sec
            // would balloon the managed heap by ~11 GB). Counts still
            // increment; the per-point queue is skipped.
            var sink1 = new MockSinkAdapter("sink-1") { CaptureHistory = false };
            var sink2 = new MockSinkAdapter("sink-2") { CaptureHistory = false };

            var sourceRegs = new List<SourceRegistration>
            {
                new() { Adapter = src1, Config = MockSrcCfg("src-1"), RouteId = "route-1" },
                new() { Adapter = src2, Config = MockSrcCfg("src-2"), RouteId = "route-2" },
            };
            var sinkRegs = new List<SinkRegistration>
            {
                new() { Adapter = sink1, Config = MockSinkCfg("sink-1"), RouteId = "route-1" },
                new() { Adapter = sink2, Config = MockSinkCfg("sink-2"), RouteId = "route-2" },
            };

            var hostBuilder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddElpisEdgeConnectHost(hostOptions);
                    services.AddSingleton<IEnumerable<SourceRegistration>>(sourceRegs);
                    services.AddSingleton<IEnumerable<SinkRegistration>>(sinkRegs);
                });

            using var host = hostBuilder.Build();
            var collector = host.Services.GetRequiredService<RuntimeDiagnosticsCollector>();

            await host.StartAsync();
            Console.WriteLine("[leak-harness] host started, beginning load + sampling");

            using var loadCts = new CancellationTokenSource();
            // Release 10 permits/sec per source → 10 polls × 100 points = 1000 pts/sec/source.
            var loader1 = Task.Run(() => LoaderLoopAsync(srcGate1, TimeSpan.FromMilliseconds(100), loadCts.Token));
            var loader2 = Task.Run(() => LoaderLoopAsync(srcGate2, TimeSpan.FromMilliseconds(100), loadCts.Token));

            var samples = await SamplingLoopAsync(options, collector, sink1, sink2);

            loadCts.Cancel();
            try { await Task.WhenAll(loader1, loader2); } catch { /* best-effort */ }

            await host.StopAsync();
            Console.WriteLine($"[leak-harness] host stopped. Captured {samples} samples to {options.OutputPath}");
            PrintSummary(options, sink1, sink2);
            return 0;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static async Task LoaderLoopAsync(SemaphoreSlim gate, TimeSpan cadence, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { gate.Release(); } catch (SemaphoreFullException) { /* skip */ }
            try { await Task.Delay(cadence, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private static async Task<int> SamplingLoopAsync(
        HarnessOptions options,
        RuntimeDiagnosticsCollector collector,
        MockSinkAdapter sink1,
        MockSinkAdapter sink2)
    {
        using var writer = new StreamWriter(options.OutputPath, append: false);
        writer.WriteLine(
            "timestamp_utc,elapsed_sec,private_bytes,managed_heap_bytes,gen0,gen1,gen2," +
            "total_points_delivered,route_event_log_live,route_event_log_dropped," +
            "bp_event_log_live,bp_event_log_dropped," +
            "sink_event_log_live,sink_event_log_dropped");
        writer.Flush();

        var proc = Process.GetCurrentProcess();
        var start = DateTime.UtcNow;
        var deadline = start + options.Duration;
        var nextSampleAt = start;
        var sampleCount = 0;

        while (DateTime.UtcNow < deadline)
        {
            var now = DateTime.UtcNow;
            if (now >= nextSampleAt)
            {
                proc.Refresh();
                var elapsed = (now - start).TotalSeconds;
                var privateBytes = proc.PrivateMemorySize64;
                var managed = GC.GetTotalMemory(forceFullCollection: false);
                var g0 = GC.CollectionCount(0);
                var g1 = GC.CollectionCount(1);
                var g2 = GC.CollectionCount(2);
                var totalDelivered = sink1.PublishedCount + sink2.PublishedCount;
                var routeLog = collector.GetRouteStateEvents("route-1");
                var bpLog = collector.GetBackpressureEvents("route-1");
                var sinkLog = collector.GetSinkEvents("route-1", "sink-1");

                writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{now:O},{elapsed:F1},{privateBytes},{managed},{g0},{g1},{g2},{totalDelivered},{routeLog?.LiveCount ?? 0},{routeLog?.TotalDropped ?? 0},{bpLog?.LiveCount ?? 0},{bpLog?.TotalDropped ?? 0},{sinkLog?.LiveCount ?? 0},{sinkLog?.TotalDropped ?? 0}"));
                writer.Flush();

                sampleCount++;
                nextSampleAt = start + TimeSpan.FromSeconds(sampleCount * options.SampleInterval.TotalSeconds);
            }

            var sleep = nextSampleAt - DateTime.UtcNow;
            if (sleep > TimeSpan.Zero)
            {
                try { await Task.Delay(sleep); } catch { break; }
            }
        }

        return sampleCount;
    }

    private static void PrintSummary(HarnessOptions options, MockSinkAdapter sink1, MockSinkAdapter sink2)
    {
        Console.WriteLine("[leak-harness] ---- summary ----");
        Console.WriteLine($"[leak-harness] duration     = {options.Duration}");
        Console.WriteLine($"[leak-harness] sink-1       = {sink1.PublishedCount} points");
        Console.WriteLine($"[leak-harness] sink-2       = {sink2.PublishedCount} points");
        Console.WriteLine($"[leak-harness] total        = {sink1.PublishedCount + sink2.PublishedCount} points");
        Console.WriteLine($"[leak-harness] csv          = {options.OutputPath}");
        Console.WriteLine("[leak-harness] -----------------");
    }

    // The buffer mode is EXPLICIT on every route. Leaving it at the default
    // would inherit BufferMode.StoreAndForward (SQLite), which has a lower
    // sustained-throughput ceiling than InMemory and caused the criterion-5
    // threshold confusion in the Phase 1 4-hour kickoff (see
    // docs/phase1-exit/leak-harness-kickoff-summary.md §"Carry-forward
    // defects from the post-fix run" #2). Making it explicit forces every
    // future soak run to declare the mode under test.
    private static GatewayConfiguration BuildConfig(BufferMode bufferMode) => new()
    {
        Gateway = new GatewaySettings
        {
            GatewayId = "GW-LEAK",
            GatewayName = "Leak Harness Gateway",
        },
        Sources = new List<SourceInstanceConfig>
        {
            new() { InstanceId = "src-1", ProtocolName = "mock", DeviceId = "dev-1" },
            new() { InstanceId = "src-2", ProtocolName = "mock", DeviceId = "dev-2" },
        },
        Sinks = new List<SinkInstanceConfig>
        {
            new() { InstanceId = "sink-1", ProtocolName = "mock" },
            new() { InstanceId = "sink-2", ProtocolName = "mock" },
        },
        Routes = new List<RouteConfig>
        {
            new()
            {
                RouteId = "route-1",
                Name = "route-1",
                SourceInstanceId = "src-1",
                SinkInstanceIds = new[] { "sink-1" },
                Buffer = new BufferPolicyConfig { Mode = bufferMode },
            },
            new()
            {
                RouteId = "route-2",
                Name = "route-2",
                SourceInstanceId = "src-2",
                SinkInstanceIds = new[] { "sink-2" },
                Buffer = new BufferPolicyConfig { Mode = bufferMode },
            },
        },
    };

    /// <summary>
    /// Sustained-throughput ceilings observed in Phase 1 benchmarks +
    /// leak-harness runs, used to interpret criterion 5 of the soak exit
    /// gate. Keep in sync with <c>docs/benchmarks/phase1-baseline.md</c>
    /// and <c>docs/phase1-exit/leak-harness-kickoff-summary.md</c>.
    /// </summary>
    private static int ExpectedThroughputCeiling(BufferMode mode) => mode switch
    {
        // Full pipeline at ~2 k pts/sec with no backpressure; matches the
        // original criterion-5 InMemory assumption (27.36M in 14 400 s).
        BufferMode.InMemory => 2_000,
        // SQLite with synchronous=FULL fsync + writer-mutex contention; the
        // 4-hour kickoff sustained a measured ~1 380 pts/sec. Target rounded
        // to 1 400 to match the corrected criterion-5 threshold (19.15M).
        BufferMode.StoreAndForward => 1_400,
        _ => 1_400,
    };

    private static MockSourceConfiguration MockSrcCfg(string id) => new()
    {
        InstanceId = id,
        ProtocolName = "mock",
        DeviceId = $"dev-{id}",
    };

    private static MockSinkConfiguration MockSinkCfg(string id) => new()
    {
        InstanceId = id,
        ProtocolName = "mock",
    };
}

internal sealed record HarnessOptions(
    TimeSpan Duration,
    TimeSpan SampleInterval,
    string OutputPath,
    BufferMode BufferMode)
{
    public static HarnessOptions Parse(string[] args)
    {
        var duration = TimeSpan.FromMinutes(2); // safe dev default
        var interval = TimeSpan.FromSeconds(10);
        var output = "leak-harness.csv";
        // Default to StoreAndForward: that's what the production edge case
        // exercises (durable buffer). Operators explicitly opting into
        // InMemory via --buffer-mode in-memory can reproduce the original
        // criterion-5 InMemory throughput assumption.
        var bufferMode = BufferMode.StoreAndForward;
        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--duration-seconds":
                    duration = TimeSpan.FromSeconds(int.Parse(args[++i], CultureInfo.InvariantCulture));
                    break;
                case "--sample-interval-seconds":
                    interval = TimeSpan.FromSeconds(int.Parse(args[++i], CultureInfo.InvariantCulture));
                    break;
                case "--output":
                    output = args[++i];
                    break;
                case "--buffer-mode":
                    bufferMode = ParseBufferMode(args[++i]);
                    break;
            }
        }
        return new HarnessOptions(duration, interval, output, bufferMode);
    }

    private static BufferMode ParseBufferMode(string value) =>
        value.ToLowerInvariant() switch
        {
            "in-memory" or "inmemory" or "mem" => BufferMode.InMemory,
            "store-and-forward" or "storeandforward" or "sqlite" or "saf" => BufferMode.StoreAndForward,
            _ => throw new ArgumentException(
                $"Unrecognized --buffer-mode value '{value}'. " +
                "Expected 'in-memory' or 'store-and-forward'.",
                nameof(value)),
        };
}
