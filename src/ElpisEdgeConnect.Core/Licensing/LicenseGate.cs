// ============================================================================
// File: Licensing/LicenseGate.cs
// Purpose: Production ILicenseGate implementation backed by ILicenseManager.
//          Walks the GatewayConfiguration and produces blocking reasons for
//          unlicensed modules, instance-limit overruns, global-limit overruns,
//          and expired-with-grace-exhausted state.
// Reference: ARCHITECTURE_BLUEPRINT.md §7, PHASE1_EXECUTION_PLAN.md B3
//
// LOCKED behaviour:
//   - Expired (grace exhausted) blocks ALL config changes regardless of content.
//     This matches blueprint §7.4: "Continue data flow, block config changes."
//   - InGracePeriod still allows config changes but emits a warning.
//   - NotLoaded fails closed for any config that contains sources, sinks, or
//     routes. Empty configs (zero of all three) are allowed for first-boot.
//   - Module identifiers are constructed as "source.{protocolName}" and
//     "sink.{protocolName}" with the protocol name lowercased per the
//     existing SourceInstanceConfig.ProtocolName regex.
// ============================================================================

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Errors;

namespace ElpisEdgeConnect.Core.Licensing;

/// <summary>
/// <see cref="ILicenseGate"/> that consults an <see cref="ILicenseManager"/>
/// to evaluate whether a configuration is allowed by the active license.
/// </summary>
public sealed class LicenseGate : ILicenseGate
{
    private readonly ILicenseManager _manager;

    /// <summary>Construct a gate over the given license manager.</summary>
    public LicenseGate(ILicenseManager manager)
    {
        System.ArgumentNullException.ThrowIfNull(manager);
        _manager = manager;
    }

    /// <inheritdoc/>
    public ValueTask<LicenseGateResult> EvaluateAsync(
        GatewayConfiguration configuration,
        CancellationToken cancellationToken)
    {
        System.ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();

        var status = _manager.Status;
        var current = _manager.Current;

        // Empty configurations are always allowed (first-boot bootstrap).
        bool isEmpty = configuration.Sources.Count == 0
            && configuration.Sinks.Count == 0
            && configuration.Routes.Count == 0;

        // Not loaded → fail closed unless empty.
        if (current is null || status == LicenseStatus.NotLoaded)
        {
            if (isEmpty)
            {
                return new ValueTask<LicenseGateResult>(LicenseGateResult.AllowAll);
            }
            return new ValueTask<LicenseGateResult>(new LicenseGateResult
            {
                Allowed = false,
                Reasons = new List<string>
                {
                    "No license is loaded; only an empty configuration is permitted.",
                },
            });
        }

        // Expired (grace exhausted) → block all config changes.
        if (status == LicenseStatus.Expired)
        {
            return new ValueTask<LicenseGateResult>(new LicenseGateResult
            {
                Allowed = false,
                Reasons = new List<string>
                {
                    $"License '{current.LicenseId}' grace period exhausted; configuration changes are blocked.",
                },
            });
        }

        // Walk the config.
        var reasons = new List<string>();
        var blockedModules = new HashSet<string>(System.StringComparer.Ordinal);
        var warnings = new List<string>();

        // -- Per-source module check + grouping for per-module count --
        // Note: the count dictionary intentionally includes entries whose
        // module is DISABLED. The limit check below is guarded by
        // `module.Enabled`, so disabled modules cannot trigger a spurious
        // instance-limit violation; they are already reported as blocked
        // above. Keeping the count dense here makes the loop simpler and
        // the disabled-module reporting path unambiguous.
        var sourceCountsByModule = new Dictionary<string, int>(System.StringComparer.Ordinal);
        foreach (var source in configuration.Sources)
        {
            var moduleKey = SourceModuleKey(source.ProtocolName);
            sourceCountsByModule[moduleKey] = sourceCountsByModule.GetValueOrDefault(moduleKey) + 1;
            if (!current.Modules.TryGetValue(moduleKey, out var module) || !module.Enabled)
            {
                blockedModules.Add(moduleKey);
                reasons.Add($"Source '{source.InstanceId}' uses module '{moduleKey}' which is not enabled by the license.");
            }
        }
        foreach (var (moduleKey, count) in sourceCountsByModule)
        {
            if (current.Modules.TryGetValue(moduleKey, out var module)
                && module.Enabled
                && module.MaxInstances is { } max
                && count > max)
            {
                reasons.Add($"Module '{moduleKey}' allows at most {max} instances; configuration declares {count}.");
            }
        }

        // -- Per-sink module check + grouping for per-module count --
        var sinkCountsByModule = new Dictionary<string, int>(System.StringComparer.Ordinal);
        foreach (var sink in configuration.Sinks)
        {
            var moduleKey = SinkModuleKey(sink.ProtocolName);
            sinkCountsByModule[moduleKey] = sinkCountsByModule.GetValueOrDefault(moduleKey) + 1;
            if (!current.Modules.TryGetValue(moduleKey, out var module) || !module.Enabled)
            {
                blockedModules.Add(moduleKey);
                reasons.Add($"Sink '{sink.InstanceId}' uses module '{moduleKey}' which is not enabled by the license.");
            }
        }
        foreach (var (moduleKey, count) in sinkCountsByModule)
        {
            if (current.Modules.TryGetValue(moduleKey, out var module)
                && module.Enabled
                && module.MaxInstances is { } max
                && count > max)
            {
                reasons.Add($"Module '{moduleKey}' allows at most {max} instances; configuration declares {count}.");
            }
        }

        // -- Global limits --
        if (configuration.Sources.Count > current.Limits.MaxSourceInstances)
        {
            reasons.Add($"Configuration declares {configuration.Sources.Count} sources; license allows {current.Limits.MaxSourceInstances}.");
        }
        if (configuration.Sinks.Count > current.Limits.MaxSinkInstances)
        {
            reasons.Add($"Configuration declares {configuration.Sinks.Count} sinks; license allows {current.Limits.MaxSinkInstances}.");
        }
        if (configuration.Routes.Count > current.Limits.MaxRoutes)
        {
            reasons.Add($"Configuration declares {configuration.Routes.Count} routes; license allows {current.Limits.MaxRoutes}.");
        }

        // -- Grace-period warning --
        if (status == LicenseStatus.InGracePeriod)
        {
            var graceDays = _manager.RemainingGrace?.Days ?? 0;
            warnings.Add($"License '{current.LicenseId}' is in its grace period ({graceDays} day(s) remaining). Renew the license to clear this warning.");
        }

        var result = new LicenseGateResult
        {
            Allowed = reasons.Count == 0,
            Reasons = reasons,
            BlockedModules = blockedModules.ToArray(),
            Warnings = warnings,
        };
        return new ValueTask<LicenseGateResult>(result);
    }

    /// <summary>
    /// Build the canonical module identifier for a source protocol name.
    /// </summary>
    public static string SourceModuleKey(string protocolName) =>
        "source." + (protocolName ?? string.Empty).ToLower(CultureInfo.InvariantCulture);

    /// <summary>
    /// Build the canonical module identifier for a sink protocol name.
    /// </summary>
    public static string SinkModuleKey(string protocolName) =>
        "sink." + (protocolName ?? string.Empty).ToLower(CultureInfo.InvariantCulture);
}
