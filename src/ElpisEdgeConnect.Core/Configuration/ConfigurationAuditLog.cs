// ============================================================================
// File: Configuration/ConfigurationAuditLog.cs
// Purpose: Append-only audit log with SHA-256 hash chain tamper detection.
// Reference: ARCHITECTURE_BLUEPRINT.md §17.7 (Audit integrity), §8.2
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Errors;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Append-only writer / reader for the configuration audit log. Each entry
/// is serialized as a single JSON line and chained to the previous entry
/// by SHA-256 of the previous line's text. On read, the chain is recomputed
/// and any mismatch surfaces as <see cref="ConfigurationException"/> with
/// <see cref="CoreErrors.ConfigAuditCorrupt"/>.
/// </summary>
/// <remarks>
/// <para>
/// Hash chain semantics: <c>PreviousHash</c> on entry N equals
/// <c>SHA-256(line N-1)</c> in lowercase hex. The first entry uses 64
/// zeros. Tampering with any historical entry breaks the chain at that
/// point and is detected by <see cref="ReadAllAsync(bool, CancellationToken)"/>
/// when called with <c>verifyChain: true</c>.
/// </para>
/// <para>
/// Per <see cref="IConfigurationStore.AppendAuditLineAsync"/>, the actual
/// file write is the store's responsibility — this class only handles
/// JSON shape and chain computation.
/// </para>
/// </remarks>
public sealed class ConfigurationAuditLog
{
    /// <summary>The all-zeros hash used as the previous-hash of the first entry.</summary>
    public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IConfigurationStore _store;

    /// <summary>Construct an audit log bound to a configuration store.</summary>
    public ConfigurationAuditLog(IConfigurationStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>
    /// Compute the next entry's <c>PreviousHash</c> based on the current
    /// last line in the log. Returns <see cref="GenesisHash"/> if the log
    /// is empty.
    /// </summary>
    public async ValueTask<string> ComputeNextPreviousHashAsync(CancellationToken cancellationToken)
    {
        string? lastLine = null;
        await foreach (var line in _store.ReadAuditLinesAsync(cancellationToken).ConfigureAwait(false))
        {
            lastLine = line;
        }

        return lastLine is null ? GenesisHash : ComputeHash(lastLine);
    }

    /// <summary>
    /// Append a new entry. The caller supplies the entry with its
    /// <see cref="ConfigurationAuditEntry.PreviousHash"/> already computed
    /// (typically via <see cref="ComputeNextPreviousHashAsync"/> on the
    /// same <see cref="ConfigurationManager"/> mutex turn).
    /// </summary>
    public async ValueTask AppendAsync(ConfigurationAuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var line = JsonSerializer.Serialize(entry, WriteOptions);
        await _store.AppendAuditLineAsync(line, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Stream every entry from the audit log in order. When
    /// <paramref name="verifyChain"/> is true, the SHA-256 chain is
    /// recomputed and any mismatch throws
    /// <see cref="ConfigurationException"/> with
    /// <see cref="CoreErrors.ConfigAuditCorrupt"/> and the broken entry
    /// index in the error context.
    /// </summary>
    public async IAsyncEnumerable<ConfigurationAuditEntry> ReadAllAsync(
        bool verifyChain,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var expectedPrevHash = GenesisHash;
        var index = 0;

        await foreach (var line in _store.ReadAuditLinesAsync(cancellationToken).ConfigureAwait(false))
        {
            ConfigurationAuditEntry entry;
            try
            {
                entry = JsonSerializer.Deserialize<ConfigurationAuditEntry>(line, ReadOptions)
                    ?? throw new JsonException("Audit entry deserialized to null.");
            }
            catch (JsonException ex)
            {
                throw new ConfigurationException(
                    CoreErrors.ConfigAuditCorrupt,
                    $"Audit log entry {index} is malformed: {ex.Message}");
            }

            if (verifyChain && entry.PreviousHash != expectedPrevHash)
            {
                throw new ConfigurationException(
                    CoreErrors.ConfigAuditCorrupt,
                    $"Audit log hash chain broken at entry {index}. " +
                    $"Expected PreviousHash '{expectedPrevHash}' but found '{entry.PreviousHash}'.");
            }

            yield return entry;

            expectedPrevHash = ComputeHash(line);
            index++;
        }
    }

    /// <summary>
    /// Compute the SHA-256 of a serialized line. Lowercase hex, 64 chars.
    /// </summary>
    public static string ComputeHash(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var bytes = Encoding.UTF8.GetBytes(line);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
