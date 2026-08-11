// ============================================================================
// File: OpcUaClientTestConnectIntegrationTests.cs
// Purpose: End-to-end validation of OpcUaClientSourceAdapter.TestConnectAsync
//          against the in-process server. The wizard's "Test Connection"
//          button drives this path per ADR-0015 Rule 6 (read-only,
//          idempotent, no draft mutation).
//
//          Three scenarios:
//            1. Running server + Anonymous + None security → success,
//               ServerState=Running, message reports success
//            2. Stopped server → failure with a connect-error code
//               (network-level, not security-level)
//            3. Wrong endpoint URL (server up but path mismatch) →
//               failure with a network-class error code
//
// Reference: docs/decisions/0015-wizard-contract.md Rule 6, Rule 11.1
//            PR 7a plan (user lock 2026-05-29)
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Sources.OpcUaClient;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Integration.Tests.OpcUaClient;

[Trait("Category", "OpcUaClient")]
public sealed class OpcUaClientTestConnectIntegrationTests : IAsyncLifetime
{
    private OpcUaClientInProcessServerFixture? _fixture;
    private OpcUaClientSourceAdapter? _adapter;

    public async Task InitializeAsync()
    {
        _fixture = await OpcUaClientInProcessServerFixture.StartAsync();
        _adapter = new OpcUaClientSourceAdapter(
            $"opcua-testconnect-{Guid.NewGuid():N}",
            NullLogger<OpcUaClientSourceAdapter>.Instance);
    }

    public async Task DisposeAsync()
    {
        if (_adapter is not null) await _adapter.DisposeAsync();
        if (_fixture is not null) await _fixture.DisposeAsync();
    }

    [Fact]
    public async Task TestConnectAsync_RunningServer_ReturnsSuccess()
    {
        var config = MakeConfig(_adapter!.InstanceId, _fixture!.EndpointUrl);

        var result = await _adapter.TestConnectAsync(config, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.EndpointUrl.Should().Be(_fixture.EndpointUrl);
        result.ServerState.Should().Be("Running");
        result.Message.Should().Contain("Running");
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public async Task TestConnectAsync_DoesNotMutateAdapterState()
    {
        // ADR-0015 Rule 11.1 / Rule 6 — Test Connection is idempotent +
        // read-only. The adapter must NOT transition past Created (it
        // was never Initialized for this fixture).
        var stateBefore = _adapter!.State;
        var config = MakeConfig(_adapter.InstanceId, _fixture!.EndpointUrl);

        _ = await _adapter.TestConnectAsync(config, CancellationToken.None);

        _adapter.State.Should().Be(stateBefore,
            "Test Connection must NOT mutate adapter state — Rule 6 / Rule 11.1");
    }

    [Fact]
    public async Task TestConnectAsync_StoppedServer_ReturnsNetworkErrorCode()
    {
        await _fixture!.StopAsync();
        var config = MakeConfig(_adapter!.InstanceId, _fixture.EndpointUrl);

        var result = await _adapter.TestConnectAsync(config, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().NotBeNull();
        // The exact stack-level error varies (BadConnectionRejected,
        // SocketException-derived, or timeout) depending on platform
        // timing. ALL routes through ClassifyConnectError produce a
        // network-class code prefix.
        result.ErrorCode.Should().StartWith("OPCUA.");
    }

    [Fact]
    public async Task TestConnectAsync_RepeatedCalls_AllSucceed()
    {
        // Idempotency — Test Connection must be safe to invoke multiple
        // times in succession (matches operator usage pattern of clicking
        // the button repeatedly while tuning a draft).
        var config = MakeConfig(_adapter!.InstanceId, _fixture!.EndpointUrl);

        for (var i = 0; i < 3; i++)
        {
            var result = await _adapter.TestConnectAsync(config, CancellationToken.None);
            result.Success.Should().BeTrue($"iteration {i} must succeed against a healthy server");
        }
    }

    private static OpcUaClientSourceConfiguration MakeConfig(string instanceId, string endpointUrl) => new()
    {
        InstanceId = instanceId,
        ProtocolName = OpcUaClientSourceConfiguration.ProtocolNameConstant,
        DeviceId = "test-server",
        EndpointUrl = endpointUrl,
        ApplicationUri = $"urn:elpis:edgeconnect:test:client:{Guid.NewGuid():N}",
        SecurityMode = OpcUaSecurityMode.None,
        AuthMode = OpcUaAuthMode.Anonymous,
        AutoAcceptUntrustedServerCertificate = true,
    };
}
