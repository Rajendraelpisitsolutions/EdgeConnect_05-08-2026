// ============================================================================
// File: Configuration/ConfigurationManagerBenchmarks.cs
// Covers: B2 benchmark from PHASE1_EXECUTION_PLAN.md §D4
//   - ConfigurationManager_Apply target: < 100 ms for 50-source config
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Benchmarks.Configuration;

/// <summary>
/// Measures the cost of <c>IConfigurationManager.ApplyDraftAsync</c> for a
/// realistic 50-source configuration. Target is sub-100 ms per apply.
/// </summary>
[MemoryDiagnoser]
[BenchmarkDotNet.Attributes.Config(typeof(InProcessConfig))]
public class ConfigurationManagerBenchmarks
{
    private string _tempDir = null!;
    private ConfigurationManager _manager = null!;
    private GatewayConfiguration _target = null!;

    [GlobalSetup]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ecc-bench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var store = new FileSystemConfigurationStore(new ConfigurationStorageLayout(_tempDir));

        // Seed an empty initial configuration so InitializeAsync succeeds
        // (it requires current.json to exist).
        var seed = new GatewayConfiguration
        {
            Gateway = new GatewaySettings
            {
                GatewayId = "GW-SEED",
                GatewayName = "Seed",
            },
        };
        var seedJson = JsonSerializer.Serialize(seed);
        store.WriteCurrentAsync(seedJson, CancellationToken.None).AsTask().GetAwaiter().GetResult();

        _manager = new ConfigurationManager(store);
        _manager.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();

        // Build a 50-source configuration. Sources + sinks + routes all
        // declared so cross-record validation passes.
        var sources = new List<SourceInstanceConfig>(50);
        var sinks = new List<SinkInstanceConfig>(50);
        var routes = new List<RouteConfig>(50);
        for (var i = 0; i < 50; i++)
        {
            var srcId = $"src-{i:D3}";
            var sinkId = $"sink-{i:D3}";
            sources.Add(new SourceInstanceConfig
            {
                InstanceId = srcId,
                ProtocolName = "mock",
                DeviceId = $"dev-{i:D3}",
            });
            sinks.Add(new SinkInstanceConfig
            {
                InstanceId = sinkId,
                ProtocolName = "mock",
            });
            routes.Add(new RouteConfig
            {
                RouteId = $"route-{i:D3}",
                Name = $"route-{i:D3}",
                SourceInstanceId = srcId,
                SinkInstanceIds = new[] { sinkId },
                Enabled = true,
            });
        }
        _target = new GatewayConfiguration
        {
            Gateway = new GatewaySettings
            {
                GatewayId = "GW-BENCH",
                GatewayName = "Bench Gateway",
            },
            Sources = sources,
            Sinks = sinks,
            Routes = routes,
        };
    }

    /// <summary>
    /// Draft → validate → apply a 50-source configuration. The apply
    /// timer excludes on-disk persistence warmup because
    /// <see cref="ConfigurationManager.InitializeAsync"/> runs in setup.
    /// </summary>
    [Benchmark]
    public async Task<ConfigurationApplyResult> CreateValidateApply_50Sources()
    {
        var draft = await _manager.CreateDraftAsync(_target, actor: "bench", CancellationToken.None);
        return await _manager.ApplyDraftAsync(draft, actor: "bench", CancellationToken.None);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try { _manager.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }
}
