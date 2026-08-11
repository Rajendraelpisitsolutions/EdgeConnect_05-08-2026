// ============================================================================
// File: Wizards/HostAddressValidation.cs
// Purpose: Operator-facing validation for the "IP address / host" field shared
//          by every network source wizard (FOCAS2, Modbus TCP, S7, MELSEC).
//
//          WHY THIS EXISTS
//          Every one of those wizards used to gate Save on nothing more than
//          string.IsNullOrWhiteSpace(host). That accepted "1" as a CNC address
//          and wrote it straight into gateway.json, where it surfaced hours
//          later as an unexplained connect failure against 0.0.0.1.
//
//          WHY NOT JUST IPAddress.TryParse
//          Because it does not catch the bug. On .NET 8:
//
//            IPAddress.TryParse("1")    -> true, 0.0.0.1
//            IPAddress.TryParse("10.1") -> true, 10.0.0.1
//
//          .NET still honours the historic inet_aton shorthand where a partial
//          dotted address is expanded into a full quad. A TryParse-based guard
//          would therefore have waved through the exact value that caused the
//          defect. The rule below closes that hole: anything composed only of
//          digits and dots must be a complete four-octet IPv4 address.
// Reference: docs/platform-principles.md P6 (operational product) — a wizard
//            must reject an unusable address at entry time, not defer it to a
//            runtime connect error the operator has to decode.
// ============================================================================

using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>
/// Validates the host portion of a device endpoint entered in a source wizard.
/// Accepts a full IPv4 address, an IPv6 address, or a DNS host name.
/// </summary>
public static class HostAddressValidation
{
    /// <summary>Maximum length of a single DNS label (RFC 1035).</summary>
    private const int MaxLabelLength = 63;

    /// <summary>Maximum length of a full DNS name (RFC 1035).</summary>
    private const int MaxHostNameLength = 253;

    /// <summary>
    /// True when <paramref name="host"/> is a usable device address.
    /// </summary>
    /// <param name="host">The raw text typed by the operator.</param>
    public static bool IsValid(string? host) => Describe(host) is null;

    /// <summary>
    /// Returns null when <paramref name="host"/> is usable, otherwise an
    /// operator-facing explanation of what is wrong and what to type instead.
    /// </summary>
    /// <param name="host">The raw text typed by the operator.</param>
    /// <remarks>
    /// The message deliberately avoids parser vocabulary ("not a valid absolute
    /// URI", "malformed literal"). It names an example address, because the
    /// operator's next action is to type one.
    /// </remarks>
    public static string? Describe(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return "Enter the device's IP address — for example 192.168.1.101.";
        }

        // Trailing/leading whitespace is a paste artefact, not an operator
        // decision; judge the trimmed value so a stray space is not an error.
        var value = host.Trim();

        if (value.Contains(' ', StringComparison.Ordinal))
        {
            return $"'{value}' contains a space. Enter a single address such as 192.168.1.101.";
        }

        // Digits-and-dots only => the operator meant an IPv4 address, so hold it
        // to a complete four-octet form. This is the branch that rejects "1"
        // and "10.1", which IPAddress.TryParse would silently expand.
        if (value.All(c => char.IsAsciiDigit(c) || c == '.'))
        {
            return IsCompleteIPv4(value)
                ? null
                : $"'{value}' is not a complete IP address. Enter all four parts — " +
                  "for example 192.168.1.101.";
        }

        // Anything containing a colon is an IPv6 literal; defer to the parser,
        // which handles compression and zone ids correctly.
        if (value.Contains(':', StringComparison.Ordinal))
        {
            return IPAddress.TryParse(value, out var v6) && v6.AddressFamily == AddressFamily.InterNetworkV6
                ? null
                : $"'{value}' is not a valid IPv6 address.";
        }

        return IsValidHostName(value)
            ? null
            : $"'{value}' is not a valid IP address or host name. " +
              "Enter an address such as 192.168.1.101, or a machine name such as cnc-line-a.";
    }

    /// <summary>
    /// True only for a fully written IPv4 address: exactly four parts, each a
    /// plain 0-255 number. Rejects the shorthand forms .NET would otherwise
    /// expand, and rejects leading zeros, which some resolvers read as octal.
    /// </summary>
    private static bool IsCompleteIPv4(string value)
    {
        var parts = value.Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (part.Length is 0 or > 3)
            {
                return false;
            }
            if (part.Length > 1 && part[0] == '0')
            {
                return false;
            }
            if (!byte.TryParse(part, out _))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Validates a DNS host name per RFC 1123: dot-separated labels of letters,
    /// digits and hyphens, where no label starts or ends with a hyphen.
    /// </summary>
    private static bool IsValidHostName(string value)
    {
        if (value.Length > MaxHostNameLength)
        {
            return false;
        }

        // A trailing dot is a legal fully-qualified form ("cnc.factory.local.").
        var candidate = value.EndsWith('.') ? value[..^1] : value;
        if (candidate.Length == 0)
        {
            return false;
        }

        foreach (var label in candidate.Split('.'))
        {
            if (label.Length is 0 or > MaxLabelLength)
            {
                return false;
            }
            if (label[0] == '-' || label[^1] == '-')
            {
                return false;
            }
            if (!label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            {
                return false;
            }
        }

        return true;
    }
}
