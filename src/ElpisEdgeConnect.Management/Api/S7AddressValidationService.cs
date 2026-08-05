// ============================================================================
// File: Api/S7AddressValidationService.cs
// Purpose: Parser-only validation of a single S7 address string for the
//          source wizard's live, debounced address field. Wraps
//          S7AddressParser, converting its ArgumentException into a
//          structured IsValid=false result with friendly operator copy (never
//          raw exception text), and returns the normalized address, parsed
//          parts, and datatype suggestions for a valid, supported address.
//
//          No network access — this is pure text validation. Timer/Counter
//          addresses parse but are NOT supported through the adapter
//          read/decode path this release, so the service reports them as
//          unsupported (IsValid=false) rather than malformed — keeping the
//          UI honest (M.2b.2 v2 decision).
// Reference: docs/sessions/2026-06-02-s7-source-wizard-plan-v2.md
//            (Address validation contract, M2)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using ElpisEdgeConnect.Management.Wizards;
using ElpisEdgeConnect.Sources.S7;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>Request to validate one operator-authored S7 address string.</summary>
public sealed record S7AddressValidationRequest
{
    /// <summary>The address text as typed (e.g. <c>"DB1.DBW0"</c>).</summary>
    public required string Address { get; init; }

    /// <summary>
    /// Optional datatype the operator has already chosen, for forward use by
    /// the UI. The service does not require it (validation is parser-only),
    /// but echoes datatype suggestions derived from the address width.
    /// </summary>
    public string? Datatype { get; init; }
}

/// <summary>Structured result of validating one S7 address (parser-only).</summary>
public sealed record S7AddressValidationResult
{
    /// <summary>True when the address parses AND is supported this release.</summary>
    public required bool IsValid { get; init; }

    /// <summary>Normalized address (<c>S7Address.ToString()</c>) when parseable.</summary>
    public string? NormalizedAddress { get; init; }

    /// <summary>Memory area name (DataBlock / Marker / Input / Output / Timer / Counter).</summary>
    public string? Area { get; init; }

    /// <summary>DB number (meaningful only for DataBlock addresses).</summary>
    public int? DbNumber { get; init; }

    /// <summary>Byte offset within the area.</summary>
    public int? ByteOffset { get; init; }

    /// <summary>Bit offset (meaningful only for bit-form addresses).</summary>
    public int? BitOffset { get; init; }

    /// <summary>Width hint (Bit / Byte / Word / DWord) when derivable.</summary>
    public string? WidthHint { get; init; }

    /// <summary>Datatype suggestions for the address width ("Address suggests: …").</summary>
    public IReadOnlyList<string> SuggestedDatatypes { get; init; } = Array.Empty<string>();

    /// <summary>Machine-readable error code when <see cref="IsValid"/> is false.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Friendly operator message when <see cref="IsValid"/> is false.</summary>
    public string? Message { get; init; }
}

/// <summary>
/// Validates a single S7 address string. Stateless and side-effect-free —
/// safe to register as a singleton and call on every keystroke (debounced
/// from the UI).
/// </summary>
public sealed class S7AddressValidationService
{
    /// <summary>Validate <paramref name="request"/>'s address. Never throws.</summary>
    public S7AddressValidationResult Validate(S7AddressValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Address))
        {
            return new S7AddressValidationResult
            {
                IsValid = false,
                ErrorCode = "S7.ADDRESS_REQUIRED",
                Message = "Address is required.",
            };
        }

        S7Address parsed;
        try
        {
            parsed = S7AddressParser.Parse(request.Address);
        }
        catch (ArgumentException)
        {
            var (code, message) = FriendlyError(request.Address);
            return new S7AddressValidationResult
            {
                IsValid = false,
                ErrorCode = code,
                Message = message,
            };
        }

        var area = parsed.Area.ToString();
        var widthHint = parsed.WidthHint.ToString();

        // Timer/Counter parse but are unsupported this release — report as
        // unsupported (not malformed) and still surface the parsed parts so
        // the UI can explain.
        if (parsed.Area is S7MemoryArea.Timer or S7MemoryArea.Counter)
        {
            var areaWord = parsed.Area == S7MemoryArea.Timer ? "Timer" : "Counter";
            return new S7AddressValidationResult
            {
                IsValid = false,
                NormalizedAddress = parsed.ToString(),
                Area = area,
                ByteOffset = parsed.ByteOffset,
                WidthHint = widthHint,
                ErrorCode = "S7.UNSUPPORTED_TIMER_COUNTER_ADDRESS",
                Message = $"{areaWord} addresses are recognized but not supported in this release. " +
                          "Use DB, M, I, or Q addresses instead. Examples: DB1.DBW0, M10.2, IW4, Q0.1.",
            };
        }

        return new S7AddressValidationResult
        {
            IsValid = true,
            NormalizedAddress = parsed.ToString(),
            Area = area,
            DbNumber = parsed.Area == S7MemoryArea.DataBlock ? parsed.DbNumber : null,
            ByteOffset = parsed.ByteOffset,
            BitOffset = parsed.WidthHint == S7AddressWidthHint.Bit ? parsed.BitOffset : null,
            WidthHint = widthHint,
            SuggestedDatatypes = S7SourceWizardModel.SuggestDatatypes(parsed.WidthHint),
        };
    }

    /// <summary>
    /// Map a parser rejection to friendly, short operator copy. Called ONLY
    /// after <see cref="S7AddressParser.Parse"/> has already thrown, so the
    /// parser remains the source of truth for valid/invalid — this only picks
    /// a nicer message for the common malformed shapes, falling back to a
    /// generic hint. Never reports a malformed address as valid.
    /// </summary>
    private static (string Code, string Message) FriendlyError(string raw)
    {
        const string Code = "S7.ADDRESS_INVALID";
        var s = raw.Trim().ToUpperInvariant();

        if (s.StartsWith("DB", StringComparison.Ordinal))
        {
            var dot = s.IndexOf('.', 2);
            if (dot < 0)
            {
                return (Code, "DB address needs an access specifier. Examples: DB1.DBW0, DB1.DBX0.0.");
            }

            var dbNumber = s[2..dot];
            if (dbNumber.Length == 0 || !dbNumber.All(char.IsDigit))
            {
                return (Code, "DB number is missing. Example: DB1.DBW0.");
            }

            var inner = s[(dot + 1)..];
            var width = inner.Length >= 3 ? inner[..3] : inner;
            if (!(inner.StartsWith("DBX", StringComparison.Ordinal)
                  || inner.StartsWith("DBB", StringComparison.Ordinal)
                  || inner.StartsWith("DBW", StringComparison.Ordinal)
                  || inner.StartsWith("DBD", StringComparison.Ordinal)))
            {
                return (Code,
                    $"{width} is not a valid DB width. Use DBX, DBB, DBW, or DBD. Examples: DB1.DBW0, DB1.DBX0.0.");
            }

            if (inner.StartsWith("DBX", StringComparison.Ordinal))
            {
                var bitPart = inner[3..];
                var bitDot = bitPart.IndexOf('.');
                if (bitDot < 0)
                {
                    return (Code, "Bit addresses need a bit offset. Example: DB1.DBX0.0.");
                }
                return (Code, "Bit offset must be 0–7.");
            }

            return (Code, $"'{raw}' is not a valid S7 address. Example: DB1.DBW0.");
        }

        return (Code,
            $"'{raw}' is not a valid S7 address. Examples: DB1.DBW0, DB1.DBX0.0, M10.2, IW4, Q0.1.");
    }
}
