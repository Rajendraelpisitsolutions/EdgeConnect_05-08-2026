// ============================================================================
// Tests: OpcUaServerConfiguration.FromSinkInstance — gateway.json
//        → typed config projection, including K's new credentials
//        and user-token policy fields.
// ============================================================================

using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Sinks.OpcUaServer;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.OpcUaServer.Tests;

public class OpcUaServerConfigurationFromSinkInstanceTests
{
    private static SinkInstanceConfig BuildInstance(string connectionJson) =>
        new()
        {
            InstanceId = "opcua-test",
            ProtocolName = OpcUaServerConfiguration.ProtocolNameConstant,
            Connection = JsonDocument.Parse(connectionJson).RootElement,
        };

    [Fact]
    public void FromSinkInstance_DefaultsToNoneAndAnonymous()
    {
        var instance = BuildInstance(@"{ ""endpointUrl"": ""opc.tcp://0.0.0.0:4840/edgeconnect"" }");
        var config = OpcUaServerConfiguration.FromSinkInstance(instance);

        config.Security.Mode.Should().Be(OpcUaSecurityMode.None);
        config.Security.UserTokenPolicies.Should().ContainSingle()
            .Which.Should().Be(OpcUaUserTokenPolicy.Anonymous);
        config.Security.Credentials.Should().BeEmpty();
    }

    [Fact]
    public void FromSinkInstance_ReadsSignAndEncryptMode()
    {
        var instance = BuildInstance(@"{
            ""endpointUrl"": ""opc.tcp://0.0.0.0:4840/edgeconnect"",
            ""security"": { ""mode"": ""SignAndEncrypt"" }
        }");

        var config = OpcUaServerConfiguration.FromSinkInstance(instance);
        config.Security.Mode.Should().Be(OpcUaSecurityMode.SignAndEncrypt);
    }

    [Fact]
    public void FromSinkInstance_ReadsUserTokenPolicies()
    {
        var instance = BuildInstance(@"{
            ""endpointUrl"": ""opc.tcp://0.0.0.0:4840/edgeconnect"",
            ""security"": {
                ""userTokenPolicies"": [""UserName"", ""Certificate""]
            }
        }");

        var config = OpcUaServerConfiguration.FromSinkInstance(instance);
        config.Security.UserTokenPolicies.Should().Equal(
            OpcUaUserTokenPolicy.UserName, OpcUaUserTokenPolicy.Certificate);
    }

    [Fact]
    public void FromSinkInstance_ReadsCredentials()
    {
        var instance = BuildInstance(@"{
            ""endpointUrl"": ""opc.tcp://0.0.0.0:4840/edgeconnect"",
            ""security"": {
                ""mode"": ""SignAndEncrypt"",
                ""userTokenPolicies"": [""UserName""],
                ""credentials"": [
                    { ""username"": ""scada"", ""password"": ""s3cr3t!"", ""displayName"": ""SCADA HMI"" },
                    { ""username"": ""mes"", ""password"": ""m3s_p4ss"" }
                ]
            }
        }");

        var config = OpcUaServerConfiguration.FromSinkInstance(instance);
        config.Security.Credentials.Should().HaveCount(2);
        config.Security.Credentials[0].Username.Should().Be("scada");
        config.Security.Credentials[0].Password.Should().Be("s3cr3t!");
        config.Security.Credentials[0].DisplayName.Should().Be("SCADA HMI");
        config.Security.Credentials[1].Username.Should().Be("mes");
        config.Security.Credentials[1].DisplayName.Should().BeNull();
    }

    [Fact]
    public void FromSinkInstance_SilentlyDropsMalformedCredentials()
    {
        var instance = BuildInstance(@"{
            ""endpointUrl"": ""opc.tcp://0.0.0.0:4840/edgeconnect"",
            ""security"": {
                ""credentials"": [
                    { ""username"": ""ok"", ""password"": ""p"" },
                    { ""username"": """", ""password"": ""p"" },
                    { ""username"": ""missing-pw"" }
                ]
            }
        }");

        var config = OpcUaServerConfiguration.FromSinkInstance(instance);
        config.Security.Credentials.Should().HaveCount(1);
        config.Security.Credentials[0].Username.Should().Be("ok");
    }
}
