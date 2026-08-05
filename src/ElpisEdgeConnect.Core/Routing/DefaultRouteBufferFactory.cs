// ============================================================================
// File: Routing/DefaultRouteBufferFactory.cs
// Purpose: Default IRouteBufferFactory. Constructs SqliteBuffer for
//          StoreAndForward routes and InMemoryBuffer for InMemory routes.
//          BufferMode.None is rejected at the routing engine boundary —
//          a None-mode route never reaches this factory because the
//          AtMostOnce delivery handler bypasses the buffer entirely.
// Reference: ARCHITECTURE_BLUEPRINT.md §6, §19.3, PHASE1_EXECUTION_PLAN.md C3
// ============================================================================

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Buffer;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Core.Routing;

/// <summary>
/// Default <see cref="IRouteBufferFactory"/>. Materializes the right
/// buffer implementation for a given runtime policy and writes
/// <c>StoreAndForward</c> files under <c>{dataPath}/buffer/{routeId}.db</c>
/// per blueprint §19.3.
/// </summary>
public sealed class DefaultRouteBufferFactory : IRouteBufferFactory
{
    private readonly string _dataPath;
    private readonly IRoutingEngineDiagnostics? _diagnostics;

    /// <summary>
    /// Construct a factory rooted at <paramref name="dataPath"/>. The
    /// per-route SQLite files live under <c>{dataPath}/buffer/</c>.
    /// </summary>
    /// <param name="dataPath">Gateway data root.</param>
    /// <param name="diagnostics">
    /// Optional diagnostics seam. When supplied, each StoreAndForward buffer is
    /// wired so a point it cannot serialize is reported as a
    /// <see cref="RoutePointQuarantinedEvent"/> ("quarantine-and-continue"),
    /// making the data-quality problem visible on the route's diagnostics
    /// instead of silently stranding delivery. Null in tests that don't care.
    /// </param>
    public DefaultRouteBufferFactory(string dataPath, IRoutingEngineDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(dataPath);
        _dataPath = dataPath;
        _diagnostics = diagnostics;
    }

    /// <inheritdoc/>
    public async Task<IMessageBuffer> CreateAsync(
        string routeId,
        BufferPolicy policy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(routeId);
        ArgumentNullException.ThrowIfNull(policy);

        switch (policy.Mode)
        {
            case BufferMode.StoreAndForward:
                {
                    var bufferDir = Path.Combine(_dataPath, "buffer");
                    Directory.CreateDirectory(bufferDir);
                    var path = Path.Combine(bufferDir, $"{routeId}.db");

                    // Chip 4 (Bug 1 P3) migration shim: pre-fix deployments
                    // stored buffers at {_dataPath}/config/buffer/{routeId}.db
                    // because CompositionRoot wired the factory with
                    // ConfigDirectory instead of ResolvedDataRoot. On first
                    // open after upgrade, move the .db + .db-shm + .db-wal
                    // triplet to the canonical path so existing store-and-
                    // forward backlog is preserved.
                    MigrateLegacyBufferIfPresent(path, routeId);

                    // Quarantine-and-continue: a point the buffer cannot
                    // serialize is skipped, not fatal. Surface each skip as a
                    // structured route event so the operator sees a loud
                    // data-quality signal instead of silent loss. The callback
                    // is invoked outside the buffer's write lock and the
                    // diagnostics seam is contractually non-blocking.
                    var diagnostics = _diagnostics;
                    Action<QuarantinedPoint>? onQuarantine = diagnostics is null
                        ? null
                        : q => diagnostics.OnRoutePointQuarantined(new RoutePointQuarantinedEvent
                        {
                            RouteId = routeId,
                            TagName = q.TagName,
                            Reason = q.Reason,
                            ObservedAtUtc = DateTime.UtcNow,
                        });

                    return await SqliteBuffer
                        .OpenAsync(routeId, path, policy, utcNow: null, onQuarantine: onQuarantine)
                        .ConfigureAwait(false);
                }

            case BufferMode.InMemory:
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new InMemoryBuffer(routeId, policy);
                }

            case BufferMode.None:
                throw new ArgumentException(
                    $"BufferMode.None is not constructible by IRouteBufferFactory. " +
                    "Routes with BufferMode.None bypass the buffer entirely; the " +
                    "routing engine must not call CreateAsync for them.",
                    nameof(policy));

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(policy),
                    $"Unknown BufferMode: {policy.Mode}");
        }
    }

    /// <summary>
    /// Move a pre-existing buffer triplet from the legacy
    /// <c>{_dataPath}/config/buffer/</c> location to the canonical
    /// <c>{_dataPath}/buffer/</c> location. Idempotent — does nothing
    /// if the canonical path is already populated or if the legacy
    /// path is empty. The .db-shm + .db-wal siblings move with the
    /// .db so SQLite WAL recovery is not corrupted (moving .db alone
    /// while .db-wal stays behind produces an undetectable
    /// inconsistency).
    /// </summary>
    private void MigrateLegacyBufferIfPresent(string canonicalDbPath, string routeId)
    {
        if (File.Exists(canonicalDbPath))
        {
            // Already at canonical path; nothing to migrate. This also
            // protects against accidentally overwriting an in-place buffer
            // with stale legacy data if both paths exist.
            return;
        }

        var legacyDir = Path.Combine(_dataPath, "config", "buffer");
        var legacyDbPath = Path.Combine(legacyDir, $"{routeId}.db");
        if (!File.Exists(legacyDbPath))
        {
            // No legacy file either; new deployment, nothing to migrate.
            return;
        }

        var canonicalDir = Path.GetDirectoryName(canonicalDbPath)!;

        // Move the .db first; then the WAL siblings if they exist.
        File.Move(legacyDbPath, canonicalDbPath);
        foreach (var ext in new[] { ".db-shm", ".db-wal" })
        {
            var legacySibling = Path.Combine(legacyDir, $"{routeId}{ext}");
            if (File.Exists(legacySibling))
            {
                var canonicalSibling = Path.Combine(canonicalDir, $"{routeId}{ext}");
                File.Move(legacySibling, canonicalSibling);
            }
        }

        Console.Error.WriteLine(
            $"[buffer] Migrated route '{routeId}' buffer from legacy path " +
            $"('{legacyDir}') to canonical path ('{canonicalDir}'). " +
            "Chip 4 (Bug 1 P3) — store-and-forward backlog preserved.");
    }
}
