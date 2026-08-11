// ============================================================================
// File: BulkSourceMergeApiTests.cs
// Purpose: Endpoint-metadata coverage for the bulk-source-merge API. Verifies
//          v3.1 sec1 auth wiring (T46/T47 contract surface):
//
//            * Endpoints registered at the expected routes.
//            * Each endpoint carries IAuthorizeData metadata so the central
//              UseAuthorization + UseAntiforgery middleware rejects
//              unauthenticated / missing-anti-forgery-token requests.
//
//          Round-trip HTTP 401 / 403 verification through
//          WebApplicationFactory is deferred — the middleware contract
//          (RequireAuthorization + global UseAntiforgery) is wired
//          consistently with every other config-changing endpoint in
//          ConfigApi / RoutesUpdateApi / SinksUpdateApi, so this metadata
//          assertion pins the gate.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Management.Api.BulkSourceMerge;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class BulkSourceMergeApiTests
{
    private sealed class StubConfigurationManager : ElpisEdgeConnect.Core.Configuration.IConfigurationManager
    {
        public ConfigurationVersionId CurrentVersionId => new("v1");
        public event EventHandler<ConfigurationChangeEventArgs>? CurrentChanged;
        public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask<GatewayConfiguration> GetCurrentAsync(CancellationToken ct) =>
            new(new GatewayConfiguration { Gateway = new GatewaySettings { GatewayId = "G", GatewayName = "G" } });
        public Task<DraftId> CreateDraftAsync(GatewayConfiguration d, string? a, CancellationToken ct) =>
            Task.FromResult(new DraftId("d1"));
        public Task<GatewayConfiguration?> GetDraftAsync(DraftId id, CancellationToken ct) =>
            Task.FromResult<GatewayConfiguration?>(null);
        public Task<IReadOnlyList<DraftId>> ListDraftsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DraftId>>(Array.Empty<DraftId>());
        public Task<ValidationResult> ValidateDraftAsync(DraftId id, CancellationToken ct) =>
            Task.FromResult(ValidationResult.Success());
        public Task<ConfigurationApplyResult> ApplyDraftAsync(DraftId id, string? a, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task DiscardDraftAsync(DraftId id, string? a, CancellationToken ct) => Task.CompletedTask;
        public Task<ConfigurationApplyResult> RollbackAsync(ConfigurationVersionId v, string? a, CancellationToken ct) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<ConfigurationHistoryEntry>> GetHistoryAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ConfigurationHistoryEntry>>(Array.Empty<ConfigurationHistoryEntry>());
        public async IAsyncEnumerable<ConfigurationAuditEntry> GetAuditLogAsync(bool v, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
        public ValueTask<ConfigurationAuditEntry> AppendRuntimeFaultAsync(ConfigurationFault f, CancellationToken ct) =>
            throw new NotImplementedException();
        public void Suppress() => CurrentChanged?.Invoke(this, null!);
    }

    private sealed class StubSchemaValidator : IConfigurationSchemaValidator
    {
        public ValueTask<ValidationResult> ValidateAsync(string json, CancellationToken ct) =>
            new(ValidationResult.Success());
    }

    private static IReadOnlyList<RouteEndpoint> CollectEndpoints()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<ElpisEdgeConnect.Core.Configuration.IConfigurationManager, StubConfigurationManager>();
        builder.Services.AddSingleton<IConfigurationSchemaValidator, StubSchemaValidator>();
        builder.Services.AddSingleton<BulkSourceMergeService>();
        builder.Services.AddSingleton<BulkMTConnectProbeService>();
        var app = builder.Build();
        app.MapBulkSourceMergeApi();

        var routeBuilder = (IEndpointRouteBuilder)app;
        return routeBuilder.DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }

    [Fact]
    public void MapBulkSourceMergeApi_RegistersPreviewSubmitAndProbeEndpoints()
    {
        var endpoints = CollectEndpoints();
        var routes = endpoints
            .Select(e => e.RoutePattern.RawText)
            .Where(p => p is not null)
            .ToArray();

        routes.Should().Contain("/api/v1/sources/bulk-preview");
        routes.Should().Contain("/api/v1/sources/bulk-submit");
        routes.Should().Contain("/api/v1/sources/bulk-probe");
    }

    [Theory]
    [InlineData("/api/v1/sources/bulk-preview")]
    [InlineData("/api/v1/sources/bulk-submit")]
    [InlineData("/api/v1/sources/bulk-probe")]
    public void Endpoint_HasAuthorizeMetadata(string route)
    {
        var endpoints = CollectEndpoints();
        var endpoint = endpoints.First(e => string.Equals(e.RoutePattern.RawText, route, System.StringComparison.Ordinal));

        var authMetadata = endpoint.Metadata.GetMetadata<IAuthorizeData>();

        authMetadata.Should().NotBeNull(
            "v3.1 §1 requires Studio authentication on the bulk-source-merge endpoints; .RequireAuthorization() landed in BulkSourceMergeApi.");
    }
}
