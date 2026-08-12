// ============================================================================
// File: Configuration/ConfigurationDifferTests.cs
// Covers: ConfigurationDiffer detects added/removed/modified across sources,
//         sinks, routes, and gateway. Output is deterministic.
// ============================================================================

using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Configuration;

public sealed class ConfigurationDifferTests
{
    private static readonly ConfigurationDiffer Differ = ConfigurationDiffer.Instance;

    [Fact]
    public void Diff_IdenticalConfigs_IsEmpty()
    {
        var config = B2TestFixtures.ValidWithMultiple();
        var result = Differ.Diff(config, config);
        result.Should().BeEmpty();
    }

    /// <summary>
    /// C3-R1 pin: a config that has been round-tripped through JSON must
    /// diff as empty against its source. Default record equality fails this
    /// because JsonElement? and IDictionary&lt;string, JsonElement&gt; fall
    /// back to reference equality on round-trip; the differ now compares
    /// structurally via JSON serialization.
    /// </summary>
    [Fact]
    public void Diff_JsonRoundTrippedConfig_ReportsNoChanges()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() },
        };
        var original = B2TestFixtures.ValidWithMultiple();
        var json = JsonSerializer.Serialize(original, options);
        var roundTripped = JsonSerializer.Deserialize<GatewayConfiguration>(json, options)!;

        // Critical: these are two different object instances with content-
        // identical JsonElement fields. The differ must treat them as equal.
        var result = Differ.Diff(original, roundTripped);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Diff_NullOldConfig_ReportsEverythingAsAdded()
    {
        var newConfig = B2TestFixtures.ValidWithMultiple();
        var result = Differ.Diff(null, newConfig);

        result.Where(c => c.EntityKind == ConfigurationEntityKind.Source).Should().HaveCount(2);
        result.Where(c => c.EntityKind == ConfigurationEntityKind.Sink).Should().HaveCount(2);
        result.Where(c => c.EntityKind == ConfigurationEntityKind.Route).Should().HaveCount(2);
        result.Where(c => c.EntityKind == ConfigurationEntityKind.GatewaySettings).Should().HaveCount(1);
        result.Should().AllSatisfy(c => c.Kind.Should().Be(ConfigurationChangeKind.Added));
    }

    // ========================================================================
    // Source diffs
    // ========================================================================

    [Fact]
    public void Diff_SourceAdded_Detected()
    {
        var oldConfig = B2TestFixtures.ValidMinimal();
        var newConfig = B2TestFixtures.ValidMinimal() with
        {
            Sources =
            [
                new SourceInstanceConfig { InstanceId = "src-1", ProtocolName = "focas2", DeviceId = "Dev-1" },
                new SourceInstanceConfig { InstanceId = "src-2", ProtocolName = "modbus", DeviceId = "Dev-2" },
            ],
        };

        var result = Differ.Diff(oldConfig, newConfig);

        result.Should().Contain(c => c.Kind == ConfigurationChangeKind.Added
            && c.EntityKind == ConfigurationEntityKind.Source
            && c.EntityId == "src-2");
    }

    [Fact]
    public void Diff_SourceRemoved_Detected()
    {
        var oldConfig = B2TestFixtures.ValidWithMultiple();
        var newConfig = oldConfig with
        {
            Sources = [oldConfig.Sources[0]],
        };

        var result = Differ.Diff(oldConfig, newConfig);

        result.Should().Contain(c => c.Kind == ConfigurationChangeKind.Removed
            && c.EntityId == "src-b");
    }

    [Fact]
    public void Diff_SourceModified_Detected()
    {
        var oldConfig = B2TestFixtures.ValidMinimal();
        var newConfig = oldConfig with
        {
            Sources =
            [
                oldConfig.Sources[0] with
                {
                    Polling = new PollingSettings { IntervalMs = 5000 },
                },
            ],
        };

        var result = Differ.Diff(oldConfig, newConfig);

        result.Should().Contain(c => c.Kind == ConfigurationChangeKind.Modified
            && c.EntityKind == ConfigurationEntityKind.Source
            && c.EntityId == "src-1");
    }

    // ========================================================================
    // Sink diffs
    // ========================================================================

    [Fact]
    public void Diff_SinkAdded_Detected()
    {
        var oldConfig = B2TestFixtures.ValidMinimal();
        var newConfig = oldConfig with
        {
            Sinks =
            [
                new SinkInstanceConfig { InstanceId = "sink-1", ProtocolName = "mqtt" },
                new SinkInstanceConfig { InstanceId = "sink-2", ProtocolName = "http" },
            ],
        };

        var result = Differ.Diff(oldConfig, newConfig);

        result.Should().Contain(c => c.Kind == ConfigurationChangeKind.Added
            && c.EntityKind == ConfigurationEntityKind.Sink
            && c.EntityId == "sink-2");
    }

    [Fact]
    public void Diff_SinkRemoved_Detected()
    {
        var oldConfig = B2TestFixtures.ValidWithMultiple();
        var newConfig = oldConfig with { Sinks = [oldConfig.Sinks[0]] };

        var result = Differ.Diff(oldConfig, newConfig);

        result.Should().Contain(c => c.Kind == ConfigurationChangeKind.Removed
            && c.EntityKind == ConfigurationEntityKind.Sink
            && c.EntityId == "sink-b");
    }

    // ========================================================================
    // Route diffs
    // ========================================================================

    [Fact]
    public void Diff_RouteAdded_Detected()
    {
        var oldConfig = B2TestFixtures.ValidMinimal();
        var newConfig = oldConfig with
        {
            Routes =
            [
                oldConfig.Routes[0],
                new RouteConfig
                {
                    RouteId = "route-2",
                    Name = "Second",
                    SourceInstanceId = "src-1",
                    SinkInstanceIds = ["sink-1"],
                },
            ],
        };

        var result = Differ.Diff(oldConfig, newConfig);

        result.Should().Contain(c => c.Kind == ConfigurationChangeKind.Added
            && c.EntityKind == ConfigurationEntityKind.Route
            && c.EntityId == "route-2");
    }

    [Fact]
    public void Diff_RouteSinksModified_Detected()
    {
        var oldConfig = B2TestFixtures.ValidMinimal();
        var newConfig = oldConfig with
        {
            Routes =
            [
                oldConfig.Routes[0] with
                {
                    SinkInstanceIds = ["sink-1", "sink-2"],
                },
            ],
        };

        var result = Differ.Diff(oldConfig, newConfig);

        result.Should().Contain(c => c.Kind == ConfigurationChangeKind.Modified
            && c.EntityKind == ConfigurationEntityKind.Route);
    }

    // ========================================================================
    // Gateway settings diff
    // ========================================================================

    [Fact]
    public void Diff_GatewaySettingsChanged_Detected()
    {
        var oldConfig = B2TestFixtures.ValidMinimal();
        var newConfig = oldConfig with
        {
            Gateway = new GatewaySettings
            {
                GatewayId = oldConfig.Gateway.GatewayId,
                GatewayName = "Renamed Gateway",
            },
        };

        var result = Differ.Diff(oldConfig, newConfig);

        result.Should().Contain(c => c.Kind == ConfigurationChangeKind.Modified
            && c.EntityKind == ConfigurationEntityKind.GatewaySettings);
    }

    // ========================================================================
    // Determinism
    // ========================================================================

    [Fact]
    public void Diff_OutputOrderIsDeterministic()
    {
        var oldConfig = B2TestFixtures.ValidMinimal();
        var newConfig = B2TestFixtures.ValidWithMultiple();

        var result1 = Differ.Diff(oldConfig, newConfig);
        var result2 = Differ.Diff(oldConfig, newConfig);

        result1.Select(c => (c.EntityKind, c.EntityId, c.Kind))
            .Should()
            .Equal(result2.Select(c => (c.EntityKind, c.EntityId, c.Kind)));
    }

    [Fact]
    public void Summarize_NoChanges_ReturnsNoChanges()
    {
        ConfigurationDiffer.Summarize([]).Should().Be("No changes");
    }

    [Fact]
    public void Summarize_MixedChanges_ReportsCounts()
    {
        var oldConfig = B2TestFixtures.ValidMinimal();
        var newConfig = B2TestFixtures.ValidWithMultiple();
        var changes = Differ.Diff(oldConfig, newConfig);

        var summary = ConfigurationDiffer.Summarize(changes);

        summary.Should().Contain("added");
    }

    [Fact]
    public void Diff_NullNewConfig_Throws()
    {
        var act = () => Differ.Diff(B2TestFixtures.ValidMinimal(), null!);
        act.Should().Throw<System.ArgumentNullException>();
    }
}
