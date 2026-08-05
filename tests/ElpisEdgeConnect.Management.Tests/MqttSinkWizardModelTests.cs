// ============================================================================
// Tests: MqttSinkWizardModel — pins the MQTT sink wizard view-model
//        (M.2b.6). Key invariants:
//
//          * Defaults match runtime contract (PerTag mode for EREMOS V2
//            compat, QoS 1, port 1883, EREMOS V2 PerTag topic).
//          * BuildSinkInstance is a faithful projection — every wizard
//            field roundtrips through SinkInstanceConfig.Connection
//            and FromSinkInstance reconstructs it.
//          * Eager validation: broker host required, port range,
//            username/password pair completeness, topic template
//            non-empty, QoS 0 or 1 only.
//          * Authentication = None omits username/password from the
//            payload entirely (no empty-string ghost fields).
// Reference: docs/sessions/2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v3.md §5
// ============================================================================

using System;
using ElpisEdgeConnect.Management.Wizards;
using ElpisEdgeConnect.Sinks.Mqtt;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class MqttSinkWizardModelTests
{
    // ─── Defaults ────────────────────────────────────────────────────────

    [Fact]
    public void Defaults_MatchRuntimeContract()
    {
        var model = new MqttSinkWizardModel();

        model.Enabled.Should().BeTrue();
        model.BrokerPort.Should().Be(1883, "default MQTT plain TCP port");
        model.UseTls.Should().BeFalse();
        model.KeepAliveSeconds.Should().Be(30);
        model.Authentication.Should().Be(MqttAuthenticationMode.None);
        model.PublishMode.Should().Be(MqttPublishMode.PerTag, "EREMOS V2 compatibility");
        model.QosLevel.Should().Be(1);
        model.RetainDefault.Should().BeFalse();
        model.PerTagTopicTemplate.Should().Be("eremos/{gatewayId}/{deviceClass}/{sourceId}/{tagName}",
            "EREMOS V2 subscription expects this exact shape");
    }

    // ─── Projection ──────────────────────────────────────────────────────

    [Fact]
    public void BuildSinkInstance_HappyPath_ProducesValidConfig()
    {
        var model = MakeMinimallyValidModel();
        model.InstanceId = "mqtt-eremos";

        var sink = model.BuildSinkInstance();

        sink.InstanceId.Should().Be("mqtt-eremos");
        sink.ProtocolName.Should().Be(MqttSinkConfiguration.ProtocolNameConstant);
        sink.Enabled.Should().BeTrue();
        sink.Connection.Should().NotBeNull();
    }

    [Fact]
    public void BuildSinkInstance_RoundtripsViaFromSinkInstance()
    {
        // Pin the wizard ↔ adapter contract — every field the wizard
        // writes must be a field FromSinkInstance reads back.
        var model = new MqttSinkWizardModel
        {
            InstanceId = "mqtt-test",
            BrokerHost = "broker.example.com",
            BrokerPort = 8883,
            UseTls = true,
            KeepAliveSeconds = 45,
            ClientId = "custom-client-id",
            Authentication = MqttAuthenticationMode.UsernamePassword,
            Username = "alice",
            Password = "s3cret",
            PublishMode = MqttPublishMode.Batch,
            BatchTopicTemplate = "edgeconnect/{gatewayId}/batch",
            QosLevel = 0,
            RetainDefault = true,
            ReconnectDelayMs = 1000,
            MaxReconnectDelayMs = 30_000,
            ReconnectMultiplier = 1.5,
        };

        var instance = model.BuildSinkInstance();
        var cfg = MqttSinkConfiguration.FromSinkInstance(instance);

        cfg.InstanceId.Should().Be("mqtt-test");
        cfg.BrokerHost.Should().Be("broker.example.com");
        cfg.BrokerPort.Should().Be(8883);
        cfg.UseTls.Should().BeTrue();
        cfg.KeepAliveSeconds.Should().Be(45);
        cfg.ClientId.Should().Be("custom-client-id");
        cfg.Username.Should().Be("alice");
        cfg.Password.Should().Be("s3cret");
        cfg.PublishMode.Should().Be(MqttPublishMode.Batch);
        cfg.TopicTemplate.Should().Be("edgeconnect/{gatewayId}/batch");
        cfg.QosLevel.Should().Be(0);
        cfg.RetainDefault.Should().BeTrue();
        cfg.ReconnectDelayMs.Should().Be(1000);
        cfg.MaxReconnectDelayMs.Should().Be(30_000);
        cfg.ReconnectMultiplier.Should().Be(1.5);
    }

    [Fact]
    public void BuildSinkInstance_AuthNone_OmitsCredentialsFromPayload()
    {
        // Defensive: when authentication is None, the wizard must NOT
        // emit empty username/password fields. The adapter treats partial
        // auth as a config error, so empty-string ghost fields could trip
        // future validation.
        var model = MakeMinimallyValidModel();
        model.Authentication = MqttAuthenticationMode.None;
        model.Username = "ignored";
        model.Password = "ignored";

        var instance = model.BuildSinkInstance();
        var cfg = MqttSinkConfiguration.FromSinkInstance(instance);

        cfg.Username.Should().BeNull();
        cfg.Password.Should().BeNull();
    }

    [Fact]
    public void BuildSinkInstance_EmptyClientId_OmitsFromPayload()
    {
        // Adapter auto-generates "edgeconnect-{InstanceId}" when ClientId is null —
        // verify the wizard doesn't write an empty-string field that would override
        // the auto-generation path.
        var model = MakeMinimallyValidModel();
        model.ClientId = "";

        var instance = model.BuildSinkInstance();
        var cfg = MqttSinkConfiguration.FromSinkInstance(instance);

        cfg.ClientId.Should().BeNull();
    }

    // ─── Eager validation ────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateBrokerHost_BlankOrWhitespace_Rejected(string host)
    {
        var model = new MqttSinkWizardModel { BrokerHost = host };
        model.ValidateBrokerHost().Should().NotBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65_536)]
    public void ValidateBrokerPort_OutOfRange_Rejected(int port)
    {
        var model = new MqttSinkWizardModel { BrokerPort = port };
        model.ValidateBrokerPort().Should().NotBeNull();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1883)]
    [InlineData(8883)]
    [InlineData(65535)]
    public void ValidateBrokerPort_InRange_Accepted(int port)
    {
        var model = new MqttSinkWizardModel { BrokerPort = port };
        model.ValidateBrokerPort().Should().BeNull();
    }

    [Fact]
    public void ValidateAuthentication_UsernamePassword_RequiresBoth()
    {
        var model = new MqttSinkWizardModel
        {
            Authentication = MqttAuthenticationMode.UsernamePassword,
            Username = "alice",
            Password = "",
        };

        model.ValidateAuthentication().Should().NotBeNull()
            .And.Subject.Should().Contain("Password");

        model.Password = "s3cret";
        model.Username = "";
        model.ValidateAuthentication().Should().NotBeNull()
            .And.Subject.Should().Contain("Username");

        model.Username = "alice";
        model.ValidateAuthentication().Should().BeNull();
    }

    [Fact]
    public void ValidateAuthentication_None_IgnoresCredentialFields()
    {
        // When the operator switched from UserPass back to None, lingering
        // username/password values in the model must NOT fail validation.
        var model = new MqttSinkWizardModel
        {
            Authentication = MqttAuthenticationMode.None,
            Username = "leftover",
            Password = "",
        };

        model.ValidateAuthentication().Should().BeNull();
    }

    [Fact]
    public void ValidateTopicTemplate_PerTag_ChecksPerTagTemplate()
    {
        var model = new MqttSinkWizardModel
        {
            PublishMode = MqttPublishMode.PerTag,
            PerTagTopicTemplate = "",
            BatchTopicTemplate = "edgeconnect/data",
        };

        model.ValidateTopicTemplate().Should().NotBeNull();
    }

    [Fact]
    public void ValidateTopicTemplate_Batch_ChecksBatchTemplate()
    {
        var model = new MqttSinkWizardModel
        {
            PublishMode = MqttPublishMode.Batch,
            PerTagTopicTemplate = "eremos/x",
            BatchTopicTemplate = "",
        };

        model.ValidateTopicTemplate().Should().NotBeNull();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    [InlineData(3)]
    public void ValidateQosLevel_NotZeroOrOne_Rejected(int qos)
    {
        var model = new MqttSinkWizardModel { QosLevel = qos };
        model.ValidateQosLevel().Should().NotBeNull()
            .And.Subject.Should().Contain("QoS 2 is not supported");
    }

    // ─── Defensive projection guards ─────────────────────────────────────

    [Fact]
    public void BuildSinkInstance_BlankBrokerHost_Throws()
    {
        var model = MakeMinimallyValidModel();
        model.BrokerHost = "";

        var act = () => model.BuildSinkInstance();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void BuildSinkInstance_AuthMissingPassword_Throws()
    {
        var model = MakeMinimallyValidModel();
        model.Authentication = MqttAuthenticationMode.UsernamePassword;
        model.Username = "alice";
        model.Password = "";

        var act = () => model.BuildSinkInstance();
        act.Should().Throw<InvalidOperationException>();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static MqttSinkWizardModel MakeMinimallyValidModel() => new()
    {
        InstanceId = "mqtt-1",
        BrokerHost = "localhost",
        BrokerPort = 1883,
    };
}
