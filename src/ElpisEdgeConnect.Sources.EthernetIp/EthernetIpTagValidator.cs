// ============================================================================
// File: EthernetIpTagValidator.cs
// Purpose: Pure static per-tag validation shared by the adapter's
//          ValidateConfigAsync (and any future CSV importer), so tag rules live
//          in one place. Mirrors ModbusTagValidator.
// ============================================================================

using System.Collections.Generic;
using System.Linq;
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
    /// <param name="tag">The tag definition to validate.</param>
    /// <param name="pathPrefix">Config path prefix for issue reporting, e.g. <c>TagDefinitions[0]</c>.</param>
    /// <param name="errors">Blocking issues; a non-empty list fails the config apply.</param>
    /// <param name="warnings">
    /// Non-blocking issues. Optional so existing callers keep compiling; pass a
    /// list to surface whitespace advice.
    /// </param>
    public static void Validate(
        EthernetIpTagDefinition tag,
        string pathPrefix,
        List<ValidationIssue> errors,
        List<ValidationIssue>? warnings = null)
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
        else
        {
            // Whitespace in an address is invisible in the UI and fatal at read
            // time: libplctag refuses to build the tag and returns ErrorBadParam
            // locally, without contacting the controller, so the tag is silently
            // dead for the life of the deployment with no hint that a space is
            // the cause. Observed in the field as a single trailing space on one
            // tag of three.
            //
            // Surrounding whitespace is a WARNING, not an error: the client
            // trims it and reads correctly, so failing validation would block a
            // whole config apply — and take a source's other, healthy tags down
            // with it — over something already recovered. Interior whitespace IS
            // an error, because trimming cannot recover it and no controller
            // symbol contains a space.
            var address = tag.Address;
            var trimmed = address.Trim();

            if (trimmed.Length != address.Length)
            {
                warnings?.Add(new ValidationIssue
                {
                    Code = EthernetIpErrors.ConfigInvalid,
                    Message = $"Tag '{tag.Name}' address is stored as \"{address}\" — it has leading or " +
                              $"trailing whitespace, which is not part of the controller symbol. It will be " +
                              $"read as \"{trimmed}\". Remove the whitespace.",
                    Path = $"{pathPrefix}.Address",
                });
            }

            if (trimmed.Any(char.IsWhiteSpace))
            {
                errors.Add(new ValidationIssue
                {
                    Code = EthernetIpErrors.ConfigInvalid,
                    Message = $"Tag '{tag.Name}' address \"{address}\" contains whitespace inside the symbol " +
                              "name. Controller symbols cannot contain spaces, and this cannot be corrected " +
                              "by trimming.",
                    Path = $"{pathPrefix}.Address",
                });
            }
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
