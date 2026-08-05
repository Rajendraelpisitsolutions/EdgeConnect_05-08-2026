// ============================================================================
// File: Parity/BrotherHttpTestServer.cs
// Purpose: Test-only HttpListener-backed test server that serves canned
//          Brother HTTP responses from a Samples/{scenario}/ directory.
//          Both the legacy BrotherHttpDataSource (oracle) and the new
//          BrotherHttpHttpApi point at this server so the parity test
//          consumes identical bytes on both sides (v3 §7 lock).
// Reference: docs/sessions/2026-05-21-mp24-brother-http-plan-v3.md §7
// ============================================================================

using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace ElpisEdgeConnect.Sources.BrotherHttp.Tests.Parity;

/// <summary>
/// Minimal HTTP listener that serves Brother HTTP endpoint responses from
/// files in a per-scenario samples directory. Each request path maps to
/// a file (e.g. <c>/HTTPD_MCNINFO</c> → <c>{samplesDir}/HTTPD_MCNINFO.txt</c>).
/// </summary>
internal sealed class BrotherHttpTestServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly string _samplesDir;
    private readonly Task _acceptLoop;
    private readonly CancellationTokenSource _stopCts = new();

    public string BaseUrl { get; }

    public BrotherHttpTestServer(string samplesDir)
    {
        if (string.IsNullOrWhiteSpace(samplesDir))
            throw new ArgumentException("samplesDir is required", nameof(samplesDir));
        if (!Directory.Exists(samplesDir))
            throw new DirectoryNotFoundException($"Samples directory not found: {samplesDir}");
        _samplesDir = samplesDir;

        var (listener, port) = StartOnFreePort();
        _listener = listener;
        BaseUrl = $"http://localhost:{port}";

        _acceptLoop = Task.Run(() => AcceptLoopAsync(_stopCts.Token));
    }

    private static (HttpListener Listener, int Port) StartOnFreePort()
    {
        var rng = new Random();
        for (var i = 0; i < 30; i++)
        {
            var port = rng.Next(20000, 60000);
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            try
            {
                listener.Start();
                return (listener, port);
            }
            catch (HttpListenerException)
            {
                listener.Close();
                continue;   // port in use — pick another
            }
        }
        throw new InvalidOperationException("Could not bind to a free localhost port after 30 attempts.");
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException) { return; }
            catch (HttpListenerException) { return; }

            try
            {
                var path = context.Request.Url?.AbsolutePath?.TrimStart('/') ?? string.Empty;
                var filePath = Path.Combine(_samplesDir, $"{path}.txt");

                if (!File.Exists(filePath))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                }
                else
                {
                    var body = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                    context.Response.ContentLength64 = body.Length;
                    context.Response.ContentType = "text/plain";
                    await context.Response.OutputStream.WriteAsync(body, ct).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                try { context.Response.StatusCode = (int)HttpStatusCode.InternalServerError; }
                catch { /* ignore */ }
            }
            finally
            {
                try { context.Response.Close(); } catch { /* ignore */ }
            }
        }
    }

    public void Dispose()
    {
        _stopCts.Cancel();
        try { _listener.Stop(); } catch { /* ignore */ }
        try { _listener.Close(); } catch { /* ignore */ }
        try { _acceptLoop.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        _stopCts.Dispose();
    }
}
