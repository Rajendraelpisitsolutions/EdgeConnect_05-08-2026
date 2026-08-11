// ============================================================================
// File: MqttRawSmokeTest.cs
// Purpose: Verify MQTTnet client can connect to the local Mosquitto broker.
//          Requires: mosquitto running on localhost:1883 with anonymous access.
// ============================================================================

using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using Xunit;

namespace ElpisEdgeConnect.Sinks.Mqtt.Tests;

/// <summary>
/// Tagged <c>RequiresMqttBroker</c> so CI environments without Mosquitto
/// can exclude it via <c>--filter "Category!=RequiresMqttBroker"</c>. Without
/// the tag, MQTTnet's connect path hangs indefinitely when the broker is
/// unreachable (observed via the Apr 2026 Windows-Arm dev-box surface).
/// </summary>
[Trait("Category", "RequiresMqttBroker")]
public sealed class MqttRawSmokeTest
{
    [Fact]
    public async Task RawMqttnet_ClientConnectsToLocalMosquitto()
    {
        using var client = new MqttFactory().CreateMqttClient();
        var opts = new MqttClientOptionsBuilder()
            .WithTcpServer("127.0.0.1", 1883)
            .WithClientId("edgeconnect-smoke-test")
            .Build();

        await client.ConnectAsync(opts, CancellationToken.None);
        Assert.True(client.IsConnected);

        await client.DisconnectAsync();
    }
}
