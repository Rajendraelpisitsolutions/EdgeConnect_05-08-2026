// ============================================================================
// File: Scanning/ScanPlannerPropertyTests.cs
// Purpose: Property-based tests for the scan-group planner, enforcing the
//          FC-size-safety invariant called out in PHASE3_EXECUTION_PLAN.md
//          §11 Definition of Done.
//
//          Properties checked (for any randomly generated tag list):
//            1. Every block's Count is within its function code's hard
//               limit (125 regs for FC03/FC04, 2000 bits for FC01/FC02).
//            2. Every input tag appears in exactly one block entry.
//            3. Each block's StartAddress + Count stays within the 16-bit
//               Modbus address space.
//            4. Within each block, every entry's [offset, offset+width)
//               range fits inside [0, block.Count).
//
//          These invariants hold for every maxGapRegisters in [0, 200].
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Sources.ModbusTcp.Scanning;
using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Tests.Scanning;

public sealed class ScanPlannerPropertyTests
{
    /// <summary>
    /// FsCheck arbitrary that produces a list of plausible tag definitions.
    /// Address range is capped well below 65_535 so no randomly generated
    /// tag overflows the 16-bit space, and datatypes are restricted to the
    /// ones the width resolver understands.
    /// </summary>
    public static class Generators
    {
        public static Arbitrary<ModbusTagDefinition[]> TagArray()
        {
            return Arb.From(Gen.ArrayOf(TagGen()));
        }

        private static Gen<ModbusTagDefinition> TagGen()
        {
            var datatypes = new[] { "bool", "int16", "uint16", "int32", "uint32", "float32", "float64", "string8" };
            var classes = new[]
            {
                ModbusRegisterClass.Coil,
                ModbusRegisterClass.DiscreteInput,
                ModbusRegisterClass.HoldingRegister,
                ModbusRegisterClass.InputRegister,
            };
            var scanRates = new[] { 100, 500, 1000, 5000 };

            return from name in Gen.Choose(0, 9999).Select(n => $"t{n}")
                   from rc in Gen.Elements(classes)
                   from addr in Gen.Choose(0, 10_000) // leave plenty of headroom
                   from scanRate in Gen.Elements(scanRates)
                   from unitId in Gen.Choose(1, 5)
                   from datatype in Gen.Elements(datatypes)
                   select new ModbusTagDefinition
                   {
                       Name = name,
                       RegisterClass = rc,
                       Address = (ushort)addr,
                       ScanRateMs = scanRate,
                       UnitId = (byte)unitId,
                       Datatype = datatype,
                   };
        }
    }

    [Property(Arbitrary = [typeof(Generators)], MaxTest = 300)]
    public bool EveryBlockRespectsFcLimit(ModbusTagDefinition[] tags)
    {
        var plan = ScanPlanner.Build(tags);
        foreach (var group in plan.Groups)
        {
            var limit = group.RegisterClass.MaxQuantity();
            foreach (var block in group.Blocks)
            {
                if (block.Count == 0 || block.Count > limit)
                {
                    return false;
                }
            }
        }
        return true;
    }

    [Property(Arbitrary = [typeof(Generators)], MaxTest = 300)]
    public bool EveryInputTagAppearsExactlyOnce(ModbusTagDefinition[] tags)
    {
        var plan = ScanPlanner.Build(tags);
        var emitted = plan.Groups
            .SelectMany(g => g.Blocks)
            .SelectMany(b => b.Entries)
            .Select(e => e.Tag)
            .ToList();

        // Compare by reference: the planner must not synthesize new tag
        // records nor drop any input.
        var inputSet = new HashSet<ModbusTagDefinition>(tags);
        var outputSet = new HashSet<ModbusTagDefinition>(emitted);
        return emitted.Count == tags.Length && inputSet.SetEquals(outputSet);
    }

    [Property(Arbitrary = [typeof(Generators)], MaxTest = 300)]
    public bool EveryBlockStaysInside16BitAddressSpace(ModbusTagDefinition[] tags)
    {
        var plan = ScanPlanner.Build(tags);
        foreach (var group in plan.Groups)
        {
            foreach (var block in group.Blocks)
            {
                if (block.StartAddress + block.Count > ushort.MaxValue + 1)
                {
                    return false;
                }
            }
        }
        return true;
    }

    [Property(Arbitrary = [typeof(Generators)], MaxTest = 300)]
    public bool EveryEntryFitsInsideItsBlock(ModbusTagDefinition[] tags)
    {
        var plan = ScanPlanner.Build(tags);
        foreach (var group in plan.Groups)
        {
            foreach (var block in group.Blocks)
            {
                foreach (var entry in block.Entries)
                {
                    if (entry.Offset + entry.Width > block.Count)
                    {
                        return false;
                    }
                    if (entry.Width == 0)
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    [Property(Arbitrary = [typeof(Generators)], MaxTest = 200)]
    public bool MaxGapRegisters_DoesNotBreakFcSafety(ModbusTagDefinition[] tags, byte maxGap)
    {
        // maxGap is a byte so FsCheck produces 0..255 uniformly.
        // The coalescing threshold should never violate the FC limit
        // regardless of how permissive the gap setting is.
        var plan = ScanPlanner.Build(tags, maxGapRegisters: maxGap);
        foreach (var group in plan.Groups)
        {
            var limit = group.RegisterClass.MaxQuantity();
            foreach (var block in group.Blocks)
            {
                if (block.Count > limit)
                {
                    return false;
                }
            }
        }
        return true;
    }
}
