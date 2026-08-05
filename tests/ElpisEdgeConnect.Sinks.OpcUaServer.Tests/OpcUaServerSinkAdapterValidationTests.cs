// ============================================================================
// Tests: OpcUaServerSinkAdapter validation surface.
//        Validation is what config tooling (Connectivity Studio in M)
//        runs against an authored gateway.json BEFORE asking the
//        adapter to start. K (security hardening) flipped the
//        former "reject everything except None+Anonymous" gate into
//        targeted coherence checks. These tests enforce:
//
//          * All four security modes (None / Sign / SignAndEncrypt)
//            and all three user-token policies (Anonymous / UserName /
//            Certificate) PASS validation when coherently configured.
//          * UserName policy WITHOUT credentials fails with the
//            locked OPCUA.USERNAME_POLICY_WITHOUT_CREDENTIALS code.
//          * Empty UserTokenPolicies array fails with the locked
//            OPCUA.SECURITY_NO_TOKEN_POLICIES code.
//          * Basic field validation — bad EndpointUrl etc.
// ============================================================================

using System;
using System.Threading;
using ElpisEdgeConnect.Sinks.OpcUaServer;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.OpcUaServer.Tests;

public class OpcUaServerSinkAdapterValidationTests
{
    private static OpcUaServerSinkAdapter MakeAdapter(string instanceId = "opcua-test") =>
        new(instanceId, NullLogger<OpcUaServerSinkAdapter>.Instance);

    private static OpcUaServerConfiguration MakeConfig(
        OpcUaSecurityMode mode = OpcUaSecurityMode.None,
        OpcUaUserTokenPolicy[]? userTokens = null,
        OpcUaCredential[]? credentials = null,
        string endpointUrl = "opc.tcp://0.0.0.0:4840/edgeconnect",
        string instanceId = "opcua-test") =>
        new()
        {
            InstanceId = instanceId,
            ProtocolName = OpcUaServerConfiguration.ProtocolNameConstant,
            EndpointUrl = endpointUrl,
            Security = new OpcUaSecurityConfig
            {
                Mode = mode,
                UserTokenPolicies = userTokens ?? new[] { OpcUaUserTokenPolicy.Anonymous },
                Credentials = credentials ?? Array.Empty<OpcUaCredential>(),
            },
        };

    [Fact]
    public async System.Threading.Tasks.Task ValidateConfigAsync_DefaultConfig_Succeeds()
    {
        var adapter = MakeAdapter();
        var result = await adapter.ValidateConfigAsync(MakeConfig(), CancellationToken.None);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async System.Threading.Tasks.Task ValidateConfigAsync_InvalidEndpointUrl_Fails()
    {
        var adapter = MakeAdapter();
        var result = await adapter.ValidateConfigAsync(
            MakeConfig(endpointUrl: "not-a-uri"),
            CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "OPCUA.CONFIG_INVALID_ENDPOINT");
    }

    // ----- K: security modes now ACCEPTED at validation -----

    [Theory]
    [InlineData(OpcUaSecurityMode.None)]
    [InlineData(OpcUaSecurityMode.Sign)]
    [InlineData(OpcUaSecurityMode.SignAndEncrypt)]
    public async System.Threading.Tasks.Task ValidateConfigAsync_AnySecurityMode_PassesWithAnonymous(OpcUaSecurityMode mode)
    {
        var adapter = MakeAdapter();
        var result = await adapter.ValidateConfigAsync(
            MakeConfig(mode: mode),
            CancellationToken.None);
        result.IsValid.Should().BeTrue(
            $"K honors SecurityMode={mode} when paired with a valid user-token policy");
    }

    [Fact]
    public async System.Threading.Tasks.Task ValidateConfigAsync_UserNameWithCredentials_Passes()
    {
        var adapter = MakeAdapter();
        var result = await adapter.ValidateConfigAsync(
            MakeConfig(
                userTokens: new[] { OpcUaUserTokenPolicy.UserName },
                credentials: new[] { new OpcUaCredential { Username = "scada", Password = "s3cr3t" } }),
            CancellationToken.None);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async System.Threading.Tasks.Task ValidateConfigAsync_CertificateToken_Passes()
    {
        var adapter = MakeAdapter();
        var result = await adapter.ValidateConfigAsync(
            MakeConfig(userTokens: new[] { OpcUaUserTokenPolicy.Certificate }),
            CancellationToken.None);
        result.IsValid.Should().BeTrue(
            "Certificate user-token validation is delegated to the OPC UA library's trust-list machinery");
    }

    // ----- K: targeted coherence failures -----

    [Fact]
    public async System.Threading.Tasks.Task ValidateConfigAsync_UserNameWithoutCredentials_Fails()
    {
        var adapter = MakeAdapter();
        var result = await adapter.ValidateConfigAsync(
            MakeConfig(userTokens: new[] { OpcUaUserTokenPolicy.UserName }),
            CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "OPCUA.USERNAME_POLICY_WITHOUT_CREDENTIALS");
    }

    [Fact]
    public async System.Threading.Tasks.Task ValidateConfigAsync_EmptyUserTokenPolicies_Fails()
    {
        var adapter = MakeAdapter();
        var result = await adapter.ValidateConfigAsync(
            MakeConfig(userTokens: Array.Empty<OpcUaUserTokenPolicy>()),
            CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "OPCUA.SECURITY_NO_TOKEN_POLICIES");
    }

    // ----- Capability surface (unchanged from H.1) -----

    [Fact]
    public void ProtocolName_MatchesConstant() =>
        MakeAdapter().ProtocolName.Should().Be(OpcUaServerConfiguration.ProtocolNameConstant);

    [Fact]
    public void Capabilities_DeclaresPullPushSubscriptionAndSessionTracking()
    {
        var caps = MakeAdapter().Capabilities;
        caps.HasFlag(ElpisEdgeConnect.Core.Adapters.SinkCapabilities.Pull).Should().BeTrue();
        caps.HasFlag(ElpisEdgeConnect.Core.Adapters.SinkCapabilities.Push).Should().BeTrue();
        caps.HasFlag(ElpisEdgeConnect.Core.Adapters.SinkCapabilities.Subscription).Should().BeTrue();
        caps.HasFlag(ElpisEdgeConnect.Core.Adapters.SinkCapabilities.SessionTracking).Should().BeTrue();
        caps.HasFlag(ElpisEdgeConnect.Core.Adapters.SinkCapabilities.Browse).Should().BeTrue();
    }
}
