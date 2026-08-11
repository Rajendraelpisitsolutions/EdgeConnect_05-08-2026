// ============================================================================
// File: Configuration/ConfigurationValidator.cs
// Purpose: Orchestrates the typed-object validation pipeline:
//          (1) DataAnnotations walk over every record
//          (2) Cross-record invariants (CrossRecordValidator)
//          (3) License gate (ILicenseGate)
//
// Schema validation of raw JSON happens earlier, at the file-loading
// boundary, via IConfigurationSchemaValidator. By the time a
// GatewayConfiguration reaches this validator, structural validity is
// already established by System.Text.Json's `required` modifier handling.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone B2
// ============================================================================

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Licensing;

// Disambiguate the two ValidationResult types: ours (our typed adapter result)
// and DataAnnotations'. This file uses our type pervasively; the DataAnnotations
// type only appears inside ValidateOne where it's referenced by full namespace.
using ValidationResult = ElpisEdgeConnect.Core.Adapters.ValidationResult;

namespace ElpisEdgeConnect.Core.Configuration;

/// <summary>
/// Orchestrates the typed configuration validation pipeline. The pipeline
/// runs three stages in order; if a stage produces errors, all subsequent
/// stages still run so the user sees every problem in one pass (rather
/// than fix-one-find-the-next).
/// </summary>
public sealed class ConfigurationValidator
{
    private readonly CrossRecordValidator _crossRecordValidator;
    private readonly ILicenseGate _licenseGate;

    /// <summary>
    /// Construct a validator with explicit dependencies. Tests use this
    /// to inject stub gates; production wiring uses the parameterless
    /// constructor which defaults to the singleton instances.
    /// </summary>
    public ConfigurationValidator(CrossRecordValidator crossRecordValidator, ILicenseGate licenseGate)
    {
        _crossRecordValidator = crossRecordValidator
            ?? throw new System.ArgumentNullException(nameof(crossRecordValidator));
        _licenseGate = licenseGate
            ?? throw new System.ArgumentNullException(nameof(licenseGate));
    }

    /// <summary>
    /// Default validator using <see cref="CrossRecordValidator.Instance"/>
    /// and <see cref="AllowAllLicenseGate.Instance"/>. Used by callers that
    /// don't need to override either dependency.
    /// </summary>
    public ConfigurationValidator()
        : this(CrossRecordValidator.Instance, AllowAllLicenseGate.Instance)
    {
    }

    /// <summary>
    /// Validate <paramref name="configuration"/> through every stage and
    /// return an aggregated <see cref="ValidationResult"/>.
    /// </summary>
    public async ValueTask<ValidationResult> ValidateAsync(
        GatewayConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (configuration is null)
        {
            return ValidationResult.Failure(CoreErrors.ConfigInvalid, "Configuration is null.");
        }

        var allErrors = new List<ValidationIssue>();
        var allWarnings = new List<ValidationIssue>();

        // Stage 1: DataAnnotations walk
        var dataAnnotationsResult = ValidateDataAnnotations(configuration);
        AddIssues(allErrors, allWarnings, dataAnnotationsResult);

        // Stage 2: cross-record invariants
        var crossRecordResult = _crossRecordValidator.Validate(configuration);
        AddIssues(allErrors, allWarnings, crossRecordResult);

        // Stage 3: license gate (B2 hand-off; AllowAll by default)
        cancellationToken.ThrowIfCancellationRequested();
        var licenseResult = await _licenseGate.EvaluateAsync(configuration, cancellationToken).ConfigureAwait(false);
        if (!licenseResult.Allowed)
        {
            for (var i = 0; i < licenseResult.Reasons.Count; i++)
            {
                allErrors.Add(new ValidationIssue
                {
                    Code = CoreErrors.LicenseModuleDisabled,
                    Message = licenseResult.Reasons[i],
                    Path = i < licenseResult.BlockedModules.Count
                        ? $"Modules[{licenseResult.BlockedModules[i]}]"
                        : null,
                });
            }

            if (licenseResult.Reasons.Count == 0)
            {
                allErrors.Add(new ValidationIssue
                {
                    Code = CoreErrors.LicenseModuleDisabled,
                    Message = "License gate rejected configuration without specifying a reason.",
                });
            }
        }

        if (allErrors.Count == 0 && allWarnings.Count == 0)
        {
            return ValidationResult.Success();
        }

        return new ValidationResult
        {
            IsValid = allErrors.Count == 0,
            Errors = allErrors,
            Warnings = allWarnings,
        };
    }

    /// <summary>
    /// Walk every record reachable from <paramref name="configuration"/>
    /// and run <c>Validator.TryValidateObject</c> on it. The walk is
    /// hand-coded rather than reflection-driven so it remains fast and
    /// the set of validated records is explicit.
    /// </summary>
    public ValidationResult ValidateDataAnnotations(GatewayConfiguration configuration)
    {
        if (configuration is null)
        {
            return ValidationResult.Failure(CoreErrors.ConfigInvalid, "Configuration is null.");
        }

        var errors = new List<ValidationIssue>();

        // Gateway-level
        ValidateOne(configuration.Gateway, "Gateway", errors);
        ValidateOne(configuration.Gateway.ManagementApi, "Gateway.ManagementApi", errors);
        ValidateOne(configuration.Gateway.Watchdog, "Gateway.Watchdog", errors);

        // Sources
        foreach (var source in configuration.Sources)
        {
            var prefix = $"Sources[{source.InstanceId}]";
            ValidateOne(source, prefix, errors);
            ValidateOne(source.Polling, $"{prefix}.Polling", errors);
        }

        // Sinks
        foreach (var sink in configuration.Sinks)
        {
            var prefix = $"Sinks[{sink.InstanceId}]";
            ValidateOne(sink, prefix, errors);
            ValidateOne(sink.Publishing, $"{prefix}.Publishing", errors);
        }

        // Routes
        foreach (var route in configuration.Routes)
        {
            var prefix = $"Routes[{route.RouteId}]";
            ValidateOne(route, prefix, errors);
            ValidateOne(route.Buffer, $"{prefix}.Buffer", errors);
            ValidateOne(route.Delivery, $"{prefix}.Delivery", errors);
        }

        return errors.Count == 0
            ? ValidationResult.Success()
            : new ValidationResult { IsValid = false, Errors = errors };
    }

    private static void ValidateOne(object instance, string pathPrefix, List<ValidationIssue> errors)
    {
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var context = new ValidationContext(instance);
        if (Validator.TryValidateObject(instance, context, results, validateAllProperties: true))
        {
            return;
        }

        foreach (var r in results)
        {
            var memberPath = r.MemberNames.FirstOrDefault();
            errors.Add(new ValidationIssue
            {
                Code = CoreErrors.ConfigInvalidValue,
                Message = r.ErrorMessage ?? "Validation failed.",
                Path = memberPath is null ? pathPrefix : $"{pathPrefix}.{memberPath}",
            });
        }
    }

    private static void AddIssues(
        List<ValidationIssue> errors,
        List<ValidationIssue> warnings,
        ValidationResult result)
    {
        foreach (var e in result.Errors)
        {
            errors.Add(e);
        }
        foreach (var w in result.Warnings)
        {
            warnings.Add(w);
        }
    }
}
