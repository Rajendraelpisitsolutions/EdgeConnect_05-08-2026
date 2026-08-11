// ============================================================================
// File: Eremos/EremosV2ContractTests.cs
// Purpose: EREMOS V2 revalidation [Fact] tests on the mock-fallback path
//          per v2 plan §6. Standalone pre-soak gate + soak sub-component
//          per Q4.
//
//          Test scope landed in this PR:
//            * DedicatedTestBroker fixture sanity (broker spawns on a
//              random port + accepts connections + stops cleanly).
//            * MockSubscriber wiring (connect + subscribe on Phase 0 +
//              receive messages + count per topic).
//            * EremosV2ContractValidator gate-by-gate logic against
//              synthetic message captures (Gate 3 schema, Gate 4 topic
//              determinism, Gate 2 emit/receive parity).
//            * BuildMockFallbackReport assembly + the explicit
//              SKIPPED-with-reason contract for Gates 6 + 7.
//
//          DEFERRED to a follow-up milestone (PR description):
//            * Full-gateway end-to-end test wiring HostHarness +
//              MqttSinkAdapter + DedicatedTestBroker + MockSubscriber
//              with MockSourceAdapter emitting realistic canonical
//              points over 2 minutes.
//            * Gate 5 (broker outage injection) — DedicatedTestBroker
//              supports Stop/Start; the test wiring is what's deferred.
//            * Gate 8 (sink backpressure) — needs SlowSinkDecorator
//              wrapping MqttSinkAdapter; that wrapper is a follow-up.
//            * Real-EREMOS path (Gates 6 + 7 unskipped) — requires the
//              customer-bound EREMOS V2 binary in the in-house lab.
//
//          All deferrals are surfaced as Skipped tests with explicit
//          reasons rather than silent gaps.
//
// Reference: docs/sessions/2026-05-21-eremos-v2-revalidation-plan-v2.md §6
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using MQTTnet;
using MQTTnet.Client;
using Xunit;

namespace ElpisEdgeConnect.Integration.Tests.Eremos;

[Trait("Category", "EremosContract")]
[Trait("Category", "RequiresMqttBroker")]
[Collection("EremosRevalidation")]
public sealed class EremosV2ContractTests
{
    private static void RequireMosquittoOrThrow()
    {
        if (!DedicatedTestBroker.IsAvailable())
        {
            throw new InvalidOperationException(
                "Mosquitto not installed at the standard Windows path. This test requires " +
                "C:\\Program Files\\mosquitto\\mosquitto.exe. The existing MQTT integration " +
                "tests share the same requirement (per CLAUDE.md §8).");
        }
    }

    [Fact]
    public async Task DedicatedTestBroker_SpawnsAndAcceptsConnections_OnFreePort()
    {
        RequireMosquittoOrThrow();

        await using var broker = new DedicatedTestBroker();

        broker.Port.Should().BeGreaterThan(0);
        broker.BrokerUrl.Should().StartWith("tcp://127.0.0.1:");

        // Verify a stock MQTT client can connect + disconnect against the
        // spawned broker. This is the fixture's contract.
        using var client = new MqttFactory().CreateMqttClient();
        var options = new MqttClientOptionsBuilder()
            .WithClientId("dedicated-broker-sanity")
            .WithTcpServer("127.0.0.1", broker.Port)
            .Build();

        var connectResult = await client.ConnectAsync(options);
        connectResult.ResultCode.Should().Be(MqttClientConnectResultCode.Success);
        await client.DisconnectAsync();
    }

    [Fact]
    public async Task MockSubscriber_ReceivesMessages_OnPhase0Pattern_AndCountsPerTopic()
    {
        // Sanity test for the mock subscriber wiring. Direct MQTT publish
        // (no gateway) to the dedicated broker; verify the subscriber
        // sees the messages on the Phase 0 subscription pattern + the
        // per-topic counter increments correctly.
        RequireMosquittoOrThrow();

        await using var broker = new DedicatedTestBroker();
        await using var subscriber = new EremosV2MockSubscriber(broker.BrokerUrl, "mock-sub-1");
        await subscriber.ConnectAsync();

        // Direct publish on Phase 0 topic shape.
        using var publisher = new MqttFactory().CreateMqttClient();
        await publisher.ConnectAsync(new MqttClientOptionsBuilder()
            .WithClientId("publisher-1")
            .WithTcpServer("127.0.0.1", broker.Port)
            .Build());

        const string topicA = "eremos/gw-1/cnc/source-1/Status_RunState";
        const string topicB = "eremos/gw-1/cnc/source-1/Status_Mode";
        for (var i = 0; i < 5; i++)
        {
            await publisher.PublishStringAsync(topicA, "RUNNING", MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);
        }
        for (var i = 0; i < 3; i++)
        {
            await publisher.PublishStringAsync(topicB, "MEM", MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce);
        }

        await publisher.DisconnectAsync();

        // Wait briefly for the subscriber to drain. Use a deadline rather
        // than a fixed sleep so the test isn't slower than necessary.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && subscriber.ReceiveCount < 8)
        {
            await Task.Delay(50);
        }

        subscriber.ReceiveCount.Should().Be(8);
        subscriber.PerTopicReceiveCounts[topicA].Should().Be(5);
        subscriber.PerTopicReceiveCounts[topicB].Should().Be(3);
    }

    [Fact]
    public void Gate3SchemaStability_AllValidPerTagPayloads_Passes()
    {
        var messages = new List<ReceivedMessage>
        {
            new("eremos/gw-1/cnc/src-1/Status_RunState", "RUNNING", Encoding.UTF8.GetBytes("RUNNING"), DateTime.UtcNow),
            new("eremos/gw-1/cnc/src-1/Status_Mode", "MEM", Encoding.UTF8.GetBytes("MEM"), DateTime.UtcNow),
            new("eremos/gw-1/cnc/src-1/CycleTime_Cycle", "12.34", Encoding.UTF8.GetBytes("12.34"), DateTime.UtcNow),
        };

        var result = EremosV2ContractValidator.Gate3SchemaStability(messages);

        result.Outcome.Should().Be(GateOutcome.Pass);
        result.Bucket.Should().Be(GateBucket.Contract);
        result.Evidence.Should().Contain("100% pass rate");
    }

    [Fact]
    public void Gate3SchemaStability_JsonWrapperPayload_Fails()
    {
        // PerTag scalar contract emits raw values; a JSON-wrapper payload
        // is a contract violation.
        var jsonWrapped = """{"value":"RUNNING","ts":"2026-05-22T08:15:00Z"}""";
        var messages = new List<ReceivedMessage>
        {
            new("eremos/gw-1/cnc/src-1/Status_RunState", jsonWrapped, Encoding.UTF8.GetBytes(jsonWrapped), DateTime.UtcNow),
        };

        var result = EremosV2ContractValidator.Gate3SchemaStability(messages);

        result.Outcome.Should().Be(GateOutcome.Fail);
        result.Evidence.Should().Contain("JSON-wrapper payload");
    }

    [Fact]
    public void Gate4TopicDeterminism_AllTopicsValidNoCollisions_Passes()
    {
        var messages = new List<ReceivedMessage>
        {
            new("eremos/gw-1/cnc/src-1/Status_RunState", "RUNNING", Encoding.UTF8.GetBytes("RUNNING"), DateTime.UtcNow),
            new("eremos/gw-1/cnc/src-1/MachineInfo_Hostname", "BRN68E74A", Encoding.UTF8.GetBytes("BRN68E74A"), DateTime.UtcNow),
        };

        var configuredPaths = new[] { "Status/RunState", "MachineInfo/Hostname", "Tools/Active/Number" };

        var result = EremosV2ContractValidator.Gate4TopicDeterminism(messages, configuredPaths);

        result.Outcome.Should().Be(GateOutcome.Pass);
        result.Bucket.Should().Be(GateBucket.Contract);
    }

    [Fact]
    public void Gate4TopicDeterminism_TopicOutsidePhase0Regex_Fails()
    {
        var messages = new List<ReceivedMessage>
        {
            new("not-eremos/gw-1/cnc/src-1/Status_RunState", "RUNNING", Encoding.UTF8.GetBytes("RUNNING"), DateTime.UtcNow),
        };

        var result = EremosV2ContractValidator.Gate4TopicDeterminism(messages, new[] { "Status/RunState" });

        result.Outcome.Should().Be(GateOutcome.Fail);
        result.Evidence.Should().Contain("outside Phase 0 regex");
    }

    [Fact]
    public void Gate4TopicDeterminism_CanonicalPathCollision_Fails()
    {
        // Even with valid topic shapes, a collision between two canonical
        // paths sanitizing to the same MQTT segment is a Gate 4 failure.
        var messages = new List<ReceivedMessage>
        {
            new("eremos/gw-1/cnc/src-1/Status_Run_State", "x", Encoding.UTF8.GetBytes("x"), DateTime.UtcNow),
        };

        var collidingPaths = new[] { "Status/Run/State", "Status_Run/State" };

        var result = EremosV2ContractValidator.Gate4TopicDeterminism(messages, collidingPaths);

        result.Outcome.Should().Be(GateOutcome.Fail);
        result.Evidence.Should().Contain("collision");
    }

    [Fact]
    public void Gate2EmitReceiveParity_AllCountsMatch_Passes()
    {
        var emit = new Dictionary<string, long>
        {
            ["eremos/gw-1/cnc/src-1/Status_RunState"] = 100,
            ["eremos/gw-1/cnc/src-1/Status_Mode"] = 100,
        };
        var receive = new Dictionary<string, long>
        {
            ["eremos/gw-1/cnc/src-1/Status_RunState"] = 100,
            ["eremos/gw-1/cnc/src-1/Status_Mode"] = 100,
        };

        var result = EremosV2ContractValidator.Gate2EmitReceiveParity(emit, receive);

        result.Outcome.Should().Be(GateOutcome.Pass);
    }

    [Fact]
    public void Gate2EmitReceiveParity_TopicMissingFromReceive_Fails()
    {
        var emit = new Dictionary<string, long>
        {
            ["eremos/gw-1/cnc/src-1/Status_RunState"] = 100,
        };
        var receive = new Dictionary<string, long>
        {
            ["eremos/gw-1/cnc/src-1/Status_RunState"] = 95,
        };

        var result = EremosV2ContractValidator.Gate2EmitReceiveParity(emit, receive);

        result.Outcome.Should().Be(GateOutcome.Fail);
        result.Evidence.Should().Contain("emit=100");
        result.Evidence.Should().Contain("receive=95");
    }

    [Fact]
    public void Gate1MqttStability_ZeroDisconnects_Passes()
    {
        var result = EremosV2ContractValidator.Gate1MqttStability(0, 60);
        result.Outcome.Should().Be(GateOutcome.Pass);
        result.Bucket.Should().Be(GateBucket.Resilience);
    }

    [Fact]
    public void Gate1MqttStability_DisconnectStorm_Fails()
    {
        var result = EremosV2ContractValidator.Gate1MqttStability(7, 60);
        result.Outcome.Should().Be(GateOutcome.Fail);
        result.Evidence.Should().Contain("disconnect storm");
    }

    [Fact]
    public void BuildMockFallbackReport_AlwaysSkipsGates6And7_WithExplicitReasons()
    {
        // The cornerstone of the mock-fallback path: Gates 6 and 7 are
        // explicitly skipped with explicit reasons. The v2 plan §4.3
        // requires explicit skip reasons rather than silent passes.
        var report = EremosV2ContractValidator.BuildMockFallbackReport(
            gate1Stability: GateResult.Pass("g1", GateBucket.Resilience, "evidence"),
            gate2Parity: GateResult.Pass("g2", GateBucket.Contract, "evidence"),
            gate3Schema: GateResult.Pass("g3", GateBucket.Contract, "evidence"),
            gate4Determinism: GateResult.Pass("g4", GateBucket.Contract, "evidence"),
            gate5Reconnect: GateResult.Pass("g5", GateBucket.Resilience, "evidence"),
            gate8Backpressure: GateResult.Pass("g8", GateBucket.Resilience, "evidence"));

        report.Path.Should().Be("mock-fallback");
        report.Skipped.Should().HaveCount(2);

        var gate6 = report.Skipped[0];
        gate6.GateName.Should().Contain("Gate 6");
        gate6.Bucket.Should().Be(GateBucket.RealEremosOnly);
        gate6.Outcome.Should().Be(GateOutcome.Skipped);
        gate6.Evidence.Should().Contain("real-EREMOS-only");
        gate6.Evidence.Should().Contain("mock-fallback path");

        var gate7 = report.Skipped[1];
        gate7.GateName.Should().Contain("Gate 7");
        gate7.Bucket.Should().Be(GateBucket.RealEremosOnly);
        gate7.Outcome.Should().Be(GateOutcome.Skipped);
        gate7.Evidence.Should().Contain("real-EREMOS-only");
        gate7.Evidence.Should().Contain("mock-fallback path");
    }

    // Full-gateway end-to-end test, Gate 5 broker-outage test, and Gate 8
    // sink-backpressure test live in sibling EremosV2EndToEndTests.cs.
    // The previous [Skip] placeholders for these three tests were removed
    // once the substrate landed.
}
