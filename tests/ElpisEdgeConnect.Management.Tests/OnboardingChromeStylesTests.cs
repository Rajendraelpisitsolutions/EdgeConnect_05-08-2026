// ============================================================================
// Tests: OnboardingChromeStylesTests — pins the two /onboard chrome fixes that
// live entirely in CSS, where nothing else in the suite can see them.
//
// Both defects were found by measuring the running Studio in a headless
// browser, and neither is reachable from a unit test: the symptom of the first
// is a scrolled-off bounding box, the symptom of the second a missing gradient.
// What IS testable — and what a well-meaning edit would break first — is the
// STRUCTURE the measured behaviour depends on. Each assertion below names the
// number it protects.
//
//   Defect 1 — the seven-step rail scrolled out of view (measured: rail top
//   -64px at scrollTop 223, 1280 wide). Fixed by pinning heading + rail as ONE
//   .page-heading-stack band, so the rail never needs to know the heading's
//   height — which is not a constant, because the heading grows when its text
//   wraps (measured: 85px at 1280, 113px at 520).
//
//   Defect 2 — the protocol-tile grid was cut in half by the sticky action bar
//   with no cue that it continued. Fixed by a scroll-driven fade attached to
//   the bar itself.
//
// Reference: site.css §"Pinned heading band" and §"Sticky wizard action bar".
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Bunit;
using ElpisEdgeConnect.Management.Components.Pages.Onboarding;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class OnboardingChromeStylesTests : TestContext
{
    public OnboardingChromeStylesTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>
    /// Repository root, walked up from THIS FILE's compile-time path rather than
    /// from the test binary. The binary can legitimately be built outside the
    /// tree (the Studio holds a lock on bin\Debug, so the documented workaround
    /// is `-p:BaseOutputPath=&lt;temp&gt;`), at which point walking up from
    /// AppContext.BaseDirectory finds nothing. The source path is fixed at
    /// compile time and is always inside the repo.
    /// </summary>
    private static string RepoRoot([CallerFilePath] string thisFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ElpisEdgeConnect.sln")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("the tests must be able to locate the repository root");
        return dir!.FullName;
    }

    private static string SiteCss() => File.ReadAllText(Path.Combine(
        RepoRoot(), "src", "ElpisEdgeConnect.Management", "wwwroot", "css", "site.css"));

    private static string OnboardingFlowMarkup() => File.ReadAllText(Path.Combine(
        RepoRoot(), "src", "ElpisEdgeConnect.Management", "Components", "Pages",
        "Onboarding", "OnboardingFlow.razor"));

    /// <summary>
    /// Every declaration block whose selector list contains <paramref name="selector"/>
    /// exactly. Comments are stripped first so prose about a rule is never mistaken
    /// for the rule.
    /// </summary>
    private static IReadOnlyList<string> BlocksFor(string css, string selector)
    {
        var withoutComments = Regex.Replace(css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        var blocks = new List<string>();

        foreach (Match m in Regex.Matches(withoutComments, @"([^{}]+)\{([^{}]*)\}", RegexOptions.Singleline))
        {
            var selectors = m.Groups[1].Value
                .Split(',')
                .Select(s => Regex.Replace(s.Trim(), @"\s+", " "));

            if (selectors.Any(s => s.EndsWith(selector, StringComparison.Ordinal)
                                   && (s.Length == selector.Length || s[^(selector.Length + 1)] is ' ' or '\n')))
            {
                blocks.Add(Regex.Replace(m.Groups[2].Value, @"\s+", " ").Trim());
            }
        }

        return blocks;
    }

    private static string Declarations(string css, string selector)
    {
        var blocks = BlocksFor(css, selector);
        blocks.Should().NotBeEmpty($"site.css must still define a rule for `{selector}`");
        return string.Join(" ", blocks);
    }

    // ── Defect 1 ────────────────────────────────────────────────────────────

    [Fact]
    public void PageHeadingStack_PinsToTheTopOfTheScrollRegion()
    {
        var declarations = Declarations(SiteCss(), ".page-heading-stack");

        declarations.Should().Contain("position: sticky");
        declarations.Should().Contain("top: 0",
            "the band carries the whole pinned chrome, so it pins at the top of the scroll region");
    }

    [Fact]
    public void PageHeadingInsideTheBand_DoesNotPinSeparately()
    {
        // Nested sticky is the failure this replaced: a rail pinned on its own
        // needs `top: <heading height>`, and that height changes with wrapping.
        // The band pins; its children are ordinary flow, which is why the rail
        // can never overlap the heading at any width.
        Declarations(SiteCss(), ".page-heading-stack > .page-heading")
            .Should().Contain("position: static");
    }

    [Fact]
    public void OnboardingFlow_PutsHeadingAndStepRail_InsideOneBand()
    {
        var markup = OnboardingFlowMarkup();

        var band = markup.IndexOf("class=\"page-heading-stack\"", StringComparison.Ordinal);
        var heading = markup.IndexOf("Class=\"page-heading\"", StringComparison.Ordinal);
        var rail = markup.IndexOf("<OnboardingProgress", StringComparison.Ordinal);

        band.Should().BeGreaterThan(-1, "the flow must wrap its pinned chrome in .page-heading-stack");
        heading.Should().BeGreaterThan(band, "the heading belongs inside the band");
        rail.Should().BeGreaterThan(heading,
            "the step rail must follow the heading inside the same band — that ordering is what "
            + "makes overlap impossible without knowing the heading's height");
    }

    [Fact]
    public void OnboardingProgress_CarriesTheRailStylingHook()
    {
        var cut = RenderComponent<OnboardingProgress>(p => p.Add(x => x.CurrentStep, 1));

        cut.Markup.Should().Contain("onboarding-rail",
            "site.css lines the rail up with page content through this class; without it the "
            + "rail sits flush to the band's full-bleed edge");
    }

    // ── Defect 2 ────────────────────────────────────────────────────────────

    [Fact]
    public void ActionBar_StaysPinnedToTheBottom()
    {
        // Regression guard, not a new behaviour: the pinned bar was fixed
        // deliberately (including the flex / margin-top:auto rules that park it
        // on the bottom edge of a short page) and the fade must not undo it.
        var css = SiteCss();

        var bar = Declarations(css, ".sticky-action-bar");
        bar.Should().Contain("position: sticky");
        bar.Should().Contain("bottom: 0");

        Declarations(css, ".app-content-scroll > .sticky-action-bar")
            .Should().Contain("margin-top: auto");
    }

    [Fact]
    public void ActionBarFade_IsClickThrough()
    {
        Declarations(SiteCss(), ".sticky-action-bar::before")
            .Should().Contain("pointer-events: none",
                "the fade covers protocol tiles — it must never swallow a click on one");
    }

    [Fact]
    public void ActionBarFade_IsInvisibleWhenThereIsNothingToScroll()
    {
        // A scroll timeline on a non-scrollable container is INACTIVE, and an
        // animation on an inactive timeline does not apply — so the base style
        // is what a short page renders. Base opacity 0 is therefore the rule
        // that keeps the fade off pages with nothing below the fold.
        BlocksFor(SiteCss(), ".sticky-action-bar::before")
            .Should().Contain(b => b.Contains("opacity: 0", StringComparison.Ordinal));
    }

    [Fact]
    public void ActionBarFade_DisappearsAtTheBottomOfTheScroll()
    {
        var css = SiteCss();

        css.Should().Contain("@supports (animation-timeline: scroll())",
            "the fade is driven by the scroll position with no JS, and degrades to no fade "
            + "where that is unsupported");

        var animated = Declarations(css, ".sticky-action-bar::before");
        animated.Should().Contain("animation-timeline: scroll(nearest block)");
        animated.Should().Contain("animation-fill-mode: backwards",
            "`backwards` — not `both` — is what returns the fade to the invisible base style "
            + "past the end of the range, i.e. at the bottom of the page");
        animated.Should().MatchRegex(@"animation-range:\s*calc\(100% - \d+px\) 100%",
            "the fade-out is a fixed distance from the bottom, so it behaves the same on a "
            + "short page and on a long tag table");
    }

    [Fact]
    public void ActionBarFade_TracksTheThemeSurface()
    {
        Declarations(SiteCss(), ".sticky-action-bar::before")
            .Should().Contain("linear-gradient(to top, var(--studio-surface), transparent)",
                "a hard-coded white would be wrong in dark theme; --studio-surface is the same "
                + "token the bar itself is painted with");
    }
}
