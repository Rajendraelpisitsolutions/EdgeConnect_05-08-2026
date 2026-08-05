// ============================================================================
// File: BulkMTConnectProbeServiceTests.cs
// Purpose: Coverage for the bulk MTConnect probe service — v3.1 sec9 hardening
//          contract surface. Implements T39 (URL construction), T41 (timeout),
//          T42 (1 MB cap), T43 (XXE-disabled XML parser), T44 (scheme-change
//          redirect), and a happy-path observation-count check.
//
//          T40 (file:// + javascript: rejected at preview) is covered by
//          RowValidatorsTests. T45 (probe failures don't block submit) is
//          implicit in BulkSourceMergeService design — probe is never
//          called from PreviewAsync / SubmitAsync.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using ElpisEdgeConnect.Management.Api.BulkSourceMerge;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class BulkMTConnectProbeServiceTests
{
    private sealed class FakeFetcher : IBulkMTConnectProbeFetcher
    {
        public Dictionary<string, (BulkProbeOutcome Outcome, string? Body, int? StatusCode)> Map { get; } = new();

        public Task<(BulkProbeOutcome Outcome, string? Body, int? StatusCode)> FetchAsync(
            string baseUrl,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Map.TryGetValue(baseUrl, out var v)
                ? v
                : (BulkProbeOutcome.Unreachable, null, null));
        }
    }

    // ── T39 — URL construction ────────────────────────────────────────────────
    [Theory]
    [InlineData("http://192.168.10.51:5000",        "http://192.168.10.51:5000/probe")]
    [InlineData("http://192.168.10.51:5000/",       "http://192.168.10.51:5000/probe")]
    [InlineData("http://192.168.10.80/mtconnect/",  "http://192.168.10.80/mtconnect/probe")]
    [InlineData("http://192.168.10.80/mtconnect",   "http://192.168.10.80/mtconnect/probe")]
    [InlineData("https://example.local/mtconnect/", "https://example.local/mtconnect/probe")]
    public void BuildProbeUri_ConstructsCorrectUrl(string baseUrl, string expected)
    {
        BulkMTConnectProbeService.BuildProbeUri(baseUrl).ToString().Should().Be(expected);
    }

    // ── T41 — Timeout ────────────────────────────────────────────────────────
    [Fact]
    public async Task ProbeAsync_TimeoutFromFetcher_BubblesAsTimeoutResult()
    {
        var fetcher = new FakeFetcher();
        fetcher.Map["http://example.local/"] = (BulkProbeOutcome.Timeout, null, null);
        var svc = new BulkMTConnectProbeService(fetcher);

        var results = await svc.ProbeAsync(new[] { "http://example.local/" }, CancellationToken.None);

        results.Should().ContainSingle().Which.Outcome.Should().Be(BulkProbeOutcome.Timeout);
        results[0].Detail.Should().Contain("5 s");
    }

    [Fact]
    public void ProbeBudget_LockedAt5Seconds()
    {
        BulkMTConnectProbeService.ProbeBudget.Should().Be(TimeSpan.FromSeconds(5));
    }

    // ── T42 — 1 MB response cap ──────────────────────────────────────────────
    [Fact]
    public async Task ProbeAsync_ResponseTooLargeFromFetcher_BubblesAsResponseTooLargeResult()
    {
        var fetcher = new FakeFetcher();
        fetcher.Map["http://example.local/"] = (BulkProbeOutcome.ResponseTooLarge, null, 200);
        var svc = new BulkMTConnectProbeService(fetcher);

        var results = await svc.ProbeAsync(new[] { "http://example.local/" }, CancellationToken.None);

        results[0].Outcome.Should().Be(BulkProbeOutcome.ResponseTooLarge);
        results[0].Detail.Should().Contain("1 MB");
    }

    [Fact]
    public void MaxResponseBytes_LockedAt1MB()
    {
        BulkMTConnectProbeService.MaxResponseBytes.Should().Be(1 * 1024 * 1024);
    }

    // ── T43 — XXE-disabled XML parser ────────────────────────────────────────
    [Fact]
    public void HardenedXmlReaderSettings_DtdProhibited()
    {
        var settings = BulkMTConnectProbeService.BuildHardenedXmlReaderSettings();

        settings.DtdProcessing.Should().Be(DtdProcessing.Prohibit);
        // XmlResolver is set-only on XmlReaderSettings; can't assert its value
        // directly. The end-to-end DTD-rejection test below verifies the
        // resolver is effectively disabled.
    }

    [Fact]
    public void HardenedXmlReaderSettings_RejectsDtdLadenResponseAtParse()
    {
        var maliciousXml =
            "<?xml version=\"1.0\"?>" +
            "<!DOCTYPE foo [ <!ENTITY xxe SYSTEM \"file:///etc/passwd\"> ]>" +
            "<MTConnectDevices><Devices><DataItem id=\"x\">&xxe;</DataItem></Devices></MTConnectDevices>";

        var settings = BulkMTConnectProbeService.BuildHardenedXmlReaderSettings();

        Action act = () =>
        {
            using var reader = XmlReader.Create(new StringReader(maliciousXml), settings);
            while (reader.Read()) { }
        };
        act.Should().Throw<XmlException>();
    }

    // ── T44 — Scheme-change redirect ─────────────────────────────────────────
    [Fact]
    public async Task ProbeAsync_RedirectSchemeChangeFromFetcher_BubblesAsRedirectSchemeChangeResult()
    {
        var fetcher = new FakeFetcher();
        fetcher.Map["http://example.local/"] = (BulkProbeOutcome.RedirectSchemeChange, null, 302);
        var svc = new BulkMTConnectProbeService(fetcher);

        var results = await svc.ProbeAsync(new[] { "http://example.local/" }, CancellationToken.None);

        results[0].Outcome.Should().Be(BulkProbeOutcome.RedirectSchemeChange);
        results[0].StatusCode.Should().Be(302);
    }

    // ── Happy path — observation count ───────────────────────────────────────
    [Fact]
    public async Task ProbeAsync_ReachableResponse_CountsDataItemElements()
    {
        var xml =
            "<MTConnectDevices>" +
            "  <Devices>" +
            "    <DataItem id=\"a\" type=\"X\" />" +
            "    <DataItem id=\"b\" type=\"Y\" />" +
            "    <DataItem id=\"c\" type=\"Z\" />" +
            "  </Devices>" +
            "</MTConnectDevices>";
        var fetcher = new FakeFetcher();
        fetcher.Map["http://example.local/"] = (BulkProbeOutcome.Reachable, xml, 200);
        var svc = new BulkMTConnectProbeService(fetcher);

        var results = await svc.ProbeAsync(new[] { "http://example.local/" }, CancellationToken.None);

        results[0].Outcome.Should().Be(BulkProbeOutcome.Reachable);
        results[0].ObservationCount.Should().Be(3);
    }

    [Fact]
    public async Task ProbeAsync_MalformedXmlInReachableResponse_BubblesAsInvalidXml()
    {
        var fetcher = new FakeFetcher();
        fetcher.Map["http://example.local/"] = (BulkProbeOutcome.Reachable, "not-xml", 200);
        var svc = new BulkMTConnectProbeService(fetcher);

        var results = await svc.ProbeAsync(new[] { "http://example.local/" }, CancellationToken.None);

        results[0].Outcome.Should().Be(BulkProbeOutcome.InvalidXml);
    }

    [Fact]
    public async Task ProbeAsync_EmptyInputList_ReturnsEmptyResults()
    {
        var svc = new BulkMTConnectProbeService(new FakeFetcher());

        var results = await svc.ProbeAsync(Array.Empty<string>(), CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task ProbeAsync_BatchOfMixedOutcomes_PreservesInputOrder()
    {
        var fetcher = new FakeFetcher();
        fetcher.Map["http://r1/"] = (BulkProbeOutcome.Reachable, "<a><DataItem/></a>", 200);
        fetcher.Map["http://r2/"] = (BulkProbeOutcome.Timeout, null, null);
        fetcher.Map["http://r3/"] = (BulkProbeOutcome.HttpError, null, 500);
        var svc = new BulkMTConnectProbeService(fetcher);

        var results = await svc.ProbeAsync(new[] { "http://r1/", "http://r2/", "http://r3/" }, CancellationToken.None);

        results[0].BaseUrl.Should().Be("http://r1/");
        results[0].Outcome.Should().Be(BulkProbeOutcome.Reachable);
        results[1].BaseUrl.Should().Be("http://r2/");
        results[1].Outcome.Should().Be(BulkProbeOutcome.Timeout);
        results[2].BaseUrl.Should().Be("http://r3/");
        results[2].Outcome.Should().Be(BulkProbeOutcome.HttpError);
    }

    // ── T45 contract check ───────────────────────────────────────────────────
    [Fact]
    public void ProbeService_IsNotReferencedByMergeService_ProbeNeverBlocksSubmit()
    {
        // The probe is operator-triggered separately from PreviewAsync /
        // SubmitAsync. BulkSourceMergeService takes IConfigurationManager +
        // IConfigurationSchemaValidator in its constructor — nothing about
        // probing — by design. This test pins that contract: if a future
        // refactor wires the probe into the merge path, this dependency-
        // shape assertion catches it.
        var ctors = typeof(BulkSourceMergeService).GetConstructors();
        var paramTypes = ctors[0].GetParameters();
        paramTypes.Select(p => p.ParameterType).Should().NotContain(typeof(BulkMTConnectProbeService));
        paramTypes.Select(p => p.ParameterType).Should().NotContain(typeof(IBulkMTConnectProbeFetcher));
    }
}
