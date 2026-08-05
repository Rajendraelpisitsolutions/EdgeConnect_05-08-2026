// ============================================================================
// File: WizardValidationMessage.cs
// Purpose: Record carried by WizardValidationBanner.Messages. M.2d.1
//          deliverable per v2 plan §5.3. FieldAnchor parameter exists for
//          API stability so M.2d.2/.3 don't need to change call sites
//          later; the scroll/focus click implementation lands in M.2d.4.
// Reference: docs/sessions/2026-05-21-m2d1-shared-primitives-plan-v2.md §5.3
// ============================================================================

namespace ElpisEdgeConnect.Management.Components.Shared;

/// <summary>
/// A single validation message surfaced by <see cref="WizardValidationBanner"/>.
/// </summary>
/// <param name="Code">Stable machine-readable code (e.g. <c>"identity.instanceId.empty"</c>).</param>
/// <param name="Path">Logical configuration path the message applies to (e.g. <c>"Sources[0].InstanceId"</c>).</param>
/// <param name="Message">Human-readable text rendered in the banner.</param>
/// <param name="FieldAnchor">
/// Optional DOM anchor (e.g. <c>"#field-instance-id"</c>) for the field
/// associated with this message. M.2d.1 carries the parameter for API
/// stability; the click-to-scroll/focus behaviour lands in M.2d.4
/// alongside the cross-wizard validation standardisation sweep.
/// </param>
public sealed record WizardValidationMessage(
    string Code,
    string Path,
    string Message,
    string? FieldAnchor = null);
