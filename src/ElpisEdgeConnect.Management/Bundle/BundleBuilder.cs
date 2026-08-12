// ============================================================================
// File: Bundle/BundleBuilder.cs
// Purpose: Composes a diagnostic bundle from the registered IBundleContributors
//          over a shared ArchiveComposer (plan v2 §1/§4). Two entry points:
//            * PreviewAsync — dry-run: runs every contributor, computes the real
//              manifest + redaction summary + total size, writes NO bytes.
//            * BuildAsync — runs the SAME contributors, streams the ZIP, and
//              writes manifest.json + bundle-info.json.
//          Determinism guarantees preview == generated (a test asserts it).
//          Fail-closed: any contributor throwing fails the whole bundle.
// Reference: docs/sessions/2026-05-31-adr0020-bundle-generation-plan-v2.md.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Management.Backup;

using IConfigurationManager = ElpisEdgeConnect.Core.Configuration.IConfigurationManager;
using HostOptions = ElpisEdgeConnect.Host.HostOptions;
using CoreConfig = ElpisEdgeConnect.Core.Configuration;

namespace ElpisEdgeConnect.Management.Bundle;

/// <summary>The dry-run result of a bundle preview — what the operator confirms before generating.</summary>
public sealed record BundlePreview
{
    /// <summary>Per-entry manifest (checksums, redactions, excludes, warnings).</summary>
    public required BundleManifest Manifest { get; init; }

    /// <summary>Aggregate redaction counts.</summary>
    public required RedactionSummary RedactionSummary { get; init; }

    /// <summary>Real total uncompressed size of all entries.</summary>
    public required long TotalBytes { get; init; }

    /// <summary>The contributors that ran (name + capability).</summary>
    public required IReadOnlyList<BundleContributorInfo> Contributors { get; init; }

    /// <summary>
    /// Count of secret-shape detector hits. When &gt; 0, the operator must
    /// acknowledge before the bundle can be generated (plan v2 §8a).
    /// </summary>
    public required int SecretShapeWarningCount { get; init; }

    /// <summary>
    /// The secret-shape hits in detail (file + path + detector verdict), so the
    /// Studio can list them and name each field in the acknowledgement prompt.
    /// </summary>
    public required IReadOnlyList<BundleSecretShapeWarning> SecretShapeWarnings { get; init; }
}

/// <summary>
/// Thrown by <see cref="BundleBuilder.BuildAsync"/> when the bundle has
/// secret-shape warnings but the caller did not acknowledge them. The bundle is
/// NOT generated. Mapped to HTTP 400 by the API.
/// </summary>
public sealed class BundleAcknowledgementRequiredException : InvalidOperationException
{
    /// <summary>Number of secret-shape warnings that require acknowledgement.</summary>
    public int SecretShapeWarningCount { get; }

    /// <summary>Create the exception with the count of unacknowledged warnings.</summary>
    public BundleAcknowledgementRequiredException(int count)
        : base($"This bundle has {count} value(s) flagged as secret-shaped and included verbatim. " +
               "Generation requires explicit acknowledgement that the bundle is safe to share.")
        => SecretShapeWarningCount = count;
}

/// <summary>Composes diagnostic bundles from the registered contributors.</summary>
public sealed class BundleBuilder
{
    /// <summary>Current bundle-format version stamped into <c>bundle-info.json</c>.</summary>
    public const int CurrentSpecVersion = 1;

    private static readonly SchemaNode ConfigSchema =
        ConfigSchemaModelBuilder.Build(typeof(CoreConfig.GatewayConfiguration));

    private readonly HostOptions _hostOptions;
    private readonly IConfigurationManager _configManager;
    private readonly Core.Identity.IGatewayIdentity _identity;
    private readonly ConfigRedactionEngine _engine;
    private readonly BundleRedactionRulesRegistry _registry;
    private readonly CoreConfig.IGatewayAuditWriter _auditWriter;
    private readonly IReadOnlyList<IBundleContributor> _contributors;

    /// <summary>Construct a builder bound to the gateway's data root + contributors.</summary>
    public BundleBuilder(
        HostOptions hostOptions,
        IConfigurationManager configManager,
        Core.Identity.IGatewayIdentity identity,
        ConfigRedactionEngine engine,
        BundleRedactionRulesRegistry registry,
        CoreConfig.IGatewayAuditWriter auditWriter,
        IEnumerable<IBundleContributor> contributors)
    {
        ArgumentNullException.ThrowIfNull(hostOptions);
        ArgumentNullException.ThrowIfNull(configManager);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(auditWriter);
        ArgumentNullException.ThrowIfNull(contributors);
        _hostOptions = hostOptions;
        _configManager = configManager;
        _identity = identity;
        _engine = engine;
        _registry = registry;
        _auditWriter = auditWriter;
        _contributors = contributors.ToList();
    }

    /// <summary>
    /// Dry-run: run every contributor and return the manifest the operator
    /// confirms — without writing any bytes. Preview artifacts are never used as
    /// generation inputs (BuildAsync recomputes from scratch).
    /// </summary>
    public async Task<BundlePreview> PreviewAsync(CancellationToken ct)
    {
        var composer = new ArchiveComposer(zip: null, _engine, ConfigSchema, _registry);
        await RunContributorsAsync(composer, ct).ConfigureAwait(false);

        return new BundlePreview
        {
            Manifest = composer.BuildManifest(),
            RedactionSummary = composer.BuildSummary(),
            TotalBytes = composer.TotalBytes,
            Contributors = ContributorInfos(),
            SecretShapeWarningCount = composer.SecretShapeWarningCount,
            SecretShapeWarnings = composer.SecretShapeWarnings,
        };
    }

    /// <summary>
    /// Generate the bundle: run the contributors, stream the ZIP into
    /// <paramref name="destination"/>, and write <c>manifest.json</c> +
    /// <c>bundle-info.json</c>. Returns the suggested filename.
    /// </summary>
    public async Task<string> BuildAsync(
        Stream destination,
        string invokedBy,
        string? reason,
        bool acknowledgedSecretShapeWarnings,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var gatewayId = _identity.GatewayId;
        var createdUtc = DateTime.UtcNow;
        var actor = string.IsNullOrWhiteSpace(invokedBy) ? "system" : invokedBy;
        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        string manifestHash;
        int contributorCount;

        using (var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            var composer = new ArchiveComposer(zip, _engine, ConfigSchema, _registry);
            await RunContributorsAsync(composer, ct).ConfigureAwait(false);

            // The acknowledgement gate is enforced HERE (not just in the UI):
            // a bundle with secret-shape warnings is not generated unless the
            // caller explicitly acknowledged them (plan v2 §8a). Note: the ZIP
            // stream may already hold redacted entries, but we throw before
            // writing manifest/bundle-info and before the audit event — the
            // caller discards the partial stream.
            if (composer.SecretShapeWarningCount > 0 && !acknowledgedSecretShapeWarnings)
            {
                throw new BundleAcknowledgementRequiredException(composer.SecretShapeWarningCount);
            }

            // manifest.json + bundle-info.json are written directly (NOT through
            // the composer) so they are not part of the composer's own checksums.
            var manifest = composer.BuildManifest();
            await WriteJsonEntryAsync(zip, "manifest.json", manifest, ct).ConfigureAwait(false);

            var contributors = ContributorInfos();
            contributorCount = contributors.Count;
            manifestHash = ComputeManifestHash(manifest);

            var info = new BundleInfo
            {
                BundleSpecVersion = CurrentSpecVersion,
                RedactionEngineVersion = ConfigRedactionEngine.EngineVersion,
                GatewayId = gatewayId,
                BundleGeneratedAtUtc = createdUtc,
                GeneratorVersion = ResolveGeneratorVersion(),
                BundlerInvokedBy = actor,
                Reason = trimmedReason,
                RedactionSummary = composer.BuildSummary(),
                Contributors = contributors,
            };
            await WriteJsonEntryAsync(zip, "bundle-info.json", info, ct).ConfigureAwait(false);
        }

        // ADR-0020 Rule 5: record that a bundle was generated (and may have left
        // the gateway) in the gateway audit chain — so "did we ever ship a bundle
        // from this gateway?" is answerable from the audit log.
        var reasonSuffix = trimmedReason is null ? string.Empty : $" Reason: {trimmedReason}";
        await _auditWriter.AppendBundleGeneratedAsync(
            actor,
            $"Diagnostic bundle generated; manifest {manifestHash}; {contributorCount} contributors.{reasonSuffix}",
            ct).ConfigureAwait(false);

        var ts = createdUtc.ToString("yyyy-MM-ddTHH-mm-ssZ", System.Globalization.CultureInfo.InvariantCulture);
        var safeId = string.IsNullOrWhiteSpace(gatewayId) ? "gateway" : BundleJson.Sanitize(gatewayId);
        return $"edgeconnect-bundle-{safeId}-{ts}.zip";
    }

    private static string ComputeManifestHash(BundleManifest manifest)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, BundleJson.Options));
        return "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private async Task RunContributorsAsync(ArchiveComposer composer, CancellationToken ct)
    {
        var config = await _configManager.GetCurrentAsync(ct).ConfigureAwait(false);
        var context = new BundleContext
        {
            GatewayId = _identity.GatewayId,
            Configuration = config,
            ConfigManager = _configManager,
            Layout = new CoreConfig.ConfigurationStorageLayout(_hostOptions.ResolvedDataRoot),
        };

        // Fail-closed: a contributor throwing propagates and fails the whole
        // bundle. No silent partial bundles (plan v2 §1).
        foreach (var contributor in _contributors)
        {
            await contributor.ContributeAsync(composer, context, ct).ConfigureAwait(false);
        }
    }

    private List<BundleContributorInfo> ContributorInfos() =>
        _contributors
            .Select(c => new BundleContributorInfo { Name = c.Name, Capability = c.Capability.ToString() })
            .ToList();

    private static async Task WriteJsonEntryAsync(ZipArchive zip, string entryName, object value, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, BundleJson.Options));
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
    }

    private static string ResolveGeneratorVersion()
    {
        try
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return !string.IsNullOrWhiteSpace(info) ? info : (asm.GetName().Version?.ToString() ?? "unknown");
        }
        catch
        {
            return "unknown";
        }
    }
}
