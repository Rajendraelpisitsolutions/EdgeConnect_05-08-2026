// ============================================================================
// Tests: WizardSectionTests — pins the M.2d.1 v2 §5.2 chrome-only contract.
//
// WizardSection is the numbered-card primitive. Verified contracts:
//   * Index + Title render as "{Index}. {Title}".
//   * Description renders as a caption directly below the title when
//     non-empty; omitted when null/empty.
//   * ChildContent renders verbatim — no enclosing layout container.
//   * Anti-layout-coupling: the section markup contains NO MudGrid
//     constructs (round-2 #10 lock — layout is the wizard's choice).
//
// Reference: docs/sessions/2026-05-21-m2d1-shared-primitives-plan-v2.md §5.2
// ============================================================================

using Bunit;
using ElpisEdgeConnect.Management.Components.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests.Components.Shared;

public sealed class WizardSectionTests : TestContext
{
    public WizardSectionTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Render_NumberedTitle_FormatsAsIndexDotSpaceTitle()
    {
        var cut = RenderComponent<WizardSection>(parameters => parameters
            .Add(p => p.Index, 2)
            .Add(p => p.Title, "Connection")
            .Add(p => p.ChildContent, (RenderFragment)(b => b.AddContent(0, "body"))));

        cut.Find("[data-testid='wizard-section-title']").TextContent.Trim()
            .Should().Be("2. Connection");
    }

    [Fact]
    public void Render_DescriptionPresent_RendersCaptionBelowTitle()
    {
        var cut = RenderComponent<WizardSection>(parameters => parameters
            .Add(p => p.Index, 1)
            .Add(p => p.Title, "Identity")
            .Add(p => p.Description, "Operator-readable instance + device identifiers.")
            .Add(p => p.ChildContent, (RenderFragment)(b => b.AddContent(0, "body"))));

        var description = cut.Find("[data-testid='wizard-section-description']");
        description.TextContent.Should().Be("Operator-readable instance + device identifiers.");
    }

    [Fact]
    public void Render_DescriptionNull_DescriptionElementAbsent()
    {
        var cut = RenderComponent<WizardSection>(parameters => parameters
            .Add(p => p.Index, 1)
            .Add(p => p.Title, "Identity")
            .Add(p => p.ChildContent, (RenderFragment)(b => b.AddContent(0, "body"))));

        cut.FindAll("[data-testid='wizard-section-description']").Should().BeEmpty();
    }

    [Fact]
    public void Render_DescriptionEmpty_DescriptionElementAbsent()
    {
        var cut = RenderComponent<WizardSection>(parameters => parameters
            .Add(p => p.Index, 1)
            .Add(p => p.Title, "Identity")
            .Add(p => p.Description, "")
            .Add(p => p.ChildContent, (RenderFragment)(b => b.AddContent(0, "body"))));

        cut.FindAll("[data-testid='wizard-section-description']").Should().BeEmpty();
    }

    [Fact]
    public void Render_ChildContent_RendersVerbatim_WithoutEnclosingLayoutContainer()
    {
        // Round-2 #10 lock: WizardSection owns chrome, NOT layout. A child
        // <table> stays a <table>; we must not wrap it in a MudGrid.
        var cut = RenderComponent<WizardSection>(parameters => parameters
            .Add(p => p.Index, 3)
            .Add(p => p.Title, "Custom layout")
            .Add(p => p.ChildContent, (RenderFragment)(b => b.AddMarkupContent(0,
                "<table data-testid='child-table'><tr><td>row</td></tr></table>"))));

        cut.Find("[data-testid='child-table']").Should().NotBeNull();
        cut.Find("[data-testid='child-table'] tr td").TextContent.Should().Be("row");
    }

    [Fact]
    public void AntiLayoutCouplingCheck_WizardSectionMarkup_ContainsNoMudGrid()
    {
        // §6 DoD: WizardSection does NOT own field layout — wizards
        // decide MudGrid / table / flex / charts inside ChildContent.
        // Verify by rendering with empty ChildContent and inspecting
        // the wrapper-only markup for any MudGrid trace.
        var cut = RenderComponent<WizardSection>(parameters => parameters
            .Add(p => p.Index, 1)
            .Add(p => p.Title, "no-grid-test"));

        // MudGrid renders as a <div class="mud-grid ...">. The presence of
        // "mud-grid" in the section's own wrapper markup (when ChildContent
        // is empty) would indicate WizardSection is wrapping in MudGrid.
        cut.Markup.Should().NotContain("mud-grid",
            "WizardSection must not wrap ChildContent in MudGrid; layout is the wizard's choice");
    }
}
