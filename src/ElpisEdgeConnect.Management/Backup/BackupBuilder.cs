// ============================================================================
// File: Backup/BackupBuilder.cs
// Purpose: Default implementation of IBackupBuilder. Composes the
//          on-disk gateway config, history files, and audit log into
//          a streamed zip, redacting secrets in JSON files via
//          ConfigRedactionEngine, computing SHA-256 per file inline via
//          IncrementalHash, and emitting the BackupManifest at the
//          zip root.
//
//          Reads gateway.json directly from disk (not through
//          IConfigurationManager) so unknown / experimental fields
//          survive into the backup byte-for-byte (modulo redaction).
//          Industrial configs accumulate operator-authored extras
//          that typed reserialization would silently drop.
//
//          audit.log ships VERBATIM because ConfigurationChange does
//          not embed Old/New values (only Path string), so the audit
//          log cannot leak secrets through diffs. If
//          ConfigurationDiffer ever changes to put values in Summary
//          or adds OldValue/NewValue fields, an audit redactor must
//          be added here — see the BackupSecretPatterns.TryGetTier
//          allowlist for the property names to scrub.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1c.3
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Management.Contracts;

// Aliases to disambiguate from types brought in by the ASP.NET Core Web SDK
// global usings — both IConfigurationManager and HostOptions live in the
// Microsoft.Extensions.* namespaces too.
using IConfigurationManager = ElpisEdgeConnect.Core.Configuration.IConfigurationManager;
using HostOptions = ElpisEdgeConnect.Host.HostOptions;
using CoreConfig = ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Management.Backup;

/// <summary>
/// Default <see cref="IBackupBuilder"/>. Pulls files via
/// <see cref="CoreConfig.ConfigurationStorageLayout"/>, redacts with
/// <see cref="ConfigRedactionEngine"/>, and emits a zip with a
/// <see cref="BackupManifest"/> at the root.
/// </summary>
public sealed class BackupBuilder : IBackupBuilder
{
    /// <summary>Schema version of the manifest format this builder emits.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>BackupType value for this milestone — secrets redacted, no certificates.</summary>
    public const string ConfigurationOnlyBackupType = "configuration-only";

    /// <summary>
    /// JSON options used to serialize the manifest. Public so test code
    /// (and any future tooling) can round-trip the manifest with the
    /// same shape contract: camelCase property names (consistent with
    /// the rest of the API), pretty-printed, UnsafeRelaxedJsonEscaping
    /// so '+' in InformationalVersion strings and angle brackets in
    /// future redaction paths render as literals instead of unicode
    /// escapes, and null fields omitted (keeps Warnings out when there
    /// are none).
    /// </summary>
    public static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // The reflection-derived config schema model (ADR-0020 M-B). Built once and
    // cached by ConfigSchemaModelBuilder; reused for gateway.json and every
    // history file (all are GatewayConfiguration documents).
    private static readonly SchemaNode ConfigSchema =
        ConfigSchemaModelBuilder.Build(typeof(CoreConfig.GatewayConfiguration));

    private readonly HostOptions _hostOptions;
    private readonly IConfigurationManager _configManager;
    private readonly ConfigRedactionEngine _redactor;
    private readonly BundleRedactionRulesRegistry _redactionRules;

    /// <summary>Construct a builder bound to the running gateway's data root + config manager.</summary>
    public BackupBuilder(
        HostOptions hostOptions,
        IConfigurationManager configManager,
        ConfigRedactionEngine redactor,
        BundleRedactionRulesRegistry redactionRules)
    {
        ArgumentNullException.ThrowIfNull(hostOptions);
        ArgumentNullException.ThrowIfNull(configManager);
        ArgumentNullException.ThrowIfNull(redactor);
        ArgumentNullException.ThrowIfNull(redactionRules);
        _hostOptions = hostOptions;
        _configManager = configManager;
        _redactor = redactor;
        _redactionRules = redactionRules;
    }

    /// <inheritdoc />
    public async Task<string> BuildAsync(Stream destination, string exportReason, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (string.IsNullOrWhiteSpace(exportReason))
        {
            exportReason = "manual";
        }

        var layout = new CoreConfig.ConfigurationStorageLayout(_hostOptions.ResolvedDataRoot);
        if (!File.Exists(layout.CurrentConfigPath))
        {
            throw new InvalidOperationException(
                $"Cannot build a backup: no active configuration found at '{layout.CurrentConfigPath}'. " +
                "Initialise the gateway before requesting a backup.");
        }

        // Load metadata we need for the manifest BEFORE writing anything to the zip.
        var config = await _configManager.GetCurrentAsync(ct).ConfigureAwait(false);
        var gatewayId = config.Gateway.GatewayId;
        var gatewayName = config.Gateway.GatewayName;

        var historyEntries = await _configManager.GetHistoryAsync(ct).ConfigureAwait(false);

        // Accumulators populated as we stream each file into the zip.
        var redactions = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var checksums = new Dictionary<string, string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        long auditEntryCount = 0;
        var historyCount = 0;
        var createdUtc = DateTime.UtcNow;

        // Compose into the destination stream. ZipArchive owns its own
        // entry streams; we wrap the destination with leaveOpen=true so
        // the API endpoint can subsequently flush / close the response.
        using (var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            // --- gateway.json (redacted via the schema-aware engine) ---
            var rawCurrent = await File.ReadAllTextAsync(layout.CurrentConfigPath, ct).ConfigureAwait(false);
            var redactedCurrent = _redactor.Redact(rawCurrent, ConfigSchema, _redactionRules);
            await WriteEntryAsync(zip, "gateway.json", redactedCurrent.RedactedJson, checksums, ct).ConfigureAwait(false);
            if (redactedCurrent.Paths.Count > 0)
            {
                redactions["gateway.json"] = redactedCurrent.Paths;
            }
            CollectRedactionWarnings("gateway.json", redactedCurrent, warnings);

            // --- history/*.json (redacted) ---
            for (var i = 0; i < historyEntries.Count; i++)
            {
                var entry = historyEntries[i];
                var historyPath = layout.HistoryPath(entry.VersionId);
                if (!File.Exists(historyPath))
                {
                    warnings.Add($"history version {entry.VersionId.Value} missing on disk at '{historyPath}' — skipped.");
                    continue;
                }

                var rawHistory = await File.ReadAllTextAsync(historyPath, ct).ConfigureAwait(false);
                var redactedHistory = _redactor.Redact(rawHistory, ConfigSchema, _redactionRules);

                // Numbered + version-suffixed so unzip listings sort predictably
                // regardless of the underlying version-id format.
                var zipName = $"history/{(i + 1):D4}-{SanitizeForFilename(entry.VersionId.Value)}.json";
                await WriteEntryAsync(zip, zipName, redactedHistory.RedactedJson, checksums, ct).ConfigureAwait(false);
                if (redactedHistory.Paths.Count > 0)
                {
                    redactions[zipName] = redactedHistory.Paths;
                }
                CollectRedactionWarnings(zipName, redactedHistory, warnings);
                historyCount++;
            }

            // --- audit.log (verbatim — see header note on safety) ---
            // Best-effort: a corrupt audit log shouldn't fail the whole backup.
            try
            {
                auditEntryCount = await WriteAuditLogAsync(zip, checksums, ct).ConfigureAwait(false);
                if (auditEntryCount == 0)
                {
                    warnings.Add("audit log is empty.");
                }
            }
            catch (ConfigurationException ex)
            {
                warnings.Add($"audit log read failed: {ex.Message}");
            }

            // --- manifest.json (LAST so checksums for all files are populated) ---
            var manifest = new BackupManifest
            {
                SchemaVersion = CurrentSchemaVersion,
                BackupType = ConfigurationOnlyBackupType,
                RedactionEngineVersion = ConfigRedactionEngine.EngineVersion,
                ProductVersion = ResolveProductVersion(),
                GatewayId = gatewayId,
                GatewayName = gatewayName,
                CreatedUtc = createdUtc,
                ExportedBy = "system",                       // auth context not wired yet
                ExportReason = exportReason,
                Components = new BackupComponents
                {
                    // current.json was verified present at the top of BuildAsync.
                    Configuration = true,
                    // Flag means "audit content was meaningfully present in the
                    // bundle." An empty audit.log file ships either way, but
                    // the flag stays false until at least one entry has been
                    // appended — so importers and operators don't mistake
                    // genesis for a real audit trail.
                    Audit = auditEntryCount > 0,
                    History = historyCount > 0,
                    // schemaVersion=1 / configuration-only never ships certs.
                    Certificates = false,
                },
                Redactions = redactions,
                Checksums = checksums,
                HistoryCount = historyCount,
                AuditEntryCount = auditEntryCount,
                Warnings = warnings.Count == 0 ? null : warnings,
            };

            var manifestJson = JsonSerializer.Serialize(manifest, ManifestJsonOptions);
            // NOTE: we DON'T include manifest.json in its own checksums dict —
            // a hash of a document that includes its own hash is a chicken-and-egg.
            // Verifiers re-compute checksums for the OTHER files and compare to
            // the values asserted by manifest.json.
            await WriteEntryAsync(zip, "manifest.json", manifestJson, checksums: null, ct).ConfigureAwait(false);
        }

        var filename = BuildFilename(gatewayId, createdUtc);
        return filename;
    }

    /// <summary>
    /// Write a single text entry into the zip and (when <paramref name="checksums"/> is non-null) record its SHA-256.
    /// </summary>
    private static async Task WriteEntryAsync(
        ZipArchive zip,
        string entryName,
        string content,
        Dictionary<string, string>? checksums,
        CancellationToken ct)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        await entryStream.WriteAsync(bytes, ct).ConfigureAwait(false);

        if (checksums is not null)
        {
            var hash = SHA256.HashData(bytes);
            checksums[entryName] = "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
        }
    }

    /// <summary>
    /// Stream the audit log into the zip while incrementally hashing
    /// its bytes. Stamps the SHA-256 into <paramref name="checksums"/>
    /// under the entry name <c>"audit.log"</c>. Returns the number of
    /// audit entries written so the caller can populate
    /// <c>AuditEntryCount</c> on the manifest.
    /// </summary>
    private async Task<long> WriteAuditLogAsync(
        ZipArchive zip,
        Dictionary<string, string> checksums,
        CancellationToken ct)
    {
        var entry = zip.CreateEntry("audit.log", CompressionLevel.Optimal);
        await using var entryStream = entry.Open();

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long entries = 0;
        var newlineBytes = Encoding.UTF8.GetBytes("\n");

        await foreach (var auditEntry in _configManager.GetAuditLogAsync(verifyChain: false, ct).ConfigureAwait(false))
        {
            var line = JsonSerializer.Serialize(auditEntry);
            var lineBytes = Encoding.UTF8.GetBytes(line);

            await entryStream.WriteAsync(lineBytes, ct).ConfigureAwait(false);
            await entryStream.WriteAsync(newlineBytes, ct).ConfigureAwait(false);
            hasher.AppendData(lineBytes);
            hasher.AppendData(newlineBytes);

            entries++;
        }

        var hash = hasher.GetHashAndReset();
        checksums["audit.log"] = "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
        return entries;
    }

    /// <summary>
    /// Append any redaction provenance warnings (e.g. a connection block whose
    /// protocol has no registered rules, so only the baseline was applied —
    /// ADR-0020 Q-B5) to the manifest's warning list, prefixed with the file
    /// they came from. In the running gateway every protocol's rules are
    /// registered, so this list is normally empty; it surfaces a misconfiguration
    /// or an old-config protocol the build no longer ships rules for.
    /// </summary>
    private static void CollectRedactionWarnings(
        string fileName, ConfigRedactionResult result, List<string> warnings)
    {
        foreach (var w in result.Warnings)
        {
            warnings.Add($"redaction({fileName}): {w.Path}: {w.Message}");
        }
    }

    /// <summary>
    /// Strip filesystem-unfriendly characters from a version id before
    /// using it as part of a zip-entry name. Defensive: today's
    /// ConfigurationVersionId is well-behaved, but we don't want to
    /// re-litigate that assumption every time the type changes.
    /// </summary>
    private static string SanitizeForFilename(string versionIdValue)
    {
        var sb = new StringBuilder(versionIdValue.Length);
        foreach (var c in versionIdValue)
        {
            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Resolve the EdgeConnect runtime version for the manifest. Falls
    /// back to <c>"unknown"</c> on any reflection error rather than
    /// failing the whole export.
    /// </summary>
    private static string ResolveProductVersion()
    {
        try
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            // InformationalVersion carries the full semver + git suffix
            // (e.g. "0.1.0-phase2+abcd123"); fall back to assembly Version
            // when the attribute isn't set.
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info)) return info;
            return asm.GetName().Version?.ToString() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>Build the Content-Disposition filename for the response.</summary>
    private static string BuildFilename(string gatewayId, DateTime createdUtc)
    {
        // ISO 8601 with safe punctuation — no colons (Windows-hostile),
        // no slashes. Operators can sort by this filename verbatim.
        var ts = createdUtc.ToString("yyyy-MM-ddTHH-mm-ssZ", CultureInfo.InvariantCulture);
        var safeId = string.IsNullOrWhiteSpace(gatewayId) ? "gateway" : SanitizeForFilename(gatewayId);
        return $"edgeconnect-backup-{safeId}-{ts}.zip";
    }
}
