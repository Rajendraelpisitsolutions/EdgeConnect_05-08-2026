// ============================================================================
// File: Wizards/BulkImportSourcesWizardModel.cs
// Purpose: Testable POCO state machine behind BulkImportSources.razor (PR I-2).
//          The razor binds to these properties; AdvanceStep / GoBack /
//          ResetAll mutate state. HTTP calls to the three bulk-source-merge
//          endpoints live behind IBulkSourceMergeClient (injected) so the
//          model is unit-testable without spinning up the API.
//
//          Steps mirror the five operator-visible steps in the static HTML
//          mockup (docs/mockups/bulk-provision-ui-phase1/index.html):
//
//            1. GatewayContext  — show gateway + MQTT sink count; advance
//                                  only when sink prereq is satisfied
//            2. ProtocolPicker  — operator picks one of the four protocols
//            3. CsvUpload       — operator uploads CSV, optional probe for
//                                  MTConnect, advances to preview
//            4. Preview         — operator reviews findings (blocker /
//                                  warning lists); submit gated on
//                                  PreviewResponse.CanSubmit
//            5. Confirmation    — post-submit; shows DraftId or failure
//                                  findings
//
//          Reference: docs/sessions/2026-06-14-bulk-provision-ui-phase1-v3.1-addendum.md §1
//                     docs/mockups/bulk-provision-ui-phase1/index.html
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Management.Contracts.BulkSourceMerge;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>Operator-visible wizard step. Numbering matches the mockup.</summary>
public enum BulkImportWizardStep
{
    /// <summary>Step 1 — show gateway context + MQTT sink count.</summary>
    GatewayContext = 1,

    /// <summary>Step 2 — pick protocol from the allowlist.</summary>
    ProtocolPicker = 2,

    /// <summary>Step 3 — upload CSV; MTConnect rows can be optionally probed.</summary>
    CsvUpload = 3,

    /// <summary>Step 4 — review preview findings; submit gated on canSubmit.</summary>
    Preview = 4,

    /// <summary>Step 5 — post-submit confirmation (draft id or failure).</summary>
    Confirmation = 5,
}

/// <summary>
/// Bindable POCO state for the bulk-import wizard. The razor mutates
/// properties directly via two-way binding; orchestration methods invoke
/// <see cref="IBulkSourceMergeClient"/> and update state accordingly.
/// </summary>
public sealed class BulkImportSourcesWizardModel
{
    private readonly IBulkSourceMergeClient _client;

    /// <summary>Build the model with a wired HTTP client.</summary>
    public BulkImportSourcesWizardModel(IBulkSourceMergeClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    // ── Step ──────────────────────────────────────────────────────────────────

    /// <summary>Current step shown in the wizard.</summary>
    public BulkImportWizardStep CurrentStep { get; private set; } = BulkImportWizardStep.GatewayContext;

    /// <summary>True while an HTTP call is in flight (UI disables interactions).</summary>
    public bool IsBusy { get; private set; }

    // ── Step 1 — Gateway context ─────────────────────────────────────────────
    /// <summary>Count of enabled MQTT sinks on the current gateway. Populated by the page on init.</summary>
    public int EnabledMqttSinkCount { get; set; }

    /// <summary>Available MQTT sink instance ids (operator picks one in step 2+).</summary>
    public IReadOnlyList<string> AvailableMqttSinks { get; set; } = Array.Empty<string>();

    // ── Step 2 — Protocol picker ─────────────────────────────────────────────
    /// <summary>Operator-selected protocol; null until step 2 completes.</summary>
    public BulkSourceMergeProtocol? SelectedProtocol { get; set; }

    // ── Step 3 — CSV upload ──────────────────────────────────────────────────
    /// <summary>Operator-selected CSV file name (UI display only).</summary>
    public string? CsvFileName { get; set; }

    /// <summary>CSV bytes uploaded by the operator.</summary>
    public byte[]? CsvBytes { get; set; }

    /// <summary>
    /// Selected MQTT sink when the gateway has 2+ enabled sinks. Null when
    /// the gateway has exactly one (the server auto-selects).
    /// </summary>
    public string? SelectedSinkInstanceId { get; set; }

    /// <summary>Optional operator-supplied import label written into the draft's audit trail.</summary>
    public string? ImportLabel { get; set; }

    // ── Step 4 — Preview ─────────────────────────────────────────────────────
    /// <summary>Most recent preview response from the server.</summary>
    public BulkSourceMergePreviewResponse? PreviewResult { get; private set; }

    // ── Step 5 — Confirmation ────────────────────────────────────────────────
    /// <summary>Most recent submit response from the server (success or failure).</summary>
    public BulkSourceMergeSubmitResponse? SubmitResult { get; private set; }

    /// <summary>Operator-facing message when an unexpected error occurs.</summary>
    public string? LastError { get; private set; }

    // ── Advance / back guards ────────────────────────────────────────────────

    /// <summary>True when step 1 has enough state to advance.</summary>
    public bool CanAdvanceFromGatewayContext => EnabledMqttSinkCount > 0;

    /// <summary>True when step 2 has a protocol selected.</summary>
    public bool CanAdvanceFromProtocol => SelectedProtocol.HasValue;

    /// <summary>True when step 3 has a non-empty CSV uploaded.</summary>
    public bool CanAdvanceFromUpload =>
        CsvBytes is { Length: > 0 } &&
        (EnabledMqttSinkCount == 1 || !string.IsNullOrEmpty(SelectedSinkInstanceId));

    /// <summary>True when the preview returned <see cref="BulkSourceMergePreviewResponse.CanSubmit"/>.</summary>
    public bool CanSubmit => PreviewResult is { CanSubmit: true };

    /// <summary>True when the most recent submit succeeded.</summary>
    public bool SubmitSucceeded => SubmitResult is { DraftId: not null };

    // ── Transitions ──────────────────────────────────────────────────────────

    /// <summary>Advance from step 1 → 2 — pure local transition.</summary>
    public void AdvanceFromGatewayContext()
    {
        if (!CanAdvanceFromGatewayContext)
        {
            throw new InvalidOperationException("Cannot advance — gateway has no enabled MQTT sink.");
        }
        CurrentStep = BulkImportWizardStep.ProtocolPicker;
    }

    /// <summary>Advance from step 2 → 3 — pure local transition.</summary>
    public void AdvanceFromProtocol()
    {
        if (!CanAdvanceFromProtocol)
        {
            throw new InvalidOperationException("Cannot advance — no protocol selected.");
        }
        CurrentStep = BulkImportWizardStep.CsvUpload;
    }

    /// <summary>Advance from step 3 → 4 — calls PreviewAsync and captures findings.</summary>
    public async Task AdvanceFromUploadAsync(CancellationToken cancellationToken)
    {
        if (!CanAdvanceFromUpload)
        {
            throw new InvalidOperationException("Cannot advance — CSV missing or sink not selected.");
        }
        if (SelectedProtocol is null || CsvBytes is null)
        {
            throw new InvalidOperationException("Internal state corrupted — protocol or CSV missing.");
        }

        IsBusy = true;
        LastError = null;
        try
        {
            var request = new BulkSourceMergePreviewRequest
            {
                Protocol = SelectedProtocol.Value,
                CsvBytes = CsvBytes,
                SelectedSinkInstanceId = EnabledMqttSinkCount > 1 ? SelectedSinkInstanceId : null,
                ImportLabel = ImportLabel,
            };
            PreviewResult = await _client.PreviewAsync(request, cancellationToken).ConfigureAwait(false);
            CurrentStep = BulkImportWizardStep.Preview;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Advance from step 4 → 5 — calls SubmitAsync if CanSubmit.</summary>
    public async Task AdvanceFromPreviewAsync(CancellationToken cancellationToken)
    {
        if (!CanSubmit)
        {
            throw new InvalidOperationException("Cannot submit — preview reported blockers.");
        }
        if (SelectedProtocol is null || CsvBytes is null || PreviewResult is null)
        {
            throw new InvalidOperationException("Internal state corrupted — preview must precede submit.");
        }

        IsBusy = true;
        LastError = null;
        try
        {
            var request = new BulkSourceMergeSubmitRequest
            {
                Protocol = SelectedProtocol.Value,
                CsvBytes = CsvBytes,
                SelectedSinkInstanceId = EnabledMqttSinkCount > 1 ? SelectedSinkInstanceId : null,
                ImportLabel = ImportLabel,
                BaseConfigHash = PreviewResult.BaseConfigHash,
            };
            SubmitResult = await _client.SubmitAsync(request, cancellationToken).ConfigureAwait(false);
            CurrentStep = BulkImportWizardStep.Confirmation;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Go back one step. Pure local transition; preserves prior data.</summary>
    public void GoBack()
    {
        if (CurrentStep > BulkImportWizardStep.GatewayContext)
        {
            CurrentStep = (BulkImportWizardStep)(CurrentStep - 1);
        }
    }

    /// <summary>Reset all state — used by the Cancel button and the Confirmation "Import more" link.</summary>
    public void ResetAll()
    {
        CurrentStep = BulkImportWizardStep.GatewayContext;
        SelectedProtocol = null;
        CsvFileName = null;
        CsvBytes = null;
        SelectedSinkInstanceId = null;
        ImportLabel = null;
        PreviewResult = null;
        SubmitResult = null;
        LastError = null;
        IsBusy = false;
    }

    // ── Convenience derivations for the razor ────────────────────────────────

    /// <summary>Blocker findings on the current preview, empty when no preview yet.</summary>
    public IReadOnlyList<BulkSourceMergeFinding> Blockers =>
        PreviewResult?.Findings.Where(f => f.Severity == BulkSourceMergeSeverity.Error).ToList()
        ?? (IReadOnlyList<BulkSourceMergeFinding>)Array.Empty<BulkSourceMergeFinding>();

    /// <summary>Warning findings on the current preview, empty when no preview yet.</summary>
    public IReadOnlyList<BulkSourceMergeFinding> Warnings =>
        PreviewResult?.Findings.Where(f => f.Severity == BulkSourceMergeSeverity.Warning).ToList()
        ?? (IReadOnlyList<BulkSourceMergeFinding>)Array.Empty<BulkSourceMergeFinding>();
}
