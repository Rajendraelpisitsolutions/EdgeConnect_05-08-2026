// ============================================================================
// File: Identity/SparkplugIdentityTypesTests.cs
// Purpose: Locks the slice-4 identity domain types: topic-safe Edge Node
//          identity, the Edge-Node-scoped alias key (no RouteId; unambiguous
//          source-qualified metric name; ordinal ordering), and the 0..255
//          constrained seq/bdSeq types (review amendment A).
// ============================================================================

using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Sinks.SparkplugB;
using ElpisEdgeConnect.Sinks.SparkplugB.Identity;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Identity;

public sealed class SparkplugIdentityTypesTests
{
    // ==== SparkplugEdgeNodeIdentity ====

    [Fact]
    public void EdgeNodeIdentity_Create_ValidElements_Succeeds()
    {
        var identity = SparkplugEdgeNodeIdentity.Create("PlantA", "gateway-01");

        identity.GroupId.Should().Be("PlantA");
        identity.EdgeNodeId.Should().Be("gateway-01");
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("a+b")]
    [InlineData("a#b")]
    public void EdgeNodeIdentity_Create_ForbiddenElement_ThrowsIdentityInvalid(string bad)
    {
        var act = () => SparkplugEdgeNodeIdentity.Create(bad, "edge");

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.IdentityInvalid);
    }

    [Fact]
    public void EdgeNodeIdentity_Create_ForbiddenEdgeNodeElement_ThrowsIdentityInvalid()
    {
        var act = () => SparkplugEdgeNodeIdentity.Create("group", "edge/node");

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.IdentityInvalid);
    }

    // ==== SparkplugAliasKey ====

    [Fact]
    public void AliasKey_Create_BuildsSourceQualifiedMetricName()
    {
        var key = SparkplugAliasKey.Create("cnc-01", "machine-3", "axes/x/load");

        key.MetricName.Should().Be("cnc-01/machine-3/axes/x/load");
    }

    [Fact]
    public void AliasKey_FromCanonical_MirrorsCoreIdentityComponents()
    {
        var canonical = CanonicalMetricKey.Create("src", "dev", "path/leaf");

        var key = SparkplugAliasKey.FromCanonical(canonical);

        key.SourceInstanceId.Should().Be("src");
        key.DeviceId.Should().Be("dev");
        key.TagPath.Should().Be("path/leaf");
    }

    [Theory]
    [InlineData("a/b", "dev", "tag")]  // slash in source → ambiguous join
    [InlineData("src", "a/b", "tag")]  // slash in device → ambiguous join
    [InlineData("", "dev", "tag")]
    [InlineData("src", "", "tag")]
    [InlineData("src", "dev", "")]
    [InlineData("src", "dev", "/leading")]
    [InlineData("src", "dev", "trailing/")]
    public void AliasKey_Create_InvalidComponent_ThrowsIdentityInvalid(string source, string device, string tagPath)
    {
        var act = () => SparkplugAliasKey.Create(source, device, tagPath);

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.IdentityInvalid);
    }

    [Fact]
    public void AliasKey_CompareTo_OrdersByOrdinalMetricName()
    {
        var keys = new[]
        {
            SparkplugAliasKey.Create("b", "d", "t"),
            SparkplugAliasKey.Create("a", "d", "z"),
            SparkplugAliasKey.Create("a", "d", "a"),
        };

        var sorted = keys.OrderBy(k => k).Select(k => k.MetricName).ToArray();

        sorted.Should().ContainInOrder("a/d/a", "a/d/z", "b/d/t");
    }

    [Fact]
    public void AliasKey_Equality_IsValueBased_WithoutRouteId()
    {
        var first = SparkplugAliasKey.Create("src", "dev", "tag");
        var second = SparkplugAliasKey.Create("src", "dev", "tag");

        first.Should().Be(second, "the alias key is Edge-Node-scoped value identity (no RouteId), so a recreated route resolves the same alias");
    }

    // ==== Constrained sequence types ====

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    public void SequenceTypes_Create_BoundaryValues_Succeed(int value)
    {
        SparkplugSequenceNumber.Create(value).Value.Should().Be((byte)value);
        SparkplugBirthDeathSequence.Create(value).Value.Should().Be((byte)value);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void SequenceTypes_Create_OutOfRange_Throws(int value)
    {
        var seq = () => SparkplugSequenceNumber.Create(value);
        var bdSeq = () => SparkplugBirthDeathSequence.Create(value);

        seq.Should().Throw<ArgumentOutOfRangeException>();
        bdSeq.Should().Throw<ArgumentOutOfRangeException>();
    }
}
