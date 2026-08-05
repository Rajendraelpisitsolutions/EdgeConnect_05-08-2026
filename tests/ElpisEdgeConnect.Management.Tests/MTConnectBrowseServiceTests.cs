// ============================================================================
// Tests: MTConnectBrowseService + status mapping (M.2b.4 M2c). The HTTP /probe
//        fetch is behind IMTConnectProbeFetcher, so every browse verdict — and
//        the status→HTTP mapping — is pinned without a live agent.
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Management.Api;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class MTConnectBrowseServiceTests
{
    private const string FullProbe = """
        <MTConnectDevices xmlns="urn:mtconnect.org:MTConnectDevices:1.7">
          <Devices>
            <Device id="d1" name="VCN-530C" uuid="u1"><Description manufacturer="Mazak"/>
              <DataItems>
                <DataItem id="exec" type="EXECUTION" category="EVENT"/>
                <DataItem id="feed" type="PATH_FEEDRATE" category="SAMPLE"/>
              </DataItems>
              <Components><Axes><Linear id="x" name="X"><DataItems>
                <DataItem id="xp" type="POSITION" category="SAMPLE"/>
              </DataItems></Linear></Axes></Components>
            </Device>
          </Devices>
        </MTConnectDevices>
        """;

    private static MTConnectBrowseService ServiceWith(ProbeFetch fetch, bool licensed = true) =>
        new(new FakeFetcher(fetch), _ => licensed, NullLogger<MTConnectBrowseService>.Instance);

    private static MTConnectBrowseRequest Req => new("http://agent.local:5000", null);

    [Fact]
    public async Task LicenseDisabled_ReturnsLicenseDisabled_AndDoesNotFetch()
    {
        var fetcher = new FakeFetcher(new ProbeFetch(ProbeFetchOutcome.Ok, FullProbe));
        var service = new MTConnectBrowseService(fetcher, _ => false, NullLogger<MTConnectBrowseService>.Instance);

        var result = await service.BrowseAsync(Req, CancellationToken.None);

        result.Status.Should().Be(MTConnectBrowseStatus.LicenseDisabled);
        fetcher.Called.Should().BeFalse("the license gate short-circuits before any network call");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("/relative/only")]
    public async Task InvalidUrl_ReturnsUnreachable(string url)
    {
        var service = ServiceWith(new ProbeFetch(ProbeFetchOutcome.Ok, FullProbe));

        var result = await service.BrowseAsync(new MTConnectBrowseRequest(url, null), CancellationToken.None);

        result.Status.Should().Be(MTConnectBrowseStatus.Unreachable);
    }

    [Fact]
    public Task FetchUnreachable_MapsToUnreachable() =>
        AssertOutcome(new ProbeFetch(ProbeFetchOutcome.Unreachable), MTConnectBrowseStatus.Unreachable);

    [Fact]
    public Task FetchTimeout_MapsToTimeout() =>
        AssertOutcome(new ProbeFetch(ProbeFetchOutcome.Timeout), MTConnectBrowseStatus.Timeout);

    [Fact]
    public Task FetchUnauthorized_MapsToUnauthorized() =>
        AssertOutcome(new ProbeFetch(ProbeFetchOutcome.Unauthorized), MTConnectBrowseStatus.Unauthorized);

    [Fact]
    public Task FetchHttpError_MapsToUnreachable() =>
        AssertOutcome(new ProbeFetch(ProbeFetchOutcome.HttpError, StatusCode: 503), MTConnectBrowseStatus.Unreachable);

    // Private helper — may reference the internal ProbeFetch* types (a public
    // [Theory] signature could not, per CS0051).
    private static async Task AssertOutcome(ProbeFetch fetch, MTConnectBrowseStatus expected)
    {
        var service = ServiceWith(fetch);
        var result = await service.BrowseAsync(Req, CancellationToken.None);
        result.Status.Should().Be(expected);
    }

    [Fact]
    public async Task FullProbe_ReachableWithRecognisedTags_AndPopulatesResult()
    {
        var service = ServiceWith(new ProbeFetch(ProbeFetchOutcome.Ok, FullProbe));

        var result = await service.BrowseAsync(Req, CancellationToken.None);

        result.Status.Should().Be(MTConnectBrowseStatus.ReachableWithRecognisedTags);
        result.Success.Should().BeTrue();
        result.DeviceName.Should().Be("VCN-530C");
        result.Manufacturer.Should().Be("Mazak");
        result.AvailableDevices.Should().Equal("VCN-530C");
        result.Axes.Should().Equal("X");
        result.Tags.Should().Contain(t => t.CanonicalTag == "status/run_state" && t.Available);
        result.Tags.Should().Contain(t => t.CanonicalTag == "spindle/load" && !t.Available);
    }

    [Fact]
    public async Task ReachableButNoRecognisedTags_WhenNoMappedDataItems()
    {
        const string bare = """
            <MTConnectDevices xmlns="urn:mtconnect.org:MTConnectDevices:1.7">
              <Devices><Device id="d1" name="X" uuid="u1">
                <DataItems><DataItem id="a" type="AVAILABILITY" category="EVENT"/></DataItems>
              </Device></Devices>
            </MTConnectDevices>
            """;
        var service = ServiceWith(new ProbeFetch(ProbeFetchOutcome.Ok, bare));

        var result = await service.BrowseAsync(Req, CancellationToken.None);

        result.Status.Should().Be(MTConnectBrowseStatus.ReachableNoRecognisedTags);
        result.Message.Should().Contain("no data");
    }

    [Fact]
    public async Task MalformedProbe_ReturnsInvalidProbeDocument()
    {
        var service = ServiceWith(new ProbeFetch(ProbeFetchOutcome.Ok, "<html>not mtconnect</html>"));

        var result = await service.BrowseAsync(Req, CancellationToken.None);

        result.Status.Should().Be(MTConnectBrowseStatus.InvalidProbeDocument);
    }

    [Fact]
    public async Task EmptyDevices_ReturnsUnsupportedAgent()
    {
        var service = ServiceWith(new ProbeFetch(ProbeFetchOutcome.Ok,
            "<MTConnectDevices xmlns=\"urn:mtconnect.org:MTConnectDevices:1.7\"><Devices/></MTConnectDevices>"));

        var result = await service.BrowseAsync(Req, CancellationToken.None);

        result.Status.Should().Be(MTConnectBrowseStatus.UnsupportedAgent);
    }

    [Theory]
    [InlineData(MTConnectBrowseStatus.LicenseDisabled, 403)]
    [InlineData(MTConnectBrowseStatus.ReachableWithRecognisedTags, 200)]
    [InlineData(MTConnectBrowseStatus.ReachableNoRecognisedTags, 200)]
    [InlineData(MTConnectBrowseStatus.Unreachable, 200)]
    [InlineData(MTConnectBrowseStatus.Timeout, 200)]
    [InlineData(MTConnectBrowseStatus.Unauthorized, 200)]
    [InlineData(MTConnectBrowseStatus.InvalidProbeDocument, 200)]
    [InlineData(MTConnectBrowseStatus.UnsupportedAgent, 200)]
    public void StatusMapping_OnlyLicenseDisabledIs403(MTConnectBrowseStatus status, int expected)
    {
        MTConnectBrowseStatusMapping.StatusCodeFor(status).Should().Be(expected);
    }

    private sealed class FakeFetcher : IMTConnectProbeFetcher
    {
        private readonly ProbeFetch _fetch;
        public bool Called { get; private set; }
        public FakeFetcher(ProbeFetch fetch) => _fetch = fetch;

        public Task<ProbeFetch> FetchProbeAsync(string agentBaseUrl, TimeSpan budget, CancellationToken ct)
        {
            Called = true;
            return Task.FromResult(_fetch);
        }
    }
}
