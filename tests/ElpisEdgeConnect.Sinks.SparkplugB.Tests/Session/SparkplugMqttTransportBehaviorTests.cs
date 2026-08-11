// ============================================================================
// File: Session/SparkplugMqttTransportBehaviorTests.cs
// Purpose: Locks the concrete transport's STATEFUL wrapper semantics (slice-4 review
//          r2 R3) with a controlled IMqttClient double — no broker, no socket. Proves
//          what the pure factories cannot: suspect ABORT retires the client WITHOUT a
//          clean DISCONNECT and suppresses the actor-facing callback; graceful
//          DisconnectAsync issues exactly one clean DISCONNECT and suppresses its
//          callback; a genuine broker drop surfaces exactly once carrying the attempt's
//          captured generation; suppression resets across a fresh client; and framework
//          CONNECT/SUBSCRIBE exceptions are normalized to typed SPARKPLUG.* errors while
//          cancellation stays cancellation.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Sinks.SparkplugB;
using ElpisEdgeConnect.Sinks.SparkplugB.Session;
using FluentAssertions;
using MQTTnet;
using MQTTnet.Client;
using Xunit;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Session;

public sealed class SparkplugMqttTransportBehaviorTests
{
    private const string NcmdTopic = "spBv1.0/PlantA/NCMD/gw-1";
    private static readonly byte[] WillBytes = { 0x01, 0x02 };

    private static SparkplugMqttConnectRequest Request() =>
        SparkplugMqttConnectRequest.Create(
            BrokerEndpoint.Create("broker.example", 1883, tls: false), "edge-01", null, null, 30,
            cleanSession: true, "spBv1.0/PlantA/NDEATH/gw-1", WillBytes);

    private static async Task<(SparkplugMqttTransport Transport, FakeMqttClient Client)> Connected(long generation = 1)
    {
        var client = new FakeMqttClient();
        var transport = new SparkplugMqttTransport(() => client);
        await transport.ConnectAsync(Request(), generation, CancellationToken.None);
        return (transport, client);
    }

    // ==== 1. Suspect retirement (ABORT) ====

    [Fact]
    public async Task Dispose_AbortsWithoutCleanDisconnect_AndSuppressesCallback()
    {
        var (transport, client) = await Connected();
        var raised = 0;
        transport.Disconnected += _ => { raised++; return Task.CompletedTask; };

        await transport.DisposeAsync();

        client.DisposeCalls.Should().Be(1);      // client disposed
        client.DisconnectCalls.Should().Be(0);   // NO clean DISCONNECT — broker publishes the Will (NDEATH)
        await client.RaiseDisconnectedAsync();   // the retirement's own disconnected callback
        raised.Should().Be(0);                   // suppressed (actor-requested retirement)
    }

    // ==== 2. Graceful disconnect ====

    [Fact]
    public async Task DisconnectAsync_IssuesOneCleanDisconnect_SuppressesCallback_NoSecondOnDispose()
    {
        var (transport, client) = await Connected();
        var raised = 0;
        transport.Disconnected += _ => { raised++; return Task.CompletedTask; };

        await transport.DisconnectAsync(CancellationToken.None);
        client.DisconnectCalls.Should().Be(1);   // clean DISCONNECT (broker discards the Will)
        await client.RaiseDisconnectedAsync();
        raised.Should().Be(0);                   // intentional — suppressed

        await transport.DisposeAsync();
        client.DisconnectCalls.Should().Be(1);   // no second clean disconnect on dispose
    }

    // ==== 3. Genuine broker loss ====

    [Fact]
    public async Task GenuineDisconnect_SurfacesOnce_CarryingCapturedGeneration()
    {
        var client = new FakeMqttClient();
        var transport = new SparkplugMqttTransport(() => client);
        long? got = null;
        var raised = 0;
        transport.Disconnected += g => { got = g; raised++; return Task.CompletedTask; };
        await transport.ConnectAsync(Request(), 5, CancellationToken.None);

        await client.RaiseDisconnectedAsync();   // an UNsuppressed genuine drop

        raised.Should().Be(1);
        got.Should().Be(5);                      // carries the attempt's captured generation
    }

    // ==== 4. Fresh-client reset ====

    [Fact]
    public async Task NewClient_ResetsSuppression_AndDelayedRetiredCallbackKeepsItsGeneration()
    {
        var clientA = new FakeMqttClient();
        var clientB = new FakeMqttClient();
        var seq = 0;
        var transport = new SparkplugMqttTransport(() => seq++ == 0 ? clientA : (IMqttClient)clientB);
        var events = new List<long>();
        transport.Disconnected += g => { lock (events) { events.Add(g); } return Task.CompletedTask; };

        await transport.ConnectAsync(Request(), 1, CancellationToken.None); // client A, gen 1
        await transport.ConnectAsync(Request(), 2, CancellationToken.None); // retires A, client B, gen 2

        await clientB.RaiseDisconnectedAsync();  // genuine drop on the live client
        await clientA.RaiseDisconnectedAsync();  // delayed callback from the retired client

        events.Should().Contain(2); // live client's drop is NOT accidentally suppressed
        events.Should().Contain(1); // the retired client's delayed callback retains its old generation
        clientA.DisposeCalls.Should().Be(1); // A was retired when B connected
    }

    // ==== 5. Exception normalization ====

    [Fact]
    public async Task ConnectAsync_FrameworkException_NormalizedToTransportConnectFailed()
    {
        var client = new FakeMqttClient { ConnectThrow = new InvalidOperationException("socket boom") };
        var transport = new SparkplugMqttTransport(() => client);

        await transport.Invoking(t => t.ConnectAsync(Request(), 1, CancellationToken.None))
            .Should().ThrowAsync<AdapterException>()
            .Where(e => e.Error.Code == SparkplugErrors.TransportConnectFailed);
    }

    [Fact]
    public async Task ConnectAsync_Cancellation_StaysCancellation_NotWrapped()
    {
        var client = new FakeMqttClient { ConnectThrow = new OperationCanceledException() };
        var transport = new SparkplugMqttTransport(() => client);

        await transport.Invoking(t => t.ConnectAsync(Request(), 1, CancellationToken.None))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SubscribeExactAsync_FrameworkException_NormalizedToTransportSubscribeFailed()
    {
        var client = new FakeMqttClient { SubscribeThrow = new InvalidOperationException("subscribe boom") };
        var transport = new SparkplugMqttTransport(() => client);
        await transport.ConnectAsync(Request(), 1, CancellationToken.None);

        await transport.Invoking(t => t.SubscribeExactAsync(NcmdTopic, CancellationToken.None))
            .Should().ThrowAsync<AdapterException>()
            .Where(e => e.Error.Code == SparkplugErrors.TransportSubscribeFailed);
    }

    [Fact]
    public async Task SubscribeExactAsync_Cancellation_StaysCancellation_NotWrapped()
    {
        var client = new FakeMqttClient { SubscribeThrow = new OperationCanceledException() };
        var transport = new SparkplugMqttTransport(() => client);
        await transport.ConnectAsync(Request(), 1, CancellationToken.None);

        await transport.Invoking(t => t.SubscribeExactAsync(NcmdTopic, CancellationToken.None))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    // ---- The controlled IMqttClient double (no broker/socket) ----
    private sealed class FakeMqttClient : IMqttClient
    {
        public int ConnectCalls { get; private set; }
        public int DisconnectCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public Exception? ConnectThrow { get; init; }
        public Exception? SubscribeThrow { get; init; }

        public bool IsConnected { get; private set; }
        public MqttClientOptions Options => null!;

        public event Func<MqttClientDisconnectedEventArgs, Task>? DisconnectedAsync;

        /// <summary>Invoke the actor-wired disconnected handler (a genuine or retirement drop).</summary>
        public Task RaiseDisconnectedAsync() =>
            DisconnectedAsync?.Invoke(new MqttClientDisconnectedEventArgs(
                clientWasConnected: true, connectResult: null!,
                reason: MqttClientDisconnectReason.NormalDisconnection,
                reasonString: null!, userProperties: null!, exception: null!)) ?? Task.CompletedTask;

        public Task<MqttClientConnectResult> ConnectAsync(MqttClientOptions options, CancellationToken cancellationToken)
        {
            ConnectCalls++;
            if (ConnectThrow is not null)
            {
                return Task.FromException<MqttClientConnectResult>(ConnectThrow);
            }

            IsConnected = true;
            return Task.FromResult(new MqttClientConnectResult()); // ResultCode defaults to Success
        }

        public Task<MqttClientSubscribeResult> SubscribeAsync(
            MqttClientSubscribeOptions options, CancellationToken cancellationToken)
        {
            if (SubscribeThrow is not null)
            {
                return Task.FromException<MqttClientSubscribeResult>(SubscribeThrow);
            }

            throw new NotSupportedException("The behavior tests never exercise a successful SUBACK on the double.");
        }

        public Task DisconnectAsync(MqttClientDisconnectOptions options, CancellationToken cancellationToken)
        {
            DisconnectCalls++;
            IsConnected = false;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            DisposeCalls++;
            IsConnected = false;
        }

        // ---- Unused IMqttClient surface (never invoked by the transport) ----
        event Func<MqttApplicationMessageReceivedEventArgs, Task> IMqttClient.ApplicationMessageReceivedAsync
        {
            add { } remove { }
        }

        event Func<MqttClientConnectedEventArgs, Task> IMqttClient.ConnectedAsync { add { } remove { } }

        event Func<MqttClientConnectingEventArgs, Task> IMqttClient.ConnectingAsync { add { } remove { } }

        event Func<MQTTnet.Diagnostics.InspectMqttPacketEventArgs, Task> IMqttClient.InspectPacketAsync
        {
            add { } remove { }
        }

        public Task PingAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MqttClientPublishResult> PublishAsync(
            MqttApplicationMessage applicationMessage, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SendExtendedAuthenticationExchangeDataAsync(
            MqttExtendedAuthenticationExchangeData data, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MqttClientUnsubscribeResult> UnsubscribeAsync(
            MqttClientUnsubscribeOptions options, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
