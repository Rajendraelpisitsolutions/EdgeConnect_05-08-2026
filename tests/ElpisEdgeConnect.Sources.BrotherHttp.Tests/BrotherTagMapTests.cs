// ============================================================================
// Tests: BrotherTagMap — catalog completeness + structural-purity contract.
//        The structural-purity test is the load-bearing guardrail from
//        v3.1 §5: BrotherTagMapEntry MUST expose only TagName/TagPath/
//        ValueType/Unit/Description. Any future PR that adds a member
//        (e.g., LegacyFieldName, a value transform) coupling production
//        mapping rules to legacy parser assumptions fails this test loudly.
// Reference: docs/sessions/2026-05-21-mp24-brother-http-plan-v3.md §5, §6
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ElpisEdgeConnect.Core.Model;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.BrotherHttp.Tests;

public sealed class BrotherTagMapTests
{
    // ── Structural-purity contract (v3.1 §5 load-bearing guardrail) ──────

    [Fact]
    public void BrotherTagMapEntry_ExposesOnlyStructuralMembers()
    {
        // The locked surface: TagName, TagPath, ValueType, Unit, Description.
        // Anything else fails the test loudly.
        var publicMembers = typeof(BrotherTagMapEntry)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.MemberType is MemberTypes.Property)
            .Select(m => m.Name)
            .ToHashSet();

        publicMembers.Should().BeEquivalentTo(new[]
        {
            nameof(BrotherTagMapEntry.TagName),
            nameof(BrotherTagMapEntry.TagPath),
            nameof(BrotherTagMapEntry.ValueType),
            nameof(BrotherTagMapEntry.Unit),
            nameof(BrotherTagMapEntry.Description),
        }, "v3.1 §5 lock — BrotherTagMapEntry surface is structural-only");
    }

    // ── Catalog completeness ──────────────────────────────────────────────

    [Fact]
    public void StaticTags_ContainsExpectedNonTemplatedEntries()
    {
        // Pins v3 §6 catalog (static rows from each endpoint table).
        // Count: 9 from /HTTPD_MCNINFO + 7 from /MNTP_CYCLETIME + 1 from
        // /MNTP_WKCNTR (PartsCount) + 2 from /ATC_TOOLS (ActiveNumber,
        // MagazineSize) + 1 from /ALARM_CURALMLIST (ActiveCount) + 4 from
        // /MNTP_MAINTNOTICE (Warning, WarningCount, NoticeCount, DueSummary).
        BrotherTagMap.StaticTags.Should().HaveCount(24);
    }

    [Fact]
    public void StaticTags_AllPathsAreUnique()
    {
        var paths = BrotherTagMap.StaticTags.Select(e => e.TagPath).ToList();
        paths.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void StaticTags_AllTagNamesAreLowercase()
    {
        foreach (var entry in BrotherTagMap.StaticTags)
        {
            entry.TagName.Should().Be(entry.TagName.ToLowerInvariant(),
                $"TagName '{entry.TagName}' must be lowercase per the FOCAS2-parity convention");
        }
    }

    [Fact]
    public void StaticTags_AllTagPathsUsePascalCase()
    {
        foreach (var entry in BrotherTagMap.StaticTags)
        {
            // Each segment after a '/' should start with an uppercase letter.
            var segments = entry.TagPath.Split('/');
            foreach (var seg in segments)
            {
                seg.Should().NotBeEmpty();
                char.IsUpper(seg[0]).Should().BeTrue(
                    $"TagPath segment '{seg}' in '{entry.TagPath}' must start uppercase");
            }
        }
    }

    [Theory]
    [InlineData("MachineInfo/Hostname", typeof(string))]
    [InlineData("MachineInfo/StatusCode", typeof(int))]
    [InlineData("Status/EmergencyStop", typeof(bool))]
    [InlineData("CycleTime/Cycle", typeof(double))]
    [InlineData("Production/PartsCount", typeof(long))]
    [InlineData("Alarms/ActiveCount", typeof(int))]
    public void StaticTags_KnownPathsCarryExpectedValueTypes(string tagPath, System.Type expectedClrType)
    {
        var entry = BrotherTagMap.StaticTags.SingleOrDefault(e => e.TagPath == tagPath);
        entry.Should().NotBeNull($"catalog must contain '{tagPath}'");

        // Coerce CanonicalValueType → the CLR primitive it conventionally maps to.
        var canonical = entry!.ValueType;
        var clrType = canonical switch
        {
            CanonicalValueType.String => typeof(string),
            CanonicalValueType.Boolean => typeof(bool),
            CanonicalValueType.Integer => typeof(int),
            CanonicalValueType.Long => typeof(long),
            CanonicalValueType.Double => typeof(double),
            _ => typeof(object),
        };
        clrType.Should().Be(expectedClrType);
    }

    // ── Templated entries (factory methods) ───────────────────────────────

    [Theory]
    [InlineData(1, "production/counter1/count", "Production/Counter1/Count")]
    [InlineData(4, "production/counter4/target", "Production/Counter4/Target")]
    public void ProductionCounter_TemplatedPathsFormatCorrectly(
        int counter, string expectedTagName, string expectedTagPath)
    {
        // Pick which method based on the expected path's last segment.
        var entry = expectedTagPath.EndsWith("/Count", System.StringComparison.Ordinal)
            ? BrotherTagMap.ProductionCounterCount(counter)
            : BrotherTagMap.ProductionCounterTarget(counter);

        entry.TagName.Should().Be(expectedTagName);
        entry.TagPath.Should().Be(expectedTagPath);
        entry.ValueType.Should().Be(CanonicalValueType.Integer);
    }

    [Theory]
    [InlineData(1, "Tools/Magazine/1/Number")]
    [InlineData(11, "Tools/Magazine/11/Length")]
    [InlineData(21, "Tools/Magazine/21/IsActive")]
    public void ToolsMagazine_TemplatedPathsCarrySlotIndex(int slot, string expectedTagPath)
    {
        var entry = expectedTagPath.EndsWith("/Number", System.StringComparison.Ordinal)
            ? BrotherTagMap.ToolsMagazineNumber(slot)
            : expectedTagPath.EndsWith("/Length", System.StringComparison.Ordinal)
                ? BrotherTagMap.ToolsMagazineLength(slot)
                : BrotherTagMap.ToolsMagazineIsActive(slot);

        entry.TagPath.Should().Be(expectedTagPath);
    }

    [Fact]
    public void ToolsTool_NameTypeLife_AreToolNumberKeyed_NotSlotKeyed()
    {
        // Pins v3 §6.1 ATC split — name/type/life are keyed by tool number,
        // not magazine slot, mirroring legacy AdditionalData["Tool.{toolNo}.*"].
        var name = BrotherTagMap.ToolsToolName(11);
        var type = BrotherTagMap.ToolsToolType(11);
        var life = BrotherTagMap.ToolsToolLife(11);

        name.TagPath.Should().Be("Tools/Tool/11/Name");
        type.TagPath.Should().Be("Tools/Tool/11/Type");
        life.TagPath.Should().Be("Tools/Tool/11/Life");
    }

    [Theory]
    [InlineData(0, "Alarms/Active/0/Number")]
    [InlineData(5, "Alarms/Active/5/Message")]
    public void Alarms_TemplatedPathsCarryIndex(int idx, string expectedTagPath)
    {
        var entry = expectedTagPath.EndsWith("/Number", System.StringComparison.Ordinal)
            ? BrotherTagMap.AlarmsActiveNumber(idx)
            : BrotherTagMap.AlarmsActiveMessage(idx);

        entry.TagPath.Should().Be(expectedTagPath);
    }

    [Theory]
    [InlineData(1, "Maintenance/Notice/1/Description")]
    [InlineData(3, "Maintenance/Notice/3/DuePercent")]
    public void MaintenanceNotice_TemplatedPathsCarryIndex(int idx, string expectedTagPath)
    {
        var entry = expectedTagPath.EndsWith("/Description", System.StringComparison.Ordinal)
            ? BrotherTagMap.MaintenanceNoticeDescription(idx)
            : BrotherTagMap.MaintenanceNoticeDuePercent(idx);

        entry.TagPath.Should().Be(expectedTagPath);
    }

    // ── Catalog-membership predicate ──────────────────────────────────────

    [Theory]
    [InlineData("MachineInfo/Hostname")]
    [InlineData("Status/State")]
    [InlineData("Production/PartsCount")]
    [InlineData("Alarms/ActiveCount")]
    public void IsKnownPathOrPrefix_ExactStaticPath_IsRecognised(string path)
    {
        BrotherTagMap.IsKnownPathOrPrefix(path).Should().BeTrue();
    }

    [Theory]
    [InlineData("status/state")]      // lowercase
    [InlineData("STATUS/STATE")]      // uppercase
    [InlineData("Status/state")]      // mixed
    public void IsKnownPathOrPrefix_CaseInsensitive(string path)
    {
        BrotherTagMap.IsKnownPathOrPrefix(path).Should().BeTrue(
            "membership check is case-insensitive per v3.1 §B.6 normalization");
    }

    [Theory]
    [InlineData("Tools/")]
    [InlineData("Maintenance/")]
    [InlineData("Status/")]
    public void IsKnownPathOrPrefix_RegisteredPrefix_IsRecognised(string prefix)
    {
        BrotherTagMap.IsKnownPathOrPrefix(prefix).Should().BeTrue();
    }

    [Theory]
    [InlineData("Status")]   // no trailing slash on a prefix root
    [InlineData("Tools")]
    public void IsKnownPathOrPrefix_PrefixWithoutTrailingSlash_IsRecognised(string prefix)
    {
        // Operator may type the prefix without trailing slash; we still recognise it.
        BrotherTagMap.IsKnownPathOrPrefix(prefix).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Unknown/Path")]
    [InlineData("MachineInfo/NotARealField")]
    [InlineData("totally_made_up")]
    public void IsKnownPathOrPrefix_UnknownPath_IsRejected(string path)
    {
        BrotherTagMap.IsKnownPathOrPrefix(path).Should().BeFalse();
    }
}
