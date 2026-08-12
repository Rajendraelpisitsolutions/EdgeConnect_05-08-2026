// ============================================================================
// Tests: TopicShapeCollisionTests — pins the v2 plan §5.3 collision-
// detection subgate of Gate 4. Standalone unit tests; no broker / no
// gateway / no MQTT — purely exercise TopicShapeAnalyzer against the
// production MqttTopicResolver sanitization rule.
//
// Reference: docs/sessions/2026-05-21-eremos-v2-revalidation-plan-v2.md §5.3
// ============================================================================

using System.Linq;
using ElpisEdgeConnect.Integration.Tests.Eremos;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Integration.Tests.Eremos;

public sealed class TopicShapeCollisionTests
{
    [Fact]
    public void DetectCollisions_AllDistinctSanitizedSegments_ReturnsZeroCollisions()
    {
        // Brother + FOCAS2 canonical tag paths — distinct sanitized forms.
        var paths = new[]
        {
            "Status/RunState",        // → Status_RunState
            "Status/Mode",            // → Status_Mode
            "MachineInfo/Hostname",   // → MachineInfo_Hostname
            "MachineInfo/StatusCode", // → MachineInfo_StatusCode
            "Tools/Active/Number",    // → Tools_Active_Number
        };

        var collisions = TopicShapeAnalyzer.DetectCollisions(paths);
        collisions.Should().BeEmpty();
    }

    [Fact]
    public void DetectCollisions_TwoSlashesVsUnderscore_FlagsCollision()
    {
        // Status/Run/State  → Status_Run_State
        // Status_Run/State  → Status_Run_State  ← COLLISION
        var paths = new[]
        {
            "Status/Run/State",
            "Status_Run/State",
        };

        var collisions = TopicShapeAnalyzer.DetectCollisions(paths);
        collisions.Should().HaveCount(1);
        collisions[0].MqttSegment.Should().Be("Status_Run_State");
        collisions[0].CollidingCanonicalPaths.Should().BeEquivalentTo(new[]
        {
            "Status/Run/State",
            "Status_Run/State",
        });
    }

    [Fact]
    public void DetectCollisions_ThreeWayCollision_FlagsAllPaths()
    {
        // All three sanitize to Status_Run_State.
        var paths = new[]
        {
            "Status/Run/State",
            "Status_Run/State",
            "Status/Run_State",
        };

        var collisions = TopicShapeAnalyzer.DetectCollisions(paths);
        collisions.Should().HaveCount(1);
        collisions[0].CollidingCanonicalPaths.Should().HaveCount(3);
    }

    [Fact]
    public void DetectCollisions_HyphenAndUnderscoreVariants_DoNotCollide()
    {
        // Status/Run-State → Status_Run-State
        // Status_Run/State → Status_Run_State
        // The hyphen vs underscore difference SURVIVES sanitization,
        // so these are NOT collisions.
        var paths = new[]
        {
            "Status/Run-State",
            "Status_Run/State",
        };

        var collisions = TopicShapeAnalyzer.DetectCollisions(paths);
        collisions.Should().BeEmpty();
    }

    [Fact]
    public void DetectCollisions_DuplicatePaths_AreDeduplicatedBeforeAnalysis()
    {
        // Same canonical path twice — not a collision (just a dup).
        var paths = new[]
        {
            "Status/RunState",
            "Status/RunState",
        };

        var collisions = TopicShapeAnalyzer.DetectCollisions(paths);
        collisions.Should().BeEmpty();
    }

    [Fact]
    public void IsValidTopicShape_Phase0CompliantTopic_Matches()
    {
        TopicShapeAnalyzer.IsValidTopicShape("eremos/gw-1/cnc/brother-line1/Status_RunState")
            .Should().BeTrue();
        TopicShapeAnalyzer.IsValidTopicShape("eremos/gw-1/cnc/brother-line1/Tools_Magazine_3_ToolNumber")
            .Should().BeTrue();
        TopicShapeAnalyzer.IsValidTopicShape("eremos/GW-CUSTOMER-A-LINE1/cnc/Brother-Parity/MachineInfo_Hostname")
            .Should().BeTrue("case-preserved sanitization should still match the Phase 0 regex");
    }

    [Fact]
    public void IsValidTopicShape_NonEremosPrefix_DoesNotMatch()
    {
        TopicShapeAnalyzer.IsValidTopicShape("notEremos/gw-1/cnc/src/Tag").Should().BeFalse();
        TopicShapeAnalyzer.IsValidTopicShape("/eremos/gw-1/cnc/src/Tag").Should().BeFalse();
    }

    [Fact]
    public void IsValidTopicShape_WrongSegmentCount_DoesNotMatch()
    {
        TopicShapeAnalyzer.IsValidTopicShape("eremos/gw-1/cnc/src").Should().BeFalse(); // 4 segments
        TopicShapeAnalyzer.IsValidTopicShape("eremos/gw-1/cnc/src/tag/extra").Should().BeFalse(); // 6 segments
    }

    [Fact]
    public void IsValidTopicShape_NullOrEmpty_DoesNotMatch()
    {
        TopicShapeAnalyzer.IsValidTopicShape(null!).Should().BeFalse();
        TopicShapeAnalyzer.IsValidTopicShape("").Should().BeFalse();
    }

    [Fact]
    public void DetectCollisions_EmptyAndNullPathsAreSilentlySkipped()
    {
        var paths = new[]
        {
            "Status/RunState",
            "",      // skipped
            null!,   // skipped
            "Status/Mode",
        };

        var collisions = TopicShapeAnalyzer.DetectCollisions(paths);
        collisions.Should().BeEmpty();
    }
}
