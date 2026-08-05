// ============================================================================
// File: StackCeilingHarness.cs
// Purpose: Shared connect/subscribe/measure logic for the 4 stack-ceiling
//          phases. Connects to an OPC UA Sample Server, creates the
//          required number of subscriptions (1,000 items each), subscribes
//          monitored items, measures sustained throughput + notification
//          queue depth, returns a structured result.
//
// LOCKED design rules:
//   * All subscriptions apply the 12 tuning knobs from LockedTuningKnobs
//   * SequentialPublishing = true (required for §19.6 ordering)
//   * Endpoint security profile: SignAndEncrypt + Basic256Sha256 + Anonymous
//     per v2.1 §6 Q7 engineering baseline
//   * Notifications counted on a per-Subscription FastDataChangeCallback
//     that does nothing but Interlocked.Add (channel-based dispatch
//     proper lives in the production OpcUaClient adapter, not here —
//     this benchmark measures STACK ceiling, not adapter ceiling)
//
// WORK ITEMS for week-1 host runs (intentionally NOT implemented yet —
// these need real-host context):
//   * Industrial-COV value-change distribution generator (workload-profiles
//     §1 Rule 1) — currently relies on the Sample Server's natural change
//     rate
//   * Heterogeneous payload-mix verification (§1 Rule 2) — currently
//     uses whatever node types the Sample Server publishes
//   * Notification-queue-depth reflection wrapper around m_messageCache
//     (v2.1 §2.6) — currently a TODO; the gate evaluation hard-fails if
//     this isn't wired by the host run
//   * 7-gate sustainability evaluation (workload-profiles §2) — currently
//     emits raw throughput only; gate evaluation is a CompileResults step
// ============================================================================

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace ElpisEdgeConnect.Benchmarks.StackCeiling;

/// <summary>
/// Shared connect / subscribe / measure harness for the 4 stack-ceiling
/// phases. Constructed once per benchmark phase. Public because the
/// nested <see cref="PhaseResult"/> appears as a return type on the
/// public <see cref="StackCeilingBenchmarks"/> methods.
/// </summary>
public sealed class StackCeilingHarness
{
    private readonly string _endpoint;
    private readonly int _monitoredItemCount;
    private readonly System.TimeSpan _duration;

    public StackCeilingHarness(string endpoint, int monitoredItemCount, System.TimeSpan duration)
    {
        _endpoint = endpoint;
        _monitoredItemCount = monitoredItemCount;
        _duration = duration;
    }

    /// <summary>Per-phase measurement result.</summary>
    public sealed record PhaseResult
    {
        public required int MonitoredItemCount { get; init; }
        public required int SubscriptionCount { get; init; }
        public required long NotificationCount { get; init; }
        public required System.TimeSpan Elapsed { get; init; }
        public double NotificationsPerSecond => NotificationCount / Elapsed.TotalSeconds;
        public required int Gen0CollectionsDuringRun { get; init; }
        public required int Gen2CollectionsDuringRun { get; init; }
    }

    /// <summary>
    /// Connect, subscribe, run for <see cref="_duration"/>, then disconnect.
    /// Returns the measured throughput + GC stats. Notification-queue depth
    /// is left as a TODO until the host-side reflection wrapper lands.
    /// </summary>
    public async Task<PhaseResult> RunAsync(CancellationToken ct)
    {
        var appConfig = await BuildApplicationConfigurationAsync().ConfigureAwait(false);
        var session = await ConnectAsync(appConfig).ConfigureAwait(false);
        try
        {
            var subscriptionCount = ComputeSubscriptionCount(_monitoredItemCount);
            // Pre-commit gate: MinPublishRequestCount must be ≥ subs+2 per
            // §2.5 (Issue #564). Surface the value we'll request to
            // diagnostics so the host run captures it in the baseline doc.
            session.MinPublishRequestCount = LockedTuningKnobs.MinPublishRequestCount(subscriptionCount);

            long notificationCount = 0;

            var subscriptions = CreateSubscriptions(session, subscriptionCount, count => Interlocked.Add(ref notificationCount, count));
            foreach (var sub in subscriptions)
            {
                session.AddSubscription(sub);
                sub.Create();
            }

            // Add monitored items across subscriptions, MaxItemsPerSubscription
            // per subscription. v2.1 §2.5 codifies the 1000-items-per-sub
            // ceiling; LockedTuningKnobs.MaxItemsPerSubscription is the lock.
            AddMonitoredItems(subscriptions, _monitoredItemCount);
            foreach (var sub in subscriptions)
            {
                sub.ApplyChanges();
            }

            // Capture GC baseline.
            var gen0BeforeRun = System.GC.CollectionCount(0);
            var gen2BeforeRun = System.GC.CollectionCount(2);
            var sw = Stopwatch.StartNew();

            try
            {
                await Task.Delay(_duration, ct).ConfigureAwait(false);
            }
            catch (System.OperationCanceledException)
            {
                // Cancelled by user — emit partial results.
            }

            sw.Stop();
            var gen0AfterRun = System.GC.CollectionCount(0);
            var gen2AfterRun = System.GC.CollectionCount(2);

            return new PhaseResult
            {
                MonitoredItemCount = _monitoredItemCount,
                SubscriptionCount = subscriptionCount,
                NotificationCount = Interlocked.Read(ref notificationCount),
                Elapsed = sw.Elapsed,
                Gen0CollectionsDuringRun = gen0AfterRun - gen0BeforeRun,
                Gen2CollectionsDuringRun = gen2AfterRun - gen2BeforeRun,
            };
        }
        finally
        {
            try
            {
                session.Close();
            }
            catch
            {
                // Best-effort cleanup; benchmark phase is over.
            }
            session.Dispose();
        }
    }

    private static int ComputeSubscriptionCount(int monitoredItemCount)
    {
        var perSub = LockedTuningKnobs.MaxItemsPerSubscription;
        return (monitoredItemCount + perSub - 1) / perSub;
    }

    private static Subscription[] CreateSubscriptions(
        Session session,
        int subscriptionCount,
        System.Action<int> onNotificationsReceived)
    {
        var subscriptions = new Subscription[subscriptionCount];
        for (var i = 0; i < subscriptionCount; i++)
        {
            var sub = new Subscription(session.DefaultSubscription)
            {
                PublishingInterval = LockedTuningKnobs.PublishingIntervalMs,
                KeepAliveCount = LockedTuningKnobs.KeepAliveCount,
                LifetimeCount = LockedTuningKnobs.LifetimeCount,
                MaxNotificationsPerPublish = LockedTuningKnobs.MaxNotificationsPerPublish,
                Priority = 0,
                PublishingEnabled = true,
                // §1.3 architectural lock: callback enqueues only and
                // returns. Real OpcUaClient adapter uses a bounded channel
                // here; this throwaway benchmark just counts notifications.
                FastDataChangeCallback = (Subscription _, DataChangeNotification n, System.Collections.Generic.IList<string> _) =>
                    onNotificationsReceived(n.MonitoredItems.Count),
            };
            // SequentialPublishing is a property on Subscription set
            // separately because stack version variance has bitten on this
            // before — keep it explicit.
            sub.SequentialPublishing = LockedTuningKnobs.SequentialPublishing;
            subscriptions[i] = sub;
        }
        return subscriptions;
    }

    private void AddMonitoredItems(Subscription[] subscriptions, int totalCount)
    {
        // For the throwaway benchmark we subscribe the Server's standard
        // ServerStatus node N times. Real workload generation per
        // workload-profiles.md §1 Rule 1/2 is week-1 host-side work.
        // ServerStatus.CurrentTime is a DateTime that changes every publish
        // — adequate for stack-ceiling measurement but NOT for the
        // industrial-mix payload distribution gate.
        var remaining = totalCount;
        var subIndex = 0;
        while (remaining > 0 && subIndex < subscriptions.Length)
        {
            var batchSize = System.Math.Min(remaining, LockedTuningKnobs.MaxItemsPerSubscription);
            var items = new System.Collections.Generic.List<MonitoredItem>(batchSize);
            for (var i = 0; i < batchSize; i++)
            {
                items.Add(new MonitoredItem(subscriptions[subIndex].DefaultItem)
                {
                    StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                    AttributeId = Attributes.Value,
                    SamplingInterval = LockedTuningKnobs.SamplingIntervalMs,
                    QueueSize = LockedTuningKnobs.AnalogQueueSize,
                    DiscardOldest = LockedTuningKnobs.DiscardOldest,
                });
            }
            subscriptions[subIndex].AddItems(items);
            remaining -= batchSize;
            subIndex++;
        }
    }

    private async Task<Session> ConnectAsync(ApplicationConfiguration appConfig)
    {
        // Discovery + pick most-secure compatible endpoint
        var endpointDescription = CoreClientUtils.SelectEndpoint(appConfig, _endpoint, useSecurity: true);
        var endpointConfiguration = EndpointConfiguration.Create(appConfig);
        var endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfiguration);

        // Anonymous identity per v2.1 §6 Q7 engineering baseline.
        var session = await Session.Create(
            configuration: appConfig,
            endpoint: endpoint,
            updateBeforeConnect: false,
            checkDomain: false,
            sessionName: "EdgeConnect.StackCeiling",
            sessionTimeout: LockedTuningKnobs.SessionTimeoutMs,
            identity: new UserIdentity(new AnonymousIdentityToken()),
            preferredLocales: null).ConfigureAwait(false);

        session.DeleteSubscriptionsOnClose = LockedTuningKnobs.DeleteSubscriptionsOnClose;
        session.KeepAliveInterval = LockedTuningKnobs.SessionKeepAliveIntervalMs;

        return session;
    }

    private async Task<ApplicationConfiguration> BuildApplicationConfigurationAsync()
    {
        var appConfig = new ApplicationConfiguration
        {
            ApplicationName = "EdgeConnect.StackCeiling",
            ApplicationUri = "urn:elpis:edgeconnect:benchmarks:stackceiling",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = "Directory",
                    StorePath = "%LocalApplicationData%/EdgeConnect/StackCeiling/own",
                    SubjectName = "EdgeConnect.StackCeiling",
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = "Directory",
                    StorePath = "%LocalApplicationData%/EdgeConnect/StackCeiling/trusted",
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = "Directory",
                    StorePath = "%LocalApplicationData%/EdgeConnect/StackCeiling/issuers",
                },
                RejectedCertificateStore = new CertificateTrustList
                {
                    StoreType = "Directory",
                    StorePath = "%LocalApplicationData%/EdgeConnect/StackCeiling/rejected",
                },
                // Lab auto-trust per v2.1 §6 Q7. Production builds use
                // explicit trust-store. This is a THROWAWAY benchmark.
                AutoAcceptUntrustedCertificates = true,
                RejectSHA1SignedCertificates = false,
                MinimumCertificateKeySize = 2048,
            },
            TransportConfigurations = new TransportConfigurationCollection(),
            TransportQuotas = new TransportQuotas { OperationTimeout = 30_000 },
            ClientConfiguration = new ClientConfiguration
            {
                DefaultSessionTimeout = (int)LockedTuningKnobs.SessionTimeoutMs,
                MinSubscriptionLifetime = 10_000,
            },
            TraceConfiguration = new TraceConfiguration { TraceMasks = 0 },
        };
        await appConfig.Validate(ApplicationType.Client).ConfigureAwait(false);

        // Ensure application certificate exists (auto-creates a self-signed
        // cert in the configured store on first run).
        var appInstance = new ApplicationInstance(appConfig);
        var hasAppCertificate = await appInstance.CheckApplicationInstanceCertificates(silent: false).ConfigureAwait(false);
        if (!hasAppCertificate)
        {
            throw new System.InvalidOperationException(
                "Stack-ceiling harness could not establish an application instance certificate. "
                + "Check StorePath permissions or pre-provision a cert at the configured StorePath.");
        }
        return appConfig;
    }
}
