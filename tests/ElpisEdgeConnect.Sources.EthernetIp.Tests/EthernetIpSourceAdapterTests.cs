using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sources.EthernetIp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.EthernetIp.Tests;

public class EthernetIpSourceAdapterTests
{
    private static readonly DateTimeOffset Start = new(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);

    private static EthernetIpSourceConfiguration Config(params EthernetIpTagDefinition[] tags) =>
        new()
        {
            InstanceId = "eip-1",
            ProtocolName = "ethernetip",
            DeviceId = "dev-1",
            DeviceName = "Line 3 PLC",
            DeviceClass = "plc",
            GatewayId = "gw-1",
            PollIntervalMs = 0, // skip the self-pace floor in tests
            Host = "10.0.0.1",
            CpuFamily = EthernetIpCpuFamily.ControlLogix,
            TagDefinitions = tags,
        };

    private static EthernetIpTagDefinition Tag(string name, string address, string datatype, int scanRateMs = 1000, double? scale = null, double? offset = null) =>
        new() { Name = name, Address = address, Datatype = datatype, ScanRateMs = scanRateMs, Scale = scale, Offset = offset };

    private static async Task<EthernetIpSourceAdapter> StartedAdapter(
        FakeEthernetIpClient fake, EthernetIpSourceConfiguration cfg, TimeProvider time)
    {
        var adapter = new EthernetIpSourceAdapter("eip-1", fake, NullLogger.Instance, gatewayIdentity: null, time: time);
        await adapter.InitializeAsync(cfg, CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);
        return adapter;
    }

    [Fact]
    public void ProtocolAndCapabilities_AreCorrect()
    {
        var adapter = new EthernetIpSourceAdapter("eip-1", new FakeEthernetIpClient(), NullLogger.Instance);
        adapter.ProtocolName.Should().Be("ethernetip");
        adapter.Capabilities.Should().Be(SourceCapabilities.Polling | SourceCapabilities.Browse);
        adapter.State.Should().Be(AdapterState.Created);
    }

    [Fact]
    public async Task Lifecycle_InitializeStartStop_TransitionsState()
    {
        var fake = new FakeEthernetIpClient();
        var adapter = await StartedAdapter(fake, Config(Tag("a", "A", "DINT")), new ManualTimeProvider(Start));
        adapter.State.Should().Be(AdapterState.Running);

        await adapter.StopAsync(CancellationToken.None);
        adapter.State.Should().Be(AdapterState.Stopped);
    }

    [Fact]
    public async Task PollAsync_DecodesTagValue_AsGoodPoint()
    {
        var fake = new FakeEthernetIpClient().WithValue("Speed", 1234, CanonicalValueType.Integer);
        var adapter = await StartedAdapter(fake, Config(Tag("speed", "Speed", "DINT")), new ManualTimeProvider(Start));

        var points = await adapter.PollAsync(CancellationToken.None);

        points.Should().HaveCount(1);
        var p = points[0];
        p.TagName.Should().Be("speed");
        p.TagPath.Should().Be("Speed");
        p.Value.Should().Be(1234);
        p.ValueType.Should().Be(CanonicalValueType.Integer);
        p.Quality.Should().Be(DataQuality.Good);
        p.GatewayId.Should().Be("gw-1");
    }

    [Fact]
    public async Task PollAsync_AppliesScaleOffset_ProducesDouble()
    {
        var fake = new FakeEthernetIpClient().WithValue("Raw", 100, CanonicalValueType.Integer);
        var cfg = Config(Tag("scaled", "Raw", "DINT", scale: 0.1, offset: 5.0));
        var adapter = await StartedAdapter(fake, cfg, new ManualTimeProvider(Start));

        var points = await adapter.PollAsync(CancellationToken.None);

        points.Should().HaveCount(1);
        points[0].ValueType.Should().Be(CanonicalValueType.Double);
        points[0].Value.Should().Be(15.0); // 100 * 0.1 + 5.0
    }

    [Fact]
    public async Task PollAsync_TagNotFound_EmitsBadPoint()
    {
        var fake = new FakeEthernetIpClient().WithNotFound("Missing");
        var adapter = await StartedAdapter(fake, Config(Tag("missing", "Missing", "DINT")), new ManualTimeProvider(Start));

        var points = await adapter.PollAsync(CancellationToken.None);

        points.Should().HaveCount(1);
        points[0].Quality.Should().Be(DataQuality.Bad);
        points[0].Value.Should().BeNull();
        points[0].ValueType.Should().Be(CanonicalValueType.Null);
    }

    [Fact]
    public async Task PollAsync_FatalRead_DropsSessionAndEmitsBadPoint()
    {
        var fake = new FakeEthernetIpClient().WithFatalRead("Boom");
        var adapter = await StartedAdapter(fake, Config(Tag("boom", "Boom", "DINT")), new ManualTimeProvider(Start));

        var points = await adapter.PollAsync(CancellationToken.None);

        points.Should().ContainSingle(p => p.Quality == DataQuality.Bad);
        fake.IsConnected.Should().BeFalse(); // session dropped by the connection manager
    }

    [Fact]
    public async Task PollAsync_RespectsPerTagScanRate()
    {
        var fake = new FakeEthernetIpClient().WithValue("Slow", 1, CanonicalValueType.Integer);
        var time = new ManualTimeProvider(Start);
        var adapter = await StartedAdapter(fake, Config(Tag("slow", "Slow", "DINT", scanRateMs: 1000)), time);

        (await adapter.PollAsync(CancellationToken.None)).Should().HaveCount(1); // due immediately
        (await adapter.PollAsync(CancellationToken.None)).Should().BeEmpty();     // not due yet

        time.Advance(TimeSpan.FromMilliseconds(1001));
        (await adapter.PollAsync(CancellationToken.None)).Should().HaveCount(1);   // due again
    }

    [Fact]
    public async Task BrowseTagsAsync_ReturnsConfiguredTags()
    {
        var fake = new FakeEthernetIpClient();
        var adapter = await StartedAdapter(fake, Config(Tag("a", "TagA", "REAL"), Tag("b", "TagB", "BOOL")), new ManualTimeProvider(Start));

        var tags = await adapter.BrowseTagsAsync(CancellationToken.None);

        tags.Should().HaveCount(2);
        tags.Select(t => t.Name).Should().Contain(new[] { "a", "b" });
        tags.First(t => t.Name == "a").ValueType.Should().Be(CanonicalValueType.Float);
    }

    [Fact]
    public async Task CheckHealthAsync_ReportsMetrics()
    {
        var fake = new FakeEthernetIpClient().WithValue("Speed", 5, CanonicalValueType.Integer);
        var adapter = await StartedAdapter(fake, Config(Tag("speed", "Speed", "DINT")), new ManualTimeProvider(Start));
        await adapter.PollAsync(CancellationToken.None);

        var health = await adapter.CheckHealthAsync(CancellationToken.None);

        health.State.Should().Be(AdapterState.Running);
        health.Metrics.Should().ContainKey("tagReads");
        health.Metrics!["cpuFamily"].Should().Be("ControlLogix");
    }

    [Fact]
    public void SubscribeAsync_NotSupported()
    {
        var adapter = new EthernetIpSourceAdapter("eip-1", new FakeEthernetIpClient(), NullLogger.Instance);
        Action act = () => _ = adapter.SubscribeAsync(CancellationToken.None);
        act.Should().Throw<NotSupportedException>();
    }
}
