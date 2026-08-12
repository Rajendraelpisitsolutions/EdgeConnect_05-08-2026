// ============================================================================
// File: Api/LicenseActivationService.cs
// Purpose: Backs the Studio "License" page. Reads the live license status
//          (GetStatus) and activates an uploaded license (validate signature ->
//          save to the license path -> hot-reload the live ILicenseManager).
// Reference: docs/licensing/licensing-complete-guide.md,
//            docs/decisions/0035-unlicensed-runtime-cutoff.md
// ============================================================================

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Licensing;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Read-only projection of the current license state for the Studio UI.
/// </summary>
public sealed record LicenseStatusDto
{
    /// <summary>Lifecycle status name (<c>Valid</c>, <c>NotLoaded</c>, <c>Expired</c>, …).</summary>
    public required string Status { get; init; }

    /// <summary>True only when a currently-valid license is loaded.</summary>
    public required bool IsValid { get; init; }

    /// <summary>Edition label, or <c>null</c> when no license is loaded.</summary>
    public string? Edition { get; init; }

    /// <summary>Licensed customer name, or <c>null</c>.</summary>
    public string? Customer { get; init; }

    /// <summary>Gateway id the license is bound to, or <c>null</c>.</summary>
    public string? GatewayId { get; init; }

    /// <summary>
    /// This gateway's own identity id — the value a license must be issued for
    /// (single-machine binding, ADR-0036). Shown so operators know which id to
    /// request a license for. <c>null</c> before the identity is established.
    /// </summary>
    public string? GatewayIdentity { get; init; }

    /// <summary>Expiry date (<c>yyyy-MM-dd</c> UTC), or <c>null</c>.</summary>
    public string? ExpiresAtUtc { get; init; }

    /// <summary>Whole days until expiry (negative once expired), or <c>null</c>.</summary>
    public int? DaysRemaining { get; init; }

    /// <summary>
    /// Whether the UI should show the Activate License feature. Always
    /// <c>false</c> while <see cref="ClockTampered"/> — see the visibility
    /// rules in <see cref="LicenseActivationService.GetStatus"/>.
    /// </summary>
    public required bool ShowActivate { get; init; }

    /// <summary>
    /// Whether the UI should show the Buy License feature. Always
    /// <c>false</c> while <see cref="ClockTampered"/> — see the visibility
    /// rules in <see cref="LicenseActivationService.GetStatus"/>.
    /// </summary>
    public required bool ShowBuy { get; init; }

    /// <summary>Destination the Buy License button opens.</summary>
    public required string BuyUrl { get; init; }

    /// <summary>
    /// Demo data-collection budget remaining, in seconds, before data collection
    /// stops (ADR-0035). This budget is consumed only while a source is actively
    /// collecting data. <c>null</c> when a valid license is present.
    /// </summary>
    public int? TrialSecondsRemaining { get; init; }

    /// <summary>
    /// True when the demo budget is actively counting down right now (a source is
    /// collecting data). False when paused — nothing is currently collecting, so
    /// the remaining time is frozen.
    /// </summary>
    public bool DemoCounting { get; init; }

    /// <summary>True once the demo budget elapsed and data collection was stopped.</summary>
    public bool DataStopped { get; init; }

    /// <summary>Running product version (e.g. <c>1.2.0</c>).</summary>
    public string? ProductVersion { get; init; }

    /// <summary>Release date of the running build (<c>yyyy-MM-dd</c> UTC).</summary>
    public string? ProductBuildDate { get; init; }

    /// <summary>
    /// True when a valid license is within 30 days of expiry — surface the
    /// renewal reminder while still allowing normal usage.
    /// </summary>
    public bool ExpiringSoon { get; init; }

    /// <summary>
    /// True when this build is newer than the (expired) license covers — the
    /// version is restricted and requires a renewed/upgraded license.
    /// </summary>
    public bool VersionRestricted { get; init; }

    /// <summary>
    /// True when the system date/time appears to have been rolled back
    /// (tampered) — the license is not honoured and the runtime is in demo mode
    /// until the clock is corrected.
    /// </summary>
    public bool ClockTampered { get; init; }
}

/// <summary>Outcome of an activation attempt.</summary>
public sealed record LicenseActivationResult
{
    /// <summary>True when the license validated, saved, and reloaded.</summary>
    public required bool Success { get; init; }

    /// <summary>Human-readable reason when <see cref="Success"/> is false.</summary>
    public string? Error { get; init; }

    /// <summary>True when a service restart is advised to start newly-licensed protocols.</summary>
    public bool RestartRecommended { get; init; }

    /// <summary>Refreshed status after a successful activation.</summary>
    public LicenseStatusDto? Status { get; init; }
}

/// <summary>
/// Validates, persists, and hot-reloads uploaded licenses, and projects the
/// current license status for the Studio. Singleton.
/// </summary>
public sealed class LicenseActivationService
{
    /// <summary>Fallback Buy License destination when none is configured.</summary>
    public const string DefaultBuyUrl = "https://elpisitsolutions.com/edgeconnect";

    private readonly ILicenseManager _license;
    private readonly ILogger<LicenseActivationService> _logger;
    private readonly string _licensePath;
    private readonly string _buyUrl;
    private readonly ElpisEdgeConnect.Host.LicenseTrialState _trialState;

    /// <summary>Construct the service.</summary>
    /// <param name="license">The live, shared license manager (hot-reload target).</param>
    /// <param name="logger">Logger.</param>
    /// <param name="licensePath">Absolute path the license file is written to.</param>
    /// <param name="buyUrl">Buy License destination; falls back to <see cref="DefaultBuyUrl"/>.</param>
    /// <param name="trialState">Shared demo-countdown state (ADR-0035).</param>
    public LicenseActivationService(
        ILicenseManager license,
        ILogger<LicenseActivationService> logger,
        string licensePath,
        string? buyUrl,
        ElpisEdgeConnect.Host.LicenseTrialState trialState)
    {
        ArgumentNullException.ThrowIfNull(license);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(licensePath);
        ArgumentNullException.ThrowIfNull(trialState);

        _license = license;
        _logger = logger;
        _licensePath = licensePath;
        _buyUrl = string.IsNullOrWhiteSpace(buyUrl) ? DefaultBuyUrl : buyUrl;
        _trialState = trialState;
    }

    /// <summary>The path the license file is read from / written to.</summary>
    public string LicensePath => _licensePath;

    /// <summary>Project the current license state for the UI.</summary>
    public LicenseStatusDto GetStatus()
    {
        var current = _license.Current;
        var status = _license.Status;
        var isValid = status == LicenseStatus.Valid;
        // Read once: the enforcer writes this from its own thread, and the
        // visibility rules below must agree with the ClockTampered they ship.
        var clockTampered = _trialState.ClockTampered;
        int? daysRemaining = current is null
            ? null
            : (int)Math.Floor((current.ExpiresAt - DateTime.UtcNow).TotalDays);

        return new LicenseStatusDto
        {
            Status = status.ToString(),
            IsValid = isValid,
            Edition = current?.Edition.ToString(),
            Customer = current?.Customer,
            GatewayId = current?.GatewayId,
            GatewayIdentity = LocalGatewayId(),
            ExpiresAtUtc = current?.ExpiresAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DaysRemaining = daysRemaining,
            // Visibility rules:
            //  * Activate — shown whenever there is no valid license (upload a
            //    new or renewed license.json).
            //  * Buy — shown ONLY when there is NO license at all (NotLoaded /
            //    Invalid). When a license exists but is EXPIRED / in grace, Buy
            //    is hidden: the operator renews via the Upgrade / Renew panel.
            //    Hidden when Valid (nothing to buy).
            //  * Both are suppressed while the clock is tampered. A rollback makes
            //    the enforcer Unload() the license, so the status drops to
            //    NotLoaded and these rules would otherwise offer Activate and Buy
            //    — telling an operator whose license is perfectly good to upload
            //    another one or purchase a second. Neither action can clear the
            //    condition: the next tick unloads the replacement just the same,
            //    because the fault is the system clock, not the license. The
            //    tamper alert already states the only fix (correct the date/time),
            //    and it clears within a tick of the clock being corrected, at
            //    which point these come back on their own if still warranted.
            ShowActivate = !isValid && !clockTampered,
            ShowBuy = (status is LicenseStatus.NotLoaded or LicenseStatus.Invalid) && !clockTampered,
            BuyUrl = _buyUrl,
            TrialSecondsRemaining = isValid
                ? null
                : (_trialState.Remaining() is { } r ? (int)Math.Max(0, r.TotalSeconds) : null),
            DemoCounting = !isValid && _trialState.Counting,
            DataStopped = _trialState.DataStopped,
            ProductVersion = ElpisEdgeConnect.Core.ProductVersion.Version,
            ProductBuildDate = ElpisEdgeConnect.Core.ProductVersion.BuildDateUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ExpiringSoon = LicenseVersionPolicy.IsExpiringSoon(status, current, DateTime.UtcNow),
            VersionRestricted = LicenseVersionPolicy.IsVersionRestricted(status, current, ElpisEdgeConnect.Core.ProductVersion.BuildDateUtc),
            ClockTampered = clockTampered,
        };
    }

    /// <summary>
    /// Validate an uploaded license (signature + payload), persist it to
    /// <see cref="LicensePath"/>, and hot-reload the live manager.
    /// </summary>
    public async Task<LicenseActivationResult> ActivateFromJsonAsync(string licenseJson, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(licenseJson))
        {
            return new LicenseActivationResult { Success = false, Error = "License content is empty." };
        }

        // 1. Validate with a throwaway manager (embedded public key, NO binding).
        //    Throws on bad signature / corrupt payload without touching disk.
        LicenseInfo? parsed;
        try
        {
            using var probe = new LicenseManager();
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(licenseJson));
            await probe.LoadAsync(ms, cancellationToken);
            parsed = probe.Current;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("License activation rejected during validation: {Message}", ex.Message);
            return new LicenseActivationResult { Success = false, Error = ex.Message };
        }

        // 1b. Single-machine binding pre-check (ADR-0036): reject a license issued
        //     for a different gateway before saving it, with a clear message.
        var localId = LocalGatewayId();
        if (!string.IsNullOrWhiteSpace(localId) && parsed is not null
            && !string.Equals(parsed.GatewayId, "*", StringComparison.Ordinal)
            && !string.Equals(parsed.GatewayId?.Trim(), localId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return new LicenseActivationResult
            {
                Success = false,
                Error = $"This license is issued for gateway '{parsed.GatewayId}', but this gateway's id is "
                    + $"'{localId}'. Request a license issued for this gateway's id.",
            };
        }

        // 2. Persist to the license path.
        try
        {
            var dir = Path.GetDirectoryName(_licensePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            await File.WriteAllTextAsync(_licensePath, licenseJson, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write license to {Path}", _licensePath);
            return new LicenseActivationResult
            {
                Success = false,
                Error = $"Could not save the license to '{_licensePath}': {ex.Message}",
            };
        }

        // 3. Hot-reload the live manager so status updates immediately (this also
        //    clears the ADR-0035 unlicensed cutoff). Newly-licensed protocol
        //    adapters still require a service restart to start collecting.
        await _license.LoadFromFileAsync(_licensePath, cancellationToken);
        _logger.LogInformation(
            "License activated and reloaded from {Path}; status is now {Status}.",
            _licensePath, _license.Status);

        return new LicenseActivationResult
        {
            Success = true,
            RestartRecommended = true,
            Status = GetStatus(),
        };
    }

    /// <summary>
    /// Resolve the license path the same way the runtime host does:
    /// <c>EDGECONNECT_LICENSE_PATH</c>, else <c>&lt;dataRoot&gt;/edgelicense.json</c>.
    /// </summary>
    public static string ResolveLicensePath()
    {
        var explicitPath = Environment.GetEnvironmentVariable("EDGECONNECT_LICENSE_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        var dataRoot = Environment.GetEnvironmentVariable("EDGECONNECT_DATA_ROOT")
            ?? (OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EdgeConnect")
                : "/var/lib/edgeconnect");
        return Path.Combine(dataRoot, "edgelicense.json");
    }

    /// <summary>
    /// Resolve the gateway identity file path the same way the runtime host does:
    /// <c>EDGECONNECT_IDENTITY_PATH</c>, else <c>&lt;dataRoot&gt;/identity</c>.
    /// </summary>
    public static string ResolveIdentityPath()
    {
        var explicitPath = Environment.GetEnvironmentVariable("EDGECONNECT_IDENTITY_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        var dataRoot = Environment.GetEnvironmentVariable("EDGECONNECT_DATA_ROOT")
            ?? (OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EdgeConnect")
                : "/var/lib/edgeconnect");
        return Path.Combine(dataRoot, "identity");
    }

    /// <summary>This gateway's persisted identity id, or <c>null</c> if not yet established.</summary>
    private static string? LocalGatewayId() =>
        ElpisEdgeConnect.Host.FileSystemGatewayIdentity.TryReadPersisted(ResolveIdentityPath());
}
