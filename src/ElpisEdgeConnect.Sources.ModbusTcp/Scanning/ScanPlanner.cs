// ============================================================================
// File: Scanning/ScanPlanner.cs
// Purpose: Build an FC-safe ScanPlan from a flat list of ModbusTagDefinitions.
//
//          Algorithm — three-pass, mirrors PHASE3_EXECUTION_PLAN.md §6:
//            1. Bucket tags by (scanRateMs, unitId, registerClass). Each
//               bucket becomes one ScanGroup.
//            2. Within each bucket, sort by address.
//            3. Greedily pack adjacent tags into ScanBlocks, coalescing
//               across gaps ≤ maxGapRegisters while respecting the function
//               code's hard limit (125 regs for FC03/FC04, 2000 bits for
//               FC01/FC02).
//
//          The planner is a pure function: same input → same plan. No I/O,
//          no allocations on the hot path — called once at adapter init and
//          on config apply.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Scanning;

/// <summary>
/// Pure builder that turns a flat list of tag definitions into an FC-safe
/// <see cref="ScanPlan"/>.
/// </summary>
public static class ScanPlanner
{
    /// <summary>Default max gap between tags when coalescing blocks.</summary>
    public const int DefaultMaxGapRegisters = 8;

    /// <summary>
    /// Build a plan from the supplied tag definitions.
    /// </summary>
    /// <param name="tags">All tags configured on the source. May be empty.</param>
    /// <param name="maxGapRegisters">
    /// Maximum tolerated gap between adjacent tags when coalescing into a
    /// single block. Defaults to <see cref="DefaultMaxGapRegisters"/>. Use 0
    /// to disable coalescing (every gap closes the current block).
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when a tag's resolved width exceeds the function code's hard
    /// limit (a single tag too wide to fit in one request), when a tag's
    /// address range overflows the 16-bit Modbus address space, or when a
    /// <c>stringN</c> datatype is malformed — see
    /// <see cref="ModbusTagWidth.Resolve"/>.
    /// </exception>
    public static ScanPlan Build(
        IReadOnlyList<ModbusTagDefinition> tags,
        int maxGapRegisters = DefaultMaxGapRegisters)
    {
        ArgumentNullException.ThrowIfNull(tags);
        if (maxGapRegisters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxGapRegisters),
                maxGapRegisters, "maxGapRegisters must be >= 0 (0 disables coalescing).");
        }

        if (tags.Count == 0)
        {
            return ScanPlan.Empty;
        }

        // Step 1 — bucket by (scanRateMs, unitId, registerClass).
        // Using the tuple directly as the dictionary key keeps the grouping
        // allocation-free and preserves insertion order via KeyValuePair
        // iteration on Dictionary.
        var buckets = new Dictionary<(int scanRateMs, byte unitId, ModbusRegisterClass rc), List<ModbusTagDefinition>>();
        foreach (var tag in tags)
        {
            var key = (tag.ScanRateMs, tag.UnitId, tag.RegisterClass);
            if (!buckets.TryGetValue(key, out var bucket))
            {
                bucket = new List<ModbusTagDefinition>();
                buckets[key] = bucket;
            }
            bucket.Add(tag);
        }

        // Produce groups in a stable order: scanRate asc, then unitId asc,
        // then registerClass asc. This keeps diagnostics output readable and
        // tests deterministic without requiring LINQ on the hot path.
        var orderedKeys = buckets.Keys
            .OrderBy(k => k.scanRateMs)
            .ThenBy(k => k.unitId)
            .ThenBy(k => (int)k.rc)
            .ToList();

        var groups = new List<ScanGroup>(orderedKeys.Count);
        foreach (var key in orderedKeys)
        {
            var bucket = buckets[key];
            var blocks = PackBucket(bucket, key.rc, maxGapRegisters);
            groups.Add(new ScanGroup
            {
                IntervalMs = key.scanRateMs,
                UnitId = key.unitId,
                RegisterClass = key.rc,
                Blocks = blocks,
            });
        }

        return new ScanPlan { Groups = groups };
    }

    // =========================================================================
    // PRIVATE
    // =========================================================================

    private static List<ScanBlock> PackBucket(
        List<ModbusTagDefinition> bucket,
        ModbusRegisterClass rc,
        int maxGapRegisters)
    {
        var maxCount = rc.MaxQuantity();

        // Pre-resolve widths so we don't recompute during the sort comparator
        // or the pack loop. ValidateWidth enforces: width >= 1, width ≤ FC
        // limit, and start+width doesn't overflow the 16-bit address space.
        var items = new List<WidthedTag>(bucket.Count);
        foreach (var tag in bucket)
        {
            var width = ModbusTagWidth.Resolve(tag);
            ValidateRange(tag, width, maxCount);
            items.Add(new WidthedTag(tag, width));
        }

        // Step 2 — sort by address, then by width (wider tags win ties so a
        // 4-reg float that starts at the same address as a 1-reg bitfield
        // enters the block first; the bitfield then overlays with offset 0).
        items.Sort((a, b) =>
        {
            var cmp = a.Tag.Address.CompareTo(b.Tag.Address);
            if (cmp != 0) return cmp;
            return b.Width.CompareTo(a.Width);
        });

        // Step 3 — greedy pack.
        var blocks = new List<ScanBlock>();
        var first = items[0];
        var currentStart = first.Tag.Address;
        var currentCount = first.Width;
        var currentEntries = new List<ScanBlockEntry>
        {
            new() { Tag = first.Tag, Offset = 0, Width = first.Width },
        };

        for (var i = 1; i < items.Count; i++)
        {
            var item = items[i];
            var currentEnd = currentStart + currentCount;

            // gap is 0 for overlapping or adjacent tags, positive for a true
            // gap. newCount is whichever of "current block size" or "new
            // extent" is larger — covers both coalescing and overlap.
            var gap = Math.Max(0, item.Tag.Address - currentEnd);
            var newCount = Math.Max(
                currentCount,
                item.Tag.Address + item.Width - currentStart);

            if (gap <= maxGapRegisters && newCount <= maxCount)
            {
                // Extend current block.
                currentCount = (ushort)newCount;
                currentEntries.Add(new ScanBlockEntry
                {
                    Tag = item.Tag,
                    Offset = (ushort)(item.Tag.Address - currentStart),
                    Width = item.Width,
                });
            }
            else
            {
                // Close current, start fresh block for this tag.
                blocks.Add(new ScanBlock
                {
                    StartAddress = currentStart,
                    Count = currentCount,
                    Entries = currentEntries,
                });
                currentStart = item.Tag.Address;
                currentCount = item.Width;
                currentEntries = new List<ScanBlockEntry>
                {
                    new() { Tag = item.Tag, Offset = 0, Width = item.Width },
                };
            }
        }

        // Flush tail.
        blocks.Add(new ScanBlock
        {
            StartAddress = currentStart,
            Count = currentCount,
            Entries = currentEntries,
        });

        return blocks;
    }

    private static void ValidateRange(ModbusTagDefinition tag, ushort width, int maxCount)
    {
        if (width == 0)
        {
            throw new ArgumentException(
                $"Tag '{tag.Name}' has zero width; datatype '{tag.Datatype}' is not valid.",
                nameof(tag));
        }
        if (width > maxCount)
        {
            throw new ArgumentException(
                $"Tag '{tag.Name}' width {width} exceeds the {tag.RegisterClass} function-code limit " +
                $"of {maxCount} per request. Split the tag into multiple smaller tags or change its datatype.",
                nameof(tag));
        }
        if (tag.Address + width > ushort.MaxValue + 1)
        {
            throw new ArgumentException(
                $"Tag '{tag.Name}' range {tag.Address}+{width} overflows the 16-bit Modbus address space.",
                nameof(tag));
        }
    }

    private readonly record struct WidthedTag(ModbusTagDefinition Tag, ushort Width);
}
