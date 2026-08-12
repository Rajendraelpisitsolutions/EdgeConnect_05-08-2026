// ============================================================================
// Tests: Brother HTTP parity — legacy BrotherHttpDataSource (oracle) vs.
//        new BrotherHttpSourceAdapter consuming the SAME response bytes
//        from BrotherHttpTestServer. The legacy side flows through
//        LegacyCanonicalMapper to produce a canonical-point set; the new
//        side flows through the adapter's BuildPoints. Asserted relation
//        is "subset" — every path the legacy oracle emits, the new
//        adapter emits with the same value. Two documented divergences
//        (MachineInfo/StatusCode + Tools/Magazine/{slot}/*) are
//        new-adapter-only canonical extensions; legacy DTO doesn't carry
//        the underlying data, so the oracle skips them by design.
// Reference: docs/sessions/2026-05-21-mp24-brother-http-plan-v3.md §7
// ============================================================================

using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Configuration;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.DataSources;
using ElpisEdgeConnect.Sources.BrotherHttp.Tests.Parity;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.BrotherHttp.Tests.Parity;

public sealed class ParityTests
{
    [Theory]
    [InlineData("running")]
    [InlineData("idle")]
    [InlineData("alarm")]
    [InlineData("standby")]
    [InlineData("maintenance-overdue")]
    public async Task LegacyOracle_And_NewAdapter_AgreeOnEveryLegacyEmittedPath(string scenario)
    {
        var (oracle, newByPath) = await RunParityAsync(scenario);

        // Sanity guard: if the legacy oracle is empty, the parity assertion
        // below is vacuous. Pin a floor so a regression in either the test
        // server, legacy DTO, or the mapper surfaces as a failure rather
        // than a silent pass.
        oracle.Should().NotBeEmpty(
            "legacy oracle must emit canonical points for a healthy scenario — " +
            "an empty oracle means the test server, legacy parser, or mapper has regressed");
        newByPath.Should().NotBeEmpty(
            "new adapter must emit canonical points for a healthy scenario");

        AssertSubsetParity(oracle, newByPath);
    }

    [Fact]
    public async Task NewAdapter_OfflineEmpty_AllEndpoints404_StartAsync_DoesNotThrow_TransitionsToRunning()
    {
        // ADR-0003 fail-soft: when every endpoint 404s (including HTTPD_MCNINFO),
        // StartAsync must NOT throw — it must log a warning, transition to Running,
        // and let the polling loop retry. Throwing would crash the host.
        // The adapter will fault after FaultThresholdConsecutiveFailures poll failures.
        var samplesDir = LocateSamplesDir("offline-empty");
        using var server = new BrotherHttpTestServer(samplesDir);

        var newConfig = BuildAdapterConfig(server.BaseUrl);
        var api = new BrotherHttpHttpApi(
            new DirectHttpClientFactory(),
            server.BaseUrl,
            timeoutSeconds: 5,
            instanceId: "brother-parity",
            logger: NullLogger.Instance);
        var adapter = new BrotherHttpSourceAdapter("brother-parity", api, NullLogger.Instance);
        await adapter.InitializeAsync(newConfig, CancellationToken.None);

        var act = async () => await adapter.StartAsync(CancellationToken.None);
        await act.Should().NotThrowAsync(
            "ADR-0003: initial probe failure must not crash the host; polling retries instead");
        adapter.State.Should().Be(AdapterState.Running);
    }

    [Fact]
    public async Task LegacyOracle_And_NewAdapter_Agree_OfflinePartial_SingleEndpointMissing()
    {
        // The probe endpoint (HTTPD_MCNINFO) and four others respond with
        // the same payloads as the "running" scenario; only ATC_TOOLS
        // 404s. The adapter must NOT short-circuit the whole poll cycle
        // when a single non-probe endpoint fails — it must continue to
        // harvest the available payloads (machine info, cycle-time,
        // counters, alarms, maintenance) and the subset-parity assertion
        // against the legacy oracle must continue to hold.
        var (oracle, newByPath) = await RunParityAsync("offline-partial");

        // Floor: with five healthy endpoints the oracle is substantially
        // populated relative to the unconditional baseline.
        oracle.Count.Should().BeGreaterThan(2,
            "five healthy endpoints worth of canonical points must reach the oracle " +
            "(only ATC_TOOLS should be missing)");
        newByPath.Should().NotBeEmpty();

        AssertSubsetParity(oracle, newByPath);

        // Sanity: confirm the missing endpoint truly dropped its contribution
        // on both sides — neither emits ToolsActiveNumber (legacy oracle
        // condition: data.ToolInfo.CurrentToolNumber > 0). If a regression
        // ever silently restored it on one side, subset parity might still
        // pass — pin the absence explicitly.
        oracle.Should().NotContainKey(BrotherTagMap.ToolsActiveNumber.TagPath,
            "ATC_TOOLS is 404 so the legacy oracle must not emit ToolsActiveNumber");
        newByPath.Should().NotContainKey(BrotherTagMap.ToolsActiveNumber.TagPath,
            "ATC_TOOLS is 404 so the new adapter must not emit ToolsActiveNumber");
    }

    // ─── helpers ─────────────────────────────────────────────────────────

    private static async Task<(IReadOnlyDictionary<string, object?> Oracle,
                                IDictionary<string, object?> NewByPath)>
        RunParityAsync(string scenario)
    {
        var samplesDir = LocateSamplesDir(scenario);
        using var server = new BrotherHttpTestServer(samplesDir);

        // ── Run the legacy oracle path. ──
        var legacyConfig = new MachineConfig
        {
            MachineId = "Brother-Parity",
            MachineName = "Brother Parity Test",
            DeviceId = "Brother-Parity",
            DeviceName = "Brother Parity Test",
            DataSourceType = DataSourceType.BrotherHttp,
            BrotherHttp = new BrotherHttpSettings
            {
                BaseUrl = server.BaseUrl,
                TimeoutSeconds = 5,
            },
            PollIntervalMs = 3000,
            Tags = new System.Collections.Generic.List<string>(),
        };
        using var legacy = new BrotherHttpDataSource(legacyConfig, NullLogger.Instance);
        var legacyDto = await legacy.CollectDataAsync(CancellationToken.None);
        var oracle = LegacyCanonicalMapper.Map(legacyDto);

        // ── Run the new adapter path against the same test server. ──
        var newConfig = BuildAdapterConfig(server.BaseUrl);
        var api = new BrotherHttpHttpApi(
            new DirectHttpClientFactory(),
            server.BaseUrl,
            timeoutSeconds: 5,
            instanceId: "brother-parity",
            logger: NullLogger.Instance);
        var adapter = new BrotherHttpSourceAdapter("brother-parity", api, NullLogger.Instance);
        await adapter.InitializeAsync(newConfig, CancellationToken.None);
        await adapter.StartAsync(CancellationToken.None);
        var newPoints = await adapter.PollAsync(CancellationToken.None);
        var newByPath = newPoints.ToDictionary(p => p.TagPath, p => (object?)p.Value);

        return (oracle, newByPath);
    }

    private static BrotherHttpSourceConfiguration BuildAdapterConfig(string baseUrl) => new()
    {
        InstanceId = "brother-parity",
        ProtocolName = BrotherHttpSourceConfiguration.ProtocolNameConstant,
        DisplayName = "Brother Parity Test",
        Enabled = true,
        PollIntervalMs = 3000,
        DeviceId = "Brother-Parity",
        DeviceName = "Brother Parity Test",
        DeviceClass = "cnc",
        Tags = System.Array.Empty<string>(),
        BaseUrl = baseUrl,
        TimeoutSeconds = 5,
    };

    private static void AssertSubsetParity(
        IReadOnlyDictionary<string, object?> oracle,
        IDictionary<string, object?> newByPath)
    {
        // Parity assertion: subset (legacy ⊆ new). Documented divergences
        // (MachineInfo/StatusCode + slot-keyed Tools/Magazine/*) are not
        // in the oracle, so they don't enter this comparison; the new
        // adapter is free to emit them.
        foreach (var (tagPath, expectedValue) in oracle)
        {
            newByPath.Should().ContainKey(tagPath,
                $"new adapter must emit '{tagPath}' (legacy oracle emits it)");
            newByPath[tagPath].Should().BeEquivalentTo(expectedValue,
                $"value mismatch at '{tagPath}' between legacy oracle and new adapter");
        }
    }

    private static string LocateSamplesDir(string scenario)
    {
        var dir = Path.Combine(System.AppContext.BaseDirectory, "Parity", "Samples", scenario);
        if (!Directory.Exists(dir))
        {
            throw new DirectoryNotFoundException(
                $"Samples directory not found for scenario '{scenario}'. Expected: {dir}. " +
                "Ensure csproj has <Content Include=\"Parity\\Samples\\**\\*.txt\" CopyToOutputDirectory=\"PreserveNewest\" />.");
        }
        return dir;
    }

    private sealed class DirectHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
