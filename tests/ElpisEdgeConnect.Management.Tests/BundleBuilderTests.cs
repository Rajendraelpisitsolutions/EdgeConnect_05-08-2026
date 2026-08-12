// ============================================================================
// Tests: BundleBuilder (ADR-0020 G1). End-to-end of the contributor model:
//        a multi-protocol gateway.json is composed into a bundle via the REAL
//        per-protocol registry. Pins that every protocol secret is redacted,
//        the v1 contributors all run, bundle-info.json carries the contributor
//        list + redaction summary, and — the load-bearing invariant — the
//        preview manifest equals the generated manifest (determinism).
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Identity;
using ElpisEdgeConnect.Management.Backup;
using ElpisEdgeConnect.Management.Bundle;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using HostOptions = ElpisEdgeConnect.Host.HostOptions;

namespace ElpisEdgeConnect.Management.Tests;

public class BundleBuilderTests
{
    private const string MultiProtocolConfig = """
        {
          "gateway": { "gatewayId": "gw-itest", "gatewayName": "Integration Gateway", "site": "Plant A" },
          "sources": [
            { "instanceId": "modbus-1", "protocolName": "modbustcp", "deviceId": "dev-modbus",
              "connection": { "host": "10.0.0.5", "port": 5020 } },
            { "instanceId": "opc-1", "protocolName": "opcua-client", "deviceId": "dev-opc",
              "connection": { "endpointUrl": "opc.tcp://plc:4840", "authMode": "UserName",
                "credentials": { "username": "operator", "password": "opc-secret-pw" } } }
          ],
          "sinks": [
            { "instanceId": "mqtt-1", "protocolName": "mqtt",
              "connection": { "brokerHost": "broker.local", "username": "pub", "password": "mqtt-secret-pw" } }
          ],
          "routes": [
            { "routeId": "r1", "name": "Modbus to MQTT", "sourceInstanceId": "modbus-1", "sinkInstanceIds": ["mqtt-1"] },
            { "routeId": "r2", "name": "Disabled", "sourceInstanceId": "opc-1", "sinkInstanceIds": ["mqtt-1"], "enabled": false }
          ]
        }
        """;

    // An mqtt sink connection with a JWT under a benign (non-secret) key —
    // survives as INCLUDE and is flagged by the secret-shape detector.
    private const string SecretShapedConfig = """
        {
          "gateway": { "gatewayId": "gw-itest", "gatewayName": "G" },
          "sinks": [
            { "instanceId": "mqtt-1", "protocolName": "mqtt",
              "connection": { "brokerHost": "broker.local",
                "customAttr": "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ4In0.abcDEF123_-" } }
          ]
        }
        """;

    private static BundleBuilder NewBuilder(TempDataRoot temp, IGatewayAuditWriter? audit = null)
    {
        var contributors = new IBundleContributor[]
        {
            new GatewayIdentityContributor(),
            new ConfigContributor(),
            new HistoryContributor(),
            new AuditContributor(),
            new RouteInventoryContributor(),
        };
        return new BundleBuilder(
            temp.HostOptions,
            new FakeBundleConfig(temp.CurrentConfigPath),
            new FakeIdentity("gw-itest"),
            new ConfigRedactionEngine(),
            RealRegistry(),
            audit ?? new FakeAuditWriter(),
            contributors);
    }

    [Fact]
    public async Task Preview_RunsAllContributors_AndRedactsEverySecret()
    {
        using var temp = new TempDataRoot();
        temp.WriteCurrentConfig(MultiProtocolConfig);

        var preview = await NewBuilder(temp).PreviewAsync(CancellationToken.None);

        preview.Contributors.Select(c => c.Name).Should().BeEquivalentTo(
            new[] { "identity", "config", "history", "audit", "route-inventory" });

        preview.Manifest.Redactions["gateway.json"].Should().BeEquivalentTo(new[]
        {
            "sources[1].connection.credentials.password",
            "sinks[0].connection.password",
        });
        preview.RedactionSummary.MaskedFields.Should().Be(2);
        preview.RedactionSummary.StrippedFields.Should().Be(0);
        preview.TotalBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Build_PreviewManifestEqualsGeneratedManifest()
    {
        using var temp = new TempDataRoot();
        temp.WriteCurrentConfig(MultiProtocolConfig);
        var builder = NewBuilder(temp);

        var preview = await builder.PreviewAsync(CancellationToken.None);

        using var ms = new MemoryStream();
        await builder.BuildAsync(ms, "operator-x", reason: null, acknowledgedSecretShapeWarnings: true, CancellationToken.None);
        var generated = ReadJsonEntry<BundleManifest>(ms, "manifest.json");

        generated.Should().BeEquivalentTo(preview.Manifest,
            "the determinism invariant guarantees the previewed manifest is exactly what gets generated");
    }

    [Fact]
    public async Task Build_WritesRedactedConfig_Inventory_Identity_And_BundleInfo()
    {
        using var temp = new TempDataRoot();
        temp.WriteCurrentConfig(MultiProtocolConfig);

        using var ms = new MemoryStream();
        await NewBuilder(temp).BuildAsync(ms, "operator-x", reason: null, acknowledgedSecretShapeWarnings: true, CancellationToken.None);

        ReadEntryText(ms, "gateway.json").Should().NotContain("opc-secret-pw").And.NotContain("mqtt-secret-pw");

        var inventory = ReadJsonEntry<JsonElement>(ms, "route-inventory.json");
        inventory.GetProperty("sources").GetInt32().Should().Be(2);
        inventory.GetProperty("routes").GetInt32().Should().Be(2);
        inventory.GetProperty("enabledRoutes").GetInt32().Should().Be(1);
        inventory.GetProperty("disabledRoutes").GetInt32().Should().Be(1);

        var info = ReadJsonEntry<BundleInfo>(ms, "bundle-info.json");
        info.BundleSpecVersion.Should().Be(BundleBuilder.CurrentSpecVersion);
        info.RedactionEngineVersion.Should().Be(ConfigRedactionEngine.EngineVersion);
        info.GatewayId.Should().Be("gw-itest");
        info.BundlerInvokedBy.Should().Be("operator-x");
        info.Contributors.Select(c => c.Name).Should().Contain(new[] { "config", "route-inventory" });
        info.RedactionSummary.MaskedFields.Should().Be(2);
    }

    [Fact]
    public async Task Build_AppendsBundleGeneratedAuditEvent()
    {
        using var temp = new TempDataRoot();
        temp.WriteCurrentConfig(MultiProtocolConfig);
        var audit = new FakeAuditWriter();

        using var ms = new MemoryStream();
        await NewBuilder(temp, audit).BuildAsync(ms, "operator-x", reason: null, acknowledgedSecretShapeWarnings: true, CancellationToken.None);

        audit.Entries.Should().ContainSingle();
        var entry = audit.Entries[0];
        entry.Action.Should().Be(ConfigurationAuditAction.BundleGenerated);
        entry.Actor.Should().Be("operator-x");
        entry.Summary.Should().Contain("Diagnostic bundle generated").And.Contain("sha256:");
    }

    [Fact]
    public async Task Preview_DoesNotAppendAuditEvent()
    {
        using var temp = new TempDataRoot();
        temp.WriteCurrentConfig(MultiProtocolConfig);
        var audit = new FakeAuditWriter();

        await NewBuilder(temp, audit).PreviewAsync(CancellationToken.None);

        audit.Entries.Should().BeEmpty("preview is a dry-run — only generation is an audit event");
    }

    [Fact]
    public async Task Preview_ReportsSecretShapeWarningCount()
    {
        using var temp = new TempDataRoot();
        temp.WriteCurrentConfig(SecretShapedConfig);

        var preview = await NewBuilder(temp).PreviewAsync(CancellationToken.None);

        preview.SecretShapeWarningCount.Should().Be(1);
        // The structured list (G5) must name the flagged field so the Studio can
        // surface it in the operator acknowledgement prompt.
        preview.SecretShapeWarnings.Should().HaveCount(1);
        preview.SecretShapeWarnings[0].File.Should().NotBeNullOrWhiteSpace();
        preview.SecretShapeWarnings[0].Path.Should().NotBeNullOrWhiteSpace();
        preview.SecretShapeWarnings[0].Detail.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Build_SecretShapeWarning_Unacknowledged_Refuses()
    {
        using var temp = new TempDataRoot();
        temp.WriteCurrentConfig(SecretShapedConfig);

        using var ms = new MemoryStream();
        var act = () => NewBuilder(temp).BuildAsync(
            ms, "operator-x", reason: null, acknowledgedSecretShapeWarnings: false, CancellationToken.None);

        (await act.Should().ThrowAsync<BundleAcknowledgementRequiredException>(
                "a bundle with secret-shape warnings must not generate without acknowledgement"))
            .Which.SecretShapeWarningCount.Should().Be(1);
    }

    [Fact]
    public async Task Build_SecretShapeWarning_Acknowledged_Succeeds_AndRecordsReason()
    {
        using var temp = new TempDataRoot();
        temp.WriteCurrentConfig(SecretShapedConfig);
        var audit = new FakeAuditWriter();

        using var ms = new MemoryStream();
        var filename = await NewBuilder(temp, audit).BuildAsync(
            ms, "operator-x", reason: "ticket-42", acknowledgedSecretShapeWarnings: true, CancellationToken.None);

        filename.Should().StartWith("edgeconnect-bundle-");
        ReadJsonEntry<BundleInfo>(ms, "bundle-info.json").Reason.Should().Be("ticket-42");
        audit.Entries[0].Summary.Should().Contain("Reason: ticket-42");
    }

    // Regression (G5 live finding): the G4 tests exercised BuildAsync with a
    // FAKE IGatewayAuditWriter, so the real ConfigurationManager audit-append
    // path never ran. Generating against a real, initialized manager must
    // succeed end-to-end and write a BUNDLE.GENERATED entry to the audit chain.
    [Fact]
    public async Task Build_WithRealConfigurationManager_AppendsAuditEntry()
    {
        using var temp = new TempDataRoot();
        temp.WriteCurrentConfig(MultiProtocolConfig);

        var layout = new ConfigurationStorageLayout(temp.Root);
        var store = new FileSystemConfigurationStore(layout);
        var manager = new ConfigurationManager(store);
        await manager.InitializeAsync(CancellationToken.None);

        var contributors = new IBundleContributor[]
        {
            new GatewayIdentityContributor(),
            new ConfigContributor(),
            new HistoryContributor(),
            new AuditContributor(),
            new RouteInventoryContributor(),
        };
        var builder = new BundleBuilder(
            temp.HostOptions,
            manager,
            new FakeIdentity("gw-itest"),
            new ConfigRedactionEngine(),
            RealRegistry(),
            manager, // the SAME manager is the IGatewayAuditWriter — the real path
            contributors);

        using var ms = new MemoryStream();
        var filename = await builder.BuildAsync(
            ms, "operator-x", reason: "ticket-99", acknowledgedSecretShapeWarnings: true, CancellationToken.None);

        filename.Should().StartWith("edgeconnect-bundle-");

        // The BUNDLE.GENERATED entry must be on the real audit chain.
        var entries = new List<ConfigurationAuditEntry>();
        await foreach (var e in manager.GetAuditLogAsync(verifyChain: true, CancellationToken.None))
        {
            entries.Add(e);
        }
        entries.Should().Contain(e =>
            e.Action == ConfigurationAuditAction.BundleGenerated && e.Summary.Contains("Reason: ticket-99"));
    }

    [Fact]
    public async Task Build_NoActiveConfig_FailsClosed()
    {
        using var temp = new TempDataRoot(); // no current.json written
        using var ms = new MemoryStream();

        var act = () => NewBuilder(temp).BuildAsync(ms, "operator-x", reason: null, acknowledgedSecretShapeWarnings: true, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "a contributor failure fails the whole bundle — no silent partial bundle");
    }

    // ----- Helpers -----------------------------------------------------------

    private static BundleRedactionRulesRegistry RealRegistry()
    {
        var services = new ServiceCollection();
        ElpisEdgeConnect.Host.BundleRedactionRulesRegistration.AddBundleRedactionRules(services);
        using var sp = services.BuildServiceProvider();
        return new BundleRedactionRulesRegistry(sp.GetServices<IBundleRedactionRules>());
    }

    private static T ReadJsonEntry<T>(MemoryStream ms, string name)
    {
        var json = ReadEntryText(ms, name);
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static string ReadEntryText(MemoryStream ms, string name)
    {
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
        var entry = zip.GetEntry(name) ?? throw new InvalidOperationException($"{name} not found in bundle");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class TempDataRoot : IDisposable
    {
        public string Root { get; }
        public HostOptions HostOptions { get; }
        public string CurrentConfigPath => Path.Combine(Root, "config", "current.json");

        public TempDataRoot()
        {
            Root = Path.Combine(Path.GetTempPath(), "edc-bundle-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "config"));
            HostOptions = new HostOptions
            {
                ConfigDirectory = Path.Combine(Root, "config"),
                LicensePath = Path.Combine(Root, "license.json"),
                GatewayIdentityPath = Path.Combine(Root, "identity"),
                DataRoot = Root,
            };
        }

        public void WriteCurrentConfig(string json) => File.WriteAllText(CurrentConfigPath, json);

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { /* best-effort */ }
        }
    }

    private sealed class FakeIdentity : IGatewayIdentity
    {
        public FakeIdentity(string gatewayId) => GatewayId = gatewayId;
        public string GatewayId { get; }
    }

    private sealed class FakeAuditWriter : IGatewayAuditWriter
    {
        public List<ConfigurationAuditEntry> Entries { get; } = new();

        public ValueTask<ConfigurationAuditEntry> AppendBundleGeneratedAsync(
            string actor, string summary, CancellationToken cancellationToken)
        {
            var entry = new ConfigurationAuditEntry
            {
                Timestamp = DateTime.UtcNow,
                VersionId = new ConfigurationVersionId("v1"),
                Action = ConfigurationAuditAction.BundleGenerated,
                Actor = actor,
                Summary = summary,
                PreviousHash = ConfigurationAuditLog.GenesisHash,
            };
            Entries.Add(entry);
            return ValueTask.FromResult(entry);
        }
    }

    private sealed class FakeBundleConfig : IConfigurationManager
    {
        private static readonly JsonSerializerOptions DeserializeOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };

        private readonly string _currentPath;

        public FakeBundleConfig(string currentPath) => _currentPath = currentPath;

        public async ValueTask<GatewayConfiguration> GetCurrentAsync(CancellationToken cancellationToken)
        {
            // Mirror the real manager: the in-memory config is available even if
            // the on-disk current.json is absent — so the ConfigContributor's own
            // fail-closed check (not a file read here) is what the no-config test
            // exercises.
            if (!File.Exists(_currentPath))
            {
                return new GatewayConfiguration
                {
                    Gateway = new GatewaySettings { GatewayId = "gw-itest", GatewayName = "gw" },
                };
            }
            var json = await File.ReadAllTextAsync(_currentPath, cancellationToken);
            return JsonSerializer.Deserialize<GatewayConfiguration>(json, DeserializeOpts)!;
        }

        public Task<IReadOnlyList<ConfigurationHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ConfigurationHistoryEntry>>(Array.Empty<ConfigurationHistoryEntry>());

#pragma warning disable CS1998 // no awaits — empty audit stream
        public async IAsyncEnumerable<ConfigurationAuditEntry> GetAuditLogAsync(
            bool verifyChain, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield break;
        }
#pragma warning restore CS1998

        public ConfigurationVersionId CurrentVersionId => throw new NotImplementedException();
        public Task InitializeAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<DraftId> CreateDraftAsync(GatewayConfiguration draft, string? actor, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<GatewayConfiguration?> GetDraftAsync(DraftId draftId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<DraftId>> ListDraftsAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ValidationResult> ValidateDraftAsync(DraftId draftId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ConfigurationApplyResult> ApplyDraftAsync(DraftId draftId, string? actor, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task DiscardDraftAsync(DraftId draftId, string? actor, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ConfigurationApplyResult> RollbackAsync(ConfigurationVersionId targetVersionId, string? actor, CancellationToken cancellationToken) => throw new NotImplementedException();
        public ValueTask<ConfigurationAuditEntry> AppendRuntimeFaultAsync(ElpisEdgeConnect.Core.Diagnostics.ConfigurationFault fault, CancellationToken cancellationToken) => throw new NotImplementedException();
        public event EventHandler<ConfigurationChangeEventArgs>? CurrentChanged { add { } remove { } }
    }
}
