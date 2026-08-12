// ============================================================================
// File: OpcUaServerEndToEndTests.cs
// Purpose: Milestone H.3 end-to-end test that exercises a real OPC UA
//          Client (OPCFoundation.NetStandard.Opc.Ua.Client) against a
//          running OpcUaServerSinkAdapter.
//
//          Three scenarios:
//            1. Connect + browse — the client connects to the endpoint
//               and discovers the EdgeConnect namespace under
//               Objects/<RootFolder>.
//            2. Subscribe + receive updates — the client subscribes to
//               a NodeId; PublishAsync emits a CanonicalDataPoint with
//               Quality=Good; the client receives a notification
//               whose StatusCode is Good and Value matches the
//               payload.
//            3. Quality propagation — PublishAsync emits a point with
//               Quality=Bad; the client observes StatusCode.Bad on the
//               subscribed item without re-subscribing.
//
//          The test wires the adapter directly (no HostHarness, no
//          routing engine) so the timing is deterministic: we control
//          exactly when PublishAsync fires and we read the client's
//          notification queue with a polling wait.
//
//          Each test picks an ephemeral TCP port to avoid collisions
//          when integration tests run in parallel.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone H.3
//            shared-knowledge/contracts/opcua-namespace-policy.md
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sinks.OpcUaServer;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Xunit;

namespace ElpisEdgeConnect.Integration.Tests;

[Trait("Category", "OpcUaServer")]
public sealed class OpcUaServerEndToEndTests : IAsyncLifetime
{
    private const string TestGatewayId = "gw-h3-e2e";
    private const string TestSourceInstanceId = "source-h3";
    private const string TestApplicationUri = "urn:elpis:edgeconnect:test:h3";

    private string _pkiRoot = "";
    private int _port;

    public Task InitializeAsync()
    {
        _pkiRoot = Path.Combine(Path.GetTempPath(), $"opcua-h3-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_pkiRoot);
        _port = GetEphemeralTcpPort();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_pkiRoot))
            {
                Directory.Delete(_pkiRoot, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup
        }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Client_CanConnectAndBrowse_DefaultNamespace()
    {
        await using var fixture = await OpcUaServerFixture.StartAsync(_port, _pkiRoot);
        await using var client = await OpcUaTestClient.ConnectAsync(fixture.EndpointUrl, _pkiRoot);

        // Walk Objects -> EdgeConnect — proves the namespace registration
        // succeeded and our root folder hangs off the standard Objects
        // folder per the namespace policy contract.
        var rootReferences = client.Browse(ObjectIds.ObjectsFolder);
        rootReferences.Should().Contain(r => r.BrowseName.Name == "EdgeConnect",
            "Objects/EdgeConnect is the locked root folder for ns=urn:elpis:edgeconnect:v1");

        // Confirm the server's NamespaceUri lock from the namespace policy:
        // urn:elpis:edgeconnect:v1 must exist in the server's namespace table.
        var namespaceUris = client.Session.NamespaceUris.ToArray();
        namespaceUris.Should().Contain("urn:elpis:edgeconnect:v1");
    }

    [Fact]
    public async Task Client_SubscribesAndReceives_UpdateWithGoodQuality()
    {
        await using var fixture = await OpcUaServerFixture.StartAsync(_port, _pkiRoot);

        // Pre-create the node so the client can subscribe against a
        // resolved NodeId. We push the value through PublishAsync,
        // which creates the node and updates the value+status.
        const string StableTagId = "spindle_rpm";
        const string MetadataDeviceClass = "cnc";
        var nodeIdString = $"{TestGatewayId}/{TestSourceInstanceId}/{StableTagId}";
        var fullNodeId = $"ns=2;s={nodeIdString}";

        await fixture.PublishAsync(MakePoint(StableTagId, 2500.0, DataQuality.Good, MetadataDeviceClass));

        await using var client = await OpcUaTestClient.ConnectAsync(fixture.EndpointUrl, _pkiRoot);

        // Translate the namespace URI to the negotiated client-side index;
        // the namespace policy locks the URI, not the per-session index.
        var nsIndex = (ushort)client.Session.NamespaceUris.GetIndex("urn:elpis:edgeconnect:v1");
        nsIndex.Should().NotBe(0, "the EdgeConnect namespace must be registered on the negotiated session");

        var clientNodeId = new NodeId(nodeIdString, nsIndex);
        var notifications = new ConcurrentQueue<MonitoredItemNotification>();
        client.Subscribe(clientNodeId, mi =>
        {
            foreach (var v in mi.DequeueValues())
            {
                notifications.Enqueue(new MonitoredItemNotification
                {
                    Value = v.Value,
                    StatusCode = v.StatusCode.Code,
                });
            }
        });

        // Drive an update through PublishAsync to trigger a notification.
        // Note: OPC UA delivers the current cached value as the first
        // notification on subscribe (the 2500.0 seed), then the new
        // value when it changes. We assert on the *updated* notification
        // (Value=2700.0) rather than just the first one.
        await fixture.PublishAsync(MakePoint(StableTagId, 2700.0, DataQuality.Good, MetadataDeviceClass));

        var updated = await WaitForMatchAsync(
            notifications,
            n => n.Value is double d && Math.Abs(d - 2700.0) < 0.0001,
            TimeSpan.FromSeconds(5));

        updated.Should().NotBeNull("the updated value must reach the client within a few publishing intervals");
        updated!.StatusCode.Should().Be(StatusCodes.Good);
        updated.Value.Should().Be(2700.0);
    }

    [Fact]
    public async Task Client_ObservesBadQuality_AfterUpstreamFailure()
    {
        await using var fixture = await OpcUaServerFixture.StartAsync(_port, _pkiRoot);

        const string StableTagId = "spindle_rpm";
        const string MetadataDeviceClass = "cnc";
        var nodeIdString = $"{TestGatewayId}/{TestSourceInstanceId}/{StableTagId}";

        // Seed a Good observation so the node exists with a known value.
        await fixture.PublishAsync(MakePoint(StableTagId, 1500.0, DataQuality.Good, MetadataDeviceClass));

        await using var client = await OpcUaTestClient.ConnectAsync(fixture.EndpointUrl, _pkiRoot);

        var nsIndex = (ushort)client.Session.NamespaceUris.GetIndex("urn:elpis:edgeconnect:v1");
        var clientNodeId = new NodeId(nodeIdString, nsIndex);
        var notifications = new ConcurrentQueue<MonitoredItemNotification>();
        client.Subscribe(clientNodeId, mi =>
        {
            foreach (var v in mi.DequeueValues())
            {
                notifications.Enqueue(new MonitoredItemNotification
                {
                    Value = v.Value,
                    StatusCode = v.StatusCode.Code,
                });
            }
        });

        // Wait for the initial Good notification first so we don't race
        // its arrival against the Bad we emit below.
        await WaitForAsync(notifications, TimeSpan.FromSeconds(5));

        // Now simulate upstream source failure — adapter emits a point
        // with Quality=Bad and Value=null per the canonical model state
        // machine. Per the namespace policy contract, the OPC UA client
        // must see StatusCode=Bad and Value=null.
        await fixture.PublishAsync(MakePoint(StableTagId, value: null, DataQuality.Bad, MetadataDeviceClass));

        var bad = await WaitForMatchAsync(
            notifications,
            n => n.StatusCode == StatusCodes.Bad,
            TimeSpan.FromSeconds(5));

        bad.Should().NotBeNull("the Bad status update must reach the client within a few publishing intervals");
        bad!.StatusCode.Should().Be(StatusCodes.Bad);
        bad.Value.Should().BeNull("OPC UA spec: when StatusCode is Bad, Value MUST be null");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static int GetEphemeralTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static CanonicalDataPoint MakePoint(
        string stableTagId,
        object? value,
        DataQuality quality,
        string deviceClass)
    {
        var now = DateTime.UtcNow;
        var canonicalType = value switch
        {
            null => CanonicalValueType.Null,
            double => CanonicalValueType.Double,
            int => CanonicalValueType.Integer,
            string => CanonicalValueType.String,
            _ => CanonicalValueType.Double,
        };
        return new CanonicalDataPoint
        {
            GatewayId = TestGatewayId,
            SourceInstanceId = TestSourceInstanceId,
            ProtocolName = "modbus-tcp",
            DeviceId = "lathe-h3",
            TagName = stableTagId,
            TagPath = stableTagId,
            Value = value,
            ValueType = canonicalType,
            Quality = quality,
            DeviceTimestamp = now,
            GatewayTimestamp = now,
            Metadata = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["deviceClass"] = deviceClass,
                ["stableTagId"] = stableTagId,
            }.ToFrozenDictionary(StringComparer.Ordinal),
        };
    }

    private static async Task<MonitoredItemNotification> WaitForAsync(
        ConcurrentQueue<MonitoredItemNotification> queue,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (queue.TryDequeue(out var n)) return n;
            await Task.Delay(50);
        }
        throw new TimeoutException(
            $"No OPC UA notification arrived within {timeout.TotalSeconds:F1}s.");
    }

    private static async Task<MonitoredItemNotification?> WaitForMatchAsync(
        ConcurrentQueue<MonitoredItemNotification> queue,
        Func<MonitoredItemNotification, bool> predicate,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            while (queue.TryDequeue(out var n))
            {
                if (predicate(n)) return n;
            }
            await Task.Delay(50);
        }
        return null;
    }

    private sealed record MonitoredItemNotification
    {
        public object? Value { get; init; }
        public uint StatusCode { get; init; }
    }

    // ------------------------------------------------------------------
    // OPC UA Server fixture — wraps the adapter lifecycle.
    // ------------------------------------------------------------------

    private sealed class OpcUaServerFixture : IAsyncDisposable
    {
        private readonly OpcUaServerSinkAdapter _adapter;

        private OpcUaServerFixture(OpcUaServerSinkAdapter adapter, string endpointUrl)
        {
            _adapter = adapter;
            EndpointUrl = endpointUrl;
        }

        public string EndpointUrl { get; }

        public static async Task<OpcUaServerFixture> StartAsync(int port, string pkiRoot)
        {
            var endpointUrl = $"opc.tcp://127.0.0.1:{port}/edgeconnect";
            var adapter = new OpcUaServerSinkAdapter(
                $"opcua-h3-{Guid.NewGuid():N}",
                NullLogger<OpcUaServerSinkAdapter>.Instance);

            var config = new OpcUaServerConfiguration
            {
                InstanceId = adapter.InstanceId,
                ProtocolName = OpcUaServerConfiguration.ProtocolNameConstant,
                EndpointUrl = endpointUrl,
                ApplicationUri = TestApplicationUri,
                ApplicationName = "EdgeConnect H.3 Test Server",
                MinPublishingIntervalMs = 50,
                Security = new OpcUaSecurityConfig
                {
                    Mode = OpcUaSecurityMode.None,
                    ApplicationCertificatePath = Path.Combine(pkiRoot, "server", "own"),
                    TrustedClientsPath = Path.Combine(pkiRoot, "server", "trusted"),
                    RejectedClientsPath = Path.Combine(pkiRoot, "server", "rejected"),
                },
            };

            await adapter.InitializeAsync(config, CancellationToken.None);
            await adapter.StartAsync(CancellationToken.None);
            return new OpcUaServerFixture(adapter, endpointUrl);
        }

        public Task PublishAsync(CanonicalDataPoint point) =>
            _adapter.PublishAsync(new[] { point }, CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _adapter.StopAsync(CancellationToken.None);
            }
            catch
            {
                // best-effort
            }
            await _adapter.DisposeAsync();
        }
    }

    // ------------------------------------------------------------------
    // OPC UA Test Client — minimal connect / browse / subscribe wrapper.
    // ------------------------------------------------------------------

    private sealed class OpcUaTestClient : IAsyncDisposable
    {
        private readonly ApplicationInstance _appInstance;
        private readonly List<Subscription> _subscriptions = new();
        public ISession Session { get; }

        private OpcUaTestClient(ApplicationInstance appInstance, ISession session)
        {
            _appInstance = appInstance;
            Session = session;
        }

        public static async Task<OpcUaTestClient> ConnectAsync(string endpointUrl, string pkiRoot)
        {
            var appConfig = new ApplicationConfiguration
            {
                ApplicationName = "EdgeConnect H.3 Test Client",
                ApplicationUri = $"urn:elpis:edgeconnect:test:client:{Guid.NewGuid():N}",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "client", "own"),
                        SubjectName = "CN=EdgeConnect H.3 Test Client",
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "client", "trusted"),
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "client", "rejected"),
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "client", "issuer"),
                    },
                    AutoAcceptUntrustedCertificates = true,
                },
                TransportConfigurations = new TransportConfigurationCollection(),
                TransportQuotas = new TransportQuotas { OperationTimeout = 60_000 },
                ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60_000 },
                TraceConfiguration = new TraceConfiguration { TraceMasks = 0 },
            };
            await appConfig.Validate(ApplicationType.Client).ConfigureAwait(false);

            var appInstance = new ApplicationInstance
            {
                ApplicationName = appConfig.ApplicationName,
                ApplicationType = ApplicationType.Client,
                ApplicationConfiguration = appConfig,
            };

            // Auto-create the client application certificate; required for
            // session establishment even under SecurityPolicies.None.
            // CS0618: see the matching suppression in OpcUaServerSinkAdapter.
#pragma warning disable CS0618
            await appInstance.CheckApplicationInstanceCertificate(
                silent: true,
                minimumKeySize: 2048).ConfigureAwait(false);
#pragma warning restore CS0618

            // Use SecurityPolicies.None to match the MVP server posture.
            var endpoint = CoreClientUtils.SelectEndpoint(appConfig, endpointUrl, useSecurity: false);
            var endpointConfiguration = EndpointConfiguration.Create(appConfig);
            var configuredEndpoint = new ConfiguredEndpoint(null, endpoint, endpointConfiguration);

            // checkDomain: false — the test cert's SAN doesn't list
            // 127.0.0.1; we trust the endpoint URL out-of-band.
            var session = await Opc.Ua.Client.Session.Create(
                appConfig,
                configuredEndpoint,
                updateBeforeConnect: false,
                checkDomain: false,
                sessionName: "h3-test-client",
                sessionTimeout: 60_000,
                identity: new UserIdentity(new AnonymousIdentityToken()),
                preferredLocales: null).ConfigureAwait(false);

            return new OpcUaTestClient(appInstance, session);
        }

        public IReadOnlyList<ReferenceDescription> Browse(NodeId rootNode)
        {
            Session.Browse(
                requestHeader: null,
                view: null,
                nodeToBrowse: rootNode,
                maxResultsToReturn: 0u,
                browseDirection: BrowseDirection.Forward,
                referenceTypeId: ReferenceTypeIds.HierarchicalReferences,
                includeSubtypes: true,
                nodeClassMask: (uint)(NodeClass.Object | NodeClass.Variable),
                continuationPoint: out _,
                references: out var references);
            return references;
        }

        public void Subscribe(NodeId nodeId, Action<MonitoredItem> onNotification)
        {
            var subscription = new Subscription(Session.DefaultSubscription)
            {
                PublishingInterval = 100,
                KeepAliveCount = 10,
                LifetimeCount = 100,
                MaxNotificationsPerPublish = 1000,
                Priority = 0,
                PublishingEnabled = true,
            };
            Session.AddSubscription(subscription);
            subscription.Create();

            var item = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = nodeId,
                AttributeId = Attributes.Value,
                MonitoringMode = MonitoringMode.Reporting,
                SamplingInterval = 50,
                QueueSize = 10,
                DiscardOldest = true,
            };
            item.Notification += (mi, _) => onNotification(mi);

            subscription.AddItem(item);
            subscription.ApplyChanges();
            _subscriptions.Add(subscription);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                foreach (var sub in _subscriptions)
                {
                    try { sub.Delete(silent: true); } catch { /* best-effort */ }
                }
                if (Session.Connected)
                {
                    Session.Close();
                }
                Session.Dispose();
            }
            catch
            {
                // best-effort
            }
            await Task.CompletedTask;
        }
    }
}
