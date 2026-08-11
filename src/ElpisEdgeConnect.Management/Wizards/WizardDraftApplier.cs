// ============================================================================
// File: Wizards/WizardDraftApplier.cs
// Purpose: Shared "Save = save" tail for the add-entity wizards.
//
//          Historically each Add wizard only CREATED a config draft
//          (POST /api/v1/config/drafts) and then sent the operator to the
//          Config page to Validate + Apply manually — so clicking "Save"
//          did not persist the source/sink/route (the tags/addresses just
//          sat in an unapplied draft). Edit mode, by contrast, applied
//          immediately (PUT). This helper makes Add behave like Edit:
//          it creates the draft AND applies it through the same audited
//          draft -> validate -> apply pipeline, so Save actually saves.
//
//          On a validation conflict the draft is KEPT and the operator is
//          routed to the Config page (with the draft pre-selected) to fix
//          and apply it, so nothing is silently lost.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>
/// Creates a config draft and applies it in one step for the Add wizards,
/// with consistent success / validation-conflict handling and navigation.
/// </summary>
public static class WizardDraftApplier
{
    /// <summary>
    /// POST the draft, then POST apply. On success show a success snackbar and
    /// navigate to <paramref name="successHref"/>. On a validation conflict keep
    /// the draft and route to the Config page so the operator can resolve it.
    /// </summary>
    /// <param name="http">Injected Studio <see cref="HttpClient"/>.</param>
    /// <param name="nav">Injected <see cref="NavigationManager"/>.</param>
    /// <param name="snackbar">Injected MudBlazor <see cref="ISnackbar"/>.</param>
    /// <param name="draftBody">
    /// The draft request body — the same object each wizard already builds:
    /// <c>new { configuration = newDraft, actor = "system" }</c>.
    /// </param>
    /// <param name="entityLabel">Human label for the success message, e.g. <c>"Source 'plc-1'"</c>.</param>
    /// <param name="successHref">Where to go after a successful apply, e.g. the entity's detail page.</param>
    /// <param name="actor">Audit actor for the apply.</param>
    public static async Task CreateApplyAndNavigateAsync(
        HttpClient http,
        NavigationManager nav,
        ISnackbar snackbar,
        object draftBody,
        string entityLabel,
        string successHref,
        string actor = "studio-ui")
    {
        var resp = await http.PostAsJsonAsync("/api/v1/config/drafts", draftBody);
        if (!resp.IsSuccessStatusCode)
        {
            snackbar.Add($"Save rejected: {await resp.Content.ReadAsStringAsync()}", Severity.Error);
            return;
        }

        var created = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        var draftId = created is { } d && d.TryGetValue("draftId", out var id) ? id : null;
        if (draftId is null)
        {
            snackbar.Add(
                "Draft created, but its id was not returned. Open Config to Validate and Apply.",
                Severity.Warning);
            nav.NavigateTo("/config");
            return;
        }

        var applyResp = await http.PostAsJsonAsync(
            $"/api/v1/config/drafts/{Uri.EscapeDataString(draftId)}/apply",
            new { actor });

        if (applyResp.StatusCode == HttpStatusCode.Conflict)
        {
            var raw = await applyResp.Content.ReadAsStringAsync();
            snackbar.Configuration.VisibleStateDuration = 10_000;
            snackbar.Add(
                $"Save could not be applied (validation failed): {raw}. "
                    + "The draft was kept — open Config to fix and apply.",
                Severity.Error);
            nav.NavigateTo($"/config?new={Uri.EscapeDataString(draftId)}");
            return;
        }
        if (!applyResp.IsSuccessStatusCode)
        {
            snackbar.Add($"Save failed to apply: {await applyResp.Content.ReadAsStringAsync()}", Severity.Error);
            nav.NavigateTo($"/config?new={Uri.EscapeDataString(draftId)}");
            return;
        }

        snackbar.Add($"{entityLabel} saved.", Severity.Success);
        nav.NavigateTo(successHref);
    }
}
