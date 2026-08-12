// ============================================================================
// File: Configuration/ConfigurationManager.cs
// Purpose: Production IConfigurationManager implementation. Owns the draft
//          lifecycle, atomic apply sequence, history, and audit log.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone B2, ARCHITECTURE_BLUEPRINT.md §8.2
//
// Atomic apply sequence (assumption 6 from the B2 pre-generation review):
//   1. Acquire mutex
//   2. Validate draft (full pipeline)
//   3. Compute diff against current
//   4. Compute new version id
//   5. Compute next audit hash
//   6. Build the audit entry
//   7. Write the OLD config to history (history/{prevVersionId}.json)
//   8. Atomically replace current.json (durable commit point)
//   9. Append the audit line (only after current.json is on disk)
//  10. Promote in-memory state
//  11. Best-effort: delete the draft file (failures swallowed)
//  12. Best-effort: run retention pruning (failures swallowed)
//  12. Emit CurrentChanged
//  13. Release mutex
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Errors;

// Disambiguate ValidationResult.
using ValidationResult = ElpisEdgeConnect.Core.Adapters.ValidationResult;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// File-backed implementation of <see cref="IConfigurationManager"/>.
/// All mutating methods are serialized through a single
/// <see cref="SemaphoreSlim"/> so concurrent applies cannot corrupt
/// history or the audit log.
/// </summary>
public sealed class ConfigurationManager : IConfigurationManager, IGatewayAuditWriter, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IConfigurationStore _store;
    private readonly IConfigurationSchemaValidator _schemaValidator;
    private readonly ConfigurationValidator _validator;
    private readonly ConfigurationDiffer _differ;
    private readonly ConfigurationAuditLog _auditLog;
    private readonly HistoryRetentionPolicy _retentionPolicy;
    private readonly Func<string> _hostnameProvider;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    private GatewayConfiguration? _current;
    private ConfigurationVersionId _currentVersionId;
    private bool _initialized;

    /// <summary>
    /// Construct a manager. Production code uses the parameterless
    /// constructor or the <see cref="FileSystemConfigurationStore"/> path;
    /// tests inject a stub store and validators.
    /// </summary>
    /// <param name="store">The configuration store.</param>
    /// <param name="schemaValidator">Optional schema validator (defaults to no-op).</param>
    /// <param name="validator">Optional typed validator.</param>
    /// <param name="retentionPolicy">Optional history retention policy.</param>
    /// <param name="hostnameProvider">
    /// Optional override for the hostname used when auto-provisioning a
    /// seed config (ADR-0016 Rule 5). Defaults to
    /// <see cref="Environment.MachineName"/>. Tests inject a deterministic
    /// value to keep auto-provision behaviour reproducible.
    /// </param>
    public ConfigurationManager(
        IConfigurationStore store,
        IConfigurationSchemaValidator? schemaValidator = null,
        ConfigurationValidator? validator = null,
        HistoryRetentionPolicy? retentionPolicy = null,
        Func<string>? hostnameProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _schemaValidator = schemaValidator ?? NoOpConfigurationSchemaValidator.Instance;
        _validator = validator ?? new ConfigurationValidator();
        _differ = ConfigurationDiffer.Instance;
        _auditLog = new ConfigurationAuditLog(store);
        _retentionPolicy = retentionPolicy ?? HistoryRetentionPolicy.Default;
        _hostnameProvider = hostnameProvider ?? (() => Environment.MachineName);
        _currentVersionId = ConfigurationVersionId.Initial;
    }

    /// <inheritdoc/>
    public ConfigurationVersionId CurrentVersionId
    {
        get
        {
            EnsureInitialized();
            return _currentVersionId;
        }
    }

    /// <inheritdoc/>
    public event EventHandler<ConfigurationChangeEventArgs>? CurrentChanged;

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            var currentJson = await _store.ReadCurrentAsync(cancellationToken).ConfigureAwait(false);
            if (currentJson is null)
            {
                // ADR-0016 Rule 5 — first-run self-provisioning. No current.json
                // on disk means this is a brand-new gateway. Build a minimal
                // empty-state seed with a hostname-derived GatewayId so Studio
                // can launch and the operator's first action via the onboarding
                // flow becomes the v1 applied config.
                await AutoProvisionSeedAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            // Stage 1: schema validation against the raw JSON
            var schemaResult = await _schemaValidator.ValidateAsync(currentJson, cancellationToken).ConfigureAwait(false);
            if (!schemaResult.IsValid)
            {
                throw new ConfigurationValidationException(
                    CoreErrors.ConfigFileCorrupt,
                    "Active configuration failed schema validation at startup. " +
                    "See ValidationResult.Errors for details.",
                    schemaResult);
            }

            // Stage 2: deserialize
            GatewayConfiguration loaded;
            try
            {
                loaded = JsonSerializer.Deserialize<GatewayConfiguration>(currentJson, JsonOptions)
                    ?? throw new JsonException("Configuration deserialized to null.");
            }
            catch (JsonException ex)
            {
                throw new ConfigurationException(
                    CoreErrors.ConfigFileCorrupt,
                    $"Active configuration JSON could not be deserialized: {ex.Message}",
                    ex);
            }

            // Stage 3: typed validation
            var typedResult = await _validator.ValidateAsync(loaded, cancellationToken).ConfigureAwait(false);
            if (!typedResult.IsValid)
            {
                throw new ConfigurationValidationException(
                    CoreErrors.ConfigFileCorrupt,
                    "Active configuration failed typed validation at startup. " +
                    "See ValidationResult.Errors for details.",
                    typedResult);
            }

            _current = loaded;
            _currentVersionId = await DiscoverCurrentVersionIdAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <summary>
    /// Build and write a minimal empty-state seed config when no
    /// <c>current.json</c> exists at startup. Called from
    /// <see cref="InitializeAsync"/> under the mutex. Appends a
    /// <see cref="ConfigurationAuditAction.AutoProvisioned"/> entry
    /// to the audit chain so the gateway's lifetime history starts
    /// from a recorded event rather than silently. ADR-0016 Rule 5.
    /// </summary>
    private async Task AutoProvisionSeedAsync(CancellationToken cancellationToken)
    {
        var hostname = _hostnameProvider();
        var seed = BuildAutoProvisionedSeed(hostname);
        var seedJson = JsonSerializer.Serialize(seed, JsonOptions);

        // Defence-in-depth — the seed is hand-built but we still run the
        // typed validator so a future refactor that breaks the seed shape
        // surfaces immediately instead of leaving a corrupt config on disk.
        var typedResult = await _validator.ValidateAsync(seed, cancellationToken).ConfigureAwait(false);
        if (!typedResult.IsValid)
        {
            throw new ConfigurationException(
                CoreErrors.RuntimeInternalError,
                "Auto-provisioned seed config failed validation. This is a Core bug. " +
                "See ValidationResult.Errors for details.");
        }

        await _store.WriteCurrentAsync(seedJson, cancellationToken).ConfigureAwait(false);

        // Audit entry — first version is genesis, no previous version,
        // no diff (empty state has no changes to record).
        var previousHash = await _auditLog.ComputeNextPreviousHashAsync(cancellationToken).ConfigureAwait(false);
        var auditEntry = new ConfigurationAuditEntry
        {
            Timestamp = DateTime.UtcNow,
            VersionId = ConfigurationVersionId.Initial,
            PreviousVersionId = null,
            Action = ConfigurationAuditAction.AutoProvisioned,
            Actor = "system",
            DraftId = null,
            Summary = $"Auto-provisioned empty seed config. " +
                      $"GatewayId='{seed.Gateway.GatewayId}', hostname='{hostname}'.",
            Changes = [],
            RuntimeFault = null,
            PreviousHash = previousHash,
        };
        await _auditLog.AppendAsync(auditEntry, cancellationToken).ConfigureAwait(false);

        // Soft-log to stderr so operators see the event in the startup
        // console without needing to dig into the audit log. Matches the
        // existing [startup] / [config] stderr-logging convention.
        Console.Error.WriteLine(
            $"[config] current.json not found at startup; auto-provisioned empty seed " +
            $"with GatewayId='{seed.Gateway.GatewayId}'. " +
            $"Configure sources/sinks/routes via the onboarding flow.");

        _current = seed;
        _currentVersionId = ConfigurationVersionId.Initial;
        _initialized = true;
    }

    /// <summary>
    /// Build the seed <see cref="GatewayConfiguration"/> used for first-run
    /// self-provisioning. Hostname is slugified to satisfy
    /// <see cref="GatewaySettings.GatewayId"/>'s regex constraint
    /// (<c>^[A-Za-z0-9][A-Za-z0-9._-]*$</c>).
    /// </summary>
    internal static GatewayConfiguration BuildAutoProvisionedSeed(string hostname)
    {
        var slug = SlugifyHostname(hostname);
        return new GatewayConfiguration
        {
            Gateway = new GatewaySettings
            {
                GatewayId = $"gw-{slug}",
                GatewayName = $"EdgeConnect on {hostname}",
            },
            // Sources, Sinks, Routes default to empty arrays.
        };
    }

    /// <summary>
    /// Convert a hostname into a slug suitable for use inside
    /// <see cref="GatewaySettings.GatewayId"/>. Rules:
    ///   * Lowercase.
    ///   * Replace any character outside <c>[a-z0-9._-]</c> with '-'.
    ///   * Strip leading non-alphanumerics (GatewayId must start with letter or digit;
    ///     the <c>"gw-"</c> prefix added by the caller already satisfies this, but
    ///     we strip here too to keep the slug itself well-formed).
    ///   * Collapse runs of '-' into a single '-' for readability.
    ///   * Truncate to 100 chars to leave headroom under the 128-char id limit
    ///     after the <c>"gw-"</c> prefix.
    ///   * If the hostname is null/empty/whitespace, fall back to <c>"host"</c>.
    /// </summary>
    internal static string SlugifyHostname(string? hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname))
        {
            return "host";
        }

        var lower = hostname.Trim().ToLowerInvariant();
        var chars = new System.Text.StringBuilder(lower.Length);
        var lastWasDash = false;
        foreach (var ch in lower)
        {
            var ok = (ch >= 'a' && ch <= 'z')
                || (ch >= '0' && ch <= '9')
                || ch == '.'
                || ch == '_'
                || ch == '-';
            if (ok)
            {
                if (ch == '-')
                {
                    if (lastWasDash) { continue; }
                    lastWasDash = true;
                }
                else
                {
                    lastWasDash = false;
                }
                chars.Append(ch);
            }
            else
            {
                if (!lastWasDash)
                {
                    chars.Append('-');
                    lastWasDash = true;
                }
            }
        }

        var slug = chars.ToString();
        // Strip leading non-alphanumerics
        var start = 0;
        while (start < slug.Length && !(char.IsLetterOrDigit(slug[start])))
        {
            start++;
        }
        slug = slug.Substring(start);
        if (slug.Length == 0) { return "host"; }
        if (slug.Length > 100) { slug = slug.Substring(0, 100); }
        // Trailing dash trim
        slug = slug.TrimEnd('-', '.', '_');
        if (slug.Length == 0) { return "host"; }
        return slug;
    }

    /// <inheritdoc/>
    public ValueTask<GatewayConfiguration> GetCurrentAsync(CancellationToken cancellationToken)
    {
        EnsureInitialized();
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<GatewayConfiguration>(_current!);
    }

    /// <inheritdoc/>
    public async Task<DraftId> CreateDraftAsync(
        GatewayConfiguration draft,
        string? actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        EnsureInitialized();

        var draftId = DraftId.NewId();
        var json = JsonSerializer.Serialize(draft, JsonOptions);

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _store.WriteDraftAsync(draftId, json, cancellationToken).ConfigureAwait(false);

            var entry = new ConfigurationAuditEntry
            {
                Timestamp = DateTime.UtcNow,
                VersionId = ConfigurationVersionId.Initial,
                PreviousVersionId = null,
                Action = ConfigurationAuditAction.DraftCreated,
                Actor = actor ?? "system",
                DraftId = draftId,
                Summary = $"Draft {draftId.Value} created",
                Changes = [],
                PreviousHash = await _auditLog.ComputeNextPreviousHashAsync(cancellationToken).ConfigureAwait(false),
            };
            await _auditLog.AppendAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }

        return draftId;
    }

    /// <inheritdoc/>
    public async Task<GatewayConfiguration?> GetDraftAsync(DraftId draftId, CancellationToken cancellationToken)
    {
        EnsureInitialized();
        var json = await _store.ReadDraftAsync(draftId, cancellationToken).ConfigureAwait(false);
        if (json is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<GatewayConfiguration>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // Drafts can be invalid; the user might be iterating. Return
            // null so the caller can choose to discard or fix.
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DraftId>> ListDraftsAsync(CancellationToken cancellationToken)
    {
        EnsureInitialized();
        var ids = await _store.ListDraftsAsync(cancellationToken).ConfigureAwait(false);
        return ids;
    }

    /// <inheritdoc/>
    public async Task<ValidationResult> ValidateDraftAsync(DraftId draftId, CancellationToken cancellationToken)
    {
        EnsureInitialized();

        var json = await _store.ReadDraftAsync(draftId, cancellationToken).ConfigureAwait(false);
        if (json is null)
        {
            return ValidationResult.Failure(
                CoreErrors.ConfigFileNotFound,
                $"Draft '{draftId.Value}' does not exist.");
        }

        var schemaResult = await _schemaValidator.ValidateAsync(json, cancellationToken).ConfigureAwait(false);
        if (!schemaResult.IsValid)
        {
            return schemaResult;
        }

        GatewayConfiguration draft;
        try
        {
            draft = JsonSerializer.Deserialize<GatewayConfiguration>(json, JsonOptions)
                ?? throw new JsonException("Draft deserialized to null.");
        }
        catch (JsonException ex)
        {
            return ValidationResult.Failure(
                CoreErrors.ConfigFileCorrupt,
                $"Draft JSON could not be deserialized: {ex.Message}");
        }

        return await _validator.ValidateAsync(draft, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ConfigurationApplyResult> ApplyDraftAsync(
        DraftId draftId,
        string? actor,
        CancellationToken cancellationToken)
    {
        EnsureInitialized();

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-read inside the mutex so we always work against the latest state.
            var json = await _store.ReadDraftAsync(draftId, cancellationToken).ConfigureAwait(false);
            if (json is null)
            {
                return ConfigurationApplyResult.Failed(
                    ValidationResult.Failure(
                        CoreErrors.ConfigFileNotFound,
                        $"Draft '{draftId.Value}' does not exist."));
            }

            // Schema stage
            var schemaResult = await _schemaValidator.ValidateAsync(json, cancellationToken).ConfigureAwait(false);
            if (!schemaResult.IsValid)
            {
                return ConfigurationApplyResult.Failed(schemaResult);
            }

            GatewayConfiguration newConfig;
            try
            {
                newConfig = JsonSerializer.Deserialize<GatewayConfiguration>(json, JsonOptions)
                    ?? throw new JsonException("Draft deserialized to null.");
            }
            catch (JsonException ex)
            {
                return ConfigurationApplyResult.Failed(
                    ValidationResult.Failure(
                        CoreErrors.ConfigFileCorrupt,
                        $"Draft JSON could not be deserialized: {ex.Message}"));
            }

            // Typed stages (DataAnnotations + cross-record + license)
            var validationResult = await _validator.ValidateAsync(newConfig, cancellationToken).ConfigureAwait(false);
            if (!validationResult.IsValid)
            {
                return ConfigurationApplyResult.Failed(validationResult);
            }

            // Compute diff and version
            var changes = _differ.Diff(_current, newConfig);
            var newVersionId = ConfigurationVersionId.NewId();
            var prevVersionId = _currentVersionId;
            var prevConfig = _current;

            // Build the audit entry
            var prevHash = await _auditLog.ComputeNextPreviousHashAsync(cancellationToken).ConfigureAwait(false);
            var entry = new ConfigurationAuditEntry
            {
                Timestamp = DateTime.UtcNow,
                VersionId = newVersionId,
                PreviousVersionId = prevVersionId.IsEmpty ? null : prevVersionId,
                Action = ConfigurationAuditAction.Applied,
                Actor = actor ?? "system",
                DraftId = draftId,
                Summary = ConfigurationDiffer.Summarize(changes),
                Changes = changes,
                PreviousHash = prevHash,
            };

            // Atomic write sequence — visibility-defining order:
            //   1. Write previous to history
            //   2. WriteCurrentAsync (the durable commit point)
            //   3. AppendAuditLine (only after current.json is on disk)
            //   4. Promote in-memory state
            //   5. Best-effort cleanup (delete draft, prune)
            // A crash between steps 2 and 3 leaves a current.json that has no
            // audit row — recoverable on next init. The reverse (audit but no
            // current.json) would create a ghost version id.
            if (prevConfig is not null && !prevVersionId.IsEmpty)
            {
                var prevJson = JsonSerializer.Serialize(prevConfig, JsonOptions);
                await _store.WriteHistoryAsync(prevVersionId, prevJson, cancellationToken).ConfigureAwait(false);
            }
            await _store.WriteCurrentAsync(json, cancellationToken).ConfigureAwait(false);
            await _auditLog.AppendAsync(entry, cancellationToken).ConfigureAwait(false);

            // Promote in-memory state BEFORE best-effort cleanup so a failure
            // in DeleteDraftAsync cannot leave the manager with stale state.
            _current = newConfig;
            _currentVersionId = newVersionId;

            // Best-effort: delete the now-applied draft. An orphan draft file
            // is harmless; the next ListDrafts will surface it for cleanup.
            try
            {
                await _store.DeleteDraftAsync(draftId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Swallow — apply has already succeeded.
            }

            // Best-effort retention. If pruning fails, the next apply catches up.
            try
            {
                await PruneHistoryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Swallow — retention is not load-bearing for correctness.
            }

            // Emit event (outside the mutex would be cleaner but emitting
            // inside is acceptable for B2 since subscribers are required
            // to be cheap; C3+ may revisit).
            CurrentChanged?.Invoke(this, new ConfigurationChangeEventArgs(prevVersionId, newVersionId, newConfig, changes));

            return new ConfigurationApplyResult
            {
                Success = true,
                VersionId = newVersionId,
                ValidationResult = validationResult,
                AuditEntry = entry,
            };
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc/>
    public async Task DiscardDraftAsync(DraftId draftId, string? actor, CancellationToken cancellationToken)
    {
        EnsureInitialized();

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existed = (await _store.ReadDraftAsync(draftId, cancellationToken).ConfigureAwait(false)) is not null;
            await _store.DeleteDraftAsync(draftId, cancellationToken).ConfigureAwait(false);

            if (existed)
            {
                var entry = new ConfigurationAuditEntry
                {
                    Timestamp = DateTime.UtcNow,
                    VersionId = ConfigurationVersionId.Initial,
                    PreviousVersionId = null,
                    Action = ConfigurationAuditAction.DraftDiscarded,
                    Actor = actor ?? "system",
                    DraftId = draftId,
                    Summary = $"Draft {draftId.Value} discarded",
                    Changes = [],
                    PreviousHash = await _auditLog.ComputeNextPreviousHashAsync(cancellationToken).ConfigureAwait(false),
                };
                await _auditLog.AppendAsync(entry, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<ConfigurationApplyResult> RollbackAsync(
        ConfigurationVersionId targetVersionId,
        string? actor,
        CancellationToken cancellationToken)
    {
        EnsureInitialized();

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var json = await _store.ReadHistoryAsync(targetVersionId, cancellationToken).ConfigureAwait(false);
            if (json is null)
            {
                return ConfigurationApplyResult.Failed(
                    ValidationResult.Failure(
                        CoreErrors.ConfigFileNotFound,
                        $"History version '{targetVersionId.Value}' does not exist."));
            }

            // Validate the rolled-back config against current rules.
            var schemaResult = await _schemaValidator.ValidateAsync(json, cancellationToken).ConfigureAwait(false);
            if (!schemaResult.IsValid)
            {
                return ConfigurationApplyResult.Failed(schemaResult);
            }

            GatewayConfiguration target;
            try
            {
                target = JsonSerializer.Deserialize<GatewayConfiguration>(json, JsonOptions)
                    ?? throw new JsonException("History entry deserialized to null.");
            }
            catch (JsonException ex)
            {
                return ConfigurationApplyResult.Failed(
                    ValidationResult.Failure(
                        CoreErrors.ConfigFileCorrupt,
                        $"History JSON could not be deserialized: {ex.Message}"));
            }

            var validationResult = await _validator.ValidateAsync(target, cancellationToken).ConfigureAwait(false);
            if (!validationResult.IsValid)
            {
                return ConfigurationApplyResult.Failed(validationResult);
            }

            var changes = _differ.Diff(_current, target);
            var newVersionId = ConfigurationVersionId.NewId();
            var prevVersionId = _currentVersionId;
            var prevConfig = _current;

            var prevHash = await _auditLog.ComputeNextPreviousHashAsync(cancellationToken).ConfigureAwait(false);
            var entry = new ConfigurationAuditEntry
            {
                Timestamp = DateTime.UtcNow,
                VersionId = newVersionId,
                PreviousVersionId = prevVersionId.IsEmpty ? null : prevVersionId,
                Action = ConfigurationAuditAction.RolledBack,
                Actor = actor ?? "system",
                DraftId = null,
                Summary = $"Rolled back to version {targetVersionId.Value}: {ConfigurationDiffer.Summarize(changes)}",
                Changes = changes,
                PreviousHash = prevHash,
            };

            // Atomic write sequence — same order as ApplyDraftAsync.
            if (prevConfig is not null && !prevVersionId.IsEmpty)
            {
                var prevJson = JsonSerializer.Serialize(prevConfig, JsonOptions);
                await _store.WriteHistoryAsync(prevVersionId, prevJson, cancellationToken).ConfigureAwait(false);
            }
            await _store.WriteCurrentAsync(json, cancellationToken).ConfigureAwait(false);
            await _auditLog.AppendAsync(entry, cancellationToken).ConfigureAwait(false);

            _current = target;
            _currentVersionId = newVersionId;

            try
            {
                await PruneHistoryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Swallow — retention is not load-bearing for correctness.
            }

            CurrentChanged?.Invoke(this, new ConfigurationChangeEventArgs(prevVersionId, newVersionId, target, changes));

            return new ConfigurationApplyResult
            {
                Success = true,
                VersionId = newVersionId,
                ValidationResult = validationResult,
                AuditEntry = entry,
            };
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConfigurationHistoryEntry>> GetHistoryAsync(CancellationToken cancellationToken)
    {
        EnsureInitialized();

        var versions = await _store.ListHistoryAsync(cancellationToken).ConfigureAwait(false);
        var entries = new List<ConfigurationHistoryEntry>(versions.Count);

        // Build a quick lookup of audit summaries keyed by version id.
        var summaries = new Dictionary<string, (DateTime AppliedAt, string Summary)>();
        await foreach (var auditEntry in _auditLog.ReadAllAsync(verifyChain: false, cancellationToken).ConfigureAwait(false))
        {
            if (auditEntry.Action == ConfigurationAuditAction.Applied
                || auditEntry.Action == ConfigurationAuditAction.RolledBack)
            {
                summaries[auditEntry.VersionId.Value] = (auditEntry.Timestamp, auditEntry.Summary);
            }
        }

        foreach (var v in versions)
        {
            summaries.TryGetValue(v.Value, out var info);
            var json = await _store.ReadHistoryAsync(v, cancellationToken).ConfigureAwait(false);
            entries.Add(new ConfigurationHistoryEntry
            {
                VersionId = v,
                AppliedAt = info.AppliedAt == default ? DateTime.UtcNow : info.AppliedAt,
                Summary = info.Summary ?? "(no audit summary)",
                SizeBytes = json?.Length ?? 0,
            });
        }

        // Most recent first
        entries.Reverse();
        return entries;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ConfigurationAuditEntry> GetAuditLogAsync(
        bool verifyChain,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EnsureInitialized();
        await foreach (var entry in _auditLog.ReadAllAsync(verifyChain, cancellationToken).ConfigureAwait(false))
        {
            yield return entry;
        }
    }

    /// <inheritdoc/>
    public async ValueTask<ConfigurationAuditEntry> AppendRuntimeFaultAsync(
        Diagnostics.ConfigurationFault fault,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fault);
        EnsureInitialized();

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Summary is a one-line, operator-readable description of the
            // fault. Structured detail is in RuntimeFault for tooling.
            var summary = $"{fault.Kind} '{fault.InstanceId}' faulted: {fault.ErrorCode}";

            var entry = new ConfigurationAuditEntry
            {
                Timestamp = DateTime.UtcNow,
                VersionId = _currentVersionId,
                PreviousVersionId = null,
                Action = ConfigurationAuditAction.RuntimeConfigurationFault,
                Actor = "system",
                DraftId = null,
                Summary = summary,
                Changes = [],
                RuntimeFault = fault,
                PreviousHash = await _auditLog.ComputeNextPreviousHashAsync(cancellationToken).ConfigureAwait(false),
            };
            await _auditLog.AppendAsync(entry, cancellationToken).ConfigureAwait(false);
            return entry;
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask<ConfigurationAuditEntry> AppendBundleGeneratedAsync(
        string actor,
        string summary,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(summary);
        EnsureInitialized();

        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entry = new ConfigurationAuditEntry
            {
                Timestamp = DateTime.UtcNow,
                VersionId = _currentVersionId,
                PreviousVersionId = null,
                Action = ConfigurationAuditAction.BundleGenerated,
                Actor = string.IsNullOrWhiteSpace(actor) ? "system" : actor,
                DraftId = null,
                Summary = summary,
                Changes = [],
                PreviousHash = await _auditLog.ComputeNextPreviousHashAsync(cancellationToken).ConfigureAwait(false),
            };
            await _auditLog.AppendAsync(entry, cancellationToken).ConfigureAwait(false);
            return entry;
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _mutex.Dispose();
        return ValueTask.CompletedTask;
    }

    // ------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------
    private void EnsureInitialized()
    {
        if (!_initialized)
        {
            throw new ConfigurationException(
                CoreErrors.RuntimeNotInitialized,
                "ConfigurationManager.InitializeAsync must be called before any other method.");
        }
    }

    private async ValueTask<ConfigurationVersionId> DiscoverCurrentVersionIdAsync(CancellationToken cancellationToken)
    {
        ConfigurationVersionId latest = ConfigurationVersionId.Initial;
        await foreach (var entry in _auditLog.ReadAllAsync(verifyChain: false, cancellationToken).ConfigureAwait(false))
        {
            if (entry.Action == ConfigurationAuditAction.Applied
                || entry.Action == ConfigurationAuditAction.RolledBack)
            {
                latest = entry.VersionId;
            }
        }
        return latest;
    }

    private async ValueTask PruneHistoryAsync(CancellationToken cancellationToken)
    {
        var versions = await _store.ListHistoryAsync(cancellationToken).ConfigureAwait(false);
        if (versions.Count <= _retentionPolicy.MaxRetained)
        {
            return;
        }

        var toDelete = versions.Take(versions.Count - _retentionPolicy.MaxRetained).ToList();
        foreach (var v in toDelete)
        {
            await _store.DeleteHistoryAsync(v, cancellationToken).ConfigureAwait(false);
        }
    }
}
