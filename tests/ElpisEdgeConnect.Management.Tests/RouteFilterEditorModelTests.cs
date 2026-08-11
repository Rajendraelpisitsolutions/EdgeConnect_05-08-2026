// ============================================================================
// Tests: RouteFilterEditorModel — pins the Filter sub-section of the
//        Route wizard (M.2b.5). Defaults match the runtime
//        TagFilterConfig (Include = ["*"]); glob-pattern validation
//        composes Core's GlobMatcher.Compile per Locked I/N; empty
//        Include collapses to the default, empty Exclude projects to
//        null on the record.
// Reference: docs/sessions/2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v3.md §5
// ============================================================================

using ElpisEdgeConnect.Management.Wizards;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class RouteFilterEditorModelTests
{
    [Fact]
    public void Defaults_MatchRuntimeTagFilterConfig()
    {
        var model = new RouteFilterEditorModel();

        model.Include.Should().Equal("*");
        model.Exclude.Should().BeEmpty();
    }

    [Fact]
    public void BuildTagFilterConfig_DefaultState_RoundtripsAsteriskInclude()
    {
        var model = new RouteFilterEditorModel();

        var cfg = model.BuildTagFilterConfig();

        cfg.Include.Should().Equal("*");
        cfg.Exclude.Should().BeNull("empty Exclude projects to null, not an empty list");
    }

    [Fact]
    public void BuildTagFilterConfig_WithIncludesAndExcludes_ProjectsBoth()
    {
        var model = new RouteFilterEditorModel();
        model.Include.Clear();
        model.Include.Add("axes/*");
        model.Include.Add("spindle/speed");
        model.Exclude.Add("diagnostics/*_internal");

        var cfg = model.BuildTagFilterConfig();

        cfg.Include.Should().Equal("axes/*", "spindle/speed");
        cfg.Exclude.Should().Equal("diagnostics/*_internal");
    }

    [Fact]
    public void BuildTagFilterConfig_EmptyInclude_CollapsesToAsterisk()
    {
        // Defensive: if the operator removes every Include pattern, the
        // wizard never produces a filter that blocks everything — that
        // would almost certainly be unintended. Falls back to the runtime
        // default "match everything" instead.
        var model = new RouteFilterEditorModel();
        model.Include.Clear();

        var cfg = model.BuildTagFilterConfig();

        cfg.Include.Should().Equal("*");
    }

    [Theory]
    [InlineData("*")]
    [InlineData("**")]
    [InlineData("axes/*")]
    [InlineData("spindle/speed")]
    [InlineData("axes/?_position")]
    [InlineData("diagnostics/**/temperature")]
    public void ValidatePattern_GlobSyntax_AcceptedByGlobMatcher(string pattern)
    {
        // Composes Core's GlobMatcher.Compile — never re-implements the
        // syntax rules. If GlobMatcher accepts the pattern, the wizard
        // accepts it; if it rejects, the wizard rejects with the same
        // semantics.
        RouteFilterEditorModel.ValidatePattern(pattern).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ValidatePattern_EmptyOrWhitespace_Rejected(string? pattern)
    {
        var error = RouteFilterEditorModel.ValidatePattern(pattern);
        error.Should().NotBeNull();
        error.Should().Contain("empty");
    }
}
