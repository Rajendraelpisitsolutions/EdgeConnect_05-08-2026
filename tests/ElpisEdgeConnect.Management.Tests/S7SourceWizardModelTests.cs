// ============================================================================
// Tests: S7SourceWizardModel (M.2b.2) — pins the JSON shape the wizard emits
//        into SourceInstanceConfig.Connection (keyed by S7ConnectionKeys, tags
//        under "tags" NOT "tagDefinitions"), the BuildSourceInstance →
//        S7SourceConfiguration.FromSourceInstance parity, the hydrate
//        round-trip invariant, and the validation rules — including the locked
//        M.2b.2 v2 decisions: planner-rejected datatype/address combos BLOCK
//        Save, Timer/Counter addresses are unsupported (block), duplicate tag
//        names block, duplicate addresses warn.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Api;
using ElpisEdgeConnect.Management.Wizards;
using ElpisEdgeConnect.Sources.S7;
using ElpisEdgeConnect.Sources.S7.Import;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class S7SourceWizardModelTests
{
    private static S7TagWizardRow Row(
        string name, string address, string? datatype = "Int", int scanRateMs = 1000) =>
        new() { Name = name, Address = address, Datatype = datatype, ScanRateMs = scanRateMs };

    // ── BuildSourceInstance shape ────────────────────────────────────────

    [Fact]
    public void BuildSourceInstance_PopulatesIdentity_AndProtocolIsS7()
    {
        var model = new S7SourceWizardModel
        {
            InstanceId = "s7-press-3",
            DeviceId = "S7-1500-P3",
            DeviceName = "Press 3 PLC",
            DeviceClass = "plc",
            Enabled = true,
            Host = "192.168.10.5",
        };

        var instance = model.BuildSourceInstance();

        instance.InstanceId.Should().Be("s7-press-3");
        instance.ProtocolName.Should().Be("s7");
        instance.DeviceId.Should().Be("S7-1500-P3");
        instance.DeviceName.Should().Be("Press 3 PLC");
        instance.DeviceClass.Should().Be("plc");
        instance.Enabled.Should().BeTrue();
    }

    [Fact]
    public void BuildSourceInstance_PacksConnectionKeys()
    {
        var model = new S7SourceWizardModel
        {
            InstanceId = "s7-1",
            Host = "10.0.0.9",
            Port = 102,
            Rack = 0,
            Slot = 1,
            ConnectionType = "Basic",
            OptimizedDbAccess = false,
            ConnectTimeoutMs = 2000,
            RequestTimeoutMs = 1000,
            KeepAlive = true,
            MaxGapBytes = 16,
            MaxReadBytes = 200,
        };

        var conn = model.BuildSourceInstance().Connection!.Value;

        conn.GetProperty("host").GetString().Should().Be("10.0.0.9");
        conn.GetProperty("port").GetInt32().Should().Be(102);
        conn.GetProperty("rack").GetInt32().Should().Be(0);
        conn.GetProperty("slot").GetInt32().Should().Be(1);
        conn.GetProperty("connectionType").GetString().Should().Be("Basic");
        conn.GetProperty("optimizedDbAccess").GetBoolean().Should().BeFalse();
        conn.GetProperty("maxGapBytes").GetInt32().Should().Be(16);
        conn.GetProperty("maxReadBytes").GetInt32().Should().Be(200);
    }

    [Fact]
    public void BuildSourceInstance_EmitsTagsUnderTagsKey_NotTagDefinitions()
    {
        var model = new S7SourceWizardModel
        {
            InstanceId = "s7-1",
            Host = "10.0.0.9",
            Tags = { Row("spindle_rpm", "DB1.DBW0", "Int", 200) },
        };

        var conn = model.BuildSourceInstance().Connection!.Value;

        conn.TryGetProperty("tagDefinitions", out _).Should().BeFalse("S7 nests tags under 'tags', not 'tagDefinitions'");
        var tags = conn.GetProperty("tags");
        tags.GetArrayLength().Should().Be(1);
        var first = tags.EnumerateArray().First();
        first.GetProperty("name").GetString().Should().Be("spindle_rpm");
        first.GetProperty("address").GetString().Should().Be("DB1.DBW0");
        first.GetProperty("datatype").GetString().Should().Be("Int");
        first.GetProperty("scanRateMs").GetInt32().Should().Be(200);
    }

    [Fact]
    public void BuildSourceInstance_OmitsOptionalTagFields_WhenNull()
    {
        var model = new S7SourceWizardModel
        {
            InstanceId = "s7-1",
            Host = "10.0.0.9",
            Tags = { new S7TagWizardRow { Name = "raw", Address = "DB1.DBW0", Datatype = null } },
        };

        var tag = model.BuildSourceInstance().Connection!.Value.GetProperty("tags").EnumerateArray().First();

        tag.TryGetProperty("datatype", out _).Should().BeFalse();
        tag.TryGetProperty("unit", out _).Should().BeFalse();
        tag.TryGetProperty("scale", out _).Should().BeFalse();
        tag.TryGetProperty("offset", out _).Should().BeFalse();
    }

    [Fact]
    public void BuildSourceInstance_StringDatatype_ComposesStringBracketN()
    {
        var model = new S7SourceWizardModel
        {
            InstanceId = "s7-1",
            Host = "10.0.0.9",
            Tags = { new S7TagWizardRow { Name = "job", Address = "DB1.DBB0", Datatype = "String", StringLength = 16 } },
        };

        var tag = model.BuildSourceInstance().Connection!.Value.GetProperty("tags").EnumerateArray().First();
        tag.GetProperty("datatype").GetString().Should().Be("string[16]");
    }

    [Fact]
    public void BuildSourceInstance_LeavesPollingAtDefault()
    {
        // S7 drives per-tag scan rates; the top-level polling interval is the
        // canonical default, not set from the wizard.
        var model = new S7SourceWizardModel { InstanceId = "s7-1", Host = "10.0.0.9" };
        var instance = model.BuildSourceInstance();
        instance.Polling.Should().BeEquivalentTo(new PollingSettings());
    }

    // ── Parity with the real typed config ────────────────────────────────

    [Fact]
    public void BuildSourceInstance_RoundTripsThrough_FromSourceInstance()
    {
        var model = new S7SourceWizardModel
        {
            InstanceId = "s7-press-3",
            DeviceId = "S7-1500-P3",
            DeviceName = "Press 3",
            Host = "192.168.10.5",
            Port = 102,
            Rack = 0,
            Slot = 1,
            ConnectionType = "Basic",
            Tags =
            {
                Row("spindle_rpm", "DB1.DBW0", "Int", 200),
                new S7TagWizardRow { Name = "feed", Address = "DB1.DBD4", Datatype = "Real", ScanRateMs = 500, Unit = "mm/min", Scale = 0.1, Offset = 0 },
            },
        };

        var config = S7SourceConfiguration.FromSourceInstance(model.BuildSourceInstance());

        config.Host.Should().Be("192.168.10.5");
        config.Port.Should().Be(102);
        config.Slot.Should().Be(1);
        config.ConnectionType.Should().Be(S7ConnectionType.Basic);
        config.DeviceId.Should().Be("S7-1500-P3");
        config.TagDefinitions.Should().HaveCount(2);
        config.TagDefinitions[0].Name.Should().Be("spindle_rpm");
        config.TagDefinitions[0].Address.Should().Be("DB1.DBW0");
        config.TagDefinitions[0].Datatype.Should().Be("Int");
        config.TagDefinitions[1].Unit.Should().Be("mm/min");
        config.TagDefinitions[1].Scale.Should().Be(0.1);
    }

    // ── Hydrate round-trip ───────────────────────────────────────────────

    [Fact]
    public void HydrateFromExisting_RoundTrips_ByteEquivalent()
    {
        var original = new S7SourceWizardModel
        {
            InstanceId = "s7-press-3",
            DeviceId = "S7-1500-P3",
            DeviceName = "Press 3",
            DeviceClass = "plc",
            Description = "Main hydraulic press",
            Enabled = false,
            Host = "192.168.10.5",
            Port = 102,
            Rack = 0,
            Slot = 1,
            ConnectionType = "PG",
            OptimizedDbAccess = true,
            ConnectTimeoutMs = 2500,
            RequestTimeoutMs = 1200,
            KeepAlive = false,
            MaxTransactionRetries = 3,
            InitialBackoffMs = 1500,
            MaxBackoffMs = 45_000,
            BackoffMultiplier = 1.5,
            CircuitBreakerThreshold = 4,
            CircuitBreakerResetMs = 20_000,
            MaxGapBytes = 8,
            MaxReadBytes = 220,
            Tags = new List<S7TagWizardRow>
            {
                new() { Name = "rpm", Address = "DB1.DBW0", Datatype = "Int", ScanRateMs = 200, Unit = "rpm", Scale = 1.0, Offset = 0.0 },
                new() { Name = "running", Address = "DB1.DBX2.0", Datatype = "Bool", ScanRateMs = 500 },
                new() { Name = "job", Address = "DB1.DBB10", Datatype = "String", StringLength = 16, ScanRateMs = 1000 },
                new() { Name = "load", Address = "DB1.DBD20", Datatype = "Real", ScanRateMs = 250, Unit = "%" },
            },
        };

        var firstEmit = original.BuildSourceInstance();
        var hydrated = S7SourceWizardModel.HydrateFromExisting(firstEmit);
        var secondEmit = hydrated.BuildSourceInstance();

        secondEmit.InstanceId.Should().Be(firstEmit.InstanceId);
        secondEmit.ProtocolName.Should().Be(firstEmit.ProtocolName);
        secondEmit.Enabled.Should().Be(firstEmit.Enabled);
        secondEmit.Connection!.Value.GetRawText().Should().Be(firstEmit.Connection!.Value.GetRawText());
    }

    [Fact]
    public void HydrateFromExisting_PreservesTagOrder()
    {
        var original = new S7SourceWizardModel
        {
            InstanceId = "s7-order",
            Host = "1.2.3.4",
            Tags =
            {
                Row("Zeta", "DB1.DBW0"),
                Row("Alpha", "DB1.DBW2"),
                Row("Mu", "DB1.DBW4"),
            },
        };

        var hydrated = S7SourceWizardModel.HydrateFromExisting(original.BuildSourceInstance());

        hydrated.Tags.Select(t => t.Name).Should().Equal("Zeta", "Alpha", "Mu");
    }

    [Fact]
    public void HydrateFromExisting_StringBracketN_SplitsBackIntoStringAndLength()
    {
        var original = new S7SourceWizardModel
        {
            InstanceId = "s7-str",
            Host = "1.2.3.4",
            Tags = { new S7TagWizardRow { Name = "job", Address = "DB1.DBB0", Datatype = "String", StringLength = 24 } },
        };

        var hydrated = S7SourceWizardModel.HydrateFromExisting(original.BuildSourceInstance());

        var row = hydrated.Tags.Should().ContainSingle().Subject;
        row.Datatype.Should().Be("String");
        row.StringLength.Should().Be(24);
    }

    [Fact]
    public void HydrateFromExisting_WrongProtocol_Throws()
    {
        var modbus = new SourceInstanceConfig { InstanceId = "m", ProtocolName = "modbustcp", DeviceId = "m" };

        var act = () => S7SourceWizardModel.HydrateFromExisting(modbus);

        act.Should().Throw<ArgumentException>().WithMessage("*s7*");
    }

    // ── ValidateTag — required fields ────────────────────────────────────

    [Fact]
    public void ValidateTag_HappyPath_IsEmpty()
    {
        S7SourceWizardModel.ValidateTag(Row("rpm", "DB1.DBW0", "Int")).Should().BeEmpty();
    }

    [Fact]
    public void ValidateTag_MissingName_FlagsName()
    {
        S7SourceWizardModel.ValidateTag(Row("", "DB1.DBW0", "Int"))
            .Should().Contain(i => i.Path == "Name");
    }

    [Fact]
    public void ValidateTag_MissingAddress_FlagsAddress()
    {
        S7SourceWizardModel.ValidateTag(Row("rpm", "", "Int"))
            .Should().Contain(i => i.Path == "Address");
    }

    [Fact]
    public void ValidateTag_InvalidAddress_FlagsAddress()
    {
        S7SourceWizardModel.ValidateTag(Row("rpm", "DB1.DBZ0", "Int"))
            .Should().Contain(i => i.Path == "Address");
    }

    [Fact]
    public void ValidateTag_NonPositiveScanRate_FlagsScanRate()
    {
        S7SourceWizardModel.ValidateTag(Row("rpm", "DB1.DBW0", "Int", scanRateMs: 0))
            .Should().Contain(i => i.Path == "ScanRateMs");
    }

    [Fact]
    public void ValidateTag_MissingDatatype_FlagsDatatype()
    {
        S7SourceWizardModel.ValidateTag(Row("rpm", "DB1.DBW0", datatype: null))
            .Should().Contain(i => i.Path == "Datatype");
    }

    [Fact]
    public void ValidateTag_UnknownDatatype_FlagsDatatype()
    {
        S7SourceWizardModel.ValidateTag(Row("rpm", "DB1.DBW0", "NotAType"))
            .Should().Contain(i => i.Path == "Datatype");
    }

    [Fact]
    public void ValidateTag_StringMissingLength_FlagsStringLength()
    {
        var row = new S7TagWizardRow { Name = "job", Address = "DB1.DBB0", Datatype = "String", StringLength = null };
        S7SourceWizardModel.ValidateTag(row).Should().Contain(i => i.Path == "StringLength");
    }

    // ── ValidateTag — Timer/Counter unsupported (M.2b.2 v2 decision) ──────

    [Theory]
    [InlineData("T5")]
    [InlineData("C3")]
    public void ValidateTag_TimerCounterAddress_IsBlockingError(string address)
    {
        var issues = S7SourceWizardModel.ValidateTag(Row("x", address, "Int"));

        issues.Should().Contain(i =>
            i.Code == "S7.UNSUPPORTED_TIMER_COUNTER_ADDRESS" && i.Path == "Address");
    }

    // ── ValidateTag — compatibility blocks (M.2b.2 v2 decision) ───────────

    [Fact]
    public void ValidateTag_BitAddressWithNonBool_IsBlockingError()
    {
        var issues = S7SourceWizardModel.ValidateTag(Row("x", "DB1.DBX0.0", "Int"));

        issues.Should().Contain(i =>
            i.Code == S7CompatibilityVerdict.BitRequiresBoolCode && i.Path == "Datatype");
    }

    [Fact]
    public void ValidateTag_DatatypeWiderThanAddress_IsBlockingError()
    {
        var issues = S7SourceWizardModel.ValidateTag(Row("x", "DB1.DBW0", "Real"));

        issues.Should().Contain(i =>
            i.Code == S7CompatibilityVerdict.DatatypeTooWideCode && i.Path == "Datatype");
    }

    [Fact]
    public void ValidateTag_NarrowerThanAddress_DoesNotBlock()
    {
        // Bool on a DBW word — planner accepts it, so it is NOT a Save block.
        S7SourceWizardModel.ValidateTag(Row("x", "DB1.DBW0", "Bool")).Should().BeEmpty();
    }

    [Fact]
    public void TagCompatibility_NarrowerThanAddress_IsWarning()
    {
        var verdict = S7SourceWizardModel.TagCompatibility(Row("x", "DB1.DBW0", "Bool"));
        verdict.Should().NotBeNull();
        verdict!.Value.Severity.Should().Be(S7CompatibilitySeverity.Warning);
    }

    // ── model.Validate — cross-row rules ─────────────────────────────────

    [Fact]
    public void Validate_DuplicateTagNames_Block()
    {
        var model = new S7SourceWizardModel
        {
            InstanceId = "s7-1",
            Host = "1.2.3.4",
            Tags = { Row("dup", "DB1.DBW0"), Row("dup", "DB1.DBW2") },
        };

        var result = model.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(i => i.Message.Contains("unique"));
    }

    [Fact]
    public void Validate_DuplicateAddresses_Warn_DoNotBlock()
    {
        var model = new S7SourceWizardModel
        {
            InstanceId = "s7-1",
            Host = "1.2.3.4",
            Tags = { Row("a", "DB1.DBW0"), Row("b", "DB1.DBW0") },
        };

        var result = model.Validate();

        result.IsValid.Should().BeTrue("duplicate addresses warn, never block");
        result.Warnings.Should().Contain(i => i.Path == "Address");
    }

    [Fact]
    public void Validate_MissingHostAndInstanceId_Block()
    {
        var model = new S7SourceWizardModel { InstanceId = "", Host = "" };

        var result = model.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(i => i.Path == "InstanceId");
        result.Errors.Should().Contain(i => i.Path == "Host");
    }

    [Fact]
    public void Validate_PrefixesRowErrorPaths_WithTagIndex()
    {
        var model = new S7SourceWizardModel
        {
            InstanceId = "s7-1",
            Host = "1.2.3.4",
            Tags = { Row("ok", "DB1.DBW0"), Row("bad", "DB1.DBX0.0", "Int") },
        };

        var result = model.Validate();

        result.Errors.Should().Contain(i => i.Path == "Tags[1].Datatype");
    }

    // ── CSV import → wizard row mapping (v1.1) ───────────────────────────

    [Fact]
    public void ToWizardRow_NormalizesDatatypeSynonym_ToCanonicalName()
    {
        var row = S7SourceWizardModel.ToWizardRow(
            new S7TagDefinition { Name = "rpm", Address = "DB1.DBW0", Datatype = "int" });
        row.Datatype.Should().Be("Int", "the dropdown binds canonical S7Datatype names");
    }

    [Fact]
    public void ToWizardRow_StringN_SplitsToStringAndLength()
    {
        var row = S7SourceWizardModel.ToWizardRow(
            new S7TagDefinition { Name = "job", Address = "DB1.DBB0", Datatype = "string[16]" });
        row.Datatype.Should().Be("String");
        row.StringLength.Should().Be(16);
    }

    [Theory]
    [InlineData("DB1.DBW0", "Word")]
    [InlineData("DB1.DBX0.0", "Bool")]
    [InlineData("DB1.DBD4", "DWord")]
    [InlineData("DB1.DBB0", "Byte")]
    public void ToWizardRow_BlankDatatype_DerivedFromAddressWidth(string address, string expected)
    {
        var row = S7SourceWizardModel.ToWizardRow(
            new S7TagDefinition { Name = "t", Address = address, Datatype = null });
        row.Datatype.Should().Be(expected);
    }

    [Fact]
    public void ToWizardRow_ImportedRow_PassesWizardValidation()
    {
        var row = S7SourceWizardModel.ToWizardRow(
            new S7TagDefinition { Name = "rpm", Address = "DB1.DBW0", Datatype = "Int", ScanRateMs = 500 });
        S7SourceWizardModel.ValidateTag(row).Should().BeEmpty();
    }

    [Fact]
    public void ShippedCsvTemplate_ImportsCleanly_AndMapsToValidRows()
    {
        // Guards the downloadable template against drift: it must parse with no
        // errors and every produced row must be a valid wizard row.
        var result = S7TagCsvImporter.Import(new StringReader(S7TagTemplateApi.TemplateCsv));

        result.IsSuccess.Should().BeTrue();
        result.Tags.Should().HaveCount(3);
        foreach (var tag in result.Tags)
        {
            var row = S7SourceWizardModel.ToWizardRow(tag);
            S7SourceWizardModel.ValidateTag(row).Should().BeEmpty($"template row '{tag.Name}' must be valid");
        }
    }
}
