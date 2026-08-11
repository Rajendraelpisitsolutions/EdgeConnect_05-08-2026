// ============================================================================
// File: Api/S7TagTemplateApi.cs
// Purpose: GET /api/v1/sources/s7/tag-template.csv — downloads a starter CSV
//          for the S7 wizard's "Import CSV" affordance. Encodes the exact
//          header the importer expects plus a few safe example rows, so
//          operators don't have to guess column names. Deliberately omits
//          Timer/Counter and planner-rejected datatype/address examples.
// Reference: docs/sessions/2026-06-03-s7-source-wizard-handoff.md (CSV v1.1)
// ============================================================================

using System;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ElpisEdgeConnect.Management.Api;

/// <summary>Endpoint registration for the S7 CSV tag-import template download.</summary>
public static class S7TagTemplateApi
{
    /// <summary>The downloadable template filename.</summary>
    public const string FileName = "s7-tags-template.csv";

    /// <summary>
    /// Template content — the importer's full header plus safe examples. Only
    /// Name + Address are strictly required; datatype is derived when blank and
    /// scan rate defaults to 1000 ms (shown explicitly here for clarity).
    /// </summary>
    public const string TemplateCsv =
        "Name,Address,Datatype,ScanRateMs,Unit,Scale,Offset\n" +
        "spindle_rpm,DB1.DBW0,Int,1000,rpm,1,0\n" +
        "estop_active,M10.2,Bool,500,,1,0\n" +
        "temperature,DB1.DBD4,Real,1000,degC,1,0\n";

    /// <summary>Map the v1 S7 tag-template endpoint onto <paramref name="builder"/>.</summary>
    public static IEndpointRouteBuilder MapS7TagTemplateApi(this IEndpointRouteBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.MapGet("/api/v1/sources/s7/tag-template.csv", () =>
            Results.File(Encoding.UTF8.GetBytes(TemplateCsv), "text/csv", FileName))
            .WithName("DownloadS7TagTemplate")
            .WithTags("Sources")
            .WithSummary("Download a starter CSV (correct header + safe examples) for S7 tag import.");

        return builder;
    }
}
