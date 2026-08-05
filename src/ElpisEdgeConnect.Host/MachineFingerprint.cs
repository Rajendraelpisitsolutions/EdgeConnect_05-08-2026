// ============================================================================
// File: MachineFingerprint.cs
// Purpose: A stable per-machine identifier, used ONLY as a guard tag on the
//          persisted gateway identity (never to derive the id). It lets the
//          runtime recover the gateway id on the SAME machine after the identity
//          file is deleted, while rejecting a copy of that record on a DIFFERENT
//          machine — so single-machine license binding (ADR-0036) is preserved.
//
//          Sources, in order of preference:
//            * Windows: HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid
//              (per-Windows-install GUID; stable across reboots / app reinstall).
//            * Linux:   /etc/machine-id or /var/lib/dbus/machine-id.
//            * Fallback: the machine name (weak, but stable on one host).
// ============================================================================

using System;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ElpisEdgeConnect.Host;

/// <summary>A stable per-machine fingerprint string. Computed once per process.</summary>
internal static class MachineFingerprint
{
    /// <summary>The machine fingerprint (e.g. <c>"win:{MachineGuid}"</c>). Never empty.</summary>
    public static string Value { get; } = Compute();

    private static string Compute()
    {
#pragma warning disable CA1031 // any failure falls back to the machine name; never throw
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var guid = ReadWindowsMachineGuid();
                if (!string.IsNullOrWhiteSpace(guid))
                {
                    return "win:" + guid.Trim();
                }
            }
            else
            {
                foreach (var path in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
                {
                    if (File.Exists(path))
                    {
                        var id = File.ReadAllText(path).Trim();
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            return "nix:" + id;
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
            // Fall through to the machine-name fallback.
        }
#pragma warning restore CA1031

        // Last resort — stable on a single host, weaker across a fleet.
        return "host:" + Environment.MachineName;
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadWindowsMachineGuid()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        return key?.GetValue("MachineGuid") as string;
    }
}
