// ============================================================================
// Tests: ModbusSourceWizardModel — pins the JSON shape the wizard
//        emits into SourceInstanceConfig.Connection. Whatever this
//        model writes is what ModbusTcpSourceConfiguration.FromSourceInstance
//        parses back on the Core side, so getting the field names + types
//        right is the wire contract.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Wizards;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class ModbusSourceWizardModelTests
{
    [Fact]
    public void BuildSourceInstance_PopulatesIdentityFields()
    {
        var model = new ModbusSourceWizardModel
        {
            InstanceId = "modbus-line-7",
            DeviceId = "S7-1200-L7",
            DeviceName = "Siemens S7 Line 7",
            DeviceClass = "plc",
            Enabled = true,
            PollIntervalMs = 200,
            Host = "192.168.1.42",
        };

        var instance = model.BuildSourceInstance();

        instance.InstanceId.Should().Be("modbus-line-7");
        instance.ProtocolName.Should().Be("modbustcp");
        instance.DeviceId.Should().Be("S7-1200-L7");
        instance.DeviceName.Should().Be("Siemens S7 Line 7");
        instance.DeviceClass.Should().Be("plc");
        instance.Enabled.Should().BeTrue();
        instance.Polling.IntervalMs.Should().Be(200);
    }

    [Fact]
    public void BuildSourceInstance_DefaultsDeviceIdAndNameToInstanceId_WhenEmpty()
    {
        var model = new ModbusSourceWizardModel
        {
            InstanceId = "modbus-only-id",
            Host = "127.0.0.1",
            // DeviceId, DeviceName intentionally left empty
        };

        var instance = model.BuildSourceInstance();

        instance.DeviceId.Should().Be("modbus-only-id");
        instance.DeviceName.Should().Be("modbus-only-id");
    }

    [Fact]
    public void BuildSourceInstance_PacksConnectionAsOpaqueJsonObject()
    {
        var model = new ModbusSourceWizardModel
        {
            InstanceId = "modbus-1",
            Host = "192.168.1.42",
            Port = 5020,
            Encapsulation = "Tcp",
            DefaultUnitId = 3,
            ConnectTimeoutMs = 3000,
            RequestTimeoutMs = 2000,
            KeepAlive = false,
            MaxTransactionRetries = 4,
            MaxGapRegisters = 16,
        };

        var instance = model.BuildSourceInstance();

        instance.Connection.Should().NotBeNull();
        var conn = instance.Connection!.Value;
        conn.GetProperty("host").GetString().Should().Be("192.168.1.42");
        conn.GetProperty("port").GetInt32().Should().Be(5020);
        conn.GetProperty("encapsulation").GetString().Should().Be("Tcp");
        conn.GetProperty("defaultUnitId").GetInt32().Should().Be(3);
        conn.GetProperty("connectTimeoutMs").GetInt32().Should().Be(3000);
        conn.GetProperty("requestTimeoutMs").GetInt32().Should().Be(2000);
        conn.GetProperty("keepAlive").GetBoolean().Should().BeFalse();
        conn.GetProperty("maxTransactionRetries").GetInt32().Should().Be(4);
        conn.GetProperty("maxGapRegisters").GetInt32().Should().Be(16);
    }

    [Fact]
    public void BuildSourceInstance_EmitsTagDefinitionsArray()
    {
        var model = new ModbusSourceWizardModel
        {
            InstanceId = "modbus-1",
            Host = "127.0.0.1",
            Tags =
            {
                new ModbusTagWizardRow
                {
                    Name = "spindle_rpm",
                    RegisterClass = "HoldingRegister",
                    Address = 0,
                    Datatype = "uint16",
                    ScanRateMs = 200,
                    Unit = "rpm",
                },
                new ModbusTagWizardRow
                {
                    Name = "feed_rate",
                    RegisterClass = "HoldingRegister",
                    Address = 10,
                    Datatype = "float32",
                    ByteOrder = "ABCD",
                    ScanRateMs = 200,
                    Unit = "mm/min",
                },
            },
        };

        var instance = model.BuildSourceInstance();
        var tags = instance.Connection!.Value.GetProperty("tagDefinitions");

        tags.GetArrayLength().Should().Be(2);
        var first = tags.EnumerateArray().First();
        first.GetProperty("name").GetString().Should().Be("spindle_rpm");
        first.GetProperty("registerClass").GetString().Should().Be("HoldingRegister");
        first.GetProperty("address").GetInt32().Should().Be(0);
        first.GetProperty("datatype").GetString().Should().Be("uint16");
        first.GetProperty("unit").GetString().Should().Be("rpm");
    }

    [Fact]
    public void BuildSourceInstance_OmitsOptionalFieldsWhenNullOrBlank()
    {
        var model = new ModbusSourceWizardModel
        {
            InstanceId = "modbus-1",
            Host = "127.0.0.1",
            Tags =
            {
                new ModbusTagWizardRow
                {
                    Name = "raw",
                    RegisterClass = "Coil",
                    Address = 0,
                    Datatype = null,    // null datatype — omit
                    ByteOrder = null,
                    Scale = null,
                    Offset = null,
                    Unit = null,
                },
            },
        };

        var instance = model.BuildSourceInstance();
        var tag = instance.Connection!.Value.GetProperty("tagDefinitions").EnumerateArray().First();

        tag.TryGetProperty("datatype", out _).Should().BeFalse("null datatype must not write the property");
        tag.TryGetProperty("byteOrder", out _).Should().BeFalse();
        tag.TryGetProperty("scale", out _).Should().BeFalse();
        tag.TryGetProperty("offset", out _).Should().BeFalse();
        tag.TryGetProperty("unit", out _).Should().BeFalse();
    }

    [Fact]
    public void BuildSourceInstance_EmittedJsonRoundTrips()
    {
        // The connection JSON the wizard emits MUST be parseable by
        // ModbusTcpSourceConfiguration.FromSourceInstance on the Core
        // side. We can't easily import that class from the test project
        // without a Modbus reference, so the shape check here pins the
        // canonical fields by name.
        var model = new ModbusSourceWizardModel
        {
            InstanceId = "modbus-1",
            Host = "127.0.0.1",
            Port = 502,
            DefaultUnitId = 1,
            Encapsulation = "Tcp",
        };

        var instance = model.BuildSourceInstance();
        // Use the same JSON conventions the management API does
        // (camelCase, web defaults) so this test reflects what consumers
        // of /api/v1/config actually see.
        var json = JsonSerializer.Serialize(instance, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        // Spot-check: the protocol name + the opaque connection block's host field
        // both round-trip through System.Text.Json.
        json.Should().Contain("\"protocolName\":\"modbustcp\"");
        json.Should().Contain("\"host\":\"127.0.0.1\"");
    }

    // ─────────────────────────────────────────────────────────────────
    // M.2b.6.2 §3.A — tag-row cross-validation. Pins the composition
    // contract: the wizard delegates to the adapter's
    // ModbusTagValidator so any future datatype/byte-order addition
    // is automatically honored by the wizard's pre-submit gate.
    // The test calls ModbusSourceWizardModel.ValidateTag, not the
    // underlying validator, to prove the wizard is wired through.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ValidateTag_HappyPath_HoldingRegisterUint16_NoByteOrder_Valid()
    {
        var row = new ModbusTagWizardRow
        {
            Name = "spindle_rpm",
            RegisterClass = "HoldingRegister",
            Address = 0,
            Datatype = "uint16",
            ByteOrder = null,
        };

        ModbusSourceWizardModel.ValidateTag(row).Should().BeEmpty();
    }

    [Fact]
    public void ValidateTag_HoldingRegisterFloat32_ABCD_Valid()
    {
        var row = new ModbusTagWizardRow
        {
            Name = "feed_rate",
            RegisterClass = "HoldingRegister",
            Address = 10,
            Datatype = "float32",
            ByteOrder = "ABCD",
        };

        ModbusSourceWizardModel.ValidateTag(row).Should().BeEmpty();
    }

    [Fact]
    public void ValidateTag_Uint16WithCDAB_FlagsByteOrderMismatch()
    {
        // The smoke-test scenario from the M.2b.6.1 manual run that
        // motivated this milestone: uint16 (2-byte) with a 4-byte
        // byte-order. Adapter would reject this at startup; the wizard
        // catches it now.
        var row = new ModbusTagWizardRow
        {
            Name = "bad_tag",
            RegisterClass = "HoldingRegister",
            Address = 0,
            Datatype = "uint16",
            ByteOrder = "CDAB",
        };

        var issues = ModbusSourceWizardModel.ValidateTag(row);

        issues.Should().ContainSingle(iss =>
            iss.Code == "MODBUS.CONFIG_INVALID" && iss.Path == "ByteOrder");
    }

    [Fact]
    public void ValidateTag_Float32WithAB_FlagsByteOrderMismatch()
    {
        var row = new ModbusTagWizardRow
        {
            Name = "bad_tag",
            RegisterClass = "HoldingRegister",
            Address = 0,
            Datatype = "float32",
            ByteOrder = "AB",
        };

        var issues = ModbusSourceWizardModel.ValidateTag(row);

        issues.Should().ContainSingle(iss =>
            iss.Code == "MODBUS.CONFIG_INVALID" && iss.Path == "ByteOrder");
    }

    [Theory]
    [InlineData("uint16", "AB")]
    [InlineData("uint16", "BA")]
    [InlineData("int16", "AB")]
    [InlineData("int32", "ABCD")]
    [InlineData("int32", "CDAB")]
    [InlineData("int32", "BADC")]
    [InlineData("int32", "DCBA")]
    [InlineData("float32", "ABCD")]
    [InlineData("float32", "DCBA")]
    [InlineData("int64", "ABCDEFGH")]
    [InlineData("float64", "HGFEDCBA")]
    public void ValidateTag_ByteOrderMatchingDatatypeWidth_IsValid(string datatype, string byteOrder)
    {
        var row = new ModbusTagWizardRow
        {
            Name = "ok",
            RegisterClass = "HoldingRegister",
            Address = 0,
            Datatype = datatype,
            ByteOrder = byteOrder,
        };

        ModbusSourceWizardModel.ValidateTag(row).Should().BeEmpty();
    }

    [Theory]
    // Two-byte datatypes paired with 4- and 8-byte byte-orders
    [InlineData("uint16", "ABCD")]
    [InlineData("int16", "DCBA")]
    [InlineData("uint16", "ABCDEFGH")]
    // Four-byte datatypes paired with 2- and 8-byte byte-orders
    [InlineData("int32", "AB")]
    [InlineData("uint32", "BA")]
    [InlineData("float32", "ABCDEFGH")]
    // Eight-byte datatypes paired with 2- and 4-byte byte-orders
    [InlineData("int64", "AB")]
    [InlineData("uint64", "ABCD")]
    [InlineData("float64", "CDAB")]
    public void ValidateTag_ByteOrderWidthMismatch_FlagsByteOrderField(string datatype, string byteOrder)
    {
        var row = new ModbusTagWizardRow
        {
            Name = "bad",
            RegisterClass = "HoldingRegister",
            Address = 0,
            Datatype = datatype,
            ByteOrder = byteOrder,
        };

        var issues = ModbusSourceWizardModel.ValidateTag(row);

        issues.Should().Contain(iss =>
            iss.Code == "MODBUS.CONFIG_INVALID" && iss.Path == "ByteOrder");
    }

    [Fact]
    public void ValidateTag_HoldingRegisterWithBool_FlagsDatatype()
    {
        // A bit datatype on a register class doesn't make sense —
        // bools come from Coil / DiscreteInput.
        var row = new ModbusTagWizardRow
        {
            Name = "bad_tag",
            RegisterClass = "HoldingRegister",
            Address = 0,
            Datatype = "bool",
            ByteOrder = null,
        };

        var issues = ModbusSourceWizardModel.ValidateTag(row);

        issues.Should().Contain(iss =>
            iss.Code == "MODBUS.CONFIG_INVALID" && iss.Path == "Datatype");
    }

    [Fact]
    public void ValidateTag_CoilWithUint16_FlagsDatatype()
    {
        // Bit-class register can only carry bool.
        var row = new ModbusTagWizardRow
        {
            Name = "bad_tag",
            RegisterClass = "Coil",
            Address = 0,
            Datatype = "uint16",
            ByteOrder = null,
        };

        var issues = ModbusSourceWizardModel.ValidateTag(row);

        issues.Should().Contain(iss =>
            iss.Code == "MODBUS.CONFIG_INVALID" && iss.Path == "Datatype");
    }

    [Fact]
    public void ValidateTag_CoilWithBool_NoByteOrder_Valid()
    {
        var row = new ModbusTagWizardRow
        {
            Name = "running",
            RegisterClass = "Coil",
            Address = 0,
            Datatype = "bool",
            ByteOrder = null,
        };

        ModbusSourceWizardModel.ValidateTag(row).Should().BeEmpty();
    }

    [Fact]
    public void ValidateTag_CoilWithBool_AndByteOrder_FlagsByteOrder()
    {
        // Byte order on a single-bit read is meaningless — adapter
        // rejects it at startup, wizard now rejects it at row-add.
        var row = new ModbusTagWizardRow
        {
            Name = "running",
            RegisterClass = "Coil",
            Address = 0,
            Datatype = "bool",
            ByteOrder = "AB",
        };

        var issues = ModbusSourceWizardModel.ValidateTag(row);

        issues.Should().Contain(iss =>
            iss.Code == "MODBUS.CONFIG_INVALID" && iss.Path == "ByteOrder");
    }

    [Fact]
    public void ValidateTag_String16_WithByteOrder_FlagsByteOrder()
    {
        // Strings are packed two chars per register, high-char first
        // by Modbus convention — a byteOrder hint doesn't apply.
        var row = new ModbusTagWizardRow
        {
            Name = "machine_name",
            RegisterClass = "HoldingRegister",
            Address = 0,
            Datatype = "string16",
            ByteOrder = "ABCDEFGH",
        };

        var issues = ModbusSourceWizardModel.ValidateTag(row);

        issues.Should().Contain(iss => iss.Path == "ByteOrder");
    }

    [Fact]
    public void ValidateTag_UnrecognisedRegisterClass_FlagsRegisterClass()
    {
        // Defensive branch — the wizard's MudSelect today always emits
        // a recognised name, but future Edit-via-Wizard or free-text
        // fields could route an unparseable value through ValidateTag.
        var row = new ModbusTagWizardRow
        {
            Name = "bad",
            RegisterClass = "NotAClass",
            Address = 0,
            Datatype = "uint16",
        };

        var issues = ModbusSourceWizardModel.ValidateTag(row);

        issues.Should().ContainSingle(iss =>
            iss.Code == "MODBUS.CONFIG_INVALID" && iss.Path == "RegisterClass");
    }

    [Fact]
    public void ValidateTag_UnrecognisedByteOrder_FlagsByteOrder()
    {
        var row = new ModbusTagWizardRow
        {
            Name = "bad",
            RegisterClass = "HoldingRegister",
            Address = 0,
            Datatype = "uint16",
            ByteOrder = "ZZZZ",
        };

        var issues = ModbusSourceWizardModel.ValidateTag(row);

        issues.Should().ContainSingle(iss =>
            iss.Code == "MODBUS.CONFIG_INVALID" && iss.Path == "ByteOrder");
    }

    // ─────────────────────────────────────────────────────────────────
    // M.2b.6.2 v2 — string datatype is split in the wizard into a
    // "string" choice + a separate StringLength field. The wizard
    // composes them into the wire form "stringN" when calling the
    // shared validator and when emitting via BuildSourceInstance.
    // Tests pin the locked rules from the v2 amendment §2.
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ValidateTag_String_MissingLength_FlagsStringLength()
    {
        // v2 Locked rule 1 — string length is required when datatype is string.
        var row = new ModbusTagWizardRow
        {
            Name = "program_number",
            RegisterClass = "HoldingRegister",
            Address = 40,
            Datatype = "string",
            StringLength = null,
        };

        var issues = ModbusSourceWizardModel.ValidateTag(row);

        issues.Should().ContainSingle(iss =>
            iss.Code == "MODBUS.CONFIG_INVALID" && iss.Path == "StringLength");
    }

    [Fact]
    public void ValidateTag_String_NonPositiveLength_FlagsStringLength()
    {
        // v2 Locked rule 2 — string length must be positive.
        var row = new ModbusTagWizardRow
        {
            Name = "program_number",
            RegisterClass = "HoldingRegister",
            Address = 40,
            Datatype = "string",
            StringLength = 0,
        };

        var issues = ModbusSourceWizardModel.ValidateTag(row);

        issues.Should().ContainSingle(iss =>
            iss.Code == "MODBUS.CONFIG_INVALID" && iss.Path == "StringLength");
    }

    [Fact]
    public void ValidateTag_String_PositiveLength_Valid()
    {
        // v2 happy path — string + positive length passes the shared
        // validator after the wizard composes them into "string16".
        var row = new ModbusTagWizardRow
        {
            Name = "program_number",
            RegisterClass = "HoldingRegister",
            Address = 40,
            Datatype = "string",
            StringLength = 16,
        };

        ModbusSourceWizardModel.ValidateTag(row).Should().BeEmpty();
    }

    [Fact]
    public void ValidateTag_NonString_StringLengthIgnored_Valid()
    {
        // Stale StringLength on a non-string row is ignored — operators
        // flipping datatypes back and forth shouldn't see ghosts.
        var row = new ModbusTagWizardRow
        {
            Name = "spindle_rpm",
            RegisterClass = "HoldingRegister",
            Address = 0,
            Datatype = "uint16",
            StringLength = 8,
        };

        ModbusSourceWizardModel.ValidateTag(row).Should().BeEmpty();
    }

    [Fact]
    public void ValidateTag_String_WithByteOrder_FlagsByteOrder()
    {
        // v2 Locked rule 3 prevents the operator from REACHING this
        // state through the wizard UI (the cell is disabled and the
        // value auto-clears on datatype change). But the shared
        // validator's rejection stays in place as defence-in-depth —
        // programmatic callers (Edit-via-Wizard later, tests, REST
        // clients) get the same answer.
        var row = new ModbusTagWizardRow
        {
            Name = "program_number",
            RegisterClass = "HoldingRegister",
            Address = 40,
            Datatype = "string",
            StringLength = 16,
            ByteOrder = "ABCD",
        };

        var issues = ModbusSourceWizardModel.ValidateTag(row);

        issues.Should().Contain(iss =>
            iss.Code == "MODBUS.CONFIG_INVALID" && iss.Path == "ByteOrder");
    }

    [Fact]
    public void BuildSourceInstance_StringDatatype_EmitsStringNComposed()
    {
        // Pin the wire-shape contract — wizard's split form maps to the
        // canonical "stringN" that ModbusTcpSourceConfiguration.FromSourceInstance
        // expects on the Core side.
        var model = new ModbusSourceWizardModel
        {
            InstanceId = "modbus-1",
            Host = "127.0.0.1",
            Tags =
            {
                new ModbusTagWizardRow
                {
                    Name = "program_number",
                    RegisterClass = "HoldingRegister",
                    Address = 40,
                    Datatype = "string",
                    StringLength = 8,
                    ScanRateMs = 1000,
                },
            },
        };

        var instance = model.BuildSourceInstance();
        var tag = instance.Connection!.Value.GetProperty("tagDefinitions").EnumerateArray().First();

        tag.GetProperty("datatype").GetString().Should().Be("string8");
    }

    // ── HYDRATE ROUND-TRIP (M.2d.2 §5.5 Edit-mode hydration) ───────────────
    [Fact]
    public void HydrateFromExisting_RoundTrips_ByteEquivalentSourceInstanceConfig()
    {
        // Modbus is the most demanding round-trip case: a per-tag list with
        // optional fields (datatype / byteOrder / scale / offset / unit) that
        // BuildSourceInstance omits when blank. HydrateFromExisting must
        // restore exactly the set of fields the emit included — and preserve
        // tag order — so the JSON re-emits byte-equivalently.
        var original = new ModbusSourceWizardModel
        {
            InstanceId = "modbus-line-7",
            DeviceId = "S7-1200-L7",
            DeviceName = "Siemens S7 Line 7",
            DeviceClass = "plc",
            Enabled = false,
            PollIntervalMs = 250,
            Host = "192.168.1.42",
            Port = 5020,
            Encapsulation = "RtuOverTcp",
            DefaultUnitId = 3,
            ConnectTimeoutMs = 4000,
            RequestTimeoutMs = 2500,
            KeepAlive = false,
            MaxTransactionRetries = 4,
            InitialBackoffMs = 1500,
            MaxBackoffMs = 45_000,
            BackoffMultiplier = 1.7,
            CircuitBreakerThreshold = 6,
            CircuitBreakerResetMs = 25_000,
            MaxGapRegisters = 16,
            Tags = new List<ModbusTagWizardRow>
            {
                new()
                {
                    Name = "ProductionCount",
                    UnitId = 1,
                    RegisterClass = "HoldingRegister",
                    Address = 100,
                    ScanRateMs = 500,
                    Datatype = "uint32",
                    ByteOrder = "CDAB",
                    Scale = 1.0,
                    Offset = 0.0,
                    Unit = "parts",
                },
                new()
                {
                    Name = "DoorOpen",
                    UnitId = 1,
                    RegisterClass = "Coil",
                    Address = 5,
                    ScanRateMs = 200,
                    Datatype = "bool",
                    // ByteOrder, Scale, Offset, Unit intentionally null — emit
                    // should omit them, and hydrate should leave them null.
                },
                new()
                {
                    Name = "JobName",
                    UnitId = 2,
                    RegisterClass = "HoldingRegister",
                    Address = 2000,
                    ScanRateMs = 1000,
                    Datatype = "string",
                    StringLength = 16,   // emits as "string16"; hydrate splits back
                    ByteOrder = "AB",
                },
                new()
                {
                    Name = "Spindle1Load",
                    UnitId = 1,
                    RegisterClass = "InputRegister",
                    Address = 350,
                    ScanRateMs = 250,
                    Datatype = "float32",
                    ByteOrder = "ABCD",
                    Scale = 0.1,
                    Offset = -50.0,
                    Unit = "%",
                },
            },
        };

        var firstEmit = original.BuildSourceInstance();
        var hydrated = ModbusSourceWizardModel.HydrateFromExisting(firstEmit);
        var secondEmit = hydrated.BuildSourceInstance();

        // Identity + polling
        secondEmit.InstanceId.Should().Be(firstEmit.InstanceId);
        secondEmit.ProtocolName.Should().Be(firstEmit.ProtocolName);
        secondEmit.DeviceId.Should().Be(firstEmit.DeviceId);
        secondEmit.DeviceName.Should().Be(firstEmit.DeviceName);
        secondEmit.DeviceClass.Should().Be(firstEmit.DeviceClass);
        secondEmit.Enabled.Should().Be(firstEmit.Enabled);
        secondEmit.Polling.IntervalMs.Should().Be(firstEmit.Polling.IntervalMs);

        // Byte-equivalent Connection block — same field set, same order,
        // same per-tag attribute presence.
        secondEmit.Connection.Should().NotBeNull();
        firstEmit.Connection.Should().NotBeNull();
        secondEmit.Connection!.Value.GetRawText().Should().Be(firstEmit.Connection!.Value.GetRawText());
    }

    [Fact]
    public void HydrateFromExisting_PreservesTagOrder()
    {
        // Modbus scan planner ordering can be sensitive to tag-list order;
        // even if the planner is order-independent today, the operator's
        // visual ordering must survive a hydrate → re-emit cycle so they
        // see the same list they saved.
        var original = new ModbusSourceWizardModel
        {
            InstanceId = "modbus-order",
            Host = "1.2.3.4",
            Tags = new List<ModbusTagWizardRow>
            {
                new() { Name = "Zeta", UnitId = 1, RegisterClass = "HoldingRegister", Address = 0, ScanRateMs = 1000 },
                new() { Name = "Alpha", UnitId = 1, RegisterClass = "HoldingRegister", Address = 1, ScanRateMs = 1000 },
                new() { Name = "Mu", UnitId = 1, RegisterClass = "HoldingRegister", Address = 2, ScanRateMs = 1000 },
            },
        };
        var emitted = original.BuildSourceInstance();

        var hydrated = ModbusSourceWizardModel.HydrateFromExisting(emitted);

        hydrated.Tags.Select(t => t.Name).Should().Equal("Zeta", "Alpha", "Mu");
    }

    [Fact]
    public void HydrateFromExisting_StringNDatatype_SplitsBackIntoStringAndLength()
    {
        // The "stringN" wire form is composed in BuildSourceInstance from
        // the split (Datatype="string" + StringLength). Hydrate must reverse
        // that composition so the wizard UI presents the operator's
        // original String + length pair, not the wire form.
        var original = new ModbusSourceWizardModel
        {
            InstanceId = "modbus-strN",
            Host = "1.2.3.4",
            Tags = new List<ModbusTagWizardRow>
            {
                new()
                {
                    Name = "Job",
                    UnitId = 1,
                    RegisterClass = "HoldingRegister",
                    Address = 0,
                    ScanRateMs = 1000,
                    Datatype = "string",
                    StringLength = 24,
                    ByteOrder = "AB",
                },
            },
        };
        var emitted = original.BuildSourceInstance();

        var hydrated = ModbusSourceWizardModel.HydrateFromExisting(emitted);

        var row = hydrated.Tags.Should().ContainSingle().Subject;
        row.Datatype.Should().Be("string");
        row.StringLength.Should().Be(24);
        row.ByteOrder.Should().Be("AB");
    }

    [Fact]
    public void HydrateFromExisting_EmptyTagList_HydratesToEmptyList()
    {
        var original = new ModbusSourceWizardModel
        {
            InstanceId = "modbus-empty",
            Host = "1.2.3.4",
            Tags = new List<ModbusTagWizardRow>(),
        };
        var emitted = original.BuildSourceInstance();

        var hydrated = ModbusSourceWizardModel.HydrateFromExisting(emitted);

        hydrated.Tags.Should().BeEmpty();
    }

    [Fact]
    public void HydrateFromExisting_WrongProtocol_Throws()
    {
        var focas2 = new SourceInstanceConfig
        {
            InstanceId = "f",
            ProtocolName = "focas2",
            DeviceId = "f",
        };

        var act = () => ModbusSourceWizardModel.HydrateFromExisting(focas2);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("*modbustcp*");
    }

    // ─── Register-class-driven datatype suggestion ──────────────────────────
    // The register class already decides the width — a coil and a discrete
    // input ARE single bits — so the pre-filled datatype follows the class the
    // operator picked instead of a blanket uint16 that the shared validator
    // then rejects. Suggest, never coerce.

    [Theory]
    [InlineData("Coil", "bool")]
    [InlineData("DiscreteInput", "bool")]
    [InlineData("HoldingRegister", "uint16")]
    [InlineData("InputRegister", "uint16")]
    public void SuggestDatatypeForRegisterClass_KnownClass_ReturnsWidthImpliedByClass(
        string registerClass, string expected)
    {
        ModbusSourceWizardModel.SuggestDatatypeForRegisterClass(registerClass)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("Coil")]
    [InlineData("DiscreteInput")]
    [InlineData("HoldingRegister")]
    [InlineData("InputRegister")]
    public void SuggestDatatypeForRegisterClass_KnownClass_ReturnsValueOfferedByTheDropdown(
        string registerClass)
    {
        // A suggestion outside the dropdown's option list renders as a blank
        // cell — worse than the wrong-but-visible value it replaced.
        var suggestion = ModbusSourceWizardModel.SuggestDatatypeForRegisterClass(registerClass);

        ModbusSourceWizardModel.Datatypes.Should().Contain(suggestion!);
    }

    [Fact]
    public void SuggestDatatypeForRegisterClass_UnrecognisedClass_ReturnsNull()
    {
        ModbusSourceWizardModel.SuggestDatatypeForRegisterClass("Sausage").Should().BeNull();
        ModbusSourceWizardModel.SuggestDatatypeForRegisterClass(null).Should().BeNull();
    }

    [Fact]
    public void ApplyRegisterClassDatatypeSuggestion_CoilOnUntouchedRow_SuggestsBool()
    {
        var row = new ModbusTagWizardRow { Name = "running" };
        row.RegisterClass = "Coil";

        row.ApplyRegisterClassDatatypeSuggestion();

        row.Datatype.Should().Be("bool");
        ModbusSourceWizardModel.ValidateTag(row).Should().BeEmpty();
    }

    [Fact]
    public void ApplyRegisterClassDatatypeSuggestion_DiscreteInputOnUntouchedRow_SuggestsBool()
    {
        var row = new ModbusTagWizardRow { Name = "door_open" };
        row.RegisterClass = "DiscreteInput";

        row.ApplyRegisterClassDatatypeSuggestion();

        row.Datatype.Should().Be("bool");
    }

    [Fact]
    public void ApplyRegisterClassDatatypeSuggestion_HoldingRegisterOnUntouchedRow_KeepsUint16Default()
    {
        // Registers legitimately carry several widths, so the class implies
        // nothing beyond "16-bit word" — the historical default stands.
        var row = new ModbusTagWizardRow { Name = "spindle_speed" };
        row.RegisterClass = "HoldingRegister";

        row.ApplyRegisterClassDatatypeSuggestion();

        row.Datatype.Should().Be("uint16");
    }

    [Fact]
    public void ApplyRegisterClassDatatypeSuggestion_InputRegisterOnUntouchedRow_KeepsUint16Default()
    {
        var row = new ModbusTagWizardRow { Name = "temperature" };
        row.RegisterClass = "InputRegister";

        row.ApplyRegisterClassDatatypeSuggestion();

        row.Datatype.Should().Be("uint16");
    }

    [Fact]
    public void ApplyRegisterClassDatatypeSuggestion_CoilAfterSuggestedUint16_RevisesTheSuggestion()
    {
        // A row starts life as a HoldingRegister carrying the suggested
        // uint16; switching it to a bit class must revise that suggestion
        // rather than leave a value the class cannot hold.
        var row = new ModbusTagWizardRow { Name = "estop" };
        row.Datatype.Should().Be("uint16");
        row.DatatypeIsOperatorChosen.Should().BeFalse();

        row.RegisterClass = "Coil";
        row.ApplyRegisterClassDatatypeSuggestion();

        row.Datatype.Should().Be("bool");
    }

    [Fact]
    public void ApplyRegisterClassDatatypeSuggestion_OperatorChoseDatatype_LeavesItUntouched()
    {
        // Suggest, never coerce — a deliberate choice outranks the class hint,
        // even when the class disagrees. The validator surfaces the conflict;
        // the wizard does not silently rewrite the operator's decision.
        var row = new ModbusTagWizardRow { Name = "packed_word", Datatype = "int16" };
        row.DatatypeIsOperatorChosen.Should().BeTrue();

        row.RegisterClass = "Coil";
        row.ApplyRegisterClassDatatypeSuggestion();

        row.Datatype.Should().Be("int16");
    }

    [Fact]
    public void ApplyRegisterClassDatatypeSuggestion_BoolSuggestion_ClearsByteOrder()
    {
        // Byte order is not applicable to a single bit, and the shared
        // validator rejects it outright.
        var row = new ModbusTagWizardRow { Name = "flag", ByteOrder = "AB" };

        row.RegisterClass = "DiscreteInput";
        row.ApplyRegisterClassDatatypeSuggestion();

        row.Datatype.Should().Be("bool");
        row.ByteOrder.Should().BeNull();
    }

    [Fact]
    public void HydrateFromExisting_ThenRegisterClassChange_KeepsSavedDatatype()
    {
        // Edit path: a tag already applied with uint16 keeps uint16, even if
        // the operator flips the register class while editing. Existing saved
        // configurations are never rewritten behind the operator's back.
        var original = new ModbusSourceWizardModel
        {
            InstanceId = "modbus-edit",
            Host = "1.2.3.4",
            Tags = new List<ModbusTagWizardRow>
            {
                new()
                {
                    Name = "legacy",
                    UnitId = 1,
                    RegisterClass = "HoldingRegister",
                    Address = 4,
                    ScanRateMs = 1000,
                    Datatype = "uint16",
                },
            },
        };
        var hydrated = ModbusSourceWizardModel.HydrateFromExisting(original.BuildSourceInstance());
        var row = hydrated.Tags.Should().ContainSingle().Subject;
        row.Datatype.Should().Be("uint16");

        row.RegisterClass = "Coil";
        row.ApplyRegisterClassDatatypeSuggestion();

        row.Datatype.Should().Be("uint16");
    }

    [Fact]
    public void HydrateFromExisting_TagWithoutDatatype_StaysWithoutDatatype()
    {
        // A saved tag that carried no datatype must not acquire one on hydrate
        // or on a later register-class change — that would change what the
        // adapter reads for an already-applied configuration.
        var original = new ModbusSourceWizardModel
        {
            InstanceId = "modbus-nodt",
            Host = "1.2.3.4",
            Tags = new List<ModbusTagWizardRow>
            {
                new()
                {
                    Name = "bare",
                    UnitId = 1,
                    RegisterClass = "Coil",
                    Address = 0,
                    ScanRateMs = 1000,
                    Datatype = null,
                },
            },
        };
        var hydrated = ModbusSourceWizardModel.HydrateFromExisting(original.BuildSourceInstance());
        var row = hydrated.Tags.Should().ContainSingle().Subject;

        row.ApplyRegisterClassDatatypeSuggestion();

        row.Datatype.Should().BeNull();
    }
}
