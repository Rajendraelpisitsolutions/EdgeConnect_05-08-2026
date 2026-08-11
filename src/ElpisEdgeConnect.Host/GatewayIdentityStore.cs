// ============================================================================
// File: GatewayIdentityStore.cs
// Purpose: Redundant, machine-anchored persistence + recovery of the gateway
//          identity, so that deleting the identity file (e.g. clearing
//          C:\ProgramData\EdgeConnect) does NOT mint a new gateway id and break
//          an already-activated license on the SAME machine.
//
//          Mechanism (mirrors ClockAnchorStore's multi-location + HMAC pattern):
//            * The id is written to SEVERAL locations the operator's cleanup is
//              unlikely to hit all of: the gateway data root, a machine-wide
//              ProgramData\Elpis\EdgeConnect folder, and (Windows) the HKLM
//              registry — which survives deleting any ProgramData folder.
//            * Every stored record is HMAC-tagged over the id AND the machine
//              fingerprint (MachineFingerprint). So a record only validates on
//              the SAME machine — copying it to a different machine fails the
//              tag, preserving single-machine license binding (ADR-0036).
//            * On startup the id is resolved from the first surviving valid
//              record (else a legacy plain-id file is promoted, else a new id is
//              generated) and then RE-WRITTEN to every location (self-healing).
//
//          A determined attacker with admin rights can still defeat any purely
//          local scheme; the goal is to stop the trivial "I deleted the identity
//          file and now my license is dead" failure without weakening binding.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using ElpisEdgeConnect.Core.Licensing;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace ElpisEdgeConnect.Host;

/// <summary>
/// Reads / writes the gateway identity across several machine-anchored locations
/// with a machine-fingerprint-bound HMAC tag, so the id survives deletion of any
/// single copy yet cannot be transplanted to another machine.
/// </summary>
public sealed class GatewayIdentityStore
{
    private const string RegistrySubKeyPath = @"SOFTWARE\Elpis\EdgeConnect";
    private const string RegistryValueName = "GatewayId";

    private readonly IReadOnlyList<string> _filePaths;
    private readonly bool _useRegistry;
    private readonly string _fingerprint;
    private readonly byte[] _hmacKey;
    private readonly ILogger? _logger;

    /// <summary>Full constructor — explicit slots + fingerprint (used by tests).</summary>
    public GatewayIdentityStore(
        IEnumerable<string> filePaths,
        string machineFingerprint,
        bool useRegistry,
        ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(machineFingerprint);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();
        foreach (var p in filePaths)
        {
            if (!string.IsNullOrWhiteSpace(p) && seen.Add(Path.GetFullPath(p)))
            {
                paths.Add(p);
            }
        }
        _filePaths = paths;
        _fingerprint = machineFingerprint;
        _useRegistry = useRegistry;
        _logger = logger;
        // Same key-derivation as ClockAnchorStore: stable across runs / machines,
        // not user-guessable without the binary.
        _hmacKey = SHA256.HashData(Encoding.UTF8.GetBytes(EmbeddedPublicKey.Pem));
    }

    /// <summary>
    /// Build a store for a primary identity file path (e.g. <c>&lt;dataRoot&gt;/identity</c>),
    /// deriving the additional machine-wide file + registry slots. The machine-wide
    /// file is preferred on read so the id is stable per machine and survives
    /// deleting the per-root copy.
    /// </summary>
    public static GatewayIdentityStore ForIdentityPath(string primaryIdentityPath, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primaryIdentityPath);

        var machineWide = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Elpis",
            "EdgeConnect",
            "identity");

        // Machine-wide first (authoritative + survives per-root deletion), then the
        // per-root path (holds the legacy plain id on a pre-upgrade machine).
        return new GatewayIdentityStore(
            new[] { machineWide, primaryIdentityPath },
            MachineFingerprint.Value,
            useRegistry: OperatingSystem.IsWindows(),
            logger);
    }

    /// <summary>
    /// Resolve the gateway id from the first surviving valid record, or a legacy
    /// plain-id file, or <c>null</c> when nothing is found (fresh machine). Read
    /// only — does not generate or write.
    /// </summary>
    public string? Read()
    {
        // 1) A tagged record whose HMAC matches THIS machine's fingerprint.
        if (_useRegistry && TryParseTagged(ReadRegistryRaw()) is { } fromRegistry)
        {
            return fromRegistry;
        }
        foreach (var path in _filePaths)
        {
            if (TryParseTagged(ReadFileRaw(path)) is { } fromFile)
            {
                return fromFile;
            }
        }

        // 2) A legacy plain id (pre-upgrade builds wrote just the GUID). Adopting
        //    it is no weaker than today's copyable file, and it is re-stamped with
        //    the fingerprint on the Write() that follows.
        foreach (var path in _filePaths)
        {
            var raw = ReadFileRaw(path);
            if (raw is not null && !raw.Contains('|', StringComparison.Ordinal))
            {
                var id = raw.Trim();
                if (!string.IsNullOrEmpty(id))
                {
                    _logger?.LogInformation(
                        "Promoting legacy gateway identity '{Id}' from '{Path}' into the machine-anchored store.",
                        id, path);
                    return id;
                }
            }
        }

        return null;
    }

    /// <summary>Persist <paramref name="gatewayId"/> (fingerprint-tagged) to every location (best effort).</summary>
    public void Write(string gatewayId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayId);
        var record = gatewayId + "|" + Convert.ToBase64String(Tag(gatewayId));

        foreach (var path in _filePaths)
        {
            WriteFile(path, record);
        }
        if (_useRegistry)
        {
            WriteRegistry(record);
        }
    }

    // ── tagging ───────────────────────────────────────────────────────────────

    private byte[] Tag(string gatewayId) =>
        HMACSHA256.HashData(_hmacKey, Encoding.UTF8.GetBytes(gatewayId + "\n" + _fingerprint));

    /// <summary>
    /// Return the id from a tagged record IFF the HMAC verifies against this
    /// machine's fingerprint (so a hand-edit or a copy from another machine is
    /// rejected); otherwise <c>null</c>.
    /// </summary>
    private string? TryParseTagged(string? raw)
    {
        if (raw is null)
        {
            return null;
        }
#pragma warning disable CA1031 // a corrupt record must never crash startup
        try
        {
            var parts = raw.Trim().Split('|');
            if (parts.Length != 2 || string.IsNullOrEmpty(parts[0]))
            {
                return null;
            }
            var expected = Tag(parts[0]);
            var actual = Convert.FromBase64String(parts[1]);
            return CryptographicOperations.FixedTimeEquals(expected, actual) ? parts[0] : null;
        }
        catch (Exception)
        {
            return null;
        }
#pragma warning restore CA1031
    }

    // ── file slots ──────────────────────────────────────────────────────────

    private string? ReadFileRaw(string path)
    {
#pragma warning disable CA1031 // an unreadable slot is simply skipped
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not read gateway identity from '{Path}'.", path);
            return null;
        }
#pragma warning restore CA1031
    }

    private void WriteFile(string path, string content)
    {
#pragma warning disable CA1031 // persistence is best-effort; never crash startup
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(path, content);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not write gateway identity to '{Path}'.", path);
        }
#pragma warning restore CA1031
    }

    // ── registry slot (Windows) ─────────────────────────────────────────────

    private string? ReadRegistryRaw()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }
        return ReadRegistryRawWindows();
    }

    [SupportedOSPlatform("windows")]
    private string? ReadRegistryRawWindows()
    {
#pragma warning disable CA1031 // absent/locked registry is simply skipped
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistrySubKeyPath);
            return key?.GetValue(RegistryValueName) as string;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not read gateway identity from the registry.");
            return null;
        }
#pragma warning restore CA1031
    }

    private void WriteRegistry(string content)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        WriteRegistryWindows(content);
    }

    [SupportedOSPlatform("windows")]
    private void WriteRegistryWindows(string content)
    {
#pragma warning disable CA1031 // e.g. a non-elevated dev run cannot write HKLM
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(RegistrySubKeyPath);
            key.SetValue(RegistryValueName, content, RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Could not write gateway identity to the registry (elevation required).");
        }
#pragma warning restore CA1031
    }
}
