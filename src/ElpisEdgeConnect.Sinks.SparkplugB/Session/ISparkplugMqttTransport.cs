// ============================================================================
// File: Session/ISparkplugMqttTransport.cs
// Purpose: The narrow, actor-owned MQTT transport seam (moved from slice 1 to
//          slice 4 per v3.1; shape per slice-2 review B2). CONNECT / SUBSCRIBE /
//          PUBLISH / DISCONNECT only — NO automatic reconnect policy (reconnect is
//          expressed solely as a Core-driven rebirth). The ACTOR owns the monotonic
//          connection-generation token and passes it to ConnectAsync; the transport
//          echoes that generation in Disconnected so the actor can ignore a delayed
//          callback from a retired client. Publishes are QoS 0 (retain=false) per the
//          Sparkplug 3.1.1 profile; the NCMD subscribe is QoS 1; the NDEATH Will
//          (carried in the connect request) is registered at QoS 1, retain=false.
// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md §1.2, §4, §9.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Session;

/// <summary>
/// The MQTT operations the Sparkplug session actor performs. Implementations MUST NOT
/// auto-reconnect: a lost/suspect connection surfaces via <see cref="Disconnected"/> and
/// is recovered only through the Core rebirth lifecycle.
/// </summary>
internal interface ISparkplugMqttTransport : IAsyncDisposable
{
    /// <summary>Whether the underlying client currently reports a live connection.</summary>
    bool IsConnected { get; }

    /// <summary>
    /// Establish a clean MQTT 3.1.1 session for the given connect attempt, tagged with the
    /// actor-supplied <paramref name="connectionGeneration"/>. The NDEATH Will in the request
    /// is registered at QoS 1, retain=false.
    /// </summary>
    /// <param name="request">The complete connect attempt (endpoint, credentials, Will).</param>
    /// <param name="connectionGeneration">The actor-owned monotonic generation for this attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when CONNECT has succeeded.</returns>
    Task ConnectAsync(SparkplugMqttConnectRequest request, long connectionGeneration, CancellationToken cancellationToken);

    /// <summary>Subscribe the exact NCMD topic at QoS 1 (no wildcards).</summary>
    /// <param name="topicFilter">The exact NCMD topic.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the SUBSCRIBE has been acknowledged.</returns>
    Task SubscribeExactAsync(string topicFilter, CancellationToken cancellationToken);

    /// <summary>
    /// Publish a payload at QoS 0, retain=false. Returns <c>true</c> only when the publish
    /// completed at the local MQTTnet transport boundary with no observable error — never a
    /// broker-receipt guarantee (plan v3 §1.7).
    /// </summary>
    /// <param name="topic">The destination topic.</param>
    /// <param name="payload">The encoded payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> on a clean local send; <c>false</c> on an observable/uncertain failure.</returns>
    Task<bool> PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);

    /// <summary>Disconnect the current client (an intentional NDEATH is published separately first).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the client has disconnected.</returns>
    Task DisconnectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Raised when the client disconnects unexpectedly. The argument is the generation of the
    /// client that dropped, so the actor can discard a stale callback from a retired client.
    /// </summary>
    event Func<long, Task>? Disconnected;

    /// <summary>
    /// Raised when the client receives an application message on the subscribed NCMD topic. The first
    /// argument is the generation of the receiving client (so the actor can discard a stale callback);
    /// the second is the raw payload bytes. The actor classifies it via <see cref="SparkplugNodeCommand"/>
    /// and never lets the callback publish or mutate protocol counters.
    /// </summary>
    event Func<long, ReadOnlyMemory<byte>, Task>? NodeCommandReceived;
}
