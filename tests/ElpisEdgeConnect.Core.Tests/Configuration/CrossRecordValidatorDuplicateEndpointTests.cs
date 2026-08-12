// ============================================================================
// File: Configuration/CrossRecordValidatorDuplicateEndpointTests.cs
// Covers: Cross-record rule 13 — two or more ENABLED sources configured
//         against the same device endpoint.
//
// The rule is ADVISORY: it must land in Warnings, must leave IsValid true, and
// must never add an Error. Every test here that asserts a warning also asserts
// the apply is still permitted, because the whole point of the severity choice
// is that a configuration which works today keeps applying.
//
// Endpoint identity per protocol lives in SourceEndpointIdentity; these tests
// pin the observable behaviour of each shape, including the protocols that are
// deliberately EXCLUDED from the rule.
// ============================================================================

using System.Linq;
using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Errors;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Configuration;

public sealed class CrossRecordValidatorDuplicateEndpointTests
{
    private static readonly CrossRecordValidator Validator = CrossRecordValidator.Instance;

    // ------------------------------------------------------------------------
    // The reported defect: two Modbus sources aimed at one PLC.
    // ------------------------------------------------------------------------
    [Fact]
    public void Rule13_TwoEnabledSourcesOnSameModbusEndpoint_Warns()
    {
        var config = WithSources(
            Source("Modbus8", "modbustcp", """{ "host": "192.168.1.10", "port": 502 }"""),
            Source("AlenTesting", "modbustcp", """{ "host": "192.168.1.10", "port": 502 }"""));

        var result = Validator.Validate(config);

        result.Warnings.Should().ContainSingle()
            .Which.Code.Should().Be(CoreErrors.ConfigDuplicateSourceEndpoint);
    }

    [Fact]
    public void Rule13_DuplicateEndpoint_DoesNotBlockApply()
    {
        var config = WithSources(
            Source("Modbus8", "modbustcp", """{ "host": "192.168.1.10", "port": 502 }"""),
            Source("AlenTesting", "modbustcp", """{ "host": "192.168.1.10", "port": 502 }"""));

        var result = Validator.Validate(config);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Rule13_DuplicateEndpoint_MessageNamesBothSourcesAndTheEndpoint()
    {
        var config = WithSources(
            Source("Modbus8", "modbustcp", """{ "host": "192.168.1.10", "port": 502 }"""),
            Source("AlenTesting", "modbustcp", """{ "host": "192.168.1.10", "port": 502 }"""));

        var warning = Validator.Validate(config).Warnings.Single();

        warning.Message.Should().Contain("'AlenTesting'");
        warning.Message.Should().Contain("'Modbus8'");
        warning.Message.Should().Contain("192.168.1.10:502");
        warning.Path.Should().Be("Sources[AlenTesting].Connection");
    }

    // ------------------------------------------------------------------------
    // Disabled sources open no connection, so they spend no budget.
    // ------------------------------------------------------------------------
    [Fact]
    public void Rule13_SecondSourceDisabled_NotWarned()
    {
        var config = WithSources(
            Source("Modbus8", "modbustcp", """{ "host": "192.168.1.10", "port": 502 }"""),
            Source("AlenTesting", "modbustcp", """{ "host": "192.168.1.10", "port": 502 }""", enabled: false));

        var result = Validator.Validate(config);

        result.Warnings.Should().BeEmpty();
    }

    // ------------------------------------------------------------------------
    // Different ports on one host are different endpoints.
    // ------------------------------------------------------------------------
    [Fact]
    public void Rule13_SameHostDifferentPort_NotWarned()
    {
        var config = WithSources(
            Source("plc-a", "modbustcp", """{ "host": "192.168.1.10", "port": 502 }"""),
            Source("plc-b", "modbustcp", """{ "host": "192.168.1.10", "port": 5020 }"""));

        var result = Validator.Validate(config);

        result.Warnings.Should().BeEmpty();
    }

    // ------------------------------------------------------------------------
    // The adapter's documented port default is applied, so "port omitted" and
    // "port: 502" are recognised as the same endpoint.
    // ------------------------------------------------------------------------
    [Fact]
    public void Rule13_ModbusDefaultPortMatchesExplicitPort_Warns()
    {
        var config = WithSources(
            Source("plc-a", "modbustcp", """{ "host": "192.168.1.10" }"""),
            Source("plc-b", "modbustcp", """{ "host": "192.168.1.10", "port": 502 }"""));

        var result = Validator.Validate(config);

        result.Warnings.Should().ContainSingle()
            .Which.Code.Should().Be(CoreErrors.ConfigDuplicateSourceEndpoint);
    }

    // ------------------------------------------------------------------------
    // DELIBERATE: two different protocols sharing a host are NOT flagged.
    // They are different services on different ports with independent
    // connection budgets, so flagging them would be a false positive.
    // ------------------------------------------------------------------------
    [Fact]
    public void Rule13_DifferentProtocolsSharingOneHost_NotWarned()
    {
        var config = WithSources(
            Source("plc-modbus", "modbustcp", """{ "host": "192.168.1.10" }"""),
            Source("plc-s7", "s7", """{ "host": "192.168.1.10" }"""));

        var result = Validator.Validate(config);

        result.Warnings.Should().BeEmpty();
    }

    // ------------------------------------------------------------------------
    // DELIBERATE: three sources on one endpoint produce ONE grouped warning
    // naming all three, not one warning per pair.
    // ------------------------------------------------------------------------
    [Fact]
    public void Rule13_ThreeSourcesOnOneEndpoint_ReportedOnceListingAll()
    {
        var config = WithSources(
            Source("plc-a", "modbustcp", """{ "host": "192.168.1.10", "port": 502 }"""),
            Source("plc-b", "modbustcp", """{ "host": "192.168.1.10", "port": 502 }"""),
            Source("plc-c", "modbustcp", """{ "host": "192.168.1.10", "port": 502 }"""));

        var warning = Validator.Validate(config).Warnings.Should().ContainSingle().Which;

        warning.Message.Should().Contain("'plc-a'");
        warning.Message.Should().Contain("'plc-b'");
        warning.Message.Should().Contain("'plc-c'");
    }

    [Fact]
    public void Rule13_TwoIndependentDuplicatePairs_ReportedSeparately()
    {
        var config = WithSources(
            Source("a1", "modbustcp", """{ "host": "192.168.1.10" }"""),
            Source("a2", "modbustcp", """{ "host": "192.168.1.10" }"""),
            Source("b1", "modbustcp", """{ "host": "192.168.1.20" }"""),
            Source("b2", "modbustcp", """{ "host": "192.168.1.20" }"""));

        var result = Validator.Validate(config);

        result.Warnings.Should().HaveCount(2);
    }

    // ------------------------------------------------------------------------
    // Modbus serial RTU: the shared resource is the COM port, not a host.
    // Two sources on one serial line is the worse form of this conflict — a
    // serial port cannot be opened twice at all.
    // ------------------------------------------------------------------------
    [Fact]
    public void Rule13_TwoSerialRtuSourcesOnOneComPort_Warns()
    {
        var config = WithSources(
            Source("rtu-a", "modbusrtu", """{ "serialPort": "COM3", "baudRate": 9600 }"""),
            Source("rtu-b", "modbusrtu", """{ "serialPort": "COM3", "baudRate": 19200 }"""));

        var warning = Validator.Validate(config).Warnings.Should().ContainSingle().Which;

        warning.Message.Should().Contain("COM3");
    }

    [Fact]
    public void Rule13_SerialRtuSourcesOnDifferentComPorts_NotWarned()
    {
        var config = WithSources(
            Source("rtu-a", "modbusrtu", """{ "serialPort": "COM3" }"""),
            Source("rtu-b", "modbusrtu", """{ "serialPort": "COM4" }"""));

        var result = Validator.Validate(config);

        result.Warnings.Should().BeEmpty();
    }

    // ------------------------------------------------------------------------
    // S7 identity is host + rack + slot. Port is NOT part of the key (the
    // driver ignores it); rack/slot ARE (two CPUs can share a host).
    // ------------------------------------------------------------------------
    [Fact]
    public void Rule13_S7SameHostRackSlotDifferentPort_Warns()
    {
        var config = WithSources(
            Source("cpu-a", "s7", """{ "host": "10.0.0.5", "rack": 0, "slot": 1, "port": 102 }"""),
            Source("cpu-b", "s7", """{ "host": "10.0.0.5", "rack": 0, "slot": 1, "port": 1102 }"""));

        var result = Validator.Validate(config);

        result.Warnings.Should().ContainSingle()
            .Which.Code.Should().Be(CoreErrors.ConfigDuplicateSourceEndpoint);
    }

    [Fact]
    public void Rule13_S7SameHostDifferentSlot_NotWarned()
    {
        var config = WithSources(
            Source("cpu-a", "s7", """{ "host": "10.0.0.5", "rack": 0, "slot": 1 }"""),
            Source("cpu-b", "s7", """{ "host": "10.0.0.5", "rack": 0, "slot": 2 }"""));

        var result = Validator.Validate(config);

        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Rule13_S7OmittedRackSlotMatchesAdapterDefaults_Warns()
    {
        var config = WithSources(
            Source("cpu-a", "s7", """{ "host": "10.0.0.5" }"""),
            Source("cpu-b", "s7", """{ "host": "10.0.0.5", "rack": 0, "slot": 1 }"""));

        var result = Validator.Validate(config);

        result.Warnings.Should().ContainSingle();
    }

    // ------------------------------------------------------------------------
    // FOCAS2 identity is ipAddress + port. FANUC controls allow very few
    // concurrent handles, so this is the textbook case for the rule.
    // ------------------------------------------------------------------------
    [Fact]
    public void Rule13_TwoFocas2SourcesOnOneControl_Warns()
    {
        var config = WithSources(
            Source("cnc-a", "focas2", """{ "ipAddress": "192.168.0.30", "port": 8193 }"""),
            Source("cnc-b", "focas2", """{ "ipAddress": "192.168.0.30" }"""));

        var warning = Validator.Validate(config).Warnings.Should().ContainSingle().Which;

        warning.Message.Should().Contain("192.168.0.30:8193");
    }

    // ------------------------------------------------------------------------
    // OPC UA identity is the endpoint URL, normalised for case and a trailing
    // slash — the same server addressed two ways is still one server.
    // ------------------------------------------------------------------------
    [Fact]
    public void Rule13_OpcUaSameEndpointUrlWrittenDifferently_Warns()
    {
        var config = WithSources(
            Source("ua-a", "opcua-client", """{ "endpointUrl": "opc.tcp://Plant1:4840/" }"""),
            Source("ua-b", "opcua-client", """{ "endpointUrl": "opc.tcp://plant1:4840" }"""));

        var result = Validator.Validate(config);

        result.Warnings.Should().ContainSingle();
    }

    [Fact]
    public void Rule13_OpcUaDifferentEndpointUrls_NotWarned()
    {
        var config = WithSources(
            Source("ua-a", "opcua-client", """{ "endpointUrl": "opc.tcp://plant1:4840" }"""),
            Source("ua-b", "opcua-client", """{ "endpointUrl": "opc.tcp://plant2:4840" }"""));

        var result = Validator.Validate(config);

        result.Warnings.Should().BeEmpty();
    }

    // ------------------------------------------------------------------------
    // MELSEC identity is host + port.
    // ------------------------------------------------------------------------
    [Fact]
    public void Rule13_MelsecSameHostAndPort_Warns()
    {
        var config = WithSources(
            Source("plc-a", "melsec", """{ "host": "172.16.4.9", "port": 5007 }"""),
            Source("plc-b", "melsec", """{ "host": "172.16.4.9", "port": 5007 }"""));

        var result = Validator.Validate(config);

        result.Warnings.Should().ContainSingle();
    }

    // ------------------------------------------------------------------------
    // EtherNet/IP identity is host + explicit CIP path — one chassis gateway
    // routes to several CPUs by path.
    // ------------------------------------------------------------------------
    [Fact]
    public void Rule13_EthernetIpSameHostDifferentCipPath_NotWarned()
    {
        var config = WithSources(
            Source("cpu-a", "ethernetip", """{ "host": "10.10.1.5", "path": "1,0" }"""),
            Source("cpu-b", "ethernetip", """{ "host": "10.10.1.5", "path": "1,1" }"""));

        var result = Validator.Validate(config);

        result.Warnings.Should().BeEmpty();
    }

    // ------------------------------------------------------------------------
    // DELIBERATE EXCLUSIONS — HTTP-polled sources.
    //
    // An MTConnect Agent is an HTTP server with no low concurrent-connection
    // cap, and one agent base URL legitimately serves many machines (they are
    // told apart by agentDeviceName). Brother HTTP is the same shape. Warning
    // on these would be noise, so they are excluded from the rule.
    // ------------------------------------------------------------------------
    [Fact]
    public void Rule13_TwoMTConnectSourcesOnOneAgentUrl_NotWarned()
    {
        var config = WithSources(
            Source("mt-a", "mtconnect", """{ "agentBaseUrl": "http://10.0.0.9:5000", "agentDeviceName": "M1" }"""),
            Source("mt-b", "mtconnect", """{ "agentBaseUrl": "http://10.0.0.9:5000", "agentDeviceName": "M2" }"""));

        var result = Validator.Validate(config);

        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Rule13_TwoBrotherHttpSourcesOnOneBaseUrl_NotWarned()
    {
        var config = WithSources(
            Source("br-a", "brother-http", """{ "baseUrl": "http://10.0.0.40" }"""),
            Source("br-b", "brother-http", """{ "baseUrl": "http://10.0.0.40" }"""));

        var result = Validator.Validate(config);

        result.Warnings.Should().BeEmpty();
    }

    // ------------------------------------------------------------------------
    // Fail-open: anything the rule cannot read with confidence is skipped.
    // ------------------------------------------------------------------------
    [Fact]
    public void Rule13_UnknownProtocol_NotWarned()
    {
        var config = WithSources(
            Source("x-a", "some-future-protocol", """{ "host": "192.168.1.10", "port": 502 }"""),
            Source("x-b", "some-future-protocol", """{ "host": "192.168.1.10", "port": 502 }"""));

        var result = Validator.Validate(config);

        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Rule13_MissingConnectionBlock_NotWarned()
    {
        var config = B2TestFixtures.ValidMinimal() with
        {
            Sources =
            [
                new SourceInstanceConfig { InstanceId = "a", ProtocolName = "modbustcp", DeviceId = "A" },
                new SourceInstanceConfig { InstanceId = "b", ProtocolName = "modbustcp", DeviceId = "B" },
            ],
            Routes = [],
        };

        var result = Validator.Validate(config);

        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Rule13_MissingHost_NotWarned()
    {
        var config = WithSources(
            Source("a", "modbustcp", """{ "port": 502 }"""),
            Source("b", "modbustcp", """{ "port": 502 }"""));

        var result = Validator.Validate(config);

        result.Warnings.Should().BeEmpty();
    }

    // ------------------------------------------------------------------------
    // A warning must not mask real errors, and must not be produced for a
    // configuration that has none.
    // ------------------------------------------------------------------------
    [Fact]
    public void Rule13_ErrorsAndWarningsCoexist_BothReported()
    {
        var duplicated = WithSources(
            Source("a", "modbustcp", """{ "host": "192.168.1.10" }"""),
            Source("b", "modbustcp", """{ "host": "192.168.1.10" }"""));

        var config = duplicated with
        {
            Routes =
            [
                new RouteConfig
                {
                    RouteId = "r",
                    Name = "r",
                    SourceInstanceId = "missing-source",
                    SinkInstanceIds = ["sink-1"],
                },
            ],
        };

        var result = Validator.Validate(config);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == CoreErrors.RouteSourceNotFound);
        result.Warnings.Should().Contain(w => w.Code == CoreErrors.ConfigDuplicateSourceEndpoint);
    }

    [Fact]
    public void Rule13_NoDuplicates_ProducesNoWarnings()
    {
        var result = Validator.Validate(B2TestFixtures.ValidWithMultiple());

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    // ------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------

    private static SourceInstanceConfig Source(
        string instanceId,
        string protocolName,
        string connectionJson,
        bool enabled = true) =>
        new()
        {
            InstanceId = instanceId,
            ProtocolName = protocolName,
            DeviceId = instanceId,
            Enabled = enabled,
            Connection = JsonSerializer.Deserialize<JsonElement>(connectionJson),
        };

    private static GatewayConfiguration WithSources(params SourceInstanceConfig[] sources) =>
        B2TestFixtures.ValidMinimal() with
        {
            Sources = sources,
            Routes = [],
        };
}
