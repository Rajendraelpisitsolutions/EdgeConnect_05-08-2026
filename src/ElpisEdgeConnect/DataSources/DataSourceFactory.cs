// ============================================================================
// File: DataSources/DataSourceFactory.cs
// Purpose: Creates and caches IMachineDataSource instances — one per CNC machine.
//          Based on MachineConfig.DataSourceType, creates the correct implementation:
//            MtLinki   → MtLinkiRestDataSource   (reads from REST API)
//            Focas2Dll → Focas2DllDataSource      (native DLL P/Invoke)
//            HttpRest  → HttpRestDataSource        (HTTP REST calls)
//
// HISTORY: Evolved from FanucApiClientFactory (which only supported HTTP REST)
// ============================================================================

using System.Collections.Concurrent;
using ElpisEdgeConnect.Configuration;
using ElpisEdgeConnect.Security;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.DataSources;

/// <summary>
/// Interface for creating and managing IMachineDataSource instances.
/// </summary>
public interface IDataSourceFactory : IDisposable
{
    /// <summary>
    /// Get an existing data source for a machine, or create a new one.
    /// Thread-safe — can be called from multiple pollers concurrently.
    /// </summary>
    IMachineDataSource GetOrCreate(MachineConfig config);

    /// <summary>
    /// Remove and dispose the data source for a specific machine.
    /// Called when a machine is removed from configuration.
    /// </summary>
    void Remove(string machineId);
}

/// <summary>
/// Factory that creates the correct IMachineDataSource implementation
/// based on each machine's DataSourceType configuration.
/// 
/// Registered as SINGLETON in DI (Program.cs).
/// </summary>
public sealed class DataSourceFactory : IDataSourceFactory
{
    private readonly ConcurrentDictionary<string, IMachineDataSource> _sources = new();
    private readonly ISecretProvider _secretProvider;
    private readonly ILoggerFactory _loggerFactory;
    private bool _disposed;

    public DataSourceFactory(ISecretProvider secretProvider, ILoggerFactory loggerFactory)
    {
        _secretProvider = secretProvider;
        _loggerFactory = loggerFactory;
    }

    public IMachineDataSource GetOrCreate(MachineConfig config)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _sources.GetOrAdd(config.MachineId, _ =>
        {
            var logger = _loggerFactory.CreateLogger($"DataSource.{config.DataSourceType}.{config.MachineId}");

            logger.LogInformation(
                "Creating {SourceType} data source for machine {MachineId} ({MachineName})",
                config.DataSourceType,
                config.MachineId,
                config.MachineName);

            IMachineDataSource source = config.DataSourceType switch
            {
                DataSourceType.MtLinki => CreateMtLinkiSource(config, logger),
                DataSourceType.Focas2Dll => CreateFocas2Source(config, logger),
                DataSourceType.HttpRest => CreateHttpRestSource(config, logger),
                DataSourceType.MTConnect => CreateMTConnectSource(config, logger),
                DataSourceType.BrotherHttp => CreateBrotherHttpSource(config, logger),
                _ => throw new ArgumentException(
                    $"Unknown DataSourceType '{config.DataSourceType}' for machine {config.MachineId}")
            };

            return source;
        });
    }

    public void Remove(string machineId)
    {
        if (_sources.TryRemove(machineId, out var source))
        {
            source.Dispose();
        }
    }

    private MtLinkiRestDataSource CreateMtLinkiSource(MachineConfig config, ILogger logger)
    {
        if (config.MtLinki == null)
        {
            throw new InvalidOperationException(
                $"Machine {config.MachineId}: DataSourceType=MtLinki requires MtLinki settings. " +
                "Add a \"MtLinki\" section with BaseUrl (e.g. http://192.168.2.167:3000).");
        }

        // Resolve credentials from env vars if needed
        if (config.MtLinki.Password.StartsWith("env:"))
        {
            config.MtLinki.Password = _secretProvider.Resolve(config.MtLinki.Password);
        }

        logger.LogInformation(
            "Machine {MachineId}: MT-LINKi REST API → {BaseUrl}/api/{ApiVersion}/equipment/{EquipmentType}, machine={Identifier}",
            config.MachineId,
            config.MtLinki.BaseUrl,
            config.MtLinki.ApiVersion,
            config.MtLinki.EquipmentType,
            config.MtLinki.MachineIdentifier);

        return new MtLinkiRestDataSource(config, logger);
    }

    private Focas2DllDataSource CreateFocas2Source(MachineConfig config, ILogger logger)
    {
        if (config.Focas2 == null)
        {
            throw new InvalidOperationException(
                $"Machine {config.MachineId}: DataSourceType=Focas2Dll requires Focas2 settings. " +
                "Add a \"Focas2\" section with IpAddress and Port.");
        }

        logger.LogInformation(
            "Machine {MachineId}: FOCAS2 DLL → {IpAddress}:{Port} (KeepAlive={KeepAlive})",
            config.MachineId,
            config.Focas2.IpAddress,
            config.Focas2.Port,
            config.Focas2.KeepAlive);

        return new Focas2DllDataSource(config, logger);
    }

    private MTConnectDataSource CreateMTConnectSource(MachineConfig config, ILogger logger)
    {
        if (config.MTConnect == null)
        {
            throw new InvalidOperationException(
                $"Machine {config.MachineId}: DataSourceType=MTConnect requires MTConnect settings. " +
                "Add a \"MTConnect\" section with BaseUrl (e.g. http://192.168.2.110:5000).");
        }

        logger.LogInformation(
            "Machine {MachineId}: MTConnect → {BaseUrl} (Device={DeviceName})",
            config.MachineId,
            config.MTConnect.BaseUrl,
            string.IsNullOrEmpty(config.MTConnect.DeviceName) ? "(auto)" : config.MTConnect.DeviceName);

        return new MTConnectDataSource(config, logger);
    }

    private BrotherHttpDataSource CreateBrotherHttpSource(MachineConfig config, ILogger logger)
    {
        if (config.BrotherHttp == null)
        {
            throw new InvalidOperationException(
                $"Machine {config.MachineId}: DataSourceType=BrotherHttp requires BrotherHttp settings. " +
                "Add a \"BrotherHttp\" section with BaseUrl (e.g. http://192.168.2.110).");
        }

        logger.LogInformation(
            "Machine {MachineId}: Brother HTTP → {BaseUrl}",
            config.MachineId,
            config.BrotherHttp.BaseUrl);

        return new BrotherHttpDataSource(config, logger);
    }

    private HttpRestDataSource CreateHttpRestSource(MachineConfig config, ILogger logger)
    {
        if (string.IsNullOrEmpty(config.BaseUrl))
        {
            throw new InvalidOperationException(
                $"Machine {config.MachineId}: DataSourceType=HttpRest requires BaseUrl. " +
                "Set BaseUrl to the CNC simulator or REST API endpoint.");
        }

        logger.LogInformation(
            "Machine {MachineId}: HTTP REST → {BaseUrl}",
            config.MachineId,
            config.BaseUrl);

        return new HttpRestDataSource(config, _secretProvider, logger);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var source in _sources.Values) source.Dispose();
        _sources.Clear();
    }
}
