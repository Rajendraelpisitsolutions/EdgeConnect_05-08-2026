// ============================================================================
// File: DataSources/HttpRestDataSource.cs
// Purpose: Data source using HTTP REST API calls to a FOCAS web service,
//          custom REST wrapper, or the CNC Simulator.
//
//          This is the original data acquisition method from the bridge's
//          initial implementation, preserved for backward compatibility
//          and simulator testing.
//
// USE CASES:
//   1. FanucCncSimulator — development/testing with simulated CNC data
//   2. Custom REST wrappers — third-party HTTP API around CNC controllers
//   3. Legacy configurations — existing deployments using BaseUrl
// ============================================================================

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using ElpisEdgeConnect.Configuration;
using ElpisEdgeConnect.Models;
using ElpisEdgeConnect.Resilience;
using ElpisEdgeConnect.Security;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;

namespace ElpisEdgeConnect.DataSources;

/// <summary>
/// HTTP REST data source — communicates via HTTP GET calls to a web service.
/// Used for the CNC Simulator, custom FOCAS REST wrappers, and backward compatibility.
/// </summary>
public sealed class HttpRestDataSource : IMachineDataSource
{
    private readonly HttpClient _httpClient;
    private readonly MachineConfig _config;
    private readonly ILogger _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _resiliencePipeline;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    public string MachineId => _config.MachineId;
    public string MachineName => _config.MachineName;
    public DataSourceType SourceType => DataSourceType.HttpRest;

    public HttpRestDataSource(
        MachineConfig config,
        ISecretProvider secretProvider,
        ILogger logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 2,
            ConnectTimeout = TimeSpan.FromMilliseconds(config.TimeoutMs),
            SslOptions = BuildSslOptions(config)
        };

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(config.BaseUrl),
            Timeout = TimeSpan.FromMilliseconds(config.TimeoutMs * 2)
        };

        ConfigureAuthentication(config, secretProvider);
        _resiliencePipeline = ResiliencePolicies.CreateHttpPipeline(config.MachineId, logger);
    }

    public async Task<CncMachineData> CollectDataAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var data = new CncMachineData
        {
            MachineId = _config.MachineId,
            MachineName = _config.MachineName,
            Tags = _config.Tags,
            Timestamp = DateTimeOffset.UtcNow
        };

        try
        {
            var tasks = new List<Task>();

            if (_config.DataPoints.Contains("Program/MainProgram") ||
                _config.DataPoints.Contains("Program/RunningStatus"))
                tasks.Add(CollectProgramDataAsync(data, ct));

            if (_config.DataPoints.Any(d => d.StartsWith("Axes/")))
                tasks.Add(CollectAxisDataAsync(data, ct));

            if (_config.DataPoints.Any(d => d.StartsWith("Spindle/")))
                tasks.Add(CollectSpindleDataAsync(data, ct));

            if (_config.DataPoints.Contains("Alarms/Active"))
                tasks.Add(CollectAlarmDataAsync(data, ct));

            if (_config.DataPoints.Contains("CycleTime"))
                tasks.Add(CollectCycleTimeAsync(data, ct));

            if (_config.DataPoints.Contains("PartsCount"))
                tasks.Add(CollectPartsCountAsync(data, ct));

            await Task.WhenAll(tasks);
            data.Status = MachineStatus.Online;
        }
        catch (BrokenCircuitException)
        {
            data.Status = MachineStatus.Offline;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            data.Status = MachineStatus.Error;
            _logger.LogWarning(ex, "Machine {MachineId}: HTTP data collection failed", _config.MachineId);
        }
        finally
        {
            sw.Stop();
            data.CollectionDurationMs = sw.ElapsedMilliseconds;
        }

        return data;
    }

    public async Task<bool> TestConnectionAsync(CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.GetAsync("focas2/program", ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // =========================================================================
    // DATA COLLECTION METHODS (same as original FanucApiClient)
    // =========================================================================

    private async Task CollectProgramDataAsync(CncMachineData data, CancellationToken ct)
    {
        var json = await GetJsonAsync("focas2/program", ct);
        if (json is null) return;
        data.MainProgram = json.GetPropertyOrDefault("mainProgram")?.GetString();
        data.RunningStatus = json.GetPropertyOrDefault("runStatus")?.GetString();
    }

    private async Task CollectAxisDataAsync(CncMachineData data, CancellationToken ct)
    {
        var json = await GetJsonAsync("focas2/position", ct);
        if (json is null) return;

        if (json.Value.TryGetProperty("axes", out var axes) &&
            axes.ValueKind == JsonValueKind.Array)
        {
            foreach (var axis in axes.EnumerateArray())
            {
                var name = axis.GetPropertyOrDefault("name")?.GetString() ?? "Unknown";
                data.Axes[name] = new AxisData
                {
                    AxisName = name,
                    AbsolutePosition = axis.GetPropertyOrDefault("absolute")?.GetDouble(),
                    MachinePosition = axis.GetPropertyOrDefault("machine")?.GetDouble(),
                    RelativePosition = axis.GetPropertyOrDefault("relative")?.GetDouble(),
                    DistanceToGo = axis.GetPropertyOrDefault("distanceToGo")?.GetDouble()
                };
            }
        }

        data.FeedRate = json.GetPropertyOrDefault("feedRate")?.GetDouble();
    }

    private async Task CollectSpindleDataAsync(CncMachineData data, CancellationToken ct)
    {
        var json = await GetJsonAsync("focas2/spindle", ct);
        if (json is null) return;

        data.Spindle = new SpindleData
        {
            Speed = json.GetPropertyOrDefault("speed")?.GetDouble(),
            Load = json.GetPropertyOrDefault("load")?.GetDouble(),
            Direction = json.GetPropertyOrDefault("direction")?.GetString()
        };
    }

    private async Task CollectAlarmDataAsync(CncMachineData data, CancellationToken ct)
    {
        var json = await GetJsonAsync("focas2/alarm", ct);
        if (json is null) return;

        if (json.Value.TryGetProperty("alarms", out var alarms) &&
            alarms.ValueKind == JsonValueKind.Array)
        {
            foreach (var alarm in alarms.EnumerateArray())
            {
                data.ActiveAlarms.Add(new AlarmData
                {
                    AlarmNumber = alarm.GetPropertyOrDefault("number")?.GetInt32() ?? 0,
                    AlarmType = alarm.GetPropertyOrDefault("type")?.GetString() ?? "",
                    Message = alarm.GetPropertyOrDefault("message")?.GetString() ?? "",
                    Axis = alarm.GetPropertyOrDefault("axis")?.GetString() ?? ""
                });
            }

            if (data.ActiveAlarms.Count > 0)
                data.Status = MachineStatus.Alarm;
        }
    }

    private async Task CollectCycleTimeAsync(CncMachineData data, CancellationToken ct)
    {
        var json = await GetJsonAsync("focas2/cycletime", ct);
        if (json is null) return;
        data.CycleTimeSeconds = json.GetPropertyOrDefault("cycleTime")?.GetDouble();
    }

    private async Task CollectPartsCountAsync(CncMachineData data, CancellationToken ct)
    {
        var json = await GetJsonAsync("focas2/counter", ct);
        if (json is null) return;
        data.PartsCount = json.GetPropertyOrDefault("partsCount")?.GetInt64();
    }

    // =========================================================================
    // HTTP HELPERS
    // =========================================================================

    private async Task<JsonElement?> GetJsonAsync(string endpoint, CancellationToken ct)
    {
        try
        {
            var response = await _resiliencePipeline.ExecuteAsync(
                async token =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                    return await _httpClient.SendAsync(request, token);
                }, ct);

            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync(ct);
            var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return doc.RootElement;
        }
        catch (BrokenCircuitException) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Machine {MachineId}: Failed to GET /{Endpoint}",
                _config.MachineId, endpoint);
            return null;
        }
    }

    private void ConfigureAuthentication(MachineConfig config, ISecretProvider secretProvider)
    {
        var password = secretProvider.Resolve(config.Password);
        switch (config.AuthType)
        {
            case AuthType.Basic:
                var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.Username}:{password}"));
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
                break;
            case AuthType.Bearer:
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", password);
                break;
        }
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static SslClientAuthenticationOptions BuildSslOptions(MachineConfig config)
    {
        var options = new SslClientAuthenticationOptions
        {
            EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                                  System.Security.Authentication.SslProtocols.Tls13,
        };

        if (!string.IsNullOrEmpty(config.CertificateThumbprint))
        {
            options.RemoteCertificateValidationCallback = (_, cert, _, _) =>
            {
                if (cert is null) return false;
                var thumbprint = cert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);
                return string.Equals(thumbprint, config.CertificateThumbprint, StringComparison.OrdinalIgnoreCase);
            };
        }
        else
        {
            options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        }

        return options;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }
}

/// <summary>
/// Extension methods for safe JsonElement property access.
/// </summary>
internal static class JsonElementExtensions
{
    public static JsonElement? GetPropertyOrDefault(this JsonElement? element, string propertyName)
    {
        if (element is null) return null;
        return element.Value.TryGetProperty(propertyName, out var prop) ? prop : null;
    }

    public static JsonElement? GetPropertyOrDefault(this JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) ? prop : null;
    }
}
