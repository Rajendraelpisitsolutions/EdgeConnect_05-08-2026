// ============================================================================
// File: Configuration/HistoryRetentionPolicyTests.cs
// Covers: HistoryRetentionPolicy bounds and the manager's enforcement of it
//         after each apply.
// ============================================================================

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Configuration;

public sealed class HistoryRetentionPolicyTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void Default_KeepsTwentyVersions()
    {
        HistoryRetentionPolicy.Default.MaxRetained.Should().Be(20);
    }

    [Fact]
    public void Constructor_NegativeMax_Throws()
    {
        var act = () => new HistoryRetentionPolicy(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_NegativeOne_Throws()
    {
        var act = () => new HistoryRetentionPolicy(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_LargeMax_Accepted()
    {
        var policy = new HistoryRetentionPolicy(1_000_000);
        policy.MaxRetained.Should().Be(1_000_000);
    }

    [Fact]
    public async Task Manager_AppliesBeyondMax_PrunesOldest()
    {
        var store = new InMemoryConfigurationStore();
        await store.WriteCurrentAsync(
            JsonSerializer.Serialize(B2TestFixtures.ValidMinimal(), JsonOptions),
            CancellationToken.None);

        var manager = new ConfigurationManager(
            store,
            retentionPolicy: new HistoryRetentionPolicy(maxRetained: 3));
        await manager.InitializeAsync(CancellationToken.None);

        // Apply 5 times so 5 history files are created. With max=3, only the
        // 3 most recent should remain.
        for (var i = 0; i < 5; i++)
        {
            var draftId = await manager.CreateDraftAsync(
                B2TestFixtures.ValidWithMultiple(),
                null,
                CancellationToken.None);
            var result = await manager.ApplyDraftAsync(draftId, null, CancellationToken.None);
            result.Success.Should().BeTrue();
        }

        var history = await store.ListHistoryAsync(CancellationToken.None);
        history.Count.Should().Be(3);
    }

    [Fact]
    public async Task Manager_AppliesUnderMax_KeepsAll()
    {
        var store = new InMemoryConfigurationStore();
        await store.WriteCurrentAsync(
            JsonSerializer.Serialize(B2TestFixtures.ValidMinimal(), JsonOptions),
            CancellationToken.None);

        var manager = new ConfigurationManager(
            store,
            retentionPolicy: new HistoryRetentionPolicy(maxRetained: 10));
        await manager.InitializeAsync(CancellationToken.None);

        for (var i = 0; i < 3; i++)
        {
            var draftId = await manager.CreateDraftAsync(B2TestFixtures.ValidWithMultiple(), null, CancellationToken.None);
            await manager.ApplyDraftAsync(draftId, null, CancellationToken.None);
        }

        var history = await store.ListHistoryAsync(CancellationToken.None);
        history.Count.Should().Be(3);
    }

    [Fact]
    public async Task Manager_FreshInstall_NoHistory_NoPruningError()
    {
        var store = new InMemoryConfigurationStore();
        await store.WriteCurrentAsync(
            JsonSerializer.Serialize(B2TestFixtures.ValidMinimal(), JsonOptions),
            CancellationToken.None);

        var manager = new ConfigurationManager(store);
        var act = async () => await manager.InitializeAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
