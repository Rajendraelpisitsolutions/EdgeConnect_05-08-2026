// ============================================================================
// File: Import/ModbusTagTemplateTests.cs
// Purpose: Ensure the two built-in F4 templates parse cleanly, round-trip
//          to valid ScanPlans, and carry the tag names their docs
//          advertise. Prevents a typo in the CSV from shipping silently.
// ============================================================================

using System.Linq;
using ElpisEdgeConnect.Sources.ModbusTcp.Import;
using ElpisEdgeConnect.Sources.ModbusTcp.Scanning;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Tests.Import;

public sealed class ModbusTagTemplateTests
{
    [Theory]
    [InlineData(ModbusTagTemplate.GenericPlc)]
    [InlineData(ModbusTagTemplate.CncViaModbusGateway)]
    public void LoadCsv_ReturnsNonEmptyContent(string name)
    {
        var csv = ModbusTagTemplate.LoadCsv(name);

        csv.Should().NotBeNullOrWhiteSpace();
        csv.Should().Contain("name,unitId,registerClass");
    }

    [Theory]
    [InlineData(ModbusTagTemplate.GenericPlc)]
    [InlineData(ModbusTagTemplate.CncViaModbusGateway)]
    public void Load_ParsesCleanly_NoErrorsNoWarnings(string name)
    {
        var result = ModbusTagTemplate.Load(name);

        result.IsSuccess.Should().BeTrue(
            "template '{0}' must parse without errors — errors were: {1}",
            name,
            string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Message}")));
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty(
            "templates shouldn't ship with overlap warnings or other surprises");
        result.Tags.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(ModbusTagTemplate.GenericPlc)]
    [InlineData(ModbusTagTemplate.CncViaModbusGateway)]
    public void Load_ProducesValidScanPlan(string name)
    {
        var result = ModbusTagTemplate.Load(name);
        var plan = ScanPlanner.Build(result.Tags);

        plan.Groups.Should().NotBeEmpty();
        plan.TotalTagCount.Should().Be(result.Tags.Count);
    }

    [Fact]
    public void GenericPlc_Contains_ExpectedCoreTags()
    {
        var result = ModbusTagTemplate.Load(ModbusTagTemplate.GenericPlc);
        var names = result.Tags.Select(t => t.Name).ToHashSet();

        // Contract: EREMOS V2 line-level OEE modules expect these shapes.
        names.Should().Contain("running");
        names.Should().Contain("parts_count");
        names.Should().Contain("cycle_time");
        names.Should().Contain("alarm_code");
    }

    [Fact]
    public void CncViaModbusGateway_Contains_ExpectedCncTags()
    {
        var result = ModbusTagTemplate.Load(ModbusTagTemplate.CncViaModbusGateway);
        var names = result.Tags.Select(t => t.Name).ToHashSet();

        // Contract: EREMOS V2 machine-level modules expect these shapes.
        names.Should().Contain("spindle_rpm");
        names.Should().Contain("feed_rate");
        names.Should().Contain("program_number");
        names.Should().Contain("tool_number");
    }

    [Fact]
    public void LoadCsv_UnknownTemplate_Throws()
    {
        var act = () => ModbusTagTemplate.LoadCsv("does-not-exist");
        act.Should().Throw<System.ArgumentException>()
            .WithMessage("*Unknown Modbus template*");
    }

    [Fact]
    public void Available_ListsBothShippedTemplates()
    {
        ModbusTagTemplate.Available.Should().Contain(ModbusTagTemplate.GenericPlc);
        ModbusTagTemplate.Available.Should().Contain(ModbusTagTemplate.CncViaModbusGateway);
        ModbusTagTemplate.Available.Should().HaveCount(2);
    }
}
