// ============================================================================
// File: OpcUaClientBrowseService.cs
// Purpose: ITagBrowseService implementation for OPC UA Client. Wired by
//          the wizard (per ADR-0015 Rule 9). Deserializes the operator's
//          edited config from BrowseRequest.SourceConfigJson, opens a
//          dedicated session via IOpcUaClientConnectionEstablisher
//          (PR 2), drives the recursive walk via OpcUaBrowseTreeBuilder
//          (PR 5), closes the session.
//
// LOCKED behaviour:
//   * Standalone — NOT tied to a running adapter instance (per PR 5
//     sign-off: operator browses an edited config that may not yet
//     exist as a running adapter)
//   * Open + close session per BrowseAsync call (no caching)
//   * JSON error split per PR 5 amendment #1:
//        OPCUA.BROWSE_CONFIG_INVALID    — malformed JSON / deserialization failure
//        OPCUA.BROWSE_CONFIG_INCOMPLETE — missing browse-critical fields
//                                          (operator can fix from UI)
//   * StartingNodeId = null → root (Objects folder, i=85)
//   * MaxDepth / MaxNodes honored by tree builder
//   * Errors classified via PR 2's ClassifyConnectError pattern
//
// Reference: docs/decisions/0015-wizard-contract.md Rule 9
//            docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md §1.1, §5.1
//            PR 5 plan + amendments (user lock 2026-05-29)
// ============================================================================

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Browse;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace ElpisEdgeConnect.Sources.OpcUaClient;

/// <summary>
/// Per-protocol <see cref="ITagBrowseService"/> for OPC UA Client.
/// </summary>
public sealed class OpcUaClientBrowseService : ITagBrowseService
{
    private static readonly JsonSerializerOptions DeserializationOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger _logger;
    private readonly IOpcUaClientConnectionEstablisher _connectionEstablisher;
    private readonly Func<Opc.Ua.Client.ISession, IOpcUaBrowseExecutor> _executorFactory;

    /// <summary>
    /// Production constructor — wires the default session establisher
    /// and browse executor.
    /// </summary>
    public OpcUaClientBrowseService(ILogger<OpcUaClientBrowseService> logger)
        : this(
            logger,
            new DefaultOpcUaClientConnectionEstablisher(),
            session => new DefaultOpcUaBrowseExecutor(session))
    {
    }

    /// <summary>
    /// Test-friendly constructor — tests substitute the establisher and
    /// executor factory so unit tests cover orchestration without a
    /// live OPC stack.
    /// </summary>
    internal OpcUaClientBrowseService(
        ILogger logger,
        IOpcUaClientConnectionEstablisher connectionEstablisher,
        Func<Opc.Ua.Client.ISession, IOpcUaBrowseExecutor> executorFactory)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(connectionEstablisher);
        ArgumentNullException.ThrowIfNull(executorFactory);
        _logger = logger;
        _connectionEstablisher = connectionEstablisher;
        _executorFactory = executorFactory;
    }

    /// <inheritdoc/>
    public async Task<Core.Browse.BrowseResult> BrowseAsync(Core.Browse.BrowseRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Deserialize the operator's edited config. JSON shape errors
        //    surface as OPCUA.BROWSE_CONFIG_INVALID; missing browse-
        //    critical fields surface as OPCUA.BROWSE_CONFIG_INCOMPLETE
        //    (PR 5 amendment #1).
        var config = DeserializeBrowseConfig(request.SourceConfigJson);
        ValidateBrowseCriticalFields(config);

        // 2. Open a dedicated session for this browse call.
        Opc.Ua.Client.ISession? session = null;
        try
        {
            session = await _connectionEstablisher
                .EstablishAsync(config, sessionName: $"EdgeConnect.OpcUaClient.Browse.{Guid.NewGuid():N}", ct)
                .ConfigureAwait(false);

            // 3. Build the tree.
            var executor = _executorFactory(session);
            var builder = new OpcUaBrowseTreeBuilder(executor, request.MaxDepth, request.MaxNodes);

            var startingNodeId = string.IsNullOrWhiteSpace(request.StartingNodeId)
                ? ObjectIds.ObjectsFolder
                : new NodeId(request.StartingNodeId);
            var startingDisplayName = string.IsNullOrWhiteSpace(request.StartingNodeId)
                ? "Objects"
                : request.StartingNodeId!;

            return await builder.BuildAsync(startingNodeId, startingDisplayName, ct).ConfigureAwait(false);
        }
        finally
        {
            if (session is not null)
            {
                try { await session.CloseAsync(CancellationToken.None).ConfigureAwait(false); } catch { /* best-effort */ }
                try { session.Dispose(); } catch { /* best-effort */ }
            }
        }
    }

    /// <summary>
    /// Deserialize the operator's source config. Malformed JSON →
    /// <c>OPCUA.BROWSE_CONFIG_INVALID</c> (per PR 5 amendment #1).
    /// </summary>
    private static OpcUaClientSourceConfiguration DeserializeBrowseConfig(string sourceConfigJson)
    {
        if (string.IsNullOrWhiteSpace(sourceConfigJson))
        {
            throw new InvalidOperationException(
                "OPCUA.BROWSE_CONFIG_INVALID: SourceConfigJson is empty.");
        }

        try
        {
            var config = JsonSerializer.Deserialize<OpcUaClientSourceConfiguration>(sourceConfigJson, DeserializationOptions);
            return config ?? throw new InvalidOperationException(
                "OPCUA.BROWSE_CONFIG_INVALID: SourceConfigJson deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"OPCUA.BROWSE_CONFIG_INVALID: SourceConfigJson is malformed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Check browse-critical fields. Missing fields → <c>OPCUA.BROWSE_CONFIG_INCOMPLETE</c>
    /// per PR 5 amendment #1. Distinct from the structural
    /// <c>OPCUA.BROWSE_CONFIG_INVALID</c> case because the operator can
    /// fix INCOMPLETE from the wizard UI (just hasn't filled the field
    /// yet), whereas INVALID indicates a programmer error or corrupted
    /// state.
    /// </summary>
    private static void ValidateBrowseCriticalFields(OpcUaClientSourceConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.EndpointUrl))
        {
            throw new InvalidOperationException(
                "OPCUA.BROWSE_CONFIG_INCOMPLETE: EndpointUrl is required for browse. "
                + "Fill in the endpoint URL in the wizard before clicking Connect & Browse.");
        }

        if (config.AuthMode == OpcUaAuthMode.UserName
            && (config.Credentials is null
                || string.IsNullOrEmpty(config.Credentials.Username)
                || string.IsNullOrEmpty(config.Credentials.Password)))
        {
            throw new InvalidOperationException(
                "OPCUA.BROWSE_CONFIG_INCOMPLETE: AuthMode is UserName but credentials are missing. "
                + "Fill in username and password before clicking Connect & Browse.");
        }

        if (config.AuthMode == OpcUaAuthMode.Certificate
            && (config.Credentials is null || string.IsNullOrEmpty(config.Credentials.CertificatePath)))
        {
            throw new InvalidOperationException(
                "OPCUA.BROWSE_CONFIG_INCOMPLETE: AuthMode is Certificate but Credentials.CertificatePath is missing. "
                + "Fill in the certificate path before clicking Connect & Browse.");
        }
    }
}
