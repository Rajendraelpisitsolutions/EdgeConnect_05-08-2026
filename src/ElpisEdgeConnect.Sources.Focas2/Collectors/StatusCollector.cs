// ============================================================================
// File: Collectors/StatusCollector.cs
// Purpose: Reads CNC status (run state, auto mode, emergency, motion, alarm)
//          and emits 5 canonical data points.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.2
// ============================================================================

using ElpisEdgeConnect.Core.Model;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Sources.Focas2.Collectors;

/// <summary>
/// Collects CNC status information: run state, auto mode, emergency stop,
/// motion state, and alarm flag. Consumes a cached <see cref="OdbStatusInfo"/>
/// if available, otherwise reads it fresh from the controller.
/// </summary>
internal sealed class StatusCollector(IFocas2Api api, ILogger logger)
{
    /// <summary>
    /// Collect status data and append canonical points to <paramref name="points"/>.
    /// </summary>
    internal void Collect(
        ushort handle,
        OdbStatusInfo? cachedStatInfo,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        OdbStatusInfo stat;
        if (cachedStatInfo.HasValue)
        {
            stat = cachedStatInfo.Value;
        }
        else
        {
            short ret = api.ReadStatusInfo(handle, out stat);
            ThrowIfFatal(ret, nameof(api.ReadStatusInfo));
            if (ret != (short)Focas2ErrorCode.EW_OK)
            {
                logger.LogDebug("ReadStatusInfo returned {ErrorCode}, skipping status", (Focas2ErrorCode)ret);
                return;
            }
        }

        // Run state mapping
        string runState = stat.Run switch
        {
            0 => "Reset",
            1 => "Stop",
            2 => "Hold",
            3 => "Running",
            4 => "MSTR",
            _ => $"Unknown({stat.Run})",
        };

        // Override to Emergency if E-stop is active
        bool emergency = stat.Emergency == 1;
        if (emergency)
        {
            runState = "Emergency";
        }

        // Auto mode mapping
        string autoMode = stat.Aut switch
        {
            0 => "MDI",
            1 => "MEM",
            3 => "EDIT",
            4 => "HANDLE",
            5 => "JOG",
            6 => "TJOG",
            7 => "THND",
            _ => $"Unknown({stat.Aut})",
        };

        // Motion state mapping
        string motionState = stat.Motion switch
        {
            0 => "None",
            1 => "Motion",
            2 => "Dwell",
            _ => $"Unknown({stat.Motion})",
        };

        var rs = Focas2TagMap.RunState;
        points.Add(factory.CreatePoint(rs.TagName, rs.TagPath, runState,
            rs.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));

        var am = Focas2TagMap.AutoMode;
        points.Add(factory.CreatePoint(am.TagName, am.TagPath, autoMode,
            am.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));

        var es = Focas2TagMap.EmergencyStop;
        points.Add(factory.CreatePoint(es.TagName, es.TagPath, emergency,
            es.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));

        var ms = Focas2TagMap.MotionState;
        points.Add(factory.CreatePoint(ms.TagName, ms.TagPath, motionState,
            ms.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));

        var af = Focas2TagMap.AlarmFlag;
        points.Add(factory.CreatePoint(af.TagName, af.TagPath, (int)stat.Alarm,
            af.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));
    }

    private static void ThrowIfFatal(short ret, string function)
    {
        var code = (Focas2ErrorCode)ret;
        if (code is Focas2ErrorCode.EW_SOCKET or Focas2ErrorCode.EW_HANDLE)
        {
            throw new Focas2FatalException(code, function);
        }
    }
}
