// ============================================================================
// File: FileSystemGatewayIdentity.cs
// Purpose: Default IGatewayIdentity implementation that persists the gateway
//          UUID to disk at HostOptions.GatewayIdentityPath. Created on first
//          start, loaded on every subsequent start. Thread-safe after
//          InitializeAsync completes — reads never block writes because
//          InitializeAsync runs exactly once during the locked
//          LoadGatewayIdentity startup phase.
// Reference: ARCHITECTURE_BLUEPRINT.md §3 (Locked Decision #19)
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Identity;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Host;

/// <summary>
/// File-backed <see cref="IGatewayIdentity"/>. The identity file holds a
/// single line — the gateway UUID — which is generated on first start and
/// loaded on every subsequent start. The path comes from
/// <see cref="HostOptions.GatewayIdentityPath"/>.
/// </summary>
public sealed class FileSystemGatewayIdentity : IGatewayIdentity
{
    private readonly string _path;
    private readonly ILogger? _logger;
    private string? _gatewayId;

    /// <summary>Construct with the configured (primary) identity file path.</summary>
    public FileSystemGatewayIdentity(string path, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string GatewayId
    {
        get
        {
            var id = _gatewayId;
            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidOperationException(
                    "Gateway identity has not been initialized yet. " +
                    "HostStartup must call InitializeAsync during the " +
                    "LoadGatewayIdentity phase before any adapter reads GatewayId.");
            }
            return id;
        }
    }

    /// <summary>
    /// Read the persisted gateway id from <paramref name="path"/> WITHOUT
    /// creating it. Returns <c>null</c> when the file is absent, empty, or
    /// unreadable. Used by the license layer to resolve the expected gateway id
    /// for single-machine binding before the identity service is initialized.
    /// </summary>
    public static string? TryReadPersisted(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        // Resolve via the SAME machine-anchored store the identity service uses,
        // so the license layer sees exactly the id the runtime resolves —
        // checking the HKLM registry + machine-wide file + per-root file
        // (fingerprint-verified), with a legacy plain-id fallback. Read-only:
        // never generates or writes. This is the single shared resolver called
        // for by docs/gateway-identity-per-system-analysis.md §7.
        return GatewayIdentityStore.ForIdentityPath(path).Read();
    }

    /// <summary>
    /// Load the identity from disk, or create it on first start. Safe to
    /// call multiple times — the second call is a no-op. Thread-safety is
    /// achieved by the locked startup sequence: this runs exactly once,
    /// on one thread, before any adapter is constructed.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_gatewayId))
        {
            return;
        }

        // Resolve the id from the machine-anchored store: a machine-wide file
        // (%ProgramData%\Elpis\EdgeConnect\identity), the per-data-root file, and
        // — on Windows — the HKLM registry, each fingerprint-HMAC-tagged. This
        // makes the gateway id STABLE PER MACHINE and independent of the data
        // root, and lets it survive deleting the per-root identity file or
        // clearing C:\ProgramData\EdgeConnect (the registry copy survives). A
        // legacy plain-id file is promoted so already-licensed machines keep
        // their id; only a truly fresh machine mints a new one. The resolved id
        // is re-written to every location (self-healing). Runs off the caller's
        // thread because the store does synchronous file/registry I/O.
        // See docs/gateway-identity-per-system-analysis.md (Option C) + ADR-0038.
        _gatewayId = await Task.Run(
            () =>
            {
                var store = GatewayIdentityStore.ForIdentityPath(_path, _logger);
                var id = store.Read();
                if (string.IsNullOrEmpty(id))
                {
                    id = Guid.NewGuid().ToString("D");
                    _logger?.LogInformation(
                        "No existing gateway identity found on this machine — generating a NEW id '{Id}'. " +
                        "Any license previously issued for a different id will not match (demo mode).",
                        id);
                }
                store.Write(id);
                return id;
            },
            cancellationToken).ConfigureAwait(false);
    }
}
