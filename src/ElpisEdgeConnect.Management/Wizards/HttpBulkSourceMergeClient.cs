// ============================================================================
// File: Wizards/HttpBulkSourceMergeClient.cs
// Purpose: Real HttpClient-backed implementation of IBulkSourceMergeClient.
//          Hits the local /api/v1/sources/bulk-preview + /bulk-submit
//          endpoints. Scoped lifetime: each Razor circuit gets its own
//          client tied to the per-circuit HttpClient (which already carries
//          the Studio's same-origin BaseAddress).
// ============================================================================

using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Management.Contracts.BulkSourceMerge;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>Real HttpClient-backed BulkSourceMergeClient.</summary>
public sealed class HttpBulkSourceMergeClient : IBulkSourceMergeClient
{
    private readonly HttpClient _http;

    /// <summary>Construct against the Studio's same-origin HttpClient.</summary>
    public HttpBulkSourceMergeClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        _http = http;
    }

    /// <inheritdoc/>
    public async Task<BulkSourceMergePreviewResponse> PreviewAsync(
        BulkSourceMergePreviewRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var resp = await _http.PostAsJsonAsync("/api/v1/sources/bulk-preview", request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessOrThrowWithBodyAsync(resp, "/bulk-preview", cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadFromJsonAsync<BulkSourceMergePreviewResponse>(cancellationToken).ConfigureAwait(false);
        return body ?? throw new InvalidOperationException("Empty response from /bulk-preview.");
    }

    /// <inheritdoc/>
    public async Task<BulkSourceMergeSubmitResponse> SubmitAsync(
        BulkSourceMergeSubmitRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var resp = await _http.PostAsJsonAsync("/api/v1/sources/bulk-submit", request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessOrThrowWithBodyAsync(resp, "/bulk-submit", cancellationToken).ConfigureAwait(false);
        var body = await resp.Content.ReadFromJsonAsync<BulkSourceMergeSubmitResponse>(cancellationToken).ConfigureAwait(false);
        return body ?? throw new InvalidOperationException("Empty response from /bulk-submit.");
    }

    /// <summary>
    /// Throw an <see cref="HttpRequestException"/> that includes the response body
    /// when the server returns non-success. The default
    /// <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/> drops the body —
    /// useless when the server returned a structured ProblemDetails payload
    /// with the real reason in the detail field.
    /// </summary>
    private static async Task EnsureSuccessOrThrowWithBodyAsync(
        HttpResponseMessage resp,
        string endpointLabel,
        CancellationToken cancellationToken)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = string.Empty;
        try
        {
            body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Body unreadable — fall through with empty body.
        }
        var snippet = string.IsNullOrEmpty(body) ? "(no body)" : Truncate(body, 1024);
        throw new HttpRequestException(
            $"{endpointLabel} returned HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}. Server response body: {snippet}");
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "... (truncated)";
}
