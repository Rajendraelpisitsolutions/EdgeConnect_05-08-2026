// ============================================================================
// File: Diagnostics/TapStreamWriter.cs
// Purpose: The Live Data Tap SSE stream loop (ADR-0018), extracted from the
//          endpoint so it is deterministically testable. Subscribes the route
//          on entry (activating demand-driven capture), streams new captures +
//          rolling status as Server-Sent Events until the caller's token
//          cancels (client disconnect), then unsubscribes — which starts the
//          cooldown and eventually releases the capture rings.
// Reference: ADR-0018 (Live Data Tap), ADR-0017 (demand-driven activation).
// ============================================================================

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Management.Contracts;

namespace ElpisEdgeConnect.Management.Diagnostics;

/// <summary>Streams a route's tap captures as SSE. See file header.</summary>
public static class TapStreamWriter
{
    /// <summary>SSE event name for a captured point.</summary>
    public const string CaptureEvent = "capture";

    /// <summary>SSE event name for a rolling status frame.</summary>
    public const string StatusEvent = "status";

    /// <summary>
    /// Drive the SSE stream for <paramref name="routeId"/>. Subscribes on
    /// entry; on every poll it emits each new capture (cursor-advanced) and a
    /// status frame; exits when <paramref name="ct"/> cancels, unsubscribing in
    /// a finally so the route always deactivates after the cooldown.
    /// </summary>
    /// <param name="tap">The capture service.</param>
    /// <param name="routeId">Route to stream.</param>
    /// <param name="writeEvent">
    /// Writes one fully-formed SSE block (<c>event: ...\ndata: ...\n\n</c>) and
    /// flushes. Injected so the endpoint writes to the HTTP response and tests
    /// collect blocks.
    /// </param>
    /// <param name="pollInterval">Delay between capture polls.</param>
    /// <param name="json">Serializer options for the DTO payloads.</param>
    /// <param name="ct">Cancels on client disconnect.</param>
    public static async Task WriteStreamAsync(
        IRouteTap tap,
        string routeId,
        Func<string, CancellationToken, Task> writeEvent,
        TimeSpan pollInterval,
        JsonSerializerOptions json,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tap);
        ArgumentException.ThrowIfNullOrEmpty(routeId);
        ArgumentNullException.ThrowIfNull(writeEvent);

        using var subscription = tap.Subscribe(routeId);
        long cursor = 0;

        // Emit an immediate status so the client renders "Capturing" at once.
        await writeEvent(Block(StatusEvent, TapStatusDto.From(tap.GetStatus(routeId)), json), ct)
            .ConfigureAwait(false);

        while (!ct.IsCancellationRequested)
        {
            var captures = tap.ReadSince(routeId, cursor);
            for (var i = 0; i < captures.Count; i++)
            {
                var c = captures[i];
                cursor = c.CaptureSequence;
                await writeEvent(Block(CaptureEvent, TapCaptureDto.From(c), json), ct).ConfigureAwait(false);
            }

            await writeEvent(Block(StatusEvent, TapStatusDto.From(tap.GetStatus(routeId)), json), ct)
                .ConfigureAwait(false);

            try
            {
                await Task.Delay(pollInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static string Block<T>(string eventName, T payload, JsonSerializerOptions json) =>
        $"event: {eventName}\ndata: {JsonSerializer.Serialize(payload, json)}\n\n";
}
