// ============================================================================
// File: Wire/MelsecReadResult.cs
// Purpose: Result of parsing a 3E binary batch-read (word) response. Protocol
//          and structural failures are returned as typed statuses — the codec
//          never throws for a non-zero end code or a malformed frame.
// ============================================================================

using System;

namespace ElpisEdgeConnect.Sources.Melsec.Wire;

/// <summary>Outcome category for a parsed batch-read response.</summary>
public enum MelsecReadStatus
{
    /// <summary>End code 0; payload present and length-checked.</summary>
    Success,

    /// <summary>Structurally valid frame carrying a non-zero MELSEC end code.</summary>
    ProtocolError,

    /// <summary>Frame failed structural validation (subheader / length / truncation).</summary>
    MalformedResponse,
}

/// <summary>
/// Result of parsing a 3E binary batch-read (word-units) response.
/// </summary>
public readonly record struct MelsecReadResult
{
    private MelsecReadResult(MelsecReadStatus status, ushort endCode, string? message, ReadOnlyMemory<byte> wordData)
    {
        Status = status;
        EndCode = endCode;
        Message = message;
        WordData = wordData;
    }

    /// <summary>Outcome category.</summary>
    public MelsecReadStatus Status { get; }

    /// <summary>MELSEC end code (0 on success; the protocol code on <see cref="MelsecReadStatus.ProtocolError"/>).</summary>
    public ushort EndCode { get; }

    /// <summary>Diagnostic message for a protocol/malformed failure; null on success.</summary>
    public string? Message { get; }

    /// <summary>Raw little-endian word payload (2 bytes per point) on success; empty otherwise.</summary>
    public ReadOnlyMemory<byte> WordData { get; }

    /// <summary>True when <see cref="Status"/> is <see cref="MelsecReadStatus.Success"/>.</summary>
    public bool IsSuccess => Status == MelsecReadStatus.Success;

    internal static MelsecReadResult Ok(ReadOnlyMemory<byte> wordData) =>
        new(MelsecReadStatus.Success, MelsecEndCode.Success, null, wordData);

    internal static MelsecReadResult Protocol(ushort endCode) =>
        new(MelsecReadStatus.ProtocolError, endCode,
            $"MELSEC end code 0x{endCode:X4}: {MelsecEndCode.Describe(endCode)}",
            ReadOnlyMemory<byte>.Empty);

    internal static MelsecReadResult Malformed(string reason) =>
        new(MelsecReadStatus.MalformedResponse, 0, reason, ReadOnlyMemory<byte>.Empty);
}
