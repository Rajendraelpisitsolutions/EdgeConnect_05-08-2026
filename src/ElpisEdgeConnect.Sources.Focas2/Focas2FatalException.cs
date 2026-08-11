// ============================================================================
// File: Focas2FatalException.cs
// Purpose: Exception thrown when FOCAS2 returns a fatal error code (EW_SOCKET
//          or EW_HANDLE) indicating the connection is dead and must be re-established.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.2
// ============================================================================

namespace ElpisEdgeConnect.Sources.Focas2;

/// <summary>
/// Thrown by collectors when a FOCAS2 call returns <see cref="Focas2ErrorCode.EW_SOCKET"/>
/// or <see cref="Focas2ErrorCode.EW_HANDLE"/>, indicating the connection is lost.
/// The adapter's poll loop catches this to trigger reconnection.
/// </summary>
internal sealed class Focas2FatalException : Exception
{
    /// <summary>The FOCAS2 error code that triggered this exception.</summary>
    public Focas2ErrorCode ErrorCode { get; }

    /// <summary>Create a fatal exception for the given error code and function name.</summary>
    public Focas2FatalException(Focas2ErrorCode code, string function)
        : base($"FOCAS2 fatal error {code} in {function}") => ErrorCode = code;
}
