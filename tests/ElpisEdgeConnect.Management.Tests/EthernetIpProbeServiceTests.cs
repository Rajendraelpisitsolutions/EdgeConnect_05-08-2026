// ============================================================================
// Tests: EthernetIpProbeService — the wizard's read-only Test Read probe
//        against a fake IEthernetIpClient. Pins read-ok / tag-not-found /
//        connect-failed outcomes, the license gate, and config-invalid for a
//        bad datatype (never a connection failure).
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Management.Api;
using ElpisEdgeConnect.Sources.EthernetIp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class EthernetIpProbeServiceTests
{
    private sealed class FakeClient : IEthernetIpClient
    {
        public EthernetIpReadResult Result { get; init; } = EthernetIpReadResult.Ok(7, CanonicalValueType.Integer);
        public bool ConnectThrows { get; init; }
        public bool IsConnected { get; private set; }
        public int ConnectCalls { get; private set; }

        public Task ConnectAsync(EthernetIpConnectionParameters parameters, CancellationToken ct)
        {
            ConnectCalls++;
            if (ConnectThrows)
            {
                throw new EthernetIpFatalException("ETHERNETIP.CONNECT_FAILED", "refused");
            }
            IsConnected = true;
            return Task.CompletedTask;
        }

        public void Disconnect() => IsConnected = false;
        public Task<EthernetIpReadResult> ReadTagAsync(string address, EthernetIpElementType elementType, CancellationToken ct)
            => Task.FromResult(Result);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static EthernetIpProbeService Service(FakeClient client, bool licensed = true) =>
        new(
            clientFactory: () => client,
            isModuleEnabled: _ => licensed,
            loggerFactory: NullLoggerFactory.Instance,
            probeBudget: TimeSpan.FromSeconds(5));

    private static EthernetIpTestReadRequest Read(string datatype = "DINT") =>
        new() { Host = "10.0.0.9", Path = "1,0", CpuFamily = "ControlLogix", Address = "Speed", Datatype = datatype };

    [Fact]
    public async Task TestRead_Success_ReadOk()
    {
        var outcome = await Service(new FakeClient()).TestReadAsync(Read(), CancellationToken.None);

        outcome.Status.Should().Be(EthernetIpProbeStatus.Success);
        outcome.Result.Success.Should().BeTrue();
        outcome.Result.Outcome.Should().Be(EthernetIpProbeOutcomes.ReadOk);
        outcome.Result.Value.Should().Be("7");
    }

    [Fact]
    public async Task TestRead_TagNotFound_IsTagNotFound()
    {
        var client = new FakeClient { Result = EthernetIpReadResult.Fail("ETHERNETIP.TAG_NOT_FOUND", "missing") };

        var outcome = await Service(client).TestReadAsync(Read(), CancellationToken.None);

        outcome.Status.Should().Be(EthernetIpProbeStatus.Failure);
        outcome.Result.Outcome.Should().Be(EthernetIpProbeOutcomes.TagNotFound);
    }

    [Fact]
    public async Task TestRead_ConnectThrows_IsConnectFailed()
    {
        var client = new FakeClient { ConnectThrows = true };

        var outcome = await Service(client).TestReadAsync(Read(), CancellationToken.None);

        outcome.Status.Should().Be(EthernetIpProbeStatus.Failure);
        outcome.Result.Outcome.Should().Be(EthernetIpProbeOutcomes.ConnectFailed);
    }

    [Fact]
    public async Task TestRead_LicenseDisabled_BlocksBeforeNetwork()
    {
        var client = new FakeClient();

        var outcome = await Service(client, licensed: false).TestReadAsync(Read(), CancellationToken.None);

        outcome.Status.Should().Be(EthernetIpProbeStatus.LicenseDisabled);
        client.ConnectCalls.Should().Be(0);
    }

    [Fact]
    public async Task TestRead_BadDatatype_IsConfigInvalid()
    {
        var outcome = await Service(new FakeClient()).TestReadAsync(Read(datatype: "WIDGET"), CancellationToken.None);

        outcome.Status.Should().Be(EthernetIpProbeStatus.Failure);
        outcome.Result.Outcome.Should().Be(EthernetIpProbeOutcomes.ConfigInvalid);
    }

    [Fact]
    public void StatusMapping_MapsHttpCodes()
    {
        EthernetIpProbeStatusMapping.StatusCodeFor(EthernetIpProbeStatus.Success).Should().Be(200);
        EthernetIpProbeStatusMapping.StatusCodeFor(EthernetIpProbeStatus.LicenseDisabled).Should().Be(403);
        EthernetIpProbeStatusMapping.StatusCodeFor(EthernetIpProbeStatus.Busy).Should().Be(409);
        EthernetIpProbeStatusMapping.StatusCodeFor(EthernetIpProbeStatus.Failure).Should().Be(200);
    }
}
