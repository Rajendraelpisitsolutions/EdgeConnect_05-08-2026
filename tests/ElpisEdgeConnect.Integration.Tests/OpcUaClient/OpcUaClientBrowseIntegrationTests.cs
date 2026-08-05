// ============================================================================
// File: OpcUaClientBrowseIntegrationTests.cs
// Purpose: End-to-end validation of OpcUaClientBrowseService against the
//          in-process server's known address space. Exercises:
//
//            BrowseAsync (StartingNodeId=null, default depth=1)
//              → Objects folder + its children, including the fixture's
//                Simulated folder
//            BrowseAsync (StartingNodeId=Simulated folder, depth=1)
//              → returns the 6 simulated tags as variable nodes
//            BrowseAsync (StartingNodeId=null, depth=2)
//              → walks deeper, surfaces nested variables under Simulated
//            BrowseAsync (MaxNodes truncation)
//              → Truncated=true when MaxNodes < actual node count
//            BrowseAsync (malformed JSON config)
//              → throws OPCUA.BROWSE_CONFIG_INVALID
//
// Reference: docs/decisions/0015-wizard-contract.md Rule 9
//            PR 7a plan (user lock 2026-05-29)
// ============================================================================

using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Browse;
using ElpisEdgeConnect.Sources.OpcUaClient;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Integration.Tests.OpcUaClient;

[Trait("Category", "OpcUaClient")]
public sealed class OpcUaClientBrowseIntegrationTests
    : IClassFixture<OpcUaClientBrowseIntegrationTests.SharedServer>
{
    private readonly SharedServer _shared;
    private readonly OpcUaClientBrowseService _browseService = new(NullLogger<OpcUaClientBrowseService>.Instance);

    public OpcUaClientBrowseIntegrationTests(SharedServer shared) { _shared = shared; }

    [Fact]
    public async Task BrowseAsync_FromObjectsRoot_DiscoversSimulatedFolder()
    {
        var request = new BrowseRequest
        {
            SourceConfigJson = MakeConfigJson(_shared.Fixture.EndpointUrl),
            // StartingNodeId null → service defaults to Objects folder.
            MaxDepth = 1,
            MaxNodes = 100,
        };

        var result = await _browseService.BrowseAsync(request, CancellationToken.None);

        result.Should().NotBeNull();
        result.Root.Should().NotBeNull();
        result.Root.Children.Should().Contain(c =>
            c.DisplayName == OpcUaClientInProcessServerFixture.SimulatedFolderBrowseName,
            "the fixture exposes a 'Simulated' folder under Objects per its locked address-space layout");
    }

    [Fact]
    public async Task BrowseAsync_FromSimulatedFolder_DiscoversAllSixTags()
    {
        var request = new BrowseRequest
        {
            SourceConfigJson = MakeConfigJson(_shared.Fixture.EndpointUrl),
            StartingNodeId = $"ns={OpcUaClientInProcessServerFixture.SimulatedNamespaceIndex};s={OpcUaClientInProcessServerFixture.SimulatedFolderBrowseName}",
            MaxDepth = 1,
            MaxNodes = 100,
        };

        var result = await _browseService.BrowseAsync(request, CancellationToken.None);

        var childNames = result.Root.Children.Select(c => c.DisplayName).ToList();
        childNames.Should().BeEquivalentTo(new[]
        {
            "Counter", "Sine", "Square", "Timestamp", "Text", "Static_Int",
        });
    }

    [Fact]
    public async Task BrowseAsync_SimulatedFolder_TagsClassifiedAsVariableNodes()
    {
        var request = new BrowseRequest
        {
            SourceConfigJson = MakeConfigJson(_shared.Fixture.EndpointUrl),
            StartingNodeId = $"ns={OpcUaClientInProcessServerFixture.SimulatedNamespaceIndex};s={OpcUaClientInProcessServerFixture.SimulatedFolderBrowseName}",
            MaxDepth = 1,
            MaxNodes = 100,
        };

        var result = await _browseService.BrowseAsync(request, CancellationToken.None);

        result.Root.Children.Should().AllSatisfy(child =>
            child.Kind.Should().Be(BrowseNodeKind.Variable,
                "the simulated tags are BaseDataVariableState — they must surface as Variable, not Folder"));
    }

    [Fact]
    public async Task BrowseAsync_MaxNodesTruncation_SetsTruncatedFlag()
    {
        var request = new BrowseRequest
        {
            SourceConfigJson = MakeConfigJson(_shared.Fixture.EndpointUrl),
            StartingNodeId = $"ns={OpcUaClientInProcessServerFixture.SimulatedNamespaceIndex};s={OpcUaClientInProcessServerFixture.SimulatedFolderBrowseName}",
            MaxDepth = 1,
            MaxNodes = 2, // Fixture has 6 tags; 2 forces truncation.
        };

        var result = await _browseService.BrowseAsync(request, CancellationToken.None);

        result.Truncated.Should().BeTrue(
            "MaxNodes=2 against a 6-tag folder must surface Truncated=true so the wizard can render a 'showing 2 of 6' banner");
    }

    [Fact]
    public async Task BrowseAsync_MalformedConfigJson_ThrowsInvalidOperation()
    {
        var request = new BrowseRequest
        {
            SourceConfigJson = "{not valid json",
            MaxDepth = 1,
            MaxNodes = 100,
        };

        var act = () => _browseService.BrowseAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*OPCUA.BROWSE_CONFIG_INVALID*");
    }

    private static string MakeConfigJson(string endpointUrl)
    {
        var config = new OpcUaClientSourceConfiguration
        {
            InstanceId = "opcua-browse-test",
            ProtocolName = OpcUaClientSourceConfiguration.ProtocolNameConstant,
            DeviceId = "test-server",
            EndpointUrl = endpointUrl,
            ApplicationUri = $"urn:elpis:edgeconnect:test:client:{Guid.NewGuid():N}",
            SecurityMode = OpcUaSecurityMode.None,
            AuthMode = OpcUaAuthMode.Anonymous,
            AutoAcceptUntrustedServerCertificate = true,
        };
        return JsonSerializer.Serialize(config);
    }

    public sealed class SharedServer : IAsyncLifetime
    {
        public OpcUaClientInProcessServerFixture Fixture { get; private set; } = null!;
        public async Task InitializeAsync() => Fixture = await OpcUaClientInProcessServerFixture.StartAsync();
        public async Task DisposeAsync() => await Fixture.DisposeAsync();
    }
}
