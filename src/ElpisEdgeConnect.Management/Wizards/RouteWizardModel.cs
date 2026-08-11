// ============================================================================
// File: Wizards/RouteWizardModel.cs
// Purpose: In-memory state for the Add-Route wizard (M.2b.5). Top-level
//          POCO that composes RouteFilterEditorModel + RouteTransformsEditorModel
//          and adds Identity / Source-wiring / Sink-wiring / Buffer /
//          Delivery state. Razor two-way-binds every field; on Save the
//          wizard calls BuildRouteConfig() to produce a canonical RouteConfig.
//
//          KEY VALIDATION DISCIPLINE (M.2b.5 v3 plan Locked N):
//            * EAGER (in this model): route-id regex via Core's own
//              attribute constant (no parallel pattern), required-field
//              presence, ≥1 sink selection, plus the sub-models' own
//              eager checks.
//            * LAZY (via POST /api/v1/config/drafts round-trip): every
//              cross-record rule — route references existing source/sink,
//              source must be enabled, dup route-id. The wizard never
//              re-implements CrossRecordValidator logic; rejection
//              responses surface as snackbars at Save time.
//
//          KEY UX INVARIANT (M.2b.5 v3 plan §3 Premium-UX discipline):
//          The wizard's Razor shell composes this model into the section
//          layout from UX mockups v1 §1.1. The model exposes BUILD blocks
//          (sub-models) and FLAT scalars (buffer/delivery) in a shape that
//          maps directly to MudBlazor section widgets — no shape gymnastics
//          required at the view layer.
// Reference: docs/sessions/2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v3.md §1, §5
//            docs/sessions/2026-05-18-mp2b5-mp2b6-ux-mockups.md §1
// ============================================================================

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>
/// Wizard state for adding a new Route. Razor binds form fields directly
/// to these properties and the composed sub-models; on Save,
/// <see cref="BuildRouteConfig"/> produces a canonical <see cref="RouteConfig"/>.
/// </summary>
/// <remarks>
/// Composition over duplication: glob + numeric validation live in the
/// sub-models, route-id regex composes the Core attribute via reflection,
/// cross-record validation defers to the management API. The model is a
/// shape-faithful aggregator, not a validator layer.
/// </remarks>
public sealed class RouteWizardModel
{
    // ─── Core-derived route-id regex (Locked N: no parallel pattern) ─────

    /// <summary>
    /// Regex pulled from <see cref="RouteConfig.RouteId"/>'s
    /// <see cref="RegularExpressionAttribute"/>. Reading the attribute at
    /// static-init time means the wizard always tracks Core's single
    /// source of truth — if Core ever loosens or tightens the regex,
    /// the wizard moves in lockstep automatically.
    /// </summary>
    private static readonly Regex RouteIdRegex = LoadRouteIdRegex();

    private static Regex LoadRouteIdRegex()
    {
        var property = typeof(RouteConfig).GetProperty(nameof(RouteConfig.RouteId))
            ?? throw new InvalidOperationException(
                $"RouteConfig.{nameof(RouteConfig.RouteId)} property not found — Core contract changed.");
        var attribute = property.GetCustomAttribute<RegularExpressionAttribute>()
            ?? throw new InvalidOperationException(
                $"RouteConfig.{nameof(RouteConfig.RouteId)} is missing its RegularExpressionAttribute — " +
                "Core contract changed.");
        return new Regex(attribute.Pattern, RegexOptions.Compiled);
    }

    // ─── Identity ────────────────────────────────────────────────────────

    /// <summary>Stable route id (e.g. <c>"jyoti17-to-eremos"</c>). Regex enforced via Core's attribute.</summary>
    public string RouteId { get; set; } = string.Empty;

    /// <summary>Human-readable route name shown in the admin UI.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether the route is enabled when the draft is applied.</summary>
    public bool Enabled { get; set; } = true;

    // ─── Source wiring ───────────────────────────────────────────────────

    /// <summary>
    /// <see cref="SourceInstanceConfig.InstanceId"/> chosen from the
    /// current gateway config's existing sources. The wizard's source
    /// picker section writes here. Cross-record check (does this id
    /// resolve to a real, enabled source?) defers to the merger / API.
    /// </summary>
    public string SourceInstanceId { get; set; } = string.Empty;

    // ─── Sink wiring (fanout) ────────────────────────────────────────────

    /// <summary>
    /// Sink instance ids the route fans out to. Razor checkboxes two-way
    /// bind to membership of this set. ≥1 required at projection time
    /// per <see cref="RouteConfig.SinkInstanceIds"/>'s Core contract.
    /// </summary>
    public HashSet<string> SinkInstanceIds { get; } = new(StringComparer.Ordinal);

    // ─── Filter (sub-model) ──────────────────────────────────────────────

    /// <summary>Composed Filter editor (Include / Exclude glob lists).</summary>
    public RouteFilterEditorModel Filter { get; } = new();

    // ─── Transforms (sub-model) ──────────────────────────────────────────

    /// <summary>Composed Transforms editor (TagMapping / Deadband / RateLimit).</summary>
    public RouteTransformsEditorModel Transforms { get; } = new();

    // ─── Buffer policy ───────────────────────────────────────────────────

    /// <summary>Buffering mode for this route. Default <see cref="BufferMode.StoreAndForward"/>.</summary>
    public BufferMode BufferMode { get; set; } = BufferMode.StoreAndForward;

    /// <summary>Maximum number of buffered points before <see cref="BufferOnOverflow"/> applies.</summary>
    public int BufferMaxDepth { get; set; } = 10_000;

    /// <summary>Maximum age in days for buffered points.</summary>
    public int BufferMaxAgeDays { get; set; } = 7;

    /// <summary>Overflow behaviour when <see cref="BufferMaxDepth"/> is hit.</summary>
    public DropPolicy BufferOnOverflow { get; set; } = DropPolicy.DropOldest;

    // ─── Delivery policy ─────────────────────────────────────────────────

    /// <summary>Delivery semantic. Default <see cref="DeliveryMode.AtLeastOnce"/> per blueprint §19.7.</summary>
    public DeliveryMode DeliveryMode { get; set; } = DeliveryMode.AtLeastOnce;

    /// <summary>Max retry attempts before the sink is marked Degraded.</summary>
    public int DeliveryMaxRetries { get; set; } = 5;

    /// <summary>Initial retry backoff in ms.</summary>
    public int DeliveryInitialBackoffMs { get; set; } = 100;

    /// <summary>Cap on the exponential backoff curve in ms.</summary>
    public int DeliveryMaxBackoffMs { get; set; } = 30_000;

    /// <summary>Exponential multiplier applied per retry.</summary>
    public double DeliveryBackoffMultiplier { get; set; } = 2.0;

    /// <summary>Randomised jitter percentage applied to each backoff.</summary>
    public int DeliveryJitterPercent { get; set; } = 10;

    /// <summary>Fan out to sinks in parallel (vs sequentially).</summary>
    public bool DeliveryFanoutParallel { get; set; } = true;

    // ─── Hydration (Edit mode) ───────────────────────────────────────────

    /// <summary>
    /// Reverse of <see cref="BuildRouteConfig"/>. Populates a new
    /// <see cref="RouteWizardModel"/> (including <see cref="Filter"/> and
    /// <see cref="Transforms"/> sub-models) from an existing
    /// <see cref="RouteConfig"/> so the Edit-mode wizard opens pre-filled.
    /// </summary>
    /// <exception cref="ArgumentNullException">When <paramref name="existing"/> is null.</exception>
    public static RouteWizardModel HydrateFromExisting(RouteConfig existing)
    {
        ArgumentNullException.ThrowIfNull(existing);

        var model = new RouteWizardModel
        {
            RouteId = existing.RouteId,
            Name = existing.Name ?? string.Empty,
            Enabled = existing.Enabled,
            SourceInstanceId = existing.SourceInstanceId,
            // Buffer
            BufferMode = existing.Buffer.Mode,
            BufferMaxDepth = existing.Buffer.MaxDepth,
            BufferMaxAgeDays = existing.Buffer.MaxAgeDays,
            BufferOnOverflow = existing.Buffer.OnOverflow,
            // Delivery
            DeliveryMode = existing.Delivery.Mode,
            DeliveryMaxRetries = existing.Delivery.MaxRetries,
            DeliveryInitialBackoffMs = existing.Delivery.InitialBackoffMs,
            DeliveryMaxBackoffMs = existing.Delivery.MaxBackoffMs,
            DeliveryBackoffMultiplier = existing.Delivery.BackoffMultiplier,
            DeliveryJitterPercent = existing.Delivery.JitterPercent,
            DeliveryFanoutParallel = existing.Delivery.FanoutParallel,
        };

        foreach (var sinkId in existing.SinkInstanceIds)
            model.SinkInstanceIds.Add(sinkId);

        // ── Filter ────────────────────────────────────────────────────
        model.Filter.Include.Clear();
        foreach (var pattern in existing.Filter.Include)
            model.Filter.Include.Add(pattern);
        if (existing.Filter.Exclude is { } excl)
        {
            foreach (var pattern in excl)
                model.Filter.Exclude.Add(pattern);
        }

        // ── Transforms ────────────────────────────────────────────────
        if (existing.Transforms is { } xforms)
        {
            if (xforms.TagMapping is not null)
            {
                foreach (var (src, canonical) in xforms.TagMapping)
                    model.Transforms.TagMappings.Add(new TagMappingRow { SourceTag = src, CanonicalTag = canonical });
            }

            if (xforms.Deadband is not null)
            {
                foreach (var (tag, value) in xforms.Deadband)
                    model.Transforms.DeadbandRows.Add(new DeadbandRow { Tag = tag, Threshold = value, Mode = DeadbandMode.Absolute });
            }

            if (xforms.DeadbandPercent is not null)
            {
                foreach (var (tag, value) in xforms.DeadbandPercent)
                    model.Transforms.DeadbandRows.Add(new DeadbandRow { Tag = tag, Threshold = value, Mode = DeadbandMode.Percent });
            }

            if (xforms.RateLimitMs is not null)
            {
                foreach (var (tag, ms) in xforms.RateLimitMs)
                    model.Transforms.RateLimitRows.Add(new RateLimitRow { Tag = tag, MinIntervalMs = ms });
            }
        }

        return model;
    }

    // ─── Eager validation helpers ───────────────────────────────────────

    /// <summary>
    /// Validate the route id against Core's attribute regex + length
    /// bounds. Returns null on success, an operator-facing error on
    /// failure.
    /// </summary>
    public static string? ValidateRouteId(string? routeId)
    {
        if (string.IsNullOrWhiteSpace(routeId))
        {
            return "Route id is required.";
        }

        if (routeId.Length > 128)
        {
            return "Route id must be 128 characters or fewer.";
        }

        if (!RouteIdRegex.IsMatch(routeId))
        {
            return "Route id must start with a letter or digit and contain only letters, digits, dots, hyphens, and underscores.";
        }

        return null;
    }

    /// <summary>
    /// Validate the human-readable route name. Optional — empty is fine
    /// (the wizard's "Name (optional)" label and BuildRouteConfig's
    /// RouteId fallback both honour this). Only length is enforced.
    /// </summary>
    public static string? ValidateName(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name) && name.Length > 256)
        {
            return "Name must be 256 characters or fewer.";
        }

        return null;
    }

    // ─── Projection ──────────────────────────────────────────────────────

    /// <summary>
    /// Project the wizard state into a canonical <see cref="RouteConfig"/>.
    /// </summary>
    /// <remarks>
    /// Defensive throws on prerequisites the wizard UI should never let
    /// reach this point (blank route id, blank source id, empty sink set).
    /// The Save button is expected to be disabled until each field
    /// validates, but the projection enforces the invariants anyway so a
    /// programmatic caller (test, API) can't construct an invalid route.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a required field is blank or when no sinks are selected.
    /// </exception>
    public RouteConfig BuildRouteConfig()
    {
        var routeIdError = ValidateRouteId(RouteId);
        if (routeIdError is not null)
        {
            throw new InvalidOperationException(routeIdError);
        }

        var nameError = ValidateName(Name);
        if (nameError is not null)
        {
            throw new InvalidOperationException(nameError);
        }

        if (string.IsNullOrWhiteSpace(SourceInstanceId))
        {
            throw new InvalidOperationException(
                "SourceInstanceId is required — pick a source from the wizard's source picker.");
        }

        if (SinkInstanceIds.Count == 0)
        {
            throw new InvalidOperationException(
                "At least one sink must be selected — a route with no sinks would never publish data.");
        }

        return new RouteConfig
        {
            RouteId = RouteId,
            // "Name (optional)" — when the operator leaves it blank, fall
            // back to RouteId so the resulting RouteConfig.Name always has
            // a non-empty value (Core's RouteConfig still expects one for
            // diagnostics + UI display labels).
            Name = string.IsNullOrWhiteSpace(Name) ? RouteId : Name,
            SourceInstanceId = SourceInstanceId,
            SinkInstanceIds = SinkInstanceIds.ToList(),
            Filter = Filter.BuildTagFilterConfig(),
            Transforms = Transforms.BuildTransformProfileConfig(),
            Buffer = new BufferPolicyConfig
            {
                Mode = BufferMode,
                MaxDepth = BufferMaxDepth,
                MaxAgeDays = BufferMaxAgeDays,
                OnOverflow = BufferOnOverflow,
            },
            Delivery = new DeliveryPolicyConfig
            {
                Mode = DeliveryMode,
                MaxRetries = DeliveryMaxRetries,
                InitialBackoffMs = DeliveryInitialBackoffMs,
                MaxBackoffMs = DeliveryMaxBackoffMs,
                BackoffMultiplier = DeliveryBackoffMultiplier,
                JitterPercent = DeliveryJitterPercent,
                FanoutParallel = DeliveryFanoutParallel,
            },
            Enabled = Enabled,
        };
    }
}
