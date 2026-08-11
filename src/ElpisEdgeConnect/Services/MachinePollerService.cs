// ============================================================================
// File: Services/MachinePollerService.cs
// Purpose: Polls a SINGLE Fanuc CNC machine on its own independent timer,
//          converts collected data to an MQTT payload, and writes it to
//          the shared Channel<MqttPayload> for the MQTT publisher to consume.
//
// DATA SOURCE AGNOSTIC:
//   Uses IMachineDataSource interface — works with MT-LINKi REST API,
//   FOCAS2 DLL, or HTTP REST data sources transparently.
//
// DATA FLOW:
//   ┌──────────────┐     ┌────────────────────┐     ┌──────────────────┐
//   │ PeriodicTimer │────▶│ IMachineDataSource  │────▶│ Channel<Payload> │
//   │  (1000ms)     │     │ .CollectDataAsync() │     │ .Writer.Write()  │
//   └──────────────┘     └────────────────────┘     └──────────────────┘
//                                                           │
//                                                           ▼
//                                                   MqttPublisherService
// ============================================================================

using System.Threading.Channels;
using ElpisEdgeConnect.Configuration;
using ElpisEdgeConnect.DataSources;
using ElpisEdgeConnect.Models;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Services;

/// <summary>
/// Polls a single Fanuc CNC machine on its own timer, converts collected data
/// to MQTT payloads, and writes them to the shared channel for publishing.
/// Uses IMachineDataSource for data-source-agnostic collection.
/// </summary>
public sealed class MachinePollerService : IAsyncDisposable
{
    private readonly IMachineDataSource _dataSource;
    private readonly MachineConfig _machineConfig;
    private readonly ChannelWriter<MqttPayload> _channelWriter;
    private readonly string _topicPrefix;
    private readonly string _publishMode;
    private readonly string _eremosTopicPrefix;
    private readonly ILogger _logger;

    private PeriodicTimer? _timer;
    private Task? _pollingTask;
    private CancellationTokenSource? _cts;

    public string MachineId => _machineConfig.MachineId;
    public bool IsRunning => _pollingTask is not null && !_pollingTask.IsCompleted;

    private long _pollCount;
    private long _errorCount;
    private long _consecutiveErrors;
    private bool _autoDisabled;
    private DateTimeOffset _lastPollTime;

    public long PollCount => Interlocked.Read(ref _pollCount);
    public long ErrorCount => Interlocked.Read(ref _errorCount);
    public long ConsecutiveErrors => Interlocked.Read(ref _consecutiveErrors);
    public bool AutoDisabled => _autoDisabled;
    public DateTimeOffset LastPollTime => _lastPollTime;

    public MachinePollerService(
        IMachineDataSource dataSource,
        MachineConfig machineConfig,
        ChannelWriter<MqttPayload> channelWriter,
        string topicPrefix,
        string publishMode,
        string eremosTopicPrefix,
        ILogger logger)
    {
        _dataSource = dataSource;
        _machineConfig = machineConfig;
        _channelWriter = channelWriter;
        _topicPrefix = topicPrefix;
        _publishMode = publishMode;
        _eremosTopicPrefix = eremosTopicPrefix;
        _logger = logger;
    }

    public void Start(CancellationToken externalToken = default)
    {
        if (IsRunning) return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_machineConfig.PollIntervalMs));
        _pollingTask = PollLoopAsync(_cts.Token);

        _logger.LogInformation(
            "Poller STARTED for {MachineId} ({MachineName}) via {SourceType} — every {IntervalMs}ms",
            _machineConfig.MachineId,
            _machineConfig.MachineName,
            _dataSource.SourceType,
            _machineConfig.PollIntervalMs);
    }

    public async Task StopAsync()
    {
        if (_cts is not null)
            await _cts.CancelAsync();

        if (_pollingTask is not null)
        {
            try { await _pollingTask; }
            catch (OperationCanceledException) { }
        }

        _timer?.Dispose();

        _logger.LogInformation(
            "Poller STOPPED for {MachineId} — {PollCount} polls, {ErrorCount} errors",
            _machineConfig.MachineId, _pollCount, _errorCount);
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        try
        {
            await PollOnceAsync(ct);
            while (await _timer!.WaitForNextTickAsync(ct))
                await PollOnceAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown — poller stop requested
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        // Safety guard: if auto-disabled, don't poll — prevents runaway connections to CNC
        if (_autoDisabled) return;

        try
        {
            var data = await _dataSource.CollectDataAsync(ct);
            Interlocked.Increment(ref _pollCount);
            _lastPollTime = DateTimeOffset.UtcNow;

            // Check if data source reports Offline/Error status (connection failed but no exception)
            if (data.Status == Models.MachineStatus.Offline || data.Status == Models.MachineStatus.Error)
            {
                var current = Interlocked.Increment(ref _consecutiveErrors);
                Interlocked.Increment(ref _errorCount);

                if (CheckAutoDisable(current))
                    return; // Auto-disabled — stop polling
            }
            else
            {
                // Successful data collection — reset consecutive error counter
                Interlocked.Exchange(ref _consecutiveErrors, 0);
            }

            // Add data source type to additional data for downstream consumers
            data.AdditionalData["dataSourceType"] = _dataSource.SourceType.ToString();

            if (string.Equals(_publishMode, "PerTag", StringComparison.OrdinalIgnoreCase))
            {
                // ── PerTag Mode: one MQTT message per tag ──
                var perTagEntries = MqttPayloadFormatter.ExtractPerTagEntries(data);
                foreach (var entry in perTagEntries)
                {
                    var payload = new MqttPayload
                    {
                        Topic = $"{_eremosTopicPrefix}/{data.MachineId}/{entry.TagName}",
                        PayloadBytes = System.Text.Encoding.UTF8.GetBytes(entry.Value),
                        QoS = 0, // QoS 0 for high-frequency telemetry
                        Retain = false,
                        MachineId = data.MachineId,
                        Timestamp = data.Timestamp
                    };

                    if (!_channelWriter.TryWrite(payload))
                    {
                        _logger.LogWarning(
                            "Channel full — back-pressure active for {MachineId}", _machineConfig.MachineId);
                        await _channelWriter.WriteAsync(payload, ct);
                    }
                }
            }
            else
            {
                // ── Bundled Mode: one JSON message per machine (legacy) ──
                var payload = ConvertToMqttPayload(data);

                if (!_channelWriter.TryWrite(payload))
                {
                    _logger.LogWarning(
                        "Channel full — back-pressure active for {MachineId}", _machineConfig.MachineId);
                    await _channelWriter.WriteAsync(payload, ct);
                }
            }

            // Log status + parts count every poll for live monitoring
            _logger.LogInformation(
                "Machine {MachineId}: Status={CncState}, Mode={Mode}, Parts={Parts}, Program={Program}, Tool=T{Tool}",
                _machineConfig.MachineId,
                data.CncState_path1_CNC ?? data.RunningStatus ?? "?",
                data.Mode_path1_CNC ?? data.AutoMode ?? "?",
                data.PartsNum_path1_CNC ?? data.PartsCount ?? 0,
                data.MainProgram_path1_CNC ?? data.MainProgram ?? "?",
                data.ToolInfo?.CurrentToolNumber.ToString() ?? "?");

            if (_pollCount % 100 == 0)
            {
                _logger.LogDebug(
                    "Machine {MachineId} [{Source}]: Poll #{Count}, Status: {Status}, Duration: {Duration}ms, Mode: {Mode}",
                    _machineConfig.MachineId, _dataSource.SourceType,
                    _pollCount, data.Status, data.CollectionDurationMs, _publishMode);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errorCount);
            var current = Interlocked.Increment(ref _consecutiveErrors);

            _logger.LogWarning(ex,
                "Machine {MachineId}: Poll error (total: {ErrorCount}, consecutive: {ConsecutiveErrors})",
                _machineConfig.MachineId, _errorCount, current);

            CheckAutoDisable(current);
        }
    }

    /// <summary>
    /// Checks if consecutive errors have hit the configured limit.
    /// If so, auto-disables the poller to protect the CNC controller.
    /// Returns true if the poller was auto-disabled.
    /// </summary>
    private bool CheckAutoDisable(long currentConsecutiveErrors)
    {
        var maxErrors = _machineConfig.MaxConsecutiveErrors;

        // MaxConsecutiveErrors = 0 means "never auto-disable"
        if (maxErrors <= 0) return false;

        if (currentConsecutiveErrors >= maxErrors)
        {
            _autoDisabled = true;
            _logger.LogCritical(
                "⚠ SAFETY AUTO-DISABLE: Machine {MachineId} ({MachineName}) disabled after {Count} consecutive errors. " +
                "Data source: {SourceType}. This protects the CNC from connection slot exhaustion. " +
                "To re-enable: restart the bridge, or change MaxConsecutiveErrors in config, or set Enabled=false then true (hot-reload).",
                _machineConfig.MachineId,
                _machineConfig.MachineName,
                currentConsecutiveErrors,
                _dataSource.SourceType);
            return true;
        }

        return false;
    }

    private MqttPayload ConvertToMqttPayload(CncMachineData data)
    {
        var topic = $"{_topicPrefix}/{data.MachineId}/data";

        // Resolve DeviceId: config override → MachineId fallback
        var deviceId = !string.IsNullOrEmpty(_machineConfig.DeviceId)
            ? _machineConfig.DeviceId
            : _machineConfig.MachineId;

        // Resolve DeviceName: config override → MachineName (sanitized) → MachineId
        var deviceName = !string.IsNullOrEmpty(_machineConfig.DeviceName)
            ? _machineConfig.DeviceName
            : !string.IsNullOrEmpty(_machineConfig.MachineName)
                ? SanitizeDeviceName(_machineConfig.MachineName)
                : _machineConfig.MachineId;

        // Format: { "DeviceId": "...", "Data": ["DeviceName.Tag,Value,Timestamp,DataTypeId", ...] }
        var jsonBytes = MqttPayloadFormatter.FormatPayload(data, deviceId, deviceName);

        return new MqttPayload
        {
            Topic = topic,
            PayloadBytes = jsonBytes,
            QoS = 1,
            Retain = false,
            MachineId = data.MachineId,
            Timestamp = data.Timestamp
        };
    }

    /// <summary>
    /// Removes spaces and special characters from MachineName to create
    /// a valid DeviceName prefix (e.g., "Lathe Bay 1" → "LatheBay1").
    /// </summary>
    private static string SanitizeDeviceName(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
        }
        return sb.Length > 0 ? sb.ToString() : "Device";
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
        _dataSource.Dispose();
    }
}
