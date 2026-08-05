// ============================================================================
// File: Session/SparkplugMqttTransportProfileTests.cs
// Purpose: Locks the concrete MQTTnet transport's wire profile WITHOUT a broker
//          (slice-4 review B3). The option/message builders and the CONNACK/SUBACK
//          validators are pure internal statics, so the pinned MQTT 3.1.1 profile —
//          protocol version, clean session, keep-alive, credentials, TLS selection,
//          QoS-1 non-retained NDEATH Will, QoS-0 non-retained DATA/BIRTH publish,
//          exact-NCMD QoS-1 SUBACK requirement, and CONNACK acceptance — is proven
//          deterministically. Real socket/broker interop remains a K6 concern.
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Sinks.SparkplugB;
using ElpisEdgeConnect.Sinks.SparkplugB.Session;
using FluentAssertions;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using Xunit;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Session;

public sealed class SparkplugMqttTransportProfileTests
{
    private const string WillTopic = "spBv1.0/PlantA/NDEATH/gw-1";
    private const string NcmdTopic = "spBv1.0/PlantA/NCMD/gw-1";
    private static readonly byte[] WillBytes = { 0x01, 0x02 };

    private static SparkplugMqttConnectRequest Request(
        bool tls = false, string? username = null, string? password = null, int keepAlive = 30) =>
        SparkplugMqttConnectRequest.Create(
            BrokerEndpoint.Create("broker.example", 8883, tls), "edge-01", username, password, keepAlive,
            cleanSession: true, WillTopic, WillBytes);

    // ==== CONNECT options ====

    [Fact]
    public void BuildConnectOptions_PinsMqtt311()
    {
        var options = SparkplugMqttTransport.BuildConnectOptions(Request());

        options.ProtocolVersion.Should().Be(MqttProtocolVersion.V311); // protocol level 4 — wire contract
    }

    [Fact]
    public void BuildConnectOptions_RequestsCleanSessionAndKeepAlive()
    {
        var options = SparkplugMqttTransport.BuildConnectOptions(Request(keepAlive: 45));

        options.CleanSession.Should().BeTrue();
        options.KeepAlivePeriod.TotalSeconds.Should().Be(45);
        options.ClientId.Should().Be("edge-01");
    }

    [Fact]
    public void BuildConnectOptions_ConfiguresQoS1NonRetainedNDeathWill()
    {
        var options = SparkplugMqttTransport.BuildConnectOptions(Request());

        options.WillTopic.Should().Be(WillTopic);
        options.WillPayload.Should().Equal(WillBytes);
        options.WillQualityOfServiceLevel.Should().Be(MqttQualityOfServiceLevel.AtLeastOnce); // QoS 1
        options.WillRetain.Should().BeFalse();
    }

    [Fact]
    public void BuildConnectOptions_WithCredentials_SetsThem()
    {
        var options = SparkplugMqttTransport.BuildConnectOptions(Request(username: "u", password: "p"));

        options.Credentials.Should().NotBeNull();
        options.Credentials!.GetUserName(options).Should().Be("u");
    }

    [Fact]
    public void BuildConnectOptions_WithoutCredentials_LeavesThemUnset()
    {
        SparkplugMqttTransport.BuildConnectOptions(Request()).Credentials.Should().BeNull();
    }

    [Fact]
    public void BuildConnectOptions_Tls_EnablesTls()
    {
        var options = SparkplugMqttTransport.BuildConnectOptions(Request(tls: true));

        options.ChannelOptions.Should().BeOfType<MqttClientTcpOptions>()
            .Which.TlsOptions.UseTls.Should().BeTrue();
    }

    [Fact]
    public void BuildConnectOptions_Plain_DoesNotEnableTls()
    {
        var options = SparkplugMqttTransport.BuildConnectOptions(Request(tls: false));

        options.ChannelOptions.Should().BeOfType<MqttClientTcpOptions>()
            .Which.TlsOptions.UseTls.Should().BeFalse();
    }

    // ==== SUBSCRIBE options ====

    [Fact]
    public void BuildSubscribeOptions_RequestsExactTopicAtQoS1()
    {
        var options = SparkplugMqttTransport.BuildSubscribeOptions(NcmdTopic);

        var filter = options.TopicFilters.Should().ContainSingle().Subject;
        filter.Topic.Should().Be(NcmdTopic);
        filter.QualityOfServiceLevel.Should().Be(MqttQualityOfServiceLevel.AtLeastOnce); // QoS 1
    }

    // ==== PUBLISH message (DATA / BIRTH) ====

    [Fact]
    public void BuildPublishMessage_IsQoS0NonRetained()
    {
        var message = SparkplugMqttTransport.BuildPublishMessage("spBv1.0/PlantA/NBIRTH/gw-1", new byte[] { 9, 8, 7 });

        message.Topic.Should().Be("spBv1.0/PlantA/NBIRTH/gw-1");
        message.QualityOfServiceLevel.Should().Be(MqttQualityOfServiceLevel.AtMostOnce); // QoS 0
        message.Retain.Should().BeFalse();
        message.PayloadSegment.ToArray().Should().Equal(9, 8, 7);
    }

    // ==== CONNACK validation ====

    [Fact]
    public void RequireConnectSuccess_OnSuccess_DoesNotThrow() =>
        FluentActions.Invoking(() => SparkplugMqttTransport.RequireConnectSuccess(true, "Success"))
            .Should().NotThrow();

    [Fact]
    public void RequireConnectSuccess_OnRefusal_ThrowsTransportConnectFailed()
    {
        FluentActions.Invoking(() => SparkplugMqttTransport.RequireConnectSuccess(false, "NotAuthorized"))
            .Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.TransportConnectFailed);
    }

    // ==== SUBACK validation ====

    [Fact]
    public void RequireExactNcmdGrant_ExactQoS1_DoesNotThrow() =>
        FluentActions.Invoking(() => SparkplugMqttTransport.RequireExactNcmdGrant(
                new List<KeyValuePair<string, int>> { new(NcmdTopic, 1) }, NcmdTopic))
            .Should().NotThrow();

    [Theory]
    [InlineData(0)]   // downgraded to QoS 0 — NCMD control path not established at QoS 1
    [InlineData(-1)]  // failure result
    public void RequireExactNcmdGrant_WrongQoS_ThrowsTransportSubscribeFailed(int grantedQos)
    {
        FluentActions.Invoking(() => SparkplugMqttTransport.RequireExactNcmdGrant(
                new List<KeyValuePair<string, int>> { new(NcmdTopic, grantedQos) }, NcmdTopic))
            .Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.TransportSubscribeFailed);
    }

    [Fact]
    public void RequireExactNcmdGrant_WrongTopic_ThrowsTransportSubscribeFailed()
    {
        FluentActions.Invoking(() => SparkplugMqttTransport.RequireExactNcmdGrant(
                new List<KeyValuePair<string, int>> { new("spBv1.0/Other/NCMD/gw-1", 1) }, NcmdTopic))
            .Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.TransportSubscribeFailed);
    }

    [Theory]
    [InlineData(0)]  // no grant at all
    [InlineData(2)]  // more than the single expected filter
    public void RequireExactNcmdGrant_WrongGrantCount_ThrowsTransportSubscribeFailed(int count)
    {
        var grants = new List<KeyValuePair<string, int>>();
        for (var i = 0; i < count; i++)
        {
            grants.Add(new KeyValuePair<string, int>(NcmdTopic, 1));
        }

        FluentActions.Invoking(() => SparkplugMqttTransport.RequireExactNcmdGrant(grants, NcmdTopic))
            .Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.TransportSubscribeFailed);
    }
}
