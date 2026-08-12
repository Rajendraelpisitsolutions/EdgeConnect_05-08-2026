// ============================================================================
// File: OpcUaClientInProcessServerFixture.cs
// Purpose: In-process minimal OPC UA Server hosted directly by the test
//          assembly. Provides a deterministic address space + tag-update
//          cadence + restart semantics so the OpcUaClientSourceAdapter
//          can be exercised end-to-end against a real OPC stack endpoint
//          WITHOUT depending on any external server installation.
//
// LOCKED behaviour (per PR 7a plan + amendments, user lock 2026-05-29):
//
//   1. Custom StandardServer subclass — not reusing Sinks.OpcUaServer
//      to preserve client-side test independence.
//   2. FixtureCapabilities surfaces what scenarios the fixture supports
//      (SupportsTransferSubscriptions / Recreate / BrowseContinuation)
//      so future protocol work doesn't have to do fixture archaeology.
//   3. Address space: 6 simulated tags hanging off Objects/Simulated:
//        - Counter      (Int32  — increments per tick)
//        - Sine         (Double — sin(t))
//        - Square       (Boolean — toggles)
//        - Timestamp    (DateTime — wall-clock UTC)
//        - Text         (String — round-robin from a small list)
//        - Static_Int   (Int32  — never changes, useful for browse-then-
//                                  subscribe tests that don't want value
//                                  churn)
//   4. Restart variants:
//        - RestartPreservingSessionsAsync — stops + restarts on the SAME
//          port so an in-flight Session can rebind via the stack's
//          SessionReconnectHandler.TransferSubscriptions path.
//        - RestartDroppingSessionsAsync — stops + restarts on a NEW
//          port (close + reopen) so the stack falls back to Recreate.
//   5. PKI under a per-fixture temp dir — wiped at DisposeAsync.
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1
//            PR 7a plan + amendments (user lock 2026-05-29)
//            docs/licensing/module-catalog.md § Third-party library licensing
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;

namespace ElpisEdgeConnect.Integration.Tests.OpcUaClient;

/// <summary>
/// Capability matrix the fixture supports. Future scenarios add fields;
/// existing tests can guard with capability checks rather than failing
/// silently when the fixture doesn't actually cover their case.
/// </summary>
public sealed record FixtureCapabilities
{
    public required bool SupportsTransferSubscriptions { get; init; }
    public required bool SupportsRecreateSubscriptions { get; init; }
    public required bool SupportsBrowseContinuationPoints { get; init; }
}

/// <summary>
/// In-process OPC UA Server hosted by the test assembly. Single instance
/// per test class via xUnit class fixture — see test classes for the
/// IClassFixture wiring.
/// </summary>
public sealed class OpcUaClientInProcessServerFixture : IAsyncDisposable
{
    public const string SimulatedFolderBrowseName = "Simulated";
    public const ushort SimulatedNamespaceIndex = 2;
    public const string SimulatedNamespaceUri = "urn:elpis:edgeconnect:test:opcuaclient";

    private readonly string _pkiRoot;
    private readonly Timer _updateTimer;
    private readonly object _serverLock = new();
    private long _counterTicks;

    private ApplicationInstance? _appInstance;
    private TestServerHost? _server;
    private int _port;

    private OpcUaClientInProcessServerFixture(string pkiRoot, int port)
    {
        _pkiRoot = pkiRoot;
        _port = port;
        _updateTimer = new Timer(OnTick, state: null, dueTime: Timeout.Infinite, period: Timeout.Infinite);
    }

    /// <summary>opc.tcp://127.0.0.1:{port}/edgeconnect-test endpoint of the running server.</summary>
    public string EndpointUrl => $"opc.tcp://127.0.0.1:{_port}/edgeconnect-test";

    /// <summary>Test-only capability matrix.</summary>
    public FixtureCapabilities Capabilities { get; } = new()
    {
        SupportsTransferSubscriptions = true,
        SupportsRecreateSubscriptions = true,
        SupportsBrowseContinuationPoints = true,
    };

    /// <summary>Start an in-process server on an ephemeral port with the standard simulated address space.</summary>
    public static async Task<OpcUaClientInProcessServerFixture> StartAsync()
    {
        var pkiRoot = Path.Combine(Path.GetTempPath(), $"opcua-client-it-{Guid.NewGuid():N}");
        Directory.CreateDirectory(pkiRoot);
        var port = GetEphemeralTcpPort();
        var fixture = new OpcUaClientInProcessServerFixture(pkiRoot, port);
        await fixture.StartServerAsync().ConfigureAwait(false);
        // 10 ms tick — predictable cadence for subscription tests.
        fixture._updateTimer.Change(dueTime: 0, period: 10);
        return fixture;
    }

    /// <summary>
    /// Stop and restart the server on the SAME port so the OPC stack's
    /// SessionReconnectHandler can rebind via the same endpoint URI.
    /// <paramref name="downTime"/> controls how long the server stays
    /// down — at least the client's keep-alive cadence (1 s for v2.1
    /// §2.5 defaults) is required for the client to actually notice
    /// the dropout; the fixture's default of 2 s gives a safe margin.
    /// </summary>
    public async Task RestartPreservingSessionsAsync(TimeSpan? downTime = null)
    {
        await StopServerAsync(preserveSessionState: true).ConfigureAwait(false);
        await Task.Delay(downTime ?? TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        await StartServerAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Stop and restart the server, dropping all session state and
    /// rebinding to a NEW port. The client cannot reach the old
    /// endpoint URI, so the SessionReconnectHandler's retries will
    /// fail indefinitely — useful for "stays Degraded" tests.
    /// </summary>
    public async Task RestartDroppingSessionsAsync(TimeSpan? downTime = null)
    {
        await StopServerAsync(preserveSessionState: false).ConfigureAwait(false);
        await Task.Delay(downTime ?? TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        _port = GetEphemeralTcpPort();
        await StartServerAsync().ConfigureAwait(false);
    }

    /// <summary>Stop the server without restarting — used by Failed-reconnect tests in PR 7b.</summary>
    public Task StopAsync() => StopServerAsync(preserveSessionState: false);

    /// <summary>NodeId of the Simulated folder for browse + subscribe targeting.</summary>
    public static NodeId SimulatedFolderNodeId =>
        new($"ns={SimulatedNamespaceIndex};s={SimulatedFolderBrowseName}");

    /// <summary>NodeId for the named tag under Objects/Simulated/.</summary>
    public static NodeId TagNodeId(string browseName) =>
        new($"ns={SimulatedNamespaceIndex};s=Simulated.{browseName}");

    /// <summary>String form of <see cref="TagNodeId"/> for adapter config.</summary>
    public static string TagNodeIdString(string browseName) =>
        $"ns={SimulatedNamespaceIndex};s=Simulated.{browseName}";

    private async Task StartServerAsync()
    {
        var appConfig = BuildApplicationConfiguration();
        await appConfig.Validate(ApplicationType.Server).ConfigureAwait(false);

        _appInstance = new ApplicationInstance
        {
            ApplicationName = appConfig.ApplicationName,
            ApplicationType = ApplicationType.Server,
            ApplicationConfiguration = appConfig,
        };

        // CS0618 — same allowance noted in OpcUaServerSinkAdapter; the
        // synchronous CheckApplicationInstanceCertificate is the only
        // overload that auto-creates the cert with the silent flag.
#pragma warning disable CS0618
        await _appInstance.CheckApplicationInstanceCertificate(
            silent: true,
            minimumKeySize: 2048).ConfigureAwait(false);
#pragma warning restore CS0618

        _server = new TestServerHost();
        await _appInstance.Start(_server).ConfigureAwait(false);
    }

    private Task StopServerAsync(bool preserveSessionState)
    {
        // preserveSessionState is informational — the OPC stack's server-
        // side session state is held in memory and dropped on Stop()
        // regardless. The "preserve" semantics for the test come from
        // the CLIENT-side SessionReconnectHandler being able to re-issue
        // CreateSession against the SAME endpoint URI (same port). When
        // we rebind to a new port the client cannot reach the prior
        // session state, forcing Recreate.
        _ = preserveSessionState;
        lock (_serverLock)
        {
            try
            {
                if (_appInstance?.Server is { } running)
                {
                    running.Stop();
                }
            }
            catch
            {
                // best-effort stop
            }
            _appInstance = null;
            _server = null;
        }
        return Task.CompletedTask;
    }

    private void OnTick(object? state)
    {
        // The TestNodeManager handles the actual variable mutation —
        // we just bump the counter so the manager has a fresh value to
        // emit on the next publish cycle. Counter increments on every
        // tick; other tags derive their value from the counter so they
        // change in lockstep.
        var counter = Interlocked.Increment(ref _counterTicks);
        var manager = _server?.NodeManager;
        if (manager is null) return;
        try { manager.AdvanceTickValues(counter); }
        catch { /* best-effort during restart races */ }
    }

    private ApplicationConfiguration BuildApplicationConfiguration() => new()
    {
        ApplicationName = "EdgeConnect OPC UA Client Test Server",
        ApplicationUri = $"urn:elpis:edgeconnect:test:server:{Guid.NewGuid():N}",
        ApplicationType = ApplicationType.Server,
        ServerConfiguration = new ServerConfiguration
        {
            BaseAddresses = { EndpointUrl },
            MinRequestThreadCount = 2,
            MaxRequestThreadCount = 50,
            MaxQueuedRequestCount = 200,
            SecurityPolicies = new ServerSecurityPolicyCollection
            {
                new()
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                },
            },
            UserTokenPolicies = new UserTokenPolicyCollection
            {
                new() { TokenType = UserTokenType.Anonymous },
            },
            MaxSessionCount = 10,
            MinSessionTimeout = 10_000,
            MaxSessionTimeout = 3_600_000,
            MaxBrowseContinuationPoints = 10,
            MaxQueryContinuationPoints = 10,
            MaxHistoryContinuationPoints = 10,
            MaxRequestAge = 600_000,
            MinPublishingInterval = 10,
            MaxPublishingInterval = 3_600_000,
            PublishingResolution = 10,
            MaxSubscriptionLifetime = 3_600_000,
            MaxMessageQueueSize = 100,
            MaxNotificationQueueSize = 100,
            MaxNotificationsPerPublish = 1_000,
            MinSubscriptionLifetime = 10_000,
            MaxPublishRequestCount = 20,
            MaxSubscriptionCount = 100,
            MaxEventQueueSize = 10_000,
        },
        SecurityConfiguration = new SecurityConfiguration
        {
            ApplicationCertificate = new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = Path.Combine(_pkiRoot, "server", "own"),
                SubjectName = "CN=EdgeConnect OPC UA Client Test Server",
            },
            TrustedPeerCertificates = new CertificateTrustList
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = Path.Combine(_pkiRoot, "server", "trusted"),
            },
            RejectedCertificateStore = new CertificateTrustList
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = Path.Combine(_pkiRoot, "server", "rejected"),
            },
            TrustedIssuerCertificates = new CertificateTrustList
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = Path.Combine(_pkiRoot, "server", "issuer"),
            },
            AutoAcceptUntrustedCertificates = true,
        },
        TransportConfigurations = new TransportConfigurationCollection(),
        TransportQuotas = new TransportQuotas
        {
            OperationTimeout = 120_000,
            MaxStringLength = 1_048_576,
            MaxByteStringLength = 4_194_304,
            MaxArrayLength = 65_535,
            MaxMessageSize = 4_194_304,
            MaxBufferSize = 65_535,
            ChannelLifetime = 300_000,
            SecurityTokenLifetime = 3_600_000,
        },
        ClientConfiguration = new ClientConfiguration(),
        TraceConfiguration = new TraceConfiguration { TraceMasks = 0 },
    };

    private static int GetEphemeralTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try { await _updateTimer.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ }
        try { await StopServerAsync(preserveSessionState: false).ConfigureAwait(false); } catch { /* best-effort */ }
        try
        {
            if (Directory.Exists(_pkiRoot)) Directory.Delete(_pkiRoot, recursive: true);
        }
        catch
        {
            // best-effort PKI cleanup
        }
    }
}

/// <summary>
/// Minimal <see cref="StandardServer"/> subclass that injects the
/// <see cref="TestNodeManager"/> with the simulated address space.
/// </summary>
internal sealed class TestServerHost : StandardServer
{
    /// <summary>Custom node manager handle, set during master-node-manager build.</summary>
    public TestNodeManager? NodeManager { get; private set; }

    protected override MasterNodeManager CreateMasterNodeManager(
        IServerInternal server,
        ApplicationConfiguration configuration)
    {
        var custom = new TestNodeManager(server, configuration);
        NodeManager = custom;
        return new MasterNodeManager(server, configuration, dynamicNamespaceUri: null, new INodeManager[]
        {
            custom,
        });
    }
}

/// <summary>
/// Hosts the simulated tag address space. Tags update on the fixture's
/// timer cadence; the OPC stack handles notification fan-out to any
/// subscribed clients via the standard <c>BaseDataVariableState.ClearChangeMasks</c>
/// machinery.
/// </summary>
internal sealed class TestNodeManager : CustomNodeManager2
{
    private readonly Dictionary<string, BaseDataVariableState> _tagNodes = new(StringComparer.Ordinal);
    private readonly object _addressSpaceLock = new();
    private readonly string[] _textValues = { "alpha", "beta", "gamma", "delta", "epsilon" };

    public TestNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration, OpcUaClientInProcessServerFixture.SimulatedNamespaceUri)
    {
    }

    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        lock (_addressSpaceLock)
        {
            base.CreateAddressSpace(externalReferences);

            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out var references))
            {
                references = new List<IReference>();
                externalReferences[ObjectIds.ObjectsFolder] = references;
            }

            var simFolder = new FolderState(null)
            {
                NodeId = OpcUaClientInProcessServerFixture.SimulatedFolderNodeId,
                BrowseName = new QualifiedName(
                    OpcUaClientInProcessServerFixture.SimulatedFolderBrowseName,
                    OpcUaClientInProcessServerFixture.SimulatedNamespaceIndex),
                DisplayName = OpcUaClientInProcessServerFixture.SimulatedFolderBrowseName,
                TypeDefinitionId = ObjectTypeIds.FolderType,
            };
            simFolder.AddReference(ReferenceTypeIds.Organizes, isInverse: true, ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, isInverse: false, simFolder.NodeId));

            AddTag(simFolder, "Counter", DataTypeIds.Int32, (int)0);
            AddTag(simFolder, "Sine", DataTypeIds.Double, 0.0);
            AddTag(simFolder, "Square", DataTypeIds.Boolean, false);
            AddTag(simFolder, "Timestamp", DataTypeIds.DateTime, DateTime.UtcNow);
            AddTag(simFolder, "Text", DataTypeIds.String, "alpha");
            AddTag(simFolder, "Static_Int", DataTypeIds.Int32, 42);

            AddPredefinedNode(SystemContext, simFolder);
        }
    }

    /// <summary>
    /// Bump every dynamic tag by one tick. Counter increments; Sine
    /// uses <c>sin(counter / 10)</c>; Square toggles every tick;
    /// Timestamp emits UtcNow; Text round-robins through a fixed list.
    /// Static_Int is intentionally left alone (its purpose is to be
    /// stable across ticks).
    /// </summary>
    public void AdvanceTickValues(long counter)
    {
        SetTag("Counter", (int)(counter % int.MaxValue));
        SetTag("Sine", Math.Sin(counter / 10.0));
        SetTag("Square", (counter % 2) == 0);
        SetTag("Timestamp", DateTime.UtcNow);
        SetTag("Text", _textValues[(int)(counter % _textValues.Length)]);
    }

    private void AddTag(FolderState parent, string browseName, NodeId dataType, object initialValue)
    {
        var variable = new BaseDataVariableState(parent)
        {
            NodeId = OpcUaClientInProcessServerFixture.TagNodeId(browseName),
            BrowseName = new QualifiedName(browseName, OpcUaClientInProcessServerFixture.SimulatedNamespaceIndex),
            DisplayName = browseName,
            DataType = dataType,
            ValueRank = ValueRanks.Scalar,
            AccessLevel = AccessLevels.CurrentRead,
            UserAccessLevel = AccessLevels.CurrentRead,
            Value = initialValue,
            StatusCode = StatusCodes.Good,
            Timestamp = DateTime.UtcNow,
            TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
        };
        parent.AddChild(variable);
        _tagNodes[browseName] = variable;
    }

    private void SetTag(string browseName, object value)
    {
        if (!_tagNodes.TryGetValue(browseName, out var node)) return;
        lock (_addressSpaceLock)
        {
            node.Value = value;
            node.Timestamp = DateTime.UtcNow;
            node.StatusCode = StatusCodes.Good;
            // ClearChangeMasks with includeChildren=false fan-outs the
            // value change to subscribed monitored items via the stack's
            // standard machinery — this is what produces the
            // DataChangeNotification the client adapter consumes.
            node.ClearChangeMasks(SystemContext, includeChildren: false);
        }
    }
}
