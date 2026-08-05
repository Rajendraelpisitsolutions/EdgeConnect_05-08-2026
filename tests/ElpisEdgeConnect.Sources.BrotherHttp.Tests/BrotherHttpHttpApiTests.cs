// ============================================================================
// Tests: BrotherHttpHttpApi — construction validation + URL formation +
//        transient-failure semantics. Uses a stub HttpMessageHandler /
//        IHttpClientFactory rather than a real HTTP listener (step-8
//        parity tests use HttpListener; step-3 tests stay in-process).
// Reference: src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherHttpHttpApi.cs
// ============================================================================

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.BrotherHttp.Tests;

public sealed class BrotherHttpHttpApiTests
{
    [Fact]
    public void Constructor_NullFactory_Throws()
    {
        Action act = () => _ = new BrotherHttpHttpApi(
            factory: null!,
            baseUrl: "http://example",
            timeoutSeconds: 5,
            instanceId: "id",
            logger: NullLogger.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("factory");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyBaseUrl_Throws(string? baseUrl)
    {
        Action act = () => _ = new BrotherHttpHttpApi(
            factory: new StubHttpClientFactory(),
            baseUrl: baseUrl!,
            timeoutSeconds: 5,
            instanceId: "id",
            logger: NullLogger.Instance);

        act.Should().Throw<ArgumentException>().WithParameterName("baseUrl");
    }

    [Fact]
    public void Constructor_NonAbsoluteBaseUrl_Throws()
    {
        Action act = () => _ = new BrotherHttpHttpApi(
            factory: new StubHttpClientFactory(),
            baseUrl: "not-a-valid-url",
            timeoutSeconds: 5,
            instanceId: "id",
            logger: NullLogger.Instance);

        act.Should().Throw<ArgumentException>().WithParameterName("baseUrl");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveTimeout_Throws(int timeoutSeconds)
    {
        Action act = () => _ = new BrotherHttpHttpApi(
            factory: new StubHttpClientFactory(),
            baseUrl: "http://example",
            timeoutSeconds: timeoutSeconds,
            instanceId: "id",
            logger: NullLogger.Instance);

        act.Should().Throw<ArgumentException>().WithParameterName("timeoutSeconds");
    }

    [Fact]
    public void Constructor_BaseUrlIsNormalised_TrailingSlashAdded()
    {
        var api = new BrotherHttpHttpApi(
            factory: new StubHttpClientFactory(),
            baseUrl: "http://192.168.2.110",   // no trailing slash
            timeoutSeconds: 5,
            instanceId: "id",
            logger: NullLogger.Instance);

        api.BaseAddressForTesting.AbsoluteUri.Should().Be("http://192.168.2.110/");
    }

    [Fact]
    public void Constructor_BaseUrlIsNormalised_ExistingTrailingSlashPreserved()
    {
        var api = new BrotherHttpHttpApi(
            factory: new StubHttpClientFactory(),
            baseUrl: "http://192.168.2.110/",  // already has trailing slash
            timeoutSeconds: 5,
            instanceId: "id",
            logger: NullLogger.Instance);

        api.BaseAddressForTesting.AbsoluteUri.Should().Be("http://192.168.2.110/");
    }

    [Theory]
    [InlineData(nameof(BrotherHttpHttpApi.GetMachineInfoAsync), "HTTPD_MCNINFO")]
    [InlineData(nameof(BrotherHttpHttpApi.GetCycleTimeAsync), "MNTP_CYCLETIME")]
    [InlineData(nameof(BrotherHttpHttpApi.GetWorkCountersAsync), "MNTP_WKCNTR")]
    [InlineData(nameof(BrotherHttpHttpApi.GetAtcToolsAsync), "ATC_TOOLS")]
    [InlineData(nameof(BrotherHttpHttpApi.GetAlarmsAsync), "ALARM_CURALMLIST")]
    [InlineData(nameof(BrotherHttpHttpApi.GetMaintenanceNoticesAsync), "MNTP_MAINTNOTICE")]
    public async Task EndpointCall_SendsGetToExpectedPath(string methodName, string expectedPath)
    {
        var factory = new StubHttpClientFactory();
        factory.Handler.NextResponse = OkResponse("payload-body");
        var api = NewApi(factory);

        await InvokeAsync(api, methodName);

        factory.Handler.Requests.Should().HaveCount(1);
        var req = factory.Handler.Requests[0];
        req.Method.Should().Be(HttpMethod.Get);
        req.RequestUri!.AbsoluteUri.Should().Be($"http://192.168.2.110/{expectedPath}");
    }

    [Fact]
    public async Task EndpointCall_OnSuccess_ReturnsResponseBody()
    {
        var factory = new StubHttpClientFactory();
        factory.Handler.NextResponse = OkResponse("BRN68E74A6608EA,SXd1,3,01,0,1");
        var api = NewApi(factory);

        var result = await api.GetMachineInfoAsync(CancellationToken.None);

        result.Should().Be("BRN68E74A6608EA,SXd1,3,01,0,1");
    }

    [Fact]
    public async Task EndpointCall_OnHttpFailure_ReturnsNull()
    {
        var factory = new StubHttpClientFactory();
        factory.Handler.NextResponse = new HttpResponseMessage(HttpStatusCode.NotFound);
        var api = NewApi(factory);

        var result = await api.GetMachineInfoAsync(CancellationToken.None);

        result.Should().BeNull("HTTP 404 is a transient failure — returns null per IBrotherHttpApi contract");
    }

    [Fact]
    public async Task EndpointCall_OnTransportException_ReturnsNull()
    {
        var factory = new StubHttpClientFactory();
        factory.Handler.ThrowOnSend = new HttpRequestException("connection refused");
        var api = NewApi(factory);

        var result = await api.GetMachineInfoAsync(CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task EndpointCall_CooperativeCancellation_Propagates()
    {
        var factory = new StubHttpClientFactory();
        factory.Handler.ThrowOnSend = new TaskCanceledException("cancelled");
        var api = NewApi(factory);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => api.GetMachineInfoAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ─── helpers ──────────────────────────────────────────────────────────

    private static BrotherHttpHttpApi NewApi(StubHttpClientFactory factory) =>
        new(factory, baseUrl: "http://192.168.2.110", timeoutSeconds: 5,
            instanceId: "brother-demo", logger: NullLogger.Instance);

    private static HttpResponseMessage OkResponse(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    private static Task<string?> InvokeAsync(BrotherHttpHttpApi api, string methodName) =>
        methodName switch
        {
            nameof(BrotherHttpHttpApi.GetMachineInfoAsync) => api.GetMachineInfoAsync(CancellationToken.None),
            nameof(BrotherHttpHttpApi.GetCycleTimeAsync) => api.GetCycleTimeAsync(CancellationToken.None),
            nameof(BrotherHttpHttpApi.GetWorkCountersAsync) => api.GetWorkCountersAsync(CancellationToken.None),
            nameof(BrotherHttpHttpApi.GetAtcToolsAsync) => api.GetAtcToolsAsync(CancellationToken.None),
            nameof(BrotherHttpHttpApi.GetAlarmsAsync) => api.GetAlarmsAsync(CancellationToken.None),
            nameof(BrotherHttpHttpApi.GetMaintenanceNoticesAsync) => api.GetMaintenanceNoticesAsync(CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(methodName), methodName, "unknown endpoint method"),
        };
}

internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    public StubHttpMessageHandler Handler { get; } = new();

    public HttpClient CreateClient(string name) => new(Handler, disposeHandler: false);
}

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = new();
    public HttpResponseMessage? NextResponse { get; set; }
    public Exception? ThrowOnSend { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        if (ThrowOnSend is not null) throw ThrowOnSend;
        return Task.FromResult(NextResponse ?? new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(""),
        });
    }
}
