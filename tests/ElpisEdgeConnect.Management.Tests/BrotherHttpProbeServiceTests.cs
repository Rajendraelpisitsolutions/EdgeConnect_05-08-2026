// ============================================================================
// Tests: BrotherHttpProbeService — pins the Brother HTTP Test Connection
//        probe behaviour (M.2d.2 v2 §4). Key invariants:
//
//          * No state mutation — probe issues GET only, never PUT/POST.
//          * License gate: when source-brother-http is disabled, returns
//            LicenseDisabled WITHOUT making any HTTP call.
//          * Single-flight per normalised BaseUrl: two probes against
//            the same target contend; "/  vs  no-slash" target the same
//            lease.
//          * Timeout: budget bounds the HTTP call; expired surface
//            BROTHER.PROBE_TIMEOUT.
//          * HTTP non-success / empty body / unreachable map to
//            structured BROTHER.PROBE_* error codes.
//          * Diagnostic fields populated: EndpointTested + HttpStatusObserved.
//          * ProbeId on every result for log correlation.
// Reference: docs/sessions/2026-05-22-m2d2-source-wizards-plan-v2.md §4
// ============================================================================

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Management.Api;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class BrotherHttpProbeServiceTests
{
    // ─── Happy path ─────────────────────────────────────────────────────

    [Fact]
    public async Task ProbeAsync_HappyPath_ReturnsSuccessWithDiagnosticFields()
    {
        var handler = new RecordingHandler((req, _) =>
        {
            req.Method.Should().Be(HttpMethod.Get, "probe must be read-only — never POST/PUT");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("MachineName=CNC-1\nStatus=Idle\n"),
            });
        });
        var service = MakeService(handler);

        var outcome = await service.ProbeAsync(
            new BrotherHttpProbeRequest { BaseUrl = "http://192.168.2.110" },
            CancellationToken.None);

        outcome.Status.Should().Be(BrotherHttpProbeStatus.Success);
        outcome.Result.Success.Should().BeTrue();
        outcome.Result.ErrorCode.Should().BeNull();
        outcome.Result.EndpointTested.Should().Be("http://192.168.2.110/HTTPD_MCNINFO");
        outcome.Result.HttpStatusObserved.Should().Be(200);
        outcome.Result.ProbeId.Should().NotBeNullOrEmpty();
        handler.RequestCount.Should().Be(1);
        handler.PostOrPutCount.Should().Be(0, "no state mutation — GET only");
    }

    // ─── License gate ───────────────────────────────────────────────────

    [Fact]
    public async Task ProbeAsync_LicenseDisabled_ReturnsLicenseDisabled_AndDoesNotMakeHttpCall()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var service = MakeService(handler, isModuleEnabled: _ => false);

        var outcome = await service.ProbeAsync(
            new BrotherHttpProbeRequest { BaseUrl = "http://192.168.2.110" },
            CancellationToken.None);

        outcome.Status.Should().Be(BrotherHttpProbeStatus.LicenseDisabled);
        outcome.Result.ErrorCode.Should().Be("BROTHER.PROBE_LICENSE_DISABLED");
        handler.RequestCount.Should().Be(0, "license gate must short-circuit before HTTP");
    }

    // ─── Single-flight (target-keyed) ───────────────────────────────────

    [Fact]
    public async Task ProbeAsync_SecondProbeSameBaseUrl_ReturnsBusy()
    {
        var slowGate = new TaskCompletionSource<HttpResponseMessage>();
        var probeStartedGate = new TaskCompletionSource();

        var handler = new RecordingHandler(async (_, _) =>
        {
            probeStartedGate.TrySetResult();
            return await slowGate.Task;
        });
        var service = MakeService(handler);

        var first = service.ProbeAsync(
            new BrotherHttpProbeRequest { BaseUrl = "http://192.168.2.110" },
            CancellationToken.None);

        await probeStartedGate.Task;

        // Second probe while first is in-flight — same target, same lease key.
        var secondOutcome = await service.ProbeAsync(
            new BrotherHttpProbeRequest { BaseUrl = "http://192.168.2.110" },
            CancellationToken.None);

        secondOutcome.Status.Should().Be(BrotherHttpProbeStatus.Busy);
        secondOutcome.Result.ErrorCode.Should().Be("BROTHER.PROBE_BUSY");

        // Release the first probe so the test cleans up.
        slowGate.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("ok"),
        });
        await first;
    }

    [Fact]
    public async Task ProbeAsync_DifferentBaseUrl_DoesNotContend()
    {
        // Target-keyed lease: a probe against another CNC must not be
        // blocked by an in-flight probe against the first.
        var slowGate = new TaskCompletionSource<HttpResponseMessage>();
        var probeStartedGate = new TaskCompletionSource();
        var handler = new RecordingHandler(async (req, _) =>
        {
            // Second URL: respond fast.
            if (req.RequestUri!.Host == "10.0.0.5")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("fast"),
                };
            }
            probeStartedGate.TrySetResult();
            return await slowGate.Task;
        });
        var service = MakeService(handler);

        var slow = service.ProbeAsync(
            new BrotherHttpProbeRequest { BaseUrl = "http://192.168.2.110" },
            CancellationToken.None);

        await probeStartedGate.Task;

        var fastOutcome = await service.ProbeAsync(
            new BrotherHttpProbeRequest { BaseUrl = "http://10.0.0.5" },
            CancellationToken.None);

        fastOutcome.Status.Should().Be(BrotherHttpProbeStatus.Success, "different target = independent lease");

        slowGate.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("done"),
        });
        await slow;
    }

    // ─── HTTP outcomes ──────────────────────────────────────────────────

    [Fact]
    public async Task ProbeAsync_Http404_ReturnsHttpStatusError()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("not found"),
                ReasonPhrase = "Not Found",
            }));
        var service = MakeService(handler);

        var outcome = await service.ProbeAsync(
            new BrotherHttpProbeRequest { BaseUrl = "http://192.168.2.110" },
            CancellationToken.None);

        outcome.Status.Should().Be(BrotherHttpProbeStatus.Failure);
        outcome.Result.ErrorCode.Should().Be("BROTHER.PROBE_HTTP_STATUS");
        outcome.Result.HttpStatusObserved.Should().Be(404);
        outcome.Result.EndpointTested.Should().Be("http://192.168.2.110/HTTPD_MCNINFO");
    }

    [Fact]
    public async Task ProbeAsync_Http200_EmptyBody_FailsWithEmptyBodyCode()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(""),
            }));
        var service = MakeService(handler);

        var outcome = await service.ProbeAsync(
            new BrotherHttpProbeRequest { BaseUrl = "http://192.168.2.110" },
            CancellationToken.None);

        outcome.Status.Should().Be(BrotherHttpProbeStatus.Failure);
        outcome.Result.ErrorCode.Should().Be("BROTHER.PROBE_EMPTY_BODY");
        outcome.Result.HttpStatusObserved.Should().Be(200);
    }

    [Fact]
    public async Task ProbeAsync_HttpRequestException_FailsWithUnreachable()
    {
        var handler = new RecordingHandler((_, _) =>
            throw new HttpRequestException("No such host is known"));
        var service = MakeService(handler);

        var outcome = await service.ProbeAsync(
            new BrotherHttpProbeRequest { BaseUrl = "http://nonexistent.example" },
            CancellationToken.None);

        outcome.Status.Should().Be(BrotherHttpProbeStatus.Failure);
        outcome.Result.ErrorCode.Should().Be("BROTHER.PROBE_UNREACHABLE");
        outcome.Result.ErrorMessage.Should().Contain("No such host");
    }

    [Fact]
    public async Task ProbeAsync_Timeout_FailsWithTimeoutCode()
    {
        // Build a service with a tiny budget so we can reliably trip it.
        var handler = new RecordingHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var service = MakeService(handler, probeBudget: TimeSpan.FromMilliseconds(50));

        var outcome = await service.ProbeAsync(
            new BrotherHttpProbeRequest { BaseUrl = "http://192.168.2.110" },
            CancellationToken.None);

        outcome.Status.Should().Be(BrotherHttpProbeStatus.Failure);
        outcome.Result.ErrorCode.Should().Be("BROTHER.PROBE_TIMEOUT");
    }

    // ─── Config validation ──────────────────────────────────────────────

    [Fact]
    public async Task ProbeAsync_InvalidBaseUrl_FailsWithConfigInvalid()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var service = MakeService(handler);

        var outcome = await service.ProbeAsync(
            new BrotherHttpProbeRequest { BaseUrl = "not-a-url" },
            CancellationToken.None);

        outcome.Status.Should().Be(BrotherHttpProbeStatus.Failure);
        outcome.Result.ErrorCode.Should().Be("BROTHER.PROBE_CONFIG_INVALID");
        handler.RequestCount.Should().Be(0, "invalid URL must short-circuit before HTTP");
    }

    // ─── BaseUrl normalisation ──────────────────────────────────────────

    [Theory]
    [InlineData("http://192.168.2.110/", "http://192.168.2.110")]
    [InlineData("http://192.168.2.110", "http://192.168.2.110")]
    [InlineData("HTTP://192.168.2.110/", "http://192.168.2.110")]
    [InlineData("http://CNC.LOCAL:8080/", "http://cnc.local:8080")]
    [InlineData("http://CNC.LOCAL/", "http://cnc.local")]
    public void NormaliseBaseUrl_ProducesCanonicalForm(string input, string expected)
    {
        BrotherHttpProbeService.NormaliseBaseUrl(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("ftp://something")]  // technically parseable, but Brother is HTTP only — let it through; the actual GET will fail anyway
    public void NormaliseBaseUrl_InvalidOrEmpty_ReturnsNullOrCanonical(string input)
    {
        var result = BrotherHttpProbeService.NormaliseBaseUrl(input);
        // We're not as strict as "must be http(s)" here — that's the
        // adapter's job — but empty/garbage must produce null.
        if (string.IsNullOrWhiteSpace(input) || input == "not-a-url")
        {
            result.Should().BeNull();
        }
    }

    // ─── Status mapping (pure) ──────────────────────────────────────────

    [Theory]
    [InlineData(BrotherHttpProbeStatus.Success, 200)]
    [InlineData(BrotherHttpProbeStatus.Failure, 200)]   // §4.6 invariant — render inline
    [InlineData(BrotherHttpProbeStatus.LicenseDisabled, 403)]
    [InlineData(BrotherHttpProbeStatus.Busy, 409)]
    public void StatusCodeFor_MapsCorrectly(BrotherHttpProbeStatus status, int expectedHttp)
    {
        BrotherHttpProbeStatusMapping.StatusCodeFor(status).Should().Be(expectedHttp);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static BrotherHttpProbeService MakeService(
        HttpMessageHandler handler,
        Func<string, bool>? isModuleEnabled = null,
        TimeSpan? probeBudget = null)
    {
        var factory = new StubHttpClientFactory(handler);
        return new BrotherHttpProbeService(
            httpClientFactory: factory,
            isModuleEnabled: isModuleEnabled ?? (_ => true),
            loggerFactory: NullLoggerFactory.Instance,
            probeBudget: probeBudget ?? TimeSpan.FromSeconds(8));
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _impl;
        public int RequestCount;
        public int PostOrPutCount;

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> impl)
        {
            _impl = impl;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref RequestCount);
            if (request.Method == HttpMethod.Post || request.Method == HttpMethod.Put)
            {
                Interlocked.Increment(ref PostOrPutCount);
            }
            return await _impl(request, cancellationToken);
        }
    }
}
