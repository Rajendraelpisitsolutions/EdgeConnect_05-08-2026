// ============================================================================
// Tests: MelsecDiagnosticsPanel render — consumes the melsec-diagnostics
// endpoint and renders the observational snapshot (route header, scan blocks,
// last end code, affected tags, per-tag quality, latency, breaker), or a typed
// "unavailable" state. Verifies the panel exposes no raw request/response
// payloads. A fake handler returns available / unavailable DTOs by source id.
// ============================================================================

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using ElpisEdgeConnect.Management.Components.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests.Components;

public sealed class MelsecDiagnosticsPanelTests : TestContext
{
    public MelsecDiagnosticsPanelTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(new HttpClient(new CannedDiagnosticsHandler())
        {
            BaseAddress = new Uri("http://localhost"),
        });
    }

    private sealed class CannedDiagnosticsHandler : HttpMessageHandler
    {
        // lastResult is the stable enum NAME — the panel parses it with a
        // string-enum converter, matching how the API emits it.
        internal const string AvailableJson = """
        {
          "available": true,
          "snapshot": {
            "instanceId": "melsec-live",
            "profileDisplayName": "Modern (iQ-R/Q/L)",
            "route": { "networkNo": 0, "pcNo": 255, "requestDestModuleIoNo": 1023, "requestDestModuleStationNo": 0 },
            "scanBlocks": [
              { "deviceSymbol": "D", "headDeviceNumber": 200, "wordCount": 2, "scanRateMs": 1000,
                "tagNames": ["total_cycles"], "lastResult": "ProtocolError", "lastEndCode": 49238, "lastMessage": "end code 0xC056" }
            ],
            "lastEndCode": 49238,
            "lastEndCodeDescription": "read address + points exceed the device range",
            "affectedTags": ["total_cycles"],
            "tagQuality": [ { "tagName": "total_cycles", "address": "D200", "quality": "Bad", "reason": "end code 0xC056" } ],
            "lastRequestLatencyMs": 21,
            "connected": true,
            "breakerState": "Closed",
            "consecutiveFailures": 0
          }
        }
        """;

        internal const string UnavailableJson = """
        { "available": false, "reason": "Source 'melsec-stopped' is not running (missing, stopped, or unlicensed).", "snapshot": null }
        """;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var body = path.Contains("melsec-live", StringComparison.Ordinal) ? AvailableJson : UnavailableJson;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private IRenderedFragment RenderPanel(string sourceId) => Render(builder =>
    {
        builder.OpenComponent<MudPopoverProvider>(0);
        builder.CloseComponent();
        builder.OpenComponent<MelsecDiagnosticsPanel>(1);
        builder.AddAttribute(2, "SourceInstanceId", sourceId);
        builder.CloseComponent();
    });

    [Fact]
    public void Available_snapshot_renders_all_fields()
    {
        var cut = RenderPanel("melsec-live");

        cut.FindAll("[data-testid='melsec-diagnostics-unavailable']").Should().BeEmpty();

        // Route header (rendered as hex).
        cut.Find("[data-testid='melsec-diagnostics-route']").TextContent
            .Should().Contain("0x00").And.Contain("0xFF").And.Contain("0x03FF");

        // Last end code + description.
        cut.Find("[data-testid='melsec-diagnostics-endcode']").TextContent
            .Should().Contain("0xC056").And.Contain("exceed the device range");

        // Affected tags + scan blocks + per-tag quality.
        cut.Find("[data-testid='melsec-diagnostics-affected']").TextContent.Should().Contain("total_cycles");
        cut.Find("[data-testid='melsec-diagnostics-blocks']").TextContent
            .Should().Contain("D200").And.Contain("ProtocolError");
        cut.Find("[data-testid='melsec-diagnostics-quality']").TextContent
            .Should().Contain("total_cycles").And.Contain("Bad");

        // Latency + breaker.
        cut.Markup.Should().Contain("21 ms").And.Contain("Closed");
    }

    [Fact]
    public void Header_shows_profile_pill_and_honest_pending_line()
    {
        var cut = RenderPanel("melsec-live");

        cut.Find("[data-testid='melsec-diagnostics-profile']").TextContent
            .Should().Contain("Modern (iQ-R/Q/L)");
        cut.Markup.Should().Contain("MC 3E binary · TCP").And.Contain("read-only");
        cut.Markup.Should().Contain("Hardware field qualification pending.");
        cut.Markup.Should().NotContain("Certified");
    }

    [Fact]
    public void Unavailable_source_renders_typed_unavailable_state()
    {
        var cut = RenderPanel("melsec-stopped");

        cut.Find("[data-testid='melsec-diagnostics-unavailable']").TextContent
            .Should().Contain("not running");
        cut.FindAll("[data-testid='melsec-diagnostics-blocks']").Should().BeEmpty();
    }

    [Fact]
    public void Panel_markup_carries_no_raw_payload_labels()
    {
        // Contract guard at the render layer: the live diagnostics panel never
        // renders raw request/response payloads (those are probe/Advanced-only).
        var cut = RenderPanel("melsec-live");

        cut.Markup.Should().NotContain("Raw");
        cut.Markup.Should().NotContain("rawWordsHex");
    }
}
