// ============================================================================
// File: LicenseTrialEnforcerTests.cs
// Purpose: Deterministic tests for the unlicensed runtime cutoff (ADR-0035).
//          The enforcement decision is a pure function (ShouldStop) over a
//          consumed-demo-budget accumulator, so the cutoff is verified without
//          any timers or Thread.Sleep. The budget is consumed ONLY while a
//          source is actively collecting data.
// Reference: docs/decisions/0035-unlicensed-runtime-cutoff.md
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Host;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Host.Tests;

public class LicenseTrialEnforcerTests
{
    private static readonly TimeSpan Trial = TimeSpan.FromHours(2);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    [Fact]
    public void ShouldStop_ValidLicense_NeverStops_AndResetsBudget()
    {
        var consumed = TimeSpan.FromHours(1.9); // pretend budget was partly spent

        var stop = LicenseTrialEnforcer.ShouldStop(
            LicenseStatus.Valid, Interval, collectingThisInterval: true, ref consumed, Trial);

        stop.Should().BeFalse();
        consumed.Should().Be(TimeSpan.Zero, "a valid license resets the demo budget");
    }

    [Fact]
    public void ShouldStop_NotCollecting_DoesNotConsumeBudget()
    {
        var consumed = TimeSpan.FromMinutes(10);

        var stop = LicenseTrialEnforcer.ShouldStop(
            LicenseStatus.NotLoaded, Interval, collectingThisInterval: false, ref consumed, Trial);

        stop.Should().BeFalse();
        consumed.Should().Be(TimeSpan.FromMinutes(10), "the budget pauses when no source is collecting");
    }

    [Fact]
    public void ShouldStop_Collecting_ConsumesBudgetByElapsed()
    {
        var consumed = TimeSpan.FromMinutes(10);

        LicenseTrialEnforcer.ShouldStop(
            LicenseStatus.NotLoaded, Interval, collectingThisInterval: true, ref consumed, Trial);

        consumed.Should().Be(TimeSpan.FromMinutes(10) + Interval, "collecting time is added to the spent budget");
    }

    [Fact]
    public void ShouldStop_CollectingBelowBudget_DoesNotStop()
    {
        var consumed = Trial - TimeSpan.FromMinutes(1);

        var stop = LicenseTrialEnforcer.ShouldStop(
            LicenseStatus.NotLoaded, TimeSpan.FromSeconds(1), collectingThisInterval: true, ref consumed, Trial);

        stop.Should().BeFalse();
    }

    [Theory]
    [InlineData(LicenseStatus.NotLoaded)]
    [InlineData(LicenseStatus.Expired)]
    [InlineData(LicenseStatus.InGracePeriod)]
    [InlineData(LicenseStatus.Invalid)]
    public void ShouldStop_AnyNonValidAtOrBeyondBudget_Stops(LicenseStatus status)
    {
        // ADR-0035: the cutoff applies to EVERY non-Valid status once the demo
        // budget of active collection is spent.
        var consumed = Trial - Interval;

        var stop = LicenseTrialEnforcer.ShouldStop(
            status, Interval, collectingThisInterval: true, ref consumed, Trial);

        stop.Should().BeTrue($"a {status} license must stop the gateway once the demo budget is consumed");
    }

    [Fact]
    public void ShouldStop_PausedWhileDisconnected_NeverStopsRegardlessOfWallClock()
    {
        // Simulate many intervals with NO collection (e.g. an unreachable device).
        var consumed = TimeSpan.Zero;
        for (var i = 0; i < 10_000; i++)
        {
            LicenseTrialEnforcer.ShouldStop(
                LicenseStatus.NotLoaded, Interval, collectingThisInterval: false, ref consumed, Trial)
                .Should().BeFalse();
        }

        consumed.Should().Be(TimeSpan.Zero, "wall-clock time never burns the demo budget while idle");
    }

    [Fact]
    public void ShouldStop_ValidThenUnlicensedAgain_ResumesFromZeroBudget()
    {
        var consumed = TimeSpan.Zero;

        // Collect unlicensed for a while.
        for (var i = 0; i < 60; i++) // 60 * 30s = 30 min
        {
            LicenseTrialEnforcer.ShouldStop(LicenseStatus.NotLoaded, Interval, true, ref consumed, Trial);
        }
        consumed.Should().Be(TimeSpan.FromMinutes(30));

        // A valid license arrives → budget resets.
        LicenseTrialEnforcer.ShouldStop(LicenseStatus.Valid, Interval, true, ref consumed, Trial);
        consumed.Should().Be(TimeSpan.Zero);

        // License lapses again → a fresh full budget must be consumed before stopping.
        var stoppedEarly = false;
        for (var i = 0; i < 239; i++) // 239 * 30s = 119.5 min < 2h
        {
            stoppedEarly |= LicenseTrialEnforcer.ShouldStop(LicenseStatus.Expired, Interval, true, ref consumed, Trial);
        }
        stoppedEarly.Should().BeFalse("the budget restarts from zero after a valid spell");

        // Crossing 2h of the new spell stops it.
        LicenseTrialEnforcer.ShouldStop(LicenseStatus.Expired, Interval, true, ref consumed, Trial)
            .Should().BeTrue();
    }

    [Fact]
    public void ResolveTrialDuration_DefaultsToTwoHours_WhenEnvUnset()
    {
        var original = Environment.GetEnvironmentVariable(LicenseTrialEnforcer.TrialMinutesEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(LicenseTrialEnforcer.TrialMinutesEnvVar, null);

            LicenseTrialEnforcer.ResolveTrialDuration().Should().Be(TimeSpan.FromHours(2));
        }
        finally
        {
            Environment.SetEnvironmentVariable(LicenseTrialEnforcer.TrialMinutesEnvVar, original);
        }
    }

    [Theory]
    [InlineData("5", 5)]
    [InlineData("120", 120)]
    [InlineData("0.5", 0.5)] // fractional minutes allowed (0.5 min = 30s)
    public void ResolveTrialDuration_HonorsEnvOverride(string raw, double expectedMinutes)
    {
        var original = Environment.GetEnvironmentVariable(LicenseTrialEnforcer.TrialMinutesEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(LicenseTrialEnforcer.TrialMinutesEnvVar, raw);

            LicenseTrialEnforcer.ResolveTrialDuration()
                .Should().Be(TimeSpan.FromMinutes(expectedMinutes));
        }
        finally
        {
            Environment.SetEnvironmentVariable(LicenseTrialEnforcer.TrialMinutesEnvVar, original);
        }
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-30")]
    public void ResolveTrialDuration_IgnoresInvalidOrNonPositive_FallsBackToDefault(string raw)
    {
        var original = Environment.GetEnvironmentVariable(LicenseTrialEnforcer.TrialMinutesEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(LicenseTrialEnforcer.TrialMinutesEnvVar, raw);

            LicenseTrialEnforcer.ResolveTrialDuration().Should().Be(TimeSpan.FromHours(2));
        }
        finally
        {
            Environment.SetEnvironmentVariable(LicenseTrialEnforcer.TrialMinutesEnvVar, original);
        }
    }
}
