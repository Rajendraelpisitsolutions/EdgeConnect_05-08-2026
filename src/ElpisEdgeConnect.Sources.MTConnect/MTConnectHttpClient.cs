// ============================================================================
// File: MTConnectHttpClient.cs
// Purpose: Production IMTConnectClient implementation over System.Net.Http.
//          Owns a single HttpClient scoped to the configured Agent base URL
//          with the configured timeout. Thread-safe — the adapter's poll
//          loop is serialized by the source supervisor, but HttpClient is
//          safe to reuse across concurrent requests regardless.
// Reference: PHASE2_ENTRY.md Phase 2 adapter list
// ============================================================================

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ElpisEdgeConnect.Sources.MTConnect;

/// <summary>
/// Production <see cref="IMTConnectClient"/>. One instance per adapter;
/// owned by the adapter and disposed on <c>DisposeAsync</c>.
/// </summary>
internal sealed class MTConnectHttpClient : IMTConnectClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly string? _deviceName;
    private bool _disposed;

    public MTConnectHttpClient(MTConnectSourceConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var baseUrl = config.AgentBaseUrl;
        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += "/";
        }
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds),
        };
        _http.DefaultRequestHeaders.Add("Accept", "application/xml");
        _deviceName = config.AgentDeviceName;
    }

    /// <inheritdoc/>
    public Task<string> GetProbeAsync(CancellationToken ct) => GetAsync("probe", ct);

    /// <inheritdoc/>
    public Task<string> GetCurrentAsync(CancellationToken ct) => GetAsync("current", ct);

    private async Task<string> GetAsync(string endpoint, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var relative = string.IsNullOrEmpty(_deviceName) ? endpoint : $"{_deviceName}/{endpoint}";
        using var response = await _http.GetAsync(relative, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}
