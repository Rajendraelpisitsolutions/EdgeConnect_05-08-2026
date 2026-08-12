// ============================================================================
// Tests: BrotherHttpDemoModeOptions — pins the env-var parser contract
//        and the cached-first-read invariant. Mirrors Focas2DemoModeOptions
//        tests almost exactly with EDGECONNECT_BROTHER_FAKE_MODE.
//
//        Tests serialise via [Collection("BrotherDemoMode")] because they
//        manipulate process-wide env var + static cache. Each test wraps
//        cleanup in try/finally so test order doesn't matter.
// Reference: src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherHttpDemoModeOptions.cs
// ============================================================================

using System;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.BrotherHttp.Tests;

[CollectionDefinition("BrotherDemoMode", DisableParallelization = true)]
public sealed class BrotherDemoModeCollection { }

[Collection("BrotherDemoMode")]
public sealed class BrotherHttpDemoModeOptionsTests
{
    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("Yes")]
    [InlineData("YES")]
    [InlineData("  true  ")]
    public void TruthyVariants_AreRecognised(string envValue)
    {
        WithFreshCacheAndEnv(envValue, () =>
            BrotherHttpDemoModeOptions.IsEnabled.Should().BeTrue(
                $"'{envValue}' should activate demo mode"));
    }

    [Theory]
    [InlineData("false")]
    [InlineData("False")]
    [InlineData("0")]
    [InlineData("no")]
    [InlineData("No")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("anything-else")]
    public void FalsyVariants_DisableMode(string envValue)
    {
        WithFreshCacheAndEnv(envValue, () =>
            BrotherHttpDemoModeOptions.IsEnabled.Should().BeFalse(
                $"'{envValue}' should leave demo mode disabled"));
    }

    [Fact]
    public void UnsetEnvVar_DisablesMode()
    {
        WithFreshCacheAndEnv(envValue: null, () =>
            BrotherHttpDemoModeOptions.IsEnabled.Should().BeFalse());
    }

    [Fact]
    public void Cache_IsImmutableAfterFirstRead()
    {
        try
        {
            BrotherHttpDemoModeOptions.Reset();

            Environment.SetEnvironmentVariable(BrotherHttpDemoModeOptions.EnvVarName, "true");
            var first = BrotherHttpDemoModeOptions.IsEnabled;
            first.Should().BeTrue();

            Environment.SetEnvironmentVariable(BrotherHttpDemoModeOptions.EnvVarName, "false");
            var second = BrotherHttpDemoModeOptions.IsEnabled;
            second.Should().BeTrue("once cached, IsEnabled is frozen for the process lifetime");
        }
        finally
        {
            Environment.SetEnvironmentVariable(BrotherHttpDemoModeOptions.EnvVarName, null);
            BrotherHttpDemoModeOptions.Reset();
        }
    }

    [Fact]
    public void StartupCriticalMessage_IsDistinctlyGreppable()
    {
        // Pins the grep-friendly phrasing — log monitoring relies on it to
        // distinguish "demo mode active" from real system failures.
        BrotherHttpDemoModeOptions.StartupCriticalMessage
            .Should().Contain("BROTHER HTTP FAKE MODE ACTIVE")
            .And.Contain(BrotherHttpDemoModeOptions.EnvVarName);
    }

    private static void WithFreshCacheAndEnv(string? envValue, Action act)
    {
        try
        {
            BrotherHttpDemoModeOptions.Reset();
            Environment.SetEnvironmentVariable(BrotherHttpDemoModeOptions.EnvVarName, envValue);
            act();
        }
        finally
        {
            Environment.SetEnvironmentVariable(BrotherHttpDemoModeOptions.EnvVarName, null);
            BrotherHttpDemoModeOptions.Reset();
        }
    }
}
