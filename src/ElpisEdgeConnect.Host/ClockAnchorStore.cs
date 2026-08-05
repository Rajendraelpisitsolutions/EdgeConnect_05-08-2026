// ============================================================================
// File: ClockAnchorStore.cs
// Purpose: Persist the "clock anchor" — the highest UTC time the runtime has
//          ever observed — so that winding the system clock BACK (to resurrect
//          an expired license) can be detected across restarts.
//          See ClockRollbackPolicy for the decision logic.
//
//          Hardening:
//            * The value is HMAC-SHA256 tagged with a key derived from the
//              embedded public key, so hand-editing the stored number is
//              detected and the entry is ignored.
//            * It is written to TWO locations (the gateway data root AND a
//              machine-wide folder). The effective anchor is the LATEST valid
//              value found, so deleting just one copy does not reset it.
//
//          A determined attacker with admin rights can still defeat any purely
//          local anti-rollback scheme; the goal here is to stop the trivial
//          "change the system date" bypass. See docs Code Protection Plan.
// ============================================================================

using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ElpisEdgeConnect.Core.Licensing;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Host;

/// <summary>
/// Reads/writes the tamper-tagged monotonic clock anchor used to detect system
/// clock rollback.
/// </summary>
public sealed class ClockAnchorStore
{
    // v2: bumped from ".clock-anchor" so any anchor written by the earlier build
    // — which could advance to a wall-clock time set into the FUTURE and then
    // wrongly flag a correction as a rollback — is ignored and a fresh, capped
    // anchor starts. Safe: a fresh anchor simply trusts the current time, exactly
    // like a first install.
    private const string FileName = ".clock-anchor-v2";

    private readonly string[] _paths;
    private readonly ILogger _logger;
    private readonly byte[] _key;

    /// <summary>Construct a store that persists under the gateway data root and a machine-wide folder.</summary>
    public ClockAnchorStore(string dataRoot, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;

        // Key is derived from the embedded public key: stable across runs and
        // machines, and not user-guessable without the binary.
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(EmbeddedPublicKey.Pem));

        var machineWide = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Elpis",
            "EdgeConnect");

        _paths = new[]
        {
            Path.Combine(dataRoot, FileName),
            Path.Combine(machineWide, FileName),
        };
    }

    /// <summary>
    /// The latest valid anchor found across all locations, or <c>null</c> when
    /// none exists yet (first run) or every copy failed its integrity check.
    /// </summary>
    public DateTime? Read()
    {
        DateTime? best = null;
        foreach (var path in _paths)
        {
            if (TryReadOne(path) is { } value && (best is null || value > best))
            {
                best = value;
            }
        }
        return best;
    }

    /// <summary>Persist <paramref name="anchorUtc"/> to every location (best effort).</summary>
    public void Write(DateTime anchorUtc)
    {
        var payload = anchorUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        var content = payload + "|" + Convert.ToBase64String(Tag(payload));

        foreach (var path in _paths)
        {
#pragma warning disable CA1031 // persistence is best-effort; never crash the host
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
                // e.g. a non-elevated dev run cannot write the machine-wide copy.
                _logger.LogDebug(ex, "Could not write the clock anchor to '{Path}'.", path);
            }
#pragma warning restore CA1031
        }
    }

    private DateTime? TryReadOne(string path)
    {
#pragma warning disable CA1031 // a corrupt/absent anchor must never crash the host
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var parts = File.ReadAllText(path).Trim().Split('|');
            if (parts.Length != 2
                || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
            {
                _logger.LogWarning("Clock anchor '{Path}' is malformed; ignoring it.", path);
                return null;
            }

            // Integrity check: reject a hand-edited timestamp.
            var expected = Tag(parts[0]);
            var actual = Convert.FromBase64String(parts[1]);
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                _logger.LogWarning(
                    "Clock anchor '{Path}' failed its integrity check (it appears to have been edited); ignoring it.",
                    path);
                return null;
            }

            if (ticks < 0 || ticks > DateTime.MaxValue.Ticks)
            {
                return null;
            }
            return new DateTime(ticks, DateTimeKind.Utc);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the clock anchor from '{Path}'.", path);
            return null;
        }
#pragma warning restore CA1031
    }

    private byte[] Tag(string payload) =>
        HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload));
}
