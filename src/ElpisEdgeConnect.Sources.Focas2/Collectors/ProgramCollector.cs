// ============================================================================
// File: Collectors/ProgramCollector.cs
// Purpose: Reads current main and running program numbers, emits 2 points.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.2
// ============================================================================

using ElpisEdgeConnect.Core.Model;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Sources.Focas2.Collectors;

/// <summary>
/// Collects the main program number and currently running program number
/// from the CNC controller.
/// </summary>
internal sealed class ProgramCollector(IFocas2Api api, ILogger logger)
{
    /// <summary>
    /// Collect program numbers and append canonical points to <paramref name="points"/>.
    /// </summary>
    internal void Collect(
        ushort handle,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        short ret = api.ReadProgramNumber(handle, out var progNum);
        ThrowIfFatal(ret, nameof(api.ReadProgramNumber));
        if (ret != (short)Focas2ErrorCode.EW_OK)
        {
            logger.LogDebug("ReadProgramNumber returned {ErrorCode}, skipping program", (Focas2ErrorCode)ret);
            return;
        }

        string mainProg = $"O{progNum.MainProgram}";

        // Validate running program: must be 0-99999, fallback to main program
        int runningNum = progNum.RunningProgram;
        string runningProg = (runningNum >= 0 && runningNum <= 99999)
            ? $"O{runningNum}"
            : mainProg;

        var mp = Focas2TagMap.MainProgram;
        points.Add(factory.CreatePoint(mp.TagName, mp.TagPath, mainProg,
            mp.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));

        var rp = Focas2TagMap.RunningProgram;
        points.Add(factory.CreatePoint(rp.TagName, rp.TagPath, runningProg,
            rp.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));
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
