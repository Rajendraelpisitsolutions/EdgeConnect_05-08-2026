// ============================================================================
// Tests: BackupBuilder — composes the zip stream end-to-end. Tests use
//        a temp directory and a tiny fake IConfigurationManager to
//        drive the builder, then inspect the produced zip in memory.
//        Key contracts pinned:
//
//           * manifest.json carries the schemaVersion, backupType,
//             ExportReason, gateway identity, and counts.
//           * gateway.json is the REDACTED version, not the raw one.
//           * audit.log ships verbatim.
//           * Checksums in the manifest match the actual file content.
//           * Empty history is handled cleanly.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Backup;
using ElpisEdgeConnect.Management.Contracts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using HostOptions = ElpisEdgeConnect.Host.HostOptions;

namespace ElpisEdgeConnect.Management.Tests;

public class BackupBuilderTests
{
    [Fact]
    public async Task BuildAsync_EmitsManifestWithExpectedFields()
    {
        using var temp = new TempDataRoot();
        temp.WriteCurrentConfig(SampleConfig);

        var builder = new BackupBuilder(temp.HostOptions, new FakeConfig("gw-test", "Test Gateway"), new ConfigRedactionEngine(), TestRegistry());

        using var ms = new MemoryStream();
        var filename = await builder.BuildAsync(ms, "manual", CancellationToken.None);

        filename.Should().StartWith("edgeconnect-backup-gw-test-").And.EndWith(".zip");

        var manifest = ReadManifest(ms);
        manifest.SchemaVersion.Should().Be(BackupBuilder.CurrentSchemaVersion);
        manifest.BackupType.Should().Be(BackupBuilder.ConfigurationOnlyBackupType);
        manifest.RedactionEngineVersion.Should().Be(ConfigRedactionEngine.EngineVersion,
            "the manifest stamps which redaction-engine revision produced the artifact (ADR-0020 R-3)");
        manifest.GatewayId.Should().Be("gw-test");
        manifest.GatewayName.Should().Be("Test Gateway");
        manifest.ExportReason.Should().Be("manual");
        manifest.ExportedBy.Should().Be("system");
        manifest.Components.Configuration.Should().BeTrue();
        manifest.Components.Certificates.Should().BeFalse(
            "schemaVersion=1 / configuration-only backupType never includes certs");
    }

    [Fact]
    public async Task BuildAsync_GatewayJsonIsRedactedNotOriginal()
    {
        using var temp = new TempDataRoot();
        temp.WriteCurrentConfig(SampleConfigWithSecret);

        var builder = new BackupBuilder(temp.HostOptions, new FakeConfig("gw-test", null), new ConfigRedactionEngine(), TestRegistry());

        using var ms = new MemoryStream();
        await builder.BuildAsync(ms, "manual", CancellationToken.None);

        var gatewayJsonInZip = ReadEntryText(ms, "gateway.json");
        gatewayJsonInZip.Should().NotContain("super-secret-value",
            "the raw secret must never appear in the exported zip");
        gatewayJsonInZip.Should().Contain("\"***\"",
            "password is a MASK-tier secret — value replaced with the mask marker");
        gatewayJsonInZip.Should().Contain("password__redacted",
            "MASK retains the key and emits sibling byte-length metadata");
    }

    [Fact]
    public async Task BuildAsync_RedactionsKeyedByFilename()
    {
        using var temp = new TempDataRoot();
        temp.WriteCurrentConfig(SampleConfigWithSecret);

        var builder = new BackupBuilder(temp.HostOptions, new FakeConfig("gw-test", null), new ConfigRedactionEngine(), TestRegistry());

        using var ms = new MemoryStream();
        await builder.BuildAsync(ms, "manual", CancellationToken.None);

        var manifest = ReadManifest(ms);
        manifest.Redactions.Should().ContainKey("gateway.json");
        manifest.Redactions["gateway.json"].Should().Contain("sources[0].connection.password");
    }

    [Fact]
    public async Task BuildAsync_ChecksumsMatchActualZipContent()
    {
        using var temp = new TempDataRoot();
        temp.WriteCurrentConfig(SampleConfig);

        var builder = new BackupBuilder(temp.HostOptions, new FakeConfig("gw-test", null), new ConfigRedactionEngine(), TestRegistry());

        using var ms = new MemoryStream();
        await builder.BuildAsync(ms, "manual", CancellationToken.None);

        var manifest = ReadManifest(ms);

        // Re-extract each file the manifest claims a checksum for and
        // verify SHA-256 matches. This is the integrity-bug catcher.
        foreach (var (entryName, claimedHash) in manifest.Checksums)
        {
            claimedHash.Should().StartWith("sha256:",
                "OCI-style prefix convention must hold across all entries");
            var actualBytes = ReadEntryBytes(ms, entryName);
            var actualHash = "sha256:" + Convert.ToHexString(SHA256.HashData(actualBytes)).ToLowerInvariant();
            actualHash.Should().Be(claimedHash,
                $"manifest checksum for '{entryName}' must match recomputed content hash");
        }
    }

    [Fact]
    public async Task BuildAsync_AuditLogIncludedWhenEntriesPresent()
    {
        using var temp = new TempDataRoot();
        temp.WriteCurrentConfig(SampleConfig);

        var cfg = new FakeConfig("gw-test", null);
        cfg.AddAuditEntry(MakeAudit("alice", "first apply"));
        cfg.AddAuditEntry(MakeAudit("bob", "second apply"));

        var builder = new BackupBuilder(temp.HostOptions, cfg, new ConfigRedactionEngine(), TestRegistry());

        using var ms = new MemoryStream();
        await builder.BuildAsync(ms, "manual", CancellationToken.None);

        var manifest = ReadManifest(ms);
        manifest.AuditEntryCount.Should().Be(2);
        manifest.Checksums.Should().ContainKey("audit.log");

        var auditText = ReadEntryText(ms, "audit.log");
        auditText.Should().Contain("first apply").And.Contain("second apply");
        auditText.Should().Contain("alice").And.Contain("bob");
    }

    [Fact]
    public async Task BuildAsync_EmptyHistoryProducesNoHistoryEntries()
    {
        using var temp = new TempDataRoot();
        temp.WriteCurrentConfig(SampleConfig);

        var builder = new BackupBuilder(temp.HostOptions, new FakeConfig("gw-test", null), new ConfigRedactionEngine(), TestRegistry());

        using var ms = new MemoryStream();
        await builder.BuildAsync(ms, "manual", CancellationToken.None);

        var manifest = ReadManifest(ms);
        manifest.HistoryCount.Should().Be(0);
        manifest.Components.History.Should().BeFalse();

        using var zip = OpenZip(ms);
        zip.Entries.Should().NotContain(e => e.FullName.StartsWith("history/"),
            "no history/ entries when historyCount == 0");
    }

    [Fact]
    public async Task BuildAsync_ExportReasonPropagatesIntoManifest()
    {
        using var temp = new TempDataRoot();
        temp.WriteCurrentConfig(SampleConfig);

        var builder = new BackupBuilder(temp.HostOptions, new FakeConfig("gw-test", null), new ConfigRedactionEngine(), TestRegistry());

        using var ms = new MemoryStream();
        await builder.BuildAsync(ms, "pre-upgrade", CancellationToken.None);

        var manifest = ReadManifest(ms);
        manifest.ExportReason.Should().Be("pre-upgrade",
            "custom reasons (scheduled, pre-upgrade, support-request, automated) must round-trip into the manifest");
    }

    [Fact]
    public async Task BuildAsync_ManifestUsesCamelCaseAndLiteralPunctuation()
    {
        // Wire contract: manifest.json must (1) use camelCase property
        // names so it lines up with the rest of the API, and (2) emit
        // characters like '+' literally so InformationalVersion strings
        // like "1.0.0+abc123" don't display as "1.0.0+abc123" when
        // an operator opens the file. Both broke at one point in M.1c.3
        // bring-up; this test pins them.
        using var temp = new TempDataRoot();
        temp.WriteCurrentConfig(SampleConfig);

        var builder = new BackupBuilder(temp.HostOptions, new FakeConfig("gw-test", null), new ConfigRedactionEngine(), TestRegistry());

        using var ms = new MemoryStream();
        await builder.BuildAsync(ms, "manual", CancellationToken.None);

        var manifestText = ReadEntryText(ms, "manifest.json");

        manifestText.Should().Contain("\"schemaVersion\"", "camelCase, not PascalCase");
        manifestText.Should().Contain("\"backupType\"");
        manifestText.Should().Contain("\"gatewayId\"");
        manifestText.Should().Contain("\"exportReason\"");
        manifestText.Should().NotContain("\"SchemaVersion\"",
            "PascalCase would diverge from the rest of the management API");

        manifestText.Should().NotContain("\\u002B",
            "'+' must render literally; UnsafeRelaxedJsonEscaping is required so " +
            "InformationalVersion strings like '1.0.0+sha…' are readable");
    }

    [Fact]
    public async Task BuildAsync_NoCurrentConfigOnDisk_Throws()
    {
        using var temp = new TempDataRoot();
        // Note: NOT calling WriteCurrentConfig — current.json doesn't exist.

        var builder = new BackupBuilder(temp.HostOptions, new FakeConfig("gw-test", null), new ConfigRedactionEngine(), TestRegistry());

        using var ms = new MemoryStream();
        var act = () => builder.BuildAsync(ms, "manual", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "deliberate hard fail — 'backup of nothing' is operationally meaningless");
    }

    [Fact]
    public async Task BuildAsync_ManifestNotIncludedInItsOwnChecksums()
    {
        // The manifest cannot contain its own SHA-256 (chicken/egg).
        // Verifiers re-compute checksums for the OTHER files only.
        using var temp = new TempDataRoot();
        temp.WriteCurrentConfig(SampleConfig);

        var builder = new BackupBuilder(temp.HostOptions, new FakeConfig("gw-test", null), new ConfigRedactionEngine(), TestRegistry());

        using var ms = new MemoryStream();
        await builder.BuildAsync(ms, "manual", CancellationToken.None);

        var manifest = ReadManifest(ms);
        manifest.Checksums.Should().NotContainKey("manifest.json",
            "manifest.json cannot checksum itself — would be circular");
    }

    [Fact]
    public async Task BuildAsync_UnknownProtocolRules_StillMasksAndSurfacesWarning()
    {
        // Empty registry → no rules for "modbustcp". The schema-aware path falls
        // back to the shared baseline (so the secret is STILL masked) and records
        // a missing-rules provenance warning (ADR-0020 Q-B5).
        using var temp = new TempDataRoot();
        temp.WriteCurrentConfig(SampleConfigWithSecret);

        var builder = new BackupBuilder(
            temp.HostOptions,
            new FakeConfig("gw-test", null),
            new ConfigRedactionEngine(),
            new BundleRedactionRulesRegistry(System.Array.Empty<IBundleRedactionRules>()));

        using var ms = new MemoryStream();
        await builder.BuildAsync(ms, "manual", CancellationToken.None);

        var gatewayJsonInZip = ReadEntryText(ms, "gateway.json");
        gatewayJsonInZip.Should().NotContain("super-secret-value",
            "baseline still masks the secret even when protocol rules are unavailable");

        var manifest = ReadManifest(ms);
        manifest.Redactions["gateway.json"].Should().Contain("sources[0].connection.password");
        manifest.Warnings.Should().NotBeNull();
        manifest.Warnings!.Should().Contain(w => w.Contains("Protocol rules unavailable for 'modbustcp'"),
            "the operator/support engineer must know baseline-only classification was applied");
    }

    [Fact]
    public async Task BuildAsync_MultiProtocolConfig_RealRegistry_RedactsEverySecret_NoWarnings()
    {
        // Full-graph integration: a realistic multi-protocol gateway.json run
        // through the LIVE schema-aware path with the REAL per-protocol rules
        // (wired exactly as the running gateway wires them). Every protocol's
        // secret must be redacted, every non-secret datum preserved, and — since
        // all protocols' rules are registered — no missing-rules warnings.
        using var temp = new TempDataRoot();
        temp.WriteCurrentConfig(MultiProtocolConfig);

        var builder = new BackupBuilder(
            temp.HostOptions,
            new FakeConfig("gw-itest", "Integration Gateway"),
            new ConfigRedactionEngine(),
            RealRegistry());

        using var ms = new MemoryStream();
        await builder.BuildAsync(ms, "manual", CancellationToken.None);

        var gatewayJson = ReadEntryText(ms, "gateway.json");

        // Every secret value is gone...
        gatewayJson.Should().NotContain("mqtt-secret-pw")
            .And.NotContain("opc-client-secret-pw")
            .And.NotContain("opc-server-secret-pw");

        // ...while non-secret data (hosts, endpoints, usernames, cert PATHS) survives.
        gatewayJson.Should().Contain("broker.local")
            .And.Contain("opc.tcp://plc:4840")
            .And.Contain("10.0.0.5")
            .And.Contain("operator")          // opcua-client username (Include)
            .And.Contain("/certs/app.der");   // opcua-server cert store path (Include)

        var manifest = ReadManifest(ms);
        manifest.Redactions["gateway.json"].Should().BeEquivalentTo(new[]
        {
            "sources[1].connection.credentials.password",                 // opcua-client
            "sinks[0].connection.password",                               // mqtt
            "sinks[1].connection.security.credentials[0].password",       // opcua-server
        });

        // No missing-rules warning fires — every configured protocol's rules are
        // registered. (A benign "audit log is empty" warning may be present from
        // the fake config; we only assert the absence of redaction warnings.)
        (manifest.Warnings ?? System.Array.Empty<string>())
            .Should().NotContain(w => w.Contains("Protocol rules unavailable", StringComparison.Ordinal),
                "every configured protocol's rules are registered, so baseline fallback never triggers");
    }

    private static BundleRedactionRulesRegistry RealRegistry()
    {
        // Build the registry from the EXACT same unconditional registration the
        // running gateway uses (Host.BundleRedactionRulesRegistration).
        var services = new ServiceCollection();
        ElpisEdgeConnect.Host.BundleRedactionRulesRegistration.AddBundleRedactionRules(services);
        using var provider = services.BuildServiceProvider();
        return new BundleRedactionRulesRegistry(provider.GetServices<IBundleRedactionRules>());
    }

    [Fact]
    public async Task BuildAsync_SecretShapedValueUnderBenignKey_WarnsButDoesNotStrip()
    {
        // 'customAttr' is not a secret name and not an MQTT known key, so its JWT
        // value survives as INCLUDE (World 2b fail-open). The R-1 detector flags
        // it in the manifest — warn-only, never stripped (ADR-0020 A1.4 #1).
        const string config = """
            {
              "gateway": { "gatewayId": "gw-test", "gatewayName": "T" },
              "sinks": [
                { "instanceId": "mqtt-1", "protocolName": "mqtt",
                  "connection": { "brokerHost": "broker.local",
                    "customAttr": "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ4In0.abcDEF123_-" } }
              ]
            }
            """;
        using var temp = new TempDataRoot();
        temp.WriteCurrentConfig(config);

        var builder = new BackupBuilder(
            temp.HostOptions, new FakeConfig("gw-test", null), new ConfigRedactionEngine(), RealRegistry());

        using var ms = new MemoryStream();
        await builder.BuildAsync(ms, "manual", CancellationToken.None);

        // Warn-only: the JWT is NOT removed (its key name isn't a secret).
        ReadEntryText(ms, "gateway.json").Should().Contain("eyJhbGciOiJIUzI1NiJ9");

        var manifest = ReadManifest(ms);
        manifest.Warnings.Should().NotBeNull();
        manifest.Warnings!.Should().Contain(
            w => w.Contains("JSON Web Token", StringComparison.Ordinal)
              && w.Contains("customAttr", StringComparison.Ordinal),
            "the detector surfaces the secret-shaped value so the operator can verify before sharing");
    }

    // ----- Sample data -------------------------------------------------------

    private const string MultiProtocolConfig = """
        {
          "gateway": { "gatewayId": "gw-itest", "gatewayName": "Integration Gateway", "licenseFile": "license.json" },
          "sources": [
            { "instanceId": "modbus-1", "protocolName": "modbustcp", "deviceId": "dev-modbus",
              "connection": { "host": "10.0.0.5", "port": 5020,
                "tagDefinitions": [ { "name": "spindle", "registerClass": "HoldingRegister", "address": 0 } ] } },
            { "instanceId": "opc-1", "protocolName": "opcua-client", "deviceId": "dev-opc",
              "connection": { "endpointUrl": "opc.tcp://plc:4840", "authMode": "UserName",
                "credentials": { "username": "operator", "password": "opc-client-secret-pw" } } }
          ],
          "sinks": [
            { "instanceId": "mqtt-1", "protocolName": "mqtt",
              "connection": { "brokerHost": "broker.local", "username": "pub", "password": "mqtt-secret-pw" } },
            { "instanceId": "opcsrv-1", "protocolName": "opcua-server",
              "connection": { "endpointUrl": "opc.tcp://0.0.0.0:4840",
                "security": { "mode": "SignAndEncrypt", "applicationCertificatePath": "/certs/app.der",
                  "credentials": [ { "username": "client1", "password": "opc-server-secret-pw" } ] } } }
          ],
          "routes": [
            { "routeId": "r1", "name": "Modbus to MQTT", "sourceInstanceId": "modbus-1", "sinkInstanceIds": ["mqtt-1"] }
          ]
        }
        """;

    private const string SampleConfig = """
        {
          "gateway": { "gatewayId": "gw-test", "gatewayName": "Test Gateway" },
          "sources": [
            { "instanceId": "modbus-1", "protocolName": "modbustcp",
              "connection": { "host": "127.0.0.1", "port": 5020 } }
          ]
        }
        """;

    private const string SampleConfigWithSecret = """
        {
          "gateway": { "gatewayId": "gw-test", "gatewayName": "Test Gateway" },
          "sources": [
            { "instanceId": "modbus-1", "protocolName": "modbustcp",
              "connection": { "host": "127.0.0.1", "port": 5020, "password": "super-secret-value" } }
          ]
        }
        """;

    private static ConfigurationAuditEntry MakeAudit(string actor, string summary) => new()
    {
        Timestamp = DateTime.UtcNow,
        VersionId = new ConfigurationVersionId("v1"),
        Action = ConfigurationAuditAction.Applied,
        Actor = actor,
        Summary = summary,
        PreviousHash = ConfigurationAuditLog.GenesisHash,
    };

    // ----- Helpers -----------------------------------------------------------

    private static BundleRedactionRulesRegistry TestRegistry() =>
        new(new IBundleRedactionRules[] { new FakeModbusRules() });

    /// <summary>
    /// Minimal redaction rules for the "modbustcp" protocol used by the test
    /// configs, so the schema-aware backup path takes the rules-present branch
    /// (no missing-rules warning) — mirroring the running gateway where every
    /// protocol's rules are registered. The test secret "password" is not a
    /// Modbus key, so it falls through to the shared baseline and is masked.
    /// </summary>
    private sealed class FakeModbusRules : IBundleRedactionRules
    {
        public string ProtocolName => "modbustcp";

        public IReadOnlyDictionary<string, BundleTier> KnownKeys { get; } =
            new Dictionary<string, BundleTier>
            {
                ["host"] = BundleTier.Include,
                ["port"] = BundleTier.Include,
            };

        public IReadOnlyDictionary<string, BundleTier> ExtraNameOverrides { get; } =
            new Dictionary<string, BundleTier>();
    }

    private static ZipArchive OpenZip(MemoryStream ms)
    {
        ms.Position = 0;
        return new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true);
    }

    private static BackupManifest ReadManifest(MemoryStream ms)
    {
        using var zip = OpenZip(ms);
        var entry = zip.GetEntry("manifest.json")
            ?? throw new InvalidOperationException("manifest.json not found in zip");
        using var stream = entry.Open();
        // Use the same options the builder serialized with, so the
        // camelCase manifest round-trips back to PascalCase C# props.
        var manifest = JsonSerializer.Deserialize<BackupManifest>(stream, BackupBuilder.ManifestJsonOptions);
        return manifest ?? throw new InvalidOperationException("manifest deserialized to null");
    }

    private static string ReadEntryText(MemoryStream ms, string name)
    {
        using var zip = OpenZip(ms);
        var entry = zip.GetEntry(name) ?? throw new InvalidOperationException($"{name} not found in zip");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static byte[] ReadEntryBytes(MemoryStream ms, string name)
    {
        using var zip = OpenZip(ms);
        var entry = zip.GetEntry(name) ?? throw new InvalidOperationException($"{name} not found in zip");
        using var stream = entry.Open();
        using var sink = new MemoryStream();
        stream.CopyTo(sink);
        return sink.ToArray();
    }

    /// <summary>
    /// Temp directory laid out the same as the gateway's data root so
    /// BackupBuilder can construct a ConfigurationStorageLayout against
    /// it. Cleans up on Dispose.
    /// </summary>
    private sealed class TempDataRoot : IDisposable
    {
        public string Root { get; }
        public HostOptions HostOptions { get; }

        public TempDataRoot()
        {
            Root = Path.Combine(Path.GetTempPath(), "edc-backup-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "config"));
            HostOptions = new HostOptions
            {
                ConfigDirectory = Path.Combine(Root, "config"),
                LicensePath = Path.Combine(Root, "license.json"),
                GatewayIdentityPath = Path.Combine(Root, "identity"),
                DataRoot = Root,
            };
        }

        public void WriteCurrentConfig(string json)
        {
            var path = Path.Combine(Root, "config", "current.json");
            File.WriteAllText(path, json);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Minimal IConfigurationManager fake — implements GetCurrentAsync
    /// (returns a hand-built GatewayConfiguration with the test's
    /// gateway identity) and GetAuditLogAsync + GetHistoryAsync. Other
    /// methods throw — BackupBuilder doesn't call them.
    /// </summary>
    private sealed class FakeConfig : IConfigurationManager
    {
        private readonly List<ConfigurationAuditEntry> _audit = new();
        private readonly List<ConfigurationHistoryEntry> _history = new();
        private readonly string _gatewayId;
        private readonly string? _gatewayName;

        public FakeConfig(string gatewayId, string? gatewayName)
        {
            _gatewayId = gatewayId;
            _gatewayName = gatewayName;
        }

        public void AddAuditEntry(ConfigurationAuditEntry entry) => _audit.Add(entry);

        public ValueTask<GatewayConfiguration> GetCurrentAsync(CancellationToken cancellationToken)
        {
            var cfg = new GatewayConfiguration
            {
                Gateway = new GatewaySettings
                {
                    GatewayId = _gatewayId,
                    GatewayName = _gatewayName ?? _gatewayId,
                },
            };
            return ValueTask.FromResult(cfg);
        }

        public async IAsyncEnumerable<ConfigurationAuditEntry> GetAuditLogAsync(
            bool verifyChain,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var e in _audit)
            {
                yield return e;
                await Task.Yield();
            }
        }

        public Task<IReadOnlyList<ConfigurationHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ConfigurationHistoryEntry>>(_history);

        // ---- unused methods ----
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
