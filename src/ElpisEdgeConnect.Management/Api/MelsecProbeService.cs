// ============================================================================
// File: Api/MelsecProbeService.cs
// Purpose: Drives the MELSEC source wizard's two read-only probes:
//            (a) Test connection — open a short-lived SLMP/TCP session and
//                report reachability.
//            (b) Test read — read the selected tag by default, or ALL VALID
//                tags via the scan planner. Invalid tags are skipped and NEVER
//                sent to the PLC; the planned blocks are returned so the UI can
//                show them.
//          Read-only; the probe never mutates running source state. Mirrors
//          S7ProbeService: license-gated on `source-melsec`, per-target
//          single-flight, fixed budget, correlation ProbeId, injectable
//          IMelsecClient factory so tests pin outcomes without a real PLC.
//          No browse / tag-discovery — the service has no such method.
// Reference: docs/sessions/2026-07-01-melsec-wizard-ui-plan-v2.md (§Δ4, Δ5, Δ6)
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Sources.Melsec;
using ElpisEdgeConnect.Sources.Melsec.Decoding;
using ElpisEdgeConnect.Sources.Melsec.Scanning;
using ElpisEdgeConnect.Sources.Melsec.Wire;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>Executes the MELSEC wizard's read-only test-connection / test-read probes.</summary>
public sealed class MelsecProbeService
{
    private const string LicenseModuleKey = LicenseModuleKeys.SourceMelsec;

    /// <summary>Fixed end-to-end probe budget (matches the S7/Modbus probes).</summary>
    internal static readonly TimeSpan DefaultProbeBudget = TimeSpan.FromSeconds(8);

    private readonly Func<MelsecSourceConfiguration, IMelsecClient> _clientFactory;
    private readonly Func<string, bool> _isModuleEnabled;
    private readonly ILogger<MelsecProbeService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _leases = new(StringComparer.Ordinal);
    private readonly TimeSpan _probeBudget;

    /// <summary>Production constructor — builds a real <see cref="SlmpClient"/> per probe, gated on <c>source-melsec</c>.</summary>
    public MelsecProbeService(ILoggerFactory loggerFactory, ILicenseManager? license = null)
        : this(cfg => new SlmpClient(cfg), MqttTestConnectionService.BuildLicenseGate(license), loggerFactory, DefaultProbeBudget)
    {
    }

    /// <summary>Test constructor — injects a fake client factory, license gate, and budget.</summary>
    internal MelsecProbeService(
        Func<MelsecSourceConfiguration, IMelsecClient> clientFactory,
        Func<string, bool> isModuleEnabled,
        ILoggerFactory loggerFactory,
        TimeSpan probeBudget)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(isModuleEnabled);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _clientFactory = clientFactory;
        _isModuleEnabled = isModuleEnabled;
        _logger = loggerFactory.CreateLogger<MelsecProbeService>();
        _probeBudget = probeBudget;
    }

    /// <summary>Test reachability: open a short-lived SLMP/TCP session and report.</summary>
    public async Task<MelsecProbeOutcome> TestConnectionAsync(MelsecTestConnectionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var probeId = GenerateProbeId();
        var sw = Stopwatch.StartNew();

        if (!_isModuleEnabled(LicenseModuleKey)) return LicenseDisabled(probeId, sw);
        if (string.IsNullOrWhiteSpace(request.Host))
            return Failure(probeId, sw, "config-invalid", "MELSEC.PROBE_CONFIG_INVALID", "Host is required.");
        if (!TryResolveProfile(request.Profile, out var profile, out var profileError))
            return Failure(probeId, sw, "config-invalid", "MELSEC.PROBE_CONFIG_INVALID", profileError);

        var leaseKey = LeaseKey(request.Host, request.Port);
        var lease = _leases.GetOrAdd(leaseKey, _ => new SemaphoreSlim(1, 1));
        if (!await lease.WaitAsync(TimeSpan.Zero, ct).ConfigureAwait(false)) return Busy(probeId, sw, leaseKey);

        IMelsecClient? client = null;
        try
        {
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeCts.CancelAfter(_probeBudget);
            client = _clientFactory(BuildConfig(request) with { DeviceProfile = profile.Key });
            var connect = await ConnectAsync(client, request, probeCts.Token).ConfigureAwait(false);

            if (connect.Cancelled)
                return Failure(probeId, sw, "timeout", "MELSEC.PROBE_TIMEOUT", $"Connection to {request.Host}:{request.Port} timed out.");
            if (!connect.Result.IsSuccess)
                return Failure(probeId, sw, "refused", "MELSEC.PROBE_REFUSED", $"Could not connect to {request.Host}:{request.Port}: {connect.Result.Message}");

            return new MelsecProbeOutcome(MelsecProbeStatus.Success,
                Result(probeId, sw, success: true, "reachable", message: $"PLC reachable at {request.Host}:{request.Port}."));
        }
        finally
        {
            Teardown(client, probeId);
            lease.Release();
        }
    }

    /// <summary>
    /// Test read: reads the requested tags (one for "selected", many for
    /// "read all valid") through the scan planner. Invalid tags are skipped and
    /// never sent to the PLC; the planned blocks are returned.
    /// </summary>
    public async Task<MelsecProbeOutcome> TestReadAsync(MelsecTestReadRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var probeId = GenerateProbeId();
        var sw = Stopwatch.StartNew();

        if (!_isModuleEnabled(LicenseModuleKey)) return LicenseDisabled(probeId, sw);
        if (string.IsNullOrWhiteSpace(request.Host))
            return Failure(probeId, sw, "config-invalid", "MELSEC.PROBE_CONFIG_INVALID", "Host is required.");
        if (!TryResolveProfile(request.Profile, out var profile, out var profileError))
            return Failure(probeId, sw, "config-invalid", "MELSEC.PROBE_CONFIG_INVALID", profileError);

        var config = BuildConfig(request) with { DeviceProfile = profile.Key };
        var tagDefs = (request.Tags ?? new List<MelsecProbeTagInput>()).Select(t => new MelsecTagDefinition
        {
            Name = t.Name,
            Address = t.Address,
            Datatype = t.Datatype,
            WordOrder = Enum.TryParse<MelsecWordOrder>(t.WordOrder, ignoreCase: true, out var wo) ? wo : MelsecWordOrder.LowWordFirst,
            ScanRateMs = t.ScanRateMs <= 0 ? 1000 : t.ScanRateMs,
            Scale = t.Scale,
            Offset = t.Offset,
        }).ToList();

        // Plan up front, profile-aware. Invalid tags (incl. devices outside the
        // profile, e.g. ZR on iQ-F) land in plan.Errors → skipped, never sent.
        var plan = MelsecScanPlanner.Build(tagDefs, config.MaxGapWords, config.MaxPointsPerRequest, profile);
        var skipped = plan.Errors.Select(e => new MelsecProbeSkippedTag { Name = e.TagName, Code = e.Code, Message = e.Message }).ToList();
        var blockSummaries = plan.Blocks.Select(b => new MelsecProbeBlockSummary
        {
            Device = b.DeviceSymbol,
            HeadDeviceNumber = b.HeadDeviceNumber,
            WordCount = b.WordCount,
            TagNames = b.Entries.Select(e => e.TagName).ToList(),
        }).ToList();

        if (plan.Blocks.Count == 0)
        {
            // Nothing valid to read — no PLC contact needed.
            var empty = Result(probeId, sw, success: true, "no-valid-tags", message: "No valid tags to read.");
            empty = empty with { Blocks = blockSummaries, Skipped = skipped };
            return new MelsecProbeOutcome(MelsecProbeStatus.Success, empty);
        }

        var leaseKey = LeaseKey(request.Host, request.Port);
        var lease = _leases.GetOrAdd(leaseKey, _ => new SemaphoreSlim(1, 1));
        if (!await lease.WaitAsync(TimeSpan.Zero, ct).ConfigureAwait(false)) return Busy(probeId, sw, leaseKey);

        IMelsecClient? client = null;
        try
        {
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeCts.CancelAfter(_probeBudget);
            client = _clientFactory(config);
            var connect = await ConnectAsync(client, request, probeCts.Token).ConfigureAwait(false);

            if (connect.Cancelled)
                return WithLists(Failure(probeId, sw, "timeout", "MELSEC.PROBE_TIMEOUT", $"Connection to {request.Host}:{request.Port} timed out."), blockSummaries, skipped);
            if (!connect.Result.IsSuccess)
                return WithLists(Failure(probeId, sw, "connect-failed", "MELSEC.PROBE_CONNECT_FAILED", $"Could not connect to {request.Host}:{request.Port}: {connect.Result.Message}"), blockSummaries, skipped);

            var tagResults = new List<MelsecProbeTagResult>();
            foreach (var block in plan.Blocks)
            {
                var read = await client.ReadWordsAsync(block.DeviceCode, block.HeadDeviceNumber, block.WordCount, probeCts.Token).ConfigureAwait(false);
                tagResults.AddRange(DecodeBlock(block, read, request.IncludeRaw));
            }

            var okCount = tagResults.Count(t => t.Quality == "Good");
            var result = Result(probeId, sw, success: true, "read-complete",
                message: $"Read {okCount}/{tagResults.Count} tags; {skipped.Count} skipped as invalid.");
            return new MelsecProbeOutcome(MelsecProbeStatus.Success,
                result with { TagResults = tagResults, Blocks = blockSummaries, Skipped = skipped });
        }
        finally
        {
            Teardown(client, probeId);
            lease.Release();
        }
    }

    private static IEnumerable<MelsecProbeTagResult> DecodeBlock(MelsecScanBlock block, MelsecClientResult read, bool includeRaw)
    {
        foreach (var entry in block.Entries)
        {
            if (read.Status == MelsecClientStatus.Success)
            {
                object? value = null;
                string? decodeError = null;
                try
                {
                    value = MelsecDecoder.Decode(read.WordData.Span, entry.ByteOffset, entry.Datatype, entry.Tag.WordOrder, entry.BitIndex);
                    value = MelsecDecoder.ApplyScaleOffset(value, entry.Tag.Scale, entry.Tag.Offset);
                }
                catch (MelsecDecodeException ex)
                {
                    decodeError = ex.Message;
                }

                yield return decodeError is not null
                    ? new MelsecProbeTagResult { Name = entry.TagName, Address = entry.Address.ToString(), Quality = "Bad", Message = $"decode failed: {decodeError}" }
                    : new MelsecProbeTagResult
                    {
                        Name = entry.TagName,
                        Address = entry.Address.ToString(),
                        Quality = "Good",
                        Value = Convert.ToString(value, CultureInfo.InvariantCulture),
                        RawWordsHex = includeRaw ? RawFor(read.WordData.Span, entry) : null,
                    };
            }
            else
            {
                var (kind, detail) = read.Status == MelsecClientStatus.ProtocolError
                    ? ("protocol", $"end code 0x{read.EndCode:X4}: {MelsecEndCode.Describe(read.EndCode)}")
                    : ("communication", read.Message ?? "read failed");
                yield return new MelsecProbeTagResult
                {
                    Name = entry.TagName,
                    Address = entry.Address.ToString(),
                    Quality = "Bad",
                    EndCode = read.Status == MelsecClientStatus.ProtocolError ? $"0x{read.EndCode:X4}" : null,
                    Message = $"{kind}: {detail}",
                };
            }
        }
    }

    private static string RawFor(ReadOnlySpan<byte> wordData, MelsecScanBlockEntry entry)
    {
        var width = entry.Datatype is MelsecDatatype.Int32 or MelsecDatatype.UInt32 or MelsecDatatype.Float32 ? 4 : 2;
        if (entry.ByteOffset < 0 || entry.ByteOffset + width > wordData.Length) return string.Empty;
        return Convert.ToHexString(wordData.Slice(entry.ByteOffset, width));
    }

    private static MelsecProbeOutcome WithLists(MelsecProbeOutcome outcome, List<MelsecProbeBlockSummary> blocks, List<MelsecProbeSkippedTag> skipped) =>
        new(outcome.Status, outcome.Result with { Blocks = blocks, Skipped = skipped });

    private static async Task<(MelsecClientResult Result, bool Cancelled)> ConnectAsync(IMelsecClient client, MelsecTestConnectionRequest request, CancellationToken probeToken)
    {
        var timeout = TimeSpan.FromMilliseconds(request.ConnectTimeoutMs <= 0 ? 3000 : request.ConnectTimeoutMs);
        try
        {
            var result = await client.ConnectAsync(request.Host, request.Port, timeout, probeToken).ConfigureAwait(false);
            return (result, false);
        }
        catch (OperationCanceledException) when (probeToken.IsCancellationRequested)
        {
            return (MelsecClientResult.Transport("cancelled"), true);
        }
    }

    private static MelsecSourceConfiguration BuildConfig(MelsecTestConnectionRequest r) => new()
    {
        InstanceId = "probe",
        ProtocolName = MelsecSourceWizardModelProtocol,
        DeviceId = "probe",
        Host = r.Host,
        Port = r.Port,
        NetworkNo = (byte)Math.Clamp(r.NetworkNo, 0, 0xFF),
        PcNo = (byte)Math.Clamp(r.PcNo, 0, 0xFF),
        RequestDestModuleIoNo = (ushort)Math.Clamp(r.RequestDestModuleIoNo, 0, 0xFFFF),
        RequestDestModuleStationNo = (byte)Math.Clamp(r.RequestDestModuleStationNo, 0, 0xFF),
        MonitoringTimerMs = r.MonitoringTimerMs <= 0 ? 4000 : r.MonitoringTimerMs,
        ConnectTimeoutMs = r.ConnectTimeoutMs <= 0 ? 3000 : r.ConnectTimeoutMs,
        RequestTimeoutMs = r.RequestTimeoutMs <= 0 ? 5000 : r.RequestTimeoutMs,
    };

    private const string MelsecSourceWizardModelProtocol = "melsec";

    /// <summary>
    /// Resolve a probe request's profile string (A-2O). Absent/blank = Modern
    /// (compatibility). Explicit values must parse AND resolve to an
    /// operator-selectable registry profile; anything else is a typed failure.
    /// </summary>
    internal static bool TryResolveProfile(
        string? profileText,
        out ElpisEdgeConnect.Sources.Melsec.Profiles.MelsecProfileDefinition profile,
        out string error)
    {
        profile = ElpisEdgeConnect.Sources.Melsec.Profiles.MelsecProfiles.Modern;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(profileText))
        {
            return true; // absent = Modern
        }
        if (Enum.TryParse<MelsecDeviceProfile>(profileText, ignoreCase: true, out var key)
            && ElpisEdgeConnect.Sources.Melsec.Profiles.MelsecProfiles.TryResolve(key, out var resolved)
            && resolved.IsOperatorSelectable)
        {
            profile = resolved;
            return true;
        }
        var selectable = string.Join(", ", ElpisEdgeConnect.Sources.Melsec.Profiles.MelsecProfiles.All
            .Where(p => p.IsOperatorSelectable).Select(p => p.Key.ToString()));
        error = $"Profile '{profileText}' is not available (available: {selectable}).";
        return false;
    }

    private void Teardown(IMelsecClient? client, string probeId)
    {
        if (client is null) return;
        try { client.Disconnect(); client.Dispose(); }
        catch (Exception ex) { _logger.LogDebug(ex, "MELSEC probe {ProbeId} teardown raised (non-fatal).", probeId); }
    }

    private static string LeaseKey(string host, int port) => $"{host}:{port}";

    private MelsecProbeOutcome LicenseDisabled(string probeId, Stopwatch sw) =>
        new(MelsecProbeStatus.LicenseDisabled, Result(probeId, sw, success: false, "license-disabled",
            errorCode: "MELSEC.PROBE_LICENSE_DISABLED", message: "Mitsubishi MELSEC source module is not licensed for this gateway."));

    private MelsecProbeOutcome Busy(string probeId, Stopwatch sw, string leaseKey) =>
        new(MelsecProbeStatus.Busy, Result(probeId, sw, success: false, "busy",
            errorCode: "MELSEC.PROBE_BUSY", message: $"Another test is in flight against {leaseKey}."));

    private MelsecProbeOutcome Failure(string probeId, Stopwatch sw, string outcome, string errorCode, string message)
    {
        _logger.LogWarning("MELSEC probe {ProbeId} failed: {Outcome} {ErrorCode} — {Message}", probeId, outcome, errorCode, message);
        return new MelsecProbeOutcome(MelsecProbeStatus.Failure, Result(probeId, sw, success: false, outcome, errorCode: errorCode, message: message));
    }

    private static MelsecProbeResultDto Result(string probeId, Stopwatch sw, bool success, string outcome,
        string? errorCode = null, string? message = null) =>
        new()
        {
            ProbeId = probeId,
            Success = success,
            ElapsedMs = (int)sw.ElapsedMilliseconds,
            Outcome = outcome,
            ErrorCode = errorCode,
            Message = message,
        };

    private static string GenerateProbeId() => string.Concat("melsecprobe-", Guid.NewGuid().ToString("N").AsSpan(0, 8));
}

/// <summary>Service-level status that drives the HTTP status code.</summary>
public enum MelsecProbeStatus
{
    /// <summary>Probe succeeded — HTTP 200.</summary>
    Success,
    /// <summary>Probe ran but connect/read failed — HTTP 200 with Success=false.</summary>
    Failure,
    /// <summary>License gate refused — HTTP 403.</summary>
    LicenseDisabled,
    /// <summary>Single-flight lease busy — HTTP 409.</summary>
    Busy,
}

/// <summary>Reachability probe request — connection parameters only.</summary>
public record MelsecTestConnectionRequest
{
    /// <summary>PLC / Ethernet-module IP or hostname.</summary>
    public required string Host { get; init; }
    /// <summary>MC/SLMP TCP port.</summary>
    public int Port { get; init; }
    /// <summary>Network number (0–255).</summary>
    public int NetworkNo { get; init; }
    /// <summary>PC number (0–255).</summary>
    public int PcNo { get; init; } = 0xFF;
    /// <summary>Request destination module I/O number (0–65535).</summary>
    public int RequestDestModuleIoNo { get; init; } = 0x03FF;
    /// <summary>Request destination module station number (0–255).</summary>
    public int RequestDestModuleStationNo { get; init; }
    /// <summary>Device monitoring timer (ms).</summary>
    public int MonitoringTimerMs { get; init; } = 4000;
    /// <summary>Connect timeout (ms).</summary>
    public int ConnectTimeoutMs { get; init; } = 3000;
    /// <summary>Per-read socket timeout (ms).</summary>
    public int RequestTimeoutMs { get; init; } = 5000;
    /// <summary>PLC family profile (A-2O). Absent/blank = Modern (compatibility
    /// with existing probe clients); explicit values must resolve to an
    /// operator-selectable registry profile.</summary>
    public string? Profile { get; init; }
}

/// <summary>One tag for the read probe.</summary>
public sealed record MelsecProbeTagInput
{
    /// <summary>Tag name.</summary>
    public required string Name { get; init; }
    /// <summary>MELSEC device address.</summary>
    public required string Address { get; init; }
    /// <summary>Datatype hint.</summary>
    public string? Datatype { get; init; }
    /// <summary>Word order for 32-bit values.</summary>
    public string? WordOrder { get; init; }
    /// <summary>Scan rate (ms).</summary>
    public int ScanRateMs { get; init; } = 1000;
    /// <summary>Optional scale.</summary>
    public double? Scale { get; init; }
    /// <summary>Optional offset.</summary>
    public double? Offset { get; init; }
}

/// <summary>Read probe request — connection parameters + the tags to read.</summary>
public sealed record MelsecTestReadRequest : MelsecTestConnectionRequest
{
    /// <summary>Tags to read (one for "selected", many for "read all valid").</summary>
    public List<MelsecProbeTagInput> Tags { get; init; } = new();
    /// <summary>Max word gap for planning.</summary>
    public int MaxGapWords { get; init; } = 8;
    /// <summary>Max points per request for planning.</summary>
    public int MaxPointsPerRequest { get; init; } = 480;
    /// <summary>Include raw word payload per tag (Advanced only; off by default).</summary>
    public bool IncludeRaw { get; init; }
}

/// <summary>Per-tag read result.</summary>
public sealed record MelsecProbeTagResult
{
    /// <summary>Tag name.</summary>
    public required string Name { get; init; }
    /// <summary>Resolved address.</summary>
    public string? Address { get; init; }
    /// <summary>Quality — "Good" or "Bad".</summary>
    public required string Quality { get; init; }
    /// <summary>Decoded value on success.</summary>
    public string? Value { get; init; }
    /// <summary>MELSEC end code (hex) on a protocol failure.</summary>
    public string? EndCode { get; init; }
    /// <summary>Diagnostic message on failure.</summary>
    public string? Message { get; init; }
    /// <summary>Raw word bytes (hex) — only when explicitly requested.</summary>
    public string? RawWordsHex { get; init; }
}

/// <summary>Summary of one planned read block.</summary>
public sealed record MelsecProbeBlockSummary
{
    /// <summary>Device symbol.</summary>
    public required string Device { get; init; }
    /// <summary>Head device number.</summary>
    public required int HeadDeviceNumber { get; init; }
    /// <summary>Returned word count.</summary>
    public required int WordCount { get; init; }
    /// <summary>Tags satisfied by this block.</summary>
    public required IReadOnlyList<string> TagNames { get; init; }
}

/// <summary>A tag skipped as invalid (never sent to the PLC).</summary>
public sealed record MelsecProbeSkippedTag
{
    /// <summary>Tag name.</summary>
    public required string Name { get; init; }
    /// <summary>Typed error code.</summary>
    public required string Code { get; init; }
    /// <summary>Reason.</summary>
    public required string Message { get; init; }
}

/// <summary>Result DTO for a MELSEC probe.</summary>
public sealed record MelsecProbeResultDto
{
    /// <summary>Correlation id.</summary>
    public required string ProbeId { get; init; }
    /// <summary>True when the probe reached its success outcome.</summary>
    public required bool Success { get; init; }
    /// <summary>Wall-clock duration (ms).</summary>
    public required int ElapsedMs { get; init; }
    /// <summary>Stable outcome token.</summary>
    public required string Outcome { get; init; }
    /// <summary>Error code when unsuccessful.</summary>
    public string? ErrorCode { get; init; }
    /// <summary>Friendly message.</summary>
    public string? Message { get; init; }
    /// <summary>Per-tag results (read probe).</summary>
    public IReadOnlyList<MelsecProbeTagResult> TagResults { get; init; } = Array.Empty<MelsecProbeTagResult>();
    /// <summary>Planned blocks (read probe).</summary>
    public IReadOnlyList<MelsecProbeBlockSummary> Blocks { get; init; } = Array.Empty<MelsecProbeBlockSummary>();
    /// <summary>Skipped invalid tags (read probe).</summary>
    public IReadOnlyList<MelsecProbeSkippedTag> Skipped { get; init; } = Array.Empty<MelsecProbeSkippedTag>();
}

/// <summary>Service-layer outcome — DTO plus the status enum for HTTP mapping.</summary>
public sealed record MelsecProbeOutcome(MelsecProbeStatus Status, MelsecProbeResultDto Result);
