// ============================================================================
// File: Wizards/MTConnectSourceWizardModel.cs
// Purpose: Testable POCO behind AddMTConnectSource.razor (M.2b.4 M3). The razor
//          binds to these properties; on Save, BuildSourceInstance() produces a
//          canonical SourceInstanceConfig with the MTConnect connection block
//          packed into the opaque Connection JsonElement (Core stays protocol-
//          agnostic). Mirrors Focas2SourceWizardModel, minus data-point
//          selection — MTConnect's tag set is the FIXED semantic map, so there
//          is nothing to select. Connection keys come from
//          MTConnectConnectionKeys (single source of truth).
// Reference: docs/sessions/2026-05-31-mtconnect-source-wizard-plan-v2.md.
// ============================================================================

using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sources.MTConnect;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>
/// Editable wizard state for an MTConnect source. The razor binds directly to
/// these properties; <see cref="BuildSourceInstance"/> emits the canonical
/// <see cref="SourceInstanceConfig"/> and <see cref="HydrateFromExisting"/> is
/// its inverse (for Edit mode).
/// </summary>
public sealed class MTConnectSourceWizardModel
{
    /// <summary>The protocol name stamped into the canonical config.</summary>
    public const string Protocol = "mtconnect";

    // ─── Identity ────────────────────────────────────────────────────────
    /// <summary>EdgeConnect instance id (unique per gateway).</summary>
    public string InstanceId { get; set; } = string.Empty;

    /// <summary>Device id; defaults to <see cref="InstanceId"/> when blank.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Operator-facing device name; defaults to <see cref="InstanceId"/> when blank.</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Device class — MTConnect sources are CNCs by definition.</summary>
    public string DeviceClass { get; set; } = "cnc";

    /// <summary>Whether the source is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Poll interval in milliseconds.</summary>
    public int PollIntervalMs { get; set; } = 1000;

    // ─── Connection ──────────────────────────────────────────────────────
    /// <summary>MTConnect agent base URL (required).</summary>
    public string AgentBaseUrl { get; set; } = string.Empty;

    /// <summary>Optional agent-side device name; blank targets the first device.</summary>
    public string AgentDeviceName { get; set; } = string.Empty;

    /// <summary>HTTP request timeout in seconds (runtime poll, not the browse probe).</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Initial reconnect backoff in ms.</summary>
    public int InitialBackoffMs { get; set; } = 2000;

    /// <summary>Maximum reconnect backoff in ms.</summary>
    public int MaxBackoffMs { get; set; } = 60_000;

    /// <summary>Backoff multiplier.</summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>Consecutive poll failures before the source reports Degraded.</summary>
    public int DegradeAfterConsecutiveFailures { get; set; } = 3;

    /// <summary>
    /// Build the canonical <see cref="SourceInstanceConfig"/>. MTConnect-specific
    /// fields land in the opaque <c>Connection</c> JsonElement.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="AgentBaseUrl"/> is blank.</exception>
    public SourceInstanceConfig BuildSourceInstance()
    {
        if (string.IsNullOrWhiteSpace(AgentBaseUrl))
        {
            throw new InvalidOperationException(
                "AgentBaseUrl is required to build an MTConnect source — the connection block must contain 'agentBaseUrl'.");
        }

        var connection = new JsonObject
        {
            [MTConnectConnectionKeys.AgentBaseUrl] = AgentBaseUrl.Trim(),
            [MTConnectConnectionKeys.TimeoutSeconds] = TimeoutSeconds,
            [MTConnectConnectionKeys.InitialBackoffMs] = InitialBackoffMs,
            [MTConnectConnectionKeys.MaxBackoffMs] = MaxBackoffMs,
            [MTConnectConnectionKeys.BackoffMultiplier] = BackoffMultiplier,
            [MTConnectConnectionKeys.DegradeAfterConsecutiveFailures] = DegradeAfterConsecutiveFailures,
        };
        if (!string.IsNullOrWhiteSpace(AgentDeviceName))
        {
            connection[MTConnectConnectionKeys.AgentDeviceName] = AgentDeviceName.Trim();
        }

        using var doc = JsonDocument.Parse(connection.ToJsonString());

        return new SourceInstanceConfig
        {
            InstanceId = InstanceId,
            ProtocolName = Protocol,
            DeviceId = string.IsNullOrWhiteSpace(DeviceId) ? InstanceId : DeviceId,
            DeviceName = string.IsNullOrWhiteSpace(DeviceName) ? InstanceId : DeviceName,
            DeviceClass = DeviceClass,
            Enabled = Enabled,
            Polling = new PollingSettings { IntervalMs = PollIntervalMs },
            Connection = doc.RootElement.Clone(),
        };
    }

    /// <summary>
    /// Inverse of <see cref="BuildSourceInstance"/> — hydrate a fresh model from a
    /// canonical config (Edit mode). Round-trip invariant: for any model M,
    /// <c>HydrateFromExisting(M.BuildSourceInstance()).BuildSourceInstance()</c>
    /// is value-equivalent to <c>M.BuildSourceInstance()</c>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="source"/> is not an MTConnect source.</exception>
    public static MTConnectSourceWizardModel HydrateFromExisting(SourceInstanceConfig source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!string.Equals(source.ProtocolName, Protocol, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Source has protocol '{source.ProtocolName}', expected '{Protocol}'.", nameof(source));
        }

        var model = new MTConnectSourceWizardModel
        {
            InstanceId = source.InstanceId,
            DeviceId = source.DeviceId,
            DeviceName = source.DeviceName ?? string.Empty,
            DeviceClass = source.DeviceClass ?? "cnc",
            Enabled = source.Enabled,
            PollIntervalMs = source.Polling.IntervalMs,
        };

        if (source.Connection is not { ValueKind: JsonValueKind.Object } conn)
        {
            return model;
        }

        if (conn.TryGetProperty(MTConnectConnectionKeys.AgentBaseUrl, out var url) && url.ValueKind == JsonValueKind.String)
        {
            model.AgentBaseUrl = url.GetString() ?? string.Empty;
        }
        if (conn.TryGetProperty(MTConnectConnectionKeys.AgentDeviceName, out var dev) && dev.ValueKind == JsonValueKind.String)
        {
            model.AgentDeviceName = dev.GetString() ?? string.Empty;
        }
        if (conn.TryGetProperty(MTConnectConnectionKeys.TimeoutSeconds, out var to) && to.ValueKind == JsonValueKind.Number)
        {
            model.TimeoutSeconds = to.GetInt32();
        }
        if (conn.TryGetProperty(MTConnectConnectionKeys.InitialBackoffMs, out var ib) && ib.ValueKind == JsonValueKind.Number)
        {
            model.InitialBackoffMs = ib.GetInt32();
        }
        if (conn.TryGetProperty(MTConnectConnectionKeys.MaxBackoffMs, out var mb) && mb.ValueKind == JsonValueKind.Number)
        {
            model.MaxBackoffMs = mb.GetInt32();
        }
        if (conn.TryGetProperty(MTConnectConnectionKeys.BackoffMultiplier, out var bm) && bm.ValueKind == JsonValueKind.Number)
        {
            model.BackoffMultiplier = bm.GetDouble();
        }
        if (conn.TryGetProperty(MTConnectConnectionKeys.DegradeAfterConsecutiveFailures, out var df) && df.ValueKind == JsonValueKind.Number)
        {
            model.DegradeAfterConsecutiveFailures = df.GetInt32();
        }

        return model;
    }
}
