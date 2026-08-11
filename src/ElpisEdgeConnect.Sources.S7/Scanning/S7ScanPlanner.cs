// ============================================================================
// File: Scanning/S7ScanPlanner.cs
// Purpose: Pure-static builder that converts a list of S7 tag
//          definitions into an executable S7ScanPlan:
//             1. Parse each tag's address text.
//             2. Resolve the datatype spec (explicit hint or derived
//                from the address width).
//             3. Bucket tags by (scanRateMs, area, dbNumber).
//             4. Sort each bucket by byte offset.
//             5. Greedy-pack into contiguous blocks, coalescing gaps up
//                to MaxGapBytes and splitting on MaxReadBytes.
//
//          Single-pass deterministic. Builds in O(n log n) where n is
//          the number of tags. No IO; fully testable in isolation.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone I (mirrors Modbus's
//            ScanPlanner)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace ElpisEdgeConnect.Sources.S7.Scanning;

/// <summary>
/// Builds an <see cref="S7ScanPlan"/> from a list of tag definitions
/// plus the configured planning knobs.
/// </summary>
public static class S7ScanPlanner
{
    /// <summary>
    /// Build the scan plan. Tags are validated, parsed, grouped, sorted,
    /// and greedy-packed.
    /// </summary>
    public static S7ScanPlan Build(
        IReadOnlyList<S7TagDefinition> tags,
        int maxGapBytes,
        int maxReadBytes)
    {
        ArgumentNullException.ThrowIfNull(tags);
        if (maxGapBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxGapBytes), "MaxGapBytes must be non-negative.");
        }
        if (maxReadBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxReadBytes), "MaxReadBytes must be positive.");
        }

        if (tags.Count == 0)
        {
            return new S7ScanPlan(Array.Empty<S7ScanGroup>());
        }

        // 1. Parse + resolve every tag eagerly. We need the typed view
        //    to drive bucketing.
        var resolved = new List<ResolvedTag>(tags.Count);
        foreach (var tag in tags)
        {
            var addr = S7AddressParser.Parse(tag.Address);
            var spec = ResolveDatatype(tag, addr);
            resolved.Add(new ResolvedTag(tag, addr, spec));
        }

        // 2. Bucket by (intervalMs, area, dbNumber).
        var buckets = new Dictionary<(int IntervalMs, S7MemoryArea Area, int Db), List<ResolvedTag>>();
        foreach (var r in resolved)
        {
            var key = (r.Tag.ScanRateMs, r.Address.Area, r.Address.DbNumber);
            if (!buckets.TryGetValue(key, out var list))
            {
                list = new List<ResolvedTag>();
                buckets[key] = list;
            }
            list.Add(r);
        }

        // 3+4+5. Sort + pack each bucket.
        var groups = new List<S7ScanGroup>(buckets.Count);
        foreach (var ((intervalMs, area, db), list) in buckets)
        {
            list.Sort(static (a, b) => a.Address.ByteOffset.CompareTo(b.Address.ByteOffset));
            var blocks = PackBlocks(list, maxGapBytes, maxReadBytes);
            groups.Add(new S7ScanGroup(intervalMs, area, db, blocks));
        }

        // Stable order — easier to reason about in tests + logs.
        groups.Sort(static (a, b) =>
        {
            var c = a.IntervalMs.CompareTo(b.IntervalMs);
            if (c != 0) return c;
            c = ((int)a.Area).CompareTo((int)b.Area);
            if (c != 0) return c;
            return a.DbNumber.CompareTo(b.DbNumber);
        });

        return new S7ScanPlan(groups);
    }

    private static List<S7ScanBlock> PackBlocks(
        List<ResolvedTag> sortedTags,
        int maxGapBytes,
        int maxReadBytes)
    {
        var result = new List<S7ScanBlock>();
        if (sortedTags.Count == 0)
        {
            return result;
        }

        var currentStart = sortedTags[0].Address.ByteOffset;
        var currentEnd = currentStart + sortedTags[0].Spec.ByteCount; // exclusive
        var currentEntries = new List<S7ScanBlockEntry>
        {
            EntryFor(sortedTags[0], currentStart),
        };

        for (var i = 1; i < sortedTags.Count; i++)
        {
            var t = sortedTags[i];
            var tStart = t.Address.ByteOffset;
            var tEnd = tStart + t.Spec.ByteCount;
            var gap = tStart - currentEnd;
            var spanIfAdded = tEnd - currentStart;

            if (gap <= maxGapBytes && spanIfAdded <= maxReadBytes)
            {
                // Coalesce into the current block.
                currentEntries.Add(EntryFor(t, currentStart));
                if (tEnd > currentEnd)
                {
                    currentEnd = tEnd;
                }
            }
            else
            {
                // Seal the current block, start a new one.
                result.Add(new S7ScanBlock(currentStart, currentEnd - currentStart, currentEntries));
                currentStart = tStart;
                currentEnd = tEnd;
                currentEntries = new List<S7ScanBlockEntry>
                {
                    EntryFor(t, currentStart),
                };
            }
        }

        result.Add(new S7ScanBlock(currentStart, currentEnd - currentStart, currentEntries));
        return result;
    }

    private static S7ScanBlockEntry EntryFor(ResolvedTag tag, int blockStart) =>
        new(
            Tag: tag.Tag,
            ParsedAddress: tag.Address,
            Spec: tag.Spec,
            BlockRelativeByteOffset: tag.Address.ByteOffset - blockStart);

    private static S7DatatypeSpec ResolveDatatype(S7TagDefinition tag, S7Address address)
    {
        // Explicit hint takes precedence.
        if (!string.IsNullOrWhiteSpace(tag.Datatype))
        {
            var spec = S7DatatypeParser.Parse(tag.Datatype, defaultSpec: default);
            ValidateAgainstAddress(tag, address, spec);
            return spec;
        }

        // No hint — derive from address width.
        var derived = address.WidthHint switch
        {
            S7AddressWidthHint.Bit => new S7DatatypeSpec(S7Datatype.Bool),
            S7AddressWidthHint.Byte => new S7DatatypeSpec(S7Datatype.Byte),
            S7AddressWidthHint.Word => new S7DatatypeSpec(S7Datatype.Word),
            S7AddressWidthHint.DWord => new S7DatatypeSpec(S7Datatype.DWord),
            _ => throw new ArgumentException(
                $"Tag '{tag.Name}' at address '{tag.Address}' has no datatype hint and no width-bearing address syntax. " +
                "Either add a 'datatype' field or use width-prefixed syntax (DBW/DBD/IW/QW/etc.).",
                nameof(tag)),
        };
        return derived;
    }

    private static void ValidateAgainstAddress(S7TagDefinition tag, S7Address address, S7DatatypeSpec spec)
    {
        // Bit-form addresses (DBX/dot-bit) only make sense with a Bool datatype.
        if (address.WidthHint == S7AddressWidthHint.Bit && spec.Datatype != S7Datatype.Bool)
        {
            throw new ArgumentException(
                $"Tag '{tag.Name}' address '{tag.Address}' is bit-form (X / dot-bit) but datatype is " +
                $"'{spec.Datatype}'. Bit-form addresses must use datatype 'bool'.",
                nameof(tag));
        }

        // Width-prefixed addresses must match the datatype's byte count.
        if (address.WidthHint is S7AddressWidthHint.Byte or S7AddressWidthHint.Word or S7AddressWidthHint.DWord)
        {
            var hintBytes = address.WidthHint switch
            {
                S7AddressWidthHint.Byte => 1,
                S7AddressWidthHint.Word => 2,
                S7AddressWidthHint.DWord => 4,
                _ => 0,
            };
            // Allow strings — they declare their own length and ignore the address width.
            // Allow when the datatype byte count is <= the hint (e.g. SInt at DBB).
            if (spec.Datatype != S7Datatype.String && spec.ByteCount > hintBytes)
            {
                throw new ArgumentException(
                    $"Tag '{tag.Name}' datatype '{spec.Datatype}' ({spec.ByteCount} bytes) does not fit the " +
                    $"address width '{address.WidthHint}' ({hintBytes} bytes). " +
                    "Either widen the address syntax (e.g. DBW→DBD) or pick a smaller datatype.",
                    nameof(tag));
            }
        }
    }

    private readonly record struct ResolvedTag(S7TagDefinition Tag, S7Address Address, S7DatatypeSpec Spec);
}
