// ============================================================================
// File: Configuration/ConfigurationAuditLogTests.cs
// Covers: Append/read round trip, hash chain integrity, tamper detection.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Errors;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Configuration;

public sealed class ConfigurationAuditLogTests
{
    private static async Task<List<ConfigurationAuditEntry>> ReadAllAsync(
        ConfigurationAuditLog log, bool verifyChain)
    {
        var entries = new List<ConfigurationAuditEntry>();
        await foreach (var e in log.ReadAllAsync(verifyChain, CancellationToken.None))
        {
            entries.Add(e);
        }
        return entries;
    }

    private static ConfigurationAuditEntry MakeEntry(string previousHash, string actor = "test", int seq = 1) =>
        new()
        {
            Timestamp = new DateTime(2026, 4, 8, 10, 30, seq, DateTimeKind.Utc),
            VersionId = new ConfigurationVersionId($"v{seq}"),
            PreviousVersionId = null,
            Action = ConfigurationAuditAction.Applied,
            Actor = actor,
            Summary = $"entry {seq}",
            Changes = [],
            PreviousHash = previousHash,
        };

    [Fact]
    public async Task Append_OneEntry_RoundTrips()
    {
        var store = new InMemoryConfigurationStore();
        var log = new ConfigurationAuditLog(store);

        var prevHash = await log.ComputeNextPreviousHashAsync(CancellationToken.None);
        prevHash.Should().Be(ConfigurationAuditLog.GenesisHash);

        var entry = MakeEntry(prevHash);
        await log.AppendAsync(entry, CancellationToken.None);

        var entries = await ReadAllAsync(log, verifyChain: true);
        entries.Should().HaveCount(1);
        entries[0].Actor.Should().Be("test");
        entries[0].VersionId.Value.Should().Be("v1");
    }

    [Fact]
    public async Task ComputeNextPreviousHash_EmptyLog_ReturnsGenesis()
    {
        var store = new InMemoryConfigurationStore();
        var log = new ConfigurationAuditLog(store);

        var hash = await log.ComputeNextPreviousHashAsync(CancellationToken.None);

        hash.Should().Be(ConfigurationAuditLog.GenesisHash);
    }

    [Fact]
    public async Task Append_MultipleEntries_HashChainCorrect()
    {
        var store = new InMemoryConfigurationStore();
        var log = new ConfigurationAuditLog(store);

        for (var i = 1; i <= 5; i++)
        {
            var prevHash = await log.ComputeNextPreviousHashAsync(CancellationToken.None);
            await log.AppendAsync(MakeEntry(prevHash, seq: i), CancellationToken.None);
        }

        var entries = await ReadAllAsync(log, verifyChain: true);
        entries.Should().HaveCount(5);
    }

    [Fact]
    public async Task ReadAll_TamperedEntry_ThrowsConfigAuditCorrupt()
    {
        var store = new InMemoryConfigurationStore();
        var log = new ConfigurationAuditLog(store);

        for (var i = 1; i <= 3; i++)
        {
            var prevHash = await log.ComputeNextPreviousHashAsync(CancellationToken.None);
            await log.AppendAsync(MakeEntry(prevHash, seq: i), CancellationToken.None);
        }

        // Replace the middle line with garbage that has the right shape
        // (so JSON parsing succeeds) but the wrong content (so the chain
        // is broken at the next entry).
        var line = store.GetAuditLine(1);
        store.CorruptAuditLine(1, line.Replace("\"v2\"", "\"vTAMPERED\""));

        var act = async () => await ReadAllAsync(log, verifyChain: true);

        await act.Should().ThrowAsync<ConfigurationException>()
            .Where(ex => ex.Error.Code == CoreErrors.ConfigAuditCorrupt);
    }

    [Fact]
    public async Task ReadAll_VerifyChainFalse_SkipsIntegrityCheck()
    {
        var store = new InMemoryConfigurationStore();
        var log = new ConfigurationAuditLog(store);

        for (var i = 1; i <= 3; i++)
        {
            var prevHash = await log.ComputeNextPreviousHashAsync(CancellationToken.None);
            await log.AppendAsync(MakeEntry(prevHash, seq: i), CancellationToken.None);
        }

        // Tamper but read without verification — must succeed.
        var line = store.GetAuditLine(1);
        store.CorruptAuditLine(1, line.Replace("\"v2\"", "\"vTAMPERED\""));

        var entries = await ReadAllAsync(log, verifyChain: false);

        entries.Should().HaveCount(3);
    }

    [Fact]
    public async Task ReadAll_MalformedLine_ThrowsConfigAuditCorrupt()
    {
        var store = new InMemoryConfigurationStore();
        var log = new ConfigurationAuditLog(store);
        await log.AppendAsync(MakeEntry(ConfigurationAuditLog.GenesisHash), CancellationToken.None);

        store.CorruptAuditLine(0, "not valid json at all");

        var act = async () => await ReadAllAsync(log, verifyChain: false);

        await act.Should().ThrowAsync<ConfigurationException>()
            .Where(ex => ex.Error.Code == CoreErrors.ConfigAuditCorrupt);
    }

    [Fact]
    public void ComputeHash_ProducesSha256HexLowercase()
    {
        var hash = ConfigurationAuditLog.ComputeHash("hello");

        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void ComputeHash_NullInput_Throws()
    {
        var act = () => ConfigurationAuditLog.ComputeHash(null!);
        act.Should().Throw<System.ArgumentNullException>();
    }

    [Fact]
    public async Task Append_NullEntry_Throws()
    {
        var log = new ConfigurationAuditLog(new InMemoryConfigurationStore());
        var act = async () => await log.AppendAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<System.ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullStore_Throws()
    {
        var act = () => new ConfigurationAuditLog(null!);
        act.Should().Throw<System.ArgumentNullException>();
    }
}
