// ============================================================================
// File: MTConnectStreamParser.cs
// Purpose: Parse an MTConnectStreams XML document (the response from
//          /current) and emit canonical data points via a supplied
//          CanonicalDataPointFactory. Separated from the adapter so parsing
//          can be unit-tested against fixed XML strings without any
//          supervisor / DI / state-machine involvement.
//
// MTConnect XML shape (simplified, varies by agent version):
//   <MTConnectStreams>
//     <Streams>
//       <DeviceStream name="CNC-1" uuid="…">
//         <ComponentStream component="Controller" …>
//           <Events>
//             <Execution dataItemId="…">ACTIVE</Execution>
//             <ControllerMode dataItemId="…">AUTOMATIC</ControllerMode>
//             <Program dataItemId="…">O1234</Program>
//             <EmergencyStop dataItemId="…">ARMED</EmergencyStop>
//             <PartCount dataItemId="…">42</PartCount>
//           </Events>
//           <Samples>
//             <PathFeedrate dataItemId="…" units="mm/min">500</PathFeedrate>
//             <SpindleSpeed dataItemId="…" units="rpm">1200</SpindleSpeed>
//             <Position dataItemId="…" name="X" subType="ACTUAL">123.456</Position>
//             <Position dataItemId="…" name="X" subType="MACHINE">120.000</Position>
//           </Samples>
//           <Condition>
//             <Fault dataItemId="…" type="SPINDLE" nativeCode="42">Spindle overtemp</Fault>
//             <Normal dataItemId="…" type="SYSTEM" />
//           </Condition>
//         </ComponentStream>
//       </DeviceStream>
//     </Streams>
//   </MTConnectStreams>
//
// Reference: https://www.mtconnect.org/ (standard)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using ElpisEdgeConnect.Core.Model;

namespace ElpisEdgeConnect.Sources.MTConnect;

/// <summary>
/// Parses an MTConnectStreams XML document and appends canonical data
/// points to a caller-supplied list. Stateless — every call is independent.
/// </summary>
internal static class MTConnectStreamParser
{
    private const string UnavailableSentinel = "UNAVAILABLE";

    // Lookup keys come from the SHARED MTConnectSemanticMap so the stream parser
    // (/current) and the wizard's /probe availability checker can never drift on
    // which source elements map to which canonical tag (plan v2 §2).

    /// <summary>
    /// Parse a single <c>/current</c> response into canonical points.
    /// Returns true if a DeviceStream was found and at least one value was
    /// available; false when the Agent returned a well-formed document with
    /// no usable content (e.g. everything marked <c>UNAVAILABLE</c>).
    /// </summary>
    /// <param name="xml">Raw XML body from the Agent.</param>
    /// <param name="factory">Canonical point factory pre-bound to the gateway+source identity.</param>
    /// <param name="points">Caller-owned list the parser appends to.</param>
    /// <param name="deviceTimestamp">Wall-clock reading timestamp (device).</param>
    /// <param name="gatewayTimestamp">Wall-clock ingest timestamp (gateway).</param>
    /// <exception cref="System.Xml.XmlException">Thrown when <paramref name="xml"/> is not valid XML.</exception>
    public static bool ParseCurrent(
        string xml,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        ArgumentNullException.ThrowIfNull(xml);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(points);

        var doc = XDocument.Parse(xml);
        var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

        var streams = doc.Descendants(ns + "DeviceStream").ToList();
        if (streams.Count == 0)
        {
            return false;
        }

        // Flatten every Event + Sample value into a lookup keyed by local
        // element name + optional subType qualifier. MTConnect agents use
        // inconsistent naming between vendors, so we store both the bare
        // local name and subtype-qualified variants (e.g., "Position_ACTUAL").
        var flat = new Dictionary<string, DataItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var stream in streams)
        {
            CollectEventsAndSamples(stream, ns, flat);
        }

        var hadValue = false;

        // Status rollup
        if (TryGetAny(flat, MTConnectSemanticMap.RunState.StreamElementNames, out var execution))
        {
            var runState = MapExecutionToRunState(execution.Value);
            points.Add(factory.CreatePoint(
                MTConnectTagMap.RunState.TagName, MTConnectTagMap.RunState.TagPath,
                runState, MTConnectTagMap.RunState.ValueType, DataQuality.Good,
                deviceTimestamp, gatewayTimestamp));
            hadValue = true;
        }
        if (TryGetAny(flat, MTConnectSemanticMap.ControllerMode.StreamElementNames, out var mode))
        {
            var mapped = MapControllerMode(mode.Value);
            points.Add(factory.CreatePoint(
                MTConnectTagMap.ControllerMode.TagName, MTConnectTagMap.ControllerMode.TagPath,
                mapped, MTConnectTagMap.ControllerMode.ValueType, DataQuality.Good,
                deviceTimestamp, gatewayTimestamp));
            hadValue = true;
        }
        if (TryGetAny(flat, MTConnectSemanticMap.EmergencyStop.StreamElementNames, out var estop))
        {
            var triggered = string.Equals(estop.Value, "TRIGGERED", StringComparison.OrdinalIgnoreCase);
            points.Add(factory.CreatePoint(
                MTConnectTagMap.EmergencyStop.TagName, MTConnectTagMap.EmergencyStop.TagPath,
                triggered, MTConnectTagMap.EmergencyStop.ValueType, DataQuality.Good,
                deviceTimestamp, gatewayTimestamp));
            hadValue = true;
        }

        // Program
        if (TryGetAny(flat, MTConnectSemanticMap.MainProgram.StreamElementNames, out var program))
        {
            points.Add(factory.CreatePoint(
                MTConnectTagMap.MainProgram.TagName, MTConnectTagMap.MainProgram.TagPath,
                program.Value, MTConnectTagMap.MainProgram.ValueType, DataQuality.Good,
                deviceTimestamp, gatewayTimestamp));
            // Running program falls back to main when no SubProgram reported.
            var running = TryGet(flat, "SubProgram", out var sub) ? sub.Value : program.Value;
            points.Add(factory.CreatePoint(
                MTConnectTagMap.RunningProgram.TagName, MTConnectTagMap.RunningProgram.TagPath,
                running, MTConnectTagMap.RunningProgram.ValueType, DataQuality.Good,
                deviceTimestamp, gatewayTimestamp));
            hadValue = true;
        }

        // Spindle
        if (TryGetDouble(flat, MTConnectSemanticMap.SpindleSpeed.StreamElementNames, out var spindleSpeed))
        {
            points.Add(factory.CreatePoint(
                MTConnectTagMap.SpindleSpeed.TagName, MTConnectTagMap.SpindleSpeed.TagPath,
                spindleSpeed, MTConnectTagMap.SpindleSpeed.ValueType, DataQuality.Good,
                deviceTimestamp, gatewayTimestamp));
            hadValue = true;
        }
        if (TryGetDouble(flat, MTConnectSemanticMap.SpindleLoad.StreamElementNames, out var spindleLoad))
        {
            points.Add(factory.CreatePoint(
                MTConnectTagMap.SpindleLoad.TagName, MTConnectTagMap.SpindleLoad.TagPath,
                spindleLoad, MTConnectTagMap.SpindleLoad.ValueType, DataQuality.Good,
                deviceTimestamp, gatewayTimestamp));
            hadValue = true;
        }

        // Feed rate
        if (TryGetDouble(flat, MTConnectSemanticMap.FeedRate.StreamElementNames, out var feed))
        {
            points.Add(factory.CreatePoint(
                MTConnectTagMap.FeedRate.TagName, MTConnectTagMap.FeedRate.TagPath,
                feed, MTConnectTagMap.FeedRate.ValueType, DataQuality.Good,
                deviceTimestamp, gatewayTimestamp));
            hadValue = true;
        }

        // Production counters
        if (TryGetLong(flat, MTConnectSemanticMap.PartsCount.StreamElementNames, out var partCount))
        {
            points.Add(factory.CreatePoint(
                MTConnectTagMap.PartsCount.TagName, MTConnectTagMap.PartsCount.TagPath,
                partCount, MTConnectTagMap.PartsCount.ValueType, DataQuality.Good,
                deviceTimestamp, gatewayTimestamp));
            hadValue = true;
        }
        if (TryGetDouble(flat, MTConnectSemanticMap.CycleTime.StreamElementNames, out var cycle))
        {
            points.Add(factory.CreatePoint(
                MTConnectTagMap.CycleTime.TagName, MTConnectTagMap.CycleTime.TagPath,
                cycle, MTConnectTagMap.CycleTime.ValueType, DataQuality.Good,
                deviceTimestamp, gatewayTimestamp));
            hadValue = true;
        }

        // Axis positions — iterate every Position sample and bucket by its
        // name attribute. Supports both ACTUAL and MACHINE subTypes.
        hadValue |= EmitAxisPositions(streams, ns, factory, points, deviceTimestamp, gatewayTimestamp);

        // Alarms — count active Fault conditions, report first fault text.
        var (alarmCount, firstFault) = ExtractAlarms(streams, ns);
        points.Add(factory.CreatePoint(
            MTConnectTagMap.AlarmCount.TagName, MTConnectTagMap.AlarmCount.TagPath,
            alarmCount, MTConnectTagMap.AlarmCount.ValueType, DataQuality.Good,
            deviceTimestamp, gatewayTimestamp));
        points.Add(factory.CreatePoint(
            MTConnectTagMap.FirstFaultMessage.TagName, MTConnectTagMap.FirstFaultMessage.TagPath,
            firstFault, MTConnectTagMap.FirstFaultMessage.ValueType, DataQuality.Good,
            deviceTimestamp, gatewayTimestamp));
        hadValue = true;

        return hadValue;
    }

    // ---- Helpers ---------------------------------------------------------

    /// <summary>
    /// One MTConnect data-item reading — the element's text value plus the
    /// <c>subType</c> and <c>name</c> attributes the parser cares about.
    /// </summary>
    private readonly record struct DataItem(string Value, string? SubType, string? Name);

    private static void CollectEventsAndSamples(
        XElement stream, XNamespace ns,
        Dictionary<string, DataItem> sink)
    {
        foreach (var section in stream.Descendants(ns + "Events").Concat(stream.Descendants(ns + "Samples")))
        {
            foreach (var element in section.Elements())
            {
                var value = element.Value?.Trim() ?? string.Empty;
                if (string.Equals(value, UnavailableSentinel, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                var localName = element.Name.LocalName;
                var subType = element.Attribute("subType")?.Value;
                var name = element.Attribute("name")?.Value;
                var item = new DataItem(value, subType, name);

                // Primary key: local element name (e.g., "Execution",
                // "SpindleSpeed"). First-writer-wins so the first ComponentStream
                // that reports a given item controls the reading.
                if (!sink.ContainsKey(localName))
                {
                    sink[localName] = item;
                }
                // Secondary key: the element's name attribute when set
                // (handles agents that name items like name="Xact").
                if (!string.IsNullOrEmpty(name) && !sink.ContainsKey(name))
                {
                    sink[name] = item;
                }
                // Tertiary key: element-name + subType (e.g., "Position_ACTUAL").
                if (!string.IsNullOrEmpty(subType))
                {
                    var qualified = $"{localName}_{subType}";
                    if (!sink.ContainsKey(qualified))
                    {
                        sink[qualified] = item;
                    }
                }
            }
        }
    }

    private static bool EmitAxisPositions(
        List<XElement> streams, XNamespace ns,
        CanonicalDataPointFactory factory, List<CanonicalDataPoint> points,
        DateTime deviceTimestamp, DateTime gatewayTimestamp)
    {
        var emitted = false;
        // Walk every <Position> sample directly (rather than through the
        // flat lookup) so we don't miss multiple axes with distinct names.
        foreach (var stream in streams)
        {
            foreach (var pos in stream.Descendants(ns + "Position"))
            {
                var axisName = pos.Attribute("name")?.Value;
                if (string.IsNullOrEmpty(axisName))
                {
                    continue;
                }
                var value = pos.Value?.Trim() ?? string.Empty;
                if (string.Equals(value, UnavailableSentinel, StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrEmpty(value))
                {
                    continue;
                }
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
                {
                    continue;
                }

                var subType = pos.Attribute("subType")?.Value ?? "ACTUAL";
                var entry = string.Equals(subType, "MACHINE", StringComparison.OrdinalIgnoreCase)
                    ? MTConnectTagMap.AxisMachine(axisName)
                    : MTConnectTagMap.AxisAbsolute(axisName);

                points.Add(factory.CreatePoint(
                    entry.TagName, entry.TagPath, numeric, entry.ValueType,
                    DataQuality.Good, deviceTimestamp, gatewayTimestamp));
                emitted = true;
            }
        }
        return emitted;
    }

    private static (int Count, string FirstFault) ExtractAlarms(List<XElement> streams, XNamespace ns)
    {
        var count = 0;
        var first = string.Empty;
        foreach (var stream in streams)
        {
            foreach (var condition in stream.Descendants(ns + "Condition"))
            {
                foreach (var child in condition.Elements())
                {
                    if (!string.Equals(child.Name.LocalName, "Fault", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    count++;
                    if (string.IsNullOrEmpty(first))
                    {
                        var msg = child.Value?.Trim() ?? string.Empty;
                        if (string.IsNullOrEmpty(msg))
                        {
                            msg = child.Attribute("nativeCode")?.Value ?? string.Empty;
                        }
                        first = msg;
                    }
                }
            }
        }
        return (count, first);
    }

    private static bool TryGet(Dictionary<string, DataItem> items, string key, out DataItem item)
        => items.TryGetValue(key, out item);

    /// <summary>First item matching any of <paramref name="keys"/> (in order).</summary>
    private static bool TryGetAny(
        Dictionary<string, DataItem> items, IReadOnlyList<string> keys, out DataItem item)
    {
        foreach (var key in keys)
        {
            if (items.TryGetValue(key, out item))
            {
                return true;
            }
        }
        item = default;
        return false;
    }

    private static bool TryGetDouble(
        Dictionary<string, DataItem> items, IReadOnlyList<string> keys, out double value)
    {
        foreach (var key in keys)
        {
            if (items.TryGetValue(key, out var item)
                && double.TryParse(item.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }
        value = default;
        return false;
    }

    private static bool TryGetLong(
        Dictionary<string, DataItem> items, IReadOnlyList<string> keys, out long value)
    {
        foreach (var key in keys)
        {
            if (items.TryGetValue(key, out var item)
                && long.TryParse(item.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
        }
        value = default;
        return false;
    }

    /// <summary>
    /// Map the MTConnect Execution enumeration into the canonical run-state
    /// vocabulary the FOCAS2 adapter uses, so downstream consumers see
    /// consistent values regardless of protocol.
    /// </summary>
    internal static string MapExecutionToRunState(string execution) =>
        execution.ToUpperInvariant() switch
        {
            "ACTIVE" => "Running",
            "INTERRUPTED" or "FEED_HOLD" or "OPTIONAL_STOP" => "Hold",
            "PROGRAM_STOPPED" or "STOPPED" => "Stop",
            "READY" => "Reset",
            _ => $"Unknown({execution})",
        };

    /// <summary>
    /// Map MTConnect ControllerMode into the canonical auto-mode vocabulary
    /// (matches the FOCAS2 StatusCollector values).
    /// </summary>
    internal static string MapControllerMode(string mode) =>
        mode.ToUpperInvariant() switch
        {
            "AUTOMATIC" => "MEM",
            "MANUAL" => "JOG",
            "MANUAL_DATA_INPUT" => "MDI",
            "SEMI_AUTOMATIC" => "HANDLE",
            "EDIT" => "EDIT",
            _ => $"Unknown({mode})",
        };
}
