// ============================================================================
// File: Api/EnableDisableApi.cs
// Purpose: Six verb-style endpoints for the inline Enable/Disable toggle
//          (M.2b.6.1):
//              POST /api/v1/sources/{id}/enable | /disable
//              POST /api/v1/sinks/{id}/enable | /disable
//              POST /api/v1/routes/{id}/enable | /disable
//
//          LAYER DISCIPLINE (Locked, v3 review):
//            This file owns ORCHESTRATION + ORDERING + TELEMETRY.
//            The planner (EnableDisablePlanner) is pure config reasoning.
//            The drawer model owns operator interaction state.
//            Telemetry lives at THIS boundary, nowhere else.
//
//          ORDERING (Locked G, v2 §2):
//             1. Stale-view check (expectedConfigurationVersion mismatch
//                → 409 CONFIG.STALE_VIEW; the operationally-misleading
//                inverted-stale case is caught BEFORE the no-op check)
//             2. No-op check (Locked F — current == desired → 200 NoOp;
//                no draft, no audit, no reload event)
//             3. Planner runs → may produce CrossRecordRefused (409)
//             4. Draft validate (existing pipeline)
//             5. Draft apply (existing pipeline)
//
//          TELEMETRY (Locked M, v3 §4):
//             Single Counter<long> "management_enable_disable_operations_total"
//             with four dimensions: entity_kind, requested_action,
//             outcome, initiated_from. No high-cardinality fields.
//             Cardinality bound: 3 × 2 × 5 × 3 = 90 time series.
//
// Reference: docs/sessions/2026-05-19-mp2b61-inline-enable-disable-plan-v3.md §4 (Locked M)
//            docs/sessions/2026-05-19-mp2b61-inline-enable-disable-plan-v2.md §2 (Locked G ordering)
//            docs/sessions/2026-05-19-mp2b61-implementation-kickoff.md §5 (layer discipline)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Contracts;
using ElpisEdgeConnect.Management.Wizards;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>
/// Endpoint registration + shared dispatch for the M.2b.6.1 inline
/// Enable/Disable toggle.
/// </summary>
public static class EnableDisableApi
{
    /// <summary>
    /// Telemetry meter name. Registered as a <see cref="System.Diagnostics.Metrics.Meter"/>
    /// shared across the six endpoint paths — single counter, four
    /// dimensions per Locked M.
    /// </summary>
    public const string MeterName = "ElpisEdgeConnect.Management.EnableDisable";

    /// <summary>The single counter name per Locked M.</summary>
    public const string CounterName = "management_enable_disable_operations_total";

    /// <summary>Map the six Enable/Disable endpoints onto <paramref name="builder"/>.</summary>
    public static IEndpointRouteBuilder MapEnableDisableApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var meter = new Meter(MeterName);
        var counter = meter.CreateCounter<long>(CounterName,
            description: "Inline Enable/Disable operations classified by entity kind, requested action, outcome, and originating page.");

        MapVerb(builder, "/api/v1/sources/{id}/enable",  ConfigEntityKind.Source, desired: true,  counter);
        MapVerb(builder, "/api/v1/sources/{id}/disable", ConfigEntityKind.Source, desired: false, counter);
        MapVerb(builder, "/api/v1/sinks/{id}/enable",    ConfigEntityKind.Sink,   desired: true,  counter);
        MapVerb(builder, "/api/v1/sinks/{id}/disable",   ConfigEntityKind.Sink,   desired: false, counter);
        MapVerb(builder, "/api/v1/routes/{id}/enable",   ConfigEntityKind.Route,  desired: true,  counter);
        MapVerb(builder, "/api/v1/routes/{id}/disable",  ConfigEntityKind.Route,  desired: false, counter);

        return builder;
    }

    private static void MapVerb(
        IEndpointRouteBuilder builder, string pattern, ConfigEntityKind kind,
        bool desired, Counter<long> counter)
    {
        builder.MapPost(pattern, async (
            string id,
            EnableDisableRequestDto? body,
            ElpisEdgeConnect.Core.Configuration.IConfigurationManager mgr,
            CancellationToken ct) =>
        {
            var outcome = await DispatchAsync(kind, id, desired, body, mgr, ct).ConfigureAwait(false);
            EmitTelemetry(counter, kind, desired, outcome.TelemetryOutcome);
            return outcome.HttpResult;
        })
        .WithTags(TagFor(kind));
    }

    // ─── Dispatch (shared across all six endpoints) ─────────────────────

    /// <summary>
    /// Run the Locked-G evaluation order and produce both an HTTP result
    /// and the telemetry outcome tag. Pure orchestration — no direct
    /// telemetry emission here (the caller emits using the returned tag
    /// so the counter increments exactly once per request).
    /// </summary>
    internal static async Task<DispatchOutcome> DispatchAsync(
        ConfigEntityKind kind,
        string id,
        bool desiredEnabled,
        EnableDisableRequestDto? body,
        ElpisEdgeConnect.Core.Configuration.IConfigurationManager mgr,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return new DispatchOutcome(
                Results.BadRequest(new { error = "Entity id is required." }),
                TelemetryOutcome: "validation_refused");
        }

        // ── Step 1 (Locked G): stale-view check ───────────────────────
        var currentVersion = mgr.CurrentVersionId;
        if (!string.IsNullOrWhiteSpace(body?.ExpectedConfigurationVersion)
            && !string.Equals(body!.ExpectedConfigurationVersion, currentVersion.Value, StringComparison.Ordinal))
        {
            return new DispatchOutcome(
                Results.Json(
                    new EnableDisableResponseDto
                    {
                        Outcome = EnableDisableOutcome.Conflict,
                        Error = new EnableDisableErrorDto
                        {
                            Code = "CONFIG.STALE_VIEW",
                            Message = "Configuration changed. Refresh and try again.",
                            ExpectedVersion = body.ExpectedConfigurationVersion,
                            CurrentVersion = currentVersion.Value,
                        },
                    },
                    statusCode: StatusCodes.Status409Conflict),
                TelemetryOutcome: "stale_view");
        }

        // ── Plan (which performs no-op + cross-record reasoning) ──────
        // The planner is the source of truth for both checks — the API
        // simply dispatches on the planner's outcome.
        var current = await mgr.GetCurrentAsync(ct).ConfigureAwait(false);
        EnableDisablePlanResult plan;
        try
        {
            plan = EnableDisablePlanner.Plan(current, kind, id, desiredEnabled);
        }
        catch (KeyNotFoundException)
        {
            return new DispatchOutcome(
                Results.NotFound(new
                {
                    error = $"{kind} '{id}' not found in current configuration.",
                }),
                TelemetryOutcome: "validation_refused");
        }

        switch (plan.Outcome)
        {
            // ── Step 2 (Locked F): no-op suppression ───────────────────
            case EnableDisablePlanOutcome.NoOp:
                return new DispatchOutcome(
                    Results.Ok(new EnableDisableResponseDto
                    {
                        Outcome = EnableDisableOutcome.NoOp,
                        Reason = EnableDisableNoOpReason.AlreadyInDesiredState,
                        Entity = new EnableDisableEntityRef
                        {
                            Kind = kind.ToString().ToLowerInvariant(),
                            Id = id,
                        },
                        CurrentEnabled = desiredEnabled,  // == current, by NoOp definition
                    }),
                    TelemetryOutcome: "noop");

            // ── Step 3 (Locked C): cross-record refusal ────────────────
            case EnableDisablePlanOutcome.CrossRecordRefused:
                return new DispatchOutcome(
                    Results.Json(
                        new EnableDisableResponseDto
                        {
                            Outcome = EnableDisableOutcome.Conflict,
                            Error = new EnableDisableErrorDto
                            {
                                Code = "CONFIG.CROSS_RECORD_REFUSED",
                                Message = BuildCrossRecordMessage(kind, id, desiredEnabled),
                                Dependents = plan.Blockers
                                    .Select(b => new EnableDisableEntityRef
                                    {
                                        Kind = b.Kind.ToString().ToLowerInvariant(),
                                        Id = b.Id,
                                        Name = b.Name,
                                    })
                                    .ToList(),
                            },
                        },
                        statusCode: StatusCodes.Status409Conflict),
                    TelemetryOutcome: "cross_record_refused");

            case EnableDisablePlanOutcome.Apply:
                return await ApplyAsync(plan.Draft!, kind, id, mgr, ct).ConfigureAwait(false);

            default:
                return new DispatchOutcome(
                    Results.Problem(
                        title: "Planner returned unexpected outcome.",
                        detail: plan.Outcome.ToString(),
                        statusCode: StatusCodes.Status500InternalServerError),
                    TelemetryOutcome: "validation_refused");
        }
    }

    // ── Apply pipeline (existing draft → validate → apply) ──────────────

    private static async Task<DispatchOutcome> ApplyAsync(
        GatewayConfiguration draft,
        ConfigEntityKind kind,
        string id,
        ElpisEdgeConnect.Core.Configuration.IConfigurationManager mgr,
        CancellationToken ct)
    {
        // Actor convention matches existing endpoints — "system" when no
        // actor is supplied (MVP; M.2d will carry richer operator context).
        const string actor = "system";

        var draftId = await mgr.CreateDraftAsync(draft, actor, ct).ConfigureAwait(false);
        var validation = await mgr.ValidateDraftAsync(draftId, ct).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            return new DispatchOutcome(
                Results.UnprocessableEntity(new
                {
                    error = "Draft failed validation.",
                    validation,
                }),
                TelemetryOutcome: "validation_refused");
        }

        var apply = await mgr.ApplyDraftAsync(draftId, actor, ct).ConfigureAwait(false);
        if (!apply.Success)
        {
            return new DispatchOutcome(
                Results.Conflict(new
                {
                    error = "Apply failed under the apply mutex (re-validation rejected the draft).",
                    apply,
                }),
                TelemetryOutcome: "validation_refused");
        }

        _ = kind;  // kind reserved for log-context; deliberately unused below.
        _ = id;

        return new DispatchOutcome(
            Results.Ok(new EnableDisableResponseDto
            {
                Outcome = EnableDisableOutcome.Applied,
                DraftId = draftId.Value,
                ValidationOutcome = "Passed",
                AppliedAt = DateTime.UtcNow,
                // The audit-record id surfaces as the new version id —
                // matches the existing apply endpoint's convention.
                AuditRecordId = apply.VersionId.Value,
            }),
            TelemetryOutcome: "applied");
    }

    // ─── Telemetry emission ─────────────────────────────────────────────

    private static void EmitTelemetry(
        Counter<long> counter, ConfigEntityKind kind, bool desired, string outcomeTag)
    {
        // Locked M: exactly four dimensions. No instance ids. No timestamps.
        // initiated_from is derived from kind for MVP — every toggle today
        // originates from the kind-aligned list page. Future cross-page
        // links can override via a request-body field when needed.
        counter.Add(1,
            new KeyValuePair<string, object?>("entity_kind", kind.ToString().ToLowerInvariant()),
            new KeyValuePair<string, object?>("requested_action", desired ? "enable" : "disable"),
            new KeyValuePair<string, object?>("outcome", outcomeTag),
            new KeyValuePair<string, object?>("initiated_from", InitiatedFromFor(kind)));
    }

    private static string InitiatedFromFor(ConfigEntityKind kind) => kind switch
    {
        ConfigEntityKind.Source => "sources_page",
        ConfigEntityKind.Sink => "sinks_page",
        ConfigEntityKind.Route => "routes_page",
        _ => "unknown",
    };

    private static string TagFor(ConfigEntityKind kind) => kind switch
    {
        ConfigEntityKind.Source => "Sources",
        ConfigEntityKind.Sink => "Sinks",
        ConfigEntityKind.Route => "Routes",
        _ => "Config",
    };

    private static string BuildCrossRecordMessage(ConfigEntityKind kind, string id, bool desired)
    {
        // Locked N copy (operational English). The endpoint surfaces the
        // structured `dependents` list separately — this message is a
        // one-line headline.
        if (kind == ConfigEntityKind.Route && desired)
        {
            return $"Cannot enable route '{id}'. Its source or destination is disabled. Enable the dependency first.";
        }
        var verb = desired ? "enable" : "disable";
        return $"Cannot {verb} {kind.ToString().ToLowerInvariant()} '{id}'. Other configuration depends on it.";
    }

    /// <summary>
    /// Internal dispatch result shape — the HTTP response plus the
    /// telemetry outcome tag the caller emits exactly once.
    /// </summary>
    internal sealed record DispatchOutcome(IResult HttpResult, string TelemetryOutcome);
}
