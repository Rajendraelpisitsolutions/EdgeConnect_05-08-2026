// ============================================================================
// Tests: WizardActionsTests — pins the M.2d.1 v2 §5.5 footer button-row
// contract.
//
// Verified contracts:
//   * Save + Cancel buttons render unconditionally.
//   * Test Connection button renders only when OnTestConnection is set.
//   * AdditionalActions slot renders only when supplied (round-2 #11).
//   * Save button is disabled when Busy=true OR CanSave=false.
//   * Cancel + Test Connection are disabled only on Busy.
//   * Label overrides flow through to the rendered button text.
//
// Reference: docs/sessions/2026-05-21-m2d1-shared-primitives-plan-v2.md §5.5
// ============================================================================

using Bunit;
using ElpisEdgeConnect.Management.Components.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests.Components.Shared;

public sealed class WizardActionsTests : TestContext
{
    public WizardActionsTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Render_DefaultLabels_SaveAndCancelRender()
    {
        var saveCalled = 0;
        var cancelCalled = 0;
        var cut = RenderComponent<WizardActions>(parameters => parameters
            .Add(p => p.OnSave, EventCallback.Factory.Create(this, () => saveCalled++))
            .Add(p => p.OnCancel, EventCallback.Factory.Create(this, () => cancelCalled++)));

        // "Save changes", not the original "Save as draft": commit 86cf2d4
        // (TL-113 operator-UI redesign) changed the default and its XML doc but
        // left this assertion behind, so the test had been red at HEAD since.
        cut.Find("[data-testid='wizard-actions-save']").TextContent.Should().Contain("Save changes");
        cut.Find("[data-testid='wizard-actions-cancel']").TextContent.Should().Contain("Cancel");
        cut.FindAll("[data-testid='wizard-actions-test-connection']").Should().BeEmpty();
        cut.FindAll("[data-testid='wizard-actions-additional']").Should().BeEmpty();
    }

    [Fact]
    public void Render_OnTestConnectionSupplied_TestConnectionButtonAppears()
    {
        var cut = RenderComponent<WizardActions>(parameters => parameters
            .Add(p => p.OnSave, EventCallback.Factory.Create(this, () => { }))
            .Add(p => p.OnCancel, EventCallback.Factory.Create(this, () => { }))
            .Add(p => p.OnTestConnection, EventCallback.Factory.Create(this, () => { })));

        cut.Find("[data-testid='wizard-actions-test-connection']").TextContent.Should().Contain("Test Connection");
    }

    [Fact]
    public void Render_AdditionalActionsSlotSupplied_RendersInBetween()
    {
        var cut = RenderComponent<WizardActions>(parameters => parameters
            .Add(p => p.OnSave, EventCallback.Factory.Create(this, () => { }))
            .Add(p => p.OnCancel, EventCallback.Factory.Create(this, () => { }))
            .Add(p => p.AdditionalActions, (RenderFragment)(b =>
                b.AddMarkupContent(0, "<button data-testid='extra-validate'>Validate</button>"))));

        cut.Find("[data-testid='wizard-actions-additional']").Should().NotBeNull();
        cut.Find("[data-testid='extra-validate']").TextContent.Should().Be("Validate");
    }

    [Fact]
    public void SaveButton_IsDisabledWhenBusy()
    {
        var cut = RenderComponent<WizardActions>(parameters => parameters
            .Add(p => p.OnSave, EventCallback.Factory.Create(this, () => { }))
            .Add(p => p.OnCancel, EventCallback.Factory.Create(this, () => { }))
            .Add(p => p.Busy, true)
            .Add(p => p.CanSave, true));

        cut.Find("[data-testid='wizard-actions-save']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void SaveButton_IsDisabledWhenCanSaveFalse_EvenIfNotBusy()
    {
        var cut = RenderComponent<WizardActions>(parameters => parameters
            .Add(p => p.OnSave, EventCallback.Factory.Create(this, () => { }))
            .Add(p => p.OnCancel, EventCallback.Factory.Create(this, () => { }))
            .Add(p => p.Busy, false)
            .Add(p => p.CanSave, false));

        cut.Find("[data-testid='wizard-actions-save']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void SaveButton_IsEnabledWhenCanSaveTrueAndNotBusy()
    {
        var cut = RenderComponent<WizardActions>(parameters => parameters
            .Add(p => p.OnSave, EventCallback.Factory.Create(this, () => { }))
            .Add(p => p.OnCancel, EventCallback.Factory.Create(this, () => { }))
            .Add(p => p.Busy, false)
            .Add(p => p.CanSave, true));

        cut.Find("[data-testid='wizard-actions-save']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Render_CustomLabels_FlowThroughToButtons()
    {
        var cut = RenderComponent<WizardActions>(parameters => parameters
            .Add(p => p.OnSave, EventCallback.Factory.Create(this, () => { }))
            .Add(p => p.OnCancel, EventCallback.Factory.Create(this, () => { }))
            .Add(p => p.OnTestConnection, EventCallback.Factory.Create(this, () => { }))
            .Add(p => p.SaveLabel, "Save changes") // M.2d.2/.3 Edit-mode label
            .Add(p => p.CancelLabel, "Discard")
            .Add(p => p.TestConnectionLabel, "Probe device"));

        cut.Find("[data-testid='wizard-actions-save']").TextContent.Should().Contain("Save changes");
        cut.Find("[data-testid='wizard-actions-cancel']").TextContent.Should().Contain("Discard");
        cut.Find("[data-testid='wizard-actions-test-connection']").TextContent.Should().Contain("Probe device");
    }
}
