// ============================================================================
// Tests: OpcUaServerSinkAdapter.ClassifyStartFailure — pins the
//        operator-facing error translation added 2026-05-28.
//
//        QA hit a port-4840 conflict on their workstation (another
//        OPC UA stack already held the port) and the raw failure
//        surfaced as a generic "Failed to start OPC UA server: …"
//        OPCUA.SERVER_START_FAILED — not actionable. The classifier
//        now distinguishes port-bind failures from other startup
//        errors so operators get a netstat remediation hint instead
//        of an opaque stack trace.
//
//        Invariants:
//
//          * SocketException(AddressAlreadyInUse) → OPCUA.SERVER_PORT_IN_USE,
//            Configuration category, retryable=false, message contains
//            the port number and a netstat hint.
//          * The bind failure pattern also surfaces wrapped inside
//            Opc.Ua.ServiceResultException with the message "Failed to
//            establish tcp listener sockets" — the classifier matches
//            on the message text too.
//          * Anything else falls through to OPCUA.SERVER_START_FAILED
//            (Internal category) — backwards-compatible with pre-2026-05-28
//            behaviour.
// Reference: Operator feedback 2026-05-28.
// ============================================================================

using System;
using System.Net.Sockets;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Sinks.OpcUaServer;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.OpcUaServer.Tests;

public class OpcUaServerSinkAdapterStartFailureTests
{
    private const string EndpointUrl = "opc.tcp://0.0.0.0:4840/edgeconnect";

    // ─── Port-in-use detection ───────────────────────────────────────────

    [Fact]
    public void ClassifyStartFailure_AddressAlreadyInUseSocketException_MapsToPortInUse()
    {
        var ex = new SocketException((int)SocketError.AddressAlreadyInUse);

        var error = OpcUaServerSinkAdapter.ClassifyStartFailure(ex, EndpointUrl);

        error.Code.Should().Be("OPCUA.SERVER_PORT_IN_USE");
        error.Category.Should().Be(ErrorCategory.Configuration);
        error.Retryable.Should().BeFalse();
        error.Message.Should().Contain("4840");
        error.Message.Should().Contain("netstat -ano | findstr :4840");
    }

    [Fact]
    public void ClassifyStartFailure_WrappedAddressAlreadyInUse_MapsToPortInUse()
    {
        // The OPC Foundation stack wraps SocketException inside its own
        // exception types — make sure the classifier walks the inner-
        // exception chain.
        var inner = new SocketException((int)SocketError.AddressAlreadyInUse);
        var outer = new InvalidOperationException("listener bring-up failed", inner);

        var error = OpcUaServerSinkAdapter.ClassifyStartFailure(outer, EndpointUrl);

        error.Code.Should().Be("OPCUA.SERVER_PORT_IN_USE");
    }

    [Fact]
    public void ClassifyStartFailure_ListenerSocketsMessage_MapsToPortInUse()
    {
        // Opc.Ua.ServiceResultException surfaces the failure as a string
        // even when the inner SocketException is absent. Match on the
        // canonical message fragment.
        var ex = new InvalidOperationException(
            "Failed to establish tcp listener sockets for Ipv4 and IPv6.");

        var error = OpcUaServerSinkAdapter.ClassifyStartFailure(ex, EndpointUrl);

        error.Code.Should().Be("OPCUA.SERVER_PORT_IN_USE");
        error.Message.Should().Contain("4840");
    }

    // ─── Endpoint-URL parsing in the message ─────────────────────────────

    [Fact]
    public void ClassifyStartFailure_NonStandardPort_NamesItInMessage()
    {
        var ex = new SocketException((int)SocketError.AddressAlreadyInUse);
        const string url = "opc.tcp://0.0.0.0:48400/edgeconnect";

        var error = OpcUaServerSinkAdapter.ClassifyStartFailure(ex, url);

        error.Message.Should().Contain("48400");
        error.Message.Should().Contain("netstat -ano | findstr :48400");
    }

    [Fact]
    public void ClassifyStartFailure_NullOrInvalidEndpointUrl_FallsBackToGenericPortPhrase()
    {
        var ex = new SocketException((int)SocketError.AddressAlreadyInUse);

        var error = OpcUaServerSinkAdapter.ClassifyStartFailure(ex, endpointUrl: null);

        error.Code.Should().Be("OPCUA.SERVER_PORT_IN_USE");
        error.Message.Should().Contain("the configured port");
    }

    // ─── Pass-through for other failures ─────────────────────────────────

    [Fact]
    public void ClassifyStartFailure_UnrelatedException_FallsThroughToServerStartFailed()
    {
        var ex = new InvalidOperationException("certificate trust list was malformed");

        var error = OpcUaServerSinkAdapter.ClassifyStartFailure(ex, EndpointUrl);

        error.Code.Should().Be("OPCUA.SERVER_START_FAILED");
        error.Category.Should().Be(ErrorCategory.Internal);
        error.Message.Should().Contain("certificate trust list was malformed");
    }

    [Fact]
    public void ClassifyStartFailure_SocketExceptionOtherErrorCode_DoesNotMapToPortInUse()
    {
        // ConnectionRefused or any non-AddressAlreadyInUse SocketError must
        // NOT be misclassified as a port-conflict.
        var ex = new SocketException((int)SocketError.ConnectionRefused);

        var error = OpcUaServerSinkAdapter.ClassifyStartFailure(ex, EndpointUrl);

        error.Code.Should().Be("OPCUA.SERVER_START_FAILED");
    }

    // ─── IsPortInUseFailure direct contract ──────────────────────────────

    [Fact]
    public void IsPortInUseFailure_TrueForAddressAlreadyInUse()
    {
        OpcUaServerSinkAdapter.IsPortInUseFailure(
            new SocketException((int)SocketError.AddressAlreadyInUse))
            .Should().BeTrue();
    }

    [Fact]
    public void IsPortInUseFailure_FalseForUnrelatedException()
    {
        OpcUaServerSinkAdapter.IsPortInUseFailure(
            new InvalidOperationException("some other error"))
            .Should().BeFalse();
    }
}
