// ============================================================================
// Tests: AddMelsecSource render — the MELSEC wizard page. Exercised in Edit
// mode (which skips the Add-mode /api/v1/config fetch) so rendering needs no
// HTTP; the probe path uses a fake handler that returns a canned test-read
// result. Covers the render-specific behaviour not already pinned by the
// MelsecSourceWizardModel unit tests: fixed protocol summary, unsupported-mode
// block, valid-row status, and the read-probe result UI (tag results, planned
// blocks, skipped-invalid tags, and raw-payload-only-behind-Advanced).
// ============================================================================

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Components.Pages.SourceWizards;
using ElpisEdgeConnect.Management.Wizards;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests.Components.SourceWizards;

public sealed class AddMelsecSourceTests : TestContext
{
    public AddMelsecSourceTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(new HttpClient(new CannedProbeHandler())
        {
            BaseAddress = new Uri("http://localhost"),
        });
    }

    // Returns a canned MELSEC test-read result so the probe UI has data to
    // render. The result deliberately carries a "Good" tag (with raw bytes), a
    // planned block, and a skipped-invalid tag.
    private sealed class CannedProbeHandler : HttpMessageHandler
    {
        internal const string ReadJson = """
        {
          "probeId": "melsecprobe-1",
          "success": true,
          "elapsedMs": 7,
          "outcome": "read-complete",
          "message": "Read 1/1 tags; 1 skipped as invalid.",
          "tagResults": [
            { "name": "cycles", "address": "D100", "quality": "Good", "value": "42", "rawWordsHex": "2A00" }
          ],
          "blocks": [
            { "device": "D", "headDeviceNumber": 100, "wordCount": 1, "tagNames": ["cycles"] }
          ],
          "skipped": [
            { "name": "timer1", "code": "MELSEC.DEVICE_NOT_IMPLEMENTED", "message": "Timer device 'T' is not implemented." }
          ]
        }
        """;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var body = path.EndsWith("/probe/melsec/test-read", StringComparison.Ordinal)
                ? ReadJson
                : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private static SourceInstanceConfig ValidMelsecConfig()
    {
        var model = new MelsecSourceWizardModel { InstanceId = "melsec-1", DeviceName = "Press 2", Host = "10.0.0.5", Port = 5007 };
        model.Tags.Add(new MelsecTagWizardRow { Name = "cycles", Address = "D100", Datatype = "Int16", ScanRateMs = 1000 });
        return model.BuildSourceInstance();
    }

    // Render the wizard (Edit mode) wrapped in a MudPopoverProvider so the tag
    // table's MudTooltip / MudSelect have their required provider in the tree.
    private IRenderedFragment RenderEdit(SourceInstanceConfig cfg) => Render(builder =>
    {
        builder.OpenComponent<MudPopoverProvider>(0);
        builder.CloseComponent();
        builder.OpenComponent<AddMelsecSource>(1);
        builder.AddAttribute(2, "EditMode", EditModeContext.Edit(cfg.InstanceId, "v1"));
        builder.AddAttribute(3, "HydratedConfig", cfg);
        builder.CloseComponent();
    });

    [Fact]
    public void Renders_profile_tiles_from_the_selectable_registry_set()
    {
        var cut = RenderEdit(ValidMelsecConfig());

        // Modern is always selectable; the iQ-F tile appears exactly when the
        // registry gate bit is flipped (A-2O flip-last rule).
        cut.FindAll("[data-testid='melsec-profile-tile-Modern']").Should().NotBeEmpty();
        var iqfTiles = cut.FindAll("[data-testid='melsec-profile-tile-IqF']");
        if (ElpisEdgeConnect.Sources.Melsec.Profiles.MelsecProfiles.IqF.IsOperatorSelectable)
        {
            iqfTiles.Should().NotBeEmpty();
        }
        else
        {
            iqfTiles.Should().BeEmpty();
        }
        cut.FindAll("[data-testid='melsec-profile-helper']").Should().NotBeEmpty();
        cut.Markup.Should().Contain("Modern iQ-R/Q/L selected");
    }

    [Fact]
    public void IqF_hydrated_config_marks_ZR_invalid_and_octal_X10_valid()
    {
        // A-2O: an iQ-F source round-trips through hydrate; the tag table then
        // validates against the iQ-F profile — ZR flagged, octal X10 accepted.
        var model = new MelsecSourceWizardModel { InstanceId = "melsec-fx5", Host = "10.0.0.5", Port = 5007, DeviceProfile = "IqF" };
        model.Tags.Add(new MelsecTagWizardRow { Name = "recipe", Address = "ZR16384", Datatype = "Int16", ScanRateMs = 1000 });
        model.Tags.Add(new MelsecTagWizardRow { Name = "input_a", Address = "X10", Datatype = "Bool", ScanRateMs = 500 });

        var cut = RenderEdit(model.BuildSourceInstance());

        cut.Markup.Should().Contain("iQ-F / FX5 selected");
        cut.Markup.Should().Contain("✗ Error");           // the ZR row
        cut.Markup.Should().Contain("✓ Valid");            // the X10 row
        cut.Markup.Should().Contain("recognized but not supported");
    }

    [Fact]
    public void Renders_fixed_protocol_summary_and_no_mode_dropdowns()
    {
        var cut = RenderEdit(ValidMelsecConfig());

        cut.FindAll("[data-testid='melsec-protocol-summary']").Should().NotBeEmpty();
        cut.Markup.Should().Contain("MC 3E binary over TCP, read-only");
        // Slice-1 fixed mode: no unsupported-mode banner for a supported config.
        cut.FindAll("[data-testid='melsec-mode-unsupported']").Should().BeEmpty();
    }

    [Fact]
    public void Valid_ZR_hex_row_renders_as_valid()
    {
        var model = new MelsecSourceWizardModel { InstanceId = "melsec-zr", Host = "10.0.0.5", Port = 5007 };
        model.Tags.Add(new MelsecTagWizardRow { Name = "recipe", Address = "ZR16384", Datatype = "Int16", ScanRateMs = 1000 });

        var cut = RenderEdit(model.BuildSourceInstance());

        cut.Markup.Should().Contain("✓ Valid");
        cut.Markup.Should().NotContain("✗ Error");
    }

    [Fact]
    public void Unsupported_hydrated_mode_shows_block_and_reset()
    {
        // Hand-edit an unsupported transport, then round-trip through hydrate.
        var model = new MelsecSourceWizardModel { InstanceId = "melsec-legacy", Host = "10.0.0.5", Port = 5007 };
        model.TransportProtocol = "Udp";
        var cfg = model.BuildSourceInstance();

        var cut = RenderEdit(cfg);

        cut.FindAll("[data-testid='melsec-mode-unsupported']").Should().NotBeEmpty();
        cut.FindAll("[data-testid='melsec-reset-mode-btn']").Should().NotBeEmpty();
        // The validation banner surfaces the human-readable "not implemented"
        // message (the CONFIG_MODE_NOT_IMPLEMENTED code is pinned at the model level).
        cut.Markup.Should().Contain("not implemented");
    }

    [Fact]
    public void DeviceNotImplemented_address_surfaces_in_banner()
    {
        var model = new MelsecSourceWizardModel { InstanceId = "melsec-t", Host = "10.0.0.5", Port = 5007 };
        model.Tags.Add(new MelsecTagWizardRow { Name = "t1", Address = "L5", Datatype = "Int16", ScanRateMs = 1000 });

        var cut = RenderEdit(model.BuildSourceInstance());

        // The row is invalid and the parser's device-not-implemented message
        // surfaces (the DEVICE_NOT_IMPLEMENTED code is pinned at the model level).
        cut.Markup.Should().Contain("✗ Error");
        cut.Markup.Should().Contain("recognized but not supported");
    }

    [Fact]
    public void ReadAll_renders_tag_results_planned_blocks_and_skipped()
    {
        var cut = RenderEdit(ValidMelsecConfig());

        cut.Find("[data-testid='melsec-test-read-all-btn']").Click();

        cut.FindAll("[data-testid='melsec-read-tag-results']").Should().NotBeEmpty();
        cut.FindAll("[data-testid='melsec-planned-blocks']").Should().NotBeEmpty();
        cut.FindAll("[data-testid='melsec-skipped-tags']").Should().NotBeEmpty();
        cut.Markup.Should().Contain("cycles");
        cut.Markup.Should().Contain("timer1");
        cut.Markup.Should().Contain("MELSEC.DEVICE_NOT_IMPLEMENTED");
    }

    [Fact]
    public void Raw_payload_hidden_until_advanced_include_raw_enabled()
    {
        var cut = RenderEdit(ValidMelsecConfig());
        cut.Find("[data-testid='melsec-test-read-all-btn']").Click();

        // Default: raw column is not shown even though the result carries rawWordsHex.
        cut.Markup.Should().NotContain("Raw (hex)");

        // Enable the Advanced include-raw switch (MudSwitch splats the
        // data-testid onto the checkbox input itself).
        cut.Find("[data-testid='melsec-include-raw']").Change(true);

        cut.Markup.Should().Contain("Raw (hex)");
        cut.Markup.Should().Contain("2A00");
    }
}
