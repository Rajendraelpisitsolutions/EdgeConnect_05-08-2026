// ============================================================================
// File: Diagnostics/TapValuePrivacyTests.cs
// Covers: ADR-0018A tap value-privacy policy + masker (Live Tap M1.5). Exact /
//         glob / case-insensitive matching, empty-policy no-op, and value-only
//         masking that preserves identity/type/quality/timestamps.
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Model;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Diagnostics;

public sealed class TapValuePrivacyTests
{
    private static CanonicalDataPoint P(string tag, object value, CanonicalValueType type) =>
        new CanonicalDataPointBuilder()
            .WithGateway("GW").WithSource("src", "mock").WithDevice("dev")
            .WithTag(tag, tag)
            .WithValue(value, type)
            .WithGoodQuality(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            .WithSequence(1)
            .Build();

    // ---- Policy ----

    [Fact]
    public void EmptyPolicy_MatchesNothing()
    {
        SensitiveTagPolicy.Empty.HasRules.Should().BeFalse();
        SensitiveTagPolicy.Empty.IsSensitive("anything").Should().BeFalse();
        new SensitiveTagPolicy(new[] { "  ", "" }).HasRules.Should().BeFalse();
    }

    [Theory]
    [InlineData("recipe/secret_setpoint", "recipe/secret_setpoint", true)]   // exact
    [InlineData("recipe/secret_setpoint", "recipe/other", false)]            // exact, no match
    [InlineData("recipe/*", "recipe/secret_setpoint", true)]                 // glob run
    [InlineData("recipe/*", "production/parts_count", false)]                // glob, no match
    [InlineData("RECIPE/Secret_Setpoint", "recipe/secret_setpoint", true)]   // case-insensitive
    [InlineData("tag?", "tag5", true)]                                       // single-char glob
    [InlineData("tag?", "tag55", false)]                                     // single-char glob too long
    public void Policy_MatchesExactAndGlob(string pattern, string tag, bool expected)
    {
        new SensitiveTagPolicy(new[] { pattern }).IsSensitive(tag).Should().Be(expected);
    }

    [Fact]
    public void Policy_AnyPatternMatching_IsSensitive()
    {
        var policy = new SensitiveTagPolicy(new[] { "recipe/*", "auth/token" });
        policy.IsSensitive("auth/token").Should().BeTrue();
        policy.IsSensitive("recipe/x").Should().BeTrue();
        policy.IsSensitive("spindle/speed").Should().BeFalse();
    }

    // ---- Masker ----

    [Fact]
    public void Masker_SensitiveTag_MasksValueOnly_PreservesEverythingElse()
    {
        var masker = new TapValueMasker(new SensitiveTagPolicy(new[] { "recipe/*" }));
        var original = P("recipe/secret_setpoint", 1234, CanonicalValueType.Integer);

        var masked = masker.Mask(original);

        masked.Value.Should().Be("***");
        masked.ValueType.Should().Be(CanonicalValueType.Integer, "type is preserved; only the value is masked");
        masked.TagName.Should().Be(original.TagName);
        masked.Quality.Should().Be(original.Quality);
        masked.DeviceTimestamp.Should().Be(original.DeviceTimestamp);
        masked.SequenceNumber.Should().Be(original.SequenceNumber);
    }

    [Fact]
    public void Masker_NonSensitiveTag_PassesThroughUnchanged()
    {
        var masker = new TapValueMasker(new SensitiveTagPolicy(new[] { "recipe/*" }));
        var original = P("spindle/speed", 3000.0, CanonicalValueType.Double);

        var result = masker.Mask(original);

        result.Should().BeSameAs(original, "no allocation when the tag is not sensitive");
    }

    [Fact]
    public void Masker_EmptyPolicy_PassesThroughUnchanged()
    {
        var masker = new TapValueMasker(SensitiveTagPolicy.Empty);
        masker.HasRules.Should().BeFalse();
        var original = P("recipe/secret_setpoint", 1, CanonicalValueType.Integer);
        masker.Mask(original).Should().BeSameAs(original);
    }
}
