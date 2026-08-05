// ============================================================================
// File: LibPlcTagClient.cs
// Purpose: Production IEthernetIpClient backed by libplctag.NET (MPL-2.0). Wraps
//          the native libplctag CIP stack. One Tag object is created and cached
//          per controller address and reused across polls (the library's
//          recommended pattern — Tag objects own the session multiplexing).
//
//          Connection model: libplctag establishes and self-heals CIP sessions
//          lazily on first read per gateway+path. ConnectAsync therefore records
//          the target and marks the client live; genuine transport failures
//          surface on ReadTagAsync as EthernetIpFatalException, which the
//          connection manager turns into backoff + circuit-breaker behaviour
//          (mirrors how the Modbus executor reports fatal transport errors).
//
//          MVP scope: atomic reads (BOOL/SINT/INT/DINT/LINT/REAL/LREAL) + AB
//          STRING. UDT / array decoding lands with the UDT walker later.
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §3
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Model;
using libplctag;

namespace ElpisEdgeConnect.Sources.EthernetIp;

/// <summary>
/// libplctag-backed <see cref="IEthernetIpClient"/>. Not thread-safe; the
/// connection manager serializes access behind its wire lock.
/// </summary>
public sealed class LibPlcTagClient : IEthernetIpClient
{
    private readonly Dictionary<string, Tag> _tags = new(StringComparer.Ordinal);
    private EthernetIpConnectionParameters? _parameters;
    private bool _connected;
    private bool _disposed;

    /// <inheritdoc/>
    public bool IsConnected => _connected;

    /// <inheritdoc/>
    public Task ConnectAsync(EthernetIpConnectionParameters parameters, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(parameters);
        if (string.IsNullOrWhiteSpace(parameters.Host))
        {
            throw new EthernetIpFatalException(
                EthernetIpErrors.ConfigInvalid, "EtherNet/IP host is required.");
        }

        // libplctag connects lazily on first read; we record the target and
        // mark live. A genuine unreachable gateway surfaces on the first
        // ReadTagAsync as a fatal error and trips the breaker.
        _parameters = parameters;
        _connected = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Disconnect()
    {
        _connected = false;
        foreach (var tag in _tags.Values)
        {
            try
            {
                tag.Dispose();
            }
            catch (Exception)
            {
                // Best-effort teardown — a dispose failure must not mask the
                // real reason we are disconnecting.
            }
        }
        _tags.Clear();
    }

    /// <inheritdoc/>
    public async Task<EthernetIpReadResult> ReadTagAsync(
        string address, EthernetIpElementType elementType, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_parameters is null || !_connected)
        {
            throw new EthernetIpFatalException(
                EthernetIpErrors.ConnectionLost, "EtherNet/IP client is not connected.");
        }

        var tag = GetOrCreateTag(address, _parameters);

        try
        {
            await tag.ReadAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (LibPlcTagException ex)
        {
            // libplctag.NET sets the exception Message to the Status enum name
            // (e.g. "ErrorNotFound", "ErrorTimeout"). Classify off that:
            // tag-level CIP errors are non-fatal (one bad tag); transport
            // errors are fatal and tear the session down.
            var status = ParseStatus(ex.Message);
            if (IsTagLevelError(status))
            {
                return EthernetIpReadResult.Fail(ClassifyTagError(status), $"Read of '{address}' failed: {ex.Message}");
            }
            _connected = false;
            throw new EthernetIpFatalException(
                EthernetIpErrors.ReadFailed, $"Fatal CIP error reading '{address}': {ex.Message}", ex);
        }

        try
        {
            return DecodeTag(tag, elementType);
        }
        catch (Exception ex)
        {
            return EthernetIpReadResult.Fail(
                EthernetIpErrors.DecodeFailed,
                $"Read '{address}' but could not decode it as {elementType}: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }
        _disposed = true;
        Disconnect();
        return ValueTask.CompletedTask;
    }

    // =========================================================================
    // PRIVATE
    // =========================================================================

    private Tag GetOrCreateTag(string address, EthernetIpConnectionParameters parameters)
    {
        if (_tags.TryGetValue(address, out var existing))
        {
            return existing;
        }

        var tag = new Tag
        {
            Name = address,
            Gateway = parameters.Host,
            Path = string.IsNullOrWhiteSpace(parameters.Path) ? null : parameters.Path,
            PlcType = MapPlcType(parameters.CpuFamily),
            Protocol = Protocol.ab_eip,
            Timeout = parameters.RequestTimeout,
        };
        _tags[address] = tag;
        return tag;
    }

    private static EthernetIpReadResult DecodeTag(Tag tag, EthernetIpElementType elementType) => elementType switch
    {
        EthernetIpElementType.Bool => EthernetIpReadResult.Ok(tag.GetBit(0), CanonicalValueType.Boolean),
        EthernetIpElementType.Sint => EthernetIpReadResult.Ok((int)tag.GetInt8(0), CanonicalValueType.Integer),
        EthernetIpElementType.Int => EthernetIpReadResult.Ok((int)tag.GetInt16(0), CanonicalValueType.Integer),
        EthernetIpElementType.Dint => EthernetIpReadResult.Ok(tag.GetInt32(0), CanonicalValueType.Integer),
        EthernetIpElementType.Lint => EthernetIpReadResult.Ok(tag.GetInt64(0), CanonicalValueType.Long),
        EthernetIpElementType.Real => EthernetIpReadResult.Ok(tag.GetFloat32(0), CanonicalValueType.Float),
        EthernetIpElementType.Lreal => EthernetIpReadResult.Ok(tag.GetFloat64(0), CanonicalValueType.Double),
        EthernetIpElementType.String => EthernetIpReadResult.Ok(tag.GetString(0) ?? string.Empty, CanonicalValueType.String),
        _ => throw new ArgumentOutOfRangeException(nameof(elementType), elementType, "Unknown element type."),
    };

    private static PlcType MapPlcType(EthernetIpCpuFamily family) => family switch
    {
        EthernetIpCpuFamily.ControlLogix => PlcType.ControlLogix,
        EthernetIpCpuFamily.CompactLogix => PlcType.ControlLogix,
        EthernetIpCpuFamily.GuardLogix => PlcType.ControlLogix,
        EthernetIpCpuFamily.MicroLogix => PlcType.MicroLogix,
        EthernetIpCpuFamily.Micro800 => PlcType.Micro800,
        EthernetIpCpuFamily.Slc500 => PlcType.Slc500,
        EthernetIpCpuFamily.Plc5 => PlcType.Plc5,
        _ => PlcType.ControlLogix,
    };

    private static Status ParseStatus(string? message) =>
        Enum.TryParse<Status>(message, ignoreCase: false, out var status) ? status : Status.ErrorBadStatus;

    /// <summary>
    /// True when the CIP error is scoped to one tag (the controller answered,
    /// but the request was bad) rather than to the whole transport.
    /// </summary>
    private static bool IsTagLevelError(Status status) =>
        status is Status.ErrorNotFound or Status.ErrorBadParam or Status.ErrorUnsupported
            or Status.ErrorOutOfBounds or Status.ErrorNoData or Status.ErrorTooSmall;

    private static string ClassifyTagError(Status status) => status switch
    {
        Status.ErrorNotFound => EthernetIpErrors.TagNotFound,
        Status.ErrorBadParam or Status.ErrorUnsupported => EthernetIpErrors.TypeMismatch,
        _ => EthernetIpErrors.ReadFailed,
    };
}
