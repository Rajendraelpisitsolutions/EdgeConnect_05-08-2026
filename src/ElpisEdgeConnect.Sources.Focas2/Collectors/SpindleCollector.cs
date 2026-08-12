// ============================================================================
// File: Collectors/SpindleCollector.cs
// Purpose: Reads feed rate, spindle speed, and spindle load, emits 3 points.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.2
// ============================================================================

using ElpisEdgeConnect.Core.Model;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Sources.Focas2.Collectors;

/// <summary>
/// Collects feed rate, spindle speed, and spindle load data from the CNC.
/// </summary>
internal sealed class SpindleCollector(IFocas2Api api, ILogger logger)
{
    /// <summary>
    /// Collect spindle/feed data and append canonical points to <paramref name="points"/>.
    /// </summary>
    internal void Collect(
        ushort handle,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        // Feed rate
        short ret = api.ReadActualFeedRate(handle, out var feed);
        ThrowIfFatal(ret, nameof(api.ReadActualFeedRate));
        if (ret == (short)Focas2ErrorCode.EW_OK)
        {
            var fr = Focas2TagMap.FeedRate;
            points.Add(factory.CreatePoint(fr.TagName, fr.TagPath, (double)feed.Data,
                fr.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp,
                unit: fr.Unit));
        }
        else
        {
            logger.LogDebug("ReadActualFeedRate returned {ErrorCode}, skipping", (Focas2ErrorCode)ret);
        }

        // Spindle speed
        ret = api.ReadActualSpindleSpeed(handle, out var speed);
        ThrowIfFatal(ret, nameof(api.ReadActualSpindleSpeed));
        if (ret == (short)Focas2ErrorCode.EW_OK)
        {
            var ss = Focas2TagMap.SpindleSpeed;
            points.Add(factory.CreatePoint(ss.TagName, ss.TagPath, (double)speed.Data,
                ss.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp,
                unit: ss.Unit));
        }
        else
        {
            logger.LogDebug("ReadActualSpindleSpeed returned {ErrorCode}, skipping", (Focas2ErrorCode)ret);
        }

        // Spindle load (spindle #0)
        ret = api.ReadSpindleLoad(handle, 0, out var load);
        ThrowIfFatal(ret, nameof(api.ReadSpindleLoad));
        if (ret == (short)Focas2ErrorCode.EW_OK)
        {
            double loadPercent = load.Data[0] / 10.0;
            var sl = Focas2TagMap.SpindleLoad;
            points.Add(factory.CreatePoint(sl.TagName, sl.TagPath, loadPercent,
                sl.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp,
                unit: sl.Unit));
        }
        else
        {
            logger.LogDebug("ReadSpindleLoad returned {ErrorCode}, skipping", (Focas2ErrorCode)ret);
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
