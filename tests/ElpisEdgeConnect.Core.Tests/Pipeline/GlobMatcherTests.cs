// ============================================================================
// File: Pipeline/GlobMatcherTests.cs
// Covers: the * ? ** glob dialect locked by C1 D6.
// ============================================================================

using ElpisEdgeConnect.Core.Pipeline.Steps;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Pipeline;

public sealed class GlobMatcherTests
{
    [Theory]
    [InlineData("*", "Spindle/Speed", true)]
    [InlineData("**", "Spindle/Speed", true)]
    [InlineData("Spindle/Speed", "Spindle/Speed", true)]
    [InlineData("Spindle/Speed", "Spindle/Load", false)]
    [InlineData("Spindle/*", "Spindle/Speed", true)]
    [InlineData("Spindle/*", "Spindle/Sub/Speed", false)]
    [InlineData("Spindle/**", "Spindle/Sub/Speed", true)]
    [InlineData("**/Speed", "Axis/1/Speed", true)]
    [InlineData("**/Speed", "Axis/Speed", true)]
    [InlineData("**/Speed", "Axis/Load", false)]
    [InlineData("Axis/?/Speed", "Axis/1/Speed", true)]
    [InlineData("Axis/?/Speed", "Axis/12/Speed", false)]
    [InlineData("Axis/?/Speed", "Axis//Speed", false)]
    [InlineData("Spindle/Speed", "spindle/speed", false)] // case sensitive
    public void IsMatch_Pattern_ReturnsExpected(string pattern, string path, bool expected)
    {
        GlobMatcher.Compile(pattern).IsMatch(path).Should().Be(expected);
    }

    [Fact]
    public void Compile_Empty_Throws()
    {
        System.Action act = () => GlobMatcher.Compile(" ");
        act.Should().Throw<System.ArgumentException>();
    }

    [Fact]
    public void MatchAll_IsSingleton()
    {
        GlobMatcher.Compile("*").Should().BeSameAs(GlobMatcher.MatchAll);
        GlobMatcher.Compile("**").Should().BeSameAs(GlobMatcher.MatchAll);
    }
}
