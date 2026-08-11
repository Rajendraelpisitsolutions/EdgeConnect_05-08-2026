// ============================================================================
// File: Buffer/SqliteRouteStoreTestHooks.cs
// Purpose: Constructor-injected, immutable test instrumentation for the K1.2d
//          replay-state capture path on SqliteRouteStore. Threaded through
//          SqliteRouteStore.OpenAsync into the private constructor; production
//          passes null (a single immutable field, no runtime mutation), so there is
//          no shared-mutable-static seam and no cross-test leakage under xUnit
//          parallelism. Tests that inject hooks construct their own store instance.
// Reference: docs/sessions/2026-07-15-sparkplug-b-k1.2d-capture-plan-v3.md §R7;
//            K1.2d kickoff handoff §4 (step 1) / §7 (constructor-injected test hooks).
//
// LOCKED behavior (do not change without revising v3 §R7):
//   - The hooks are IMMUTABLE and constructor-injected (never a mutable property).
//   - CaptureEnteredCriticalSection is SYNCHRONOUS (no await inside the mutex region).
//   - Hook exceptions ESCAPE UNCHANGED — they are never wrapped/translated into a
//     BufferException (they run outside the SqliteException/decode catch scopes).
// ============================================================================

using System;

namespace ElpisEdgeConnect.Core.Buffer;

/// <summary>
/// Which logical capture query GROUP is about to execute, reported to
/// <see cref="SqliteRouteStoreTestHooks.QueryExecuting"/> immediately before that group runs.
/// A group is a logical unit, not a single SQL statement — <see cref="Boundary"/> covers the
/// cutoff + cursor reads (two commands). Lets a test assert, structurally, that the
/// boundary-only capture path never scans the <c>latest_value</c> manifest
/// (<see cref="ManifestScan"/> is never emitted).
/// </summary>
internal enum CaptureQueryKind
{
    /// <summary>The boundary read group: append cutoff (<c>next_sequence</c>) + the sink cursor.</summary>
    Boundary = 0,

    /// <summary>The current-generation <c>latest_value</c> manifest scan (the snapshot read).</summary>
    ManifestScan = 1,
}

/// <summary>
/// Optional, immutable test hooks for the replay-state capture path. Null in production.
/// The primitive constructor supplies both hooks positionally; either may be null.
/// </summary>
/// <param name="CaptureEnteredCriticalSection">
/// Fired synchronously AFTER the capture acquires the writer mutex and BEFORE it opens the
/// read transaction, so a test can deterministically interleave an append / generation
/// advance against the capture. A throwing hook propagates as-is (never translated).
/// </param>
/// <param name="QueryExecuting">
/// Fired once per logical capture query GROUP, immediately before that group runs, tagged with
/// its <see cref="CaptureQueryKind"/>. A group may issue more than one SQL command (e.g.
/// <see cref="CaptureQueryKind.Boundary"/> covers the cutoff + cursor reads); this is a
/// test-only seam, not a per-statement trace. A throwing hook propagates as-is (never translated).
/// </param>
internal sealed record SqliteRouteStoreTestHooks(
    Action? CaptureEnteredCriticalSection = null,
    Action<CaptureQueryKind>? QueryExecuting = null);
