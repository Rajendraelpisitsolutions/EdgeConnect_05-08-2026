// ============================================================================
// Tests: Focas2DemoModeOptions — pins the env-var parser contract from
//        M.2b.3.1 plan v2 §5.4 + user clarification 2026-05-18:
//          Truthy:   true, 1, yes  (case-insensitive, trimmed)
//          Falsy:    false, 0, no, empty, unset
//        Plus the cached-first-read invariant from Locked F.
//
//        Tests are serialised via [Collection("Focas2DemoMode")] because
//        they manipulate a process-wide env var + a static cache. Each
//        test wraps cleanup in try/finally so test order doesn't matter.
// Reference: docs/sessions/2026-05-18-mp2b31-focas2-demo-mode-plan-v2.md §5.4
// ============================================================================

using System;
using ElpisEdgeConnect.Sources.Focas2;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Focas2.Tests;

/// <summary>
/// Serialises all tests that touch <see cref="Focas2DemoModeOptions"/>'s
/// static cache or the process-wide env var. xUnit runs tests in the
/// same collection sequentially.
/// </summary>
[CollectionDefinition("Focas2DemoMode", DisableParallelization = true)]
public sealed class Focas2DemoModeCollection { }

[Collection("Focas2DemoMode")]
public sealed class Focas2DemoModeOptionsTests
{
    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("Yes")]
    [InlineData("YES")]
    [InlineData("  true  ")]  // surrounding whitespace tolerated
    public void TruthyVariants_AreRecognised(string envValue)
    {
        WithFreshCacheAndEnv(envValue, () =>
            Focas2DemoModeOptions.IsEnabled.Should().BeTrue(
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
            Focas2DemoModeOptions.IsEnabled.Should().BeFalse(
                $"'{envValue}' should leave demo mode disabled"));
    }

    [Fact]
    public void UnsetEnvVar_DisablesMode()
    {
        WithFreshCacheAndEnv(envValue: null, () =>
            Focas2DemoModeOptions.IsEnabled.Should().BeFalse());
    }

    [Fact]
    public void Cache_IsImmutableAfterFirstRead()
    {
        // Pins Locked F: toggling requires restart. Once IsEnabled has
        // been read for the first time, subsequent env-var changes do
        // NOT affect the cached value.
        try
        {
            Focas2DemoModeOptions.Reset();

            Environment.SetEnvironmentVariable(Focas2DemoModeOptions.EnvVarName, "true");
            var first = Focas2DemoModeOptions.IsEnabled;
            first.Should().BeTrue();

            // Mid-process env-var change — cache should ignore it.
            Environment.SetEnvironmentVariable(Focas2DemoModeOptions.EnvVarName, "false");
            var second = Focas2DemoModeOptions.IsEnabled;
            second.Should().BeTrue("once cached, IsEnabled is frozen for the process lifetime");
        }
        finally
        {
            Environment.SetEnvironmentVariable(Focas2DemoModeOptions.EnvVarName, null);
            Focas2DemoModeOptions.Reset();
        }
    }

    private static void WithFreshCacheAndEnv(string? envValue, Action act)
    {
        try
        {
            Focas2DemoModeOptions.Reset();
            Environment.SetEnvironmentVariable(Focas2DemoModeOptions.EnvVarName, envValue);
            act();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Focas2DemoModeOptions.EnvVarName, null);
            Focas2DemoModeOptions.Reset();
        }
    }
}
