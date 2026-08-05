// ============================================================================
// File: IS7Client.cs
// Purpose: Thin transport abstraction over Sharp7's S7Client. Keeps the
//          adapter testable against a fake by isolating the wire surface.
//
//          Designed around the read pattern the planner emits: one
//          ReadAreaAsync per (area, dbNumber, startByte, byteCount).
//          The implementation either delegates to Sharp7.S7Client or
//          returns canned bytes (test double).
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone I
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ElpisEdgeConnect.Sources.S7;

/// <summary>
/// Transport abstraction over the underlying S7 client library.
/// Single-threaded per instance — the adapter serializes calls.
/// </summary>
public interface IS7Client : IDisposable
{
    /// <summary>True when the underlying connection is currently established.</summary>
    bool IsConnected { get; }

    /// <summary>
    /// Establish the ISO-on-TCP session against
    /// <paramref name="host"/>:<paramref name="port"/> for
    /// CPU at (<paramref name="rack"/>, <paramref name="slot"/>).
    /// Returns a success / failure result via
    /// <see cref="S7OperationResult"/>.
    /// </summary>
    Task<S7OperationResult> ConnectAsync(
        string host,
        int port,
        int rack,
        int slot,
        S7ConnectionType connectionType,
        TimeSpan timeout,
        CancellationToken ct);

    /// <summary>Drop the session if currently connected.</summary>
    void Disconnect();

    /// <summary>
    /// Read <paramref name="byteCount"/> bytes starting at
    /// <paramref name="startByte"/> in
    /// (<paramref name="area"/>, <paramref name="dbNumber"/>). For
    /// areas other than DataBlock, <paramref name="dbNumber"/> must be 0.
    /// </summary>
    /// <returns>
    /// The populated portion of <paramref name="buffer"/> on success.
    /// On failure, <see cref="S7OperationResult.Success"/> is false and
    /// <see cref="S7OperationResult.ErrorCode"/> carries the library code.
    /// </returns>
    Task<S7OperationResult> ReadAreaAsync(
        S7MemoryArea area,
        int dbNumber,
        int startByte,
        int byteCount,
        byte[] buffer,
        CancellationToken ct);
}

/// <summary>
/// Result of a Sharp7 operation. <see cref="Success"/> is true when the
/// underlying library returned 0; otherwise <see cref="ErrorCode"/>
/// carries the Sharp7 numeric code and <see cref="ErrorText"/> the
/// human-readable explanation.
/// </summary>
public readonly record struct S7OperationResult(bool Success, int ErrorCode, string ErrorText)
{
    /// <summary>Convenience: build a successful result.</summary>
    public static S7OperationResult Ok { get; } = new(true, 0, string.Empty);

    /// <summary>Build a failure result with the given Sharp7 error info.</summary>
    public static S7OperationResult Fail(int code, string text) => new(false, code, text);
}
