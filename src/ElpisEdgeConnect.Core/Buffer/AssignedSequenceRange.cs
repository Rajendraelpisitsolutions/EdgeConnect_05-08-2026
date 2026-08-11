// ============================================================================
// File: Buffer/AssignedSequenceRange.cs
// Purpose: The contiguous route-buffer sequence range a tracked append assigned.
//          Returned by SqliteRouteStore.AppendAsync so a (future, K1.3) route path
//          knows exactly which sequences its batch landed at, without a second read.
// Reference: docs/sessions/2026-07-14-sparkplug-b-k1.2-route-store-plan-v3.md §6 (M2),
//            §10 (append path); K1.2c handoff §3 item 2.
// ============================================================================

namespace ElpisEdgeConnect.Core.Buffer;

/// <summary>
/// The contiguous, inclusive range of route-buffer sequences a tracked append assigned.
/// For an empty append <see cref="Count"/> is 0 and the bounds both equal the unchanged
/// head (no sequence was consumed).
/// </summary>
/// <param name="FirstSequence">The first sequence assigned (== the head before the append).</param>
/// <param name="LastSequence">The last sequence assigned (== <see cref="FirstSequence"/> + <see cref="Count"/> - 1 when non-empty; == <see cref="FirstSequence"/> when empty).</param>
/// <param name="Count">The number of points appended.</param>
internal readonly record struct AssignedSequenceRange(long FirstSequence, long LastSequence, int Count);
