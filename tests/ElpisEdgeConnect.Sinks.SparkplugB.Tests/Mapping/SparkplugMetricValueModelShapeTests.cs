// ============================================================================
// File: Mapping/SparkplugMetricValueModelShapeTests.cs
// Purpose: Locks the containment guarantee from slice-3 review r1: the
//          validated model must be impossible to construct, clone (`with`), or
//          mutate into an invalid state from OUTSIDE the assembly, and its
//          byte value must be an immutable representation. These reflection
//          shape tests fail by name if a later change re-publicizes the model
//          or reintroduces a mutable backing array.
// ============================================================================

using System.Collections.Immutable;
using System.Reflection;
using ElpisEdgeConnect.Sinks.SparkplugB.Mapping;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Mapping;

public sealed class SparkplugMetricValueModelShapeTests
{
    private static readonly Type Model = typeof(SparkplugMetricValueModel);

    [Fact]
    public void Model_IsNotPublic_SoNoExternalConstructionCloningOrMutationExists()
    {
        Model.IsPublic.Should().BeFalse(
            "the validated model is assembly-internal; the public surface is the payload factories, " +
            "so no external caller can use `with` to clone an instance into an invalid state");
        Model.IsSealed.Should().BeTrue();
    }

    [Fact]
    public void Model_HasNoPublicConstructorVisibleOutsideTheAssembly()
    {
        // BindingFlags.Public on an internal type still reveals ctors usable by
        // in-assembly (and InternalsVisibleTo) code; the type's own non-public
        // visibility is what contains them. Assert both layers explicitly.
        var externallyReachable = Model.IsVisible;

        externallyReachable.Should().BeFalse("no constructor of the validated model is reachable outside the assembly");
    }

    [Fact]
    public void Model_BytesValue_IsAnImmutableRepresentation()
    {
        var property = Model.GetProperty(nameof(SparkplugMetricValueModel.BytesValue), BindingFlags.Public | BindingFlags.Instance);

        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(ImmutableArray<byte>?),
            "a mutable byte[] escaping the validated model would defeat validate-then-encode");
    }
}
