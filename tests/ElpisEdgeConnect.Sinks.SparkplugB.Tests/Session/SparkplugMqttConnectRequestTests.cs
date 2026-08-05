// ============================================================================
// File: Session/SparkplugMqttConnectRequestTests.cs
// Purpose: Locks the per-attempt connect request's secret-safety (slice-4 review B4):
//          the redacted ToString never emits username / password / Will bytes, and the
//          Will payload is defensively copied so a caller's later mutation can never
//          reach into the immutable request.
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sinks.SparkplugB.Session;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Session;

public sealed class SparkplugMqttConnectRequestTests
{
    private const string SecretUser = "operator-XYZ";
    private const string SecretPass = "SUPERSECRET-do-not-log-123";
    private static readonly byte[] WillBytes = { 0xDE, 0xAD, 0xBE, 0xEF };

    private static SparkplugMqttConnectRequest Create(ReadOnlyMemory<byte> will) =>
        SparkplugMqttConnectRequest.Create(
            BrokerEndpoint.Create("broker.example", 8883, tls: true),
            clientId: "edge-01", username: SecretUser, password: SecretPass, keepAliveSeconds: 30,
            cleanSession: true, willTopic: "spBv1.0/PlantA/NDEATH/gw-1", willPayload: will);

    [Fact]
    public void ToString_NeverEmitsCredentialsOrWillBytes()
    {
        var text = Create(WillBytes).ToString();

        text.Should().NotContain(SecretUser);
        text.Should().NotContain(SecretPass);
        text.Should().NotContain("DEAD");     // no Will byte content in any form
        text.Should().NotContain("222");      // 0xDE as a decimal byte, likewise absent
        text.Should().Contain("HasCredentials = True"); // presence only, never the values
        text.Should().Contain("WillPayloadBytes = 4");  // length only, never the bytes
        text.Should().Contain("ClientId = edge-01");
    }

    [Fact]
    public void ToString_WithoutCredentials_ReportsHasCredentialsFalse()
    {
        var request = SparkplugMqttConnectRequest.Create(
            BrokerEndpoint.Create("broker.example", 1883, tls: false),
            clientId: "edge-01", username: null, password: null, keepAliveSeconds: 30,
            cleanSession: true, willTopic: "spBv1.0/PlantA/NDEATH/gw-1", willPayload: WillBytes);

        request.ToString().Should().Contain("HasCredentials = False");
    }

    [Fact]
    public void Create_DefensivelyCopiesWillPayload_CallerMutationDoesNotLeak()
    {
        var caller = new byte[] { 1, 2, 3, 4 };
        var request = Create(caller);

        caller[0] = 0xFF; // mutate the caller's buffer AFTER construction

        request.WillPayload.ToArray().Should().Equal(1, 2, 3, 4); // request retains its own copy
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Create_KeepAliveOutOfRange_Throws(int keepAlive)
    {
        var act = () => SparkplugMqttConnectRequest.Create(
            BrokerEndpoint.Create("h", 1883, tls: false), "edge-01", null, null, keepAlive,
            cleanSession: true, "spBv1.0/PlantA/NDEATH/gw-1", WillBytes);

        act.Should().Throw<ArgumentException>();
    }
}
