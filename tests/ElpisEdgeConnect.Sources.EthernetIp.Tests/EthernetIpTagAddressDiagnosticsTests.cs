// ============================================================================
// File: EthernetIpTagAddressDiagnosticsTests.cs
// Purpose: Guards the three diagnosability defects found while investigating an
//          EtherNet/IP source that delivered no data against an Allen-Bradley
//          Micro820 (2080-LC20-20QBB).
//
//          D-1  ErrorBadParam was reported as ETHERNETIP.TYPE_MISMATCH, on the
//               premise that "the controller answered, but the request was bad".
//               Measurement disproved it: ErrorBadParam returns in 0-30ms where a
//               real round trip is 12-60ms and a genuine timeout is the full
//               3000ms. Nothing was ever sent, so no type was observed — the
//               operator was pointed at a data-type problem that did not exist.
//
//          D-3  A single trailing space ("Start_PB ") made libplctag refuse to
//               build the tag, silently disabling it for the life of the
//               deployment. Nothing trimmed or warned.
//
//          These pin BEHAVIOUR, not wording: the error CODE an operator filters
//          on, and whether validation blocks or merely warns.
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Sources.EthernetIp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.EthernetIp.Tests;

public class EthernetIpTagAddressDiagnosticsTests
{
    private static EthernetIpSourceConfiguration ConfigWith(params EthernetIpTagDefinition[] tags) =>
        new()
        {
            InstanceId = "eip-1",
            ProtocolName = "ethernetip",
            DeviceId = "dev-1",
            DeviceClass = "plc",
            Host = "10.0.0.1",
            CpuFamily = EthernetIpCpuFamily.Micro800,
            TagDefinitions = tags,
        };

    private static EthernetIpSourceAdapter NewAdapter() =>
        new("eip-1", new FakeEthernetIpClient(), NullLogger.Instance);

    // ─── D-3: surrounding whitespace warns, and does NOT block the apply ────

    [Theory]
    [InlineData("Start_PB ")]
    [InlineData(" Start_PB")]
    [InlineData("  Start_PB  ")]
    public async Task SurroundingWhitespace_Warns_ButConfigStaysValid(string address)
    {
        var cfg = ConfigWith(new EthernetIpTagDefinition
        {
            Name = "Tag1",
            Address = address,
            Datatype = "BOOL",
        });

        var result = await NewAdapter().ValidateConfigAsync(cfg, CancellationToken.None);

        result.IsValid.Should().BeTrue(
            "the client trims it and reads correctly — failing the apply would take this " +
            "source's other, healthy tags offline over something already recovered");
        result.Warnings.Should().ContainSingle()
            .Which.Path.Should().Be("TagDefinitions[0].Address");
    }

    [Fact]
    public async Task SurroundingWhitespace_WarningNamesTheStoredAndEffectiveAddress()
    {
        var cfg = ConfigWith(new EthernetIpTagDefinition
        {
            Name = "Tag1",
            Address = "Start_PB ",
            Datatype = "BOOL",
        });

        var result = await NewAdapter().ValidateConfigAsync(cfg, CancellationToken.None);

        // The whole point is that the operator cannot SEE the space, so the
        // message has to quote both forms for the difference to be visible.
        var message = result.Warnings.Single().Message;
        message.Should().Contain("\"Start_PB \"");
        message.Should().Contain("\"Start_PB\"");
    }

    [Fact]
    public async Task InteriorWhitespace_IsAnError_BecauseTrimmingCannotRecoverIt()
    {
        var cfg = ConfigWith(new EthernetIpTagDefinition
        {
            Name = "Tag1",
            Address = "Start PB",
            Datatype = "BOOL",
        });

        var result = await NewAdapter().ValidateConfigAsync(cfg, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path == "TagDefinitions[0].Address");
    }

    [Fact]
    public async Task CleanAddress_ProducesNeitherErrorNorWarning()
    {
        var cfg = ConfigWith(new EthernetIpTagDefinition
        {
            Name = "Tag1",
            Address = "_IO_EM_DI_00",
            Datatype = "BOOL",
        });

        var result = await NewAdapter().ValidateConfigAsync(cfg, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    // ─── D-1: the error code an operator filters on ────────────────────────

    [Fact]
    public void TagDefinitionInvalid_IsDistinctFromTypeMismatch()
    {
        // Two different faults must not share a code: TYPE_MISMATCH means the
        // controller answered and rejected the element type, which is actionable
        // by changing the datatype. TAG_DEFINITION_INVALID means nothing was
        // sent at all, which is actionable by fixing the address. Collapsing
        // them sent operators to the wrong field.
        EthernetIpErrors.TagDefinitionInvalid.Should().NotBe(EthernetIpErrors.TypeMismatch);
        EthernetIpErrors.TagDefinitionInvalid.Should().Be("ETHERNETIP.TAG_DEFINITION_INVALID");
    }

    [Fact]
    public void ErrorCatalog_KeepsTheEthernetIpPrefix()
    {
        // MODULE.CATEGORY_SUBCATEGORY per CLAUDE.md; operators filter on the
        // prefix, so a new code that breaks the convention is invisible.
        EthernetIpErrors.TagDefinitionInvalid.Should().StartWith("ETHERNETIP.");
    }
}
