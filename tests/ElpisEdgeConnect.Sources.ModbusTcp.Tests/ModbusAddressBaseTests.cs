// ============================================================================
// File: ModbusAddressBaseTests.cs
// Purpose: Unit tests for the address-base input-normalisation layer —
//          operator-entered addresses (zero-based / one-based / Modicon 4xxxx)
//          are converted ONCE at config-parse time into the zero-based logical
//          addresses the wire protocol requires.
//
//          Also pins the silent-misconfiguration guard: under the default
//          zero-based notation, a Modicon-looking address is a config-time
//          validation ERROR rather than a runtime Quality=Bad/Value=null read.
// Reference: docs/modbus-address-base-design.md
// ============================================================================

using System.Collections.Generic;
using System.Text.Json;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Tests;

public sealed class ModbusAddressBaseTests
{
    // ---------------------------------------------------------------- helpers

    private static SourceInstanceConfig Instance(string connectionJson) => new()
    {
        InstanceId = "plc-1",
        ProtocolName = "modbustcp",
        DeviceId = "plc1",
        DeviceClass = "plc",
        Connection = JsonDocument.Parse(connectionJson).RootElement,
    };

    private static string ConnWithTag(string? addressBase, string registerClass, int address)
    {
        var baseLine = addressBase is null ? "" : $"\"addressBase\": \"{addressBase}\",";
        return $$"""
        {
          "host": "192.168.1.50",
          {{baseLine}}
          "tagDefinitions": [
            { "name": "t1", "registerClass": "{{registerClass}}", "address": {{address}}, "datatype": "int16" }
          ]
        }
        """;
    }

    // ---------------------------------------------------- conversion (parsing)

    [Fact]
    public void FromSourceInstance_DefaultsToZeroBased_AndLeavesAddressUnchanged()
    {
        var cfg = ModbusTcpSourceConfiguration.FromSourceInstance(
            Instance(ConnWithTag(addressBase: null, "HoldingRegister", 32)));

        cfg.AddressBase.Should().Be(ModbusAddressBase.ZeroBased,
            "omitting addressBase must preserve the historical contract");
        cfg.TagDefinitions[0].Address.Should().Be(32);
    }

    [Theory]
    [InlineData("HoldingRegister", 40001, 0)]
    [InlineData("HoldingRegister", 40033, 32)]   // set_pressure from the customer's PLC map
    [InlineData("HoldingRegister", 40041, 40)]   // cycle_count
    [InlineData("InputRegister", 30001, 0)]
    [InlineData("DiscreteInput", 10001, 0)]
    [InlineData("Coil", 1, 0)]
    public void FromSourceInstance_Modicon_SubtractsClassPrefix(
        string registerClass, int entered, int expectedWire)
    {
        var cfg = ModbusTcpSourceConfiguration.FromSourceInstance(
            Instance(ConnWithTag("Modicon", registerClass, entered)));

        cfg.AddressBase.Should().Be(ModbusAddressBase.Modicon);
        cfg.TagDefinitions[0].Address.Should().Be((ushort)expectedWire);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(33, 32)]
    public void FromSourceInstance_OneBased_SubtractsOne(int entered, int expectedWire)
    {
        var cfg = ModbusTcpSourceConfiguration.FromSourceInstance(
            Instance(ConnWithTag("OneBased", "HoldingRegister", entered)));

        cfg.TagDefinitions[0].Address.Should().Be((ushort)expectedWire);
    }

    [Fact]
    public void FromSourceInstance_ModiconAddressBelowClassBase_Throws()
    {
        // 1 is a valid coil in Modicon, but nonsense for a holding register
        // (40001 is the first) — fail loudly instead of wrapping to a bogus register.
        var act = () => ModbusTcpSourceConfiguration.FromSourceInstance(
            Instance(ConnWithTag("Modicon", "HoldingRegister", 1)));

        act.Should().Throw<System.ArgumentException>()
            .WithMessage("*addressBase*");
    }

    [Fact]
    public void FromSourceInstance_AddressBaseIsCaseInsensitive()
    {
        var cfg = ModbusTcpSourceConfiguration.FromSourceInstance(
            Instance(ConnWithTag("modicon", "HoldingRegister", 40033)));

        cfg.TagDefinitions[0].Address.Should().Be(32);
    }

    // -------------------------------------------------- conversion (pure API)

    [Theory]
    [InlineData(ModbusRegisterClass.Coil, 1)]
    [InlineData(ModbusRegisterClass.DiscreteInput, 10001)]
    [InlineData(ModbusRegisterClass.InputRegister, 30001)]
    [InlineData(ModbusRegisterClass.HoldingRegister, 40001)]
    public void ModiconOffset_MatchesTheClassicDataModel(ModbusRegisterClass rc, int expected) =>
        ModbusAddressBaseExtensions.ModiconOffset(rc).Should().Be(expected);

    [Fact]
    public void TryToZeroBased_ZeroBased_IsIdentity()
    {
        ModbusAddressBase.ZeroBased
            .TryToZeroBased(32, ModbusRegisterClass.HoldingRegister, out var wire)
            .Should().BeTrue();
        wire.Should().Be(32);
    }

    [Fact]
    public void TryToZeroBased_ResultOutsideAddressSpace_ReturnsFalse()
    {
        ModbusAddressBase.Modicon
            .TryToZeroBased(5, ModbusRegisterClass.HoldingRegister, out _)
            .Should().BeFalse();
    }

    // ------------------------------------------------------------- validation

    [Fact]
    public void Validator_ZeroBasedWithModiconLookingAddress_IsAnError()
    {
        // The exact silent misconfiguration this feature exists to catch:
        // operator typed the address straight off the PLC manual.
        var tag = new ModbusTagDefinition
        {
            Name = "set_pressure",
            RegisterClass = ModbusRegisterClass.HoldingRegister,
            Address = 40033,
            Datatype = "int16",
        };
        var errors = new List<ValidationIssue>();

        ModbusTagValidator.Validate(tag, "TagDefinitions[0]", errors, ModbusAddressBase.ZeroBased);

        errors.Should().ContainSingle(e => e.Path == "TagDefinitions[0].Address")
            .Which.Message.Should().Contain("32", "the message must tell the operator the correct address");
    }

    [Fact]
    public void Validator_ModiconBase_DoesNotFlagAlreadyConvertedAddress()
    {
        // Under Modicon the address arriving here is already zero-based (32),
        // so there is nothing to warn about.
        var tag = new ModbusTagDefinition
        {
            Name = "set_pressure",
            RegisterClass = ModbusRegisterClass.HoldingRegister,
            Address = 32,
            Datatype = "int16",
        };
        var errors = new List<ValidationIssue>();

        ModbusTagValidator.Validate(tag, "TagDefinitions[0]", errors, ModbusAddressBase.Modicon);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validator_ZeroBasedOrdinaryAddress_IsNotFlagged()
    {
        var tag = new ModbusTagDefinition
        {
            Name = "actual_temperature",
            RegisterClass = ModbusRegisterClass.HoldingRegister,
            Address = 0,
            Datatype = "int16",
        };
        var errors = new List<ValidationIssue>();

        ModbusTagValidator.Validate(tag, "TagDefinitions[0]", errors);

        errors.Should().BeEmpty();
    }

    // ------------------------------------------------------- connection keys

    [Fact]
    public void ConnectionKeys_IncludeAddressBase() =>
        ModbusTcpConnectionKeys.All.Should().Contain(ModbusTcpConnectionKeys.AddressBase);
}
