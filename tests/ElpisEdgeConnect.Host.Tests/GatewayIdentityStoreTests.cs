// ============================================================================
// File: GatewayIdentityStoreTests.cs
// Purpose: Pin the machine-anchored gateway-identity behaviour that makes the
//          gateway id STABLE PER MACHINE (docs/gateway-identity-per-system-
//          analysis.md, Option C; ADR-0038). Uses the store's explicit
//          constructor with temp file slots + a test fingerprint and
//          useRegistry:false, so tests are deterministic and never touch HKLM.
//
//          Covered:
//            * fresh (no records) -> Read returns null
//            * Write then Read -> same id (stable)
//            * survives deleting one slot -> recovered from another
//            * legacy plain-id file -> promoted (existing licensed machines)
//            * record from a DIFFERENT machine fingerprint -> rejected
//              (single-machine binding / transplant protection, ADR-0036)
//            * tampered tag -> rejected
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using ElpisEdgeConnect.Host;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Host.Tests;

public sealed class GatewayIdentityStoreTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    private string NewTempPath()
    {
        var p = Path.Combine(Path.GetTempPath(), "ec-gwid-" + Guid.NewGuid().ToString("N") + ".id");
        _tempFiles.Add(p);
        return p;
    }

    private static GatewayIdentityStore Store(string fingerprint, params string[] paths) =>
        new(paths, fingerprint, useRegistry: false, logger: null);

    [Fact]
    public void Read_WhenNoRecordsExist_ReturnsNull()
    {
        var store = Store("fp-A", NewTempPath(), NewTempPath());

        store.Read().Should().BeNull();
    }

    [Fact]
    public void WriteThenRead_ReturnsTheSameId()
    {
        var file = NewTempPath();
        var store = Store("fp-A", file);

        store.Write("11112222-3333-4444-5555-666677778888");

        store.Read().Should().Be("11112222-3333-4444-5555-666677778888");
    }

    [Fact]
    public void Read_AfterDeletingTheFirstSlot_RecoversFromAnotherSlot()
    {
        // The whole point: deleting the per-data-root copy (or clearing a
        // ProgramData folder) must NOT lose the id — another anchored slot holds it.
        var perRoot = NewTempPath();
        var machineWide = NewTempPath();
        var store = Store("fp-A", machineWide, perRoot);
        store.Write("aaaa1111-bbbb-2222-cccc-3333dddd4444");

        File.Delete(perRoot);
        File.Exists(machineWide).Should().BeTrue("the second slot still holds the record");

        store.Read().Should().Be("aaaa1111-bbbb-2222-cccc-3333dddd4444");
    }

    [Fact]
    public void Read_PromotesALegacyPlainIdFile()
    {
        // Pre-upgrade builds wrote just the raw GUID (no fingerprint tag).
        // Adopting it keeps already-licensed machines on their existing id.
        var file = NewTempPath();
        File.WriteAllText(file, "30bf7c3e-1084-42c7-9ea4-1af041de4eb9");

        var store = Store("fp-A", file);

        store.Read().Should().Be("30bf7c3e-1084-42c7-9ea4-1af041de4eb9");
    }

    [Fact]
    public void Read_RejectsATaggedRecordFromADifferentMachine()
    {
        // A record written on machine A must not validate on machine B — this
        // is what preserves single-machine license binding (ADR-0036).
        var file = NewTempPath();
        Store("fingerprint-machine-A", file).Write("secret-id-9999");

        var onMachineB = Store("fingerprint-machine-B", file);

        onMachineB.Read().Should().BeNull(
            "the fingerprint-bound HMAC must reject a record copied from another machine");
    }

    [Fact]
    public void Read_RejectsATamperedRecord()
    {
        var file = NewTempPath();
        Store("fp-A", file).Write("55556666-7777-8888-9999-000011112222");

        // Corrupt the record so the id no longer matches its tag.
        var record = File.ReadAllText(file);
        File.WriteAllText(file, record.Replace("5555", "9999", StringComparison.Ordinal));

        Store("fp-A", file).Read().Should().BeNull(
            "a hand-edited id whose HMAC tag no longer matches must be rejected");
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try { if (File.Exists(f)) { File.Delete(f); } }
            catch (IOException) { /* best effort */ }
        }
    }
}
