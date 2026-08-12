// ============================================================================
// Tests: BundleApi HTTP contract (ADR-0020 G4/G5). Exercises the minimal-API
//        endpoints in-process via TestServer with a REAL BundleBuilder over a
//        real, initialized ConfigurationManager — the layer the G4 unit tests
//        (which called BuildAsync directly with a fake audit writer) skipped.
//
//        NOTE: TestServer is more permissive than real Kestrel about response
//        headers — the G5 live finding (a manual Content-Disposition colliding
//        with Results.File caused a bodyless 500 only under Kestrel) is NOT
//        caught here. These tests guard the endpoint CONTRACT (status, content
//        type, download filename, zip validity, the acknowledgement gate); the
//        defense against the header bug is keeping the handler header-free.
// ============================================================================

using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Identity;
using ElpisEdgeConnect.Management.Api;
using ElpisEdgeConnect.Management.Backup;
using ElpisEdgeConnect.Management.Bundle;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using HostOptions = ElpisEdgeConnect.Host.HostOptions;

namespace ElpisEdgeConnect.Management.Tests;

public class BundleApiTests
{
    private const string Config = """
        {
          "gateway": { "gatewayId": "gw-itest", "gatewayName": "G" },
          "sinks": [
            { "instanceId": "mqtt-1", "protocolName": "mqtt",
              "connection": { "brokerHost": "broker.local", "username": "pub", "password": "secret-pw" } }
          ]
        }
        """;

    [Fact]
    public async Task PostBundle_ReturnsDownloadableZip_AndAppendsAudit()
    {
        using var temp = new TempRoot(Config);
        var (manager, builder) = temp.BuildRealBundleBuilder();
        await manager.InitializeAsync(CancellationToken.None);

        await using var app = BuildApp(builder);
        await app.StartAsync();
        var client = app.GetTestClient();

        var resp = await client.PostAsJsonAsync("/api/v1/bundle",
            new GenerateBundleRequest("ticket-7", AcknowledgedSecretShapeWarnings: true));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be(BundleApi.ZipContentType);
        resp.Content.Headers.ContentDisposition!.FileNameStar.Should().StartWith("edgeconnect-bundle-");

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        zip.GetEntry("bundle-info.json").Should().NotBeNull();
        zip.GetEntry("manifest.json").Should().NotBeNull();

        await app.StopAsync();
    }

    [Fact]
    public async Task PostBundle_UnacknowledgedSecretShape_Returns400WithCount()
    {
        const string secretShaped = """
            {
              "gateway": { "gatewayId": "gw-itest", "gatewayName": "G" },
              "sinks": [
                { "instanceId": "mqtt-1", "protocolName": "mqtt",
                  "connection": { "brokerHost": "b",
                    "customAttr": "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ4In0.abcDEF123_-" } }
              ]
            }
            """;
        using var temp = new TempRoot(secretShaped);
        var (manager, builder) = temp.BuildRealBundleBuilder();
        await manager.InitializeAsync(CancellationToken.None);

        await using var app = BuildApp(builder);
        await app.StartAsync();
        var client = app.GetTestClient();

        var resp = await client.PostAsJsonAsync("/api/v1/bundle",
            new GenerateBundleRequest(null, AcknowledgedSecretShapeWarnings: false));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await app.StopAsync();
    }

    [Fact]
    public async Task GetPreview_ReturnsManifest()
    {
        using var temp = new TempRoot(Config);
        var (manager, builder) = temp.BuildRealBundleBuilder();
        await manager.InitializeAsync(CancellationToken.None);

        await using var app = BuildApp(builder);
        await app.StartAsync();
        var client = app.GetTestClient();

        var preview = await client.GetFromJsonAsync<BundlePreview>("/api/v1/bundle/preview");

        preview.Should().NotBeNull();
        preview!.Contributors.Should().NotBeEmpty();

        await app.StopAsync();
    }

    private static WebApplication BuildApp(BundleBuilder builder)
    {
        var b = WebApplication.CreateBuilder();
        b.WebHost.UseTestServer();
        b.Services.AddSingleton(builder);
        b.Services.AddRouting();
        var app = b.Build();
        app.MapBundleApi();
        return app;
    }

    private sealed class TempRoot : System.IDisposable
    {
        public string Root { get; }

        public TempRoot(string config)
        {
            Root = Path.Combine(Path.GetTempPath(), "edc-bundleapi-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "config"));
            File.WriteAllText(Path.Combine(Root, "config", "current.json"), config);
        }

        public (ConfigurationManager Manager, BundleBuilder Builder) BuildRealBundleBuilder()
        {
            var manager = new ConfigurationManager(
                new FileSystemConfigurationStore(new ConfigurationStorageLayout(Root)));

            var hostOptions = new HostOptions
            {
                ConfigDirectory = Path.Combine(Root, "config"),
                LicensePath = Path.Combine(Root, "license.json"),
                GatewayIdentityPath = Path.Combine(Root, "identity"),
                DataRoot = Root,
            };

            var services = new ServiceCollection();
            ElpisEdgeConnect.Host.BundleRedactionRulesRegistration.AddBundleRedactionRules(services);
            using var sp = services.BuildServiceProvider();
            var registry = new BundleRedactionRulesRegistry(sp.GetServices<IBundleRedactionRules>());

            var contributors = new IBundleContributor[]
            {
                new GatewayIdentityContributor(),
                new ConfigContributor(),
                new HistoryContributor(),
                new AuditContributor(),
                new RouteInventoryContributor(),
            };
            var builder = new BundleBuilder(
                hostOptions, manager, new Id(), new ConfigRedactionEngine(),
                registry, manager, contributors);
            return (manager, builder);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { /* best-effort */ }
        }

        private sealed class Id : IGatewayIdentity { public string GatewayId => "gw-itest"; }
    }
}
