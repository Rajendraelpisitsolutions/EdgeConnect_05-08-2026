// ============================================================================
// File: EthernetIpFatalException.cs
// Purpose: Exception signalling a connection-fatal EtherNet/IP error. The
//          connection manager drops the CIP session and re-establishes it
//          before the next transaction — a retry on the dead session would
//          fail identically.
// Reference: ARCHITECTURE_BLUEPRINT.md §13.3
// ============================================================================

using System;

namespace ElpisEdgeConnect.Sources.EthernetIp;

/// <summary>
/// Thrown by <see cref="IEthernetIpClient"/> implementations when an operation
/// fails in a way that invalidates the CIP session (gateway unreachable,
/// connection lost, socket I/O error, timeout exceeding the retry budget).
/// </summary>
/// <remarks>
/// Per-tag CIP errors (tag not found, type mismatch) are <b>not</b> fatal —
/// they indicate a request problem while the session stays healthy. Those
/// surface as <see cref="EthernetIpReadResult"/> failures, not exceptions, so
/// one bad tag never tears down the session for the others.
/// </remarks>
public sealed class EthernetIpFatalException : Exception
{
    /// <summary>Stable error code from <see cref="EthernetIpErrors"/>.</summary>
    public string ErrorCode { get; }

    /// <summary>Create a fatal exception with the given code and message.</summary>
    public EthernetIpFatalException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>Create a fatal exception wrapping an inner exception.</summary>
    public EthernetIpFatalException(string errorCode, string message, Exception inner)
        : base(message, inner)
    {
        ErrorCode = errorCode;
    }
}
