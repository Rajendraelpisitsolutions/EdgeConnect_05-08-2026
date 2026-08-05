// ============================================================================
// File: MTConnectProbeParser.cs
// Purpose: Bounded parser for an MTConnect /probe document, used by the Studio
//          add-source wizard (M.2b.4) to make the adapter's FIXED semantic tag
//          map accurate for a specific agent. It does NOT expose arbitrary
//          dataItems for selection — it answers two questions only:
//            1. Which of the fixed canonical tags can this agent actually
//               produce? (availability, via the SHARED MTConnectSemanticMap so
//               it can never disagree with what the runtime stream parser emits)
//            2. Which physical axes does it have? (Linear/Rotary components)
//
//          Public so the Management browse service can call it; the protocol
//          knowledge stays in the adapter assembly (plan v2 §1, Q-1).
// Reference: docs/sessions/2026-05-31-mtconnect-source-wizard-plan-v2.md §2/§3.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace ElpisEdgeConnect.Sources.MTConnect;

/// <summary>Thrown when a document parses as XML but is not a usable MTConnect /probe.</summary>
public sealed class MTConnectProbeFormatException : Exception
{
    /// <summary>Create the exception with a human-readable reason.</summary>
    public MTConnectProbeFormatException(string message) : base(message) { }
}

/// <summary>Availability of one canonical semantic tag for the probed device.</summary>
public sealed record MTConnectDiscoveredTag
{
    /// <summary>Canonical tag name (e.g. <c>spindle/load</c>).</summary>
    public required string CanonicalTag { get; init; }

    /// <summary>True when the agent declares a source dataItem for this tag.</summary>
    public required bool Available { get; init; }

    /// <summary>The matched dataItem <c>type</c> (when available).</summary>
    public string? SourceDataItemType { get; init; }

    /// <summary>The matched dataItem <c>id</c> (when available).</summary>
    public string? SourceDataItemId { get; init; }

    /// <summary>Why the tag is unavailable (when not available).</summary>
    public string? Reason { get; init; }
}

/// <summary>The bounded result of parsing a /probe document for one target device.</summary>
public sealed record MTConnectProbeResult
{
    /// <summary>Names of every device in the /probe (for multi-device handling — one source per device).</summary>
    public required IReadOnlyList<string> DeviceNames { get; init; }

    /// <summary>The device these results describe (the requested one, or the first).</summary>
    public string? TargetDeviceName { get; init; }

    /// <summary>Target device UUID, when present.</summary>
    public string? TargetDeviceUuid { get; init; }

    /// <summary>Target device manufacturer (from its Description), when present.</summary>
    public string? Manufacturer { get; init; }

    /// <summary>Per-canonical-tag availability for the target device.</summary>
    public required IReadOnlyList<MTConnectDiscoveredTag> Tags { get; init; }

    /// <summary>Discovered axis identities (Linear/Rotary components with a Position dataItem), capped.</summary>
    public required IReadOnlyList<string> Axes { get; init; }

    /// <summary>True when at least one canonical tag is available — drives ReachableWithRecognisedTags.</summary>
    public bool HasRecognisedTags => Tags.Any(t => t.Available);
}

/// <summary>Parses an MTConnect <c>/probe</c> document into the bounded discovery result.</summary>
public static class MTConnectProbeParser
{
    /// <summary>Maximum axes surfaced — protects the wizard from malformed agents (plan v2, QC-1).</summary>
    public const int MaxAxes = 12;

    /// <summary>
    /// Parse <paramref name="probeXml"/>. When <paramref name="targetDeviceName"/> is
    /// supplied it selects that device (case-insensitive); otherwise the first device
    /// is used. The returned <see cref="MTConnectProbeResult.DeviceNames"/> is empty
    /// when the document is a valid <c>MTConnectDevices</c> envelope with no devices.
    /// </summary>
    /// <exception cref="System.Xml.XmlException">Body is not well-formed XML.</exception>
    /// <exception cref="MTConnectProbeFormatException">Body is XML but not an MTConnectDevices document.</exception>
    public static MTConnectProbeResult Parse(string probeXml, string? targetDeviceName = null)
    {
        ArgumentNullException.ThrowIfNull(probeXml);

        var doc = XDocument.Parse(probeXml); // XmlException → caller maps to InvalidProbeDocument
        var root = doc.Root;
        if (root is null || root.Name.LocalName != "MTConnectDevices")
        {
            throw new MTConnectProbeFormatException(
                "Document is not an MTConnect /probe response (expected an <MTConnectDevices> root).");
        }

        var ns = root.GetDefaultNamespace();
        var devices = root.Descendants(ns + "Device").ToList();
        var deviceNames = devices
            .Select(d => d.Attribute("name")?.Value ?? d.Attribute("uuid")?.Value ?? "(unnamed)")
            .ToList();

        if (devices.Count == 0)
        {
            // Valid envelope, but nothing to onboard.
            return new MTConnectProbeResult
            {
                DeviceNames = deviceNames,
                Tags = Array.Empty<MTConnectDiscoveredTag>(),
                Axes = Array.Empty<string>(),
            };
        }

        var device = SelectDevice(devices, ns, targetDeviceName);

        // Flatten every dataItem under the target device. Key off type (upper) but
        // keep the first id + whether any condition exists.
        var dataItems = device.Descendants(ns + "DataItem").ToList();
        var byType = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        var hasCondition = false;
        foreach (var di in dataItems)
        {
            var type = di.Attribute("type")?.Value;
            if (!string.IsNullOrEmpty(type) && !byType.ContainsKey(type))
            {
                byType[type] = di;
            }
            if (string.Equals(di.Attribute("category")?.Value, MTConnectSemanticMap.ConditionCategory, StringComparison.OrdinalIgnoreCase))
            {
                hasCondition = true;
            }
        }

        var tags = new List<MTConnectDiscoveredTag>();

        // Scalar/event tags — uniform availability via the shared map.
        foreach (var mapping in MTConnectSemanticMap.Scalar)
        {
            XElement? match = null;
            string? matchedType = null;
            foreach (var probeType in mapping.ProbeDataItemTypes)
            {
                if (byType.TryGetValue(probeType, out var di))
                {
                    match = di;
                    matchedType = probeType;
                    break;
                }
            }
            tags.Add(new MTConnectDiscoveredTag
            {
                CanonicalTag = mapping.Tag.TagName,
                Available = match is not null,
                SourceDataItemType = matchedType,
                SourceDataItemId = match?.Attribute("id")?.Value,
                Reason = match is null
                    ? $"Agent exposes no {string.Join(" / ", mapping.ProbeDataItemTypes)} dataItem"
                    : null,
            });
        }

        // Alarm tags — available when ANY Condition dataItem exists.
        foreach (var alarm in MTConnectSemanticMap.AlarmTags)
        {
            tags.Add(new MTConnectDiscoveredTag
            {
                CanonicalTag = alarm.TagName,
                Available = hasCondition,
                SourceDataItemType = hasCondition ? "CONDITION" : null,
                Reason = hasCondition ? null : "Agent exposes no Condition dataItems",
            });
        }

        return new MTConnectProbeResult
        {
            DeviceNames = deviceNames,
            TargetDeviceName = device.Attribute("name")?.Value,
            TargetDeviceUuid = device.Attribute("uuid")?.Value,
            Manufacturer = device.Descendants(ns + "Description").FirstOrDefault()?.Attribute("manufacturer")?.Value,
            Tags = tags,
            Axes = DiscoverAxes(device, ns),
        };
    }

    private static XElement SelectDevice(List<XElement> devices, XNamespace ns, string? targetDeviceName)
    {
        if (!string.IsNullOrWhiteSpace(targetDeviceName))
        {
            var match = devices.FirstOrDefault(d =>
                string.Equals(d.Attribute("name")?.Value, targetDeviceName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }
        return devices[0];
    }

    private static List<string> DiscoverAxes(XElement device, XNamespace ns)
    {
        // Axes are Linear/Rotary components; identity = name → nativeName → id
        // (QC-1). Only count axes that actually carry a Position dataItem, so we
        // surface exactly the axis tags the adapter can emit.
        var axes = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in device.Descendants()
                     .Where(e => e.Name.LocalName is "Linear" or "Rotary"))
        {
            var hasPosition = component.Descendants(ns + "DataItem")
                .Any(di => string.Equals(di.Attribute("type")?.Value, MTConnectSemanticMap.PositionType, StringComparison.OrdinalIgnoreCase));
            if (!hasPosition)
            {
                continue;
            }
            var name = component.Attribute("name")?.Value
                       ?? component.Attribute("nativeName")?.Value
                       ?? component.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
            {
                continue;
            }
            axes.Add(name);
            if (axes.Count >= MaxAxes)
            {
                break;
            }
        }
        return axes;
    }
}
