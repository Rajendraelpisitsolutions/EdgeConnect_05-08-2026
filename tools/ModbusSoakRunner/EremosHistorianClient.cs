// ============================================================================
// File: EremosHistorianClient.cs
// Purpose: Tiny HTTP client that pings EREMOS V2's historian API to count
//          how many tags it has ingested under a given deviceClass. Used
//          by the soak runner to close the loop end-to-end:
//
//             EdgeConnect publish_count   ←→   EREMOS V2 historian count
//
//          If the ratio is ~1.0 we know the full chain
//          (Modbus → adapter → routing → MQTT → broker → EREMOS subscriber →
//          historian write) is healthy. If publish is high but historian
//          count is low, the broker or subscriber side is dropping data.
//
// Auth: supports either a pre-acquired JWT, or username+password
// credentials. With credentials, the client logs in on first use and
// transparently re-authenticates on 401 (handles token expiry during a
// long soak).
//
// URL convention: the supplied base URL must include the API version
// prefix (e.g. http://host/api/v1/). The client appends bare resource
// names (historian, auth/login). This matches what users see in their
// API docs and avoids double-prefix bugs.
//
// Defensive parsing: the EREMOS V2 historian response shape isn't fully
// documented here — we accept several common conventions:
//   - Top-level integer (count-only endpoint)
//   - JSON object with `count`, `totalCount`, or `total` field
//   - JSON array (length = count, but capped by server-side pagination)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ElpisEdgeConnect.Tools.ModbusSoakRunner;

/// <summary>
/// Credential bundle for username/password login. Field is named
/// <c>Username</c> rather than <c>Email</c> because EREMOS V2's API
/// validates the request as <c>{ "username": "...", "password": "..." }</c>
/// — even though the value is conventionally an email address.
/// </summary>
internal sealed record EremosCredentials(string Username, string Password);

/// <summary>
/// Minimal HTTP client that polls EREMOS V2's historian to count tags
/// ingested under a given <c>deviceClass</c>. Failures and unparseable
/// responses are swallowed and surfaced to the caller as
/// <see langword="null"/> — the soak shouldn't fall over because the
/// management API was briefly unavailable.
/// </summary>
internal sealed class EremosHistorianClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly string _deviceClass;
    private readonly EremosCredentials? _credentials;
    private string? _bearerToken;

    /// <summary>
    /// Create a client.
    /// </summary>
    /// <param name="baseUrl">
    /// Full API base URL including the version prefix, e.g.
    /// <c>http://host/api/v1/</c>. The client appends bare resource paths
    /// (<c>historian</c>, <c>auth/login</c>) underneath.
    /// </param>
    /// <param name="deviceClass">Historian filter value.</param>
    /// <param name="bearerToken">Pre-acquired JWT, optional.</param>
    /// <param name="credentials">Username/password — used to log in if no token supplied or on 401.</param>
    public EremosHistorianClient(
        string baseUrl,
        string deviceClass,
        string? bearerToken = null,
        EremosCredentials? credentials = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceClass);

        _baseUri = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        _deviceClass = deviceClass;
        _bearerToken = bearerToken;
        _credentials = credentials;

        _http = new HttpClient
        {
            BaseAddress = _baseUri,
            Timeout = TimeSpan.FromSeconds(10),
        };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>Best-effort connectivity probe — used during the runner's startup phase.</summary>
    public async Task<bool> ProbeAsync(CancellationToken ct)
    {
        // Make sure we're authed (so probe doesn't return 401 just because we never logged in).
        await EnsureAuthAsync(ct).ConfigureAwait(false);
        try
        {
            using var req = NewAuthorizedRequest(HttpMethod.Get, BuildPath(sinceUtc: null, limit: 1));
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Count tags ingested for <c>deviceClass</c> since
    /// <paramref name="sinceUtc"/>. Returns <see langword="null"/> on any
    /// transport failure or unparseable response — caller treats that as
    /// "no data this tick" and continues.
    /// </summary>
    public async Task<long?> CountSinceAsync(DateTime? sinceUtc, CancellationToken ct)
    {
        try
        {
            await EnsureAuthAsync(ct).ConfigureAwait(false);

            using var req = NewAuthorizedRequest(HttpMethod.Get, BuildPath(sinceUtc, limit: null));
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);

            // Single retry on 401 — token expired mid-soak, log in again.
            if (resp.StatusCode == HttpStatusCode.Unauthorized && _credentials is not null)
            {
                _bearerToken = null;
                await EnsureAuthAsync(ct).ConfigureAwait(false);
                using var retry = NewAuthorizedRequest(HttpMethod.Get, BuildPath(sinceUtc, limit: null));
                using var retryResp = await _http.SendAsync(retry, ct).ConfigureAwait(false);
                if (!retryResp.IsSuccessStatusCode) return null;
                return TryExtractCount(await retryResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            }

            if (!resp.IsSuccessStatusCode) return null;
            return TryExtractCount(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient timeout — treat as transient.
            return null;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();

    // =========================================================================
    // PRIVATE — auth
    // =========================================================================

    private async Task EnsureAuthAsync(CancellationToken ct)
    {
        if (_bearerToken is not null) return;
        if (_credentials is null) return;

        try
        {
            // EREMOS V2 expects {"username": "...", "password": "..."} —
            // its FluentValidation rejects an empty Username (HTTP 400) if
            // the field is missing. The "username" value is conventionally
            // an email but the field name is fixed.
            using var req = new HttpRequestMessage(HttpMethod.Post, "auth/login")
            {
                Content = JsonContent.Create(new
                {
                    username = _credentials.Username,
                    password = _credentials.Password,
                }),
            };
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return;
            }
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _bearerToken = TryExtractToken(body);
        }
        catch
        {
            // Login failed — leave token null; subsequent requests will hit the same wall and skip.
        }
    }

    private HttpRequestMessage NewAuthorizedRequest(HttpMethod method, string relativePath)
    {
        var req = new HttpRequestMessage(method, relativePath);
        if (_bearerToken is not null)
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
        }
        return req;
    }

    /// <summary>
    /// Pull a token from a login response. Accepts common shapes:
    /// <list type="bullet">
    ///   <item><c>{ "token": "..." }</c></item>
    ///   <item><c>{ "accessToken": "..." }</c></item>
    ///   <item><c>{ "access_token": "..." }</c> (OAuth2)</item>
    ///   <item><c>{ "data": { "token": "..." } }</c></item>
    /// </list>
    /// </summary>
    private static string? TryExtractToken(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            foreach (var key in TokenKeys)
            {
                if (root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                {
                    return v.GetString();
                }
            }
            // Nested data object?
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in TokenKeys)
                {
                    if (data.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                    {
                        return v.GetString();
                    }
                }
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly string[] TokenKeys = { "token", "accessToken", "access_token", "jwt" };

    // =========================================================================
    // PRIVATE — historian path
    // =========================================================================

    private string BuildPath(DateTime? sinceUtc, int? limit)
    {
        // The base URL already carries /api/v1/ — append only the bare resource.
        var path = $"historian?deviceClass={Uri.EscapeDataString(_deviceClass)}";
        if (sinceUtc is { } since)
        {
            path += $"&since={Uri.EscapeDataString(since.ToString("O", CultureInfo.InvariantCulture))}";
        }
        if (limit is { } cap)
        {
            path += $"&limit={cap}";
        }
        return path;
    }

    /// <summary>
    /// Try to pull a count from the response body. Accepts:
    /// <list type="bullet">
    ///   <item>Top-level integer (count-only endpoint)</item>
    ///   <item>JSON object with <c>count</c> / <c>totalCount</c> / <c>total</c></item>
    ///   <item>JSON array (length, but server-side pagination caps it)</item>
    /// </list>
    /// Returns <see langword="null"/> when nothing recognizable is found.
    /// </summary>
    private static long? TryExtractCount(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            switch (root.ValueKind)
            {
                case JsonValueKind.Number:
                    return root.TryGetInt64(out var asLong) ? asLong : null;

                case JsonValueKind.Array:
                    return root.GetArrayLength();

                case JsonValueKind.Object:
                    foreach (var name in CountFieldNames)
                    {
                        if (root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
                            && v.TryGetInt64(out var n))
                        {
                            return n;
                        }
                    }
                    // If the object carries an array under a common key, fall back
                    // to that array's length.
                    foreach (var listName in ListFieldNames)
                    {
                        if (root.TryGetProperty(listName, out var arr) && arr.ValueKind == JsonValueKind.Array)
                        {
                            return arr.GetArrayLength();
                        }
                    }
                    return null;

                default:
                    return null;
            }
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly string[] CountFieldNames = { "count", "totalCount", "total" };
    private static readonly string[] ListFieldNames = { "items", "data", "results", "points" };
}
