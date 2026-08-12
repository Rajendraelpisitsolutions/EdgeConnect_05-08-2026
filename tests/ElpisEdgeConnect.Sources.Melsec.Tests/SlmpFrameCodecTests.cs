// ============================================================================
// Tests: SlmpFrameCodec — 3E binary batch-read (0401) request builder + response
//        parser. Slice-1 read-only path only (word units over TCP).
//
// GOLDEN VECTORS: re-derived and verified against the PINNED official manuals
// (Phase A-1 parity audit, 2026-07-03). Citations use document number +
// revision + section/table; page numbers are the PDF's printed page and are
// secondary. Manuals remain FIELD-UNVERIFIED against real silicon until a
// hardware capture (Gate B); these citations pin the SPEC side.
//
//   [MC]   MELSEC Communication Protocol Reference Manual,
//          SH(NA)-080008-AB (May 2022, "AB(2205)KWIX").
//   [SLMP] SLMP Reference Manual, SH(NA)-080956ENG-N (Oct 2025, "N(2510)MEE").
//
//   Subheader 50 00 / D0 00 (fixed marker) ..... [MC] §5.3 "Subheader" (p42)
//   Request/response data length, 2-byte LE .... [MC] §5.3 (p43)
//   Monitoring timer, 250 ms units, LE, 0=inf .. [MC] §5.3 "Monitoring timer" (p43)
//   End code, 2-byte LE, 0 = normal ............ [MC] §5.3 "End code" (p44)
//   3E route order/defaults 00 FF FF03 00 ...... [MC] §6.1 "4E frame, 3E frame" (p48)
//   0401 word-units request layout + example ... [MC] §8.2 "Batch read in word
//          units" (p86-88); binary example 01 04 00 00 | head 3B LE | code | count
//          LE (p88); same form in [SLMP] §5.2 "Read" (p46-50)
//   Device codes & radix (Q/L subcmd 0000):
//          D=A8 dec, W=B4 hex, R=AF dec, ZR=B0 hex, M=90 dec, X=9C hex,
//          Y=9D hex, B=A0 hex ................. [MC] §8.1 "Device code list" (p68)
//   Head device number, 3-byte LE (Q/L form) ... [MC] §8.1 "Device number" (p67)
//   Word-units point cap 1..960 (iQ-R/L/Q/L) ... [MC] §8.2 (p87); [SLMP] §5.2 (p48)
//   End-code DESCRIPTIONS are per-module ....... [MC] §5.3 (p44) refers to the
//          target module's user manual; MelsecEndCode.Describe is advisory only.
// ============================================================================

using System;
using ElpisEdgeConnect.Sources.Melsec.Wire;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Melsec.Tests;

public class SlmpFrameCodecTests
{
    // ---- Request: full golden frame ---------------------------------------

    [Fact]
    public void BuildBatchReadWordRequest_D100_1point_produces_exact_golden_frame()
    {
        // D100, 1 word, local CPU, monitoring timer = 4 units (1000 ms).
        var frame = SlmpFrameCodec.BuildBatchReadWordRequest(
            Slmp3ERoute.LocalCpu, monitoringTimerUnits: 4, MelsecDeviceCode.D,
            headDeviceNumber: 100, points: 1);

        // 50 00 | 00 | FF | FF 03 | 00 | 0C 00 | 04 00 | 01 04 | 00 00 | 64 00 00 | A8 | 01 00
        frame.Should().Equal(
            0x50, 0x00,             // subheader (fixed marker, NOT little-endian)
            0x00,                   // network no
            0xFF,                   // pc no
            0xFF, 0x03,             // request dest module I/O no (0x03FF, LE)
            0x00,                   // request dest module station no
            0x0C, 0x00,             // request data length (12, LE)
            0x04, 0x00,             // monitoring timer (4 units, LE)
            0x01, 0x04,             // command 0x0401 (LE)
            0x00, 0x00,             // subcommand 0x0000 (word units, LE)
            0x64, 0x00, 0x00,       // head device 100 (3-byte LE)
            0xA8,                   // device code D
            0x01, 0x00);            // point count (1, LE)
    }

    // ---- Request: device code + head encoding per device ------------------

    // Address-string radix is applied UPSTREAM (step 3); here the head number is
    // already resolved. The comments show the operator address each value comes
    // from, to prove hex vs decimal handling — including the corrected ZR=hex.
    [Theory]
    [InlineData(MelsecDeviceCode.D, 100, 0xA8, 0x64, 0x00, 0x00)]   // "D100"  decimal -> 100
    [InlineData(MelsecDeviceCode.W, 0x1A, 0xB4, 0x1A, 0x00, 0x00)]  // "W1A"   HEX     -> 26
    [InlineData(MelsecDeviceCode.R, 200, 0xAF, 0xC8, 0x00, 0x00)]   // "R200"  decimal -> 200
    [InlineData(MelsecDeviceCode.ZR, 0x1F, 0xB0, 0x1F, 0x00, 0x00)] // "ZR1F"  HEX     -> 31 (corrected ZR radix)
    [InlineData(MelsecDeviceCode.M, 100, 0x90, 0x64, 0x00, 0x00)]   // "M100"  decimal -> 100
    [InlineData(MelsecDeviceCode.X, 0x20, 0x9C, 0x20, 0x00, 0x00)]  // "X20"   HEX     -> 32
    [InlineData(MelsecDeviceCode.Y, 0x30, 0x9D, 0x30, 0x00, 0x00)]  // "Y30"   HEX     -> 48
    [InlineData(MelsecDeviceCode.B, 0x07, 0xA0, 0x07, 0x00, 0x00)]  // "B7"    HEX     -> 7
    public void BuildBatchReadWordRequest_encodes_device_code_and_head(
        MelsecDeviceCode device, int head, int code, int h0, int h1, int h2)
    {
        var frame = SlmpFrameCodec.BuildBatchReadWordRequest(
            Slmp3ERoute.LocalCpu, monitoringTimerUnits: 0, device, head, points: 1);

        frame.Length.Should().Be(21);
        frame[18].Should().Be((byte)code, "device code byte must match SH-080008");
        frame[15].Should().Be((byte)h0);
        frame[16].Should().Be((byte)h1);
        frame[17].Should().Be((byte)h2);
    }

    [Fact]
    public void BuildBatchReadWordRequest_encodes_head_device_as_3_byte_little_endian()
    {
        // 70000 = 0x011170 -> LE bytes 70 11 01 across the 3-byte head field.
        var frame = SlmpFrameCodec.BuildBatchReadWordRequest(
            Slmp3ERoute.LocalCpu, monitoringTimerUnits: 0, MelsecDeviceCode.D,
            headDeviceNumber: 70000, points: 1);

        frame[15].Should().Be(0x70);
        frame[16].Should().Be(0x11);
        frame[17].Should().Be(0x01);
    }

    [Theory]
    [InlineData(5, 0x05, 0x00)]
    [InlineData(300, 0x2C, 0x01)]   // 300 = 0x012C -> LE 2C 01
    [InlineData(960, 0xC0, 0x03)]   // max points 960 = 0x03C0 -> LE C0 03
    public void BuildBatchReadWordRequest_encodes_point_count_little_endian(int points, int lo, int hi)
    {
        var frame = SlmpFrameCodec.BuildBatchReadWordRequest(
            Slmp3ERoute.LocalCpu, monitoringTimerUnits: 0, MelsecDeviceCode.D, 0, points);

        frame[19].Should().Be((byte)lo);
        frame[20].Should().Be((byte)hi);
    }

    [Fact]
    public void BuildBatchReadWordRequest_encodes_route_and_timer_fields_little_endian()
    {
        var route = new Slmp3ERoute(NetworkNo: 0x01, PcNo: 0x02,
            RequestDestModuleIoNo: 0x03FF, RequestDestModuleStationNo: 0x04);

        var frame = SlmpFrameCodec.BuildBatchReadWordRequest(
            route, monitoringTimerUnits: 0x012C, MelsecDeviceCode.D, 0, points: 1);

        frame[2].Should().Be(0x01);            // network
        frame[3].Should().Be(0x02);            // pc
        frame[4].Should().Be(0xFF);            // io lo
        frame[5].Should().Be(0x03);            // io hi
        frame[6].Should().Be(0x04);            // station
        frame[9].Should().Be(0x2C);            // monitoring timer lo (0x012C)
        frame[10].Should().Be(0x01);           // monitoring timer hi
    }

    [Theory]
    [InlineData(0)]
    [InlineData(961)]
    [InlineData(-1)]
    public void BuildBatchReadWordRequest_rejects_out_of_range_point_count(int points)
    {
        var act = () => SlmpFrameCodec.BuildBatchReadWordRequest(
            Slmp3ERoute.LocalCpu, 0, MelsecDeviceCode.D, 0, points);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0x1000000)] // > 0xFFFFFF
    public void BuildBatchReadWordRequest_rejects_out_of_range_head_device(int head)
    {
        var act = () => SlmpFrameCodec.BuildBatchReadWordRequest(
            Slmp3ERoute.LocalCpu, 0, MelsecDeviceCode.D, head, points: 1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---- Response: success + round-trip -----------------------------------

    [Fact]
    public void ParseBatchReadWordResponse_success_returns_word_payload()
    {
        // Two words: 0x1234, 0xABCD (LE on the wire).
        var response = BuildResponse(MelsecEndCode.Success, new byte[] { 0x34, 0x12, 0xCD, 0xAB });

        var result = SlmpFrameCodec.ParseBatchReadWordResponse(response, expectedPoints: 2);

        result.Status.Should().Be(MelsecReadStatus.Success);
        result.IsSuccess.Should().BeTrue();
        result.EndCode.Should().Be(0);
        result.WordData.ToArray().Should().Equal(0x34, 0x12, 0xCD, 0xAB);
    }

    [Fact]
    public void RoundTrip_build_request_then_parse_matching_response()
    {
        _ = SlmpFrameCodec.BuildBatchReadWordRequest(
            Slmp3ERoute.LocalCpu, 4, MelsecDeviceCode.D, 100, points: 1);

        var response = BuildResponse(MelsecEndCode.Success, new byte[] { 0x34, 0x12 });
        var result = SlmpFrameCodec.ParseBatchReadWordResponse(response, expectedPoints: 1);

        result.IsSuccess.Should().BeTrue();
        result.WordData.ToArray().Should().Equal(0x34, 0x12);
    }

    // ---- Response: non-zero end codes -> typed protocol errors ------------

    [Fact]
    public void ParseBatchReadWordResponse_nonzero_end_code_is_protocol_error_not_exception()
    {
        var response = BuildResponse(0xC059, Array.Empty<byte>()); // command/subcommand error

        var result = SlmpFrameCodec.ParseBatchReadWordResponse(response, expectedPoints: 1);

        result.Status.Should().Be(MelsecReadStatus.ProtocolError);
        result.EndCode.Should().Be(0xC059);
        result.Message.Should().Contain("0xC059");
    }

    [Fact]
    public void ParseBatchReadWordResponse_protocol_error_tolerates_trailing_error_info()
    {
        // Abnormal responses may append an error-information block after the end
        // code; the parser reports the end code and ignores the extra bytes.
        var response = BuildResponse(0xC056, new byte[] { 0x00, 0xFF, 0xFF, 0x03, 0x00, 0x01, 0x04, 0x00, 0x00 });

        var result = SlmpFrameCodec.ParseBatchReadWordResponse(response, expectedPoints: 2);

        result.Status.Should().Be(MelsecReadStatus.ProtocolError);
        result.EndCode.Should().Be(0xC056);
    }

    // ---- Response: malformed / truncated frames ---------------------------

    [Fact]
    public void ParseBatchReadWordResponse_too_short_is_malformed()
    {
        var result = SlmpFrameCodec.ParseBatchReadWordResponse(new byte[10], expectedPoints: 1);

        result.Status.Should().Be(MelsecReadStatus.MalformedResponse);
        result.Message.Should().Contain("too short");
    }

    [Fact]
    public void ParseBatchReadWordResponse_bad_subheader_is_malformed()
    {
        var response = BuildResponse(MelsecEndCode.Success, new byte[] { 0x00, 0x00 });
        response[0] = 0x50; // request subheader byte in a response position

        var result = SlmpFrameCodec.ParseBatchReadWordResponse(response, expectedPoints: 1);

        result.Status.Should().Be(MelsecReadStatus.MalformedResponse);
        result.Message.Should().Contain("subheader");
    }

    [Fact]
    public void ParseBatchReadWordResponse_truncated_payload_is_malformed()
    {
        // Declares 2 words (rlen 6) but two payload bytes are missing.
        var full = BuildResponse(MelsecEndCode.Success, new byte[] { 0x11, 0x22, 0x33, 0x44 });
        var truncated = full.AsSpan(0, full.Length - 2).ToArray();

        var result = SlmpFrameCodec.ParseBatchReadWordResponse(truncated, expectedPoints: 2);

        result.Status.Should().Be(MelsecReadStatus.MalformedResponse);
        result.Message.Should().Contain("truncated");
    }

    [Fact]
    public void ParseBatchReadWordResponse_payload_length_mismatch_is_malformed()
    {
        // Frame carries 1 word, but the caller expected 2.
        var response = BuildResponse(MelsecEndCode.Success, new byte[] { 0x34, 0x12 });

        var result = SlmpFrameCodec.ParseBatchReadWordResponse(response, expectedPoints: 2);

        result.Status.Should().Be(MelsecReadStatus.MalformedResponse);
        result.Message.Should().Contain("payload length mismatch");
    }

    [Fact]
    public void ParseBatchReadWordResponse_rejects_out_of_range_expected_points()
    {
        var response = BuildResponse(MelsecEndCode.Success, new byte[] { 0x00, 0x00 });

        var act = () => SlmpFrameCodec.ParseBatchReadWordResponse(response, expectedPoints: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>Build a well-formed 3E binary response with the given end code and
    /// post-end-code data bytes (payload on success, error-info on failure).</summary>
    private static byte[] BuildResponse(ushort endCode, byte[] afterEndCode)
    {
        var responseDataLength = (ushort)(2 + afterEndCode.Length);
        var f = new byte[9 + responseDataLength];
        f[0] = 0xD0; f[1] = 0x00;               // response subheader
        f[2] = 0x00;                            // network
        f[3] = 0xFF;                            // pc
        f[4] = 0xFF; f[5] = 0x03;               // io (0x03FF, LE)
        f[6] = 0x00;                            // station
        f[7] = (byte)(responseDataLength & 0xFF);
        f[8] = (byte)(responseDataLength >> 8);
        f[9] = (byte)(endCode & 0xFF);
        f[10] = (byte)(endCode >> 8);
        Array.Copy(afterEndCode, 0, f, 11, afterEndCode.Length);
        return f;
    }
}
