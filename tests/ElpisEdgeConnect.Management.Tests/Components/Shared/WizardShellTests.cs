// ============================================================================
// Tests: WizardShellTests — pins the M.2d.1 v2 §5.1 narrow-shell contract.
//
// WizardShell is the page-frame primitive. Verified contracts:
//   * Loading branch renders when IsLoading is true.
//   * Error branch renders when LoadError is non-empty (priority over
//     IsLoading per the existing wizard convention).
//   * Content branch renders ChildContent when neither error nor loading
//     is active.
//   * Footer slot renders only when content branch is active.
//   * SaveState is parameter-only — no visual side-effect inside the
//     shell itself (Q4 verdict).
//   * Anti-coupling: the shell does NOT render any MudPaper-with-numbered-
//     title — that's WizardSection's job (round-1 #1 narrowing lock).
//
// Reference: docs/sessions/2026-05-21-m2d1-shared-primitives-plan-v2.md §5.1
// ============================================================================

using Bunit;
using ElpisEdgeConnect.Management.Components.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests.Components.Shared;

public sealed class WizardShellTests : TestContext
{
    public WizardShellTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Render_WhenLoadErrorIsSet_RendersErrorBranch_NotLoadingOrContent()
    {
        var cut = RenderComponent<WizardShell>(parameters => parameters
            .Add(p => p.Title, "Test wizard")
            .Add(p => p.LoadError, "Could not load gateway config: timeout")
            .Add(p => p.IsLoading, true) // Even with IsLoading=true, LoadError takes priority
            .Add(p => p.ChildContent, (RenderFragment)(b => b.AddContent(0, "should not render"))));

        cut.Find("[data-testid='wizard-shell-load-error']").Should().NotBeNull();
        cut.Markup.Should().Contain("Could not load gateway config: timeout");
        cut.FindAll("[data-testid='wizard-shell-loading']").Should().BeEmpty();
        cut.FindAll("[data-testid='wizard-shell-content']").Should().BeEmpty();
        cut.Markup.Should().NotContain("should not render");
    }

    [Fact]
    public void Render_WhenIsLoadingIsTrue_AndNoLoadError_RendersLoadingBranch_NotContent()
    {
        var cut = RenderComponent<WizardShell>(parameters => parameters
            .Add(p => p.Title, "Test wizard")
            .Add(p => p.IsLoading, true)
            .Add(p => p.ChildContent, (RenderFragment)(b => b.AddContent(0, "should not render"))));

        cut.Find("[data-testid='wizard-shell-loading']").Should().NotBeNull();
        cut.FindAll("[data-testid='wizard-shell-load-error']").Should().BeEmpty();
        cut.FindAll("[data-testid='wizard-shell-content']").Should().BeEmpty();
        cut.Markup.Should().NotContain("should not render");
    }

    [Fact]
    public void Render_WhenContentBranchActive_RendersChildContent()
    {
        var cut = RenderComponent<WizardShell>(parameters => parameters
            .Add(p => p.Title, "Test wizard")
            .Add(p => p.Subtitle, "subtitle text")
            .Add(p => p.IsLoading, false)
            .Add(p => p.LoadError, null)
            .Add(p => p.ChildContent, (RenderFragment)(b => b.AddMarkupContent(0, "<div data-testid='child-marker'>hello</div>"))));

        cut.Find("[data-testid='wizard-shell-content']").Should().NotBeNull();
        cut.Find("[data-testid='child-marker']").TextContent.Should().Be("hello");
        cut.Markup.Should().Contain("Test wizard");
        cut.Markup.Should().Contain("subtitle text");
    }

    [Fact]
    public void Render_WhenFooterSupplied_AndContentBranchActive_RendersFooter()
    {
        var cut = RenderComponent<WizardShell>(parameters => parameters
            .Add(p => p.Title, "Test wizard")
            .Add(p => p.ChildContent, (RenderFragment)(b => b.AddContent(0, "body")))
            .Add(p => p.Footer, (RenderFragment)(b => b.AddMarkupContent(0, "<div data-testid='footer-marker'>footer</div>"))));

        cut.Find("[data-testid='wizard-shell-footer']").Should().NotBeNull();
        cut.Find("[data-testid='footer-marker']").TextContent.Should().Be("footer");
    }

    [Fact]
    public void Render_WhenFooterNull_FooterSlotIsAbsent()
    {
        var cut = RenderComponent<WizardShell>(parameters => parameters
            .Add(p => p.Title, "Test wizard")
            .Add(p => p.ChildContent, (RenderFragment)(b => b.AddContent(0, "body"))));

        cut.FindAll("[data-testid='wizard-shell-footer']").Should().BeEmpty();
    }

    [Fact]
    public void Render_WhenFooterSupplied_AndLoadingBranchActive_FooterSlotIsAbsent()
    {
        // Footer renders only in the content branch — operators don't
        // see Save/Cancel buttons while the wizard is still loading.
        var cut = RenderComponent<WizardShell>(parameters => parameters
            .Add(p => p.Title, "Test wizard")
            .Add(p => p.IsLoading, true)
            .Add(p => p.Footer, (RenderFragment)(b => b.AddContent(0, "should not render"))));

        cut.FindAll("[data-testid='wizard-shell-footer']").Should().BeEmpty();
        cut.Markup.Should().NotContain("should not render");
    }

    [Theory]
    [InlineData(WizardSaveState.Editing)]
    [InlineData(WizardSaveState.Saving)]
    [InlineData(WizardSaveState.Saved)]
    [InlineData(WizardSaveState.Failed)]
    public void SaveState_HasNoVisualSideEffect_InM2d1(WizardSaveState state)
    {
        // Q4 lock — M.2d.1 exposes the parameter but does not render any
        // state-specific visual. M.2d.2/.3 decide per-wizard.
        var cut = RenderComponent<WizardShell>(parameters => parameters
            .Add(p => p.Title, "Test wizard")
            .Add(p => p.SaveState, state)
            .Add(p => p.ChildContent, (RenderFragment)(b => b.AddContent(0, "body"))));

        // Render output must not surface the SaveState string.
        cut.Markup.Should().NotContain(state.ToString());
    }

    [Fact]
    public void AntiCouplingCheck_WizardShellMarkup_ContainsNoMudPaperWithNumberedTitle()
    {
        // §6 DoD: WizardShell does NOT own section structure — that's
        // WizardSection's job. Verify by inspecting the rendered markup
        // for the section-paper signature.
        var cut = RenderComponent<WizardShell>(parameters => parameters
            .Add(p => p.Title, "Test wizard")
            .Add(p => p.ChildContent, (RenderFragment)(b => b.AddContent(0, "body"))));

        // Subtitle / title are rendered via Typo.h5 / Typo.body2, NOT
        // Typo.subtitle1 with a numeric prefix. The signature
        // "1. " ... "2. " etc. only appears if WizardShell tried to
        // render section structure itself.
        var numberedSectionPattern = System.Text.RegularExpressions.Regex.Match(
            cut.Markup, @"\b\d+\.\s+\w+", System.Text.RegularExpressions.RegexOptions.None);
        numberedSectionPattern.Success.Should().BeFalse(
            "WizardShell must not render section structure; WizardSection owns that responsibility");
    }
}
