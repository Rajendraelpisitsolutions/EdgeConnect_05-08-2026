// ============================================================================
// File: IMelsecClient.cs
// Purpose: Thin transport abstraction over the SLMP wire, so the adapter and
//          connection manager are testable against a fake. The real TCP client
//          (SlmpClient, built on SlmpFrameCodec) arrives in step 6; step 5 runs
//          entirely against a fake IMelsecClient — no real network.
//
//          ReadWordsAsync returns a typed result distinguishing transport
//          failure (drop + reconnect), a non-zero MELSEC end code (protocol),
//          a malformed frame (drop, desync), and success (word payload).
// Reference: docs/decisions/0033-melsec-slmp-handrolled-slice1-scope.md
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Sources.Melsec.Wire;

namespace ElpisEdgeConnect.Sources.Melsec;

/// <summary>Outcome category of a transport-level MELSEC operation.</summary>
public enum MelsecClientStatus
{
    /// <summary>Connected, or read succeeded with a word payload.</summary>
    Success,

    /// <summary>Socket/connection failure or timeout — the connection is dropped and reconnected.</summary>
    TransportError,

    /// <summary>Structurally valid response carrying a non-zero MELSEC end code.</summary>
    ProtocolError,

    /// <summary>Structurally invalid response — dropped to avoid frame desync (3E has no serial).</summary>
    MalformedResponse,
}

/// <summary>Result of an <see cref="IMelsecClient"/> operation.</summary>
public readonly record struct MelsecClientResult
{
    private MelsecClientResult(MelsecClientStatus status, ushort endCode, string? message, ReadOnlyMemory<byte> wordData)
    {
        Status = status;
        EndCode = endCode;
        Message = message;
        WordData = wordData;
    }

    /// <summary>Outcome category.</summary>
    public MelsecClientStatus Status { get; }

    /// <summary>MELSEC end code (0 unless <see cref="MelsecClientStatus.ProtocolError"/>).</summary>
    public ushort EndCode { get; }

    /// <summary>Diagnostic message for a failure; null on success.</summary>
    public string? Message { get; }

    /// <summary>Little-endian word payload (2 bytes per point) on a successful read.</summary>
    public ReadOnlyMemory<byte> WordData { get; }

    /// <summary>True when <see cref="Status"/> is <see cref="MelsecClientStatus.Success"/>.</summary>
    public bool IsSuccess => Status == MelsecClientStatus.Success;

    /// <summary>A connect success (no payload).</summary>
    public static MelsecClientResult Connected { get; } =
        new(MelsecClientStatus.Success, 0, null, ReadOnlyMemory<byte>.Empty);

    /// <summary>A successful read carrying the word payload.</summary>
    public static MelsecClientResult Ok(ReadOnlyMemory<byte> wordData) =>
        new(MelsecClientStatus.Success, 0, null, wordData);

    /// <summary>A transport/socket failure.</summary>
    public static MelsecClientResult Transport(string message) =>
        new(MelsecClientStatus.TransportError, 0, message, ReadOnlyMemory<byte>.Empty);

    /// <summary>A non-zero MELSEC end code.</summary>
    public static MelsecClientResult Protocol(ushort endCode) =>
        new(MelsecClientStatus.ProtocolError, endCode,
            $"MELSEC end code 0x{endCode:X4}: {MelsecEndCode.Describe(endCode)}", ReadOnlyMemory<byte>.Empty);

    /// <summary>A malformed response frame.</summary>
    public static MelsecClientResult Malformed(string message) =>
        new(MelsecClientStatus.MalformedResponse, 0, message, ReadOnlyMemory<byte>.Empty);
}

/// <summary>
/// Transport abstraction over the SLMP wire. Single-threaded per instance — the
/// connection manager serializes calls (one in-flight read at a time).
/// </summary>
public interface IMelsecClient : IDisposable
{
    /// <summary>True when the underlying connection is currently established.</summary>
    bool IsConnected { get; }

    /// <summary>Establish the TCP session to <paramref name="host"/>:<paramref name="port"/>.</summary>
    Task<MelsecClientResult> ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken ct);

    /// <summary>Drop the session if connected. Lock-free (safe to call to interrupt a wedged read).</summary>
    void Disconnect();

    /// <summary>
    /// Batch-read <paramref name="points"/> words starting at
    /// (<paramref name="device"/>, <paramref name="headDeviceNumber"/>).
    /// </summary>
    Task<MelsecClientResult> ReadWordsAsync(MelsecDeviceCode device, int headDeviceNumber, int points, CancellationToken ct);
}
