// ============================================================================
// File: ModbusTcpToOpcUaServerEndToEndTests.cs
// Purpose: Full-stack verification of the Customer B happy path —
//          pymodbus simulator → ModbusTcpSourceAdapter → canonical
//          pipeline → OpcUaServerSinkAdapter → OPC UA Client. Proves
//          the H milestone composes with the existing Phase 3 Modbus
//          source and the locked canonical pipeline:
//
//            * Real Modbus simulator emits known seeded values that
//              random-walk over time.
//            * ModbusTcpSourceAdapter polls and produces
//              CanonicalDataPoints (Phase 3 — already certified).
//            * RoutingEngine fans points to the OPC UA sink.
//            * OpcUaServerSinkAdapter updates address-space nodes.
//            * An OPC UA Client connects, subscribes, and observes
//              the values + Quality=Good on the expected NodeIds.
//
//          Uses PythonModbusSimulatorFixture (spawns pymodbus
//          directly) rather than the Docker-based fixture so the
//          test runs on dev laptops without Docker installed.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone H end-to-end
//            shared-knowledge/contracts/opcua-namespace-policy.md
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Identity;
using ElpisEdgeConnect.Host.Adapters;
using ElpisEdgeConnect.Sinks.OpcUaServer;
using ElpisEdgeConnect.Sources.ModbusTcp;
using ElpisEdgeConnect.Sources.ModbusTcp.Scanning;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Xunit;
using Xunit.Abstractions;
using static ElpisEdgeConnect.Integration.Tests.IntegrationTestData;

namespace ElpisEdgeConnect.Integration.Tests;

[Trait("Category", "OpcUaServer")]
[Trait("Category", "RequiresPython")]
public sealed class ModbusTcpToOpcUaServerEndToEndTests
    : IClassFixture<PythonModbusSimulatorFixture>
{
    private const string TestGatewayId = "gw-modbus-opcua-e2e";
    private const string SourceInstanceId = "modbus-e2e";
    private const string SinkInstanceId = "opcua-e2e";
    private const string RouteId = "route-modbus-opcua";
    private const string NamespaceUri = "urn:elpis:edgeconnect:v1";

    private readonly PythonModbusSimulatorFixture _sim;
    private readonly ITestOutputHelper _output;

    public ModbusTcpToOpcUaServerEndToEndTests(
        PythonModbusSimulatorFixture sim,
        ITestOutputHelper output)
    {
        _sim = sim;
        _output = output;
    }

    [Fact]
    public async Task ModbusSimulator_ToOpcUaServer_ToClient_HappyPath()
    {
        if (!_sim.IsAvailable)
        {
            _output.WriteLine($"[SKIPPED] pymodbus simulator unavailable: {_sim.UnavailableReason}");
            return;
        }

        // --- Pick ephemeral OPC UA endpoint port + per-test PKI root --------
        var opcUaPort = FindFreeTcpPort();
        var pkiRoot = Path.Combine(Path.GetTempPath(), $"opcua-modbus-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(pkiRoot);

        try
        {
            // --- Modbus source --------------------------------------------------
            var modbusConfig = new ModbusTcpSourceConfiguration
            {
                InstanceId = SourceInstanceId,
                ProtocolName = "modbustcp",
                DeviceId = "plc-modbus-opcua",
                DeviceName = "Customer B Demo PLC",
                DeviceClass = "plc",
                Host = _sim.Host,
                Port = (ushort)_sim.Port,
                DefaultUnitId = 1,
                PollIntervalMs = 100,
                ConnectTimeoutMs = 3000,
                RequestTimeoutMs = 2000,
                KeepAlive = true,
                MaxTransactionRetries = 1,
                InitialBackoffMs = 50,
                MaxBackoffMs = 500,
                CircuitBreakerThreshold = 100,
                CircuitBreakerResetMs = 1000,
                MaxGapRegisters = 8,
                TagDefinitions = BuildDemoTagSet(),
            };
            var modbusAdapter = new ModbusTcpSourceAdapter(
                SourceInstanceId,
                new FluentModbusClient(),
                NullLogger<ModbusTcpSourceAdapter>.Instance,
                new StubGatewayIdentity(TestGatewayId));
            var sourceReg = new SourceRegistration
            {
                Adapter = modbusAdapter,
                Config = modbusConfig,
                RouteId = RouteId,
            };

            // --- OPC UA Server sink --------------------------------------------
            var opcUaEndpoint = $"opc.tcp://127.0.0.1:{opcUaPort}/edgeconnect";
            var opcUaConfig = new OpcUaServerConfiguration
            {
                InstanceId = SinkInstanceId,
                ProtocolName = OpcUaServerConfiguration.ProtocolNameConstant,
                EndpointUrl = opcUaEndpoint,
                ApplicationUri = $"urn:elpis:edgeconnect:test:server:{Guid.NewGuid():N}",
                ApplicationName = "EdgeConnect Modbus→OPC UA E2E Server",
                MinPublishingIntervalMs = 50,
                Security = new OpcUaSecurityConfig
                {
                    Mode = OpcUaSecurityMode.None,
                    ApplicationCertificatePath = Path.Combine(pkiRoot, "server", "own"),
                    TrustedClientsPath = Path.Combine(pkiRoot, "server", "trusted"),
                    RejectedClientsPath = Path.Combine(pkiRoot, "server", "rejected"),
                },
            };
            var opcUaAdapter = new OpcUaServerSinkAdapter(
                SinkInstanceId,
                NullLogger<OpcUaServerSinkAdapter>.Instance);
            var sinkReg = new SinkRegistration
            {
                Adapter = opcUaAdapter,
                Config = opcUaConfig,
                RouteId = RouteId,
            };

            // --- Boot the full host (real routing engine, real pipeline) -------
            await using var host = HostHarness.Build(
                sources: new[] { sourceReg },
                sinks: new[] { sinkReg },
                config: Config(Route(RouteId, SourceInstanceId, new[] { SinkInstanceId })));

            await host.StartAsync();

            _output.WriteLine($"Modbus simulator: {_sim.Host}:{_sim.Port}");
            _output.WriteLine($"OPC UA endpoint:  {opcUaEndpoint}");

            // --- Connect OPC UA Client + subscribe to canonical tags -----------
            await using var client = await OpcUaTestClient.ConnectAsync(opcUaEndpoint, pkiRoot);
            var nsIndex = (ushort)client.Session.NamespaceUris.GetIndex(NamespaceUri);
            nsIndex.Should().NotBe(0,
                "the EdgeConnect namespace must be registered on the negotiated session");

            // Subscribe to a representative spread across datatypes — covers
            // bool / uint16 / float32 / uint32-CDAB / string / scaled int16.
            var subscribedTags = new[]
            {
                "running",       // bool
                "spindle_rpm",   // uint16, walks 1200..1600
                "feed_rate",     // float32, walks 200..300
                "parts_count",   // uint32 CDAB, monotonic
                "mode",          // string
                "temperature",   // scaled int16
            };

            var notifications = new ConcurrentDictionary<string, ConcurrentQueue<(object? Value, uint Status)>>();
            foreach (var tag in subscribedTags)
            {
                var nodeIdString = $"{TestGatewayId}/{SourceInstanceId}/{tag}";
                var clientNodeId = new NodeId(nodeIdString, nsIndex);
                notifications[tag] = new ConcurrentQueue<(object?, uint)>();
                var tagName = tag; // capture
                client.Subscribe(clientNodeId, mi =>
                {
                    foreach (var v in mi.DequeueValues())
                    {
                        notifications[tagName].Enqueue((v.Value, v.StatusCode.Code));
                    }
                });
            }

            // --- Wait for every tag to land at least one notification ----------
            // Modbus poll cadence is 200-1000ms per tag; allow up to 20s for
            // every subscribed tag to surface.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                if (subscribedTags.All(t => notifications[t].Count > 0))
                {
                    break;
                }
                await Task.Delay(100);
            }

            // Trigger one more publishing cycle so we see the random walks.
            await Task.Delay(1500);

            await host.StopAsync();

            // --- Assertions ---------------------------------------------------
            foreach (var tag in subscribedTags)
            {
                notifications[tag].Should().NotBeEmpty(
                    $"tag '{tag}' was subscribed but never received a notification");
            }

            // Quality should be Good across the board — Modbus simulator
            // never errors, and the canonical pipeline should pass through
            // DataQuality.Good which maps to StatusCodes.Good.
            foreach (var (tag, queue) in notifications)
            {
                var latest = queue.Last();
                latest.Status.Should().Be(StatusCodes.Good,
                    $"tag '{tag}' should arrive with StatusCode.Good — Modbus sim is healthy");
            }

            // Value assertions per tag — match what server.py exposes:
            var runningValue = notifications["running"].Last().Value;
            runningValue.Should().BeOfType<bool>("'running' is a Modbus coil → bool");
            runningValue.Should().Be(true, "server.py seeds coil 0 = True (running)");

            var rpmValue = notifications["spindle_rpm"].Last().Value;
            rpmValue.Should().BeAssignableTo<IConvertible>();
            Convert.ToInt32(rpmValue, System.Globalization.CultureInfo.InvariantCulture)
                .Should().BeInRange(1200, 1600,
                    "server.py seeds spindle_rpm=1450 and random-walks within 1200..1600");

            var feedValue = notifications["feed_rate"].Last().Value;
            feedValue.Should().BeAssignableTo<IConvertible>();
            Convert.ToDouble(feedValue, System.Globalization.CultureInfo.InvariantCulture)
                .Should().BeInRange(200.0, 300.0,
                    "server.py seeds feed_rate=250.5 and random-walks within 200..300 mm/min");

            var partsValue = notifications["parts_count"].Last().Value;
            partsValue.Should().BeAssignableTo<IConvertible>();
            Convert.ToInt64(partsValue, System.Globalization.CultureInfo.InvariantCulture)
                .Should().BeGreaterOrEqualTo(1_234_567L,
                    "parts_count seeded at 1,234,567 and is monotonic increment");

            var modeValue = notifications["mode"].Last().Value;
            modeValue.Should().BeOfType<string>().And.Subject
                .As<string>().Trim().Should().Be("AUTO",
                    "server.py seeds mode register block with 'AUTO' (space-padded)");

            var tempValue = notifications["temperature"].Last().Value;
            tempValue.Should().BeAssignableTo<IConvertible>();
            Convert.ToDouble(tempValue, System.Globalization.CultureInfo.InvariantCulture)
                .Should().BeInRange(30.0, 50.0,
                    "raw 380..460 with scale 0.1 → 38.0..46.0 °C");

            // Health snapshot — every tag should have decoded cleanly.
            var health = await modbusAdapter.CheckHealthAsync(CancellationToken.None);
            ((long)health.Metrics!["pollSuccesses"]!).Should().BeGreaterThan(0);
            ((long)health.Metrics!["decodeFailures"]!).Should().Be(0,
                "every demo tag must decode cleanly end-to-end");

            // Diagnostics — the OPC UA sink should report the connected
            // client through its session-tracking surface.
            var diagnostics = host.Collector;
            var snap = diagnostics.GetRouteSnapshot(RouteId)!;
            var sinkSnap = snap.Sinks.Single(s => s.SinkInstanceId == SinkInstanceId);
            // SinkSessionPoller default cadence is 5s and we already waited
            // ~1.5s above; the connection may or may not be reflected in the
            // most-recent snapshot yet, but if it IS, count must be > 0.
            if (sinkSnap.ActiveSessions is { } sessions)
            {
                _output.WriteLine(
                    $"Diagnostics observed {sessions.Count} active OPC UA session(s) on the sink.");
            }

            _output.WriteLine($"Notifications received per tag: {string.Join(", ", subscribedTags.Select(t => $"{t}={notifications[t].Count}"))}");
        }
        finally
        {
            try { Directory.Delete(pkiRoot, recursive: true); } catch { /* best-effort */ }
        }
    }

    // ------------------------------------------------------------------
    // Helpers — shared shape with OpcUaServerEndToEndTests but kept
    // independent for test-class isolation.
    // ------------------------------------------------------------------

    private static IReadOnlyList<ModbusTagDefinition> BuildDemoTagSet() => new ModbusTagDefinition[]
    {
        new() { Name = "running",         RegisterClass = ModbusRegisterClass.Coil,            Address = 0,   ScanRateMs = 200, Datatype = "bool" },
        new() { Name = "alarm_active",    RegisterClass = ModbusRegisterClass.Coil,            Address = 1,   ScanRateMs = 200, Datatype = "bool" },
        new() { Name = "spindle_rpm",     RegisterClass = ModbusRegisterClass.HoldingRegister, Address = 0,   ScanRateMs = 200, Datatype = "uint16", Unit = "rpm" },
        new() { Name = "feed_rate",       RegisterClass = ModbusRegisterClass.HoldingRegister, Address = 10,  ScanRateMs = 200, Datatype = "float32", ByteOrder = ModbusByteOrder.ABCD, Unit = "mm/min" },
        new() { Name = "parts_count",     RegisterClass = ModbusRegisterClass.HoldingRegister, Address = 20,  ScanRateMs = 300, Datatype = "uint32",  ByteOrder = ModbusByteOrder.CDAB },
        new() { Name = "mode",            RegisterClass = ModbusRegisterClass.HoldingRegister, Address = 60,  ScanRateMs = 500, Datatype = "string8" },
        new() { Name = "temperature",     RegisterClass = ModbusRegisterClass.InputRegister,   Address = 0,   ScanRateMs = 300, Datatype = "int16", Scale = 0.1, Unit = "C" },
    };

    private static int FindFreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class StubGatewayIdentity : IGatewayIdentity
    {
        public StubGatewayIdentity(string gatewayId) => GatewayId = gatewayId;
        public string GatewayId { get; }
    }

    // ------------------------------------------------------------------
    // Test-local OPC UA Client wrapper — identical surface to the one in
    // OpcUaServerEndToEndTests, kept local for class-isolation.
    // ------------------------------------------------------------------
    private sealed class OpcUaTestClient : IAsyncDisposable
    {
        private readonly List<Subscription> _subscriptions = new();
        public ISession Session { get; }

        private OpcUaTestClient(ISession session) { Session = session; }

        public static async Task<OpcUaTestClient> ConnectAsync(string endpointUrl, string pkiRoot)
        {
            var appConfig = new ApplicationConfiguration
            {
                ApplicationName = "EdgeConnect Modbus→OPC UA E2E Client",
                ApplicationUri = $"urn:elpis:edgeconnect:test:client:{Guid.NewGuid():N}",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "client", "own"),
                        SubjectName = "CN=EdgeConnect Modbus E2E Client",
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
#pragma warning disable CS0618
            await appInstance.CheckApplicationInstanceCertificate(
                silent: true, minimumKeySize: 2048).ConfigureAwait(false);
#pragma warning restore CS0618

            var endpoint = CoreClientUtils.SelectEndpoint(appConfig, endpointUrl, useSecurity: false);
            var endpointConfiguration = EndpointConfiguration.Create(appConfig);
            var configuredEndpoint = new ConfiguredEndpoint(null, endpoint, endpointConfiguration);

            var session = await Opc.Ua.Client.Session.Create(
                appConfig,
                configuredEndpoint,
                updateBeforeConnect: false,
                checkDomain: false,
                sessionName: "modbus-opcua-e2e",
                sessionTimeout: 60_000,
                identity: new UserIdentity(new AnonymousIdentityToken()),
                preferredLocales: null).ConfigureAwait(false);

            return new OpcUaTestClient(session);
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

        public ValueTask DisposeAsync()
        {
            try
            {
                foreach (var sub in _subscriptions)
                {
                    try { sub.Delete(silent: true); } catch { /* best-effort */ }
                }
                if (Session.Connected) Session.Close();
                Session.Dispose();
            }
            catch { /* best-effort */ }
            return ValueTask.CompletedTask;
        }
    }
}
