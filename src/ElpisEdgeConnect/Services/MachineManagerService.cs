// ============================================================================
// File: Services/MachineManagerService.cs
// Purpose: The ORCHESTRATOR — manages the lifecycle of all machine pollers.
//          Supports three data source types per machine (MT-LINKi REST API,
//          FOCAS2 DLL, HTTP REST) via the IDataSourceFactory abstraction.
//
// RESPONSIBILITIES:
//   1. Read machine configurations from IOptionsMonitor (hot-reloadable)
//   2. Create one MachinePollerService per enabled machine (with correct data source)
//   3. Enforce concurrency limits during startup
//   4. Monitor poller health and publish diagnostics
//   5. Handle hot-reload: start new machines, stop removed ones
//   6. Publish bridge status ("online"/"offline") to MQTT
//   7. Gracefully shutdown all pollers on service stop
// ============================================================================

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using ElpisEdgeConnect.Configuration;
using ElpisEdgeConnect.DataSources;
using ElpisEdgeConnect.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ElpisEdgeConnect.Services;

/// <summary>
/// Orchestrator that manages the lifecycle of all machine pollers.
/// Uses IDataSourceFactory to create the correct data source per machine.
/// </summary>
public sealed class MachineManagerService : BackgroundService
{
    private readonly IDataSourceFactory _dataSourceFactory;
    private readonly MqttPublisherService _mqttPublisher;
    private readonly IOptionsMonitor<List<MachineConfig>> _machinesMonitor;
    private readonly AppSettings _appSettings;
    private readonly MqttSettings _mqttSettings;
    private readonly ILogger<MachineManagerService> _logger;
    private readonly ILoggerFactory _loggerFactory;

    private readonly ConcurrentDictionary<string, MachinePollerService> _pollers = new();
    private readonly SemaphoreSlim _concurrencyGate;

    public MachineManagerService(
        IDataSourceFactory dataSourceFactory,
        MqttPublisherService mqttPublisher,
        IOptionsMonitor<List<MachineConfig>> machinesMonitor,
        IOptions<AppSettings> appSettings,
        IOptions<MqttSettings> mqttSettings,
        ILogger<MachineManagerService> logger,
        ILoggerFactory loggerFactory)
    {
        _dataSourceFactory = dataSourceFactory;
        _mqttPublisher = mqttPublisher;
        _machinesMonitor = machinesMonitor;
        _appSettings = appSettings.Value;
        _mqttSettings = mqttSettings.Value;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _concurrencyGate = new SemaphoreSlim(_appSettings.MaxParallelPolls);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // In DryRun mode, don't wait for MQTT connection — it may not connect at all
            if (_mqttSettings.DryRun)
            {
                _logger.LogInformation("Machine Manager starting — DryRun mode, skipping MQTT wait");
            }
            else
            {
                _logger.LogInformation("Machine Manager starting — waiting for MQTT connection...");
                while (!_mqttPublisher.IsConnected && !stoppingToken.IsCancellationRequested)
                    await Task.Delay(500, stoppingToken);
            }

            await PublishBridgeStatusAsync("online", stoppingToken);

            // Log data source summary
            var machines = _machinesMonitor.CurrentValue;
            var summary = machines.Where(m => m.Enabled)
                .GroupBy(m => m.DataSourceType)
                .Select(g => $"{g.Key}: {g.Count()}")
                .ToList();
            _logger.LogInformation(
                "Starting pollers for {Count} machines — Data sources: {Summary}",
                machines.Count(m => m.Enabled),
                string.Join(", ", summary));

            await StartPollersAsync(machines, stoppingToken);

            _machinesMonitor.OnChange(async newConfig =>
            {
                _logger.LogInformation("Machine configuration changed — reconciling pollers...");
                await ReconcilePollersAsync(newConfig, stoppingToken);
            });

            // Diagnostics loop — runs until shutdown
            using var diagnosticsTimer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await diagnosticsTimer.WaitForNextTickAsync(stoppingToken))
                await PublishDiagnosticsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown — host requested stop via Ctrl+C or service stop.
            // Do not propagate — this prevents the "Hosting failed to start" crash.
            _logger.LogInformation("Machine Manager received shutdown signal");
        }
    }

    private async Task StartPollersAsync(List<MachineConfig> machines, CancellationToken ct)
    {
        var enabledMachines = machines.Where(m => m.Enabled).ToList();

        _logger.LogInformation(
            "Starting {Enabled} pollers out of {Total} configured (max parallel: {MaxParallel})",
            enabledMachines.Count, machines.Count, _appSettings.MaxParallelPolls);

        var startTasks = enabledMachines.Select(async config =>
        {
            await _concurrencyGate.WaitAsync(ct);
            try { StartPoller(config, ct); }
            finally { _concurrencyGate.Release(); }
        });

        await Task.WhenAll(startTasks);
        _logger.LogInformation("All {Count} pollers started", _pollers.Count);
    }

    private void StartPoller(MachineConfig config, CancellationToken ct)
    {
        if (_pollers.ContainsKey(config.MachineId))
        {
            _logger.LogDebug("Poller for {MachineId} already exists — skipping", config.MachineId);
            return;
        }

        try
        {
            // Create the appropriate data source (MT-LINKi REST, FOCAS2 DLL, or HTTP REST)
            var dataSource = _dataSourceFactory.GetOrCreate(config);

            var pollerLogger = _loggerFactory.CreateLogger<MachinePollerService>();
            var poller = new MachinePollerService(
                dataSource,
                config,
                _mqttPublisher.Writer,
                _mqttSettings.TopicPrefix,
                _mqttSettings.PublishMode,
                _mqttSettings.EremosTopicPrefix,
                pollerLogger);

            if (_pollers.TryAdd(config.MachineId, poller))
            {
                poller.Start(ct);
            }
            else
            {
                _logger.LogWarning("Failed to register poller for {MachineId}", config.MachineId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Machine {MachineId}: Failed to create {SourceType} data source — skipping",
                config.MachineId, config.DataSourceType);
        }
    }

    private async Task ReconcilePollersAsync(List<MachineConfig> newConfig, CancellationToken ct)
    {
        var enabledIds = newConfig.Where(m => m.Enabled).Select(m => m.MachineId).ToHashSet();
        var runningIds = _pollers.Keys.ToHashSet();

        // Stop removed/disabled machines
        var toStop = runningIds.Except(enabledIds).ToList();
        foreach (var id in toStop)
        {
            if (_pollers.TryRemove(id, out var poller))
            {
                _logger.LogInformation("Stopping poller for {MachineId} (removed/disabled)", id);
                await poller.StopAsync();
                await poller.DisposeAsync();
                _dataSourceFactory.Remove(id);
            }
        }

        // Start new/re-enabled machines
        var toStart = enabledIds.Except(runningIds).ToList();
        foreach (var config in newConfig.Where(m => toStart.Contains(m.MachineId)))
        {
            _logger.LogInformation("Starting poller for new machine {MachineId} [{SourceType}]",
                config.MachineId, config.DataSourceType);
            StartPoller(config, ct);
        }

        _logger.LogInformation(
            "Reconciliation complete — stopped: {Stopped}, started: {Started}, total: {Total}",
            toStop.Count, toStart.Count, _pollers.Count);
    }

    private async Task PublishDiagnosticsAsync(CancellationToken ct)
    {
        var diagnostics = new
        {
            bridgeId = _mqttSettings.ClientId,
            timestamp = DateTimeOffset.UtcNow,
            machineCount = _pollers.Count,
            mqttPublished = _mqttPublisher.PublishedCount,
            mqttFailed = _mqttPublisher.FailedCount,
            mqttDropped = _mqttPublisher.DroppedCount,
            mqttConnected = _mqttPublisher.IsConnected,
            machines = _pollers.Values.Select(p => new
            {
                machineId = p.MachineId,
                isRunning = p.IsRunning,
                autoDisabled = p.AutoDisabled,
                pollCount = p.PollCount,
                errorCount = p.ErrorCount,
                consecutiveErrors = p.ConsecutiveErrors,
                lastPollTime = p.LastPollTime
            }).ToList()
        };

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(diagnostics, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        var payload = new MqttPayload
        {
            Topic = $"{_mqttSettings.TopicPrefix}/$bridge/diagnostics",
            PayloadBytes = jsonBytes,
            QoS = 0, Retain = true,
            MachineId = "bridge",
            Timestamp = DateTimeOffset.UtcNow
        };

        _mqttPublisher.Writer.TryWrite(payload);

        _logger.LogInformation(
            "Bridge diagnostics — Machines: {Count}, Published: {Pub}, Failed: {Fail}, Dropped: {Drop}",
            _pollers.Count, _mqttPublisher.PublishedCount,
            _mqttPublisher.FailedCount, _mqttPublisher.DroppedCount);
    }

    private async Task PublishBridgeStatusAsync(string status, CancellationToken ct)
    {
        var payload = new MqttPayload
        {
            Topic = $"{_mqttSettings.TopicPrefix}/$bridge/status",
            PayloadBytes = Encoding.UTF8.GetBytes(status),
            QoS = 1, Retain = true,
            MachineId = "bridge",
            Timestamp = DateTimeOffset.UtcNow
        };
        _mqttPublisher.Writer.TryWrite(payload);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Machine Manager shutting down — stopping {Count} pollers...", _pollers.Count);

        var stopTasks = _pollers.Values.Select(async p =>
        {
            await p.StopAsync();
            await p.DisposeAsync();
        });

        await Task.WhenAll(stopTasks);
        _pollers.Clear();

        await PublishBridgeStatusAsync("offline", cancellationToken);
        await base.StopAsync(cancellationToken);

        _logger.LogInformation("Machine Manager shutdown complete");
    }
}
