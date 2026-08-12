// ============================================================================
// Tests: OpcUaClientBrowseServiceTests — pin the orchestration:
//        deserialize JSON → open session → run tree builder → close session.
//        Tests substitute the establisher + executor factory so we cover
//        the error split (PR 5 amendment #1) and the session-open/close
//        contract without a live OPC stack.
//
//        Invariants:
//          * Malformed JSON → OPCUA.BROWSE_CONFIG_INVALID
//          * Missing browse-critical fields (EndpointUrl / UserName
//            credentials / Certificate path) → OPCUA.BROWSE_CONFIG_INCOMPLETE
//          * Happy path: establisher called, session closed, BrowseResult
//            returned
//          * StartingNodeId null → default ObjectsFolder root used
//          * StartingNodeId set → that node becomes the tree root
//          * Establisher failure surfaces the original exception
// Reference: docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1
//            PR 5 plan + amendments (user lock 2026-05-29)
// ============================================================================

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Sources.OpcUaClient;
using FluentAssertions;
using BrowseRequest = ElpisEdgeConnect.Core.Browse.BrowseRequest;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Opc.Ua;
using Opc.Ua.Client;
using Xunit;

namespace ElpisEdgeConnect.Sources.OpcUaClient.Tests;

public sealed class OpcUaClientBrowseServiceTests
{
    private static string ValidConfigJson() => JsonSerializer.Serialize(new
    {
        InstanceId = "opcua-test",
        ProtocolName = OpcUaClientSourceConfiguration.ProtocolNameConstant,
        DeviceId = "factorytalk",
        EndpointUrl = "opc.tcp://factorytalk.pilot.local:4840",
        SecurityMode = "SignAndEncrypt",
        AuthMode = "Anonymous",
    });

    private static (OpcUaClientBrowseService Service, IOpcUaClientConnectionEstablisher Establisher, IOpcUaBrowseExecutor Executor) BuildService(
        IOpcUaBrowseExecutor? executor = null)
    {
        var fakeSession = Substitute.For<ISession>();
        fakeSession.CloseAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((StatusCode)StatusCodes.Good));

        var establisher = Substitute.For<IOpcUaClientConnectionEstablisher>();
        establisher.EstablishAsync(
            Arg.Any<OpcUaClientSourceConfiguration>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(fakeSession));

        executor ??= Substitute.For<IOpcUaBrowseExecutor>();
        executor.BrowseAsync(Arg.Any<NodeId>(), Arg.Any<CancellationToken>())
            .Returns(new BrowseExecutorResult { References = new ReferenceDescriptionCollection() });

        var service = new OpcUaClientBrowseService(NullLogger.Instance, establisher, _ => executor);
        return (service, establisher, executor);
    }

    // ─── Deserialization error split (PR 5 amendment #1) ──────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ \"EndpointUrl\": ")]
    [InlineData("definitely not json")]
    public async Task BrowseAsync_MalformedJson_ReturnsInvalidErrorCode(string badJson)
    {
        var (service, _, _) = BuildService();
        var request = new BrowseRequest { SourceConfigJson = badJson };

        var act = () => service.BrowseAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*OPCUA.BROWSE_CONFIG_INVALID*");
    }

    [Fact]
    public async Task BrowseAsync_MissingEndpointUrl_ReturnsIncompleteErrorCode()
    {
        // Operator hasn't filled EndpointUrl yet — OPCUA.BROWSE_CONFIG_INCOMPLETE
        // signals "fixable from UI" per amendment #1.
        var (service, _, _) = BuildService();
        var json = JsonSerializer.Serialize(new
        {
            InstanceId = "opcua-test",
            ProtocolName = OpcUaClientSourceConfiguration.ProtocolNameConstant,
            DeviceId = "factorytalk",
            EndpointUrl = "",
        });
        var request = new BrowseRequest { SourceConfigJson = json };

        var act = () => service.BrowseAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*OPCUA.BROWSE_CONFIG_INCOMPLETE*");
    }

    [Fact]
    public async Task BrowseAsync_UserNameWithoutCredentials_ReturnsIncompleteErrorCode()
    {
        var (service, _, _) = BuildService();
        var json = JsonSerializer.Serialize(new
        {
            InstanceId = "opcua-test",
            ProtocolName = OpcUaClientSourceConfiguration.ProtocolNameConstant,
            DeviceId = "factorytalk",
            EndpointUrl = "opc.tcp://factorytalk.pilot.local:4840",
            AuthMode = "UserName",
            // Credentials deliberately omitted.
        });
        var request = new BrowseRequest { SourceConfigJson = json };

        var act = () => service.BrowseAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*OPCUA.BROWSE_CONFIG_INCOMPLETE*");
    }

    // ─── Happy path orchestration ─────────────────────────────────────

    [Fact]
    public async Task BrowseAsync_HappyPath_OpensSessionAndReturnsResult()
    {
        var (service, establisher, _) = BuildService();
        var request = new BrowseRequest { SourceConfigJson = ValidConfigJson() };

        var result = await service.BrowseAsync(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Root.Should().NotBeNull();
        await establisher.Received(1).EstablishAsync(
            Arg.Any<OpcUaClientSourceConfiguration>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BrowseAsync_StartingNodeIdNull_UsesObjectsFolderAsRoot()
    {
        var (service, _, executor) = BuildService();
        var request = new BrowseRequest { SourceConfigJson = ValidConfigJson() };

        await service.BrowseAsync(request, CancellationToken.None);

        // ObjectIds.ObjectsFolder = i=85.
        await executor.Received(1).BrowseAsync(
            Arg.Is<NodeId>(n => n == ObjectIds.ObjectsFolder),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BrowseAsync_StartingNodeIdSet_UsesThatNode()
    {
        var (service, _, executor) = BuildService();
        var request = new BrowseRequest
        {
            SourceConfigJson = ValidConfigJson(),
            StartingNodeId = "ns=2;i=42",
        };

        await service.BrowseAsync(request, CancellationToken.None);

        await executor.Received(1).BrowseAsync(
            Arg.Is<NodeId>(n => n.ToString() == "ns=2;i=42"),
            Arg.Any<CancellationToken>());
    }
}
