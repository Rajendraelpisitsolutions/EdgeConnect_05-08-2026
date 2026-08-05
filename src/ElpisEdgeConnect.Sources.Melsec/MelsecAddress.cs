// ============================================================================
// File: MelsecAddress.cs
// Purpose: Pure parser for operator MELSEC device addresses (e.g. "D100",
//          "W1A", "ZR1F", "D100.3"). Applies per-device radix (ADR-0033 Rule 3,
//          ZR = hex) and word-bit rules (Rule 4). Rejects with typed errors —
//          never generic exceptions:
//            * recognized-but-unsupported device -> DEVICE_NOT_IMPLEMENTED
//            * anything else malformed            -> CONFIG_INVALID_ADDRESS
// ============================================================================

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using ElpisEdgeConnect.Sources.Melsec.Wire;

namespace ElpisEdgeConnect.Sources.Melsec;

/// <summary>A typed parse/validation error for a MELSEC address.</summary>
/// <param name="Code">Error code (e.g. <c>MELSEC.DEVICE_NOT_IMPLEMENTED</c>).</param>
/// <param name="Message">Operator-facing explanation.</param>
public sealed record MelsecAddressError(string Code, string Message);

/// <summary>A parsed, validated MELSEC device address.</summary>
public sealed record MelsecAddress
{
    /// <summary>The resolved device.</summary>
    public required MelsecDeviceDescriptor Device { get; init; }

    /// <summary>The head device number (radix-resolved to an integer).</summary>
    public required int DeviceNumber { get; init; }

    /// <summary>Explicit word-bit index (0..15) for a <c>Dn.b</c> address; null otherwise.</summary>
    public int? BitIndex { get; init; }

    /// <summary>True when this address yields a boolean (a bit device, or a word-bit form).</summary>
    public bool ResolvesToBool => Device.Kind == MelsecDeviceKind.Bit || BitIndex.HasValue;

    /// <summary>Reconstruct the operator address string (radix-correct).</summary>
    public override string ToString()
    {
        var number = Device.Radix switch
        {
            16 => DeviceNumber.ToString("X", CultureInfo.InvariantCulture),
            8 => System.Convert.ToString(DeviceNumber, 8),
            _ => DeviceNumber.ToString(CultureInfo.InvariantCulture),
        };
        return BitIndex is null
            ? $"{Device.Symbol}{number}"
            : $"{Device.Symbol}{number}.{BitIndex.Value.ToString("X", CultureInfo.InvariantCulture)}";
    }
}

/// <summary>Parses operator MELSEC device address strings into <see cref="MelsecAddress"/>.</summary>
public static class MelsecAddressParser
{
    /// <summary>Error code: the device is recognized but not implemented in this release.</summary>
    public const string DeviceNotImplemented = "MELSEC.DEVICE_NOT_IMPLEMENTED";

    /// <summary>Error code: the address is empty, malformed, or references an unknown device.</summary>
    public const string InvalidAddress = "MELSEC.CONFIG_INVALID_ADDRESS";

    /// <summary>
    /// Try to parse <paramref name="address"/> with the shipped Slice-1 (Modern)
    /// device set and radices. Returns false with a typed <paramref name="error"/>
    /// on any failure. Behavior-identical to the pre-registry parser (pinned by
    /// the byte-identity suite).
    /// </summary>
    public static bool TryParse(
        string? address,
        [NotNullWhen(true)] out MelsecAddress? result,
        [NotNullWhen(false)] out MelsecAddressError? error) =>
        TryParse(address, Profiles.MelsecProfiles.Modern, out result, out error);

    /// <summary>
    /// Try to parse <paramref name="address"/> against a specific family
    /// profile (A-2 Gate A-2I). The profile supplies the device set and the
    /// per-device radix (e.g. iQ-F X/Y operator labels are OCTAL); everything
    /// else — symbol resolution, word-bit rules, head-range check — is shared.
    /// </summary>
    public static bool TryParse(
        string? address,
        Profiles.MelsecProfileDefinition profile,
        [NotNullWhen(true)] out MelsecAddress? result,
        [NotNullWhen(false)] out MelsecAddressError? error)
    {
        System.ArgumentNullException.ThrowIfNull(profile);
        result = null;
        error = null;

        if (string.IsNullOrWhiteSpace(address))
        {
            error = new MelsecAddressError(InvalidAddress, "address is empty");
            return false;
        }

        var text = address.Trim().ToUpperInvariant();

        // Split off an optional word-bit suffix (exactly one '.').
        string body;
        string? bitText = null;
        var dotParts = text.Split('.');
        switch (dotParts.Length)
        {
            case 1: body = dotParts[0]; break;
            case 2: body = dotParts[0]; bitText = dotParts[1]; break;
            default:
                error = new MelsecAddressError(InvalidAddress, $"address '{address}' has multiple '.' separators");
                return false;
        }

        // Resolve device symbol by LONGEST globally-known prefix (4→1 chars) so
        // multi-char symbols win over their prefixes (STN before ST/S; LSTN
        // before L) and hex numbers beginning A-F (e.g. ZRAF) are not mistaken
        // for letters. A symbol outside this profile's set (e.g. ZR on iQ-F)
        // still resolves so it rejects with a precise error.
        string? symbol = null;
        for (var len = Math.Min(4, body.Length); len >= 1; len--)
        {
            if (MelsecDevices.IsKnownSymbol(body[..len]))
            {
                symbol = body[..len];
                break;
            }
        }

        if (symbol is null)
        {
            error = new MelsecAddressError(InvalidAddress, $"address '{address}' has an unrecognized device");
            return false;
        }

        var numberText0 = body[symbol.Length..];

        // Bare T / C / ST are ambiguous timer/counter/retentive-timer prefixes —
        // reject with an explicit mnemonic suggestion (never auto-convert).
        if (AmbiguousPrefixSuggestion(symbol, numberText0) is { } suggestion)
        {
            error = new MelsecAddressError(DeviceNotImplemented, suggestion);
            return false;
        }

        // Recognized symbol outside this profile's supported set — covers the
        // global recognized-unsupported list (long families, S/L/F/…) and devices
        // another family has but this profile excludes (ZR on iQ-F).
        if (!profile.TryGetDevice(symbol, out var device))
        {
            error = new MelsecAddressError(DeviceNotImplemented,
                $"device '{symbol}' is recognized but not supported in this release (supported: {profile.SupportedList})");
            return false;
        }

        var numberText = body[symbol.Length..];
        if (numberText.Length == 0)
        {
            error = new MelsecAddressError(InvalidAddress, $"address '{address}' is missing a device number");
            return false;
        }

        if (!TryParseNumber(numberText, device.Radix, out var number))
        {
            var radixName = device.Radix switch { 16 => "hexadecimal", 8 => "octal", _ => "decimal" };
            error = new MelsecAddressError(InvalidAddress,
                $"address '{address}' has an invalid {radixName} device number '{numberText}'");
            return false;
        }

        if (number > SlmpFrameCodec.MaxHeadDeviceNumber)
        {
            error = new MelsecAddressError(InvalidAddress,
                $"device number '{numberText}' exceeds the maximum 0x{SlmpFrameCodec.MaxHeadDeviceNumber:X}");
            return false;
        }

        int? bitIndex = null;
        if (bitText is not null)
        {
            if (device.Kind != MelsecDeviceKind.Word)
            {
                error = new MelsecAddressError(InvalidAddress,
                    $"bit suffix is not allowed on bit device '{symbol}' (it is already bit-addressed)");
                return false;
            }
            if (bitText.Length == 0
                || !int.TryParse(bitText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bit)
                || bit is < 0 or > 15)
            {
                error = new MelsecAddressError(InvalidAddress,
                    $"bit index '.{bitText}' must be 0..F (hexadecimal, 0..15)");
                return false;
            }
            bitIndex = bit;
        }

        result = new MelsecAddress { Device = device, DeviceNumber = number, BitIndex = bitIndex };
        return true;
    }

    /// <summary>
    /// If <paramref name="symbol"/> is a bare ambiguous timer/counter prefix
    /// (T / C / ST), return a suggestion naming the explicit mnemonics; else null.
    /// </summary>
    private static string? AmbiguousPrefixSuggestion(string symbol, string number)
    {
        var n = string.IsNullOrEmpty(number) ? "100" : number;
        return symbol switch
        {
            "T" => $"'T{number}' is ambiguous. Use TN{n} for timer current value, TS{n} for timer contact, or TC{n} for timer coil.",
            "C" => $"'C{number}' is ambiguous. Use CN{n} for counter current value, CS{n} for counter contact, or CC{n} for counter coil.",
            "ST" => $"'ST{number}' is ambiguous. Use STN{n} for retentive-timer current value, STS{n} for retentive-timer contact, or STC{n} for retentive-timer coil.",
            _ => null,
        };
    }

    private static bool TryParseNumber(string text, int radix, out int value)
    {
        if (radix == 16)
        {
            return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value) && value >= 0;
        }
        if (radix == 8)
        {
            // Octal operator labels (iQ-F X/Y). int.TryParse has no octal mode;
            // accumulate digits 0-7 with an explicit overflow guard.
            value = 0;
            foreach (var c in text)
            {
                if (c is < '0' or > '7') { value = 0; return false; }
                if (value > (int.MaxValue - (c - '0')) / 8) { value = 0; return false; }
                value = (value * 8) + (c - '0');
            }
            return text.Length > 0;
        }
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0;
    }
}
