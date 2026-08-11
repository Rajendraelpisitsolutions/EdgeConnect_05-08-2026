// ============================================================================
// Tests: WizardValidationBannerTests — pins the M.2d.1 v2 §5.3 surface
// extended in M.2d.4 with the scroll-to-field JS interop.
//
// Verified contracts:
//   * Null or empty Messages → zero DOM (no banner rendered).
//   * Single message → inline rendering.
//   * Multiple messages → bullet list.
//   * Severity routes to the corresponding MudAlert class.
//   * OnMessageClick fires when a message WITH FieldAnchor is clicked.
//   * OnMessageClick does NOT fire when a message WITHOUT FieldAnchor is
//     clicked (no anchor → nothing to scroll to → no callback).
//   * M.2d.4: clicking a message WITH FieldAnchor invokes the JS interop
//     wizardValidation.scrollToFieldAnchor with that selector.
//   * M.2d.4: clicking a message WITHOUT FieldAnchor does NOT invoke
//     the JS interop.
//
// Reference: docs/decisions/0015-wizard-contract.md Rule 5
// ============================================================================

using System.Collections.Generic;
using Bunit;
using ElpisEdgeConnect.Management.Components.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests.Components.Shared;

public sealed class WizardValidationBannerTests : TestContext
{
    public WizardValidationBannerTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Render_MessagesNull_RendersNothing()
    {
        var cut = RenderComponent<WizardValidationBanner>();
        cut.FindAll("[data-testid='wizard-validation-banner']").Should().BeEmpty();
    }

    [Fact]
    public void Render_MessagesEmpty_RendersNothing()
    {
        var cut = RenderComponent<WizardValidationBanner>(parameters => parameters
            .Add(p => p.Messages, new List<WizardValidationMessage>()));
        cut.FindAll("[data-testid='wizard-validation-banner']").Should().BeEmpty();
    }

    [Fact]
    public void Render_SingleMessage_RendersInlineNotBulletList()
    {
        var msgs = new List<WizardValidationMessage>
        {
            new("test.001", "Sources[0].InstanceId", "Instance id is required."),
        };

        var cut = RenderComponent<WizardValidationBanner>(parameters => parameters
            .Add(p => p.Messages, msgs));

        cut.Find("[data-testid='wizard-validation-banner']").Should().NotBeNull();
        var rendered = cut.FindAll("[data-testid='wizard-validation-message']");
        rendered.Count.Should().Be(1);
        cut.Markup.Should().Contain("Instance id is required.");
        // Single-message form does NOT render a bullet list.
        cut.FindAll("ul").Should().BeEmpty();
    }

    [Fact]
    public void Render_MultipleMessages_RendersBulletList()
    {
        var msgs = new List<WizardValidationMessage>
        {
            new("test.001", "Sources[0].InstanceId", "Instance id is required."),
            new("test.002", "Sources[0].BaseUrl", "Base URL must be a valid http(s) URI."),
        };

        var cut = RenderComponent<WizardValidationBanner>(parameters => parameters
            .Add(p => p.Messages, msgs));

        cut.FindAll("ul").Count.Should().Be(1);
        cut.FindAll("[data-testid='wizard-validation-message']").Count.Should().Be(2);
    }

    [Theory]
    [InlineData(WizardValidationSeverity.Error, "mud-alert-outlined-error")]
    [InlineData(WizardValidationSeverity.Warning, "mud-alert-outlined-warning")]
    [InlineData(WizardValidationSeverity.Info, "mud-alert-outlined-info")]
    public void Render_SeverityRoutesToMudAlertSeverityClass(WizardValidationSeverity severity, string expectedClassFragment)
    {
        var msgs = new List<WizardValidationMessage>
        {
            new("test.001", "x", "test message"),
        };

        var cut = RenderComponent<WizardValidationBanner>(parameters => parameters
            .Add(p => p.Severity, severity)
            .Add(p => p.Messages, msgs));

        // MudAlert with Variant.Outlined emits class names like
        // "mud-alert-outlined-error" / "mud-alert-outlined-warning" /
        // "mud-alert-outlined-info" (the "-outlined-" modifier is part
        // of the variant-specific naming).
        cut.Markup.Should().Contain(expectedClassFragment,
            $"severity {severity} should route to MudAlert class fragment '{expectedClassFragment}'");
    }

    [Fact]
    public void Click_MessageWithFieldAnchor_FiresOnMessageClick()
    {
        WizardValidationMessage? clicked = null;
        var msgs = new List<WizardValidationMessage>
        {
            new("test.001", "Sources[0].InstanceId", "Instance id is required.", FieldAnchor: "#field-instance-id"),
        };

        var cut = RenderComponent<WizardValidationBanner>(parameters => parameters
            .Add(p => p.Messages, msgs)
            .Add(p => p.OnMessageClick, EventCallback.Factory.Create<WizardValidationMessage>(this, m => clicked = m)));

        cut.Find("[data-testid='wizard-validation-message']").Click();

        clicked.Should().NotBeNull();
        clicked!.FieldAnchor.Should().Be("#field-instance-id");
    }

    [Fact]
    public void Click_MessageWithoutFieldAnchor_DoesNotFireOnMessageClick()
    {
        // No anchor = nothing to scroll to = no callback firing. Avoids
        // confusing operators who'd expect a click to do something
        // visible. M.2d.4 makes the click side-effect concrete.
        var fired = 0;
        var msgs = new List<WizardValidationMessage>
        {
            new("test.001", "x", "no anchor message"),
        };

        var cut = RenderComponent<WizardValidationBanner>(parameters => parameters
            .Add(p => p.Messages, msgs)
            .Add(p => p.OnMessageClick, EventCallback.Factory.Create<WizardValidationMessage>(this, _ => fired++)));

        cut.Find("[data-testid='wizard-validation-message']").Click();

        fired.Should().Be(0);
    }

    [Fact]
    public void Click_MessageWithFieldAnchor_InvokesScrollToFieldAnchorJsInterop()
    {
        // M.2d.4 / ADR-0015 Rule 5: clicking a message with a FieldAnchor
        // calls window.wizardValidation.scrollToFieldAnchor with that
        // selector. The JS-side helper handles "selector matched no
        // element" — the C# side just invokes.
        var msgs = new List<WizardValidationMessage>
        {
            new("test.001", "Sources[0].InstanceId", "Instance id is required.", FieldAnchor: "#field-instance-id"),
        };

        // bUnit JSInterop in Strict mode requires explicit Setup before
        // invocation. Loose mode (used by other tests in this class) lets
        // calls pass silently — that's fine for tests that don't care
        // about the interop side-effect. This test cares: assert exact
        // call with exact argument.
        JSInterop.Mode = JSRuntimeMode.Strict;
        var setup = JSInterop.SetupVoid("wizardValidation.scrollToFieldAnchor", "#field-instance-id");

        var cut = RenderComponent<WizardValidationBanner>(parameters => parameters
            .Add(p => p.Messages, msgs));

        cut.Find("[data-testid='wizard-validation-message']").Click();

        setup.VerifyInvoke("wizardValidation.scrollToFieldAnchor");
    }

    [Fact]
    public void Click_MessageWithoutFieldAnchor_DoesNotInvokeJsInterop()
    {
        // No FieldAnchor → no scroll target → no JS interop call. Strict
        // mode would throw on any uninvoked-yet-called function, so the
        // assertion is: no setup made, no invoke happened (no exception).
        var msgs = new List<WizardValidationMessage>
        {
            new("test.001", "x", "no anchor message"),
        };

        JSInterop.Mode = JSRuntimeMode.Strict;
        // Deliberately NO setup — if the banner calls the interop anyway,
        // bUnit raises JSRuntimeUnhandledInvocationException, which fails
        // the test.

        var cut = RenderComponent<WizardValidationBanner>(parameters => parameters
            .Add(p => p.Messages, msgs));

        cut.Find("[data-testid='wizard-validation-message']").Click();

        JSInterop.Invocations.Should().BeEmpty(
            "messages without FieldAnchor must not trigger any JS interop");
    }
}
