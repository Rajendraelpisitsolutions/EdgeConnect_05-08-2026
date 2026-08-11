// ============================================================================
// File: EthernetIpTagDiagnosticsTests.cs
// Purpose: Pins the diagnostic contract for tag-level EtherNet/IP failures.
//
//          Regression cover for a field incident (Micro820 at a customer site)
//          where a tag address carried an invisible trailing space. libplctag
//          rejected the tag definition locally with BAD_PARAM — no request ever
//          reached the controller — but the adapter reported it as
//          ETHERNETIP.TYPE_MISMATCH, sending the operator to check BOOL-vs-DINT
//          instead of the address string. Diagnosis took a full evidence sweep;
//          it should have taken one error message.
// ============================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Sources.EthernetIp;
using FluentAssertions;
using libplctag;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.EthernetIp.Tests;

public class EthernetIpTagErrorClassificationTests
{
    [Fact]
    public void BadParam_IsNotReportedAsTypeMismatch()
    {
        // BAD_PARAM is raised by libplctag before any I/O, so nothing is known
        // about the controller's actual data type. Calling it a type mismatch
        // is a claim the adapter is not entitled to make.
        LibPlcTagClient.ClassifyTagError(Status.ErrorBadParam)
            .Should().Be(EthernetIpErrors.TagDefinitionInvalid);
    }

    [Fact]
    public void Unsupported_IsStillATypeMismatch()
    {
        // The controller answered and refused the service for this tag — that
        // genuinely is a type/service mismatch.
        LibPlcTagClient.ClassifyTagError(Status.ErrorUnsupported)
            .Should().Be(EthernetIpErrors.TypeMismatch);
    }

    [Fact]
    public void NotFound_MapsToTagNotFound()
    {
        LibPlcTagClient.ClassifyTagError(Status.ErrorNotFound)
            .Should().Be(EthernetIpErrors.TagNotFound);
    }

    [Fact]
    public void BadParam_StaysTagLevel_SoSiblingTagsKeepPolling()
    {
        LibPlcTagClient.IsTagLevelError(Status.ErrorBadParam).Should().BeTrue();
    }

    [Fact]
    public void Timeout_IsFatal_NotTagLevel()
    {
        LibPlcTagClient.IsTagLevelError(Status.ErrorTimeout).Should().BeFalse();
    }

    [Fact]
    public void BadParam_MessageTellsTheOperatorWhatToCheck()
    {
        var msg = LibPlcTagClient.DescribeTagError(Status.ErrorBadParam, "Start_PB ", "ErrorBadParam");

        // The bare libplctag status name is useless to an operator. The message
        // must name the two things that actually cause this.
        msg.Should().Contain("Start_PB ");
        msg.Should().Contain("not contacted");
        msg.Should().ContainEquivalentOf("spaces");
        msg.Should().ContainEquivalentOf("Global Variable");
    }

    [Fact]
    public void OtherTagErrors_KeepTheRawStatusText()
    {
        LibPlcTagClient.DescribeTagError(Status.ErrorNotFound, "Speed", "ErrorNotFound")
            .Should().Be("Read of 'Speed' failed: ErrorNotFound");
    }
}

public class EthernetIpTagAddressWhitespaceTests
{
    private static EthernetIpSourceConfiguration BaseConfig(params EthernetIpTagDefinition[] tags) =>
        new()
        {
            InstanceId = "eip-1",
            ProtocolName = "ethernetip",
            DeviceId = "dev-1",
            DeviceClass = "plc",
            Host = "10.0.0.1",
            CpuFamily = EthernetIpCpuFamily.ControlLogix,
            TagDefinitions = tags,
        };

    private static EthernetIpSourceAdapter NewAdapter() =>
        new("eip-1", new FakeEthernetIpClient(), NullLogger.Instance);

    [Theory]
    [InlineData("Start_PB ")]
    [InlineData(" Start_PB")]
    [InlineData("Start_PB\t")]
    public async Task SurroundingWhitespace_WarnsButStillApplies(string address)
    {
        var cfg = BaseConfig(new EthernetIpTagDefinition
        {
            Name = "Tag1", Address = address, Datatype = "BOOL",
        });

        var result = await NewAdapter().ValidateConfigAsync(cfg, CancellationToken.None);

        // The client trims at read time, so this must not block the apply and
        // take the source's other tags down with it.
        result.IsValid.Should().BeTrue();
        result.Warnings.Should().ContainSingle()
            .Which.Path.Should().Be("TagDefinitions[0].Address");
        result.Warnings[0].Message.Should().Contain("whitespace");
        result.Warnings[0].Message.Should().Contain("Start_PB");
    }

    [Fact]
    public async Task InternalWhitespace_IsAnError()
    {
        // Trimming cannot recover this one — a CIP symbolic name has no spaces.
        var cfg = BaseConfig(new EthernetIpTagDefinition
        {
            Name = "Tag1", Address = "Start PB", Datatype = "BOOL",
        });

        var result = await NewAdapter().ValidateConfigAsync(cfg, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Path == "TagDefinitions[0].Address");
    }

    [Fact]
    public async Task CleanAddress_ProducesNoWarnings()
    {
        var cfg = BaseConfig(new EthernetIpTagDefinition
        {
            Name = "Tag2", Address = "Stop_PB", Datatype = "BOOL",
        });

        var result = await NewAdapter().ValidateConfigAsync(cfg, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task WhitespaceWarning_IsReportedPerTag()
    {
        // The incident config had exactly this shape: one dirty address next to
        // two clean ones. Only the dirty one should be called out.
        var cfg = BaseConfig(
            new EthernetIpTagDefinition { Name = "Tag1", Address = "Start_PB ", Datatype = "BOOL" },
            new EthernetIpTagDefinition { Name = "Tag2", Address = "Stop_PB", Datatype = "BOOL" },
            new EthernetIpTagDefinition { Name = "Tag3", Address = "Motor_Run", Datatype = "BOOL" });

        var result = await NewAdapter().ValidateConfigAsync(cfg, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().ContainSingle()
            .Which.Path.Should().Be("TagDefinitions[0].Address");
    }

    [Fact]
    public async Task MissingAddress_StillReportsMissingField_NotAWhitespaceIssue()
    {
        var cfg = BaseConfig(new EthernetIpTagDefinition
        {
            Name = "Tag1", Address = "   ", Datatype = "BOOL",
        });

        var result = await NewAdapter().ValidateConfigAsync(cfg, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.Code == EthernetIpErrors.ConfigMissingField && e.Path == "TagDefinitions[0].Address");
    }
}
