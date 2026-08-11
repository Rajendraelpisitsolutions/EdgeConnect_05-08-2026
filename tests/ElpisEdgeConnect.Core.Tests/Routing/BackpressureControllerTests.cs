// ============================================================================
// File: Routing/BackpressureControllerTests.cs
// Purpose: Phase 6 pin tests for BackpressureController. Covers the full
//          (channelHasRoom, bufferHasRoom, dropPolicy) decision matrix and
//          the structural guarantee that the controller carries no payload
//          types.
// Milestone: C3 Commit 2 (phase 6).
// ============================================================================

using System.Linq;
using System.Reflection;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Routing;

public sealed class BackpressureControllerTests
{
    [Theory]
    // channel has room → always Pass regardless of buffer / policy
    [InlineData(true, true, DropPolicy.DropOldest, BackpressureDecision.Pass)]
    [InlineData(true, false, DropPolicy.DropOldest, BackpressureDecision.Pass)]
    [InlineData(true, true, DropPolicy.DropNewest, BackpressureDecision.Pass)]
    [InlineData(true, false, DropPolicy.DropNewest, BackpressureDecision.Pass)]
    [InlineData(true, true, DropPolicy.Block, BackpressureDecision.Pass)]
    [InlineData(true, false, DropPolicy.Block, BackpressureDecision.Pass)]
    // channel full, buffer has room → always Spill regardless of policy
    [InlineData(false, true, DropPolicy.DropOldest, BackpressureDecision.Spill)]
    [InlineData(false, true, DropPolicy.DropNewest, BackpressureDecision.Spill)]
    [InlineData(false, true, DropPolicy.Block, BackpressureDecision.Spill)]
    // channel full, buffer full → depends on policy
    [InlineData(false, false, DropPolicy.DropOldest, BackpressureDecision.Drop)]
    [InlineData(false, false, DropPolicy.DropNewest, BackpressureDecision.Drop)]
    [InlineData(false, false, DropPolicy.Block, BackpressureDecision.Spill)]
    public void BackpressureController_DecisionMatrix_IsPinned(
        bool channelHasRoom,
        bool bufferHasRoom,
        DropPolicy policy,
        BackpressureDecision expected)
    {
        var c = new BackpressureController(policy);
        c.Decide(channelHasRoom, bufferHasRoom).Should().Be(expected);
    }

    [Fact]
    public void Controller_ExposesConfiguredDropPolicy()
    {
        new BackpressureController(DropPolicy.DropNewest).DropPolicy.Should().Be(DropPolicy.DropNewest);
    }
}

/// <summary>
/// Structural pin: <see cref="BackpressureController"/> is a policy-only
/// class. It must never accept a <see cref="CanonicalDataPoint"/> as a
/// parameter — payload movement is the mechanism's job
/// (<see cref="RouteChannelSpillover"/>).
/// </summary>
public sealed class BackpressureControllerStructureTests
{
    [Fact]
    public void ControllerMethods_DoNotAcceptCanonicalDataPointPayloads()
    {
        var forbidden = typeof(CanonicalDataPoint);
        var methods = typeof(BackpressureController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        foreach (var m in methods)
        {
            foreach (var p in m.GetParameters())
            {
                var t = p.ParameterType;
                var isCdp = t == forbidden
                    || (t.IsGenericType && t.GetGenericArguments().Any(a => a == forbidden))
                    || (t.IsArray && t.GetElementType() == forbidden);
                isCdp.Should().BeFalse(
                    $"BackpressureController.{m.Name} must not take a CanonicalDataPoint payload — " +
                    "policy must remain separate from mechanism.");
            }
        }
    }
}
