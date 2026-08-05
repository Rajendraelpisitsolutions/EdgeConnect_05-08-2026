// ============================================================================
// File: EthernetIpTagValidator.cs
// Purpose: Pure static per-tag validation shared by the adapter's
//          ValidateConfigAsync (and any future CSV importer), so tag rules live
//          in one place. Mirrors ModbusTagValidator.
// ============================================================================

using System.Collections.Generic;
using ElpisEdgeConnect.Core.Adapters;

namespace ElpisEdgeConnect.Sources.EthernetIp;

/// <summary>Validates a single <see cref="EthernetIpTagDefinition"/>.</summary>
internal static class EthernetIpTagValidator
{
    /// <summary>
    /// Append validation issues for <paramref name="tag"/> to
    /// <paramref name="errors"/>, prefixing each path with
    /// <paramref name="pathPrefix"/>.
    /// </summary>
    public static void Validate(EthernetIpTagDefinition tag, string pathPrefix, List<ValidationIssue> errors)
    {
        if (string.IsNullOrWhiteSpace(tag.Name))
        {
            errors.Add(new ValidationIssue
            {
                Code = EthernetIpErrors.ConfigMissingField,
                Message = "Tag name is required.",
                Path = $"{pathPrefix}.Name",
            });
        }

        if (string.IsNullOrWhiteSpace(tag.Address))
        {
            errors.Add(new ValidationIssue
            {
                Code = EthernetIpErrors.ConfigMissingField,
                Message = "Tag address (controller symbolic name) is required.",
                Path = $"{pathPrefix}.Address",
            });
        }

        var elementType = EthernetIpElementTypeExtensions.ParseOrNull(tag.Datatype);
        if (elementType is null)
        {
            errors.Add(new ValidationIssue
            {
                Code = EthernetIpErrors.ConfigInvalid,
                Message = $"Tag '{tag.Name}' has invalid or missing datatype '{tag.Datatype}'. " +
                          "Accepted: BOOL, SINT, INT, DINT, LINT, REAL, LREAL, STRING.",
                Path = $"{pathPrefix}.Datatype",
            });
        }
        else if ((tag.Scale is not null || tag.Offset is not null) && !elementType.Value.SupportsScaleOffset())
        {
            errors.Add(new ValidationIssue
            {
                Code = EthernetIpErrors.ConfigInvalid,
                Message = $"Tag '{tag.Name}' is {elementType} — scale/offset only apply to numeric types.",
                Path = $"{pathPrefix}.Scale",
            });
        }

        if (tag.ScanRateMs <= 0)
        {
            errors.Add(new ValidationIssue
            {
                Code = EthernetIpErrors.ConfigOutOfRange,
                Message = $"Tag '{tag.Name}' ScanRateMs must be > 0.",
                Path = $"{pathPrefix}.ScanRateMs",
            });
        }
    }
}
