// ============================================================================
// File: Wire/SlmpFrameCodec.cs
// Purpose: Pure codec for SLMP / MC Protocol 3E BINARY batch-read (command
//          0x0401, word-units subcommand 0x0000) request/response frames over
//          TCP. Slice-1 read-only path only (ADR-0033): no writes, no 4E/1E,
//          no ASCII, no UDP. Stateless and allocation-light; heavily tested
//          before any adapter/scan-planner integration.
//
//          3E binary layout (little-endian multi-byte fields; the subheader is
//          a fixed 2-byte marker, NOT an LE-encoded value):
//
//          Request  (21 bytes for a word batch read):
//            50 00 | net | pc | ioLo ioHi | station | lenLo lenHi
//            | timerLo timerHi | 01 04 | 00 00 | headLo headMid headHi | dev | cntLo cntHi
//            (request data length = 12: from monitoring timer through count)
//          Response (11 + 2*points on success):
//            D0 00 | net | pc | ioLo ioHi | station | rlenLo rlenHi
//            | ecLo ecHi | <payload...>
//            (response data length = 2 (end code) + payload bytes)
//
//          GOLDEN BYTES ARE SPEC-DERIVED (Mitsubishi SH(NA)-080008) and remain
//          FIELD-UNVERIFIED until the customer Part B capture confirms them.
// Reference: docs/decisions/0033-melsec-slmp-handrolled-slice1-scope.md
// ============================================================================

using System;

namespace ElpisEdgeConnect.Sources.Melsec.Wire;

/// <summary>
/// Stateless codec for SLMP / MC 3E binary batch-read (word-units) frames.
/// </summary>
public static class SlmpFrameCodec
{
    /// <summary>Batch-read command (0x0401).</summary>
    public const ushort CommandBatchRead = 0x0401;

    /// <summary>Word-units subcommand (0x0000).</summary>
    public const ushort SubcommandWordUnits = 0x0000;

    /// <summary>Maximum word points per 3E-binary batch read on modern (Q/L/iQ) CPUs.</summary>
    public const int MaxWordPoints = 960;

    /// <summary>Largest addressable head-device number (the 3-byte head-device field).</summary>
    public const int MaxHeadDeviceNumber = 0xFFFFFF;

    /// <summary>Fixed request subheader bytes (0x50, 0x00).</summary>
    private const byte RequestSubheader0 = 0x50;
    private const byte RequestSubheader1 = 0x00;

    /// <summary>Fixed response subheader bytes (0xD0, 0x00).</summary>
    private const byte ResponseSubheader0 = 0xD0;
    private const byte ResponseSubheader1 = 0x00;

    // Bytes from the subheader through the response-data-length field.
    private const int HeaderLength = 9;

    // Request data length for a word batch read: monitoring timer(2) + command(2)
    // + subcommand(2) + head device(3) + device code(1) + count(2) = 12.
    private const ushort BatchReadWordRequestDataLength = 12;

    /// <summary>
    /// Build a 3E binary batch-read (word units) request frame.
    /// </summary>
    /// <param name="route">Route header (network / PC / module / station).</param>
    /// <param name="monitoringTimerUnits">Monitoring timer in 250 ms units (0 = wait indefinitely).</param>
    /// <param name="device">Device to read.</param>
    /// <param name="headDeviceNumber">Head device number (already radix-resolved to an integer).</param>
    /// <param name="points">Word points to read (1..<see cref="MaxWordPoints"/>).</param>
    /// <exception cref="ArgumentOutOfRangeException">If head device or point count is out of range.</exception>
    public static byte[] BuildBatchReadWordRequest(
        Slmp3ERoute route,
        ushort monitoringTimerUnits,
        MelsecDeviceCode device,
        int headDeviceNumber,
        int points)
    {
        if (headDeviceNumber is < 0 or > MaxHeadDeviceNumber)
        {
            throw new ArgumentOutOfRangeException(nameof(headDeviceNumber),
                $"Head device number must be 0..{MaxHeadDeviceNumber} (got {headDeviceNumber}).");
        }
        if (points is < 1 or > MaxWordPoints)
        {
            throw new ArgumentOutOfRangeException(nameof(points),
                $"Word point count must be 1..{MaxWordPoints} (got {points}).");
        }

        var f = new byte[HeaderLength + BatchReadWordRequestDataLength]; // 9 + 12 = 21

        f[0] = RequestSubheader0;
        f[1] = RequestSubheader1;
        f[2] = route.NetworkNo;
        f[3] = route.PcNo;
        f[4] = (byte)(route.RequestDestModuleIoNo & 0xFF);
        f[5] = (byte)(route.RequestDestModuleIoNo >> 8);
        f[6] = route.RequestDestModuleStationNo;
        f[7] = (byte)(BatchReadWordRequestDataLength & 0xFF);
        f[8] = (byte)(BatchReadWordRequestDataLength >> 8);
        f[9] = (byte)(monitoringTimerUnits & 0xFF);
        f[10] = (byte)(monitoringTimerUnits >> 8);
        f[11] = (byte)(CommandBatchRead & 0xFF);
        f[12] = (byte)(CommandBatchRead >> 8);
        f[13] = (byte)(SubcommandWordUnits & 0xFF);
        f[14] = (byte)(SubcommandWordUnits >> 8);
        f[15] = (byte)(headDeviceNumber & 0xFF);
        f[16] = (byte)((headDeviceNumber >> 8) & 0xFF);
        f[17] = (byte)((headDeviceNumber >> 16) & 0xFF);
        f[18] = (byte)device;
        f[19] = (byte)(points & 0xFF);
        f[20] = (byte)(points >> 8);

        return f;
    }

    /// <summary>
    /// Parse a 3E binary batch-read (word units) response. Never throws for a
    /// protocol-level failure — a non-zero end code yields
    /// <see cref="MelsecReadStatus.ProtocolError"/> and a structurally invalid
    /// frame yields <see cref="MelsecReadStatus.MalformedResponse"/>.
    /// </summary>
    /// <param name="response">The raw response bytes.</param>
    /// <param name="expectedPoints">Word points the matching request asked for (1..<see cref="MaxWordPoints"/>).</param>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="expectedPoints"/> is out of range.</exception>
    public static MelsecReadResult ParseBatchReadWordResponse(ReadOnlySpan<byte> response, int expectedPoints)
    {
        if (expectedPoints is < 1 or > MaxWordPoints)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedPoints),
                $"Expected point count must be 1..{MaxWordPoints} (got {expectedPoints}).");
        }

        const int minLength = HeaderLength + 2; // + end code
        if (response.Length < minLength)
        {
            return MelsecReadResult.Malformed(
                $"response too short: {response.Length} bytes (need >= {minLength}).");
        }

        if (response[0] != ResponseSubheader0 || response[1] != ResponseSubheader1)
        {
            return MelsecReadResult.Malformed(
                $"bad response subheader 0x{response[0]:X2}{response[1]:X2} (expected 0xD000).");
        }

        int responseDataLength = response[7] | (response[8] << 8);
        if (responseDataLength < 2)
        {
            return MelsecReadResult.Malformed(
                $"response data length {responseDataLength} too small to contain an end code.");
        }
        if (response.Length < HeaderLength + responseDataLength)
        {
            return MelsecReadResult.Malformed(
                $"truncated frame: declared {responseDataLength} data bytes, only {response.Length - HeaderLength} present.");
        }

        var endCode = (ushort)(response[9] | (response[10] << 8));
        if (endCode != MelsecEndCode.Success)
        {
            return MelsecReadResult.Protocol(endCode);
        }

        int payloadBytes = responseDataLength - 2; // minus the end code
        int expectedBytes = expectedPoints * 2;
        if (payloadBytes != expectedBytes)
        {
            return MelsecReadResult.Malformed(
                $"payload length mismatch: expected {expectedBytes} bytes for {expectedPoints} points, got {payloadBytes}.");
        }

        var payload = response.Slice(HeaderLength + 2, payloadBytes).ToArray();
        return MelsecReadResult.Ok(payload);
    }
}
