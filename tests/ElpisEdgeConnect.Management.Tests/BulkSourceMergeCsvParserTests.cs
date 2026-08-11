// ============================================================================
// File: BulkSourceMergeCsvParserTests.cs
// Purpose: Coverage for the minimal CSV reader powering BulkSourceMergeService.
//          Includes header-shape (v3.1 §8), size + row caps, RFC-4180 edge
//          cases the wizard's documented input shape can hit (quoted commas,
//          escaped quotes, BOM, CRLF/LF), and per-row structural errors.
// ============================================================================

using System.Linq;
using System.Text;
using ElpisEdgeConnect.Management.Api.BulkSourceMerge;
using ElpisEdgeConnect.Management.Contracts.BulkSourceMerge;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class BulkSourceMergeCsvParserTests
{
    private static readonly string[] HostRequired = new[] { "deviceId", "deviceName", "host", "enabled" };

    [Fact]
    public void Parse_HappyPath_ReturnsRowsWithLineNumbersAndCells()
    {
        var csv = """
deviceId,deviceName,host,enabled
cnc-001,Lathe-A,192.168.10.21,true
cnc-002,Lathe-B,192.168.10.22,false
""";
        var result = BulkSourceMergeCsvParser.Parse(Encoding.UTF8.GetBytes(csv), HostRequired);

        result.Findings.Should().BeEmpty();
        result.Rows.Should().HaveCount(2);
        result.Rows[0].LineNumber.Should().Be(2);
        result.Rows[0].Cells["deviceId"].Should().Be("cnc-001");
        result.Rows[0].Cells["deviceName"].Should().Be("Lathe-A");
        result.Rows[0].Cells["host"].Should().Be("192.168.10.21");
        result.Rows[0].Cells["enabled"].Should().Be("true");
        result.Rows[1].LineNumber.Should().Be(3);
        result.Rows[1].Cells["enabled"].Should().Be("false");
    }

    [Fact]
    public void Parse_HeaderMissingRequiredColumn_ReturnsCsvHeaderShapeInvalid()
    {
        var csv = "deviceId,deviceName,host\ncnc-001,Lathe-A,192.168.10.21\n";

        var result = BulkSourceMergeCsvParser.Parse(Encoding.UTF8.GetBytes(csv), HostRequired);

        result.Rows.Should().BeEmpty();
        result.Findings.Should().ContainSingle()
            .Which.Code.Should().Be(BulkSourceMergeErrorCode.CsvHeaderShapeInvalid);
        result.Findings[0].Message.Should().Contain("missing").And.Contain("enabled");
    }

    [Fact]
    public void Parse_HeaderHasExtraColumn_ReturnsCsvHeaderShapeInvalid()
    {
        var csv = "deviceId,deviceName,host,enabled,extra\ncnc-001,Lathe-A,192.168.10.21,true,oops\n";

        var result = BulkSourceMergeCsvParser.Parse(Encoding.UTF8.GetBytes(csv), HostRequired);

        result.Rows.Should().BeEmpty();
        result.Findings.Should().ContainSingle()
            .Which.Code.Should().Be(BulkSourceMergeErrorCode.CsvHeaderShapeInvalid);
        result.Findings[0].Message.Should().Contain("unexpected").And.Contain("extra");
    }

    [Fact]
    public void Parse_HeaderHasDuplicateColumn_ReturnsCsvHeaderShapeInvalid()
    {
        var csv = "deviceId,deviceName,host,host\ncnc-001,Lathe-A,a,b\n";

        var result = BulkSourceMergeCsvParser.Parse(Encoding.UTF8.GetBytes(csv), HostRequired);

        result.Rows.Should().BeEmpty();
        result.Findings.Should().ContainSingle()
            .Which.Code.Should().Be(BulkSourceMergeErrorCode.CsvHeaderShapeInvalid);
        result.Findings[0].Message.Should().Contain("duplicate");
    }

    [Fact]
    public void Parse_HeaderOrderDiffers_StillAcceptsBecauseOrderIsIgnored()
    {
        var csv = "enabled,host,deviceName,deviceId\ntrue,192.168.10.21,Lathe-A,cnc-001\n";

        var result = BulkSourceMergeCsvParser.Parse(Encoding.UTF8.GetBytes(csv), HostRequired);

        result.Findings.Should().BeEmpty();
        result.Rows.Should().ContainSingle();
        result.Rows[0].Cells["deviceId"].Should().Be("cnc-001");
        result.Rows[0].Cells["enabled"].Should().Be("true");
    }

    [Fact]
    public void Parse_EmptyBody_ReturnsCsvParseFailed()
    {
        var result = BulkSourceMergeCsvParser.Parse(System.Array.Empty<byte>(), HostRequired);

        result.Rows.Should().BeEmpty();
        result.Findings.Should().ContainSingle()
            .Which.Code.Should().Be(BulkSourceMergeErrorCode.CsvParseFailed);
    }

    [Fact]
    public void Parse_BodyExceedsByteCap_ReturnsCsvParseFailed()
    {
        var oversized = new byte[BulkSourceMergeCsvParser.MaxBytes + 1];

        var result = BulkSourceMergeCsvParser.Parse(oversized, HostRequired);

        result.Findings.Should().ContainSingle()
            .Which.Code.Should().Be(BulkSourceMergeErrorCode.CsvParseFailed);
        result.Findings[0].Message.Should().Contain("byte").And.Contain("limit");
    }

    [Fact]
    public void Parse_BodyExceedsRowCap_ReturnsCsvTooManyRows()
    {
        var sb = new StringBuilder();
        sb.AppendLine("deviceId,deviceName,host,enabled");
        for (var i = 1; i <= BulkSourceMergeCsvParser.MaxDataRows + 1; i++)
        {
            sb.Append("cnc-").Append(i).AppendLine(",Lathe,192.168.10.21,true");
        }

        var result = BulkSourceMergeCsvParser.Parse(Encoding.UTF8.GetBytes(sb.ToString()), HostRequired);

        result.Findings.Should().ContainSingle()
            .Which.Code.Should().Be(BulkSourceMergeErrorCode.CsvTooManyRows);
    }

    [Fact]
    public void Parse_QuotedFieldWithEmbeddedComma_Preserved()
    {
        var csv = """
deviceId,deviceName,host,enabled
cnc-001,"Lathe, Bay 7",192.168.10.21,true
""";
        var result = BulkSourceMergeCsvParser.Parse(Encoding.UTF8.GetBytes(csv), HostRequired);

        result.Findings.Should().BeEmpty();
        result.Rows[0].Cells["deviceName"].Should().Be("Lathe, Bay 7");
    }

    [Fact]
    public void Parse_QuotedFieldWithEscapedQuote_Preserved()
    {
        var csv = "deviceId,deviceName,host,enabled\ncnc-001,\"Mill \"\"A\"\"\",192.168.10.21,true\n";

        var result = BulkSourceMergeCsvParser.Parse(Encoding.UTF8.GetBytes(csv), HostRequired);

        result.Findings.Should().BeEmpty();
        result.Rows[0].Cells["deviceName"].Should().Be("Mill \"A\"");
    }

    [Fact]
    public void Parse_RowWithWrongCellCount_EmitsPerRowFinding()
    {
        var csv = "deviceId,deviceName,host,enabled\ncnc-001,Lathe-A,192.168.10.21\ncnc-002,Lathe-B,192.168.10.22,true\n";

        var result = BulkSourceMergeCsvParser.Parse(Encoding.UTF8.GetBytes(csv), HostRequired);

        result.Rows.Should().ContainSingle().Which.Cells["deviceId"].Should().Be("cnc-002");
        result.Findings.Should().ContainSingle().Which.Code.Should().Be(BulkSourceMergeErrorCode.CsvParseFailed);
        result.Findings[0].CsvRow.Should().Be(2);
    }

    [Fact]
    public void Parse_Utf8Bom_Tolerated()
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var body = Encoding.UTF8.GetBytes("deviceId,deviceName,host,enabled\ncnc-001,Lathe-A,192.168.10.21,true\n");
        var combined = bom.Concat(body).ToArray();

        var result = BulkSourceMergeCsvParser.Parse(combined, HostRequired);

        result.Findings.Should().BeEmpty();
        result.Rows.Should().ContainSingle()
            .Which.Cells["deviceId"].Should().Be("cnc-001");
    }

    [Fact]
    public void Parse_CrlfLineEndings_HandledIdenticallyToLf()
    {
        var csv = "deviceId,deviceName,host,enabled\r\ncnc-001,Lathe-A,192.168.10.21,true\r\ncnc-002,Lathe-B,192.168.10.22,false\r\n";

        var result = BulkSourceMergeCsvParser.Parse(Encoding.UTF8.GetBytes(csv), HostRequired);

        result.Findings.Should().BeEmpty();
        result.Rows.Should().HaveCount(2);
        result.Rows[0].LineNumber.Should().Be(2);
        result.Rows[1].LineNumber.Should().Be(3);
    }

    [Fact]
    public void Parse_InvalidUtf8Bytes_ReturnsCsvParseFailed()
    {
        // 0xFF is never a valid UTF-8 lead byte.
        var bad = new byte[] { (byte)'d', (byte)'e', 0xFF, (byte)'\n' };

        var result = BulkSourceMergeCsvParser.Parse(bad, HostRequired);

        result.Findings.Should().ContainSingle()
            .Which.Code.Should().Be(BulkSourceMergeErrorCode.CsvParseFailed);
    }
}
