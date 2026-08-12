// ============================================================================
// Tests: RouteWizardModel — pins the top-level Route wizard view-model
//        (M.2b.5). Key invariants:
//
//          * Defaults match the runtime contract (StoreAndForward buffer,
//            AtLeastOnce delivery, 10k depth, 100ms initial backoff).
//          * BuildRouteConfig is a faithful projection: every field
//            roundtrips, sub-models compose, and Filter/Transforms project
//            via their own contract (empty Transforms → null on the record).
//          * Eager validation (Locked N): route-id regex composes Core's
//            attribute (no parallel pattern), required-field presence, and
//            ≥1 sink. Cross-record rules are NOT enforced here — those
//            land on the merger / API.
//          * Defensive guards: blank route-id / name / source / empty sink
//            set throw InvalidOperationException so a programmatic caller
//            can't construct an invalid route.
// Reference: docs/sessions/2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v3.md §5
// ============================================================================

using System;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Wizards;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class RouteWizardModelTests
{
    // ─── Defaults ────────────────────────────────────────────────────────

    [Fact]
    public void Defaults_MatchRuntimeContract()
    {
        var model = new RouteWizardModel();

        model.Enabled.Should().BeTrue();
        model.BufferMode.Should().Be(BufferMode.StoreAndForward);
        model.BufferMaxDepth.Should().Be(10_000);
        model.BufferMaxAgeDays.Should().Be(7);
        model.BufferOnOverflow.Should().Be(DropPolicy.DropOldest);

        model.DeliveryMode.Should().Be(DeliveryMode.AtLeastOnce, "pinned by blueprint §19.7");
        model.DeliveryMaxRetries.Should().Be(5);
        model.DeliveryInitialBackoffMs.Should().Be(100);
        model.DeliveryMaxBackoffMs.Should().Be(30_000);
        model.DeliveryBackoffMultiplier.Should().Be(2.0);
        model.DeliveryJitterPercent.Should().Be(10);
        model.DeliveryFanoutParallel.Should().BeTrue();
    }

    [Fact]
    public void Defaults_FilterIncludesEverything()
    {
        var model = new RouteWizardModel();

        model.Filter.Include.Should().Equal("*");
        model.Filter.Exclude.Should().BeEmpty();
    }

    // ─── Projection ──────────────────────────────────────────────────────

    [Fact]
    public void BuildRouteConfig_HappyPath_RoundtripsAllFields()
    {
        var model = new RouteWizardModel
        {
            RouteId = "jyoti17-to-eremos",
            Name = "Jyoti #17 → EREMOS",
            SourceInstanceId = "focas-cell-A",
            Enabled = false,
            BufferMaxDepth = 50_000,
            BufferMaxAgeDays = 30,
            DeliveryMaxRetries = 10,
            DeliveryInitialBackoffMs = 250,
        };
        model.SinkInstanceIds.Add("mqtt-eremos");
        model.SinkInstanceIds.Add("mqtt-archive");

        var cfg = model.BuildRouteConfig();

        cfg.RouteId.Should().Be("jyoti17-to-eremos");
        cfg.Name.Should().Be("Jyoti #17 → EREMOS");
        cfg.SourceInstanceId.Should().Be("focas-cell-A");
        cfg.SinkInstanceIds.Should().BeEquivalentTo(new[] { "mqtt-eremos", "mqtt-archive" });
        cfg.Enabled.Should().BeFalse();
        cfg.Buffer.MaxDepth.Should().Be(50_000);
        cfg.Buffer.MaxAgeDays.Should().Be(30);
        cfg.Delivery.MaxRetries.Should().Be(10);
        cfg.Delivery.InitialBackoffMs.Should().Be(250);
    }

    [Fact]
    public void BuildRouteConfig_EmptyTransforms_ProjectsNullTransformProfile()
    {
        // Composition: the wizard's Transforms sub-model returns null on
        // its BuildTransformProfileConfig() when every sub-block is empty,
        // and the top-level model passes that null through unchanged.
        var model = MakeMinimallyValidModel();

        var cfg = model.BuildRouteConfig();

        cfg.Transforms.Should().BeNull("an empty transforms editor maps to identity (null profile)");
    }

    [Fact]
    public void BuildRouteConfig_WithTransforms_ProjectsViaSubModel()
    {
        var model = MakeMinimallyValidModel();
        model.Transforms.DeadbandRows.Add(new DeadbandRow
        {
            Tag = "spindle/speed",
            Mode = DeadbandMode.Percent,
            Threshold = 0.05,
        });
        model.Transforms.RateLimitRows.Add(new RateLimitRow
        {
            Tag = "alarms/active",
            MinIntervalMs = 5000,
        });

        var cfg = model.BuildRouteConfig();

        cfg.Transforms.Should().NotBeNull();
        cfg.Transforms!.DeadbandPercent.Should().NotBeNull();
        cfg.Transforms.DeadbandPercent!["spindle/speed"].Should().Be(0.05);
        cfg.Transforms.RateLimitMs.Should().NotBeNull();
        cfg.Transforms.RateLimitMs!["alarms/active"].Should().Be(5000);
    }

    [Fact]
    public void BuildRouteConfig_DefaultFilter_ProjectsAsteriskInclude()
    {
        var model = MakeMinimallyValidModel();

        var cfg = model.BuildRouteConfig();

        cfg.Filter.Include.Should().Equal("*");
        cfg.Filter.Exclude.Should().BeNull();
    }

    // ─── Eager validation (Locked N) ─────────────────────────────────────

    [Theory]
    [InlineData("jyoti17-to-eremos")]
    [InlineData("a")]
    [InlineData("Z9")]
    [InlineData("route.with.dots")]
    [InlineData("route_with_underscores")]
    [InlineData("route-with-hyphens")]
    public void ValidateRouteId_AcceptedByCoreRegex_Accepted(string id)
    {
        RouteWizardModel.ValidateRouteId(id).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ValidateRouteId_BlankOrWhitespace_Rejected(string? id)
    {
        var error = RouteWizardModel.ValidateRouteId(id);
        error.Should().NotBeNull();
        error.Should().Contain("required");
    }

    [Theory]
    [InlineData("-leading-hyphen")]   // must start with letter/digit
    [InlineData(".leading-dot")]
    [InlineData("_leading-underscore")]
    [InlineData("has space")]
    [InlineData("has/slash")]
    [InlineData("has@symbol")]
    public void ValidateRouteId_InvalidPattern_Rejected(string id)
    {
        var error = RouteWizardModel.ValidateRouteId(id);
        error.Should().NotBeNull();
        error.Should().Contain("letter or digit");
    }

    [Fact]
    public void ValidateRouteId_UsesCoresAttributePattern_NoParallelPattern()
    {
        // Locked N: the wizard's regex composes Core's RegularExpressionAttribute,
        // not a duplicate string literal. Verify by reading the attribute and
        // confirming the wizard accepts/rejects the same shapes Core's
        // attribute does.
        var property = typeof(RouteConfig).GetProperty(nameof(RouteConfig.RouteId));
        property.Should().NotBeNull();
        var attribute = property!.GetCustomAttribute<RegularExpressionAttribute>();
        attribute.Should().NotBeNull("RouteConfig.RouteId must carry its regex via attribute");

        // A value the Core attribute considers valid is accepted by the wizard.
        var coreAccepts = new System.Text.RegularExpressions.Regex(attribute!.Pattern).IsMatch("valid_id-1.2");
        coreAccepts.Should().BeTrue("sanity: Core's attribute should accept this");
        RouteWizardModel.ValidateRouteId("valid_id-1.2").Should().BeNull();

        // A value the Core attribute considers invalid is also rejected by the wizard.
        var coreRejects = new System.Text.RegularExpressions.Regex(attribute.Pattern).IsMatch("-bad");
        coreRejects.Should().BeFalse("sanity: Core's attribute should reject this");
        RouteWizardModel.ValidateRouteId("-bad").Should().NotBeNull();
    }

    [Fact]
    public void ValidateRouteId_OverLength_Rejected()
    {
        var id = new string('a', 129);

        var error = RouteWizardModel.ValidateRouteId(id);

        error.Should().NotBeNull();
        error.Should().Contain("128");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ValidateName_BlankOrWhitespace_Accepted(string? name)
    {
        // Operator-feedback fix 2026-05-28 — Name is now actually optional
        // (UI label said "(optional)" but ValidateName previously rejected
        // empty strings). BuildRouteConfig falls back to RouteId when Name
        // is empty so the resulting RouteConfig.Name is still non-empty.
        var error = RouteWizardModel.ValidateName(name);
        error.Should().BeNull();
    }

    [Fact]
    public void ValidateName_TooLong_Rejected()
    {
        // Length check is the only remaining ValidateName constraint.
        var error = RouteWizardModel.ValidateName(new string('a', 257));
        error.Should().NotBeNull();
    }

    // ─── Defensive projection guards ─────────────────────────────────────

    [Fact]
    public void BuildRouteConfig_BlankRouteId_Throws()
    {
        var model = MakeMinimallyValidModel();
        model.RouteId = "";

        var act = () => model.BuildRouteConfig();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void BuildRouteConfig_BlankName_FallsBackToRouteId()
    {
        // Operator-feedback fix 2026-05-28 — Name is optional; when empty
        // the projection uses RouteId so the resulting RouteConfig.Name
        // is never empty (Core's RouteConfig still wants a non-empty
        // display label for diagnostics + UI).
        var model = MakeMinimallyValidModel();
        model.Name = "";

        var result = model.BuildRouteConfig();

        result.Name.Should().Be(model.RouteId);
    }

    [Fact]
    public void BuildRouteConfig_BlankSourceInstanceId_Throws()
    {
        var model = MakeMinimallyValidModel();
        model.SourceInstanceId = "";

        var act = () => model.BuildRouteConfig();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SourceInstanceId*");
    }

    [Fact]
    public void BuildRouteConfig_EmptySinkSet_Throws()
    {
        var model = MakeMinimallyValidModel();
        model.SinkInstanceIds.Clear();

        var act = () => model.BuildRouteConfig();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*sink*");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static RouteWizardModel MakeMinimallyValidModel()
    {
        var model = new RouteWizardModel
        {
            RouteId = "r1",
            Name = "Route 1",
            SourceInstanceId = "src-1",
        };
        model.SinkInstanceIds.Add("sink-1");
        return model;
    }
}
