// ============================================================================
// Tests: RouteTransformsEditorModel — pins the Transforms sub-section of
//        the Route wizard (M.2b.5). Key invariants:
//
//          * Unified Deadband table: per-row Mode (Absolute|Percent) +
//            cross-row no-duplicate-tag enforces "one mode per tag"
//            (the constraint Core's CrossRecordValidator enforces at
//            draft-create time).
//          * Numeric-range validation is EAGER (Locked N): rejects
//            non-finite, negative Absolute, out-of-(0,1] Percent,
//            non-positive RateLimitMs.
//          * EnrichmentTags is OMITTED — Core marks it DORMANT.
//          * Projection collapses every empty sub-block to null; full
//            empty model returns null TransformProfileConfig (identity).
// Reference: docs/sessions/2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v3.md §5
// ============================================================================

using System.Linq;
using ElpisEdgeConnect.Management.Wizards;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class RouteTransformsEditorModelTests
{
    // ─── Empty / identity ──────────────────────────────────────────────────

    [Fact]
    public void BuildTransformProfileConfig_EmptyModel_ReturnsNull()
    {
        // Core's TransformProfileConfig accepts null on RouteConfig — that's
        // an identity route. The wizard preserves the same shape.
        var model = new RouteTransformsEditorModel();

        var cfg = model.BuildTransformProfileConfig();

        cfg.Should().BeNull("an empty editor maps to identity (null transform profile)");
    }

    // ─── Tag mapping ───────────────────────────────────────────────────────

    [Fact]
    public void BuildTransformProfileConfig_TagMappings_ProjectToDictionary()
    {
        var model = new RouteTransformsEditorModel();
        model.TagMappings.Add(new TagMappingRow { SourceTag = "raw_rpm", CanonicalTag = "spindle/speed" });
        model.TagMappings.Add(new TagMappingRow { SourceTag = "raw_load", CanonicalTag = "spindle/load" });

        var cfg = model.BuildTransformProfileConfig();

        cfg.Should().NotBeNull();
        cfg!.TagMapping.Should().NotBeNull();
        cfg.TagMapping!["raw_rpm"].Should().Be("spindle/speed");
        cfg.TagMapping["raw_load"].Should().Be("spindle/load");
    }

    [Fact]
    public void ValidateTagMappingRow_DuplicateSourceTag_Rejected()
    {
        var model = new RouteTransformsEditorModel();
        model.TagMappings.Add(new TagMappingRow { SourceTag = "raw_rpm", CanonicalTag = "spindle/speed" });

        var dup = new TagMappingRow { SourceTag = "raw_rpm", CanonicalTag = "different/path" };
        model.TagMappings.Add(dup);

        var error = model.ValidateTagMappingRow(dup);

        error.Should().Contain("already mapped");
    }

    // ─── Deadband (unified table) ──────────────────────────────────────────

    [Fact]
    public void BuildTransformProfileConfig_DeadbandRows_SplitByModeIntoTwoDictionaries()
    {
        // The unified Deadband table's Mode column drives the split into
        // the two separate Core dictionaries. Pins the projection contract
        // — operators see one table, the runtime sees two dictionaries.
        var model = new RouteTransformsEditorModel();
        model.DeadbandRows.Add(new DeadbandRow { Tag = "axes/x/absolute", Mode = DeadbandMode.Absolute, Threshold = 0.5 });
        model.DeadbandRows.Add(new DeadbandRow { Tag = "spindle/speed", Mode = DeadbandMode.Percent, Threshold = 0.05 });

        var cfg = model.BuildTransformProfileConfig();

        cfg.Should().NotBeNull();
        cfg!.Deadband.Should().NotBeNull();
        cfg.Deadband!["axes/x/absolute"].Should().Be(0.5);
        cfg.Deadband.Should().NotContainKey("spindle/speed");

        cfg.DeadbandPercent.Should().NotBeNull();
        cfg.DeadbandPercent!["spindle/speed"].Should().Be(0.05);
        cfg.DeadbandPercent.Should().NotContainKey("axes/x/absolute");
    }

    [Fact]
    public void BuildTransformProfileConfig_OnlyAbsoluteRows_PercentDictIsNull()
    {
        var model = new RouteTransformsEditorModel();
        model.DeadbandRows.Add(new DeadbandRow { Tag = "axes/x/absolute", Mode = DeadbandMode.Absolute, Threshold = 0.5 });

        var cfg = model.BuildTransformProfileConfig();

        cfg!.Deadband.Should().NotBeNull().And.HaveCount(1);
        cfg.DeadbandPercent.Should().BeNull("empty percent rows collapse to null, not an empty dict");
    }

    [Fact]
    public void ValidateDeadbandRow_DuplicateTag_Rejected()
    {
        // The "one mode per tag" invariant Core enforces in
        // CrossRecordValidator is made impossible-to-construct at the
        // wizard layer: a tag can have ONE row with ONE Mode, and adding
        // a second row with the same tag is rejected here.
        var model = new RouteTransformsEditorModel();
        model.DeadbandRows.Add(new DeadbandRow { Tag = "axes/x/absolute", Mode = DeadbandMode.Absolute, Threshold = 0.5 });

        var dup = new DeadbandRow { Tag = "axes/x/absolute", Mode = DeadbandMode.Percent, Threshold = 0.05 };
        model.DeadbandRows.Add(dup);

        var error = model.ValidateDeadbandRow(dup);

        error.Should().Contain("already has a deadband");
        error.Should().Contain("Absolute OR Percent");
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ValidateDeadbandRow_AbsoluteWithNonFiniteOrNegative_Rejected(double value)
    {
        var model = new RouteTransformsEditorModel();
        var row = new DeadbandRow { Tag = "axes/x", Mode = DeadbandMode.Absolute, Threshold = value };

        var error = model.ValidateDeadbandRow(row);

        error.Should().NotBeNull();
    }

    [Theory]
    [InlineData(0.0)]      // boundary — must be > 0
    [InlineData(1.1)]      // > 1
    [InlineData(-0.01)]    // negative
    public void ValidateDeadbandRow_PercentOutsideValidRange_Rejected(double value)
    {
        // (NaN / Infinity are covered by the finite-check test above —
        // they don't reach this range check because IsFinite rejects first.)
        var model = new RouteTransformsEditorModel();
        var row = new DeadbandRow { Tag = "spindle/speed", Mode = DeadbandMode.Percent, Threshold = value };

        var error = model.ValidateDeadbandRow(row);

        error.Should().NotBeNull();
        error.Should().Contain("0, 1");
    }

    [Theory]
    [InlineData(0.05)]     // 5%
    [InlineData(0.001)]    // 0.1%
    [InlineData(1.0)]      // upper boundary inclusive
    public void ValidateDeadbandRow_PercentInRange_Accepted(double value)
    {
        var model = new RouteTransformsEditorModel();
        var row = new DeadbandRow { Tag = "spindle/speed", Mode = DeadbandMode.Percent, Threshold = value };

        model.ValidateDeadbandRow(row).Should().BeNull();
    }

    // ─── Rate limit ────────────────────────────────────────────────────────

    [Fact]
    public void BuildTransformProfileConfig_RateLimitRows_ProjectToDictionary()
    {
        var model = new RouteTransformsEditorModel();
        model.RateLimitRows.Add(new RateLimitRow { Tag = "alarms/active", MinIntervalMs = 5000 });

        var cfg = model.BuildTransformProfileConfig();

        cfg!.RateLimitMs.Should().NotBeNull();
        cfg.RateLimitMs!["alarms/active"].Should().Be(5000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void ValidateRateLimitRow_NonPositive_Rejected(int ms)
    {
        var model = new RouteTransformsEditorModel();
        var row = new RateLimitRow { Tag = "alarms/active", MinIntervalMs = ms };

        var error = model.ValidateRateLimitRow(row);

        error.Should().Contain("strictly positive");
    }

    // ─── EnrichmentTags omission ───────────────────────────────────────────

    [Fact]
    public void EnrichmentTags_DeliberatelyOmitted_FromTransformsEditor()
    {
        // EnrichmentTags is DORMANT in Core (its XML doc explicitly says
        // "values in this map have no runtime effect" until C1.5/C3).
        // The wizard must not surface a no-op field. Verify by inspecting
        // the public surface of the model — there should be no
        // Enrichment property on the editor.
        var editorProperties = typeof(RouteTransformsEditorModel).GetProperties()
            .Select(p => p.Name)
            .ToArray();

        editorProperties.Should().NotContain(name => name.Contains("Enrichment"),
            "EnrichmentTags is DORMANT in Core; surfacing an editor would mislead operators");
    }
}
