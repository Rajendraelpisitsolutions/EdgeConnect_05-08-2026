// ============================================================================
// File: Collectors/ToolCollector.cs
// Purpose: Reads current tool number (T-code), tool offset configuration and
//          values, and tool life data. Emits FLAT per-tool / per-group tags
//          (Tool.CurrentNo, Tool.{n}.GeomRad, ToolLife.{g}.Counter, ...) that
//          the EREMOS V2 ToolLifeIngestionService consumes directly.
//
// Tag-shape rationale (see Focas2TagMap "Tool" section header): EREMOS detects
// tool topics by the literal "/Tool." / "/ToolLife." marker and matches field
// names exactly. A single structured object tag is NOT ingestable; flat scalar
// tags are. Offset value fields use the gateway short form (WearLen/GeomRad/...)
// which EREMOS normalizes; tool-life fields use EREMOS's exact names
// (Counter/Limit/CountType/Status/ToolNo/LifeRemain).
//
// FOCAS offset-memory-type → field mapping (ported from the proven legacy
// reader src/ElpisEdgeConnect/DataSources/Focas2DllDataSource.cs):
//   Type A (single) : cnc_rdtofs type 0 -> WearLen
//   Type B (2)      : 0 -> WearLen, 1 -> GeomLen
//   Type C (4)      : 0 -> WearLen, 1 -> WearRad, 2 -> GeomLen, 3 -> GeomRad
// Raw integer values are scaled /1000 (IS-B metric increment), matching legacy.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.2
// ============================================================================

using ElpisEdgeConnect.Core.Model;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Sources.Focas2.Collectors;

/// <summary>
/// Collects tool data: current T-code (via modal or macro), tool offset
/// memory configuration and per-tool values, and tool life management data.
/// Emits flat per-tool / per-group canonical points for EREMOS V2 ingestion.
/// </summary>
internal sealed class ToolCollector(IFocas2Api api, ILogger logger)
{
    private const int MaxTools = 200;
    private const double OffsetScale = 1000.0; // IS-B metric increment (raw -> mm)

    /// <summary>
    /// Collect tool data and append canonical points to <paramref name="points"/>.
    /// </summary>
    internal void Collect(
        ushort handle,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        int currentTool = CollectToolNumber(handle, factory, points, deviceTimestamp, gatewayTimestamp);
        CollectToolOffsets(handle, factory, points, deviceTimestamp, gatewayTimestamp, currentTool);
        CollectToolLife(handle, factory, points, deviceTimestamp, gatewayTimestamp);
    }

    /// <summary>Read the active T-code. Returns the tool number, or -1 if unavailable.</summary>
    private int CollectToolNumber(
        ushort handle,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        int toolNumber = -1;

        // Try modal data type 108 (T-code) first
        var modalData = new byte[256];
        short ret = api.ReadModal(handle, 108, (short)modalData.Length, modalData);
        ThrowIfFatal(ret, nameof(api.ReadModal));
        if (ret == (short)Focas2ErrorCode.EW_OK && modalData.Length >= 8)
        {
            // Modal data: offset 4 = 4-byte integer (T-code value)
            toolNumber = BitConverter.ToInt32(modalData, 4);
        }

        // Fallback: read macro variable #4120
        if (toolNumber <= 0)
        {
            ret = api.ReadMacro(handle, 4120, 10, out var macro);
            ThrowIfFatal(ret, nameof(api.ReadMacro));
            if (ret == (short)Focas2ErrorCode.EW_OK)
            {
                toolNumber = (int)(macro.McVal / Math.Pow(10, macro.McDig));
            }
        }

        if (toolNumber > 0)
        {
            var tn = Focas2TagMap.ToolCurrentNumber;
            points.Add(factory.CreatePoint(tn.TagName, tn.TagPath, toolNumber,
                tn.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));
            return toolNumber;
        }

        logger.LogDebug("Could not read tool number from modal or macro");
        return -1;
    }

    private void CollectToolOffsets(
        ushort handle,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp,
        int currentTool)
    {
        // Determine offset type and count
        short ofsType = 0; // 0=A (single), 1=B (wear+geometry), 2=C (4 types)
        short useNo = 0;
        bool gotInfo = false;

        short ret = api.ReadToolOffsetInfo2(handle, out ofsType, out useNo);
        ThrowIfFatal(ret, nameof(api.ReadToolOffsetInfo2));
        if (ret == (short)Focas2ErrorCode.EW_OK)
        {
            gotInfo = true;
        }
        else
        {
            // Fallback: get count only, assume type A
            ret = api.ReadToolOffsetInfo(handle, out useNo);
            ThrowIfFatal(ret, nameof(api.ReadToolOffsetInfo));
            if (ret == (short)Focas2ErrorCode.EW_OK)
            {
                ofsType = 0;
                gotInfo = true;
            }
        }

        if (!gotInfo)
        {
            logger.LogDebug("Could not read tool offset info, skipping offsets");
            return;
        }

        string typeLabel = ofsType switch { 0 => "A", 1 => "B", 2 => "C", _ => $"Unknown({ofsType})" };
        int toolCount = Math.Min((int)useNo, MaxTools);

        // Emit offset type and count
        var ot = Focas2TagMap.ToolOffsetType;
        points.Add(factory.CreatePoint(ot.TagName, ot.TagPath, typeLabel,
            ot.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));

        var oc = Focas2TagMap.ToolOffsetCount;
        points.Add(factory.CreatePoint(oc.TagName, oc.TagPath, (int)useNo,
            oc.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));

        if (toolCount <= 0)
            return;

        // Read each offset sub-type that the memory type exposes, then emit one
        // flat tag per (tool, field) for non-zero values only.
        switch (ofsType)
        {
            case 1: // Memory B — wear + geometry (length only)
                EmitOffsetField(handle, 0, toolCount, Focas2TagMap.OffsetWearLength,
                    factory, points, deviceTimestamp, gatewayTimestamp);
                EmitOffsetField(handle, 1, toolCount, Focas2TagMap.OffsetGeometryLength,
                    factory, points, deviceTimestamp, gatewayTimestamp);
                break;
            case 2: // Memory C — wear len/rad + geometry len/rad
                EmitOffsetField(handle, 0, toolCount, Focas2TagMap.OffsetWearLength,
                    factory, points, deviceTimestamp, gatewayTimestamp);
                EmitOffsetField(handle, 1, toolCount, Focas2TagMap.OffsetWearRadius,
                    factory, points, deviceTimestamp, gatewayTimestamp);
                EmitOffsetField(handle, 2, toolCount, Focas2TagMap.OffsetGeometryLength,
                    factory, points, deviceTimestamp, gatewayTimestamp);
                EmitOffsetField(handle, 3, toolCount, Focas2TagMap.OffsetGeometryRadius,
                    factory, points, deviceTimestamp, gatewayTimestamp);
                break;
            default: // Memory A — single combined offset
                EmitOffsetField(handle, 0, toolCount, Focas2TagMap.OffsetWearLength,
                    factory, points, deviceTimestamp, gatewayTimestamp);
                break;
        }

        // Flag the active tool's offset so EREMOS can mark it (Tool.{n}.IsActive).
        if (currentTool > 0 && currentTool <= toolCount)
        {
            var act = Focas2TagMap.ToolOffsetActive(currentTool);
            points.Add(factory.CreatePoint(act.TagName, act.TagPath, true,
                act.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));
        }
    }

    /// <summary>
    /// Batch-read one offset sub-type for all tools and emit a flat tag per
    /// non-zero value. Falls back to individual reads if the batch read fails.
    /// </summary>
    private void EmitOffsetField(
        ushort handle,
        short focasType,
        int toolCount,
        string field,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        bool batchOk = false;

        // Batch read in chunks of 50. Buffer layout (ODBTOFS range): 6-byte
        // header (datano_s, datano_e, type) then a 4-byte long per value.
        for (int start = 1; start <= toolCount; start += 50)
        {
            int end = Math.Min(start + 49, toolCount);
            int count = end - start + 1;
            int bufLen = 6 + 4 * count;
            var buffer = new byte[bufLen];

            short ret = api.ReadToolOffsetRange(handle, (short)start, focasType, (short)end, (short)bufLen, buffer);
            ThrowIfFatal(ret, nameof(api.ReadToolOffsetRange));
            if (ret != (short)Focas2ErrorCode.EW_OK)
            {
                logger.LogDebug("ReadToolOffsetRange(type={Type}, {Start}-{End}) returned {Code}",
                    focasType, start, end, (Focas2ErrorCode)ret);
                batchOk = false;
                break;
            }

            batchOk = true;
            for (int i = 0; i < count; i++)
            {
                int raw = BitConverter.ToInt32(buffer, 6 + i * 4);
                if (raw != 0)
                    EmitOffsetValue(start + i, field, raw, factory, points, deviceTimestamp, gatewayTimestamp);
            }
        }

        if (batchOk)
            return;

        // Fallback: individual reads (some controllers don't support cnc_rdtofsr).
        for (short toolNum = 1; toolNum <= toolCount; toolNum++)
        {
            short ret = api.ReadToolOffset(handle, toolNum, focasType, 8, out var tofs);
            ThrowIfFatal(ret, nameof(api.ReadToolOffset));
            if (ret == (short)Focas2ErrorCode.EW_OK && tofs.Data != 0)
                EmitOffsetValue(toolNum, field, tofs.Data, factory, points, deviceTimestamp, gatewayTimestamp);
        }
    }

    private static void EmitOffsetValue(
        int toolNumber,
        string field,
        int raw,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        var entry = Focas2TagMap.ToolOffset(toolNumber, field);
        points.Add(factory.CreatePoint(entry.TagName, entry.TagPath, raw / OffsetScale,
            entry.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp, unit: entry.Unit));
    }

    private void CollectToolLife(
        ushort handle,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        // Probe tool-life availability and the count type (count vs minutes).
        var infoBuffer = new byte[6];
        short ret = api.ReadToolLifeInfo(handle, infoBuffer);
        ThrowIfFatal(ret, nameof(api.ReadToolLifeInfo));

        bool enabled = false;
        string countTypeStr = "Count";
        if (ret == (short)Focas2ErrorCode.EW_OK)
        {
            short maxGroups = BitConverter.ToInt16(infoBuffer, 0);
            short countType = BitConverter.ToInt16(infoBuffer, 2);
            enabled = maxGroups > 0;
            countTypeStr = countType == 1 ? "Minutes" : "Count";
        }

        var en = Focas2TagMap.ToolLifeEnabled;
        points.Add(factory.CreatePoint(en.TagName, en.TagPath, enabled,
            en.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));

        if (!enabled)
            return;

        ret = api.ReadToolLifeGroupCount(handle, out short numGroups);
        ThrowIfFatal(ret, nameof(api.ReadToolLifeGroupCount));
        if (ret != (short)Focas2ErrorCode.EW_OK || numGroups <= 0)
        {
            logger.LogDebug("ReadToolLifeGroupCount returned {Code}, numGroups={Num}", (Focas2ErrorCode)ret, numGroups);
            return;
        }

        ret = api.ReadToolLifeUseGroup(handle, out int activeGroup);
        ThrowIfFatal(ret, nameof(api.ReadToolLifeUseGroup));
        if (ret != (short)Focas2ErrorCode.EW_OK)
            activeGroup = 0;

        int groupsToRead = Math.Min((int)numGroups, 50);
        for (int g = 1; g <= groupsToRead; g++)
        {
            // cnc_rdtlgrp layout: [4 groupNo][4 nTool][4 life_count][4 life_limit][...]
            var grpBuffer = new byte[128];
            ret = api.ReadToolLifeGroup(handle, g, grpBuffer);
            ThrowIfFatal(ret, nameof(api.ReadToolLifeGroup));
            if (ret != (short)Focas2ErrorCode.EW_OK)
                continue;

            int groupNo = BitConverter.ToInt32(grpBuffer, 0);
            if (groupNo <= 0) groupNo = g;
            int nTools = BitConverter.ToInt32(grpBuffer, 4);
            int lifeCount = BitConverter.ToInt32(grpBuffer, 8);
            int lifeLimit = BitConverter.ToInt32(grpBuffer, 12);

            string status = groupNo == activeGroup
                ? "Active"
                : (lifeLimit > 0 && lifeCount >= lifeLimit ? "Expired" : "Standby");
            double remainPct = lifeLimit > 0
                ? Math.Max(0, (1.0 - (double)lifeCount / lifeLimit) * 100.0)
                : 100.0;

            EmitLife(groupNo, Focas2TagMap.LifeToolNo, nTools, CanonicalValueType.Integer,
                factory, points, deviceTimestamp, gatewayTimestamp);
            EmitLife(groupNo, Focas2TagMap.LifeCounter, lifeCount, CanonicalValueType.Integer,
                factory, points, deviceTimestamp, gatewayTimestamp);
            EmitLife(groupNo, Focas2TagMap.LifeLimit, lifeLimit, CanonicalValueType.Integer,
                factory, points, deviceTimestamp, gatewayTimestamp);
            EmitLife(groupNo, Focas2TagMap.LifeCountType, countTypeStr, CanonicalValueType.String,
                factory, points, deviceTimestamp, gatewayTimestamp);
            EmitLife(groupNo, Focas2TagMap.LifeStatus, status, CanonicalValueType.String,
                factory, points, deviceTimestamp, gatewayTimestamp);
            EmitLife(groupNo, Focas2TagMap.LifeRemain, remainPct, CanonicalValueType.Double,
                factory, points, deviceTimestamp, gatewayTimestamp);
        }
    }

    private static void EmitLife(
        int groupNo,
        string field,
        object value,
        CanonicalValueType valueType,
        CanonicalDataPointFactory factory,
        List<CanonicalDataPoint> points,
        DateTime deviceTimestamp,
        DateTime gatewayTimestamp)
    {
        var entry = Focas2TagMap.ToolLifeGroup(groupNo, field, valueType);
        points.Add(factory.CreatePoint(entry.TagName, entry.TagPath, value,
            entry.ValueType, DataQuality.Good, deviceTimestamp, gatewayTimestamp));
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
