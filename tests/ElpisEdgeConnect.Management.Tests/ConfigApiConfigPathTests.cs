// ============================================================================
// Tests: ConfigApiConfigPathTests — pins the M.2b.6.2 §3.B surface that
//        GET /api/v1/config/version returns the absolute current.json
//        path plus the name of the env var (if any) that overrode the
//        default. The endpoint body was extracted into a static helper
//        (ConfigApi.BuildCurrentConfigVersionDtoAsync) so we can drive
//        it without spinning up a WebApplicationFactory; the env-var
//        lookup is passed as a delegate so tests stay deterministic
//        and don't race other tests through the process environment.
// Reference: docs/sessions/2026-05-20-mp2b62-smoke-driven-ux-hardening-plan.md §3.B
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Host;
using ElpisEdgeConnect.Management.Api;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class ConfigApiConfigPathTests
{
    [Fact]
    public async Task BuildCurrentConfigVersionDtoAsync_NoEnvVar_ReturnsDefaultOverrideNull()
    {
        var temp = NewTempDataRoot();
        try
        {
            var host = MakeHostOptions(temp);
            var mgr = new VersionFakeConfigManager(MakeEmptyConfig(), "v-1");

            var dto = await ConfigApi.BuildCurrentConfigVersionDtoAsync(
                mgr, host, _ => null, CancellationToken.None);

            dto.ConfigPath.Should().Be(Path.Combine(temp, "config", "current.json"));
            dto.Override.Should().BeNull();
            dto.VersionId.Should().Be("v-1");
        }
        finally { try { Directory.Delete(temp, recursive: true); } catch { } }
    }

    [Fact]
    public async Task BuildCurrentConfigVersionDtoAsync_EnvVarSet_SurfacesOverrideName()
    {
        var temp = NewTempDataRoot();
        try
        {
            var host = MakeHostOptions(temp);
            var mgr = new VersionFakeConfigManager(MakeEmptyConfig(), "v-2");

            // Simulate an operator setting EDGECONNECT_DATA_ROOT — the
            // helper sees the lookup return a non-null value and emits
            // the env-var name in Override.
            var dto = await ConfigApi.BuildCurrentConfigVersionDtoAsync(
                mgr, host,
                envLookup: name => name == "EDGECONNECT_DATA_ROOT" ? temp : null,
                CancellationToken.None);

            dto.ConfigPath.Should().Be(Path.Combine(temp, "config", "current.json"));
            dto.Override.Should().Be("EDGECONNECT_DATA_ROOT");
        }
        finally { try { Directory.Delete(temp, recursive: true); } catch { } }
    }

    [Fact]
    public async Task BuildCurrentConfigVersionDtoAsync_OtherEnvVarSet_DoesNotMisattribute()
    {
        var temp = NewTempDataRoot();
        try
        {
            var host = MakeHostOptions(temp);
            var mgr = new VersionFakeConfigManager(MakeEmptyConfig(), "v-3");

            // Chip 5 resolution: EDGECONNECT_CONFIG_DIR is no longer
            // recognised at all (env-var read removed in
            // EdgeConnectComposition; deprecation warning logged at
            // startup if operators still set it). This test continues
            // to pin the version endpoint NOT reporting it as an
            // override — same expected output, stronger underlying
            // guarantee.
            var dto = await ConfigApi.BuildCurrentConfigVersionDtoAsync(
                mgr, host,
                envLookup: name => name == "EDGECONNECT_CONFIG_DIR" ? "/tmp/x" : null,
                CancellationToken.None);

            dto.Override.Should().BeNull();
        }
        finally { try { Directory.Delete(temp, recursive: true); } catch { } }
    }

    // ─── helpers ─────────────────────────────────────────────────────────

    private static string NewTempDataRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ec-cfgpath-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "config"));
        return dir;
    }

    private static HostOptions MakeHostOptions(string dataRoot) => new()
    {
        ConfigDirectory = Path.Combine(dataRoot, "config"),
        LicensePath = Path.Combine(dataRoot, "license.json"),
        GatewayIdentityPath = Path.Combine(dataRoot, "identity"),
        DataRoot = dataRoot,
    };

    private static GatewayConfiguration MakeEmptyConfig() => new()
    {
        Gateway = new GatewaySettings { GatewayId = "gw-test", GatewayName = "Test" },
        Sources = Array.Empty<SourceInstanceConfig>(),
        Sinks = Array.Empty<SinkInstanceConfig>(),
        Routes = Array.Empty<RouteConfig>(),
    };

    /// <summary>
    /// Minimal IConfigurationManager fake — only the two methods
    /// BuildCurrentConfigVersionDtoAsync actually touches return
    /// real values; everything else throws so an accidental
    /// reliance during a refactor is loud.
    /// </summary>
    private sealed class VersionFakeConfigManager : IConfigurationManager
    {
        private readonly GatewayConfiguration _current;

        public VersionFakeConfigManager(GatewayConfiguration current, string versionId)
        {
            _current = current;
            CurrentVersionId = new ConfigurationVersionId(versionId);
        }

        public ConfigurationVersionId CurrentVersionId { get; }

        public ValueTask<GatewayConfiguration> GetCurrentAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(_current);

        public Task InitializeAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<DraftId> CreateDraftAsync(GatewayConfiguration draft, string? actor, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<GatewayConfiguration?> GetDraftAsync(DraftId draftId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<DraftId>> ListDraftsAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ValidationResult> ValidateDraftAsync(DraftId draftId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ConfigurationApplyResult> ApplyDraftAsync(DraftId draftId, string? actor, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task DiscardDraftAsync(DraftId draftId, string? actor, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ConfigurationApplyResult> RollbackAsync(ConfigurationVersionId targetVersionId, string? actor, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<ConfigurationHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public IAsyncEnumerable<ConfigurationAuditEntry> GetAuditLogAsync(bool verifyChain, CancellationToken cancellationToken) => throw new NotImplementedException();
        public ValueTask<ConfigurationAuditEntry> AppendRuntimeFaultAsync(ElpisEdgeConnect.Core.Diagnostics.ConfigurationFault fault, CancellationToken cancellationToken) => throw new NotImplementedException();
        public event EventHandler<ConfigurationChangeEventArgs>? CurrentChanged { add { } remove { } }
    }
}
