// ============================================================================
// File: Api/BulkSourceMerge/BulkSourceMergeService.cs
// Purpose: Orchestrator behind the bulk-source-merge wizard handlers.
//
//          Per v3 architecture amendment + v3.1 implementation lock, this
//          service MERGES N new sources into the CURRENT gateway config:
//          one draft per submit, not one gateway.json per row. Preview is
//          stateless and informational; Submit re-parses from scratch so
//          a hostile client can't forge a preview and ship a tampered
//          merged config (v3.1 sec1 submit-replay safety).
//
//          Composition:
//            1. BulkSourceMergeCsvParser   — structural CSV → rows
//            2. RowValidators              — per-cell semantic findings
//            3. TemplateSubstitutionEngine — JSON-safe placeholder substitution
//            4. JSON deserialize           — rendered text → SourceInstanceConfig + RouteConfig
//            5. Merge into existing config — append-only on Sources + Routes
//            6. BaseConfigHashComputer     — stale-preview guard (sec6)
//            7. IConfigurationSchemaValidator — final merged-config schema check
//            8. IConfigurationManager.CreateDraftAsync — only on submit
//
// Reference: docs/sessions/2026-06-14-bulk-provision-ui-phase1-v3.1-addendum.md
//            sections 1, 2, 3, 4, 5, 6, 7, 8
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Contracts.BulkSourceMerge;
using IConfigurationManager = ElpisEdgeConnect.Core.Configuration.IConfigurationManager;

namespace ElpisEdgeConnect.Management.Api.BulkSourceMerge;

/// <summary>
/// Service orchestrating CSV → typed-config → merged-draft for the
/// bulk-source-merge wizard. See file header for the composed pipeline.
/// </summary>
public sealed class BulkSourceMergeService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IConfigurationManager _configurationManager;
    private readonly IConfigurationSchemaValidator _schemaValidator;

    /// <summary>Construct with the configuration manager + schema validator from Core.</summary>
    public BulkSourceMergeService(
        IConfigurationManager configurationManager,
        IConfigurationSchemaValidator schemaValidator)
    {
        ArgumentNullException.ThrowIfNull(configurationManager);
        ArgumentNullException.ThrowIfNull(schemaValidator);
        _configurationManager = configurationManager;
        _schemaValidator = schemaValidator;
    }

    /// <summary>
    /// Preview the merge: parse + validate + compute base hash + resolve
    /// sink + simulate the merge + schema-validate. Stateless on the
    /// server; the response is informational only.
    /// </summary>
    public async Task<BulkSourceMergePreviewResponse> PreviewAsync(
        BulkSourceMergePreviewRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var currentConfig = await _configurationManager.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var baseConfigHash = BaseConfigHashComputer.Compute(currentConfig);

        var outcome = await RunMergeAsync(
            request.Protocol,
            request.CsvBytes,
            request.SelectedSinkInstanceId,
            currentConfig,
            cancellationToken).ConfigureAwait(false);

        var findings = new List<BulkSourceMergeFinding>(outcome.Findings);
        await AppendUnappliedDraftWarningAsync(findings, cancellationToken).ConfigureAwait(false);

        var canSubmit = !findings.Any(f => f.Severity == BulkSourceMergeSeverity.Error);

        return new BulkSourceMergePreviewResponse
        {
            BaseConfigHash = baseConfigHash,
            ChosenSinkInstanceId = outcome.ChosenSinkInstanceId ?? string.Empty,
            ParsedRowCount = outcome.ParsedRowCount,
            Findings = findings,
            CanSubmit = canSubmit,
        };
    }

    /// <summary>
    /// Submit the merge: per v3.1 sec1 we re-parse from the original CSV
    /// bytes, recompute every check, verify the base config hash hasn't
    /// drifted since preview, and only then call
    /// <see cref="IConfigurationManager.CreateDraftAsync"/>.
    /// </summary>
    public async Task<BulkSourceMergeSubmitResponse> SubmitAsync(
        BulkSourceMergeSubmitRequest request,
        string? actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var currentConfig = await _configurationManager.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var currentHash = BaseConfigHashComputer.Compute(currentConfig);

        if (!string.Equals(currentHash, request.BaseConfigHash, StringComparison.Ordinal))
        {
            return new BulkSourceMergeSubmitResponse
            {
                DraftId = null,
                Findings = new[]
                {
                    new BulkSourceMergeFinding
                    {
                        Code = BulkSourceMergeErrorCode.BaseConfigHashMismatch,
                        Severity = BulkSourceMergeSeverity.Error,
                        Message = "The current configuration changed since your preview (someone else applied a draft). Refresh the preview and review the new state before submitting.",
                    },
                },
            };
        }

        var outcome = await RunMergeAsync(
            request.Protocol,
            request.CsvBytes,
            request.SelectedSinkInstanceId,
            currentConfig,
            cancellationToken).ConfigureAwait(false);

        if (outcome.Findings.Any(f => f.Severity == BulkSourceMergeSeverity.Error) || outcome.MergedConfig is null)
        {
            return new BulkSourceMergeSubmitResponse
            {
                DraftId = null,
                Findings = outcome.Findings,
            };
        }

        var draftId = await _configurationManager.CreateDraftAsync(
            outcome.MergedConfig,
            actor,
            cancellationToken).ConfigureAwait(false);

        return new BulkSourceMergeSubmitResponse
        {
            DraftId = draftId.Value,
            Findings = outcome.Findings,
        };
    }

    // ── Merge pipeline ────────────────────────────────────────────────────────
    private async Task<MergeOutcome> RunMergeAsync(
        BulkSourceMergeProtocol protocol,
        byte[] csvBytes,
        string? selectedSinkInstanceId,
        GatewayConfiguration currentConfig,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(csvBytes);

        var entry = TemplateRegistry.Get(protocol);
        var findings = new List<BulkSourceMergeFinding>();

        // ── 1. Parse CSV
        var parseResult = BulkSourceMergeCsvParser.Parse(csvBytes, entry.RequiredCsvColumns);
        findings.AddRange(parseResult.Findings);
        var parsedRowCount = parseResult.Rows.Count;
        if (parseResult.Rows.Count == 0 || HasBlocker(findings))
        {
            return MergeOutcome.Aborted(findings, parsedRowCount);
        }

        // ── 2. Per-row semantic validation + collect intra-CSV duplicates
        var rowAddresses = new Dictionary<int, string>(parseResult.Rows.Count);
        var rowDeviceIds = new Dictionary<int, string>(parseResult.Rows.Count);
        var rowDeviceNames = new Dictionary<int, string>(parseResult.Rows.Count);
        var rowEnabled = new Dictionary<int, string>(parseResult.Rows.Count);
        var rowsBlocked = new HashSet<int>();
        foreach (var row in parseResult.Rows)
        {
            row.Cells.TryGetValue("deviceId", out var deviceId);
            row.Cells.TryGetValue("deviceName", out var deviceName);
            row.Cells.TryGetValue("enabled", out var enabledValue);
            row.Cells.TryGetValue(entry.AddressColumnName, out var addressValue);
            deviceId ??= string.Empty;
            deviceName ??= string.Empty;
            enabledValue ??= string.Empty;
            addressValue ??= string.Empty;

            var rowBlocked = false;

            var idFinding = RowValidators.ValidateDeviceId(deviceId, row.LineNumber);
            if (idFinding is not null) { findings.Add(idFinding); rowBlocked = true; }

            var nameFinding = RowValidators.ValidateRequiredCellNonEmpty("deviceName", deviceName, row.LineNumber);
            if (nameFinding is not null) { findings.Add(nameFinding); rowBlocked = true; }

            var enabledFinding = RowValidators.ValidateEnabledValue(enabledValue, row.LineNumber);
            if (enabledFinding is not null) { findings.Add(enabledFinding); rowBlocked = true; }

            BulkSourceMergeFinding? addressFinding = entry.Protocol switch
            {
                BulkSourceMergeProtocol.Mtconnect => RowValidators.ValidateMtConnectBaseUrl(addressValue, row.LineNumber),
                _                                 => RowValidators.ValidateRequiredCellNonEmpty(entry.AddressColumnName, addressValue, row.LineNumber),
            };
            if (addressFinding is not null) { findings.Add(addressFinding); rowBlocked = true; }

            rowDeviceIds[row.LineNumber] = deviceId;
            rowDeviceNames[row.LineNumber] = deviceName;
            rowAddresses[row.LineNumber] = addressValue;
            rowEnabled[row.LineNumber] = enabledValue;
            if (rowBlocked)
            {
                rowsBlocked.Add(row.LineNumber);
            }
        }

        // ── 3. Intra-CSV duplicate detection
        DetectDuplicateDeviceIds(parseResult.Rows, rowDeviceIds, findings, rowsBlocked);
        DetectDuplicateDeviceNames(parseResult.Rows, rowDeviceNames, findings);

        // ── 4. Sink selection
        var sinkResolution = ResolveSink(currentConfig, selectedSinkInstanceId);
        if (sinkResolution.Finding is not null)
        {
            findings.Add(sinkResolution.Finding);
        }
        var chosenSinkInstanceId = sinkResolution.ChosenSinkInstanceId;

        if (HasBlocker(findings) || chosenSinkInstanceId is null)
        {
            return MergeOutcome.Aborted(findings, parsedRowCount, chosenSinkInstanceId);
        }

        // ── 5. Template substitution + deserialization for clean rows
        var sourceEngine = new TemplateSubstitutionEngine(entry.SourcePlaceholders);
        var routeEngine = new TemplateSubstitutionEngine(entry.RoutePlaceholders);
        var sinkLabel = SinkLabelFor(currentConfig, chosenSinkInstanceId);
        var newSources = new List<SourceInstanceConfig>(parseResult.Rows.Count);
        var newRoutes = new List<RouteConfig>(parseResult.Rows.Count);

        foreach (var row in parseResult.Rows)
        {
            if (rowsBlocked.Contains(row.LineNumber)) continue;
            var deviceId = rowDeviceIds[row.LineNumber];
            var deviceName = rowDeviceNames[row.LineNumber];
            var address = rowAddresses[row.LineNumber];
            var enabledValue = rowEnabled[row.LineNumber];
            var instanceId = $"{deviceId}-source";
            var routeId = $"route-{deviceId}";
            var routeName = $"{deviceName} to {sinkLabel}";

            var sourceValues = new Dictionary<string, string>
            {
                ["instanceId"]                = instanceId,
                ["deviceId"]                  = deviceId,
                ["deviceName"]                = deviceName,
                ["enabled"]                   = enabledValue,
                [entry.AddressPlaceholderName] = address,
            };
            string sourceJson;
            try
            {
                sourceJson = sourceEngine.Render(entry.SourceTemplate, sourceValues);
            }
            catch (TemplateSubstitutionException ex)
            {
                findings.Add(new BulkSourceMergeFinding
                {
                    Code = ex.ErrorCode,
                    Severity = BulkSourceMergeSeverity.Error,
                    Message = ex.Message,
                    CsvRow = row.LineNumber,
                });
                continue;
            }

            var source = JsonSerializer.Deserialize<SourceInstanceConfig>(sourceJson, JsonOptions)
                         ?? throw new InvalidOperationException("Rendered source JSON deserialized to null — should not happen for a valid template.");
            newSources.Add(source);

            var routeValues = new Dictionary<string, string>
            {
                ["routeId"]        = routeId,
                ["routeName"]      = routeName,
                ["instanceId"]     = instanceId,
                ["sinkInstanceId"] = chosenSinkInstanceId,
            };
            string routeJson;
            try
            {
                routeJson = routeEngine.Render(entry.RouteTemplate, routeValues);
            }
            catch (TemplateSubstitutionException ex)
            {
                findings.Add(new BulkSourceMergeFinding
                {
                    Code = ex.ErrorCode,
                    Severity = BulkSourceMergeSeverity.Error,
                    Message = ex.Message,
                    CsvRow = row.LineNumber,
                });
                continue;
            }

            var route = JsonSerializer.Deserialize<RouteConfig>(routeJson, JsonOptions)
                        ?? throw new InvalidOperationException("Rendered route JSON deserialized to null — should not happen for a valid template.");
            newRoutes.Add(route);
        }

        // ── 6. Collision detection vs current config
        DetectCollisionsAgainstCurrent(currentConfig, newSources, newRoutes, findings);

        if (HasBlocker(findings))
        {
            return MergeOutcome.Aborted(findings, parsedRowCount, chosenSinkInstanceId);
        }

        // ── 7. Build merged config + schema-validate
        var mergedConfig = currentConfig with
        {
            Sources = currentConfig.Sources.Concat(newSources).ToList(),
            Routes = currentConfig.Routes.Concat(newRoutes).ToList(),
        };

        var mergedJson = JsonSerializer.Serialize(mergedConfig);
        var schemaResult = await _schemaValidator.ValidateAsync(mergedJson, cancellationToken).ConfigureAwait(false);
        if (!schemaResult.IsValid)
        {
            var firstError = schemaResult.Errors.Count > 0 ? schemaResult.Errors[0] : null;
            findings.Add(new BulkSourceMergeFinding
            {
                Code = BulkSourceMergeErrorCode.MergedConfigSchemaViolation,
                Severity = BulkSourceMergeSeverity.Error,
                Message = firstError is not null
                    ? $"Merged configuration failed schema validation: {firstError.Code} — {firstError.Message}."
                    : "Merged configuration failed schema validation.",
            });
            return MergeOutcome.Aborted(findings, parsedRowCount, chosenSinkInstanceId);
        }

        return new MergeOutcome
        {
            Findings = findings,
            ParsedRowCount = parsedRowCount,
            ChosenSinkInstanceId = chosenSinkInstanceId,
            MergedConfig = mergedConfig,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static bool HasBlocker(IEnumerable<BulkSourceMergeFinding> findings) =>
        findings.Any(f => f.Severity == BulkSourceMergeSeverity.Error);

    private static void DetectDuplicateDeviceIds(
        IReadOnlyList<ParsedCsvRow> rows,
        Dictionary<int, string> rowDeviceIds,
        List<BulkSourceMergeFinding> findings,
        HashSet<int> rowsBlocked)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var id = rowDeviceIds[row.LineNumber];
            if (string.IsNullOrEmpty(id)) continue;
            if (seen.TryGetValue(id, out var firstLine))
            {
                findings.Add(new BulkSourceMergeFinding
                {
                    Code = BulkSourceMergeErrorCode.CsvDuplicateDeviceId,
                    Severity = BulkSourceMergeSeverity.Error,
                    Message = $"deviceId '{id}' appears on multiple rows (first on line {firstLine}, again on line {row.LineNumber}). Each row must have a unique deviceId.",
                    CsvRow = row.LineNumber,
                    Subject = id,
                });
                rowsBlocked.Add(row.LineNumber);
            }
            else
            {
                seen[id] = row.LineNumber;
            }
        }
    }

    private static void DetectDuplicateDeviceNames(
        IReadOnlyList<ParsedCsvRow> rows,
        Dictionary<int, string> rowDeviceNames,
        List<BulkSourceMergeFinding> findings)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var name = rowDeviceNames[row.LineNumber];
            if (string.IsNullOrEmpty(name)) continue;
            if (seen.TryGetValue(name, out var firstLine))
            {
                findings.Add(new BulkSourceMergeFinding
                {
                    Code = BulkSourceMergeErrorCode.DuplicateDeviceName,
                    Severity = BulkSourceMergeSeverity.Warning,
                    Message = $"deviceName '{name}' appears on multiple rows (first on line {firstLine}, again on line {row.LineNumber}). Names do not need to be unique, but operators usually want to disambiguate.",
                    CsvRow = row.LineNumber,
                    Subject = name,
                });
            }
            else
            {
                seen[name] = row.LineNumber;
            }
        }
    }

    private static SinkResolution ResolveSink(GatewayConfiguration currentConfig, string? selectedSinkInstanceId)
    {
        var enabledMqttSinks = currentConfig.Sinks
            .Where(s => string.Equals(s.ProtocolName, "mqtt", StringComparison.Ordinal) && s.Enabled)
            .ToList();

        if (enabledMqttSinks.Count == 0)
        {
            return new SinkResolution
            {
                ChosenSinkInstanceId = null,
                Finding = new BulkSourceMergeFinding
                {
                    Code = BulkSourceMergeErrorCode.NoMqttSink,
                    Severity = BulkSourceMergeSeverity.Error,
                    Message = "This gateway has no enabled MQTT sink. Add an MQTT sink (Sinks page > Add destination) before running bulk source import.",
                },
            };
        }

        if (enabledMqttSinks.Count == 1)
        {
            return new SinkResolution { ChosenSinkInstanceId = enabledMqttSinks[0].InstanceId, Finding = null };
        }

        // 2+ sinks — require explicit selection
        if (string.IsNullOrEmpty(selectedSinkInstanceId))
        {
            var ids = string.Join(", ", enabledMqttSinks.Select(s => s.InstanceId));
            return new SinkResolution
            {
                ChosenSinkInstanceId = null,
                Finding = new BulkSourceMergeFinding
                {
                    Code = BulkSourceMergeErrorCode.SinkSelectionRequired,
                    Severity = BulkSourceMergeSeverity.Error,
                    Message = $"This gateway has {enabledMqttSinks.Count} enabled MQTT sinks ({ids}). Select one to route the new sources to.",
                },
            };
        }

        var match = enabledMqttSinks.FirstOrDefault(s => string.Equals(s.InstanceId, selectedSinkInstanceId, StringComparison.Ordinal));
        if (match is null)
        {
            return new SinkResolution
            {
                ChosenSinkInstanceId = null,
                Finding = new BulkSourceMergeFinding
                {
                    Code = BulkSourceMergeErrorCode.SinkSelectionRequired,
                    Severity = BulkSourceMergeSeverity.Error,
                    Message = $"Selected sink '{selectedSinkInstanceId}' is not an enabled MQTT sink on this gateway.",
                    Subject = selectedSinkInstanceId,
                },
            };
        }

        return new SinkResolution { ChosenSinkInstanceId = match.InstanceId, Finding = null };
    }

    private static string SinkLabelFor(GatewayConfiguration currentConfig, string sinkInstanceId)
    {
        // No human-readable Name field on SinkInstanceConfig in Core; use the
        // instance id as the route-name label — same shape the offline
        // generator effectively produces via its hardcoded names.
        _ = currentConfig;
        return sinkInstanceId;
    }

    private static void DetectCollisionsAgainstCurrent(
        GatewayConfiguration currentConfig,
        IReadOnlyList<SourceInstanceConfig> newSources,
        IReadOnlyList<RouteConfig> newRoutes,
        List<BulkSourceMergeFinding> findings)
    {
        var existingInstanceIds = new HashSet<string>(
            currentConfig.Sources.Select(s => s.InstanceId),
            StringComparer.Ordinal);
        var existingDeviceIds = new HashSet<string>(
            currentConfig.Sources.Select(s => s.DeviceId),
            StringComparer.Ordinal);
        var existingRouteIds = new HashSet<string>(
            currentConfig.Routes.Select(r => r.RouteId),
            StringComparer.Ordinal);
        var existingRouteIdsCaseInsensitive = new HashSet<string>(
            currentConfig.Routes.Select(r => r.RouteId),
            StringComparer.OrdinalIgnoreCase);
        var existingRouteNames = new HashSet<string>(
            currentConfig.Routes.Select(r => r.Name),
            StringComparer.Ordinal);

        foreach (var src in newSources)
        {
            if (existingInstanceIds.Contains(src.InstanceId))
            {
                findings.Add(new BulkSourceMergeFinding
                {
                    Code = BulkSourceMergeErrorCode.SourceInstanceIdCollision,
                    Severity = BulkSourceMergeSeverity.Error,
                    Message = $"Generated Source.InstanceId '{src.InstanceId}' already exists on this gateway. Resolve the deviceId conflict before submitting.",
                    Subject = src.InstanceId,
                });
            }
            if (existingDeviceIds.Contains(src.DeviceId))
            {
                findings.Add(new BulkSourceMergeFinding
                {
                    Code = BulkSourceMergeErrorCode.SourceDeviceIdCollision,
                    Severity = BulkSourceMergeSeverity.Error,
                    Message = $"Source.DeviceId '{src.DeviceId}' already exists on this gateway. The same physical device cannot be imported twice.",
                    Subject = src.DeviceId,
                });
            }
        }

        foreach (var rt in newRoutes)
        {
            if (existingRouteIds.Contains(rt.RouteId) || existingRouteIdsCaseInsensitive.Contains(rt.RouteId))
            {
                findings.Add(new BulkSourceMergeFinding
                {
                    Code = BulkSourceMergeErrorCode.RouteIdCollision,
                    Severity = BulkSourceMergeSeverity.Error,
                    Message = $"Generated RouteId '{rt.RouteId}' already exists on this gateway.",
                    Subject = rt.RouteId,
                });
            }
            if (existingRouteNames.Contains(rt.Name))
            {
                findings.Add(new BulkSourceMergeFinding
                {
                    Code = BulkSourceMergeErrorCode.DuplicateRouteName,
                    Severity = BulkSourceMergeSeverity.Warning,
                    Message = $"Route Name '{rt.Name}' matches an existing route's Name. The RouteId / SourceInstanceId checks guarantee uniqueness; the name match is informational.",
                    Subject = rt.Name,
                });
            }
        }
    }

    private async Task AppendUnappliedDraftWarningAsync(
        List<BulkSourceMergeFinding> findings,
        CancellationToken cancellationToken)
    {
        var existing = await _configurationManager.ListDraftsAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Count > 0)
        {
            findings.Add(new BulkSourceMergeFinding
            {
                Code = BulkSourceMergeErrorCode.UnappliedDraftExists,
                Severity = BulkSourceMergeSeverity.Warning,
                Message = $"An unapplied draft already exists for this gateway ({existing.Count} draft(s)). Submitting will create another draft alongside the existing one(s); you'll need to choose which to apply.",
            });
        }
    }

    private sealed record SinkResolution
    {
        public required string? ChosenSinkInstanceId { get; init; }
        public required BulkSourceMergeFinding? Finding { get; init; }
    }

    private sealed record MergeOutcome
    {
        public required IReadOnlyList<BulkSourceMergeFinding> Findings { get; init; }
        public required int ParsedRowCount { get; init; }
        public string? ChosenSinkInstanceId { get; init; }
        public GatewayConfiguration? MergedConfig { get; init; }

        public static MergeOutcome Aborted(
            IReadOnlyList<BulkSourceMergeFinding> findings,
            int parsedRowCount,
            string? chosenSinkInstanceId = null) => new()
            {
                Findings = findings,
                ParsedRowCount = parsedRowCount,
                ChosenSinkInstanceId = chosenSinkInstanceId,
                MergedConfig = null,
            };
    }
}
