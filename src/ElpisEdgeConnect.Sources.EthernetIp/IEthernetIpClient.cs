// ============================================================================
// File: IEthernetIpClient.cs
// Purpose: Thin abstraction over the underlying EtherNet/IP / CIP client
//          library (libplctag). The adapter's connection manager and poll loop
//          talk only to this interface; the libplctag binding is hidden in
//          LibPlcTagClient. Enables:
//            1. Unit tests using EthernetIpDemoClient (no PLC, no native calls).
//            2. Swapping the underlying library without touching adapter logic.
//
//          libplctag manages CIP sessions lazily per gateway+path and merges
//          queued reads into Forward-Open packets itself, so this seam stays
//          deliberately small: open a session, read one named tag, disconnect.
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §3.1
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Sources.EthernetIp;

/// <summary>
/// Transport-neutral EtherNet/IP client used by the adapter. Implementations
/// wrap a concrete CIP library (production: <c>LibPlcTagClient</c>; tests /
/// demo: <c>EthernetIpDemoClient</c>).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ConnectAsync"/> throws <see cref="EthernetIpFatalException"/> for
/// session-fatal failures (gateway unreachable, socket error). Per-tag read
/// problems (tag not found, type mismatch) are returned as non-success
/// <see cref="EthernetIpReadResult"/> values — never thrown — so one bad tag
/// does not tear down the session for the others.
/// </para>
/// <para>
/// Implementations are serialized by the connection manager's wire lock; they
/// need not be internally thread-safe.
/// </para>
/// </remarks>
public interface IEthernetIpClient : IAsyncDisposable
{
    /// <summary>True if the client currently holds a usable CIP session.</summary>
    bool IsConnected { get; }

    /// <summary>
    /// Establish (or validate) a CIP session to the controller described by
    /// <paramref name="parameters"/>. Throws <see cref="EthernetIpFatalException"/>
    /// on a session-fatal failure.
    /// </summary>
    Task ConnectAsync(EthernetIpConnectionParameters parameters, CancellationToken ct);

    /// <summary>Close the CIP session if open. Idempotent.</summary>
    void Disconnect();

    /// <summary>
    /// Read a single controller tag by symbolic name, decoding it as
    /// <paramref name="elementType"/>. Returns a success or failure result;
    /// throws <see cref="EthernetIpFatalException"/> only for session-fatal
    /// transport errors.
    /// </summary>
    Task<EthernetIpReadResult> ReadTagAsync(string address, EthernetIpElementType elementType, CancellationToken ct);
}

/// <summary>Immutable parameters describing a CIP session target.</summary>
public sealed record EthernetIpConnectionParameters
{
    /// <summary>Gateway IP / hostname of the controller.</summary>
    public required string Host { get; init; }

    /// <summary>CIP routing path to the CPU (may be empty for embedded-port families).</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Rockwell CPU family — selects the libplctag <c>plc</c> token.</summary>
    public required EthernetIpCpuFamily CpuFamily { get; init; }

    /// <summary>Session connect timeout.</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromMilliseconds(2000);

    /// <summary>Per-read response timeout.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMilliseconds(1000);
}

/// <summary>Outcome of a single tag read.</summary>
public sealed record EthernetIpReadResult
{
    /// <summary>True when the read completed and decoded successfully.</summary>
    public required bool Success { get; init; }

    /// <summary>The decoded value when <see cref="Success"/> is true.</summary>
    public object? Value { get; init; }

    /// <summary>The canonical value type of <see cref="Value"/> when successful.</summary>
    public CanonicalValueType ValueType { get; init; } = CanonicalValueType.Null;

    /// <summary>Stable error code (<see cref="EthernetIpErrors"/>) when not successful.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Human-readable detail when not successful.</summary>
    public string? ErrorText { get; init; }

    /// <summary>Build a success result.</summary>
    public static EthernetIpReadResult Ok(object value, CanonicalValueType valueType) =>
        new() { Success = true, Value = value, ValueType = valueType };

    /// <summary>Build a non-fatal failure result.</summary>
    public static EthernetIpReadResult Fail(string errorCode, string errorText) =>
        new() { Success = false, ErrorCode = errorCode, ErrorText = errorText };
}
