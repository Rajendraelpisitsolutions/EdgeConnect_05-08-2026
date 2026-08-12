// ============================================================================
// File: Buffer/BufferPolicy.cs
// Purpose: Runtime policy record describing how a buffer instance behaves at
//          steady state. Distinct from BufferPolicyConfig (the JSON DTO from
//          B1) — this is the validated, materialized form the runtime works
//          with, and is what InMemoryBuffer / SqliteBuffer (C2b) accept.
// Reference: ARCHITECTURE_BLUEPRINT.md §6, PHASE1_EXECUTION_PLAN.md C2a
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Core.Buffer;

/// <summary>
/// Runtime buffer policy. Built from <see cref="BufferPolicyConfig"/> at
/// route activation time and handed to the buffer factory.
/// </summary>
/// <remarks>
/// This is intentionally distinct from <see cref="BufferPolicyConfig"/>:
/// the config record is the JSON DTO that lives in user-edited files;
/// this record is the validated, materialized policy the runtime consumes.
/// Tests, code, and docs should not confuse the two layers — the DTO is
/// optional/forgiving, the runtime policy is strict and final.
/// </remarks>
public sealed record BufferPolicy
{
    /// <summary>Buffering mode (None / InMemory / StoreAndForward).</summary>
    public required BufferMode Mode { get; init; }

    /// <summary>
    /// Maximum number of points held before <see cref="DropPolicy"/> applies.
    /// Must be strictly positive for in-memory buffers.
    /// </summary>
    public required int MaxDepth { get; init; }

    /// <summary>Behavior when the buffer reaches <see cref="MaxDepth"/>.</summary>
    public required DropPolicy DropPolicy { get; init; }

    /// <summary>
    /// Maximum age of a buffered point. <c>null</c> (default) means no age
    /// cap. When set on the durable SQLite buffer (C2b), the retention
    /// sweep deletes rows older than this age even if some sinks have not
    /// yet acked them — a deliberate unread-data loss policy counted under
    /// <see cref="BufferStats.DroppedByRetention"/>. Operators must opt in
    /// explicitly. The in-memory buffer ignores this field.
    /// </summary>
    public TimeSpan? MaxAge { get; init; }

    /// <summary>
    /// Approximate maximum on-disk byte ceiling. Reserved for future
    /// retention strategies; C2a and C2b do not enforce this.
    /// </summary>
    public long? MaxBytes { get; init; }

    /// <summary>
    /// Whether the persistent buffer compresses payloads. Ignored by the
    /// in-memory buffer.
    /// </summary>
    public bool CompressionEnabled { get; init; }

    /// <summary>
    /// Maximum points committed in a single SQLite transaction by the
    /// durable buffer. Producer batches larger than this are split into
    /// consecutive transactions to bound transaction duration and WAL
    /// growth. Default 10,000. Ignored by the in-memory buffer.
    /// </summary>
    public int MaxBatchSize { get; init; } = 10_000;

    /// <summary>
    /// Periodic reclaim tick for the durable buffer. Default 5 seconds.
    /// Ack handlers signal the reclaim loop opportunistically; this value
    /// bounds the maximum delay between an ack and the corresponding row
    /// deletion. Ignored by the in-memory buffer.
    /// </summary>
    public TimeSpan ReclaimInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Map a B1 <see cref="BufferPolicyConfig"/> DTO to a runtime policy.
    /// </summary>
    public static BufferPolicy FromConfig(BufferPolicyConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new BufferPolicy
        {
            Mode = config.Mode,
            MaxDepth = config.MaxDepth > 0 ? config.MaxDepth : 10_000,
            DropPolicy = config.OnOverflow,
            MaxAge = config.MaxAgeDays > 0 ? TimeSpan.FromDays(config.MaxAgeDays) : null,
        };
    }
}
