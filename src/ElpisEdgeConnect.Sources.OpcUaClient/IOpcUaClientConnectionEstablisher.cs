// ============================================================================
// File: IOpcUaClientConnectionEstablisher.cs
// Purpose: Test seam for the full OPC UA Client connection-establish
//          pipeline. Wraps app-config build + validate, certificate
//          provisioning, endpoint discovery, user-identity build, and
//          Session.Create into a single substitutable surface.
//
//          The IOpcUaClientSessionFactory abstraction (the bare
//          Session.Create wrapper) is now an INTERNAL implementation
//          detail of DefaultOpcUaClientConnectionEstablisher — tests
//          substitute the establisher (this interface) rather than the
//          factory, because everything that happens BEFORE the factory
//          call (cert store creation, endpoint discovery) is itself
//          heavyweight I/O that real unit tests need to skip.
//
// LOCKED design rules:
//   * Production wiring uses DefaultOpcUaClientConnectionEstablisher
//   * Tests use NSubstitute on this interface to short-circuit ALL real I/O
//   * Callers (adapter) own the returned ISession's lifetime
//
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1, §5.1
// ============================================================================

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Establish a connected OPC UA <see cref="ISession"/> from an
/// <see cref="OpcUaClientSourceConfiguration"/>. Wraps the full
/// pipeline (app config build + validate, cert init, endpoint discovery,
/// user-identity build, session factory call) into a single
/// test-substitutable surface.
/// </summary>
internal interface IOpcUaClientConnectionEstablisher
{
    /// <summary>
    /// Run the full connect pipeline and return a connected, ready-to-use
    /// <see cref="ISession"/>. The caller owns the session's lifetime
    /// (Dispose / CloseAsync).
    /// </summary>
    Task<ISession> EstablishAsync(
        OpcUaClientSourceConfiguration config,
        string sessionName,
        CancellationToken ct);
}
