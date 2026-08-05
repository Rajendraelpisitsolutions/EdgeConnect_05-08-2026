// ============================================================================
// File: Configuration/SparkplugSinkConfigurationTests.cs
// Purpose: Locks K3 slice-1 configuration parsing (FromSinkInstance, incl. STRICT
//          type checking per review B3), the semantic validator, the bounded
//          transport-recovery defaults (plan v3 §4.7), topic-safe group/edge-node
//          rules, and secret non-disclosure (redacted ToString / no secret in
//          validation output).
// ============================================================================

using System.Text.Json;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Sinks.SparkplugB;
using ElpisEdgeConnect.Sinks.SparkplugB.Configuration;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Configuration;

public sealed class SparkplugSinkConfigurationTests
{
    private const string SecretSentinel = "SUPERSECRET-do-not-log-123";

    // ==== FromSinkInstance ====

    [Fact]
    public void FromSinkInstance_MinimalConnection_ParsesRequiredFieldsAndDefaults()
    {
        var instance = Instance(new { brokerHost = "localhost", groupId = "PlantA", edgeNodeId = "gw-1" });

        var cfg = SparkplugSinkConfiguration.FromSinkInstance(instance);

        cfg.BrokerHost.Should().Be("localhost");
        cfg.GroupId.Should().Be("PlantA");
        cfg.EdgeNodeId.Should().Be("gw-1");
        cfg.BrokerPort.Should().BeNull(); // omitted → resolved to the default at endpoint construction
        cfg.ResolveBrokerEndpoint().Port.Should().Be(1883);
        cfg.KeepAliveSeconds.Should().Be(30);
        cfg.TransportRecoveryMaxAttempts.Should().Be(3);
        cfg.TransportRecoveryInitialDelayMs.Should().Be(1000);
        cfg.TransportRecoveryMaxDelayMs.Should().Be(30000);
    }

    [Fact]
    public void FromSinkInstance_FullConnection_ParsesOverrides()
    {
        var instance = Instance(new
        {
            brokerHost = "broker.example",
            brokerPort = 8883,
            useTls = true,
            clientId = "edge-01",
            username = "u",
            password = "p",
            groupId = "PlantA",
            edgeNodeId = "gw-1",
            keepAliveSeconds = 45,
            transportRecoveryMaxAttempts = 5,
            transportRecoveryInitialDelayMs = 500,
            transportRecoveryMaxDelayMs = 20000,
        });

        var cfg = SparkplugSinkConfiguration.FromSinkInstance(instance);

        cfg.BrokerPort.Should().Be(8883);
        cfg.UseTls.Should().BeTrue();
        cfg.ClientId.Should().Be("edge-01");
        cfg.Username.Should().Be("u");
        cfg.Password.Should().Be("p");
        cfg.KeepAliveSeconds.Should().Be(45);
        cfg.TransportRecoveryMaxAttempts.Should().Be(5);
        cfg.TransportRecoveryInitialDelayMs.Should().Be(500);
        cfg.TransportRecoveryMaxDelayMs.Should().Be(20000);
    }

    [Fact]
    public void FromSinkInstance_WrongProtocol_Throws()
    {
        var instance = Instance(new { brokerHost = "h", groupId = "g", edgeNodeId = "n" }, protocol: "mqtt");

        var act = () => SparkplugSinkConfiguration.FromSinkInstance(instance);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromSinkInstance_MissingConnection_Throws()
    {
        var instance = new SinkInstanceConfig { InstanceId = "spb-1", ProtocolName = "sparkplug-b" };

        var act = () => SparkplugSinkConfiguration.FromSinkInstance(instance);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("brokerHost")]
    [InlineData("groupId")]
    [InlineData("edgeNodeId")]
    public void FromSinkInstance_MissingRequiredField_Throws(string omit)
    {
        var fields = new Dictionary<string, object>
        {
            ["brokerHost"] = "h",
            ["groupId"] = "g",
            ["edgeNodeId"] = "n",
        };
        fields.Remove(omit);
        var instance = Instance(fields);

        var act = () => SparkplugSinkConfiguration.FromSinkInstance(instance);

        act.Should().Throw<ArgumentException>();
    }

    // ==== FromSinkInstance strict type checking (review B3) ====

    [Theory] // every numeric field wired through the strict reader (guards wrong-reader copy/paste)
    [InlineData("brokerPort")]
    [InlineData("keepAliveSeconds")]
    [InlineData("transportRecoveryMaxAttempts")]
    [InlineData("transportRecoveryInitialDelayMs")]
    [InlineData("transportRecoveryMaxDelayMs")]
    public void FromSinkInstance_NumericFieldAsString_EachRejectedNotCoerced(string field)
    {
        var fields = new Dictionary<string, object>
        {
            ["brokerHost"] = "h",
            ["groupId"] = "g",
            ["edgeNodeId"] = "n",
            [field] = "not-a-number",
        };
        var instance = Instance(fields);

        var act = () => SparkplugSinkConfiguration.FromSinkInstance(instance);

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.ConfigInvalidFieldType);
    }

    [Fact]
    public void FromSinkInstance_BooleanFieldAsString_RejectedNotCoerced()
    {
        var instance = Instance(new { brokerHost = "h", groupId = "g", edgeNodeId = "n", useTls = "true" });

        var act = () => SparkplugSinkConfiguration.FromSinkInstance(instance);

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.ConfigInvalidFieldType);
    }

    [Fact]
    public void FromSinkInstance_NumericOverflow_Rejected()
    {
        var instance = Instance(new { brokerHost = "h", groupId = "g", edgeNodeId = "n", brokerPort = 99999999999L });

        var act = () => SparkplugSinkConfiguration.FromSinkInstance(instance);

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.ConfigInvalidFieldType);
    }

    [Fact]
    public void FromSinkInstance_WrongTypeError_DoesNotLeakFieldValue()
    {
        // The password value must never appear in a field-type diagnostic.
        var instance = Instance(new { brokerHost = "h", groupId = "g", edgeNodeId = "n", password = 12345 });

        var act = () => SparkplugSinkConfiguration.FromSinkInstance(instance);

        act.Should().Throw<AdapterException>().Which.Message.Should().NotContain("12345");
    }

    // ==== Validator ====

    [Fact]
    public void Validate_ValidConfig_Succeeds()
    {
        SparkplugSinkConfigurationValidator.Validate(ValidConfig()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_MissingBrokerHost_Fails()
    {
        var result = SparkplugSinkConfigurationValidator.Validate(ValidConfig() with { BrokerHost = "" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(i => i.Code == SparkplugErrors.ConfigMissingBrokerHost);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void Validate_InvalidPort_Fails(int port)
    {
        var result = SparkplugSinkConfigurationValidator.Validate(ValidConfig() with { BrokerPort = port });

        result.Errors.Should().Contain(i => i.Code == SparkplugErrors.ConfigInvalidBrokerPort);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("a+b")]
    [InlineData("a#b")]
    public void Validate_ForbiddenGroupId_Fails(string groupId)
    {
        var result = SparkplugSinkConfigurationValidator.Validate(ValidConfig() with { GroupId = groupId });

        result.Errors.Should().Contain(i => i.Code == SparkplugErrors.ConfigInvalidGroupId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("a+b")]
    [InlineData("a#b")]
    public void Validate_ForbiddenEdgeNodeId_Fails(string edgeNodeId)
    {
        var result = SparkplugSinkConfigurationValidator.Validate(ValidConfig() with { EdgeNodeId = edgeNodeId });

        result.Errors.Should().Contain(i => i.Code == SparkplugErrors.ConfigInvalidEdgeNodeId);
    }

    [Theory]
    [InlineData(0)]      // below the 1s minimum
    [InlineData(65536)]  // above the 16-bit MQTT 3.1.1 maximum (65535)
    public void Validate_KeepAliveOutOfRange_Fails(int keepAlive)
    {
        var result = SparkplugSinkConfigurationValidator.Validate(ValidConfig() with { KeepAliveSeconds = keepAlive });

        result.Errors.Should().Contain(i => i.Code == SparkplugErrors.ConfigInvalidKeepAlive);
    }

    [Fact]
    public void Validate_KeepAliveAtMaximum_Succeeds() =>
        SparkplugSinkConfigurationValidator.Validate(ValidConfig() with { KeepAliveSeconds = 65535 })
            .IsValid.Should().BeTrue();

    [Theory]
    [InlineData("")]     // present-but-empty must be rejected, not treated as omitted
    [InlineData("   ")]  // whitespace-only is not a valid client id
    public void Validate_BlankClientId_Fails(string clientId)
    {
        var result = SparkplugSinkConfigurationValidator.Validate(ValidConfig() with { ClientId = clientId });

        result.Errors.Should().Contain(i => i.Code == SparkplugErrors.ConfigInvalidClientId);
    }

    [Fact]
    public void Validate_OmittedClientId_Succeeds() =>
        // null → auto-generated at Begin; never reaches the connect request as a blank id.
        SparkplugSinkConfigurationValidator.Validate(ValidConfig() with { ClientId = null })
            .IsValid.Should().BeTrue();

    [Fact]
    public void Validate_NonPositiveRecoveryAttempts_Fails()
    {
        var result = SparkplugSinkConfigurationValidator.Validate(
            ValidConfig() with { TransportRecoveryMaxAttempts = 0 });

        result.Errors.Should().Contain(i => i.Code == SparkplugErrors.ConfigInvalidRecoveryBudget);
    }

    [Fact]
    public void Validate_MaxDelayBelowInitialDelay_Fails()
    {
        var result = SparkplugSinkConfigurationValidator.Validate(
            ValidConfig() with { TransportRecoveryInitialDelayMs = 5000, TransportRecoveryMaxDelayMs = 1000 });

        result.Errors.Should().Contain(i => i.Code == SparkplugErrors.ConfigInvalidRecoveryBudget);
    }

    [Fact]
    public void Validate_IncompleteAuth_Fails()
    {
        var result = SparkplugSinkConfigurationValidator.Validate(
            ValidConfig() with { Username = "u", Password = null });

        result.Errors.Should().Contain(i => i.Code == SparkplugErrors.ConfigAuthIncomplete);
    }

    [Fact]
    public void Validate_WhitespaceUsernameCountsAsPresent_FlagsIncompleteAuth()
    {
        // Documents intentional IsNullOrEmpty semantics: whitespace-only is "present".
        var result = SparkplugSinkConfigurationValidator.Validate(
            ValidConfig() with { Username = " ", Password = null });

        result.Errors.Should().Contain(i => i.Code == SparkplugErrors.ConfigAuthIncomplete);
    }

    [Fact]
    public void Validate_WrongType_FailsWithConfigWrongType()
    {
        var result = SparkplugSinkConfigurationValidator.Validate(
            new OtherSinkConfiguration { InstanceId = "x", ProtocolName = "other" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(i => i.Code == SparkplugErrors.ConfigWrongType);
    }

    [Fact]
    public void Validate_InvalidConfigWithSecret_NeverEmitsPasswordValue()
    {
        var cfg = ValidConfig() with { Username = "u", Password = SecretSentinel, BrokerPort = 0 };

        var result = SparkplugSinkConfigurationValidator.Validate(cfg);

        result.Errors.Should().NotContain(i => i.Message.Contains(SecretSentinel));
    }

    // ==== Secret non-disclosure (ToString) ====

    [Fact]
    public void ToString_RedactsCredentials()
    {
        var cfg = ValidConfig() with { Username = "operator", Password = SecretSentinel };

        var text = cfg.ToString();

        text.Should().NotContain(SecretSentinel);
        text.Should().NotContain("operator");
        text.Should().Contain("***");
    }

    [Fact]
    public void ConnectionKeys_All_CoversEveryKey()
    {
        // The public field type is FrozenSet<string> — immutable by construction
        // (cannot be cast back to a mutable set). Verify it is complete.
        SparkplugConnectionKeys.All.Should().Contain("brokerHost").And.Contain("password");
        SparkplugConnectionKeys.All.Count.Should().Be(12);
    }

    // ==== Broker endpoint resolution (v3.2 omitted-vs-explicit port) ====

    [Fact]
    public void ResolveBrokerEndpoint_OmittedPort_Plain_Defaults1883() =>
        (ValidConfig() with { BrokerPort = null, UseTls = false }).ResolveBrokerEndpoint().Port.Should().Be(1883);

    [Fact]
    public void ResolveBrokerEndpoint_OmittedPort_Tls_Defaults8883() =>
        (ValidConfig() with { BrokerPort = null, UseTls = true }).ResolveBrokerEndpoint().Port.Should().Be(8883);

    [Fact]
    public void ResolveBrokerEndpoint_ExplicitPort_UsedAsIs() =>
        (ValidConfig() with { BrokerPort = 8884 }).ResolveBrokerEndpoint().Port.Should().Be(8884);

    [Fact]
    public void ResolveBrokerEndpoint_HostNormalizedAndTlsCarried()
    {
        var endpoint = (ValidConfig() with { BrokerHost = "Broker.EXAMPLE.com", UseTls = true, BrokerPort = null })
            .ResolveBrokerEndpoint();
        endpoint.Host.Should().Be("broker.example.com");
        endpoint.Tls.Should().BeTrue();
    }

    // ==== Helpers ====

    private static SparkplugSinkConfiguration ValidConfig() => new()
    {
        InstanceId = "spb-1",
        ProtocolName = SparkplugBProtocol.ProtocolName,
        BrokerHost = "localhost",
        GroupId = "PlantA",
        EdgeNodeId = "gw-1",
    };

    private static SinkInstanceConfig Instance(object connection, string protocol = "sparkplug-b") => new()
    {
        InstanceId = "spb-1",
        ProtocolName = protocol,
        Connection = JsonSerializer.SerializeToElement(connection),
    };

    private sealed record OtherSinkConfiguration : SinkConfiguration;
}
