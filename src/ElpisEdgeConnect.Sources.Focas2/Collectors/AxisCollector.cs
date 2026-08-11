// ============================================================================
// File: Collectors/AxisCollector.cs
// Purpose: Reads absolute, machine, relative, and distance-to-go positions
//          for all configured axes and emits 4 points per axis.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.2
// ============================================================================

using ElpisEdgeConnect.Core.Model;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Sources.Focas2.Collectors;

/// <summary>
/// Collects axis position data (absolute, machine, relative, distance-to-go)
/// for all configured axes and scales by the decimal position reported by
/// the controller.
/// </summary>
internal sealed class AxisCollector(IFocas2Api api, ILogger logger)
{
    private const int DefaultDecimalPlaces = 3;

    /// <summary>
    /// Collect axis positions and append canonical points to <paramref name="points"/>.
    /// </summary>
    internal void Collect(
        ushort handle,
        IReadOnlyList<string> axisNames,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        if (axisNames.Count == 0) return;

        short bufLen = (short)(4 + 4 + 4 * Focas2Interop.MAX_AXIS);

        // Read all four position types for all axes
        ReadAndEmitPositions(handle, axisNames, bufLen, factory, points,
            deviceTimestamp, gatewayTimestamp,
            api.ReadAbsolutePosition, Focas2TagMap.AxisAbsolute, "ReadAbsolutePosition");

        ReadAndEmitPositions(handle, axisNames, bufLen, factory, points,
            deviceTimestamp, gatewayTimestamp,
            api.ReadMachinePosition, Focas2TagMap.AxisMachine, "ReadMachinePosition");

        ReadAndEmitPositions(handle, axisNames, bufLen, factory, points,
            deviceTimestamp, gatewayTimestamp,
            api.ReadRelativePosition, Focas2TagMap.AxisRelative, "ReadRelativePosition");

        ReadAndEmitPositions(handle, axisNames, bufLen, factory, points,
            deviceTimestamp, gatewayTimestamp,
            api.ReadDistanceToGo, Focas2TagMap.AxisDistanceToGo, "ReadDistanceToGo");
    }

    private void ReadAndEmitPositions(
        ushort handle,
        IReadOnlyList<string> axisNames,
        short bufLen,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp,
        ReadPositionFunc readFunc,
        Func<string, TagMapEntry> tagFunc,
        string functionName)
    {
        short ret = readFunc(handle, Focas2Interop.ALL_AXES, bufLen, out var axisData);
        ThrowIfFatal(ret, functionName);
        if (ret != (short)Focas2ErrorCode.EW_OK)
        {
            logger.LogDebug("{Function} returned {ErrorCode}, skipping", functionName, (Focas2ErrorCode)ret);
            return;
        }

        for (int i = 0; i < axisNames.Count; i++)
        {
            int rawValue = axisData.Data[i];
            int decimalPlaces = (axisData.Decimal != null && i < axisData.Decimal.Length)
                ? axisData.Decimal[i]
                : DefaultDecimalPlaces;

            double scaled = rawValue / Math.Pow(10, decimalPlaces);

            var tag = tagFunc(axisNames[i]);
            points.Add(factory.CreatePoint(tag.TagName, tag.TagPath, scaled,
                tag.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp,
                unit: tag.Unit));
        }
    }

    private delegate short ReadPositionFunc(ushort handle, short axisNum, short length, out OdbAxisData position);

    private static void ThrowIfFatal(short ret, string function)
    {
        var code = (Focas2ErrorCode)ret;
        if (code is Focas2ErrorCode.EW_SOCKET or Focas2ErrorCode.EW_HANDLE)
        {
            throw new Focas2FatalException(code, function);
        }
    }
}
