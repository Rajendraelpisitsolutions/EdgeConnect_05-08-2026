// ============================================================================
// File: Scanning/MelsecScanPlanner.cs
// Purpose: Pure scan planner. Turns configured MELSEC tags into deterministic
//          word-unit read blocks + decode mappings, reusing the step-3 parser
//          and device metadata (no duplicated device/radix tables). No transport
//          or adapter coupling.
//
// Rules (ADR-0033 Slice-1):
//   1. Group by scanRateMs first; never coalesce across scan rates.
//   2. Group by device symbol/code; never coalesce D/W/R/ZR/X/Y/... together.
//   3. Word devices D/W/R/ZR: Int16/UInt16 = 1 word; Int32/UInt32/Float32 = 2
//      words; a word-bit (D100.3) reads its containing word (1 word).
//   4. Bit devices M/X/Y/B: read via word-unit (0401/0000); 1 returned word =
//      16 consecutive bit-device points from the block head; mapping records the
//      bit offset from the block head.
//   5. Bit-device ranges never coalesce with word/word-bit ranges (guaranteed by
//      per-symbol grouping — a word-bit like D100.3 is a D (word) tag).
//   6. MaxGapWords is measured in returned 16-bit words for BOTH kinds (for bit
//      devices 1 gap word = 16 bit-device points).
//   7. No block exceeds the 960 returned-word hard cap; oversized demand splits.
//   8. Tags on the same word share one block, differing only in decode mapping.
//   9. Ordering is stable (deterministic blocks + entries).
//  10. Unplannable tags produce typed errors, never generic exceptions.
//
// FIELD-VERIFICATION CAVEAT (Part B): bit-device word-unit reads here use the
// block's minimum point as the head (e.g. M100 -> head M100, 16 pts/word). Some
// MELSEC CPUs require the word-unit head of a bit device to be a multiple of 16;
// this is deferred to the customer Part B capture and may add 16-alignment later.
// Reference: docs/decisions/0033-melsec-slmp-handrolled-slice1-scope.md
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Sources.Melsec.Wire;

namespace ElpisEdgeConnect.Sources.Melsec.Scanning;

/// <summary>Builds a deterministic <see cref="MelsecScanPlan"/> from configured tags.</summary>
public static class MelsecScanPlanner
{
    /// <summary>Error code: datatype hint could not be parsed.</summary>
    public const string InvalidDatatype = "MELSEC.CONFIG_INVALID_DATATYPE";

    /// <summary>Error code: datatype is incoherent with the address (e.g. Bool on a plain word device).</summary>
    public const string DatatypeMismatch = "MELSEC.CONFIG_DATATYPE_MISMATCH";

    /// <summary>Error code: scan rate must be positive.</summary>
    public const string InvalidScanRate = "MELSEC.CONFIG_INVALID_SCANRATE";

    /// <summary>Hard cap on returned words per block (modern 3E-binary CPUs).</summary>
    public const int HardWordCap = SlmpFrameCodec.MaxWordPoints; // 960

    private const int BitsPerWord = 16;

    private sealed record ResolvedTag(MelsecTagDefinition Tag, MelsecAddress Address, MelsecDatatype Datatype, int Start, int WidthWords);

    /// <summary>
    /// Plan <paramref name="tags"/> into read blocks with the shipped (Modern)
    /// profile. <paramref name="maxGapWords"/> bounds gap coalescing (in returned
    /// words); <paramref name="maxPointsPerRequest"/> is clamped to <see cref="HardWordCap"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="maxGapWords"/> is negative.</exception>
    public static MelsecScanPlan Build(IReadOnlyList<MelsecTagDefinition> tags, int maxGapWords, int maxPointsPerRequest) =>
        Build(tags, maxGapWords, maxPointsPerRequest, Profiles.MelsecProfiles.Modern);

    /// <summary>
    /// Plan <paramref name="tags"/> into read blocks against a specific family
    /// profile (A-2O): the profile supplies the device set, per-device radix
    /// (e.g. iQ-F octal X/Y operator labels), and its own word cap. The wire
    /// <see cref="HardWordCap"/> still applies as the outer bound.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="maxGapWords"/> is negative.</exception>
    public static MelsecScanPlan Build(
        IReadOnlyList<MelsecTagDefinition> tags,
        int maxGapWords,
        int maxPointsPerRequest,
        Profiles.MelsecProfileDefinition profile)
    {
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(profile);
        if (maxGapWords < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxGapWords), "MaxGapWords must be >= 0.");
        }

        var wireCap = Math.Min(HardWordCap, profile.MaxWordsPerBatchRead);
        var cap = maxPointsPerRequest <= 0 ? wireCap : Math.Min(maxPointsPerRequest, wireCap);

        var errors = new List<MelsecPlanningError>();
        var resolved = new List<ResolvedTag>();

        foreach (var tag in tags)
        {
            if (!MelsecAddressParser.TryParse(tag.Address, profile, out var address, out var addressError))
            {
                errors.Add(new MelsecPlanningError(tag.Name, addressError.Code, addressError.Message));
                continue;
            }
            if (tag.ScanRateMs <= 0)
            {
                errors.Add(new MelsecPlanningError(tag.Name, InvalidScanRate, $"scanRateMs must be > 0 (got {tag.ScanRateMs})"));
                continue;
            }
            if (!TryResolveDatatype(tag, address, out var datatype, out var code, out var message))
            {
                errors.Add(new MelsecPlanningError(tag.Name, code, message));
                continue;
            }
            resolved.Add(new ResolvedTag(tag, address, datatype, address.DeviceNumber, WidthWords(datatype)));
        }

        var blocks = new List<MelsecScanBlock>();
        var groups = resolved
            .GroupBy(r => (r.Tag.ScanRateMs, r.Address.Device.Symbol))
            .OrderBy(g => g.Key.ScanRateMs)
            .ThenBy(g => g.Key.Symbol, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var device = group.First().Address.Device;
            blocks.AddRange(device.Kind == MelsecDeviceKind.Word
                ? PlanWordDevice(group, device, cap, maxGapWords)
                : PlanBitDevice(group, device, cap, maxGapWords));
        }

        return new MelsecScanPlan { Blocks = blocks, Errors = errors };
    }

    private static bool TryResolveDatatype(
        MelsecTagDefinition tag, MelsecAddress address, out MelsecDatatype datatype, out string code, out string message)
    {
        code = string.Empty;
        message = string.Empty;

        if (!string.IsNullOrWhiteSpace(tag.Datatype))
        {
            if (!MelsecDatatypeParser.TryParse(tag.Datatype, out datatype, out var parseError))
            {
                code = InvalidDatatype;
                message = $"tag '{tag.Name}': {parseError}";
                return false;
            }
        }
        else
        {
            datatype = address.ResolvesToBool ? MelsecDatatype.Bool : MelsecDatatype.Int16;
        }

        if (datatype == MelsecDatatype.Bool && !address.ResolvesToBool)
        {
            code = DatatypeMismatch;
            message = $"tag '{tag.Name}': Bool requires a word-bit (e.g. D100.3) or a bit device, not '{address}'";
            return false;
        }
        if (datatype != MelsecDatatype.Bool && address.ResolvesToBool)
        {
            code = DatatypeMismatch;
            message = $"tag '{tag.Name}': a bit/word-bit address ('{address}') requires a Bool datatype, not {datatype}";
            return false;
        }
        // A-3b: timer/counter current values (TN/STN/CN) are single-word — no
        // 32-bit datatypes (2-word current values are the excluded long family).
        if (address.Device.SingleWordOnly
            && datatype is MelsecDatatype.Int32 or MelsecDatatype.UInt32 or MelsecDatatype.Float32)
        {
            code = DatatypeMismatch;
            message = $"tag '{tag.Name}': {address.Device.Symbol} is a single-word current value — use Int16 or UInt16, not {datatype}";
            return false;
        }

        return true;
    }

    private static int WidthWords(MelsecDatatype datatype) => datatype switch
    {
        MelsecDatatype.Bool or MelsecDatatype.Int16 or MelsecDatatype.UInt16 => 1,
        MelsecDatatype.Int32 or MelsecDatatype.UInt32 or MelsecDatatype.Float32 => 2,
        _ => 1,
    };

    private static List<MelsecScanBlock> PlanWordDevice(
        IEnumerable<ResolvedTag> group, MelsecDeviceDescriptor device, int cap, int maxGapWords)
    {
        var ordered = group
            .OrderBy(r => r.Start)
            .ThenBy(r => r.Tag.Name, StringComparer.Ordinal)
            .ToList();

        var blocks = new List<MelsecScanBlock>();
        var entries = new List<ResolvedTag>();
        int head = -1, end = -1;

        foreach (var r in ordered)
        {
            int start = r.Start;
            int stop = r.Start + r.WidthWords; // exclusive

            if (head < 0)
            {
                head = start; end = stop; entries.Add(r);
                continue;
            }

            int gap = start - end;                 // <= 0 means touching/overlapping
            int projectedEnd = Math.Max(end, stop);
            int projectedCount = projectedEnd - head;

            if (gap <= maxGapWords && projectedCount <= cap)
            {
                end = projectedEnd;
                entries.Add(r);
            }
            else
            {
                blocks.Add(BuildWordBlock(device, head, end, entries));
                head = start; end = stop; entries = new List<ResolvedTag> { r };
            }
        }

        if (head >= 0)
        {
            blocks.Add(BuildWordBlock(device, head, end, entries));
        }
        return blocks;
    }

    private static List<MelsecScanBlock> PlanBitDevice(
        IEnumerable<ResolvedTag> group, MelsecDeviceDescriptor device, int cap, int maxGapWords)
    {
        var ordered = group
            .OrderBy(r => r.Start)
            .ThenBy(r => r.Tag.Name, StringComparer.Ordinal)
            .ToList();

        var blocks = new List<MelsecScanBlock>();
        var entries = new List<ResolvedTag>();
        int head = -1, max = -1;

        foreach (var r in ordered)
        {
            int point = r.Start;

            if (head < 0)
            {
                head = point; max = point; entries.Add(r);
                continue;
            }

            int wordOfMax = (max - head) / BitsPerWord;
            int wordOfPoint = (point - head) / BitsPerWord;
            int gapWords = Math.Max(0, wordOfPoint - wordOfMax - 1);
            int projectedMax = Math.Max(max, point);
            int projectedWords = ((projectedMax - head) / BitsPerWord) + 1;

            if (gapWords <= maxGapWords && projectedWords <= cap)
            {
                max = projectedMax;
                entries.Add(r);
            }
            else
            {
                blocks.Add(BuildBitBlock(device, head, max, entries));
                head = point; max = point; entries = new List<ResolvedTag> { r };
            }
        }

        if (head >= 0)
        {
            blocks.Add(BuildBitBlock(device, head, max, entries));
        }
        return blocks;
    }

    private static MelsecScanBlock BuildWordBlock(MelsecDeviceDescriptor device, int head, int end, List<ResolvedTag> entries)
    {
        var mapped = entries
            .Select(r => new MelsecScanBlockEntry
            {
                Tag = r.Tag,
                Address = r.Address,
                Datatype = r.Datatype,
                ByteOffset = (r.Start - head) * 2,
                BitIndex = r.Address.BitIndex, // set only for word-bit (Bool); null otherwise
            });
        return AssembleBlock(device, head, end - head, entries[0].Tag.ScanRateMs, mapped);
    }

    private static MelsecScanBlock BuildBitBlock(MelsecDeviceDescriptor device, int head, int max, List<ResolvedTag> entries)
    {
        var wordCount = ((max - head) / BitsPerWord) + 1;
        var mapped = entries
            .Select(r =>
            {
                int offsetFromHead = r.Start - head;
                return new MelsecScanBlockEntry
                {
                    Tag = r.Tag,
                    Address = r.Address,
                    Datatype = r.Datatype,
                    ByteOffset = (offsetFromHead / BitsPerWord) * 2,
                    BitIndex = offsetFromHead % BitsPerWord,
                };
            });
        return AssembleBlock(device, head, wordCount, entries[0].Tag.ScanRateMs, mapped);
    }

    private static MelsecScanBlock AssembleBlock(
        MelsecDeviceDescriptor device, int head, int wordCount, int scanRateMs, IEnumerable<MelsecScanBlockEntry> mapped)
    {
        var ordered = mapped
            .OrderBy(e => e.ByteOffset)
            .ThenBy(e => e.BitIndex ?? -1)
            .ThenBy(e => e.TagName, StringComparer.Ordinal)
            .ToList();

        return new MelsecScanBlock
        {
            DeviceCode = device.Code,
            DeviceSymbol = device.Symbol,
            HeadDeviceNumber = head,
            WordCount = wordCount,
            ScanRateMs = scanRateMs,
            Entries = ordered,
        };
    }
}
