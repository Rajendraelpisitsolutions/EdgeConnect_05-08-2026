// ============================================================================
// File: Configuration/SinkInstanceConfigTests.cs
// Covers: SinkInstanceConfig + PublishingSettings extras dictionary.
// ============================================================================

using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Configuration;

public sealed class SinkInstanceConfigTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void Deserialize_MqttSink_ConnectionAndPublishingExtras()
    {
        const string json = """
            {
              "InstanceId": "mqtt-eremos-main",
              "ProtocolName": "mqtt",
              "Connection": {
                "BrokerHost": "broker.example.com",
                "BrokerPort": 8883,
                "ClientId": "edgeconnect-test",
                "UseTls": true
              },
              "Publishing": {
                "BatchSize": 200,
                "BatchIntervalMs": 100,
                "TopicPrefix": "factory/cnc",
                "QoS": 1,
                "Retain": false
              }
            }
            """;

        var sink = JsonSerializer.Deserialize<SinkInstanceConfig>(json, JsonOptions);

        sink.Should().NotBeNull();
        sink!.InstanceId.Should().Be("mqtt-eremos-main");
        sink.ProtocolName.Should().Be("mqtt");

        // Universal publishing fields are typed
        sink.Publishing.BatchSize.Should().Be(200);
        sink.Publishing.BatchIntervalMs.Should().Be(100);

        // Protocol-specific publishing fields land in Extras
        sink.Publishing.Extras.Should().NotBeNull();
        sink.Publishing.Extras!["TopicPrefix"].GetString().Should().Be("factory/cnc");
        sink.Publishing.Extras["QoS"].GetInt32().Should().Be(1);
        sink.Publishing.Extras["Retain"].GetBoolean().Should().BeFalse();

        // Connection block stays opaque
        sink.Connection.Should().NotBeNull();
        sink.Connection!.Value.GetProperty("BrokerHost").GetString().Should().Be("broker.example.com");
    }

    [Fact]
    public void Deserialize_HttpSink_ShapedDifferentlyFromMqtt()
    {
        const string json = """
            {
              "InstanceId": "http-backup",
              "ProtocolName": "http",
              "Connection": {
                "BaseUrl": "https://api.example.com/ingest",
                "AuthType": "Bearer",
                "Token": "env:API_TOKEN"
              },
              "Publishing": {
                "BatchSize": 50,
                "BatchIntervalMs": 5000,
                "ContentType": "application/json"
              }
            }
            """;

        var sink = JsonSerializer.Deserialize<SinkInstanceConfig>(json, JsonOptions);

        sink!.Publishing.BatchSize.Should().Be(50);
        sink.Publishing.BatchIntervalMs.Should().Be(5000);
        sink.Publishing.Extras!["ContentType"].GetString().Should().Be("application/json");

        sink.Connection!.Value.GetProperty("BaseUrl").GetString().Should().Be("https://api.example.com/ingest");
        sink.Connection.Value.GetProperty("AuthType").GetString().Should().Be("Bearer");
    }

    [Fact]
    public void Deserialize_NoPublishingBlock_DefaultsApplied()
    {
        const string json = """
            {
              "InstanceId": "default-pub",
              "ProtocolName": "mqtt"
            }
            """;

        var sink = JsonSerializer.Deserialize<SinkInstanceConfig>(json, JsonOptions);

        sink!.Publishing.BatchSize.Should().Be(100);
        sink.Publishing.BatchIntervalMs.Should().Be(250);
        sink.Publishing.Extras.Should().BeNull();
    }

    [Fact]
    public void Deserialize_DisabledSink_Honored()
    {
        const string json = """
            {
              "InstanceId": "off-sink",
              "ProtocolName": "tcp",
              "Enabled": false
            }
            """;

        var sink = JsonSerializer.Deserialize<SinkInstanceConfig>(json, JsonOptions);

        sink!.Enabled.Should().BeFalse();
    }

    [Fact]
    public void Deserialize_MissingInstanceId_Throws()
    {
        const string json = """
            {
              "ProtocolName": "mqtt"
            }
            """;

        var act = () => JsonSerializer.Deserialize<SinkInstanceConfig>(json, JsonOptions);

        act.Should().Throw<JsonException>();
    }
}
