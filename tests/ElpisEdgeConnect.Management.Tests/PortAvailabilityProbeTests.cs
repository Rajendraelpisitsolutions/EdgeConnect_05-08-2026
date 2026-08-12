// ============================================================================
// Tests: PortAvailabilityProbe — pins the wizard-time port pre-flight
//        check added 2026-05-28 after QA hit a port-4840 conflict on
//        their workstation (Kepware / Matrikon / Prosys / vendor PLC
//        tooling already owning the OPC UA IANA default).
//
//        The probe is the wizard-side counterpart to OpcUaServerSinkAdapter's
//        ClassifyStartFailure. Invariants:
//
//          * URL parsing: opc.tcp://host:port is required. Other
//            schemes / missing URLs / non-absolute URLs return Invalid.
//          * Default OPC UA port: opc.tcp without an explicit port
//            assumes 4840.
//          * Available path: an unbound port returns Available and
//            does NOT remain bound after the probe returns.
//          * InUse path: when something else holds the port, the
//            probe returns InUse with a netstat remediation hint.
// Reference: Operator feedback 2026-05-28 (PR #44 follow-up).
// ============================================================================

using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using ElpisEdgeConnect.Management.Wizards;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class PortAvailabilityProbeTests
{
    // ─── URL parsing ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("http://localhost:4840/")]        // wrong scheme
    [InlineData("tcp://localhost:4840/")]         // wrong scheme
    public void TryParseOpcTcpPort_InvalidInputs_ReturnsFalse(string? url)
    {
        PortAvailabilityProbe.TryParseOpcTcpPort(url, out var port).Should().BeFalse();
        port.Should().Be(0);
    }

    [Theory]
    [InlineData("opc.tcp://localhost:4840/edgeconnect", 4840)]
    [InlineData("opc.tcp://0.0.0.0:48400/edgeconnect", 48400)]
    [InlineData("opc.tcp://192.168.1.10:62541/", 62541)]
    public void TryParseOpcTcpPort_ValidInputs_ExtractsPort(string url, int expected)
    {
        PortAvailabilityProbe.TryParseOpcTcpPort(url, out var port).Should().BeTrue();
        port.Should().Be(expected);
    }

    // ─── Probe outcomes ──────────────────────────────────────────────────

    [Fact]
    public async Task ProbeAsync_InvalidUrl_ReturnsInvalidWithoutBinding()
    {
        var result = await PortAvailabilityProbe.ProbeAsync("not-an-opcua-url");

        result.Status.Should().Be(PortProbeStatus.Invalid);
        result.Port.Should().Be(0);
        result.Message.Should().Contain("opc.tcp://");
    }

    [Fact]
    public async Task ProbeAsync_FreePort_ReturnsAvailable_AndReleasesImmediately()
    {
        var port = PickFreePort();
        var url = $"opc.tcp://localhost:{port}/edgeconnect";

        var result = await PortAvailabilityProbe.ProbeAsync(url);

        result.Status.Should().Be(PortProbeStatus.Available);
        result.Port.Should().Be(port);
        result.Message.Should().Contain(port.ToString());

        // The probe MUST release the port on return so the real sink can
        // bind it later. Verify by binding the same port ourselves — if
        // the probe leaked the listener this would throw.
        using var follower = new TcpListener(IPAddress.Any, port);
        follower.Start();
        follower.Stop();
    }

    [Fact]
    public async Task ProbeAsync_PortHeldByAnotherListener_ReturnsInUseWithNetstatHint()
    {
        var port = PickFreePort();
        using var holder = new TcpListener(IPAddress.Any, port);
        holder.Start();
        try
        {
            var url = $"opc.tcp://localhost:{port}/edgeconnect";
            var result = await PortAvailabilityProbe.ProbeAsync(url);

            result.Status.Should().Be(PortProbeStatus.InUse);
            result.Port.Should().Be(port);
            result.Message.Should().Contain("already in use");
            result.Message.Should().Contain($"netstat -ano | findstr :{port}");
        }
        finally
        {
            holder.Stop();
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Pick an ephemeral free TCP port by binding to port 0 and reading
    /// back the OS-assigned port. Standard pattern for tests that need a
    /// free port without racing other test runs on the same host.
    /// </summary>
    private static int PickFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
