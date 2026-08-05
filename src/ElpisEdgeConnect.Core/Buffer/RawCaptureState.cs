// ============================================================================
// File: Buffer/RawCaptureState.cs
// Purpose: The deep-copied, off-lock-safe result of a single coherent replay-state
//          raw capture on SqliteRouteStore (K1.2d). Read under the writer mutex inside
//          one deferred read transaction (the first read pins the snapshot); the rows
//          are then decoded OFF the lock. Every field is a deep copy — in particular
//          RawManifestRow.Envelope is an OWNED byte[] read via GetFieldValue<byte[]>,
//          never an alias to a live SQLite buffer — so the state is safe to hand to an
//          off-lock decoder that outlives the transaction.
// Reference: docs/sessions/2026-07-15-sparkplug-b-k1.2d-capture-plan-v3.md §R2
//            (RawManifestRow deep copy) / §R12 step 1; K1.2d kickoff handoff §4.
// ============================================================================

using System.Collections.Generic;

namespace ElpisEdgeConnect.Core.Buffer;

/// <summary>
/// One deep-copied <c>latest_value</c> manifest row: the physical key + type + sequence
/// + generation columns and the value-carrying envelope BLOB. The
/// <see cref="Envelope"/> is an owned copy (never an alias to a live DB read buffer), so
/// the row can be decoded off the writer lock after the capture transaction has ended.
/// </summary>
/// <param name="SourceInstanceId">Source instance id (PK component).</param>
/// <param name="DeviceId">Device id (PK component).</param>
/// <param name="TagPath">Canonical tag path (PK component).</param>
/// <param name="ValueType">The declared canonical datatype (from the <c>value_type</c> column).</param>
/// <param name="RouteBufferSequence">The route-buffer sequence the value was appended at.</param>
/// <param name="SchemaGeneration">The route-schema generation stamped on the row.</param>
/// <param name="Envelope">The value-carrying envelope BLOB (an owned copy).</param>
internal readonly record struct RawManifestRow(
    string SourceInstanceId,
    string DeviceId,
    string TagPath,
    int ValueType,
    long RouteBufferSequence,
    long SchemaGeneration,
    byte[] Envelope);

/// <summary>
/// The coherent raw state captured in one deferred read transaction: the append cutoff,
/// the current route-schema generation, the (optional) sink cursor, and the
/// current-generation manifest rows. The decode/validation into a
/// <c>LatestValueSnapshot</c> happens OFF the lock in a later step from this <b>owned,
/// deep-copied</b> snapshot. (It is owned and detached from SQLite — not deeply immutable:
/// the row list and each <c>Envelope</c> array are mutable; the guarantee is ownership, not
/// immutability.)
/// </summary>
/// <param name="CutoffExclusive">The append cutoff (<c>next_sequence</c>) read FIRST to pin the snapshot.</param>
/// <param name="Generation">The current route-schema generation at capture.</param>
/// <param name="Cursor">The sink's <c>next_unread</c> cursor, or null when the sink has no cursor.</param>
/// <param name="Manifest">The current-generation <c>latest_value</c> rows (deep-copied).</param>
internal readonly record struct RawCaptureState(
    long CutoffExclusive,
    long Generation,
    long? Cursor,
    IReadOnlyList<RawManifestRow> Manifest);
