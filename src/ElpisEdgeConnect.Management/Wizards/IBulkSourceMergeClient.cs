// ============================================================================
// File: Wizards/IBulkSourceMergeClient.cs
// Purpose: Seam over the HTTP calls to the bulk-source-merge API. The wizard
//          model takes this as a constructor dependency so it stays
//          unit-testable without spinning up Kestrel.
// ============================================================================

using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Management.Contracts.BulkSourceMerge;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>HTTP-call seam for the bulk-source-merge endpoints.</summary>
public interface IBulkSourceMergeClient
{
    /// <summary>POST /api/v1/sources/bulk-preview.</summary>
    Task<BulkSourceMergePreviewResponse> PreviewAsync(
        BulkSourceMergePreviewRequest request,
        CancellationToken cancellationToken);

    /// <summary>POST /api/v1/sources/bulk-submit.</summary>
    Task<BulkSourceMergeSubmitResponse> SubmitAsync(
        BulkSourceMergeSubmitRequest request,
        CancellationToken cancellationToken);
}
