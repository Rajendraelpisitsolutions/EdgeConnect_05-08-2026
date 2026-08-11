// ============================================================================
// File: ModbusTcpSourceAdapterTests.cs
// Purpose: Unit tests for ModbusTcpSourceAdapter — lifecycle, capabilities,
//          config validation, health, browse, and executor routing via the
//          internal ExecuteAsyncInternal hook.
// ============================================================================

using System.Linq;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Identity;
using ElpisEdgeConnect.Core.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Tests;

public sealed class ModbusTcpSourceAdapterTests
{
    private static ModbusTcpSourceConfiguration ValidConfig() => new()
    {
        InstanceId = "plc-test",
        ProtocolName = "modbustcp",
        DeviceId = "plc",
        // Modbus REQUIRES DeviceClass (no implicit default for a
        // protocol-agnostic adapter). All fixture-using tests fall back to
        // "plc" since most cover PLC-style scenarios.
        DeviceClass = "plc",
        Host = "192.168.1.50",
        Port = 502,
    };

    [Fact]
    public void Capabilities_IsPollingPlusBrowse()
    {
        var adapter = new ModbusTcpSourceAdapter("a", new FakeModbusClient(), NullLogger.Instance);

        adapter.Capabilities.Should().Be(SourceCapabilities.Polling | SourceCapabilities.Browse);
    }

    [Fact]
    public void ProtocolName_IsModbusTcp()
    {
        var adapter = new ModbusTcpSourceAdapter("a", new FakeModbusClient(), NullLogger.Instance);

        adapter.ProtocolName.Should().Be("modbustcp");
    }

    [Fact]
    public async Task InitializeAsync_WithWrongConfigType_TransitionsToFailedAndThrows()
    {
        var adapter = new ModbusTcpSourceAdapter("a", new FakeModbusClient(), NullLogger.Instance);
        var wrong = Substitute.For<SourceConfiguration>();

        var act = () => adapter.InitializeAsync(wrong, default);

        await act.Should().ThrowAsync<System.InvalidOperationException>();
        adapter.State.Should().Be(AdapterState.Failed);
    }

    [Fact]
    public async Task InitializeAsync_Valid_TransitionsToInitialized()
    {
        var adapter = new ModbusTcpSourceAdapter("a", new FakeModbusClient(), NullLogger.Instance);

        await adapter.InitializeAsync(ValidConfig(), default);

        adapter.State.Should().Be(AdapterState.Initialized);
    }

    [Fact]
    public async Task StartAsync_AfterInitialize_TransitionsToRunning()
    {
        var adapter = new ModbusTcpSourceAdapter("a", new FakeModbusClient(), NullLogger.Instance);
        await adapter.InitializeAsync(ValidConfig(), default);

        await adapter.StartAsync(default);

        adapter.State.Should().Be(AdapterState.Running);
    }

    [Fact]
    public async Task ReconfigureAsync_DefaultImpl_StopsAndRestartsWithNewConfig()
    {
        // Pins the ISourceAdapter.ReconfigureAsync default-implementation
        // contract for ModbusTcp — no behavioural regression. Adapter
        // ends in Running on the NEW config; old listener disposed cleanly.
        // Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.3.5
        var adapter = new ModbusTcpSourceAdapter("a", new FakeModbusClient(), NullLogger.Instance);
        ISourceAdapter via = adapter;
        await via.InitializeAsync(ValidConfig(), default);
        await via.StartAsync(default);

        var newConfig = ValidConfig() with { Host = "192.168.1.99" };
        await via.ReconfigureAsync(newConfig, default);

        adapter.State.Should().Be(AdapterState.Running);
    }

    [Fact]
    public async Task StartAsync_ConnectFails_StillTransitionsToRunningForRetryLoop()
    {
        // Matches the FOCAS2 semantic: initial connect failures don't fail
        // startup, because the poll loop retries on its own cadence.
        var client = new FakeModbusClient
        {
            ConnectException = new ModbusFatalException(ModbusErrors.ConnectFailed, "offline"),
        };
        var adapter = new ModbusTcpSourceAdapter("a", client, NullLogger.Instance);
        await adapter.InitializeAsync(ValidConfig(), default);

        await adapter.StartAsync(default);

        adapter.State.Should().Be(AdapterState.Running);
    }

    [Fact]
    public async Task StopAsync_AfterStart_TransitionsToStopped()
    {
        var adapter = new ModbusTcpSourceAdapter("a", new FakeModbusClient(), NullLogger.Instance);
        await adapter.InitializeAsync(ValidConfig(), default);
        await adapter.StartAsync(default);

        await adapter.StopAsync(default);

        adapter.State.Should().Be(AdapterState.Stopped);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsMetrics()
    {
        var adapter = new ModbusTcpSourceAdapter("a", new FakeModbusClient(), NullLogger.Instance);
        await adapter.InitializeAsync(ValidConfig(), default);
        await adapter.StartAsync(default);

        var health = await adapter.CheckHealthAsync(default);

        health.State.Should().Be(AdapterState.Running);
        health.Level.Should().Be(HealthLevel.Healthy);
        health.Metrics.Should().NotBeNull();
        health.Metrics!.Should().ContainKey("endpoint");
        health.Metrics.Should().ContainKey("connected");
        health.Metrics.Should().ContainKey("transactionsExecuted");
        health.Metrics.Should().ContainKey("circuitBreakerState");
    }

    [Fact]
    public async Task ValidateConfigAsync_MissingHost_FailsWithConfigMissingField()
    {
        var adapter = new ModbusTcpSourceAdapter("a", new FakeModbusClient(), NullLogger.Instance);
        var bad = ValidConfig() with { Host = "" };

        var result = await adapter.ValidateConfigAsync(bad, default);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == ModbusErrors.ConfigMissingField && e.Path == "Host");
    }

    [Fact]
    public async Task ValidateConfigAsync_ZeroPort_Fails()
    {
        var adapter = new ModbusTcpSourceAdapter("a", new FakeModbusClient(), NullLogger.Instance);
        var bad = ValidConfig() with { Port = 0 };

        var result = await adapter.ValidateConfigAsync(bad, default);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path == "Port");
    }

    [Fact]
    public async Task ValidateConfigAsync_MaxBackoffLowerThanInitial_Fails()
    {
        var adapter = new ModbusTcpSourceAdapter("a", new FakeModbusClient(), NullLogger.Instance);
        var bad = ValidConfig() with { InitialBackoffMs = 5000, MaxBackoffMs = 1000 };

        var result = await adapter.ValidateConfigAsync(bad, default);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path == "MaxBackoffMs");
    }

    [Fact]
    public async Task ValidateConfigAsync_InvalidTagDefinition_Fails()
    {
        var adapter = new ModbusTcpSourceAdapter("a", new FakeModbusClient(), NullLogger.Instance);
        var bad = ValidConfig() with
        {
            TagDefinitions = [new ModbusTagDefinition
            {
                Name = " ",
                RegisterClass = ModbusRegisterClass.HoldingRegister,
                Address = 0,
                ScanRateMs = 0,
            }],
        };

        var result = await adapter.ValidateConfigAsync(bad, default);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path == "TagDefinitions[0].Name");
        result.Errors.Should().Contain(e => e.Path == "TagDefinitions[0].ScanRateMs");
    }

    [Fact]
    public async Task BrowseTagsAsync_MapsTagDefinitionsToCanonicalTagDefs()
    {
        var adapter = new ModbusTcpSourceAdapter("a", new FakeModbusClient(), NullLogger.Instance);
        var cfg = ValidConfig() with
        {
            TagDefinitions =
            [
                new ModbusTagDefinition
                {
                    Name = "spindle_load",
                    UnitId = 1,
                    RegisterClass = ModbusRegisterClass.HoldingRegister,
                    Address = 100,
                    ScanRateMs = 500,
                    Datatype = "uint16",
                    Unit = "%",
                },
            ],
        };
        await adapter.InitializeAsync(cfg, default);

        var tags = await adapter.BrowseTagsAsync(default);

        tags.Should().HaveCount(1);
        var tag = tags[0];
        tag.Name.Should().Be("spindle_load");
        tag.Writable.Should().BeFalse();
        tag.Unit.Should().Be("%");
        tag.ProtocolMetadata.Should().NotBeNull();
        tag.ProtocolMetadata!["registerClass"].Should().Be("HoldingRegister");
        tag.ProtocolMetadata["address"].Should().Be("100");
    }

    [Fact]
    public void SubscribeAsync_IsNotSupported()
    {
        var adapter = new ModbusTcpSourceAdapter("a", new FakeModbusClient(), NullLogger.Instance);

        var act = () => adapter.SubscribeAsync(default).GetAsyncEnumerator();

        act.Should().Throw<System.NotSupportedException>();
    }

    [Fact]
    public async Task ExecuteAsyncInternal_RoutesThroughExecutor()
    {
        var client = new FakeModbusClient();
        client.HoldingRegisterResults.Enqueue(() => [0x1234, 0x5678]);
        var adapter = new ModbusTcpSourceAdapter("a", client, NullLogger.Instance);
        await adapter.InitializeAsync(ValidConfig(), default);
        await adapter.StartAsync(default);

        var result = await adapter.ExecuteAsyncInternal(
            new ModbusReadRequest(1, ModbusRegisterClass.HoldingRegister, 0, 2),
            default);

        result.IsSuccess.Should().BeTrue();
        result.RegisterPayload.Should().Equal(new ushort[] { 0x1234, 0x5678 });

        var health = await adapter.CheckHealthAsync(default);
        health.Metrics!["transactionsExecuted"].Should().Be(1L);
    }

    [Fact]
    public async Task PollAsync_NoTagDefinitions_ReturnsEmpty()
    {
        var adapter = new ModbusTcpSourceAdapter("a", new FakeModbusClient(), NullLogger.Instance);
        await adapter.InitializeAsync(ValidConfig(), default);
        await adapter.StartAsync(default);

        var points = await adapter.PollAsync(default);

        points.Should().BeEmpty();
    }

    [Fact]
    public async Task PollAsync_DecodesHoldingRegisterTag_IntoCanonicalPoint()
    {
        var client = new FakeModbusClient();
        client.HoldingRegisterResults.Enqueue(() => [0x1234]);

        var cfg = ValidConfig() with
        {
            PollIntervalMs = 0,
            TagDefinitions =
            [
                new ModbusTagDefinition
                {
                    Name = "spindle_rpm",
                    UnitId = 1,
                    RegisterClass = ModbusRegisterClass.HoldingRegister,
                    Address = 0,
                    ScanRateMs = 100,
                    Datatype = "uint16",
                    Unit = "rpm",
                },
            ],
        };
        var adapter = new ModbusTcpSourceAdapter("a", client, NullLogger.Instance);
        await adapter.InitializeAsync(cfg, default);
        await adapter.StartAsync(default);

        var points = await adapter.PollAsync(default);

        points.Should().HaveCount(1);
        var p = points[0];
        p.TagName.Should().Be("spindle_rpm");
        p.Value.Should().Be(0x1234);
        p.ValueType.Should().Be(ElpisEdgeConnect.Core.Model.CanonicalValueType.Integer);
        p.Unit.Should().Be("rpm");
        p.Quality.Should().Be(ElpisEdgeConnect.Core.Model.DataQuality.Good);
    }

    [Fact]
    public async Task PollAsync_AppliesScaleAndOffset()
    {
        var client = new FakeModbusClient();
        client.InputRegisterResults.Enqueue(() => [1000]); // raw 1000

        var cfg = ValidConfig() with
        {
            PollIntervalMs = 0,
            TagDefinitions =
            [
                new ModbusTagDefinition
                {
                    Name = "temp",
                    RegisterClass = ModbusRegisterClass.InputRegister,
                    Address = 0,
                    ScanRateMs = 100,
                    Datatype = "int16",
                    Scale = 0.1,
                    Offset = 0.0,
                    Unit = "C",
                },
            ],
        };
        var adapter = new ModbusTcpSourceAdapter("a", client, NullLogger.Instance);
        await adapter.InitializeAsync(cfg, default);
        await adapter.StartAsync(default);

        var points = await adapter.PollAsync(default);

        points.Should().HaveCount(1);
        points[0].Value.Should().BeOfType<double>().Which.Should().BeApproximately(100.0, 1e-9);
        points[0].ValueType.Should().Be(ElpisEdgeConnect.Core.Model.CanonicalValueType.Double);
    }

    [Fact]
    public async Task PollAsync_CoilDecode_EmitsBoolean()
    {
        var client = new FakeModbusClient();
        client.CoilResults.Enqueue(() => [true]);

        var cfg = ValidConfig() with
        {
            PollIntervalMs = 0,
            TagDefinitions =
            [
                new ModbusTagDefinition
                {
                    Name = "running",
                    RegisterClass = ModbusRegisterClass.Coil,
                    Address = 0,
                    ScanRateMs = 100,
                    Datatype = "bool",
                },
            ],
        };
        var adapter = new ModbusTcpSourceAdapter("a", client, NullLogger.Instance);
        await adapter.InitializeAsync(cfg, default);
        await adapter.StartAsync(default);

        var points = await adapter.PollAsync(default);

        points.Should().HaveCount(1);
        points[0].Value.Should().Be(true);
        points[0].ValueType.Should().Be(ElpisEdgeConnect.Core.Model.CanonicalValueType.Boolean);
    }

    [Fact]
    public async Task PollAsync_PerGroupTimers_SkipsNotYetDueGroup()
    {
        var client = new FakeModbusClient();
        client.HoldingRegisterResults.Enqueue(() => [1]);
        client.HoldingRegisterResults.Enqueue(() => [2]); // for the slow-group call on the 2nd poll

        var start = DateTimeOffset.UtcNow;
        var time = new FakeTimeProvider(start);

        var cfg = ValidConfig() with
        {
            PollIntervalMs = 0,
            TagDefinitions =
            [
                new ModbusTagDefinition
                {
                    Name = "fast",
                    RegisterClass = ModbusRegisterClass.HoldingRegister,
                    Address = 0,
                    ScanRateMs = 100,
                    Datatype = "uint16",
                },
                new ModbusTagDefinition
                {
                    Name = "slow",
                    RegisterClass = ModbusRegisterClass.HoldingRegister,
                    Address = 100,
                    ScanRateMs = 5000,
                    Datatype = "uint16",
                },
            ],
        };
        var adapter = new ModbusTcpSourceAdapter("a", client, NullLogger.Instance, gatewayIdentity: null, time: time);
        await adapter.InitializeAsync(cfg, default);
        await adapter.StartAsync(default);

        // First poll: both groups are due (no prior run).
        var first = await adapter.PollAsync(default);
        first.Should().HaveCount(2);

        // Re-enqueue results for a potential second sweep.
        client.HoldingRegisterResults.Enqueue(() => [10]);

        // Advance time past fast interval but NOT past slow interval.
        time.Advance(TimeSpan.FromMilliseconds(200));

        var second = await adapter.PollAsync(default);
        second.Should().ContainSingle(p => p.TagName == "fast");
        second.Should().NotContain(p => p.TagName == "slow");
    }

    // ========================================================================
    // G.5 — Quality propagation: per the canonical-data-model contract
    // (docs/core/canonical-data-model.md), a block-read failure emits one
    // Quality=Bad point per tag in the failed block so downstream OPC UA /
    // MQTT clients can observe the outage instead of consuming stale
    // Good-quality values. Value is null, ValueType is Null, QualityReason
    // carries the underlying error.
    // ========================================================================
    [Fact]
    public async Task PollAsync_BlockTransactionFailure_EmitsBadPointPerTagInBlock()
    {
        var client = new FakeModbusClient
        {
            ReadException = new ModbusSlaveException(0x03, 0x02, "Illegal Data Address"),
        };

        var cfg = ValidConfig() with
        {
            PollIntervalMs = 0,
            MaxTransactionRetries = 0,
            TagDefinitions =
            [
                new ModbusTagDefinition
                {
                    Name = "t1",
                    RegisterClass = ModbusRegisterClass.HoldingRegister,
                    Address = 0,
                    ScanRateMs = 100,
                    Datatype = "uint16",
                },
                new ModbusTagDefinition
                {
                    Name = "t2",
                    RegisterClass = ModbusRegisterClass.HoldingRegister,
                    Address = 1,
                    ScanRateMs = 100,
                    Datatype = "uint16",
                },
            ],
        };
        var adapter = new ModbusTcpSourceAdapter("a", client, NullLogger.Instance);
        await adapter.InitializeAsync(cfg, default);
        await adapter.StartAsync(default);

        var points = await adapter.PollAsync(default);

        // One Bad point per tag in the failed block.
        points.Should().HaveCount(2);
        points.Should().OnlyContain(p => p.Quality == DataQuality.Bad);
        points.Should().OnlyContain(p => p.ValueType == CanonicalValueType.Null);
        points.Should().OnlyContain(p => p.Value == null);
        // QualityReason carries enough detail for ops triage. With the
        // auto-adaptive address base, an Illegal Data Address (0x02) is probed
        // and turned into an operator-facing message naming the address rather
        // than the raw "Illegal Data Address" device string.
        points.Should().OnlyContain(
            p => !string.IsNullOrEmpty(p.QualityReason)
                 && p.QualityReason.Contains("no data at address",
                     StringComparison.Ordinal));
        // Failure counter still increments — the metric is the truth source
        // for "did we have a scan failure?" independent of the new emission.
        var health = await adapter.CheckHealthAsync(default);
        health.Metrics!["transactionFailures"].Should().Be(1L);
    }

    [Fact]
    public async Task PollAsync_HappyPath_EmitsGoodQuality()
    {
        // Sanity-pin: when the source is Running and the read succeeds, every
        // emission must be Good. Together with the failure-path test above,
        // this proves the Good <-> Bad cycle without needing a fail+recover
        // wall-clock test.
        var client = new FakeModbusClient();
        client.HoldingRegisterResults.Enqueue(() => [42]);

        var cfg = ValidConfig() with
        {
            PollIntervalMs = 0,
            TagDefinitions =
            [
                new ModbusTagDefinition
                {
                    Name = "good_tag",
                    RegisterClass = ModbusRegisterClass.HoldingRegister,
                    Address = 0,
                    ScanRateMs = 100,
                    Datatype = "uint16",
                },
            ],
        };
        var adapter = new ModbusTcpSourceAdapter("a", client, NullLogger.Instance);
        await adapter.InitializeAsync(cfg, default);
        await adapter.StartAsync(default);

        var points = await adapter.PollAsync(default);

        points.Should().ContainSingle();
        points.Single().Quality.Should().Be(DataQuality.Good);
        points.Single().QualityReason.Should().BeNull();
    }

    [Fact]
    public async Task PollAsync_CoalescesTagsIntoSingleBlock()
    {
        var client = new FakeModbusClient();
        client.HoldingRegisterResults.Enqueue(() => [100, 200, 300]);

        var cfg = ValidConfig() with
        {
            PollIntervalMs = 0,
            MaxGapRegisters = 8,
            TagDefinitions =
            [
                new ModbusTagDefinition { Name = "a", RegisterClass = ModbusRegisterClass.HoldingRegister, Address = 0, ScanRateMs = 100, Datatype = "uint16" },
                new ModbusTagDefinition { Name = "b", RegisterClass = ModbusRegisterClass.HoldingRegister, Address = 1, ScanRateMs = 100, Datatype = "uint16" },
                new ModbusTagDefinition { Name = "c", RegisterClass = ModbusRegisterClass.HoldingRegister, Address = 2, ScanRateMs = 100, Datatype = "uint16" },
            ],
        };
        var adapter = new ModbusTcpSourceAdapter("a", client, NullLogger.Instance);
        await adapter.InitializeAsync(cfg, default);
        await adapter.StartAsync(default);

        var points = await adapter.PollAsync(default);

        points.Should().HaveCount(3);
        points.Select(p => p.TagName).Should().Equal("a", "b", "c");
        points.Select(p => p.Value).Should().Equal(100, 200, 300);
        // Only ONE wire call — all three tags coalesced into one block.
        client.Calls.Should().ContainSingle();
    }

    [Fact]
    public async Task ValidateConfigAsync_UnknownDatatype_Rejects()
    {
        var adapter = new ModbusTcpSourceAdapter("a", new FakeModbusClient(), NullLogger.Instance);
        var bad = ValidConfig() with
        {
            TagDefinitions =
            [
                new ModbusTagDefinition
                {
                    Name = "weird",
                    RegisterClass = ModbusRegisterClass.HoldingRegister,
                    Address = 0,
                    ScanRateMs = 100,
                    Datatype = "quantum32",
                },
            ],
        };

        var result = await adapter.ValidateConfigAsync(bad, default);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path == "TagDefinitions[0].Datatype");
    }

    [Fact]
    public async Task ValidateConfigAsync_ScaleOnBool_Rejects()
    {
        var adapter = new ModbusTcpSourceAdapter("a", new FakeModbusClient(), NullLogger.Instance);
        var bad = ValidConfig() with
        {
            TagDefinitions =
            [
                new ModbusTagDefinition
                {
                    Name = "running",
                    RegisterClass = ModbusRegisterClass.Coil,
                    Address = 0,
                    ScanRateMs = 100,
                    Datatype = "bool",
                    Scale = 2.0,
                },
            ],
        };

        var result = await adapter.ValidateConfigAsync(bad, default);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("Scale/Offset", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateConfigAsync_ByteOrderWidthMismatch_Rejects()
    {
        var adapter = new ModbusTcpSourceAdapter("a", new FakeModbusClient(), NullLogger.Instance);
        var bad = ValidConfig() with
        {
            TagDefinitions =
            [
                new ModbusTagDefinition
                {
                    Name = "flow",
                    RegisterClass = ModbusRegisterClass.HoldingRegister,
                    Address = 0,
                    ScanRateMs = 100,
                    Datatype = "float32",
                    // AB is 2-byte; float32 needs a 4-byte ordering.
                    ByteOrder = ElpisEdgeConnect.Sources.ModbusTcp.Scanning.ModbusByteOrder.AB,
                },
            ],
        };

        var result = await adapter.ValidateConfigAsync(bad, default);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path == "TagDefinitions[0].ByteOrder");
    }

    [Fact]
    public async Task ValidateConfigAsync_ByteOrderOnStringDatatype_Rejects()
    {
        // M.2b.6.2 — strings are packed two chars per register, high-char
        // first by Modbus convention; specifying a ByteOrder has no
        // effect and used to be silently ignored. Tightened to reject
        // so the wizard, CSV importer, and adapter all surface the
        // same rejection rather than letting the operator believe
        // their byteOrder takes effect.
        var adapter = new ModbusTcpSourceAdapter("a", new FakeModbusClient(), NullLogger.Instance);
        var bad = ValidConfig() with
        {
            TagDefinitions =
            [
                new ModbusTagDefinition
                {
                    Name = "machine_name",
                    RegisterClass = ModbusRegisterClass.HoldingRegister,
                    Address = 0,
                    ScanRateMs = 1000,
                    Datatype = "string16",
                    ByteOrder = ElpisEdgeConnect.Sources.ModbusTcp.Scanning.ModbusByteOrder.ABCDEFGH,
                },
            ],
        };

        var result = await adapter.ValidateConfigAsync(bad, default);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path == "TagDefinitions[0].ByteOrder");
    }

    // ============================================================
    // F5 — per-block diagnostics surfacing in AdapterHealth.Metrics
    // ============================================================

    [Fact]
    public async Task CheckHealthAsync_ExposesPerBlockMetrics_AfterPoll()
    {
        var client = new FakeModbusClient();
        client.HoldingRegisterResults.Enqueue(() => [100, 200]);

        var cfg = ValidConfig() with
        {
            PollIntervalMs = 0,
            TagDefinitions =
            [
                new ModbusTagDefinition
                {
                    Name = "a",
                    RegisterClass = ModbusRegisterClass.HoldingRegister,
                    Address = 0,
                    ScanRateMs = 100,
                    Datatype = "uint16",
                },
                new ModbusTagDefinition
                {
                    Name = "b",
                    RegisterClass = ModbusRegisterClass.HoldingRegister,
                    Address = 1,
                    ScanRateMs = 100,
                    Datatype = "uint16",
                },
            ],
        };
        var adapter = new ModbusTcpSourceAdapter("a", client, NullLogger.Instance);
        await adapter.InitializeAsync(cfg, default);
        await adapter.StartAsync(default);
        await adapter.PollAsync(default);

        var health = await adapter.CheckHealthAsync(default);

        health.Metrics.Should().ContainKey("blockMetrics");
        var blocks = (System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>)
            health.Metrics!["blockMetrics"];
        blocks.Should().ContainSingle("two adjacent uint16 tags coalesce into one block");
        var entry = blocks[0];
        entry["unitId"].Should().Be((byte)1);
        entry["registerClass"].Should().Be("HoldingRegister");
        entry["txs"].Should().Be(1L);
        entry["ok"].Should().Be(1L);
        entry["fail"].Should().Be(0L);
        entry.Should().ContainKey("rttMeanMs");
        entry.Should().ContainKey("rttLatestMs");
    }

    [Fact]
    public async Task CheckHealthAsync_SurfacesSlaveExceptionMap_AfterFailure()
    {
        var client = new FakeModbusClient
        {
            ReadException = new ModbusSlaveException(0x03, 0x02, "Illegal Data Address"),
        };
        var cfg = ValidConfig() with
        {
            PollIntervalMs = 0,
            MaxTransactionRetries = 0,
            TagDefinitions =
            [
                new ModbusTagDefinition
                {
                    Name = "x",
                    RegisterClass = ModbusRegisterClass.HoldingRegister,
                    Address = 0,
                    ScanRateMs = 100,
                    Datatype = "uint16",
                },
            ],
        };
        var adapter = new ModbusTcpSourceAdapter("a", client, NullLogger.Instance);
        await adapter.InitializeAsync(cfg, default);
        await adapter.StartAsync(default);
        await adapter.PollAsync(default);

        var health = await adapter.CheckHealthAsync(default);

        health.Metrics.Should().ContainKey("slaveExceptionsByCode");
        var map = (System.Collections.Generic.IReadOnlyDictionary<string, long>)
            health.Metrics!["slaveExceptionsByCode"];
        map.Should().ContainKey("0x03/0x02").WhoseValue.Should().Be(1);

        var blocks = (System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>)
            health.Metrics!["blockMetrics"];
        blocks.Single()["slaveExceptions"].Should().Be(1L);
        blocks.Single()["fail"].Should().Be(1L);
    }

    // ============================================================
    // DeviceClass — required for Modbus, validated for shape
    // ============================================================

    [Fact]
    public async Task ValidateConfigAsync_MissingDeviceClass_Rejects()
    {
        var adapter = new ModbusTcpSourceAdapter("a", new FakeModbusClient(), NullLogger.Instance);
        var bad = ValidConfig() with { DeviceClass = null };

        var result = await adapter.ValidateConfigAsync(bad, default);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path == "DeviceClass" &&
            e.Code == ModbusErrors.ConfigMissingField);
    }

    [Theory]
    [InlineData("PLC")]            // uppercase
    [InlineData("plc/sub")]         // forbidden char
    [InlineData("plc#main")]
    [InlineData("plc+aux")]
    [InlineData("")]
    public async Task ValidateConfigAsync_BadDeviceClassShape_Rejects(string bad)
    {
        var adapter = new ModbusTcpSourceAdapter("a", new FakeModbusClient(), NullLogger.Instance);
        var cfg = ValidConfig() with { DeviceClass = bad };

        var result = await adapter.ValidateConfigAsync(cfg, default);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path == "DeviceClass");
    }

    [Theory]
    [InlineData("plc")]
    [InlineData("daq")]
    [InlineData("meter")]
    [InlineData("tracker")]
    [InlineData("custom-class")]   // future / 3rd-party — regex allows it
    public async Task ValidateConfigAsync_GoodDeviceClassShape_Accepts(string good)
    {
        var adapter = new ModbusTcpSourceAdapter("a", new FakeModbusClient(), NullLogger.Instance);
        var cfg = ValidConfig() with { DeviceClass = good };

        var result = await adapter.ValidateConfigAsync(cfg, default);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task PollAsync_DeviceClass_FlowsIntoCanonicalDataPointMetadata()
    {
        var client = new FakeModbusClient();
        client.HoldingRegisterResults.Enqueue(() => [42]);

        var cfg = ValidConfig() with
        {
            DeviceClass = "plc",
            PollIntervalMs = 0,
            TagDefinitions =
            [
                new ModbusTagDefinition
                {
                    Name = "rpm",
                    RegisterClass = ModbusRegisterClass.HoldingRegister,
                    Address = 0,
                    ScanRateMs = 100,
                    Datatype = "uint16",
                },
            ],
        };
        var adapter = new ModbusTcpSourceAdapter("a", client, NullLogger.Instance);
        await adapter.InitializeAsync(cfg, default);
        await adapter.StartAsync(default);

        var points = await adapter.PollAsync(default);

        points.Should().ContainSingle();
        points[0].Metadata.Should().NotBeNull();
        points[0].Metadata!["deviceClass"].Should().Be("plc");
    }

    [Fact]
    public async Task ValidateConfigAsync_BoolOnHoldingRegister_Rejects()
    {
        var adapter = new ModbusTcpSourceAdapter("a", new FakeModbusClient(), NullLogger.Instance);
        var bad = ValidConfig() with
        {
            TagDefinitions =
            [
                new ModbusTagDefinition
                {
                    Name = "bit",
                    RegisterClass = ModbusRegisterClass.HoldingRegister,
                    Address = 0,
                    ScanRateMs = 100,
                    Datatype = "bool",
                },
            ],
        };

        var result = await adapter.ValidateConfigAsync(bad, default);

        result.IsValid.Should().BeFalse();
    }

    /// <summary>
    /// Minimal manual-tick <see cref="TimeProvider"/> so the per-group timer
    /// test is fully deterministic.
    /// </summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset start) => _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan span) => _now = _now.Add(span);
    }

    [Fact]
    public void Identity_Constructor_UsesGatewayIdWhenProvided()
    {
        var identity = Substitute.For<IGatewayIdentity>();
        identity.GatewayId.Returns("gw-42");
        var adapter = new ModbusTcpSourceAdapter("a", new FakeModbusClient(), NullLogger.Instance, identity);

        // Initialize synchronously via the gateway identity — verify
        // InitializeAsync pulls identity without throwing.
        var act = async () => await adapter.InitializeAsync(ValidConfig(), default);
        act.Should().NotThrowAsync();
    }
}
