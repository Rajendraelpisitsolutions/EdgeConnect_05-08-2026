// ============================================================================
// File: Bundle/ArchiveComposer.cs
// Purpose: The shared archive-composition core (plan v2 §3). Implements
//          IBundleWriter: contributors hand it content; it applies redaction via
//          ConfigRedactionEngine, computes SHA-256 per entry, and DERIVES the
//          manifest tier grouping + redaction summary (Q-7 — contributors stay
//          tier-agnostic). Supports a dry-run mode (preview computes the real
//          manifest + checksums + size without writing the ZIP).
//
//          Backup and bundle both compose over this core; BundleBuilder does not
//          extend BackupBuilder (plan v2 §3).
// Reference: docs/sessions/2026-05-31-adr0020-bundle-generation-plan-v2.md §3/§4.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Management.Backup;

namespace ElpisEdgeConnect.Management.Bundle;

/// <summary>
/// Composes bundle entries into a (real or dry-run) archive, applying redaction
/// and accumulating the manifest + redaction summary. Not thread-safe — one
/// composer per bundle build.
/// </summary>
public sealed class ArchiveComposer : IBundleWriter
{
    private readonly ZipArchive? _zip; // null = dry-run (preview): manifest only, no bytes written
    private readonly ConfigRedactionEngine _engine;
    private readonly SchemaNode _configSchema;
    private readonly BundleRedactionRulesRegistry _registry;

    private readonly Dictionary<string, string> _checksums = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> _redactions = new(StringComparer.Ordinal);
    private readonly List<string> _excluded = new();
    private readonly List<string> _warnings = new();
    private readonly List<BundleSecretShapeWarning> _secretShapeWarningDetails = new();
    private int _includedEntries;
    private int _maskedFields;
    private int _strippedFields;
    private int _secretShapeWarnings;
    private long _totalBytes;

    /// <summary>
    /// Construct a composer. Pass <paramref name="zip"/> = <see langword="null"/>
    /// for a dry-run (preview) — checksums and the manifest are still computed,
    /// but no entry bytes are written.
    /// </summary>
    public ArchiveComposer(
        ZipArchive? zip,
        ConfigRedactionEngine engine,
        SchemaNode configSchema,
        BundleRedactionRulesRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(configSchema);
        ArgumentNullException.ThrowIfNull(registry);
        _zip = zip;
        _engine = engine;
        _configSchema = configSchema;
        _registry = registry;
    }

    /// <summary>Total uncompressed byte size of all entries — the real preview size estimate.</summary>
    public long TotalBytes => _totalBytes;

    /// <inheritdoc />
    public async Task AddRedactedJsonAsync(string entryName, string rawJson, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(entryName);
        ArgumentNullException.ThrowIfNull(rawJson);

        var result = _engine.Redact(rawJson, _configSchema, _registry);
        await WriteEntryAsync(entryName, result.RedactedJson, ct).ConfigureAwait(false);

        if (result.Paths.Count > 0)
        {
            _redactions[entryName] = result.Paths;
        }
        _maskedFields += result.MaskedPaths.Count;
        _strippedFields += result.StrippedPaths.Count;
        foreach (var w in result.Warnings)
        {
            _warnings.Add($"{entryName}: {w.Path}: {w.Message}");
            if (w.Kind == RedactionWarningKind.SecretShape)
            {
                _secretShapeWarnings++;
                _secretShapeWarningDetails.Add(new BundleSecretShapeWarning
                {
                    File = entryName,
                    Path = w.Path,
                    Detail = w.Message,
                });
            }
        }
    }

    /// <summary>
    /// Count of secret-shape detector hits across the bundle — drives the
    /// operator acknowledgement gate (a bundle with &gt; 0 cannot be generated
    /// without explicit acknowledgement).
    /// </summary>
    public int SecretShapeWarningCount => _secretShapeWarnings;

    /// <summary>
    /// Structured secret-shape detector hits (file + path + detector verdict) —
    /// lets the preview name each flagged field in the operator acknowledgement.
    /// </summary>
    public IReadOnlyList<BundleSecretShapeWarning> SecretShapeWarnings => _secretShapeWarningDetails;

    /// <inheritdoc />
    public Task AddVerbatimAsync(string entryName, string content, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(entryName);
        ArgumentNullException.ThrowIfNull(content);
        return WriteEntryAsync(entryName, content, ct);
    }

    /// <inheritdoc />
    public void AddExcludedNote(string surface, string reason)
    {
        ArgumentException.ThrowIfNullOrEmpty(surface);
        _excluded.Add($"{surface}: {reason}");
    }

    /// <summary>Build the per-entry manifest (manifest.json).</summary>
    public BundleManifest BuildManifest() => new()
    {
        Checksums = _checksums,
        Redactions = _redactions,
        Excluded = _excluded,
        Warnings = _warnings,
    };

    /// <summary>Build the aggregate redaction summary (for bundle-info.json).</summary>
    public RedactionSummary BuildSummary() => new()
    {
        IncludedEntries = _includedEntries,
        MaskedFields = _maskedFields,
        StrippedFields = _strippedFields,
        ExcludedSurfaces = _excluded.Count,
    };

    private async Task WriteEntryAsync(string entryName, string content, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        _checksums[entryName] = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        _includedEntries++;
        _totalBytes += bytes.Length;

        if (_zip is not null)
        {
            var entry = _zip.CreateEntry(entryName, CompressionLevel.Optimal);
            await using var stream = entry.Open();
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        }
    }
}
