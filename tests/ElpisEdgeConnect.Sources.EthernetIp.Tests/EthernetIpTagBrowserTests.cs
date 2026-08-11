// ============================================================================
// File: EthernetIpTagBrowserTests.cs
// Purpose: Pin the CIP symbol-table wire format and the datatype mapping the
//          tag browser exposes to the source wizard.
//
//          The browser exists because a controller that publishes nothing you
//          expect is indistinguishable, through test-read alone, from a
//          controller you are addressing wrongly. These tests fix the decoding
//          so the listing can be trusted as the answer to "what can I address?"
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using ElpisEdgeConnect.Sources.EthernetIp;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.EthernetIp.Tests;

public class EthernetIpTagBrowserDecodeTests
{
    /// <summary>
    /// Build one listing entry in the controller's wire format:
    /// instance id (4) + type (2) + element length (2) + dims (12) +
    /// name length (2) + name.
    /// </summary>
    private static byte[] Entry(string name, ushort typeCode, ushort elementLength)
    {
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes((uint)0x1234));
        bytes.AddRange(BitConverter.GetBytes(typeCode));
        bytes.AddRange(BitConverter.GetBytes(elementLength));
        bytes.AddRange(BitConverter.GetBytes((uint)0));
        bytes.AddRange(BitConverter.GetBytes((uint)0));
        bytes.AddRange(BitConverter.GetBytes((uint)0));
        bytes.AddRange(BitConverter.GetBytes((ushort)name.Length));
        bytes.AddRange(Encoding.ASCII.GetBytes(name));
        return [.. bytes];
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var all = new List<byte>();
        foreach (var p in parts) all.AddRange(p);
        return [.. all];
    }

    [Fact]
    public void Decode_ReadsNameTypeAndLength()
    {
        var payload = Entry("_IO_EM_DI_00", 0x00C1, 1);

        var symbols = LibPlcTagTagBrowser.Decode(payload);

        symbols.Should().ContainSingle();
        symbols[0].Name.Should().Be("_IO_EM_DI_00");
        symbols[0].CipTypeCode.Should().Be(0x00C1);
        symbols[0].ElementLength.Should().Be(1);
        symbols[0].Datatype.Should().Be("BOOL");
    }

    [Fact]
    public void Decode_ReadsEveryEntryInOrder()
    {
        var payload = Concat(
            Entry("_IO_EM_DI_00", 0x00C1, 1),
            Entry("_IO_EM_AI_00", 0x00C7, 2),
            Entry("WaterLevel", 0x00C3, 2));

        var symbols = LibPlcTagTagBrowser.Decode(payload);

        symbols.Should().HaveCount(3);
        symbols.Should().SatisfyRespectively(
            a => a.Name.Should().Be("_IO_EM_DI_00"),
            b => b.Name.Should().Be("_IO_EM_AI_00"),
            c => c.Name.Should().Be("WaterLevel"));
    }

    [Fact]
    public void Decode_SeparatesControllerBuiltInsFromProjectVariables()
    {
        // The distinction the incident turned on: a listing full of _IO_EM_*
        // and nothing else means the project publishes nothing.
        var payload = Concat(
            Entry("_IO_EM_DI_00", 0x00C1, 1),
            Entry("__SYSVA_CYCLECNT", 0x00C4, 4),
            Entry("WaterLevel", 0x00C3, 2));

        var symbols = LibPlcTagTagBrowser.Decode(payload);

        symbols[0].IsBuiltIn.Should().BeTrue();
        symbols[1].IsBuiltIn.Should().BeTrue();
        symbols[2].IsBuiltIn.Should().BeFalse();
    }

    [Fact]
    public void Decode_FlagsStructuresAndLeavesThemWithoutADatatype()
    {
        // Bit 15 set = structure/UDT; the low bits are a template id, not an
        // atomic type, so no datatype token can be offered.
        var payload = Entry("MyUdt", 0x8ABC, 20);

        var symbols = LibPlcTagTagBrowser.Decode(payload);

        symbols[0].IsStructure.Should().BeTrue();
        symbols[0].Datatype.Should().BeNull();
    }

    [Fact]
    public void Decode_StopsCleanlyOnTrailingPadding()
    {
        // Real payloads are followed by padding; a zero name length marks the
        // end. Decoding must stop rather than emit garbage entries.
        var payload = Concat(Entry("_IO_EM_DI_00", 0x00C1, 1), new byte[40]);

        var symbols = LibPlcTagTagBrowser.Decode(payload);

        symbols.Should().ContainSingle();
    }

    [Fact]
    public void Decode_StopsWhenANameWouldOverrunTheBuffer()
    {
        var truncated = Entry("_IO_EM_DI_00", 0x00C1, 1)[..18];

        var symbols = LibPlcTagTagBrowser.Decode(truncated);

        symbols.Should().BeEmpty();
    }

    [Fact]
    public void Decode_EmptyPayloadYieldsNoSymbols()
    {
        LibPlcTagTagBrowser.Decode([]).Should().BeEmpty();
    }
}

public class EthernetIpTagBrowserDatatypeMappingTests
{
    [Theory]
    [InlineData(0x00C1, "BOOL")]
    [InlineData(0x00C2, "SINT")]
    [InlineData(0x00C3, "INT")]
    [InlineData(0x00C4, "DINT")]
    [InlineData(0x00C5, "LINT")]
    [InlineData(0x00CA, "REAL")]
    [InlineData(0x00CB, "LREAL")]
    public void AtomicTypesMapToTheirConfigurationToken(int cip, string expected)
    {
        LibPlcTagTagBrowser.MapDatatype(cip).Should().Be(expected);
    }

    [Theory]
    [InlineData(0x00C6, "SINT")]   // USINT
    [InlineData(0x00C7, "INT")]    // UINT — the Micro820 analog channels
    [InlineData(0x00C8, "DINT")]   // UDINT
    [InlineData(0x00C9, "LINT")]   // ULINT
    public void UnsignedTypesMapOntoTheSameWidthSignedToken(int cip, string expected)
    {
        // EdgeConnect has no unsigned element types; the same-width signed token
        // reads the correct bytes. Documented so the widening is deliberate.
        LibPlcTagTagBrowser.MapDatatype(cip).Should().Be(expected);
    }

    [Fact]
    public void UnknownTypeCodeYieldsNoSuggestion()
    {
        // Better to offer nothing than to suggest a datatype that silently
        // decodes the wrong bytes.
        LibPlcTagTagBrowser.MapDatatype(0x1234).Should().BeNull();
    }

    [Fact]
    public void EveryMappedTokenIsAcceptedByTheElementTypeParser()
    {
        // The browser's suggestion must be directly usable as a tag datatype;
        // a token the parser rejects would be worse than no suggestion.
        foreach (var cip in new[] { 0x00C1, 0x00C2, 0x00C3, 0x00C4, 0x00C5,
                                    0x00C6, 0x00C7, 0x00C8, 0x00C9, 0x00CA, 0x00CB })
        {
            var token = LibPlcTagTagBrowser.MapDatatype(cip);
            token.Should().NotBeNull();
            EthernetIpElementTypeExtensions.ParseOrNull(token!).Should().NotBeNull(
                because: $"CIP 0x{cip:X4} suggests '{token}', which must be a valid tag datatype");
        }
    }
}
