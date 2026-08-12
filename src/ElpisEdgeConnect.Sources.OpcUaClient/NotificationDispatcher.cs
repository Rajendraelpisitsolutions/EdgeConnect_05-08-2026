// ============================================================================
// File: NotificationDispatcher.cs
// Purpose: Production implementation of the v2.1 §1.3 channel-based
//          notification hot path. Single bounded Channel per adapter;
//          FastDataChangeCallback enqueues batches non-blockingly with
//          DropOldest fallback; worker drains the channel and translates
//          via OpcUaTypeMapper.
//
// LOCKED architectural invariants (verified by NotificationDispatcherTests):
//
//   1. OnNotification is non-blocking:
//        - At most one Channel.Writer.TryWrite + a few Interlocked ops
//        - NEVER awaits, NEVER blocks, NEVER does I/O
//        - Verified by the "callback completes in <1ms" gate per PR 4
//          amendment #2
//
//   2. DropOldest backpressure policy is HARD-CODED — NOT operator-
//      configurable. Channel.Writer.TryWrite returns false when full;
//      we Read once to drop oldest, then TryWrite again. Increments
//      DroppedDueToBackpressure counter.
//
//   3. Worker thread:
//        - Started on first OnNotification call (lazy) so the dispatcher
//          can be constructed before subscriptions are wired
//        - Drains channel via async-foreach
//        - Exits on cancellation OR channel completion
//        - try/catch around translation to avoid worker death on bad
//          DataValue — increments DroppedDueToBackpressure (per-CDP)
//
//   4. Shutdown:
//        - StopAsync writes completion sentinel + cancels worker token
//        - Awaits worker drain with the configured timeout
//        - Items left in channel after timeout → DroppedAtShutdown
//          counter
//
//   5. Received counter increments BEFORE TryWrite — captures every
//      notification the stack handed us, independent of fate
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.3
//            PR 4 plan + amendments (user lock 2026-05-29)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Model;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Production <see cref="INotificationDispatcher"/>. Single bounded
/// Channel + worker per adapter. DropOldest backpressure policy.
/// </summary>
internal sealed class NotificationDispatcher : INotificationDispatcher, IAsyncDisposable
{
    private readonly Channel<TranslatedBatch> _outputChannel;
    private readonly OpcUaTypeMapper _typeMapper;
    private readonly ILogger _logger;
    private readonly TimeSpan _shutdownDrainTimeout;

    // Counter fields — read via Interlocked.Read for snapshot consistency.
    private long _received;
    private long _dispatched;
    private long _droppedDueToBackpressure;
    private long _droppedAtShutdown;

    // Slice 0 commit 3.0 retirement: ingress-close-before-drain. The callback path
    // must STOP accepting work before a drain can count as proof. Once retiring,
    // OnNotification rejects (records, never enqueues).
    private volatile bool _ingressRetiring;
    private long _rejectedAfterRetirement;

    /// <summary>
    /// Construct a dispatcher with the channel sized per
    /// <paramref name="channelCapacity"/>. Validated by the adapter at
    /// configuration time.
    /// </summary>
    public NotificationDispatcher(
        int channelCapacity,
        OpcUaTypeMapper typeMapper,
        ILogger logger,
        TimeSpan? shutdownDrainTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(typeMapper);
        ArgumentNullException.ThrowIfNull(logger);
        if (channelCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channelCapacity), channelCapacity,
                "Channel capacity must be strictly positive (validated by the adapter's ValidateConfigAsync; this is a defence-in-depth check).");
        }

        // BoundedChannelFullMode.Wait gives us the explicit drop control —
        // when TryWrite returns false the OnNotification path decides
        // what to do (drop oldest, increment counter).
        _outputChannel = Channel.CreateBounded<TranslatedBatch>(new BoundedChannelOptions(channelCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });

        _typeMapper = typeMapper;
        _logger = logger;
        _shutdownDrainTimeout = shutdownDrainTimeout ?? TimeSpan.FromSeconds(1);
    }

    /// <inheritdoc/>
    public void OnNotification(
        Subscription subscription,
        DataChangeNotification notification,
        IList<string> stringTable)
    {
        if (notification?.MonitoredItems is null || notification.MonitoredItems.Count == 0)
        {
            return;
        }

        // 0. Retirement: ingress is closed — reject + record, never enqueue. This
        //    is the "no more callback work can be accepted" half of CallbackDrain
        //    proof (the drain half is RetireAndDrainAsync).
        if (_ingressRetiring)
        {
            Interlocked.Add(ref _rejectedAfterRetirement, notification.MonitoredItems.Count);
            return;
        }

        // 1. Translate INSIDE the callback. The alternative (queue raw
        //    notification, translate in worker) is theoretically faster
        //    but means dropping notifications on backpressure loses
        //    type-correct CDPs we'd already have paid for.
        var translated = TranslateInline(subscription, notification);
        if (translated.Count == 0)
        {
            return;
        }

        var batch = new TranslatedBatch(translated);

        // 2. Received counter increments BEFORE write — captures every
        //    notification independent of fate (PR 4 amendment #4).
        Interlocked.Add(ref _received, translated.Count);

        // 3. Fast path — channel has room.
        if (_outputChannel.Writer.TryWrite(batch))
        {
            return;
        }

        // 4. DropOldest — drain one batch (the oldest in the channel),
        //    then retry. If the second TryWrite ALSO fails, we drop the
        //    incoming batch entirely.
        if (_outputChannel.Reader.TryRead(out var droppedOldest))
        {
            Interlocked.Add(ref _droppedDueToBackpressure, droppedOldest.Cdps.Count);
            if (_outputChannel.Writer.TryWrite(batch))
            {
                return;
            }
        }

        // 5. Last resort — drop the new batch entirely. This can happen
        //    if a concurrent consumer drained the slot we freed; rare
        //    but non-zero under sustained backpressure.
        Interlocked.Add(ref _droppedDueToBackpressure, translated.Count);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<CanonicalDataPoint> ConsumeAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var batch in _outputChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            foreach (var cdp in batch.Cdps)
            {
                Interlocked.Increment(ref _dispatched);
                yield return cdp;
            }
        }
    }

    /// <inheritdoc/>
    public NotificationDispatcherCounters GetCounters() => new()
    {
        Received = Interlocked.Read(ref _received),
        Dispatched = Interlocked.Read(ref _dispatched),
        DroppedDueToBackpressure = Interlocked.Read(ref _droppedDueToBackpressure),
        DroppedAtShutdown = Interlocked.Read(ref _droppedAtShutdown),
    };

    /// <summary>
    /// Complete the channel writer and await drain-or-timeout. Items
    /// left in the channel after timeout are counted as
    /// DroppedAtShutdown. Idempotent — second call is a no-op.
    /// </summary>
    public async Task StopAsync(CancellationToken ct)
    {
        // 1. Signal "no more writes" — ConsumeAsync will exit once the
        //    channel drains.
        _outputChannel.Writer.TryComplete();

        // 2. Wait up to the drain timeout for the consumer to finish.
        //    Without this we can't tell whether shutdown-time drops are
        //    "consumer never started" or "consumer slow."
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_shutdownDrainTimeout);
        try
        {
            await _outputChannel.Reader.Completion.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Drain timeout expired — count whatever's left.
            while (_outputChannel.Reader.TryRead(out var leftover))
            {
                Interlocked.Add(ref _droppedAtShutdown, leftover.Cdps.Count);
            }
        }
    }

    /// <summary>
    /// Slice 0 retirement: close the callback ingress. After this,
    /// <see cref="OnNotification"/> rejects (records, never enqueues). MUST be
    /// called BEFORE <see cref="RetireAndDrainAsync"/> so that drain is proof.
    /// </summary>
    public void BeginRetiringIngress() => _ingressRetiring = true;

    /// <summary>
    /// Slice 0 retirement drain proof: complete the channel and await drain, then
    /// return a STRUCTURED result distinguishing fully-drained from
    /// timeout/dropped. Only <see cref="CallbackDrainResult.FullyDrained"/> is
    /// <c>CallbackDrain = Proven</c>; a drain timeout (accepted work undrained) is
    /// terminal Unproven. Callbacks rejected after ingress closed are recorded but
    /// do not block proof (they were explicitly rejected under the retirement path).
    /// Call <see cref="BeginRetiringIngress"/> first.
    /// </summary>
    public async Task<CallbackDrainResult> RetireAndDrainAsync(CancellationToken ct)
    {
        _outputChannel.Writer.TryComplete();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_shutdownDrainTimeout);
        try
        {
            await _outputChannel.Reader.Completion.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            return new CallbackDrainResult
            {
                FullyDrained = true,
                DroppedAtShutdown = 0,
                RejectedAfterRetirement = Interlocked.Read(ref _rejectedAfterRetirement),
            };
        }
        catch (OperationCanceledException)
        {
            long dropped = 0;
            while (_outputChannel.Reader.TryRead(out var leftover))
            {
                dropped += leftover.Cdps.Count;
                Interlocked.Add(ref _droppedAtShutdown, leftover.Cdps.Count);
            }
            return new CallbackDrainResult
            {
                FullyDrained = false,
                DroppedAtShutdown = dropped,
                RejectedAfterRetirement = Interlocked.Read(ref _rejectedAfterRetirement),
            };
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(_shutdownDrainTimeout);
            await StopAsync(cts.Token).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    /// <summary>
    /// Translate every monitored-item value in the notification into a
    /// canonical data point. Runs inline in the callback path; per-CDP
    /// translation errors are logged + counted but don't block other
    /// items.
    /// </summary>
    private List<CanonicalDataPoint> TranslateInline(Subscription subscription, DataChangeNotification notification)
    {
        var result = new List<CanonicalDataPoint>(notification.MonitoredItems.Count);
        foreach (var monitoredItemNotification in notification.MonitoredItems)
        {
            try
            {
                if (monitoredItemNotification?.Value is null)
                {
                    continue;
                }
                var item = subscription.FindItemByClientHandle(monitoredItemNotification.ClientHandle);
                var tagName = item?.DisplayName ?? string.Empty;
                if (string.IsNullOrWhiteSpace(tagName))
                {
                    continue;
                }
                var cdp = _typeMapper.Translate(monitoredItemNotification.Value, tagName);
                result.Add(cdp);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "OPC UA notification translation failed for ClientHandle {ClientHandle}; skipping.",
                    monitoredItemNotification?.ClientHandle);
            }
        }
        return result;
    }

    /// <summary>
    /// Channel payload — wraps a translated CDP batch so the channel
    /// type stays uniform. Internal because it's an implementation
    /// detail of this dispatcher.
    /// </summary>
    private readonly record struct TranslatedBatch(IReadOnlyList<CanonicalDataPoint> Cdps);
}
