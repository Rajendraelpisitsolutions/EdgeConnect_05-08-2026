// ============================================================================
// File: FakeMTConnectClient.cs
// Purpose: Configurable IMTConnectClient for unit tests. Returns fixed XML
//          strings (or throws configured exceptions) so adapter behavior
//          can be exercised without a live Agent.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace ElpisEdgeConnect.Sources.MTConnect.Tests;

/// <summary>
/// Configurable fake implementing <see cref="IMTConnectClient"/>. Defaults
/// both responses to a well-formed but minimal empty MTConnectStreams
/// document; tests override per scenario.
/// </summary>
internal sealed class FakeMTConnectClient : IMTConnectClient
{
    private const string EmptyStreams =
        "<MTConnectStreams xmlns=\"urn:mtconnect.org:MTConnectStreams:1.7\"><Streams/></MTConnectStreams>";

    /// <summary>XML body returned by <see cref="GetCurrentAsync"/>.</summary>
    public string CurrentResponse { get; set; } = EmptyStreams;

    /// <summary>XML body returned by <see cref="GetProbeAsync"/>.</summary>
    public string ProbeResponse { get; set; } =
        "<MTConnectDevices><Devices><Device name=\"CNC-1\" uuid=\"urn:test:cnc-1\"/></Devices></MTConnectDevices>";

    /// <summary>
    /// Override to simulate transport errors. When non-null, the call returns
    /// the exception instead of <see cref="CurrentResponse"/>.
    /// </summary>
    public Exception? CurrentException { get; set; }

    /// <summary>Per-call invocation counter, for ordering assertions.</summary>
    public int GetCurrentCallCount { get; private set; }
    public int GetProbeCallCount { get; private set; }

    public Task<string> GetProbeAsync(CancellationToken ct)
    {
        GetProbeCallCount++;
        return Task.FromResult(ProbeResponse);
    }

    public Task<string> GetCurrentAsync(CancellationToken ct)
    {
        GetCurrentCallCount++;
        if (CurrentException is not null)
        {
            throw CurrentException;
        }
        return Task.FromResult(CurrentResponse);
    }
}
