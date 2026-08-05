// ============================================================================
// Tests: MelsecDiagnosticsService — resolves the running adapter via
// ISupervisedSourceRegistry and returns its observational snapshot, or a typed
// unavailable DTO for missing / not-running / not-MELSEC / no-registry. Never
// throws; carries no raw payloads.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.Management.Api;
using ElpisEdgeConnect.Sources.Melsec.Diagnostics;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class MelsecDiagnosticsServiceTests
{
    // Minimal ISourceAdapter stub — the diagnostics service only calls GetAdapter
    // + (for MELSEC) GetDiagnosticsSnapshot, so the rest may throw.
    private class StubSourceAdapter : ISourceAdapter
    {
        public string InstanceId => "stub";
        public string ProtocolName => "stub";
        public SourceCapabilities Capabilities => SourceCapabilities.Polling;
        public AdapterState State => AdapterState.Running;
        public Task InitializeAsync(SourceConfiguration config, CancellationToken ct) => throw new NotSupportedException();
        public Task StartAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task StopAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<AdapterHealth> CheckHealthAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<CanonicalDataPoint>> PollAsync(CancellationToken ct) => throw new NotSupportedException();
        public IAsyncEnumerable<CanonicalDataPoint> SubscribeAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<TagDefinition>> BrowseTagsAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<ValidationResult> ValidateConfigAsync(SourceConfiguration config, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MelsecStubAdapter : StubSourceAdapter, IMelsecDiagnosticsProvider
    {
        private readonly MelsecDiagnosticsSnapshot _snapshot;
        public MelsecStubAdapter(MelsecDiagnosticsSnapshot snapshot) => _snapshot = snapshot;
        public MelsecDiagnosticsSnapshot GetDiagnosticsSnapshot() => _snapshot;
    }

    private sealed class FakeRegistry : ISupervisedSourceRegistry
    {
        public ISourceAdapter? Adapter { get; set; }
        public ISourceIntake? GetIntake(string sourceInstanceId) => null;
        public IReadOnlyCollection<string> SourceInstanceIds => Array.Empty<string>();
        public ISourceAdapter? GetAdapter(string sourceInstanceId) => Adapter;
    }

    private static MelsecDiagnosticsSnapshot SampleSnapshot() => new()
    {
        InstanceId = "melsec-1",
        Route = new MelsecRouteDiagnostics(0, 0xFF, 0x03FF, 0),
        ScanBlocks = new[]
        {
            new MelsecScanBlockDiagnostics
            {
                DeviceSymbol = "D", HeadDeviceNumber = 200, WordCount = 2, ScanRateMs = 1000,
                TagNames = new[] { "total_cycles" }, LastResult = MelsecBlockResult.ProtocolError,
                LastEndCode = 0xC056, LastMessage = "end code 0xC056",
            },
        },
        LastEndCode = 0xC056,
        LastEndCodeDescription = "read address + points exceed the device range",
        AffectedTags = new[] { "total_cycles" },
        TagQuality = new[] { new MelsecTagQuality { TagName = "total_cycles", Address = "D200", Quality = "Bad", Reason = "end code 0xC056" } },
        LastRequestLatencyMs = 21,
        Connected = true,
        BreakerState = "Closed",
        ConsecutiveFailures = 0,
    };

    [Fact]
    public void Available_returns_full_snapshot()
    {
        var svc = new MelsecDiagnosticsService(new FakeRegistry { Adapter = new MelsecStubAdapter(SampleSnapshot()) });

        var dto = svc.GetDiagnostics("melsec-1");

        dto.Available.Should().BeTrue();
        dto.Snapshot.Should().NotBeNull();
        dto.Snapshot!.Route.Should().Be(new MelsecRouteDiagnostics(0, 0xFF, 0x03FF, 0));
        dto.Snapshot.ScanBlocks.Should().ContainSingle();
        dto.Snapshot.LastEndCode.Should().Be(0xC056);
        dto.Snapshot.LastEndCodeDescription.Should().NotBeNullOrEmpty();
        dto.Snapshot.AffectedTags.Should().Equal("total_cycles");
        dto.Snapshot.TagQuality.Should().ContainSingle(t => t.Quality == "Bad");
        dto.Snapshot.LastRequestLatencyMs.Should().Be(21);
    }

    [Fact]
    public void Not_running_source_is_unavailable()
    {
        var dto = new MelsecDiagnosticsService(new FakeRegistry { Adapter = null }).GetDiagnostics("melsec-1");

        dto.Available.Should().BeFalse();
        dto.Reason.Should().Contain("not running");
        dto.Snapshot.Should().BeNull();
    }

    [Fact]
    public void Non_melsec_source_is_unavailable()
    {
        var dto = new MelsecDiagnosticsService(new FakeRegistry { Adapter = new StubSourceAdapter() }).GetDiagnostics("some-modbus");

        dto.Available.Should().BeFalse();
        dto.Reason.Should().Contain("not a MELSEC");
    }

    [Fact]
    public void Null_registry_degrades_gracefully()
    {
        new MelsecDiagnosticsService(registry: null).GetDiagnostics("melsec-1").Available.Should().BeFalse();
    }

    [Fact]
    public void Empty_id_is_unavailable_not_thrown()
    {
        var svc = new MelsecDiagnosticsService(new FakeRegistry());
        var act = () => svc.GetDiagnostics("");
        act.Should().NotThrow();
        svc.GetDiagnostics("").Available.Should().BeFalse();
    }

    [Fact]
    public void Snapshot_has_no_raw_payload_surface()
    {
        // Contract guard: the diagnostics snapshot must not expose raw bytes.
        var props = typeof(MelsecDiagnosticsSnapshot).GetProperties().Select(p => p.Name);
        props.Should().NotContain(n => n.Contains("Raw", StringComparison.OrdinalIgnoreCase));
    }
}
