// ============================================================================
// File: Api/EthernetIpBrowseApi.cs
// Purpose: POST /api/v1/sources/browse/ethernetip/tags — ask a controller which
//          symbols it publishes, so the operator configures addresses from the
//          device's own list instead of guessing.
//
//          Before this endpoint the EtherNet/IP wizard offered only test-read:
//          you had to already know an address to check it. When a controller
//          publishes nothing you expect, test-read reports TAG_DEFINITION_INVALID
//          for every guess and gives no way to discover the truth. Listing turns
//          that into a single call.
//
//          Read-only, license-gated on source-ethernet-ip, single-flight per
//          target, and bounded by the same probe budget as test-read.
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Sources.EthernetIp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>Endpoint registration for the EtherNet/IP tag browser.</summary>
public static class EthernetIpBrowseApi
{
    /// <summary>Map the v1 EtherNet/IP tag-listing endpoint onto <paramref name="builder"/>.</summary>
    public static IEndpointRouteBuilder MapEthernetIpBrowseApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var group = builder.MapGroup("/api/v1/sources/browse/ethernetip").WithTags("Sources");

        group.MapPost("/tags", async (
            EthernetIpBrowseTagsRequest request,
            EthernetIpTagBrowseService service,
            ILogger<EthernetIpTagBrowseService> logger,
            CancellationToken ct) =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Host))
            {
                return Results.BadRequest(new { error = "host is required." });
            }

            EthernetIpBrowseOutcome outcome;
            try
            {
                outcome = await service.ListTagsAsync(request, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error driving EthernetIpTagBrowseService.ListTagsAsync");
                return Results.Problem(
                    title: "EtherNet/IP tag listing failed unexpectedly.",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            return Results.Json(outcome.Result, statusCode: EthernetIpProbeStatusMapping.StatusCodeFor(outcome.Status));
        })
        .WithName("BrowseEthernetIpTags")
        .WithSummary("List every symbol the controller publishes (read-only). Use this to discover valid addresses.")
        .Produces<EthernetIpBrowseResultDto>(StatusCodes.Status200OK)
        .Produces<EthernetIpBrowseResultDto>(StatusCodes.Status403Forbidden)
        .Produces<EthernetIpBrowseResultDto>(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status500InternalServerError);

        return builder;
    }
}

/// <summary>Drives the read-only EtherNet/IP tag listing. Singleton.</summary>
public sealed class EthernetIpTagBrowseService
{
    private const string LicenseModuleKey = LicenseModuleKeys.SourceEthernetIp;

    private readonly Func<IEthernetIpTagBrowser> _browserFactory;
    private readonly Func<string, bool> _isModuleEnabled;
    private readonly ILogger<EthernetIpTagBrowseService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _leases = new(StringComparer.Ordinal);
    private readonly TimeSpan _budget;

    /// <summary>Production constructor — real libplctag browser, gated on source-ethernet-ip.</summary>
    public EthernetIpTagBrowseService(ILoggerFactory loggerFactory, ILicenseManager? license = null)
        : this(
            browserFactory: () => new LibPlcTagTagBrowser(),
            isModuleEnabled: MqttTestConnectionService.BuildLicenseGate(license),
            loggerFactory: loggerFactory,
            budget: EthernetIpProbeService.DefaultProbeBudget)
    {
    }

    /// <summary>Test-only constructor — injects a fake browser, license gate and budget.</summary>
    internal EthernetIpTagBrowseService(
        Func<IEthernetIpTagBrowser> browserFactory,
        Func<string, bool> isModuleEnabled,
        ILoggerFactory loggerFactory,
        TimeSpan budget)
    {
        ArgumentNullException.ThrowIfNull(browserFactory);
        ArgumentNullException.ThrowIfNull(isModuleEnabled);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _browserFactory = browserFactory;
        _isModuleEnabled = isModuleEnabled;
        _logger = loggerFactory.CreateLogger<EthernetIpTagBrowseService>();
        _budget = budget;
    }

    /// <summary>List the controller's published symbols. Read-only.</summary>
    public async Task<EthernetIpBrowseOutcome> ListTagsAsync(EthernetIpBrowseTagsRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sw = Stopwatch.StartNew();

        if (!_isModuleEnabled(LicenseModuleKey))
        {
            return Fail(EthernetIpProbeStatus.LicenseDisabled, sw, "ETHERNETIP.BROWSE_LICENSE_DISABLED",
                "The EtherNet/IP source module is not enabled by the current license.");
        }
        if (string.IsNullOrWhiteSpace(request.Host))
        {
            return Fail(EthernetIpProbeStatus.Failure, sw, "ETHERNETIP.BROWSE_CONFIG_INVALID", "Host is required.");
        }

        var cpuFamily = EthernetIpCpuFamilyExtensions.ParseOrNull(request.CpuFamily) ?? EthernetIpCpuFamily.ControlLogix;
        var path = request.Path ?? cpuFamily.DefaultPath();

        var leaseKey = $"{request.Host}|{path}|@tags";
        var lease = _leases.GetOrAdd(leaseKey, _ => new SemaphoreSlim(1, 1));
        if (!await lease.WaitAsync(TimeSpan.Zero, ct).ConfigureAwait(false))
        {
            return Fail(EthernetIpProbeStatus.Busy, sw, "ETHERNETIP.BROWSE_BUSY",
                "A tag listing for this controller is already running.");
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_budget);

            var parameters = new EthernetIpConnectionParameters
            {
                Host = request.Host,
                Path = path,
                CpuFamily = cpuFamily,
                ConnectTimeout = TimeSpan.FromMilliseconds(request.ConnectTimeoutMs <= 0 ? 2000 : request.ConnectTimeoutMs),
                RequestTimeout = TimeSpan.FromMilliseconds(request.ConnectTimeoutMs <= 0 ? 2000 : request.ConnectTimeoutMs),
            };

            IReadOnlyList<EthernetIpSymbol> symbols;
            try
            {
                symbols = await _browserFactory().ListTagsAsync(parameters, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                return Fail(EthernetIpProbeStatus.Failure, sw, "ETHERNETIP.BROWSE_TIMEOUT",
                    $"Listing tags on {request.Host} timed out.");
            }
            catch (EthernetIpFatalException ex)
            {
                return Fail(EthernetIpProbeStatus.Failure, sw, "ETHERNETIP.BROWSE_FAILED", ex.Message);
            }

            var userCount = symbols.Count(s => !s.IsBuiltIn);
            _logger.LogInformation(
                "EtherNet/IP tag listing on {Host}: {Total} symbol(s), {User} from the controller project, in {Elapsed}ms.",
                request.Host, symbols.Count, userCount, sw.ElapsedMilliseconds);

            return new EthernetIpBrowseOutcome(EthernetIpProbeStatus.Success, new EthernetIpBrowseResultDto
            {
                Success = true,
                ElapsedMs = (int)sw.ElapsedMilliseconds,
                TotalCount = symbols.Count,
                BuiltInCount = symbols.Count - userCount,
                UserCount = userCount,
                Message = userCount == 0
                    // The exact situation that caused the incident. Say it plainly
                    // rather than leaving the operator to infer it from an empty list.
                    ? $"{symbols.Count} symbol(s), all controller built-ins. This controller publishes "
                      + "no variables from its own project — on Micro800, a variable is only published "
                      + "when it is declared as a Global Variable and downloaded."
                    : $"{symbols.Count} symbol(s), {userCount} from the controller project.",
                Tags = symbols.Select(s => new EthernetIpSymbolDto
                {
                    Name = s.Name,
                    Datatype = s.Datatype,
                    CipTypeCode = $"0x{s.CipTypeCode:X4}",
                    ElementLength = s.ElementLength,
                    IsStructure = s.IsStructure,
                    IsBuiltIn = s.IsBuiltIn,
                }).ToList(),
            });
        }
        finally
        {
            lease.Release();
        }
    }

    private static EthernetIpBrowseOutcome Fail(
        EthernetIpProbeStatus status, Stopwatch sw, string errorCode, string message) =>
        new(status, new EthernetIpBrowseResultDto
        {
            Success = false,
            ElapsedMs = (int)sw.ElapsedMilliseconds,
            ErrorCode = errorCode,
            Message = message,
            Tags = [],
        });
}

/// <summary>Request for a read-only EtherNet/IP tag listing.</summary>
public sealed record EthernetIpBrowseTagsRequest
{
    /// <summary>Gateway IP / hostname of the controller.</summary>
    public required string Host { get; init; }

    /// <summary>CIP routing path. When null, derived from the CPU family.</summary>
    public string? Path { get; init; }

    /// <summary>CPU family (ControlLogix / CompactLogix / Micro800 / …).</summary>
    public string CpuFamily { get; init; } = "ControlLogix";

    /// <summary>Connect timeout in milliseconds.</summary>
    public int ConnectTimeoutMs { get; init; } = 2000;
}

/// <summary>One symbol in the listing response.</summary>
public sealed record EthernetIpSymbolDto
{
    /// <summary>Symbol name — copy this verbatim into the tag address.</summary>
    public required string Name { get; init; }

    /// <summary>Datatype token to configure, or null when the type has no atomic equivalent.</summary>
    public string? Datatype { get; init; }

    /// <summary>Raw CIP type code, for diagnostics.</summary>
    public required string CipTypeCode { get; init; }

    /// <summary>Element size in bytes.</summary>
    public required int ElementLength { get; init; }

    /// <summary>True when the symbol is a structure/UDT rather than an atomic value.</summary>
    public bool IsStructure { get; init; }

    /// <summary>True for controller built-ins rather than project variables.</summary>
    public bool IsBuiltIn { get; init; }
}

/// <summary>Result DTO for an EtherNet/IP tag listing.</summary>
public sealed record EthernetIpBrowseResultDto
{
    /// <summary>True when the listing succeeded.</summary>
    public required bool Success { get; init; }

    /// <summary>Wall-clock duration in milliseconds.</summary>
    public required int ElapsedMs { get; init; }

    /// <summary>Machine-readable error code when <see cref="Success"/> is false.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Operator-facing summary.</summary>
    public string? Message { get; init; }

    /// <summary>Total symbols published.</summary>
    public int TotalCount { get; init; }

    /// <summary>How many are controller built-ins (_IO_EM_* / __SYSVA_*).</summary>
    public int BuiltInCount { get; init; }

    /// <summary>How many come from the operator's own controller project.</summary>
    public int UserCount { get; init; }

    /// <summary>The symbols themselves.</summary>
    public required IReadOnlyList<EthernetIpSymbolDto> Tags { get; init; }
}

/// <summary>Service-level outcome pairing a status with its DTO.</summary>
public sealed record EthernetIpBrowseOutcome(EthernetIpProbeStatus Status, EthernetIpBrowseResultDto Result);
