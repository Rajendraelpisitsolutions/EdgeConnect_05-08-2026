// ============================================================================
// Tests: S7 demo mode — env-var parser/cache contract (S7DemoModeOptions) and
//        the production-ctor dispatch (S7SourceAdapter picks S7DemoClient vs
//        Sharp7Client). Mirrors the FOCAS2 demo-mode tests. These touch the
//        process env var + a frozen static cache, so they run serialized.
// ============================================================================

using System;
using ElpisEdgeConnect.Sources.S7;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.S7.Tests;

/// <summary>Serializes every test that touches EDGECONNECT_S7_FAKE_MODE / the static cache.</summary>
[CollectionDefinition("S7DemoMode", DisableParallelization = true)]
public sealed class S7DemoModeCollection { }

[Collection("S7DemoMode")]
public class S7DemoModeOptionsTests
{
    private static bool ReadWith(string? value)
    {
        Environment.SetEnvironmentVariable(S7DemoModeOptions.EnvVarName, value);
        S7DemoModeOptions.Reset();
        try
        {
            return S7DemoModeOptions.IsEnabled;
        }
        finally
        {
            Environment.SetEnvironmentVariable(S7DemoModeOptions.EnvVarName, null);
            S7DemoModeOptions.Reset();
        }
    }

    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("  true  ")]
    public void IsEnabled_TruthyValues_AreEnabled(string value) => ReadWith(value).Should().BeTrue();

    [Theory]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("no")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("anything-else")]
    public void IsEnabled_FalsyValues_AreDisabled(string value) => ReadWith(value).Should().BeFalse();

    [Fact]
    public void IsEnabled_Unset_IsDisabled() => ReadWith(null).Should().BeFalse();

    [Fact]
    public void IsEnabled_IsCachedAfterFirstRead()
    {
        Environment.SetEnvironmentVariable(S7DemoModeOptions.EnvVarName, "true");
        S7DemoModeOptions.Reset();
        try
        {
            S7DemoModeOptions.IsEnabled.Should().BeTrue();
            // Mid-process change is ignored until Reset (toggling needs restart).
            Environment.SetEnvironmentVariable(S7DemoModeOptions.EnvVarName, "false");
            S7DemoModeOptions.IsEnabled.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(S7DemoModeOptions.EnvVarName, null);
            S7DemoModeOptions.Reset();
        }
    }
}

[Collection("S7DemoMode")]
public class S7SourceAdapterDemoDispatchTests
{
    [Fact]
    public void ProductionCtor_DemoOff_UsesSharp7Client()
    {
        Environment.SetEnvironmentVariable(S7DemoModeOptions.EnvVarName, null);
        S7DemoModeOptions.Reset();
        try
        {
            var adapter = new S7SourceAdapter("s7-prod", NullLogger<S7SourceAdapter>.Instance);
            adapter.ClientForTesting.Should().BeOfType<Sharp7Client>();
        }
        finally
        {
            S7DemoModeOptions.Reset();
        }
    }

    [Fact]
    public void ProductionCtor_DemoOn_UsesDemoClient()
    {
        Environment.SetEnvironmentVariable(S7DemoModeOptions.EnvVarName, "true");
        S7DemoModeOptions.Reset();
        try
        {
            var adapter = new S7SourceAdapter("s7-demo", NullLogger<S7SourceAdapter>.Instance);
            adapter.ClientForTesting.Should().BeOfType<S7DemoClient>();
        }
        finally
        {
            Environment.SetEnvironmentVariable(S7DemoModeOptions.EnvVarName, null);
            S7DemoModeOptions.Reset();
        }
    }
}
