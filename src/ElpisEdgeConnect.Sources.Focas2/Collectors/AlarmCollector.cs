// ============================================================================
// File: Collectors/AlarmCollector.cs
// Purpose: Reads alarm status and active alarm messages, emits AlarmCount and
//          AlarmsActive (structured alarm list) points.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.2
// ============================================================================

using ElpisEdgeConnect.Core.Model;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Sources.Focas2.Collectors;

/// <summary>
/// Collects active alarm information: alarm status bitmask, alarm messages,
/// and emits structured alarm data as canonical points.
/// </summary>
internal sealed class AlarmCollector(IFocas2Api api, ILogger logger)
{
    private const short MaxAlarms = 10;

    /// <summary>
    /// Collect alarm data and append canonical points to <paramref name="points"/>.
    /// </summary>
    internal void Collect(
        ushort handle,
        IReadOnlyList<string>? axisNames,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        // Read alarm status bitmask
        short ret = api.ReadAlarmStatus(handle, out var alarmStatus);
        ThrowIfFatal(ret, nameof(api.ReadAlarmStatus));
        if (ret != (short)Focas2ErrorCode.EW_OK)
        {
            logger.LogDebug("ReadAlarmStatus returned {ErrorCode}, skipping alarms", (Focas2ErrorCode)ret);
            return;
        }

        // If no alarms active, emit zero count and return
        if (alarmStatus.Data == 0)
        {
            var ac = Focas2TagMap.AlarmCount;
            points.Add(factory.CreatePoint(ac.TagName, ac.TagPath, 0,
                ac.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));
            return;
        }

        // Read alarm messages (type=-1 means all types)
        short numAlarms = MaxAlarms;
        var alarmMessages = new OdbAlarmMessage[MaxAlarms];
        ret = api.ReadAlarmMessages(handle, -1, ref numAlarms, alarmMessages);
        ThrowIfFatal(ret, nameof(api.ReadAlarmMessages));
        if (ret != (short)Focas2ErrorCode.EW_OK)
        {
            logger.LogDebug("ReadAlarmMessages returned {ErrorCode}, emitting count only", (Focas2ErrorCode)ret);
            var ac = Focas2TagMap.AlarmCount;
            points.Add(factory.CreatePoint(ac.TagName, ac.TagPath, 1,
                ac.ValueType, DataQuality.Uncertain, deviceTimestamp, gatewayTimestamp,
                qualityReason: "Alarm status set but messages unreadable"));
            return;
        }

        // Build structured alarm list
        var alarmList = new List<Dictionary<string, object>>(numAlarms);
        for (int i = 0; i < numAlarms; i++)
        {
            ref var alarm = ref alarmMessages[i];
            string axisName = ResolveAxisName(alarm.Axis, axisNames);
            alarmList.Add(new Dictionary<string, object>
            {
                ["alarmNumber"] = alarm.AlarmNo,
                ["type"] = MapAlarmType(alarm.Type),
                ["message"] = alarm.AlarmMessage?.TrimEnd('\0') ?? "",
                ["axis"] = axisName,
            });
        }

        var aa = Focas2TagMap.AlarmsActive;
        points.Add(factory.CreatePoint(aa.TagName, aa.TagPath, alarmList,
            aa.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));

        var acTag = Focas2TagMap.AlarmCount;
        points.Add(factory.CreatePoint(acTag.TagName, acTag.TagPath, numAlarms,
            acTag.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));
    }

    private static string ResolveAxisName(short axisIndex, IReadOnlyList<string>? axisNames)
    {
        if (axisIndex <= 0) return "";
        int idx = axisIndex - 1; // FOCAS2 axis index is 1-based
        if (axisNames != null && idx < axisNames.Count) return axisNames[idx];
        return $"Axis{axisIndex}";
    }

    private static string MapAlarmType(short type) => type switch
    {
        0 => "PS",
        1 => "OT",
        2 => "OH",
        3 => "SV",
        4 => "SR",
        5 => "MC",
        6 => "SP",
        7 => "DS",
        8 => "IE",
        9 => "EX",
        10 => "PC",
        _ => "Unknown",
    };

    private static void ThrowIfFatal(short ret, string function)
    {
        var code = (Focas2ErrorCode)ret;
        if (code is Focas2ErrorCode.EW_SOCKET or Focas2ErrorCode.EW_HANDLE)
        {
            throw new Focas2FatalException(code, function);
        }
    }
}
