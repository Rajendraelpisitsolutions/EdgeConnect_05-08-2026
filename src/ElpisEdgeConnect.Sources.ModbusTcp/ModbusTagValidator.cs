// ============================================================================
// File: ModbusTagValidator.cs
// Purpose: Shared tag-level validation used by both the adapter's
//          ValidateConfigAsync and the F4 CSV importer. Keeps the rules in
//          one place so importing bulk tags can never produce a tag the
//          adapter would later reject.
// Reference: PHASE3_EXECUTION_PLAN.md §5 (per-tag config),
//            PHASE3_EXECUTION_PLAN.md F4 (CSV import)
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Sources.ModbusTcp.Decoding;
using ElpisEdgeConnect.Sources.ModbusTcp.Scanning;

namespace ElpisEdgeConnect.Sources.ModbusTcp;

/// <summary>
/// Pure validation helper for <see cref="ModbusTagDefinition"/>. Used by both
/// <see cref="ModbusTcpSourceAdapter.ValidateConfigAsync"/> and the F4 CSV
/// importer so the rules live in one place.
/// </summary>
public static class ModbusTagValidator
{
    /// <summary>
    /// Validate one tag and append any issues to <paramref name="errors"/>.
    /// The <paramref name="pathPrefix"/> is prepended to every issue's
    /// <see cref="ValidationIssue.Path"/> — callers from JSON config pass
    /// <c>"TagDefinitions[i]"</c>; the CSV importer passes a row-based
    /// prefix such as <c>"row 17"</c>.
    /// </summary>
    /// <param name="tag">The tag to validate.</param>
    /// <param name="pathPrefix">
    /// Context prefix used to scope each issue to its source location.
    /// May be an empty string.
    /// </param>
    /// <param name="errors">List that issues are appended to.</param>
    /// <param name="addressBase">
    /// The notation the operator used for this source's addresses. By the time
    /// validation runs the address has already been normalised to zero-based, so
    /// this is used only to decide whether a suspiciously Modicon-looking address
    /// means the operator forgot to convert (<see cref="ModbusAddressBase.ZeroBased"/>)
    /// or was already handled by the conversion.
    /// </param>
    public static void Validate(
        ModbusTagDefinition tag,
        string pathPrefix,
        List<ValidationIssue> errors,
        ModbusAddressBase addressBase = ModbusAddressBase.ZeroBased)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(errors);

        var path = pathPrefix ?? string.Empty;

        // Silent-misconfiguration guard: under zero-based notation an address in
        // the Modicon ranges is almost never what the operator meant — a device
        // rarely maps register 40033, so the read fails and every tag in the
        // block surfaces as Quality=Bad/Value=null, which reads as a device
        // fault. Fail at config-apply time with the fix in the message instead.
        // Address-base / address mismatch guard. A 4xxxx (Modicon) address entered
        // under ANY non-Modicon base (zero-based OR one-based) is almost never what
        // the operator meant — it converts to a register the device won't have, so
        // every read surfaces as Quality=Bad/"illegal data address", which reads as
        // a device fault. Fail at config-apply / wizard time with the fix in the
        // message instead of letting the device reject it silently.
        if (addressBase != ModbusAddressBase.Modicon
            && ModbusAddressBaseExtensions.LooksLikeModiconNotation(tag.Address))
        {
            var suggested = tag.Address - ModbusAddressBaseExtensions.ModiconOffset(tag.RegisterClass);
            errors.Add(new ValidationIssue
            {
                Code = ModbusErrors.ImportLegacyAddress,
                Message =
                    $"Tag '{tag.Name}' address looks like Modicon (4xxxx) notation, but this source uses " +
                    $"{addressBase} addressing — it maps to a register the device is unlikely to have, so reads " +
                    $"fail with \"illegal data address\". Set the source's Address base to \"Modicon (4xxxx)\", " +
                    (addressBase == ModbusAddressBase.ZeroBased && suggested >= 0
                        ? $"or enter the zero-based address {suggested}."
                        : $"or re-enter the address in {addressBase} form."),
                Path = Qualify(path, "Address"),
            });
        }

        if (string.IsNullOrWhiteSpace(tag.Name))
        {
            errors.Add(new ValidationIssue
            {
                Code = ModbusErrors.ConfigMissingField,
                Message = "Tag name is required.",
                Path = Qualify(path, "Name"),
            });
        }
        if (tag.ScanRateMs <= 0)
        {
            errors.Add(new ValidationIssue
            {
                Code = ModbusErrors.ConfigOutOfRange,
                Message = "ScanRateMs must be > 0.",
                Path = Qualify(path, "ScanRateMs"),
            });
        }

        // Parse datatype up front so bit-class defaults are applied cleanly.
        ModbusDatatypeSpec? spec = null;
        var defaultSpec = tag.RegisterClass.IsBitRead()
            ? new ModbusDatatypeSpec(ModbusDatatype.Bool)
            : new ModbusDatatypeSpec(ModbusDatatype.UInt16);
        try
        {
            spec = ModbusDatatypeParser.Parse(tag.Datatype, defaultSpec);
        }
        catch (ArgumentException ex)
        {
            errors.Add(new ValidationIssue
            {
                Code = ModbusErrors.ConfigInvalid,
                Message = ex.Message,
                Path = Qualify(path, "Datatype"),
            });
        }

        if (spec is { } s)
        {
            // Bit classes: only Bool datatype makes sense.
            if (tag.RegisterClass.IsBitRead() && s.Datatype != ModbusDatatype.Bool)
            {
                errors.Add(new ValidationIssue
                {
                    Code = ModbusErrors.ConfigInvalid,
                    Message = $"Tag '{tag.Name}' uses {tag.RegisterClass} but datatype is '{s.Datatype}'. Bit-class tags must use 'bool'.",
                    Path = Qualify(path, "Datatype"),
                });
            }
            // Register classes: Bool is wrong (a bit cannot come out of FC03/FC04).
            if (!tag.RegisterClass.IsBitRead() && s.Datatype == ModbusDatatype.Bool)
            {
                errors.Add(new ValidationIssue
                {
                    Code = ModbusErrors.ConfigInvalid,
                    Message = $"Tag '{tag.Name}' uses {tag.RegisterClass} but datatype 'bool' requires Coil or DiscreteInput.",
                    Path = Qualify(path, "Datatype"),
                });
            }

            // Byte-order vs. datatype width mismatch (e.g. float32 with AB).
            if (tag.ByteOrder is { } order &&
                !tag.RegisterClass.IsBitRead() &&
                s.Datatype != ModbusDatatype.StringN &&
                s.ByteCount != order.ByteCount())
            {
                errors.Add(new ValidationIssue
                {
                    Code = ModbusErrors.ConfigInvalid,
                    Message = $"Tag '{tag.Name}' byte-order {order} is {order.ByteCount()}-byte but datatype {s.Datatype} is {s.ByteCount}-byte.",
                    Path = Qualify(path, "ByteOrder"),
                });
            }

            // Byte-order on a bit class is meaningless — a single bit has no ordering.
            if (tag.ByteOrder is not null && tag.RegisterClass.IsBitRead())
            {
                errors.Add(new ValidationIssue
                {
                    Code = ModbusErrors.ConfigInvalid,
                    Message = $"Tag '{tag.Name}' specifies a ByteOrder but {tag.RegisterClass} reads a single bit — byte order is not applicable.",
                    Path = Qualify(path, "ByteOrder"),
                });
            }

            // Strings are packed two chars per register, high-char first
            // by Modbus convention (pymodbus / Siemens / Schneider). A
            // byte-order hint doesn't apply and was silently ignored
            // before M.2b.6.2; tightened here so the wizard, CSV importer,
            // and adapter all surface the same rejection rather than
            // letting the operator believe their byteOrder takes effect.
            if (tag.ByteOrder is not null && s.Datatype == ModbusDatatype.StringN)
            {
                errors.Add(new ValidationIssue
                {
                    Code = ModbusErrors.ConfigInvalid,
                    Message = $"Tag '{tag.Name}' specifies a ByteOrder but datatype '{tag.Datatype}' is a string — Modbus strings are always packed two chars per register, high-char first.",
                    Path = Qualify(path, "ByteOrder"),
                });
            }

            // Scale / offset only valid for numeric datatypes.
            if (!s.SupportsScaleOffset && (tag.Scale is not null || tag.Offset is not null))
            {
                errors.Add(new ValidationIssue
                {
                    Code = ModbusErrors.ConfigInvalid,
                    Message = $"Tag '{tag.Name}' specifies Scale/Offset but datatype '{s.Datatype}' does not support linear scaling. Remove Scale/Offset or change the datatype.",
                    Path = Qualify(path, "Scale"),
                });
            }
        }
    }

    private static string Qualify(string prefix, string field)
        => string.IsNullOrEmpty(prefix) ? field : $"{prefix}.{field}";
}
