// ============================================================================
// File: Licensing/LicenseManager.cs
// Purpose: Production ILicenseManager implementation. Loads license.json,
//          verifies its RSA-PSS signature, parses it into a frozen
//          LicenseInfo snapshot, and exposes lock-free fast-path checks.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone B3, ARCHITECTURE_BLUEPRINT.md §7
//
// Concurrency model:
//   - Loads are serialized through _loadLock (SemaphoreSlim).
//   - Reads (Current, Status, IsModuleEnabled, ...) read _snapshot via
//     Volatile.Read with no lock. _snapshot is replaced atomically by Loads.
//   - The snapshot is fully immutable; FrozenDictionary backs the module
//     and feature lookups so the fast path is allocation-free.
//
// Date semantics (locked, B3):
//   - The license file's expiresAt is a date with no time component.
//   - It is interpreted as 23:59:59.999 UTC of that date.
//   - A license dated "2026-04-07" remains Valid throughout 2026-04-07 UTC
//     and only enters InGracePeriod at 2026-04-08 00:00:00 UTC.
// ============================================================================

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Errors;

namespace ElpisEdgeConnect.Core.Licensing;

/// <summary>
/// File-backed implementation of <see cref="ILicenseManager"/>.
/// </summary>
public sealed class LicenseManager : ILicenseManager, IDisposable
{
    private readonly LicenseSignatureValidator _validator;
    private readonly LicenseEnforcementPolicy _policy;
    private readonly Func<DateTime> _utcNow;
    private readonly Func<string?>? _expectedGatewayIdProvider;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private static readonly string[] DateFormats = new[]
    {
        "yyyy-MM-dd",
        "yyyy-MM-ddTHH:mm:ssZ",
        "yyyy-MM-ddTHH:mm:ss.fffZ",
    };

    private readonly HashSet<string> _raisedWarnings = new(StringComparer.Ordinal);
    private readonly object _warningLock = new();

    private LicenseInfo? _snapshot;
    private LicenseStatus _status = LicenseStatus.NotLoaded;
    private TimeSpan? _remainingGrace;

    /// <summary>
    /// Construct a manager with the given signature validator and policy.
    /// Production code uses the parameterless overload, which wires the
    /// embedded dev key and the default policy.
    /// </summary>
    public LicenseManager(
        LicenseSignatureValidator validator,
        LicenseEnforcementPolicy? policy = null,
        Func<DateTime>? utcNow = null,
        Func<string?>? expectedGatewayIdProvider = null)
    {
        ArgumentNullException.ThrowIfNull(validator);
        _validator = validator;
        _policy = policy ?? LicenseEnforcementPolicy.Default;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _expectedGatewayIdProvider = expectedGatewayIdProvider;
    }

    /// <summary>Construct using the embedded dev public key.</summary>
    public LicenseManager()
        : this(new LicenseSignatureValidator(EmbeddedPublicKey.Pem))
    {
    }

    /// <summary>
    /// Construct using the embedded dev public key, bound to a single gateway.
    /// <paramref name="expectedGatewayIdProvider"/> supplies the running
    /// gateway's identity (resolved at load time); a loaded license whose
    /// <c>gatewayId</c> does not match is rejected (ADR-0036). A license
    /// <c>gatewayId</c> of <c>*</c> floats (valid on any gateway); a null/empty
    /// expected id disables the check (e.g. before the identity is established).
    /// </summary>
    public LicenseManager(Func<string?>? expectedGatewayIdProvider)
        : this(
            new LicenseSignatureValidator(EmbeddedPublicKey.Pem),
            expectedGatewayIdProvider: expectedGatewayIdProvider)
    {
    }

    /// <inheritdoc/>
    public LicenseInfo? Current => Volatile.Read(ref _snapshot);

    /// <inheritdoc/>
    public LicenseStatus Status => _status;

    /// <inheritdoc/>
    public TimeSpan? RemainingGrace => _remainingGrace;

    /// <inheritdoc/>
    public event EventHandler<LicenseWarning>? WarningRaised;

    /// <inheritdoc/>
    public async Task LoadFromFileAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            throw new LicenseException(
                CoreErrors.LicenseFileNotFound,
                $"License file '{path}' was not found.");
        }
        await using var stream = File.OpenRead(path);
        await LoadAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task LoadAsync(Stream licenseJson, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(licenseJson);

        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string json;
            using (var reader = new StreamReader(licenseJson, leaveOpen: true))
            {
                json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new LicenseException(
                    CoreErrors.LicenseFileCorrupt,
                    $"License JSON could not be parsed: {ex.Message}");
            }

            using (doc)
            {
                // 1. Extract signature.
                if (!doc.RootElement.TryGetProperty(CanonicalJson.SignaturePropertyName, out var sigElement)
                    || sigElement.ValueKind != JsonValueKind.String)
                {
                    throw new LicenseException(
                        CoreErrors.LicenseSignatureInvalid,
                        "License is missing the 'signature' field.");
                }
                var sigBase64 = sigElement.GetString()!;

                // 2. Canonicalize the payload (sans signature).
                var canonical = CanonicalJson.CanonicalizeRoot(doc.RootElement);

                // 3. Verify.
                if (!_validator.Verify(canonical, sigBase64))
                {
                    throw new LicenseException(
                        CoreErrors.LicenseSignatureInvalid,
                        "License signature does not match the embedded public key. " +
                        "The license file may be tampered, truncated, or signed with the wrong key.");
                }

                // 4. Parse typed payload.
                var info = ParsePayload(doc.RootElement);

                // 4b. Single-machine binding (ADR-0036): reject a license issued
                // for a different gateway. Throws before the snapshot swap, so a
                // mismatched license leaves the previous state untouched.
                EnforceGatewayBinding(info);

                // 5. Evaluate expiration.
                var snapshot = LicenseExpirationTracker.Evaluate(_utcNow(), info.ExpiresAt, _policy);

                // 6. Atomic swap.
                Volatile.Write(ref _snapshot, info);
                _status = snapshot.Status;
                _remainingGrace = snapshot.RemainingGrace;

                // 7. Reset warning dedupe set so newly loaded license can re-raise its boundaries.
                lock (_warningLock)
                {
                    _raisedWarnings.Clear();
                }

                // 8. Raise an initial warning if appropriate.
                MaybeRaiseWarningForSnapshot(snapshot, info);
            }
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <inheritdoc/>
    public bool IsModuleEnabled(string moduleKey)
    {
        ArgumentNullException.ThrowIfNull(moduleKey);
        var snap = Volatile.Read(ref _snapshot);
        if (snap is null)
        {
            return false;
        }
        return snap.Modules.TryGetValue(moduleKey, out var module) && module.Enabled;
    }

    /// <inheritdoc/>
    public bool IsFeatureEnabled(string featureKey)
    {
        ArgumentNullException.ThrowIfNull(featureKey);
        var snap = Volatile.Read(ref _snapshot);
        if (snap is null)
        {
            return false;
        }
        return snap.Features.TryGetValue(featureKey, out var enabled) && enabled;
    }

    /// <inheritdoc/>
    public LicenseEvaluationResult CheckInstanceLimit(string moduleKey, int proposedCount)
    {
        ArgumentNullException.ThrowIfNull(moduleKey);
        var snap = Volatile.Read(ref _snapshot);
        if (snap is null)
        {
            return LicenseEvaluationResult.Deny(
                CoreErrors.LicenseNotLoaded,
                "No license is loaded.");
        }
        if (!snap.Modules.TryGetValue(moduleKey, out var module) || !module.Enabled)
        {
            return LicenseEvaluationResult.Deny(
                CoreErrors.LicenseModuleDisabled,
                $"Module '{moduleKey}' is not enabled by the active license.");
        }
        if (module.MaxInstances is { } max && proposedCount > max)
        {
            return LicenseEvaluationResult.Deny(
                CoreErrors.LicenseInstanceLimitReached,
                $"Module '{moduleKey}' allows at most {max} instances; configuration declares {proposedCount}.");
        }
        return LicenseEvaluationResult.Allow;
    }

    /// <inheritdoc/>
    public void Tick()
    {
        var info = Volatile.Read(ref _snapshot);
        if (info is null)
        {
            return;
        }
        var snapshot = LicenseExpirationTracker.Evaluate(_utcNow(), info.ExpiresAt, _policy);
        _status = snapshot.Status;
        _remainingGrace = snapshot.RemainingGrace;
        MaybeRaiseWarningForSnapshot(snapshot, info);
    }

    /// <inheritdoc/>
    public void Unload()
    {
        // Atomically drop the license so every fast-path read (Current,
        // IsModuleEnabled, ...) immediately observes "unlicensed". The host
        // calls this when the license file is deleted or fails to re-verify,
        // so removing the file takes effect at once rather than at next start.
        Volatile.Write(ref _snapshot, null);
        _status = LicenseStatus.NotLoaded;
        _remainingGrace = null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _loadLock.Dispose();
        _validator.Dispose();
    }

    // ------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------

    /// <summary>
    /// Enforce single-machine binding (ADR-0036). When an expected gateway id is
    /// available and the license is not a floating license (<c>gatewayId == "*"</c>),
    /// a mismatch throws <see cref="CoreErrors.LicenseGatewayMismatch"/>.
    /// </summary>
    private void EnforceGatewayBinding(LicenseInfo info)
    {
        var expected = _expectedGatewayIdProvider?.Invoke();
        if (string.IsNullOrWhiteSpace(expected))
        {
            // No local identity established yet, or binding intentionally disabled.
            return;
        }
        if (string.Equals(info.GatewayId, "*", StringComparison.Ordinal))
        {
            // Floating license — valid on any gateway.
            return;
        }
        if (!string.Equals(info.GatewayId?.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new LicenseException(
                CoreErrors.LicenseGatewayMismatch,
                $"License '{info.LicenseId}' is issued for gateway '{info.GatewayId}', but this " +
                $"gateway's identity is '{expected}'. Licenses are bound to a single gateway " +
                "(ADR-0036); request a license issued for this gateway's id.");
        }
    }

    private void MaybeRaiseWarningForSnapshot(LicenseExpirationSnapshot snapshot, LicenseInfo info)
    {
        if (!snapshot.IsWarningBoundary)
        {
            return;
        }

        // Dedupe key: status + days. Once a given (status, days) pair has
        // been raised, we don't raise it again until LoadAsync resets the set.
        var key = $"{snapshot.Status}:{snapshot.DaysUntilExpiry}";
        lock (_warningLock)
        {
            if (!_raisedWarnings.Add(key))
            {
                return;
            }
        }

        var warning = new LicenseWarning
        {
            Level = snapshot.WarningLevel,
            Code = snapshot.Status switch
            {
                LicenseStatus.InGracePeriod => CoreErrors.LicenseExpired,
                LicenseStatus.Expired => CoreErrors.LicenseGraceExhausted,
                _ => null,
            },
            Message = BuildWarningMessage(snapshot, info),
            DaysUntilExpiry = snapshot.DaysUntilExpiry,
            Status = snapshot.Status,
            RaisedAtUtc = _utcNow(),
        };

        WarningRaised?.Invoke(this, warning);
    }

    private static string BuildWarningMessage(LicenseExpirationSnapshot snapshot, LicenseInfo info)
    {
        return snapshot.Status switch
        {
            LicenseStatus.Valid => $"License '{info.LicenseId}' expires in {snapshot.DaysUntilExpiry} day(s).",
            LicenseStatus.InGracePeriod => $"License '{info.LicenseId}' has expired. Grace period remaining: {snapshot.RemainingGrace?.Days ?? 0} day(s).",
            LicenseStatus.Expired => $"License '{info.LicenseId}' grace period exhausted. Configuration changes are blocked.",
            _ => $"License '{info.LicenseId}' status: {snapshot.Status}.",
        };
    }

    private static LicenseInfo ParsePayload(JsonElement root)
    {
        try
        {
            var licenseId = RequireString(root, "licenseId");
            var customer = RequireString(root, "customer");
            var gatewayId = RequireString(root, "gatewayId");
            var editionStr = RequireString(root, "edition");
            // The License Generator writes the edition as the DISPLAY string, which
            // may contain spaces ("CNC Pro", "Trial Period"). The enum member names
            // have no spaces (CncPro, TrialPeriod), so strip spaces before parsing —
            // "CNC Pro" -> "CNCPro" -> CncPro, "Trial Period" -> "TrialPeriod" (both
            // case-insensitive). Space-free values (Starter/Professional/Enterprise)
            // are unaffected. Genuinely unknown values still fail below.
            if (!Enum.TryParse<LicenseEdition>(editionStr.Replace(" ", string.Empty), ignoreCase: true, out var edition)
                || edition == LicenseEdition.Unknown)
            {
                throw new LicenseException(
                    CoreErrors.LicenseFileCorrupt,
                    $"License 'edition' value '{editionStr}' is not recognised.");
            }

            var issuedAt = RequireDate(root, "issuedAt", endOfDay: false);
            var expiresAt = RequireDate(root, "expiresAt", endOfDay: true);
            if (expiresAt < issuedAt)
            {
                throw new LicenseException(
                    CoreErrors.LicenseFileCorrupt,
                    "License 'expiresAt' is earlier than 'issuedAt'.");
            }

            var limitsEl = RequireObject(root, "limits");
            var limits = new LicenseLimits
            {
                MaxSourceInstances = RequireInt(limitsEl, "maxSourceInstances"),
                MaxSinkInstances = RequireInt(limitsEl, "maxSinkInstances"),
                MaxRoutes = RequireInt(limitsEl, "maxRoutes"),
            };
            if (limits.MaxSourceInstances < 0 || limits.MaxSinkInstances < 0 || limits.MaxRoutes < 0)
            {
                throw new LicenseException(
                    CoreErrors.LicenseFileCorrupt,
                    "License 'limits' values must be non-negative.");
            }

            var modulesEl = RequireObject(root, "modules");
            var modules = new Dictionary<string, LicenseModule>(StringComparer.Ordinal);
            foreach (var prop in modulesEl.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object)
                {
                    throw new LicenseException(
                        CoreErrors.LicenseFileCorrupt,
                        $"License module '{prop.Name}' must be a JSON object.");
                }
                bool enabled = prop.Value.TryGetProperty("enabled", out var enabledEl)
                    && enabledEl.ValueKind is JsonValueKind.True or JsonValueKind.False
                    && enabledEl.GetBoolean();
                int? maxInstances = null;
                if (prop.Value.TryGetProperty("maxInstances", out var maxEl) && maxEl.ValueKind == JsonValueKind.Number)
                {
                    maxInstances = maxEl.GetInt32();
                    if (maxInstances < 0)
                    {
                        throw new LicenseException(
                            CoreErrors.LicenseFileCorrupt,
                            $"License module '{prop.Name}' has negative maxInstances.");
                    }
                }
                modules[prop.Name] = new LicenseModule { Enabled = enabled, MaxInstances = maxInstances };
            }

            FrozenDictionary<string, bool> features = FrozenDictionary<string, bool>.Empty;
            if (root.TryGetProperty("features", out var featuresEl) && featuresEl.ValueKind == JsonValueKind.Object)
            {
                var f = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (var prop in featuresEl.EnumerateObject())
                {
                    if (prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        f[prop.Name] = prop.Value.GetBoolean();
                    }
                }
                features = f.ToFrozenDictionary(StringComparer.Ordinal);
            }

            return new LicenseInfo
            {
                LicenseId = licenseId,
                Customer = customer,
                GatewayId = gatewayId,
                Edition = edition,
                IssuedAt = issuedAt,
                ExpiresAt = expiresAt,
                Limits = limits,
                Modules = modules.ToFrozenDictionary(StringComparer.Ordinal),
                Features = features,
            };
        }
        catch (LicenseException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw new LicenseException(
                CoreErrors.LicenseFileCorrupt,
                $"License payload could not be decoded: {ex.Message}");
        }
    }

    private static string RequireString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String)
        {
            throw new LicenseException(
                CoreErrors.LicenseFileCorrupt,
                $"License is missing required string field '{name}'.");
        }
        var s = el.GetString();
        if (string.IsNullOrWhiteSpace(s))
        {
            throw new LicenseException(
                CoreErrors.LicenseFileCorrupt,
                $"License field '{name}' must not be empty.");
        }
        return s;
    }

    private static int RequireInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Number)
        {
            throw new LicenseException(
                CoreErrors.LicenseFileCorrupt,
                $"License is missing required integer field '{name}'.");
        }
        return el.GetInt32();
    }

    private static JsonElement RequireObject(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Object)
        {
            throw new LicenseException(
                CoreErrors.LicenseFileCorrupt,
                $"License is missing required object field '{name}'.");
        }
        return el;
    }

    private static DateTime RequireDate(JsonElement obj, string name, bool endOfDay)
    {
        var s = RequireString(obj, name);
        if (!DateTime.TryParseExact(
                s,
                DateFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            throw new LicenseException(
                CoreErrors.LicenseFileCorrupt,
                $"License field '{name}' is not a valid date (expected yyyy-MM-dd).");
        }
        if (endOfDay && parsed.TimeOfDay == TimeSpan.Zero)
        {
            // End-of-day UTC: 23:59:59.999
            parsed = new DateTime(parsed.Year, parsed.Month, parsed.Day, 23, 59, 59, 999, DateTimeKind.Utc);
        }
        else
        {
            parsed = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }
        return parsed;
    }
}
