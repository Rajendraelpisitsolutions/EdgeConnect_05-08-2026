// ============================================================================
// File: Configuration/CrossRecordValidator.cs
// Purpose: Enforces invariants that span multiple records and cannot be
//          expressed as DataAnnotations on a single type.
// Reference: ARCHITECTURE_BLUEPRINT.md §4.4, §6, §19.7; B2 plan
//
// Locked rules (from B2 pre-generation review assumption 3):
//   1. Every RouteConfig.SourceInstanceId resolves to a SourceInstanceConfig.
//   2. Every id in RouteConfig.SinkInstanceIds resolves to a SinkInstanceConfig.
//   3. RouteConfig.SinkInstanceIds is non-empty.
//   4. No two sources share an InstanceId.
//   5. No two sinks share an InstanceId.
//   6. No two routes share a RouteId.
//   7. AtMostOnce delivery requires BufferMode.None.
//   8. StoreAndForward buffer requires AtLeastOnce delivery.
//   9. (Implicit, covered by 4/5/6.)
//  10. Every TagMapping target is non-empty whitespace-free.
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Errors;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Validates cross-record invariants on a <see cref="GatewayConfiguration"/>.
/// Stateless and thread-safe; one shared instance is sufficient.
/// </summary>
public sealed class CrossRecordValidator
{
    /// <summary>Singleton instance.</summary>
    public static CrossRecordValidator Instance { get; } = new();

    /// <summary>
    /// Run every cross-record rule and return an aggregated
    /// <see cref="ValidationResult"/>. The result is invalid if any single
    /// rule failed; all rule violations are returned in one pass so the
    /// user can fix multiple problems at once.
    /// </summary>
    public ValidationResult Validate(GatewayConfiguration configuration)
    {
        if (configuration is null)
        {
            return ValidationResult.Failure(
                CoreErrors.ConfigInvalid,
                "Configuration is null.");
        }

        var errors = new List<ValidationIssue>();

        CheckDuplicateInstanceIds(configuration, errors);
        CheckRouteReferences(configuration, errors);
        CheckRouteDeliveryBufferCompatibility(configuration, errors);
        CheckTagMappings(configuration, errors);
        CheckTransformProfiles(configuration, errors);
        CheckDeliveryPolicies(configuration, errors);

        return errors.Count == 0
            ? ValidationResult.Success()
            : new ValidationResult { IsValid = false, Errors = errors };
    }

    // ------------------------------------------------------------------------
    // Rules 4, 5, 6 — duplicate instance / route ids
    // ------------------------------------------------------------------------
    private static void CheckDuplicateInstanceIds(GatewayConfiguration config, List<ValidationIssue> errors)
    {
        // Rule 4: source ids unique
        foreach (var dup in FindDuplicates(config.Sources, s => s.InstanceId))
        {
            errors.Add(new ValidationIssue
            {
                Code = CoreErrors.ConfigDuplicateSourceId,
                Message = $"Two or more sources share InstanceId '{dup}'. Source ids must be unique within the gateway.",
                Path = $"Sources[{dup}]",
            });
        }

        // Rule 5: sink ids unique
        foreach (var dup in FindDuplicates(config.Sinks, s => s.InstanceId))
        {
            errors.Add(new ValidationIssue
            {
                Code = CoreErrors.ConfigDuplicateSinkId,
                Message = $"Two or more sinks share InstanceId '{dup}'. Sink ids must be unique within the gateway.",
                Path = $"Sinks[{dup}]",
            });
        }

        // Rule 6: route ids unique
        foreach (var dup in FindDuplicates(config.Routes, r => r.RouteId))
        {
            errors.Add(new ValidationIssue
            {
                Code = CoreErrors.ConfigDuplicateRouteId,
                Message = $"Two or more routes share RouteId '{dup}'. Route ids must be unique within the gateway.",
                Path = $"Routes[{dup}]",
            });
        }
    }

    // ------------------------------------------------------------------------
    // Rules 1, 2, 3 — route references and non-empty sinks
    // ------------------------------------------------------------------------
    private static void CheckRouteReferences(GatewayConfiguration config, List<ValidationIssue> errors)
    {
        var sourceIds = new HashSet<string>(config.Sources.Select(s => s.InstanceId));
        var sinkIds = new HashSet<string>(config.Sinks.Select(s => s.InstanceId));

        foreach (var route in config.Routes)
        {
            // Rule 1: source must exist
            if (!sourceIds.Contains(route.SourceInstanceId))
            {
                errors.Add(new ValidationIssue
                {
                    Code = CoreErrors.RouteSourceNotFound,
                    Message = $"Route '{route.RouteId}' references source '{route.SourceInstanceId}' which is not configured.",
                    Path = $"Routes[{route.RouteId}].SourceInstanceId",
                });
            }

            // Rule 3: at least one sink
            if (route.SinkInstanceIds.Count == 0)
            {
                errors.Add(new ValidationIssue
                {
                    Code = CoreErrors.RouteNoSinks,
                    Message = $"Route '{route.RouteId}' has no sinks. Every route must dispatch to at least one sink.",
                    Path = $"Routes[{route.RouteId}].SinkInstanceIds",
                });
            }

            // Rule 2: every referenced sink must exist
            foreach (var sinkId in route.SinkInstanceIds)
            {
                if (!sinkIds.Contains(sinkId))
                {
                    errors.Add(new ValidationIssue
                    {
                        Code = CoreErrors.RouteSinkNotFound,
                        Message = $"Route '{route.RouteId}' references sink '{sinkId}' which is not configured.",
                        Path = $"Routes[{route.RouteId}].SinkInstanceIds",
                    });
                }
            }
        }
    }

    // ------------------------------------------------------------------------
    // Rules 7, 8 — delivery vs buffer mode compatibility
    // ------------------------------------------------------------------------
    private static void CheckRouteDeliveryBufferCompatibility(GatewayConfiguration config, List<ValidationIssue> errors)
    {
        foreach (var route in config.Routes)
        {
            // Rule 7: AtMostOnce requires BufferMode.None (blueprint §19.7)
            if (route.Delivery.Mode == DeliveryMode.AtMostOnce
                && route.Buffer.Mode != BufferMode.None)
            {
                errors.Add(new ValidationIssue
                {
                    Code = CoreErrors.RouteAtMostOnceRequiresNoBuffer,
                    Message = $"Route '{route.RouteId}' uses DeliveryMode.AtMostOnce with BufferMode.{route.Buffer.Mode}. " +
                              "AtMostOnce bypasses buffering entirely (blueprint §19.7); BufferMode must be None.",
                    Path = $"Routes[{route.RouteId}].Buffer.Mode",
                });
            }

            // Rule 8: StoreAndForward requires AtLeastOnce (blueprint §6)
            if (route.Buffer.Mode == BufferMode.StoreAndForward
                && route.Delivery.Mode != DeliveryMode.AtLeastOnce)
            {
                errors.Add(new ValidationIssue
                {
                    Code = CoreErrors.RouteStoreAndForwardRequiresAtLeastOnce,
                    Message = $"Route '{route.RouteId}' uses BufferMode.StoreAndForward with DeliveryMode.{route.Delivery.Mode}. " +
                              "Store-and-forward requires AtLeastOnce semantics (blueprint §6).",
                    Path = $"Routes[{route.RouteId}].Delivery.Mode",
                });
            }
        }
    }

    // ------------------------------------------------------------------------
    // Rule 10 — tag mapping targets must be non-empty / whitespace-free
    // ------------------------------------------------------------------------
    private static void CheckTagMappings(GatewayConfiguration config, List<ValidationIssue> errors)
    {
        foreach (var route in config.Routes)
        {
            var mapping = route.Transforms?.TagMapping;
            if (mapping is null)
            {
                continue;
            }

            foreach (var (source, target) in mapping)
            {
                if (string.IsNullOrWhiteSpace(target))
                {
                    errors.Add(new ValidationIssue
                    {
                        Code = CoreErrors.ConfigInvalidTagMapping,
                        Message = $"Route '{route.RouteId}' has an invalid tag mapping: source tag '{source}' " +
                                  "maps to a null, empty, or whitespace-only target.",
                        Path = $"Routes[{route.RouteId}].Transforms.TagMapping[{source}]",
                    });
                }
            }
        }
    }

    // ------------------------------------------------------------------------
    // Rules 11-14 — transform profile sanity (C1)
    //   - Absolute deadband thresholds must be finite and >= 0.
    //   - Percentage deadband thresholds must be in (0, 1].
    //   - A tag may not appear in both absolute and percentage deadband maps.
    //   - Rate limit values must be strictly positive.
    // ------------------------------------------------------------------------
    private static void CheckTransformProfiles(GatewayConfiguration config, List<ValidationIssue> errors)
    {
        foreach (var route in config.Routes)
        {
            var profile = route.Transforms;
            if (profile is null)
            {
                continue;
            }

            if (profile.Deadband is { } absolute)
            {
                foreach (var (tag, threshold) in absolute)
                {
                    if (!double.IsFinite(threshold) || threshold < 0)
                    {
                        errors.Add(new ValidationIssue
                        {
                            Code = CoreErrors.PipelineInvalidDeadbandThreshold,
                            Message = $"Route '{route.RouteId}' has an invalid absolute deadband threshold for tag '{tag}': {threshold}. Threshold must be finite and non-negative.",
                            Path = $"Routes[{route.RouteId}].Transforms.Deadband[{tag}]",
                        });
                    }
                }
            }

            if (profile.DeadbandPercent is { } percent)
            {
                foreach (var (tag, fraction) in percent)
                {
                    if (!double.IsFinite(fraction) || fraction <= 0 || fraction > 1)
                    {
                        errors.Add(new ValidationIssue
                        {
                            Code = CoreErrors.PipelineInvalidDeadbandThreshold,
                            Message = $"Route '{route.RouteId}' has an invalid percentage deadband for tag '{tag}': {fraction}. Value must be in (0, 1].",
                            Path = $"Routes[{route.RouteId}].Transforms.DeadbandPercent[{tag}]",
                        });
                    }
                }
            }

            if (profile.Deadband is { } abs && profile.DeadbandPercent is { } pct)
            {
                foreach (var tag in abs.Keys)
                {
                    if (pct.ContainsKey(tag))
                    {
                        errors.Add(new ValidationIssue
                        {
                            Code = CoreErrors.PipelineDeadbandConflict,
                            Message = $"Route '{route.RouteId}' tag '{tag}' is configured in both Deadband and DeadbandPercent. A tag may only use one deadband mode.",
                            Path = $"Routes[{route.RouteId}].Transforms.DeadbandPercent[{tag}]",
                        });
                    }
                }
            }

            if (profile.RateLimitMs is { } rate)
            {
                foreach (var (tag, ms) in rate)
                {
                    if (ms <= 0)
                    {
                        errors.Add(new ValidationIssue
                        {
                            Code = CoreErrors.PipelineInvalidRateLimit,
                            Message = $"Route '{route.RouteId}' rate-limit for tag '{tag}' is {ms}ms; must be strictly positive.",
                            Path = $"Routes[{route.RouteId}].Transforms.RateLimitMs[{tag}]",
                        });
                    }
                }
            }
        }
    }

    // ------------------------------------------------------------------------
    // Rule 12 — delivery policy field ranges and consistency (C3 commit 1)
    //   - InitialBackoffMs > 0 (also enforced by DataAnnotations Range)
    //   - MaxBackoffMs >= InitialBackoffMs (the cross-field consistency
    //     check that DataAnnotations cannot express)
    //   - BackoffMultiplier in [1.0, 10.0] (also DataAnnotations)
    //   - JitterPercent in [0, 100] (also DataAnnotations)
    //   - MaxRetries in [0, 100] (also DataAnnotations)
    //
    // The DataAnnotations checks fire from CheckTypedFields above; the only
    // RULE this method adds is the cross-field MaxBackoffMs >= InitialBackoffMs
    // relationship. The other ranges are listed here for traceability.
    // ------------------------------------------------------------------------
    private static void CheckDeliveryPolicies(GatewayConfiguration config, List<ValidationIssue> errors)
    {
        foreach (var route in config.Routes)
        {
            var policy = route.Delivery;
            if (policy.MaxBackoffMs < policy.InitialBackoffMs)
            {
                errors.Add(new ValidationIssue
                {
                    Code = CoreErrors.RouteBackoffRangeInvalid,
                    Message = $"Route '{route.RouteId}' delivery policy: MaxBackoffMs ({policy.MaxBackoffMs}) " +
                              $"must be greater than or equal to InitialBackoffMs ({policy.InitialBackoffMs}).",
                    Path = $"Routes[{route.RouteId}].Delivery.MaxBackoffMs",
                });
            }
        }
    }

    private static IEnumerable<string> FindDuplicates<T>(IEnumerable<T> items, System.Func<T, string> keySelector) =>
        items
            .GroupBy(keySelector)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
}
