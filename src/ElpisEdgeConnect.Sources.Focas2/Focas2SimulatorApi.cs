// ============================================================================
// File: Focas2SimulatorApi.cs
// Purpose: IFocas2Api implementation that talks to an external FANUC 0i-TF
//          CNC simulator over its newline-delimited JSON protocol instead of
//          P/Invoking the native Fwlib64 library.
//
//          Activated only by Focas2SimulatorOptions (EDGECONNECT_FOCAS2_SIMULATOR).
//          The native path is untouched; this is a sibling of Focas2DemoApi.
//
//          WHY THIS EXISTS: the simulator implements the FOCAS2 function set
//          and data structures faithfully, but FANUC's FOCAS Ethernet wire
//          format is proprietary, so the real Fwlib64 cannot speak to it. This
//          class bridges that gap at the IFocas2Api seam, which is the only
//          place in the adapter that knows how bytes reach a controller.
//
//          Unsupported-by-simulator calls return EW_NOOPT, which is exactly
//          what a 0i-TF without that option returns. Every collector already
//          treats a non-EW_OK as "skip this data point", so the adapter
//          degrades the same way it would against real hardware.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 4.2
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ElpisEdgeConnect.Sources.Focas2;

/// <summary>
/// <see cref="IFocas2Api"/> backed by an external CNC simulator speaking
/// newline-delimited JSON on the configured address and port.
/// </summary>
internal sealed class Focas2SimulatorApi : IFocas2Api, IDisposable
{
    private const short Ok = (short)Focas2ErrorCode.EW_OK;
    private const short NoOption = (short)Focas2ErrorCode.EW_NOOPT;
    private const short BadHandle = (short)Focas2ErrorCode.EW_HANDLE;
    private const short SocketError = (short)Focas2ErrorCode.EW_SOCKET;

    /// <summary>Offset at which PMC payload bytes begin inside the caller's buffer.</summary>
    private const int PmcDataOffset = 10;

    private readonly ConcurrentDictionary<ushort, SimulatorConnection> _connections = new();
    private bool _disposed;

    // ---- Connection --------------------------------------------------------

    public short AllocLibHandle(string ipAddress, ushort port, int timeout, out ushort handle)
    {
        handle = 0;
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return (short)Focas2ErrorCode.EW_DATA;
        }

        SimulatorConnection? connection = null;
        try
        {
            connection = SimulatorConnection.Open(ipAddress, port, timeout);

            var ret = connection.Call(
                "cnc_allclibhndl3",
                Args(("ip", ipAddress), ("port", (int)port), ("timeout", timeout)),
                out var data);

            if (ret != Ok || !TryGetInt32(data, "handle", out var allocated) || allocated <= 0)
            {
                connection.Dispose();
                return ret == Ok ? SocketError : ret;
            }

            // The simulator's handle space is global across its machines, so it
            // doubles as our handle without a second mapping layer.
            handle = (ushort)allocated;
            connection.Handle = handle;

            if (!_connections.TryAdd(handle, connection))
            {
                connection.Dispose();
                handle = 0;
                return SocketError;
            }

            connection = null; // ownership transferred to the dictionary
            return Ok;
        }
        catch (SocketException)
        {
            return SocketError;
        }
        catch (IOException)
        {
            return SocketError;
        }
        catch (JsonException)
        {
            return (short)Focas2ErrorCode.EW_UNEXP;
        }
        finally
        {
            connection?.Dispose();
        }
    }

    public short FreeLibHandle(ushort handle)
    {
        if (!_connections.TryRemove(handle, out var connection))
        {
            return BadHandle;
        }

        try
        {
            connection.Call("cnc_freelibhndl", Args(("handle", (int)handle)), out _);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            // The socket is going away regardless; closing is what matters.
        }
        finally
        {
            connection.Dispose();
        }

        return Ok;
    }

    /// <summary>
    /// The simulator applies its read timeout at the socket, so this only has to
    /// adjust the local receive deadline for the handle.
    /// </summary>
    public short SetDataTimeout(ushort handle, int seconds)
        => Invoke(handle, connection =>
        {
            connection.SetReadTimeout(seconds);
            return Ok;
        });

    // ---- Status ------------------------------------------------------------

    public short ReadStatusInfo(ushort handle, out OdbStatusInfo statusInfo)
    {
        statusInfo = default;
        var local = default(OdbStatusInfo);

        var ret = Call(handle, "cnc_statinfo", null, data =>
        {
            local.Dummy = 0;
            local.Tmmode = ReadInt16(data, "tmmode");
            local.Aut = ReadInt16(data, "aut");
            local.Run = ReadInt16(data, "run");
            local.Motion = ReadInt16(data, "motion");
            local.Mstb = ReadInt16(data, "mstb");
            local.Emergency = ReadInt16(data, "emergency");
            local.Alarm = ReadInt16(data, "alarm");
            local.Edit = ReadInt16(data, "edit");
            return Ok;
        });

        statusInfo = local;
        return ret;
    }

    // ---- System ------------------------------------------------------------

    public short ReadSystemInfo(ushort handle, out OdbSystemInfo sysInfo)
    {
        sysInfo = default;
        var local = default(OdbSystemInfo);

        var ret = Call(handle, "cnc_sysinfo", null, data =>
        {
            local.Dummy = 0;
            local.MaxAxis = ReadString(data, "max_axis");
            local.CncType = ReadString(data, "cnc_type");
            local.MtType = ReadString(data, "mt_type");
            local.Series = ReadString(data, "series");
            local.Version = ReadString(data, "version");
            local.Axes = ReadString(data, "axes");
            return Ok;
        });

        sysInfo = local;
        return ret;
    }

    public short ReadAxisCount(ushort handle, out short axisCount)
    {
        short count = 0;
        var ret = Call(handle, "cnc_rdaxisname", null, data =>
        {
            count = ReadInt16(data, "num");
            return Ok;
        });

        axisCount = count;
        return ret;
    }

    public short ReadAxisNames(ushort handle, ref short dataNum, OdbAxisName[] axisNames)
    {
        ArgumentNullException.ThrowIfNull(axisNames);

        short written = 0;
        var capacity = Math.Min((int)dataNum, axisNames.Length);

        var ret = Call(handle, "cnc_rdaxisname", null, data =>
        {
            if (!data.TryGetProperty("names", out var names) || names.ValueKind != JsonValueKind.Array)
            {
                return (short)Focas2ErrorCode.EW_DATA;
            }

            foreach (var name in names.EnumerateArray())
            {
                if (written >= capacity)
                {
                    break;
                }

                axisNames[written] = new OdbAxisName
                {
                    Name = ReadString(name, "name"),
                    Suffix = ReadString(name, "suff"),
                };
                written++;
            }

            return Ok;
        });

        dataNum = written;
        return ret;
    }

    // ---- Program -----------------------------------------------------------

    public short ReadProgramNumber(ushort handle, out OdbProgramNumber programNum)
    {
        programNum = default;
        var local = default(OdbProgramNumber);

        var ret = Call(handle, "cnc_rdprgnum", null, data =>
        {
            local.RunningProgram = ReadInt32(data, "data");
            local.MainProgram = ReadInt32(data, "mdata");
            return Ok;
        });

        programNum = local;
        return ret;
    }

    /// <summary>
    /// Not modelled: the simulator exposes its directory as JSON rather than the
    /// packed PRGDIR byte layout this signature expects.
    /// </summary>
    public short ReadProgramDirectory(ushort handle, short type, ref int topProg, ref short numProg, byte[] progDir)
    {
        numProg = 0;
        return NoOption;
    }

    // ---- Position ----------------------------------------------------------

    public short ReadAbsolutePosition(ushort handle, short axisNum, short length, out OdbAxisData position)
        => ReadPosition(handle, "absolute", out position);

    public short ReadMachinePosition(ushort handle, short axisNum, short length, out OdbAxisData position)
        => ReadPosition(handle, "machine", out position);

    public short ReadRelativePosition(ushort handle, short axisNum, short length, out OdbAxisData position)
        => ReadPosition(handle, "relative", out position);

    public short ReadDistanceToGo(ushort handle, short axisNum, short length, out OdbAxisData position)
        => ReadPosition(handle, "distance", out position);

    /// <summary>
    /// One cnc_rdposition call carries all four views plus each axis's decimal
    /// count, so every position read maps onto it and picks the view it wants.
    /// </summary>
    private short ReadPosition(ushort handle, string view, out OdbAxisData position)
    {
        var local = new OdbAxisData
        {
            Data = new int[Focas2Interop.MAX_AXIS],
            Decimal = new short[Focas2Interop.MAX_AXIS],
        };

        var ret = Call(handle, "cnc_rdposition", Args(("axis", -1)), data =>
        {
            if (!data.TryGetProperty("positions", out var positions) ||
                positions.ValueKind != JsonValueKind.Array)
            {
                return (short)Focas2ErrorCode.EW_DATA;
            }

            var index = 0;
            foreach (var axis in positions.EnumerateArray())
            {
                if (index >= Focas2Interop.MAX_AXIS)
                {
                    break;
                }

                if (axis.TryGetProperty(view, out var element))
                {
                    local.Data[index] = ReadInt32(element, "data");
                    local.Decimal[index] = ReadInt16(element, "dec");
                }

                index++;
            }

            return Ok;
        });

        position = local;
        return ret;
    }

    // ---- Feed / Spindle ----------------------------------------------------

    public short ReadActualFeedRate(ushort handle, out OdbActualFeed feedRate)
    {
        feedRate = default;
        var local = default(OdbActualFeed);

        var ret = Call(handle, "cnc_actf", null, data =>
        {
            local.Data = ReadInt32(data, "data");
            return Ok;
        });

        feedRate = local;
        return ret;
    }

    public short ReadActualSpindleSpeed(ushort handle, out OdbActualSpeed spindleSpeed)
    {
        spindleSpeed = default;
        var local = default(OdbActualSpeed);

        var ret = Call(handle, "cnc_acts", null, data =>
        {
            local.Data = ReadInt32(data, "data");
            return Ok;
        });

        spindleSpeed = local;
        return ret;
    }

    public short ReadSpindleLoad(ushort handle, short spindleNo, out OdbSpindleLoad load)
    {
        var local = new OdbSpindleLoad { Data = new short[4] };

        var ret = Call(handle, "cnc_rdspmeter", Args(("type", 0)), data =>
        {
            if (!data.TryGetProperty("loads", out var loads) || loads.ValueKind != JsonValueKind.Array)
            {
                return (short)Focas2ErrorCode.EW_DATA;
            }

            var index = 0;
            foreach (var meter in loads.EnumerateArray())
            {
                if (index >= local.Data.Length)
                {
                    break;
                }

                local.Data[index] = ReadInt16(meter, "data");
                index++;
            }

            return Ok;
        });

        load = local;
        return ret;
    }

    // ---- Alarms ------------------------------------------------------------

    public short ReadAlarmStatus(ushort handle, out OdbAlarmStatus alarmStatus)
    {
        alarmStatus = default;
        var local = default(OdbAlarmStatus);

        var ret = Call(handle, "cnc_alarm2", null, data =>
        {
            // The bitmask is wider than this struct's short; the low 16 bits
            // carry every type the simulator raises except PC (bit 19).
            var mask = ReadInt64(data, "alarm");
            local.Data = unchecked((short)(mask & 0xFFFF));
            return Ok;
        });

        alarmStatus = local;
        return ret;
    }

    public short ReadAlarmMessages(ushort handle, short type, ref short num, OdbAlarmMessage[] alarms)
    {
        ArgumentNullException.ThrowIfNull(alarms);

        short written = 0;
        var capacity = Math.Min((int)num, alarms.Length);

        var ret = Call(handle, "cnc_rdalmmsg2", Args(("type", (int)type), ("num", capacity)), data =>
        {
            if (!data.TryGetProperty("messages", out var messages) ||
                messages.ValueKind != JsonValueKind.Array)
            {
                return (short)Focas2ErrorCode.EW_DATA;
            }

            foreach (var message in messages.EnumerateArray())
            {
                if (written >= capacity)
                {
                    break;
                }

                alarms[written] = new OdbAlarmMessage
                {
                    AlarmNo = ReadInt32(message, "alm_no"),
                    Type = ReadInt16(message, "type"),
                    Axis = ReadInt16(message, "axis"),
                    Dummy = 0,
                    MsgLength = ReadInt16(message, "msg_len"),
                    AlarmMessage = ReadString(message, "alm_msg"),
                };
                written++;
            }

            return Ok;
        });

        num = written;
        return ret;
    }

    // ---- Production --------------------------------------------------------

    public short ReadTimer(ushort handle, short type, out OdbTimer timer)
    {
        timer = default;
        var local = default(OdbTimer);
        local.Type = type;

        var ret = Call(handle, "cnc_rdtimer", Args(("type", (int)type)), data =>
        {
            local.Minute = ReadInt32(data, "minute");
            local.Msec = ReadInt32(data, "msec");
            return Ok;
        });

        timer = local;
        return ret;
    }

    public short ReadParameter(ushort handle, short paramNo, short axisNo, short length, out OdbParameter param)
    {
        param = default;
        var local = default(OdbParameter);

        var ret = Call(handle, "cnc_rdparam", Args(("number", (int)paramNo), ("axis", (int)axisNo)), data =>
        {
            local.DataNo = ReadInt16(data, "datano");
            local.Type = ReadInt16(data, "type");
            local.LData = ReadInt32(data, "value");
            return Ok;
        });

        param = local;
        return ret;
    }

    // ---- Tool --------------------------------------------------------------

    /// <summary>
    /// Only modal type 108 (the T code) is modelled, which is the one the tool
    /// collector asks for. The value goes at offset 4 as a 4-byte integer.
    /// </summary>
    public short ReadModal(ushort handle, short type, short length, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (type != 108 || data.Length < 8)
        {
            return NoOption;
        }

        var tool = 0;
        var ret = Call(handle, "cnc_rdtofsinfo", null, element =>
        {
            tool = ReadInt32(element, "current_tool");
            return Ok;
        });

        if (ret != Ok)
        {
            return ret;
        }

        BitConverter.TryWriteBytes(data.AsSpan(4), tool);
        return Ok;
    }

    public short ReadMacro(ushort handle, short number, short length, out OdbMacro macro)
    {
        macro = default;
        var local = default(OdbMacro);

        var ret = Call(handle, "cnc_rdmacro", Args(("number", (int)number)), data =>
        {
            local.DataNo = ReadInt16(data, "datano");
            local.McVal = ReadInt32(data, "mcr_val");
            local.McDig = ReadInt16(data, "dec_val");
            return Ok;
        });

        macro = local;
        return ret;
    }

    public short ReadToolOffsetInfo2(ushort handle, out short ofsType, out short useNo)
    {
        short type = 0;
        short count = 0;

        var ret = Call(handle, "cnc_rdtofsinfo", null, data =>
        {
            // The simulated lathe carries geometry and wear for X, Z and nose
            // radius, which is FANUC offset memory C.
            type = 2;
            count = ReadInt16(data, "num");
            return Ok;
        });

        ofsType = type;
        useNo = count;
        return ret;
    }

    public short ReadToolOffsetInfo(ushort handle, out short useNo)
    {
        short count = 0;
        var ret = Call(handle, "cnc_rdtofsinfo", null, data =>
        {
            count = ReadInt16(data, "num");
            return Ok;
        });

        useNo = count;
        return ret;
    }

    public short ReadToolOffset(ushort handle, short number, short type, short length, out OdbToolOffset tofs)
    {
        tofs = default;
        var local = default(OdbToolOffset);

        var ret = Call(handle, "cnc_rdtofs", Args(("number", (int)number), ("type", (int)type)), data =>
        {
            local.DataNo = ReadInt16(data, "datano");
            local.Type = ReadInt16(data, "type");
            local.Data = ReadInt32(data, "data");
            return Ok;
        });

        tofs = local;
        return ret;
    }

    /// <summary>Not modelled: the packed range layout has no simulator equivalent.</summary>
    public short ReadToolOffsetRange(ushort handle, short startNo, short type, short endNo, short length, byte[] data)
        => NoOption;

    /// <summary>Not modelled: tool life management is an option the simulated control does not carry.</summary>
    public short ReadToolLifeInfo(ushort handle, byte[] data) => NoOption;

    /// <summary>Not modelled: tool life management is an option the simulated control does not carry.</summary>
    public short ReadToolLifeGroupCount(ushort handle, out short count)
    {
        count = 0;
        return NoOption;
    }

    /// <summary>Not modelled: tool life management is an option the simulated control does not carry.</summary>
    public short ReadToolLifeGroup(ushort handle, int groupNo, byte[] data) => NoOption;

    /// <summary>Not modelled: tool life management is an option the simulated control does not carry.</summary>
    public short ReadToolLifeUseGroup(ushort handle, out int groupNo)
    {
        groupNo = 0;
        return NoOption;
    }

    // ---- PMC ---------------------------------------------------------------

    public short ReadPmcRange(ushort handle, short addrType, short dataType,
        ushort startNo, ushort endNo, ushort length, byte[] pmcData)
    {
        ArgumentNullException.ThrowIfNull(pmcData);

        if (dataType != Focas2Interop.PMC_TYPE_BYTE || pmcData.Length <= PmcDataOffset)
        {
            return NoOption;
        }

        return Call(handle, "pmc_rdpmcrng",
            Args(("adr_type", (int)addrType), ("data_type", (int)dataType),
                 ("s_number", (int)startNo), ("e_number", (int)endNo)),
            data =>
            {
                if (!data.TryGetProperty("data", out var values) || values.ValueKind != JsonValueKind.Array)
                {
                    return (short)Focas2ErrorCode.EW_DATA;
                }

                // Callers read the payload from a fixed offset past the header.
                var index = PmcDataOffset;
                foreach (var value in values.EnumerateArray())
                {
                    if (index >= pmcData.Length)
                    {
                        break;
                    }

                    pmcData[index] = unchecked((byte)(value.TryGetInt32(out var raw) ? raw : 0));
                    index++;
                }

                return Ok;
            });
    }

    // ---- Diagnostics / messages / maintenance ------------------------------

    /// <summary>Not modelled: the simulated control exposes no diagnosis numbers.</summary>
    public short ReadDiagnosticData(ushort handle, short diagNo, short axisNo, short length, out OdbDiagnosticData diagData)
    {
        diagData = default;
        return NoOption;
    }

    /// <summary>Not modelled: the simulated control exposes no diagnosis numbers.</summary>
    public short ReadDiagnosticDataArray(ushort handle, short diagNo, short axisNo, short length, byte[] diagData)
        => NoOption;

    /// <summary>Not modelled: the simulated control raises alarms, not operator messages.</summary>
    public short ReadOperatorMessage(ushort handle, short type, short length, byte[] opmsg) => NoOption;

    /// <summary>Not modelled: spindle maintenance data is an option the simulated control does not carry.</summary>
    public short ReadSpMaintCheck(ushort handle, short type, byte[] data) => NoOption;

    // ---- Plumbing ----------------------------------------------------------

    /// <summary>
    /// Resolves the handle, issues one request, and hands the payload to
    /// <paramref name="project"/>. Transport faults become EW_SOCKET so the
    /// adapter's existing fatal handling reconnects exactly as it would for a
    /// dropped controller.
    /// </summary>
    private short Call(ushort handle, string function,
        Dictionary<string, object?>? args, Func<JsonElement, short> project)
    {
        return Invoke(handle, connection =>
        {
            args ??= new Dictionary<string, object?>(StringComparer.Ordinal);
            args["handle"] = (int)handle;

            var ret = connection.Call(function, args, out var data);
            return ret != Ok ? ret : project(data);
        });
    }

    private short Invoke(ushort handle, Func<SimulatorConnection, short> action)
    {
        if (!_connections.TryGetValue(handle, out var connection))
        {
            return BadHandle;
        }

        try
        {
            return action(connection);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            DropConnection(handle);
            return SocketError;
        }
        catch (JsonException)
        {
            DropConnection(handle);
            return (short)Focas2ErrorCode.EW_UNEXP;
        }
    }

    private void DropConnection(ushort handle)
    {
        if (_connections.TryRemove(handle, out var connection))
        {
            connection.Dispose();
        }
    }

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs)
    {
        var args = new Dictionary<string, object?>(pairs.Length + 1, StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
        {
            args[key] = value;
        }
        return args;
    }

    private static short ReadInt16(JsonElement element, string property)
        => TryGetInt32(element, property, out var value) ? unchecked((short)value) : (short)0;

    private static int ReadInt32(JsonElement element, string property)
        => TryGetInt32(element, property, out var value) ? value : 0;

    private static long ReadInt64(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(property, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt64(out var parsed)
            ? parsed
            : 0L;

    private static string ReadString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(property, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool TryGetInt32(JsonElement element, string property, out int result)
    {
        result = 0;

        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(property, out var value))
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt32(out result))
            {
                return true;
            }

            if (value.TryGetDouble(out var asDouble))
            {
                result = (int)asDouble;
                return true;
            }
        }

        if (value.ValueKind == JsonValueKind.True)
        {
            result = 1;
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var handle in _connections.Keys)
        {
            DropConnection(handle);
        }
    }

    /// <summary>
    /// One socket to one simulated control. FOCAS2 is not thread safe per
    /// handle and the adapter already serialises on Focas2Thread; the lock here
    /// is belt and braces so a stray caller cannot interleave two frames.
    /// </summary>
    private sealed class SimulatorConnection : IDisposable
    {
        private readonly TcpClient _client;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private readonly object _gate = new();
        private long _sequence;

        private SimulatorConnection(TcpClient client, StreamReader reader, StreamWriter writer)
        {
            _client = client;
            _reader = reader;
            _writer = writer;
        }

        /// <summary>Handle the simulator issued for this socket.</summary>
        public ushort Handle { get; set; }

        public static SimulatorConnection Open(string ipAddress, ushort port, int timeoutSeconds)
        {
            var milliseconds = Math.Clamp(timeoutSeconds, 1, 120) * 1000;
            var client = new TcpClient { NoDelay = true };

            try
            {
                client.Connect(ipAddress, port);
                client.ReceiveTimeout = milliseconds;
                client.SendTimeout = milliseconds;

                var stream = client.GetStream();
                var reader = new StreamReader(stream, Encoding.UTF8, false, 8192, leaveOpen: true);
                var writer = new StreamWriter(stream, new UTF8Encoding(false), 8192, leaveOpen: true)
                {
                    AutoFlush = true,
                    NewLine = "\n",
                };

                return new SimulatorConnection(client, reader, writer);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        public void SetReadTimeout(int seconds)
        {
            var milliseconds = Math.Clamp(seconds, 1, 120) * 1000;
            _client.ReceiveTimeout = milliseconds;
            _client.SendTimeout = milliseconds;
        }

        /// <summary>Sends one request frame and returns the decoded return code.</summary>
        public short Call(string function, Dictionary<string, object?>? args, out JsonElement data)
        {
            string line;

            lock (_gate)
            {
                _sequence++;
                var request = JsonSerializer.Serialize(new
                {
                    seq = _sequence,
                    fn = function,
                    args = args ?? new Dictionary<string, object?>(StringComparer.Ordinal),
                });

                _writer.WriteLine(request);

                line = _reader.ReadLine()
                    ?? throw new IOException(string.Create(CultureInfo.InvariantCulture,
                        $"FOCAS2 simulator closed the connection during {function}."));
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            var ret = root.TryGetProperty("ret", out var retValue) && retValue.TryGetInt32(out var parsed)
                ? unchecked((short)parsed)
                : (short)Focas2ErrorCode.EW_UNEXP;

            data = root.TryGetProperty("data", out var payload) && payload.ValueKind != JsonValueKind.Null
                ? payload.Clone()
                : default;

            return ret;
        }

        public void Dispose()
        {
            try
            {
                _reader.Dispose();
                _writer.Dispose();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // Already torn down at the socket layer.
            }
            finally
            {
                _client.Dispose();
            }
        }
    }
}
