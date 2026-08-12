// ============================================================================
// File: Collectors/ProductionCollector.cs
// Purpose: Reads cycle time (timer type 2) and parts count (param 6712),
//          emits CycleTime and PartsCount points.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.2
// ============================================================================

using ElpisEdgeConnect.Core.Model;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Sources.Focas2.Collectors;

/// <summary>
/// Collects production data: cycle time from the CNC timer and parts count
/// from parameter 6712.
/// </summary>
internal sealed class ProductionCollector(IFocas2Api api, ILogger logger)
{
    /// <summary>
    /// Collect production data and append canonical points to <paramref name="points"/>.
    /// </summary>
    internal void Collect(
        ushort handle,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        // Cycle time (timer type 2)
        short ret = api.ReadTimer(handle, 2, out var timer);
        ThrowIfFatal(ret, nameof(api.ReadTimer));
        if (ret == (short)Focas2ErrorCode.EW_OK)
        {
            double cycleTimeSec = timer.Minute * 60.0 + timer.Msec / 1000.0;
            var ct = Focas2TagMap.CycleTime;
            points.Add(factory.CreatePoint(ct.TagName, ct.TagPath, cycleTimeSec,
                ct.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp,
                unit: ct.Unit));
        }
        else
        {
            logger.LogDebug("ReadTimer(type=2) returned {ErrorCode}, skipping cycle time", (Focas2ErrorCode)ret);
        }

        // Parts count (parameter 6712)
        ret = api.ReadParameter(handle, 6712, 0, 8, out var param);
        ThrowIfFatal(ret, nameof(api.ReadParameter));
        if (ret == (short)Focas2ErrorCode.EW_OK)
        {
            var pc = Focas2TagMap.PartsCount;
            points.Add(factory.CreatePoint(pc.TagName, pc.TagPath, (long)param.LData,
                pc.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));
        }
        else
        {
            logger.LogDebug("ReadParameter(6712) returned {ErrorCode}, skipping parts count", (Focas2ErrorCode)ret);
        }
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
