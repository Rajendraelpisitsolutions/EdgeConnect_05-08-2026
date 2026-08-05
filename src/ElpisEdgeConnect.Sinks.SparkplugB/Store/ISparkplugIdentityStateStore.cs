// ============================================================================
// File: Store/ISparkplugIdentityStateStore.cs
// Purpose: The gateway-scoped, durable Sparkplug identity store (K3 slice 2;
//          plan v3 §5.4-§5.7). Holds the crash-safe bdSeq counter and the
//          batch-atomic Edge-Node alias allocator, keyed by SparkplugStoreIdentity.
//          Injected as a singleton; K4 resolves the gateway data root, constructs
//          the SQLite implementation with an absolute path, and owns its lifetime.
//          An adapter USES the store but does not own or dispose it.
// Reference: docs/sessions/2026-07-19-sparkplug-b-k3-session-actor-plan-v3.md §1.1, §5.
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Sinks.SparkplugB.Identity;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Store;

/// <summary>
/// The durable per-Edge-Node identity state: the bdSeq reservation counter and
/// the stable alias map. All operations are fail-closed — a corrupt/unreadable
/// store or a failed durable commit throws rather than returning a fabricated or
/// reset value.
/// </summary>
public interface ISparkplugIdentityStateStore : IDisposable
{
    /// <summary>
    /// Durably reserve the next bdSeq for <paramref name="identity"/> — committed
    /// BEFORE the value is returned (before CONNECT construction), in a serialized
    /// transaction. The internal reservation counter is strictly monotonic and
    /// never reused (a committed-but-unused value is skipped after restart); the
    /// returned wire value wraps 0..255. Corruption or a commit failure throws.
    /// </summary>
    /// <param name="identity">The Edge Node identity.</param>
    /// <returns>The reserved bdSeq wire value.</returns>
    /// <exception cref="ElpisEdgeConnect.Core.Errors.AdapterException">
    /// Thrown with <see cref="SparkplugErrors.IdentityStoreUnavailable"/> on corruption or commit failure.
    /// </exception>
    SparkplugBirthDeathSequence ReserveNextBdSeq(SparkplugStoreIdentity identity);

    /// <summary>
    /// Resolve the complete alias map for a prospective birth manifest in ONE atomic
    /// transaction: every existing canonical-key → alias mapping is preserved, every
    /// missing key is allocated a new node-unique alias (deterministic ordinal order,
    /// app aliases begin at 1), and the whole set commits or none of it does. The
    /// returned map covers exactly <paramref name="manifest"/>. Persisted mappings are
    /// never deleted or recycled in v1 — they survive route recreation and temporary
    /// absence (the bdSeq metric's own alias is a birth-time concern, not persisted here).
    /// </summary>
    /// <param name="identity">The Edge Node identity.</param>
    /// <param name="manifest">The prospective birth manifest (app metric keys).</param>
    /// <returns>An immutable map from each manifest key to its stable alias.</returns>
    /// <exception cref="ElpisEdgeConnect.Core.Errors.AdapterException">
    /// Thrown with <see cref="SparkplugErrors.AliasAllocationConflict"/> on a uniqueness
    /// violation, or <see cref="SparkplugErrors.IdentityStoreUnavailable"/> on corruption.
    /// </exception>
    IReadOnlyDictionary<SparkplugAliasKey, ulong> ResolveAliases(
        SparkplugStoreIdentity identity, IReadOnlyCollection<SparkplugAliasKey> manifest);
}
