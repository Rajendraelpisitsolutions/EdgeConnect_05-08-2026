// ============================================================================
// File: Models/MqttPayloadFormatter.cs
// Purpose: Converts CncMachineData into the downstream system's expected
//          MQTT wire format:
//
//          {
//            "DeviceId": "DeviceID_CNC",
//            "Data": [
//              "DeviceName.TagName1,Value,Timestamp,DataType",
//              "DeviceName.TagName2,Value,Timestamp,DataType"
//            ]
//          }
//
//          Each Data entry is a comma-separated string:
//            - DeviceName.TagName  → Machine name + data point identifier
//            - Value               → The raw value (number, string, bool)
//            - Timestamp           → ISO 8601 UTC timestamp
//            - DataType            → Signal data type ID (integer)
//
// SIGNAL DATA TYPE MAP:
//   signalDataTypeId | signalDataTypeName
//   -----------------+--------------------
//           1        | bit
//           2        | int
//           3        | float
//           4        | double
//           5        | word         (used for string/text values)
//           6        | floatA
//           7        | floatB
//           8        | floatC
//           9        | floatD
//          11        | ulong
//          12        | 4Qfp
//          13        | 3Qfp
// ============================================================================

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ElpisEdgeConnect.Models;

/// <summary>
/// Signal data type IDs matching the downstream system's type catalog.
/// Used as the DataType field in the MQTT payload Data[] array entries.
/// </summary>
public static class SignalDataType
{
    public const int Bit    = 1;   // Boolean: 0/1, true/false
    public const int Int    = 2;   // Signed integer (alarm numbers, status codes)
    public const int Float  = 3;   // Single-precision float (loads, percentages)
    public const int Double = 4;   // Double-precision float (positions, speeds, rates)
    public const int Word   = 5;   // String/text values (program names, status text)
    public const int FloatA = 6;
    public const int FloatB = 7;
    public const int FloatC = 8;
    public const int FloatD = 9;
    public const int ULong  = 11;  // Unsigned long (counters, durations)
    public const int Fp4Q   = 12;  // 4-quadrant floating point
    public const int Fp3Q   = 13;  // 3-quadrant floating point
}

/// <summary>
/// Converts CncMachineData into the target MQTT wire format.
/// 
/// OUTPUT FORMAT:
/// {
///   "DeviceId": "CNC-001",
///   "Data": [
///     "LatheBay1.MainProgram,O1234,2026-02-11T10:00:00.000Z,5",
///     "LatheBay1.SpindleSpeed,1200.50,2026-02-11T10:00:00.000Z,4",
///     "LatheBay1.PartsCount,42,2026-02-11T10:00:00.000Z,11",
///     "LatheBay1.AlarmActive,0,2026-02-11T10:00:00.000Z,1"
///   ]
/// }
/// </summary>
public static class MqttPayloadFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Converts CncMachineData to the target wire format JSON bytes.
    /// </summary>
    /// <param name="data">Collected CNC machine data snapshot</param>
    /// <param name="deviceId">Device ID for the payload (from config)</param>
    /// <param name="deviceName">Device name prefix for each tag (from config)</param>
    /// <returns>UTF-8 JSON bytes ready for MQTT publishing</returns>
    public static byte[] FormatPayload(CncMachineData data, string deviceId, string deviceName)
    {
        var timestamp = data.Timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ",
            CultureInfo.InvariantCulture);

        var entries = new List<string>(32); // Pre-size for typical CNC data

        // ── Machine Status ──
        AddEntry(entries, deviceName, "Status", data.Status.ToString(), timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "StatusCode", ((int)data.Status).ToString(), timestamp, SignalDataType.Int);
        AddEntry(entries, deviceName, "CncState_path1_CNC", data.CncState_path1_CNC??"NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "PartsNum_path1_CNC", data.PartsNum_path1_CNC.ToString()?? "NA",timestamp,SignalDataType.Word);
        AddEntry(entries, deviceName, "Disconnect_CNC", data.Disconnect_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Bit);
        AddEntry(entries, deviceName, "Mode_path1_CNC", data.Mode_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "MainProgram_path1_CNC", data.MainProgram_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "ActProgram_path1_CNC", data.ActProgram_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "MainComment_path1_CNC", data.MainComment_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "ActComment_path1_CNC", data.ActComment_path1_CNC ?? "NA", timestamp, SignalDataType.Word);

        AddEntry(entries, deviceName, "SigCUT_path1_CNC", data.SigCUT_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Bit);
        AddEntry(entries, deviceName, "SigSBK_path1_CNC", data.SigSBK_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Bit);
        AddEntry(entries, deviceName, "SigDM00_path1_CNC", data.SigDM00_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Bit);
        AddEntry(entries, deviceName, "SigDM01_path1_CNC", data.SigDM01_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Bit);
        AddEntry(entries, deviceName, "SigMDRN_path1_CNC", data.SigMDRN_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Bit);

        AddEntry(entries, deviceName, "CncWarning_path1_CNC", data.CncWarning_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "CncFan1Status_path1_CNC", data.CncFan1Status_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "CncFan2Status_path1_CNC", data.CncFan2Status_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "CncFan3Status_path1_CNC", data.CncFan3Status_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "CncFan4Status_path1_CNC", data.CncFan4Status_path1_CNC ?? "NA", timestamp, SignalDataType.Word);

        AddEntry(entries, deviceName, "CncBatLow_0_path1_CNC", data.CncBatLow_0_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Bit);
        AddEntry(entries, deviceName, "ApcBatZero_0_path1_CNC", data.ApcBatZero_0_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Bit);
        AddEntry(entries, deviceName, "ApcBatLow_0_path1_CNC", data.ApcBatLow_0_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Bit);
        AddEntry(entries, deviceName, "IndBatZero_0_path1_CNC", data.IndBatZero_0_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Bit);
        AddEntry(entries, deviceName, "SpdBatZero_0_path1_CNC", data.SpdBatZero_0_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Bit);
        AddEntry(entries, deviceName, "SSpdBatZero_0_path1_CNC", data.SSpdBatZero_0_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Bit);

        AddEntry(entries, deviceName, "ServoTemp_0_path1_CNC", data.ServoTemp_0_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "PulseCoderTemp_0_path1_CNC", data.PulseCoderTemp_0_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "ServoLeakResistData_0_path1_CNC", data.ServoLeakResistData_0_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Word);

        AddEntry(entries, deviceName, "InFan1SrvAmpStatus_0_path1_CNC", data.InFan1SrvAmpStatus_0_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "InFan2SrvAmpStatus_0_path1_CNC", data.InFan2SrvAmpStatus_0_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "RadFan1SrvAmpStatus_0_path1_CNC", data.RadFan1SrvAmpStatus_0_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "RadFan2SrvAmpStatus_0_path1_CNC", data.RadFan2SrvAmpStatus_0_path1_CNC ?? "NA", timestamp, SignalDataType.Word);

        AddEntry(entries, deviceName, "InFan1SrvComPwStatus_0_path1_CNC", data.InFan1SrvComPwStatus_0_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "InFan2SrvComPwStatus_0_path1_CNC", data.InFan2SrvComPwStatus_0_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "RadFan1SrvComPwStatus_0_path1_CNC", data.RadFan1SrvComPwStatus_0_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "RadFan2SrvComPwStatus_0_path1_CNC", data.RadFan2SrvComPwStatus_0_path1_CNC ?? "NA", timestamp, SignalDataType.Word);

        AddEntry(entries, deviceName, "CncBatLow_1_path1_CNC", data.CncBatLow_1_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Bit);
        AddEntry(entries, deviceName, "ApcBatZero_1_path1_CNC", data.ApcBatZero_1_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Bit);
        AddEntry(entries, deviceName, "ApcBatLow_1_path1_CNC", data.ApcBatLow_1_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Bit);
        AddEntry(entries, deviceName, "IndBatZero_1_path1_CNC", data.IndBatZero_1_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Bit);
        AddEntry(entries, deviceName, "SpdBatZero_1_path1_CNC", data.SpdBatZero_1_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Bit);
        AddEntry(entries, deviceName, "SSpdBatZero_1_path1_CNC", data.SSpdBatZero_1_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Bit);

        AddEntry(entries, deviceName, "ServoTemp_1_path1_CNC", data.ServoTemp_1_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "PulseCoderTemp_1_path1_CNC", data.PulseCoderTemp_1_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "ServoLeakResistData_1_path1_CNC", data.ServoLeakResistData_1_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Word);

        AddEntry(entries, deviceName, "InFan1SrvAmpStatus_1_path1_CNC", data.InFan1SrvAmpStatus_1_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "InFan2SrvAmpStatus_1_path1_CNC", data.InFan2SrvAmpStatus_1_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "RadFan1SrvAmpStatus_1_path1_CNC", data.RadFan1SrvAmpStatus_1_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "RadFan2SrvAmpStatus_1_path1_CNC", data.RadFan2SrvAmpStatus_1_path1_CNC ?? "NA", timestamp, SignalDataType.Word);

        AddEntry(entries, deviceName, "InFan1SrvComPwStatus_1_path1_CNC", data.InFan1SrvComPwStatus_1_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "InFan2SrvComPwStatus_1_path1_CNC", data.InFan2SrvComPwStatus_1_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "RadFan1SrvComPwStatus_1_path1_CNC", data.RadFan1SrvComPwStatus_1_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "RadFan2SrvComPwStatus_1_path1_CNC", data.RadFan2SrvComPwStatus_1_path1_CNC ?? "NA", timestamp, SignalDataType.Word);

        AddEntry(entries, deviceName, "SpindleTemp_0_path1_CNC", data.SpindleTemp_0_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "SpindleLeakResistData_0_path1_CNC", data.SpindleLeakResistData_0_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "SpindleTotalRev1_0_path1_CNC", data.SpindleTotalRev1_0_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "SpindleTotalRev2_0_path1_CNC", data.SpindleTotalRev2_0_path1_CNC?.ToString() ?? "NA", timestamp, SignalDataType.Word);

        AddEntry(entries, deviceName, "InFan1SpdlComPwStatus_0_path1_CNC", data.InFan1SpdlComPwStatus_0_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "InFan2SpdlComPwStatus_0_path1_CNC", data.InFan2SpdlComPwStatus_0_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "RadFan1SpdlComPwStatus_0_path1_CNC", data.RadFan1SpdlComPwStatus_0_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "RadFan2SpdlComPwStatus_0_path1_CNC", data.RadFan2SpdlComPwStatus_0_path1_CNC ?? "NA", timestamp, SignalDataType.Word);

        AddEntry(entries, deviceName, "InFan1SpindleAmpStatus_0_path1_CNC", data.InFan1SpindleAmpStatus_0_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "InFan2SpindleAmpStatus_0_path1_CNC", data.InFan2SpindleAmpStatus_0_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "RadFan1SpindleAmpStatus_0_path1_CNC", data.RadFan1SpindleAmpStatus_0_path1_CNC ?? "NA", timestamp, SignalDataType.Word);
        AddEntry(entries, deviceName, "RadFan2SpindleAmpStatus_0_path1_CNC", data.RadFan2SpindleAmpStatus_0_path1_CNC ?? "NA", timestamp, SignalDataType.Word);

        // ── FOCAS2 Status (AutoMode, EmergencyStop) ──
        if (data.AutoMode != null)
            AddEntry(entries, deviceName, "AutoMode", data.AutoMode, timestamp, SignalDataType.Word);
        if (data.EmergencyStop.HasValue)
            AddEntry(entries, deviceName, "EmergencyStop", data.EmergencyStop.Value ? "1" : "0", timestamp, SignalDataType.Bit);

        // ── FOCAS2 System Info (cached, included in every payload) ──
        if (data.SystemInfo != null)
        {
            if (!string.IsNullOrEmpty(data.SystemInfo.Series))
                AddEntry(entries, deviceName, "CncSeries", data.SystemInfo.Series, timestamp, SignalDataType.Word);
            if (!string.IsNullOrEmpty(data.SystemInfo.CncType))
                AddEntry(entries, deviceName, "CncType", data.SystemInfo.CncType, timestamp, SignalDataType.Word);
            if (!string.IsNullOrEmpty(data.SystemInfo.Version))
                AddEntry(entries, deviceName, "CncVersion", data.SystemInfo.Version, timestamp, SignalDataType.Word);
            AddEntry(entries, deviceName, "AxisCount", data.SystemInfo.AxisCount.ToString(), timestamp, SignalDataType.Int);
        }

        // ── Program Info ──
        if (data.MainProgram != null)
            AddEntry(entries, deviceName, "MainProgram", data.MainProgram, timestamp, SignalDataType.Word);

        if (data.RunningStatus != null)
            AddEntry(entries, deviceName, "RunningStatus", data.RunningStatus, timestamp, SignalDataType.Word);

        // ── Axis Positions ──
        foreach (var (axisName, axis) in data.Axes)
        {
            if (axis.AbsolutePosition.HasValue)
                AddDoubleEntry(entries, deviceName, $"Axis.{axisName}.Absolute", axis.AbsolutePosition.Value, timestamp);

            if (axis.MachinePosition.HasValue)
                AddDoubleEntry(entries, deviceName, $"Axis.{axisName}.Machine", axis.MachinePosition.Value, timestamp);

            if (axis.RelativePosition.HasValue)
                AddDoubleEntry(entries, deviceName, $"Axis.{axisName}.Relative", axis.RelativePosition.Value, timestamp);

            if (axis.DistanceToGo.HasValue)
                AddDoubleEntry(entries, deviceName, $"Axis.{axisName}.DistanceToGo", axis.DistanceToGo.Value, timestamp);
        }

        // ── Feed Rate ──
        if (data.FeedRate.HasValue)
            AddDoubleEntry(entries, deviceName, "FeedRate", data.FeedRate.Value, timestamp);

        // ── Spindle ──
        if (data.Spindle != null)
        {
            if (data.Spindle.Speed.HasValue)
                AddDoubleEntry(entries, deviceName, "Spindle.Speed", data.Spindle.Speed.Value, timestamp);

            if (data.Spindle.Load.HasValue)
                AddEntry(entries, deviceName, "Spindle.Load",
                    data.Spindle.Load.Value.ToString("F2", CultureInfo.InvariantCulture),
                    timestamp, SignalDataType.Float);

            if (data.Spindle.Direction != null)
                AddEntry(entries, deviceName, "Spindle.Direction", data.Spindle.Direction, timestamp, SignalDataType.Word);
        }

        // ── Production ──
        if (data.CycleTimeSeconds.HasValue)
            AddDoubleEntry(entries, deviceName, "CycleTime", data.CycleTimeSeconds.Value, timestamp);

        if (data.PartsCount.HasValue)
            AddEntry(entries, deviceName, "PartsCount",
                data.PartsCount.Value.ToString(CultureInfo.InvariantCulture),
                timestamp, SignalDataType.ULong);

        // ── Alarms ──
        AddEntry(entries, deviceName, "AlarmActive",
            data.ActiveAlarms.Count > 0 ? "1" : "0", timestamp, SignalDataType.Bit);

        AddEntry(entries, deviceName, "AlarmCount",
            data.ActiveAlarms.Count.ToString(), timestamp, SignalDataType.Int);
        
        // commented by bhanu
        //for (int i = 0; i < data.ActiveAlarms.Count; i++)
        //{
        //    var alarm = data.ActiveAlarms[i];
        //    var prefix = $"Alarm.{i}";
        //    AddEntry(entries, deviceName, $"{prefix}.Number", alarm.AlarmNumber.ToString(), timestamp, SignalDataType.Int);
        //    AddEntry(entries, deviceName, $"{prefix}.Type", alarm.AlarmType, timestamp, SignalDataType.Word);
        //    AddEntry(entries, deviceName, $"{prefix}.Message", alarm.Message, timestamp, SignalDataType.Word);
        //    if (!string.IsNullOrEmpty(alarm.Axis))
        //        AddEntry(entries, deviceName, $"{prefix}.Axis", alarm.Axis, timestamp, SignalDataType.Word);
        //}

        // ── Tool Data (FOCAS2 exclusive — no extra MT-LINKi license needed) ──
        if (data.ToolInfo != null)
        {
            AddEntry(entries, deviceName, "Tool.CurrentNo",
                data.ToolInfo.CurrentToolNumber.ToString(), timestamp, SignalDataType.Int);
            AddEntry(entries, deviceName, "Tool.OffsetCount",
                data.ToolInfo.OffsetCount.ToString(), timestamp, SignalDataType.Int);
            AddEntry(entries, deviceName, "Tool.OffsetType",
                data.ToolInfo.OffsetMemoryType, timestamp, SignalDataType.Word);
            AddEntry(entries, deviceName, "Tool.LifeEnabled",
                data.ToolInfo.ToolLifeEnabled ? "1" : "0", timestamp, SignalDataType.Bit);

            // Current tool's offset values
            var activeTool = data.ToolInfo.Offsets.FirstOrDefault(o => o.IsActive);
            if (activeTool != null)
            {
                AddDoubleEntry(entries, deviceName, "Tool.Active.WearLength", activeTool.WearLength, timestamp);
                AddDoubleEntry(entries, deviceName, "Tool.Active.WearRadius", activeTool.WearRadius, timestamp);
                AddDoubleEntry(entries, deviceName, "Tool.Active.GeomLength", activeTool.GeometryLength, timestamp);
                AddDoubleEntry(entries, deviceName, "Tool.Active.GeomRadius", activeTool.GeometryRadius, timestamp);
            }

            // All non-zero tool offsets as individual tags
            foreach (var ofs in data.ToolInfo.Offsets)
            {
                var prefix = $"Tool.{ofs.Number}";
                if (ofs.WearLength != 0)
                    AddDoubleEntry(entries, deviceName, $"{prefix}.WearLen", ofs.WearLength, timestamp);
                if (ofs.WearRadius != 0)
                    AddDoubleEntry(entries, deviceName, $"{prefix}.WearRad", ofs.WearRadius, timestamp);
                if (ofs.GeometryLength != 0)
                    AddDoubleEntry(entries, deviceName, $"{prefix}.GeomLen", ofs.GeometryLength, timestamp);
                if (ofs.GeometryRadius != 0)
                    AddDoubleEntry(entries, deviceName, $"{prefix}.GeomRad", ofs.GeometryRadius, timestamp);
            }

            // Tool life groups
            foreach (var life in data.ToolInfo.LifeGroups)
            {
                var prefix = $"ToolLife.{life.GroupNo}";
                AddEntry(entries, deviceName, $"{prefix}.ToolNo", life.ToolNo.ToString(), timestamp, SignalDataType.Int);
                AddEntry(entries, deviceName, $"{prefix}.Counter", life.LifeCounter.ToString(), timestamp, SignalDataType.Int);
                AddEntry(entries, deviceName, $"{prefix}.Limit", life.LifeLimit.ToString(), timestamp, SignalDataType.Int);
                AddEntry(entries, deviceName, $"{prefix}.Status", life.Status, timestamp, SignalDataType.Word);
                AddEntry(entries, deviceName, $"{prefix}.Type", life.CountType, timestamp, SignalDataType.Word);
                AddDoubleEntry(entries, deviceName, $"{prefix}.RemainingPct", life.LifeRemainingPercent, timestamp);
            }

            // ── Computed Summary Tags (for EREMOS dashboard KPIs) ──
            if (data.ToolInfo.LifeGroups.Count > 0)
            {
                var minPct = data.ToolInfo.LifeGroups.Min(g => g.LifeRemainingPercent);
                var expiredCount = data.ToolInfo.LifeGroups.Count(g =>
                    string.Equals(g.Status, "Expired", StringComparison.OrdinalIgnoreCase));
                var activeGroup = data.ToolInfo.LifeGroups.FirstOrDefault(g =>
                    string.Equals(g.Status, "Active", StringComparison.OrdinalIgnoreCase));

                AddDoubleEntry(entries, deviceName, "Tool.MinLifePct", minPct, timestamp);
                AddEntry(entries, deviceName, "Tool.ExpiredCount",
                    expiredCount.ToString(), timestamp, SignalDataType.Int);
                AddEntry(entries, deviceName, "Tool.TotalGroups",
                    data.ToolInfo.LifeGroups.Count.ToString(), timestamp, SignalDataType.Int);
                AddEntry(entries, deviceName, "Tool.ActiveGroupNo",
                    (activeGroup?.GroupNo ?? 0).ToString(), timestamp, SignalDataType.Int);
            }
        }

        // ── Diagnostics ──
        AddEntry(entries, deviceName, "CollectionDurationMs",
            data.CollectionDurationMs.ToString(), timestamp, SignalDataType.ULong);

        // ── Additional Data (custom data points) ──
        foreach (var (key, value) in data.AdditionalData)
        {
            if (value == null) continue;
            var (valStr, typeId) = InferTypeAndFormat(value);
            AddEntry(entries, deviceName, key, valStr, timestamp, typeId);
        }

        // ── Build the final payload object ──
        var payload = new DevicePayload
        {
            DeviceId = deviceId,
            Data = entries
        };

        return JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
    }

    // =========================================================================
    // ENTRY BUILDERS
    // =========================================================================

    /// <summary>
    /// Adds a "DeviceName.TagName,Value,Timestamp,DataTypeId" entry to the list.
    /// </summary>
    private static void AddEntry(
        List<string> entries, string deviceName, string tagName,
        string value, string timestamp, int dataTypeId)
    {
        // Escape commas in value to prevent breaking CSV structure
        var safeValue = value.Replace(",", ";");
        entries.Add($"{deviceName}.{tagName},{safeValue},{timestamp},{dataTypeId}");
    }

    /// <summary>
    /// Shorthand for double-precision values (most common for CNC positions/speeds).
    /// </summary>
    private static void AddDoubleEntry(
        List<string> entries, string deviceName, string tagName,
        double value, string timestamp)
    {
        entries.Add($"{deviceName}.{tagName},{value.ToString("F4", CultureInfo.InvariantCulture)},{timestamp},{SignalDataType.Double}");
    }

    /// <summary>
    /// Infer the signal data type from a runtime object value.
    /// Used for AdditionalData dictionary entries where the type isn't known at compile time.
    /// </summary>
    private static (string value, int typeId) InferTypeAndFormat(object value)
    {
        return value switch
        {
            bool b => (b ? "1" : "0", SignalDataType.Bit),
            int i => (i.ToString(CultureInfo.InvariantCulture), SignalDataType.Int),
            long l => (l.ToString(CultureInfo.InvariantCulture), SignalDataType.ULong),
            float f => (f.ToString("F4", CultureInfo.InvariantCulture), SignalDataType.Float),
            double d => (d.ToString("F4", CultureInfo.InvariantCulture), SignalDataType.Double),
            string s => (s.Replace(",", ";"), SignalDataType.Word),
            _ => (value.ToString()?.Replace(",", ";") ?? "", SignalDataType.Word)
        };
    }

    // =========================================================================
    // PER-TAG FORMAT (EREMOS Integration)
    // =========================================================================

    /// <summary>
    /// A single tag entry for per-tag MQTT publishing.
    /// Each becomes a separate MQTT message: topic = prefix/machineId/tagName, payload = value.
    /// </summary>
    public readonly record struct PerTagEntry(string TagName, string Value);

    /// <summary>
    /// Extracts all tags from CncMachineData as individual (tagName, value) pairs.
    /// Used when PublishMode = "PerTag" — each entry becomes a separate MQTT message.
    ///
    /// Values are published as raw strings:
    ///   - Numeric values: "3500", "45.2000", "0", "1"
    ///   - String values:  "MEM", "O1234", "IDLE"
    ///   - Boolean/bit:    "0" or "1"
    ///
    /// EREMOS MqttPayloadParser handles type conversion on the subscriber side.
    /// </summary>
    public static List<PerTagEntry> ExtractPerTagEntries(CncMachineData data)
    {
        var entries = new List<PerTagEntry>(80);

        // ── Machine Status & Signals ──
        AddTag(entries, "Disconnect_CNC", data.Disconnect_CNC?.ToString());
        AddTag(entries, "Mode_path1_CNC", data.Mode_path1_CNC);
        AddTag(entries, "CncState_path1_CNC", data.CncState_path1_CNC);
        AddTag(entries, "SigMDRN_path1_CNC", data.SigMDRN_path1_CNC?.ToString());
        AddTag(entries, "SigCUT_path1_CNC", data.SigCUT_path1_CNC?.ToString());
        AddTag(entries, "SigSBK_path1_CNC", data.SigSBK_path1_CNC?.ToString());
        AddTag(entries, "SigDM00_path1_CNC", data.SigDM00_path1_CNC?.ToString());
        AddTag(entries, "SigDM01_path1_CNC", data.SigDM01_path1_CNC?.ToString());

        // ── Program Info ──
        AddTag(entries, "MainProgram_path1_CNC", data.MainProgram_path1_CNC);
        AddTag(entries, "ActProgram_path1_CNC", data.ActProgram_path1_CNC);
        AddTag(entries, "MainComment_path1_CNC", data.MainComment_path1_CNC);
        AddTag(entries, "ActComment_path1_CNC", data.ActComment_path1_CNC);
        AddTag(entries, "PartsNum_path1_CNC", data.PartsNum_path1_CNC.ToString());

        // ── CNC Warning & Fans ──
        AddTag(entries, "CncWarning_path1_CNC", data.CncWarning_path1_CNC);
        AddTag(entries, "CncFan1Status_path1_CNC", data.CncFan1Status_path1_CNC);
        AddTag(entries, "CncFan2Status_path1_CNC", data.CncFan2Status_path1_CNC);
        AddTag(entries, "CncFan3Status_path1_CNC", data.CncFan3Status_path1_CNC);
        AddTag(entries, "CncFan4Status_path1_CNC", data.CncFan4Status_path1_CNC);

        // ── Servo Path 0 ──
        AddTag(entries, "CncBatLow_0_path1_CNC", data.CncBatLow_0_path1_CNC?.ToString());
        AddTag(entries, "ApcBatZero_0_path1_CNC", data.ApcBatZero_0_path1_CNC?.ToString());
        AddTag(entries, "ApcBatLow_0_path1_CNC", data.ApcBatLow_0_path1_CNC?.ToString());
        AddTag(entries, "IndBatZero_0_path1_CNC", data.IndBatZero_0_path1_CNC?.ToString());
        AddTag(entries, "SpdBatZero_0_path1_CNC", data.SpdBatZero_0_path1_CNC?.ToString());
        AddTag(entries, "SSpdBatZero_0_path1_CNC", data.SSpdBatZero_0_path1_CNC?.ToString());
        AddTag(entries, "ServoTemp_0_path1_CNC", data.ServoTemp_0_path1_CNC?.ToString());
        AddTag(entries, "PulseCoderTemp_0_path1_CNC", data.PulseCoderTemp_0_path1_CNC?.ToString());
        AddTag(entries, "ServoLeakResistData_0_path1_CNC", data.ServoLeakResistData_0_path1_CNC?.ToString());
        AddTag(entries, "InFan1SrvAmpStatus_0_path1_CNC", data.InFan1SrvAmpStatus_0_path1_CNC);
        AddTag(entries, "InFan2SrvAmpStatus_0_path1_CNC", data.InFan2SrvAmpStatus_0_path1_CNC);
        AddTag(entries, "RadFan1SrvAmpStatus_0_path1_CNC", data.RadFan1SrvAmpStatus_0_path1_CNC);
        AddTag(entries, "RadFan2SrvAmpStatus_0_path1_CNC", data.RadFan2SrvAmpStatus_0_path1_CNC);
        AddTag(entries, "InFan1SrvComPwStatus_0_path1_CNC", data.InFan1SrvComPwStatus_0_path1_CNC);
        AddTag(entries, "InFan2SrvComPwStatus_0_path1_CNC", data.InFan2SrvComPwStatus_0_path1_CNC);
        AddTag(entries, "RadFan1SrvComPwStatus_0_path1_CNC", data.RadFan1SrvComPwStatus_0_path1_CNC);
        AddTag(entries, "RadFan2SrvComPwStatus_0_path1_CNC", data.RadFan2SrvComPwStatus_0_path1_CNC);

        // ── Servo Path 1 ──
        AddTag(entries, "CncBatLow_1_path1_CNC", data.CncBatLow_1_path1_CNC?.ToString());
        AddTag(entries, "ApcBatZero_1_path1_CNC", data.ApcBatZero_1_path1_CNC?.ToString());
        AddTag(entries, "ApcBatLow_1_path1_CNC", data.ApcBatLow_1_path1_CNC?.ToString());
        AddTag(entries, "IndBatZero_1_path1_CNC", data.IndBatZero_1_path1_CNC?.ToString());
        AddTag(entries, "SpdBatZero_1_path1_CNC", data.SpdBatZero_1_path1_CNC?.ToString());
        AddTag(entries, "SSpdBatZero_1_path1_CNC", data.SSpdBatZero_1_path1_CNC?.ToString());
        AddTag(entries, "ServoTemp_1_path1_CNC", data.ServoTemp_1_path1_CNC?.ToString());
        AddTag(entries, "PulseCoderTemp_1_path1_CNC", data.PulseCoderTemp_1_path1_CNC?.ToString());
        AddTag(entries, "ServoLeakResistData_1_path1_CNC", data.ServoLeakResistData_1_path1_CNC?.ToString());
        AddTag(entries, "InFan1SrvAmpStatus_1_path1_CNC", data.InFan1SrvAmpStatus_1_path1_CNC);
        AddTag(entries, "InFan2SrvAmpStatus_1_path1_CNC", data.InFan2SrvAmpStatus_1_path1_CNC);
        AddTag(entries, "RadFan1SrvAmpStatus_1_path1_CNC", data.RadFan1SrvAmpStatus_1_path1_CNC);
        AddTag(entries, "RadFan2SrvAmpStatus_1_path1_CNC", data.RadFan2SrvAmpStatus_1_path1_CNC);
        AddTag(entries, "InFan1SrvComPwStatus_1_path1_CNC", data.InFan1SrvComPwStatus_1_path1_CNC);
        AddTag(entries, "InFan2SrvComPwStatus_1_path1_CNC", data.InFan2SrvComPwStatus_1_path1_CNC);
        AddTag(entries, "RadFan1SrvComPwStatus_1_path1_CNC", data.RadFan1SrvComPwStatus_1_path1_CNC);
        AddTag(entries, "RadFan2SrvComPwStatus_1_path1_CNC", data.RadFan2SrvComPwStatus_1_path1_CNC);

        // ── Spindle ──
        AddTag(entries, "SpindleTemp_0_path1_CNC", data.SpindleTemp_0_path1_CNC?.ToString());
        AddTag(entries, "SpindleLeakResistData_0_path1_CNC", data.SpindleLeakResistData_0_path1_CNC?.ToString());
        AddTag(entries, "SpindleTotalRev1_0_path1_CNC", data.SpindleTotalRev1_0_path1_CNC?.ToString());
        AddTag(entries, "SpindleTotalRev2_0_path1_CNC", data.SpindleTotalRev2_0_path1_CNC?.ToString());
        AddTag(entries, "InFan1SpdlComPwStatus_0_path1_CNC", data.InFan1SpdlComPwStatus_0_path1_CNC);
        AddTag(entries, "InFan2SpdlComPwStatus_0_path1_CNC", data.InFan2SpdlComPwStatus_0_path1_CNC);
        AddTag(entries, "RadFan1SpdlComPwStatus_0_path1_CNC", data.RadFan1SpdlComPwStatus_0_path1_CNC);
        AddTag(entries, "RadFan2SpdlComPwStatus_0_path1_CNC", data.RadFan2SpdlComPwStatus_0_path1_CNC);
        AddTag(entries, "InFan1SpindleAmpStatus_0_path1_CNC", data.InFan1SpindleAmpStatus_0_path1_CNC);
        AddTag(entries, "InFan2SpindleAmpStatus_0_path1_CNC", data.InFan2SpindleAmpStatus_0_path1_CNC);
        AddTag(entries, "RadFan1SpindleAmpStatus_0_path1_CNC", data.RadFan1SpindleAmpStatus_0_path1_CNC);
        AddTag(entries, "RadFan2SpindleAmpStatus_0_path1_CNC", data.RadFan2SpindleAmpStatus_0_path1_CNC);

        // ── FOCAS2 Status ──
        if (data.AutoMode != null)
            AddTag(entries, "AutoMode", data.AutoMode);
        if (data.EmergencyStop.HasValue)
            AddTag(entries, "EmergencyStop", data.EmergencyStop.Value ? "1" : "0");

        // ── FOCAS2 System Info (cached) ──
        if (data.SystemInfo != null)
        {
            if (!string.IsNullOrEmpty(data.SystemInfo.Series))
                AddTag(entries, "CncSeries", data.SystemInfo.Series);
            if (!string.IsNullOrEmpty(data.SystemInfo.CncType))
                AddTag(entries, "CncType", data.SystemInfo.CncType);
            if (!string.IsNullOrEmpty(data.SystemInfo.Version))
                AddTag(entries, "CncVersion", data.SystemInfo.Version);
            AddTag(entries, "AxisCount", data.SystemInfo.AxisCount.ToString());
        }

        // ── Standard Production Data (from CncMachineData base properties) ──
        if (data.MainProgram != null)
            AddTag(entries, "MainProgram", data.MainProgram);
        if (data.RunningStatus != null)
            AddTag(entries, "RunningStatus", data.RunningStatus);
        if (data.FeedRate.HasValue)
            AddTag(entries, "FeedRate", data.FeedRate.Value.ToString("F4", CultureInfo.InvariantCulture));
        if (data.Spindle?.Speed.HasValue == true)
            AddTag(entries, "Spindle.Speed", data.Spindle.Speed.Value.ToString("F4", CultureInfo.InvariantCulture));
        if (data.Spindle?.Load.HasValue == true)
            AddTag(entries, "Spindle.Load", data.Spindle.Load.Value.ToString("F2", CultureInfo.InvariantCulture));
        if (data.CycleTimeSeconds.HasValue)
            AddTag(entries, "CycleTime", data.CycleTimeSeconds.Value.ToString("F4", CultureInfo.InvariantCulture));
        if (data.PartsCount.HasValue)
            AddTag(entries, "PartsCount", data.PartsCount.Value.ToString(CultureInfo.InvariantCulture));

        // ── Axis Positions ──
        foreach (var (axisName, axis) in data.Axes)
        {
            if (axis.AbsolutePosition.HasValue)
                AddTag(entries, $"Axis.{axisName}.Absolute", axis.AbsolutePosition.Value.ToString("F4", CultureInfo.InvariantCulture));
            if (axis.MachinePosition.HasValue)
                AddTag(entries, $"Axis.{axisName}.Machine", axis.MachinePosition.Value.ToString("F4", CultureInfo.InvariantCulture));
        }

        // ── Tool Data (FOCAS2 exclusive) ──
        if (data.ToolInfo != null)
        {
            AddTag(entries, "Tool.CurrentNo", data.ToolInfo.CurrentToolNumber.ToString());
            AddTag(entries, "Tool.OffsetCount", data.ToolInfo.OffsetCount.ToString());
            AddTag(entries, "Tool.OffsetType", data.ToolInfo.OffsetMemoryType);
            AddTag(entries, "Tool.LifeEnabled", data.ToolInfo.ToolLifeEnabled ? "1" : "0");

            // Current tool's offset values
            var activeTool = data.ToolInfo.Offsets.FirstOrDefault(o => o.IsActive);
            if (activeTool != null)
            {
                AddTag(entries, "Tool.Active.WearLength", activeTool.WearLength.ToString("F4", CultureInfo.InvariantCulture));
                AddTag(entries, "Tool.Active.WearRadius", activeTool.WearRadius.ToString("F4", CultureInfo.InvariantCulture));
                AddTag(entries, "Tool.Active.GeomLength", activeTool.GeometryLength.ToString("F4", CultureInfo.InvariantCulture));
                AddTag(entries, "Tool.Active.GeomRadius", activeTool.GeometryRadius.ToString("F4", CultureInfo.InvariantCulture));
            }

            // All non-zero tool offsets
            foreach (var ofs in data.ToolInfo.Offsets)
            {
                var prefix = $"Tool.{ofs.Number}";
                if (ofs.WearLength != 0)
                    AddTag(entries, $"{prefix}.WearLen", ofs.WearLength.ToString("F4", CultureInfo.InvariantCulture));
                if (ofs.WearRadius != 0)
                    AddTag(entries, $"{prefix}.WearRad", ofs.WearRadius.ToString("F4", CultureInfo.InvariantCulture));
                if (ofs.GeometryLength != 0)
                    AddTag(entries, $"{prefix}.GeomLen", ofs.GeometryLength.ToString("F4", CultureInfo.InvariantCulture));
                if (ofs.GeometryRadius != 0)
                    AddTag(entries, $"{prefix}.GeomRad", ofs.GeometryRadius.ToString("F4", CultureInfo.InvariantCulture));
            }

            // Tool life groups
            foreach (var life in data.ToolInfo.LifeGroups)
            {
                var prefix = $"ToolLife.{life.GroupNo}";
                AddTag(entries, $"{prefix}.ToolNo", life.ToolNo.ToString());
                AddTag(entries, $"{prefix}.Counter", life.LifeCounter.ToString());
                AddTag(entries, $"{prefix}.Limit", life.LifeLimit.ToString());
                AddTag(entries, $"{prefix}.Status", life.Status);
                AddTag(entries, $"{prefix}.Type", life.CountType);
                AddTag(entries, $"{prefix}.RemainingPct", life.LifeRemainingPercent.ToString("F1", CultureInfo.InvariantCulture));
            }

            // ── Computed Summary Tags (for EREMOS dashboard KPIs) ──
            if (data.ToolInfo.LifeGroups.Count > 0)
            {
                var minPct = data.ToolInfo.LifeGroups.Min(g => g.LifeRemainingPercent);
                var expiredCount = data.ToolInfo.LifeGroups.Count(g =>
                    string.Equals(g.Status, "Expired", StringComparison.OrdinalIgnoreCase));
                var activeGroup = data.ToolInfo.LifeGroups.FirstOrDefault(g =>
                    string.Equals(g.Status, "Active", StringComparison.OrdinalIgnoreCase));

                AddTag(entries, "Tool.MinLifePct", minPct.ToString("F1", CultureInfo.InvariantCulture));
                AddTag(entries, "Tool.ExpiredCount", expiredCount.ToString());
                AddTag(entries, "Tool.TotalGroups", data.ToolInfo.LifeGroups.Count.ToString());
                AddTag(entries, "Tool.ActiveGroupNo", (activeGroup?.GroupNo ?? 0).ToString());
            }
        }

        return entries;
    }

    /// <summary>
    /// Adds a tag entry only if the value is not null or "NA".
    /// </summary>
    private static void AddTag(List<PerTagEntry> entries, string tagName, string? value)
    {
        if (value != null && value != "NA")
            entries.Add(new PerTagEntry(tagName, value));
    }

    // =========================================================================
    // WIRE FORMAT MODEL
    // =========================================================================

    /// <summary>
    /// The top-level JSON object published to the MQTT broker.
    /// 
    /// Serialized as:
    /// {
    ///   "deviceId": "CNC-001",
    ///   "data": [
    ///     "LatheBay1.MainProgram,O1234,2026-02-11T10:00:00.000Z,5",
    ///     "LatheBay1.SpindleSpeed,1200.5000,2026-02-11T10:00:00.000Z,4"
    ///   ]
    /// }
    /// </summary>
    /// 
    private sealed class DevicePayload
    {
        [JsonPropertyName("DeviceId")]
        public required string DeviceId { get; init; }

        [JsonPropertyName("Data")]
        public required List<string> Data { get; init; }
    }
}
