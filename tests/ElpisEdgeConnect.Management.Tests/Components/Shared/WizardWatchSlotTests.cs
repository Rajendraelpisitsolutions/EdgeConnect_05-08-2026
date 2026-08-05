// ============================================================================
// Tests: WizardWatchSlotTests — pins the M.2d.1 v2 §5.4 strict-no-op
// contract (Q1 verdict).
//
// Verified contracts:
//   * Available=false AND debug env-var unset → ZERO DOM.
//   * Available=false AND debug env-var "true" → renders the debug
//     placeholder banner (small Info MudAlert).
//   * Available=false AND debug env-var "TRUE" / " true " → also enabled
//     (case-insensitive, trimmed).
//   * Available=true → renders the pending-M.2c placeholder banner
//     (M.2c will replace the body with the real Live Tag Watch table).
//
// Reference: docs/sessions/2026-05-21-m2d1-shared-primitives-plan-v2.md §5.4
// ============================================================================

using System.Collections.Generic;
using Bunit;
using ElpisEdgeConnect.Management.Components.Shared;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests.Components.Shared;

public sealed class WizardWatchSlotTests : TestContext
{
    public WizardWatchSlotTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Render_AvailableFalse_DebugEnvUnset_RendersZeroDom()
    {
        // Q1 lock — the strict no-op default. M.2d.2/.3 can drop this
        // component into wizard sections without any visual side-effect.
        var cut = RenderComponent<WizardWatchSlot>(parameters => parameters
            .Add(p => p.SourceInstanceId, "src-1")
            .Add(p => p.Available, false)
            .Add(p => p.EnvLookup, _ => null));

        cut.Markup.Trim().Should().BeEmpty(
            "WizardWatchSlot is strict no-op by default; production must render zero DOM");
    }

    [Fact]
    public void Render_AvailableFalse_DebugEnvTrue_RendersDebugPlaceholder()
    {
        var cut = RenderComponent<WizardWatchSlot>(parameters => parameters
            .Add(p => p.SourceInstanceId, "src-1")
            .Add(p => p.TagPaths, (IReadOnlyList<string>)new[] { "Status/RunState", "MachineInfo/Hostname" })
            .Add(p => p.Available, false)
            .Add(p => p.EnvLookup, name => name == "EDGECONNECT_WIZARD_WATCH_PLACEHOLDER" ? "true" : null));

        cut.Find("[data-testid='wizard-watch-slot-debug-placeholder']").Should().NotBeNull();
        cut.Markup.Should().Contain("src-1");
        cut.Markup.Should().Contain("Status/RunState");
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("True")]
    [InlineData(" true ")]
    public void Render_DebugEnv_CaseInsensitiveAndTrimmed(string envValue)
    {
        var cut = RenderComponent<WizardWatchSlot>(parameters => parameters
            .Add(p => p.SourceInstanceId, "src-1")
            .Add(p => p.Available, false)
            .Add(p => p.EnvLookup, name => name == "EDGECONNECT_WIZARD_WATCH_PLACEHOLDER" ? envValue : null));

        cut.FindAll("[data-testid='wizard-watch-slot-debug-placeholder']").Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("false")]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("")]
    public void Render_DebugEnv_NonTrueValues_RemainStrictNoOp(string envValue)
    {
        var cut = RenderComponent<WizardWatchSlot>(parameters => parameters
            .Add(p => p.SourceInstanceId, "src-1")
            .Add(p => p.Available, false)
            .Add(p => p.EnvLookup, name => name == "EDGECONNECT_WIZARD_WATCH_PLACEHOLDER" ? envValue : null));

        cut.Markup.Trim().Should().BeEmpty(
            $"only literal 'true' (case-insensitive) enables the placeholder; got '{envValue}'");
    }

    [Fact]
    public void Render_AvailableTrue_RendersPendingM2cPlaceholder()
    {
        // Available=true is the M.2c-wired branch. M.2d.1 renders an
        // Info banner placeholder; M.2c will replace the body with the
        // real Live Tag Watch table.
        var cut = RenderComponent<WizardWatchSlot>(parameters => parameters
            .Add(p => p.SourceInstanceId, "src-1")
            .Add(p => p.TagPaths, (IReadOnlyList<string>)new[] { "Status/RunState" })
            .Add(p => p.Available, true));

        cut.Find("[data-testid='wizard-watch-slot-pending-m2c']").Should().NotBeNull();
        cut.Markup.Should().Contain("src-1");
        cut.Markup.Should().Contain("Status/RunState");
    }
}
