# Cross-Wizard Consistency Audit — M.2d.4

**Scope:** Six wizards — three source (Focas2, Brother HTTP, Modbus TCP), two sink (MQTT, OpcUa Server), one route (AddRoute).
**Initial snapshot:** 2026-05-27, against branch `claude/m2d4-impl` (forked from master `421f112`).
**Post-M.2d.4 update:** 2026-05-27, after Steps 3–7 implementation.
**Baseline plan reference:** [`docs/sessions/2026-05-27-m2d4-cross-wizard-sweep-plan-v2.1.md`](../../../docs/sessions/2026-05-27-m2d4-cross-wizard-sweep-plan-v2.1.md)
**Wizard contract:** [`docs/decisions/0015-wizard-contract.md`](../../../docs/decisions/0015-wizard-contract.md)

This document captures the **current state** of consistency across all six wizards. Every "✓" was verified post-M.2d.4. Every "n/a" is documented in ADR-0015 as a deliberate carve-out.

The checklist is verified manually at PR review time. Drift (cells regressing from ✓ to ✗ in future milestones) is the trigger for considering an automated drift test.

---

## Section A — Structural primitives adoption

| Aspect | Focas2 | Brother | Modbus | MQTT | OpcUa | Route |
|---|---|---|---|---|---|---|
| Uses `WizardShell` | ✓ | ✓ | ✓ | ✓ M.2d.4 | ✓ M.2d.4 | n/a (wiring wizard, different shape — ADR-0015 Rule 1 carve-out) |
| Uses `WizardSection` for numbered sections | ✓ | ✓ | ✓ | ✓ M.2d.4 | ✓ M.2d.4 | n/a |
| Uses `WizardActions` for footer | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Manual `MudPaper` + hardcoded "N. Title" | — | — | — | — (removed) | — (removed) | — |

**Status: closed.** Steps 4–5 adopted `WizardShell + WizardSection` in both sink wizards. OpcUa's pre-existing collapsible expansion-panel sections (Namespace, Capacity) converted to flat `WizardSection` with `Description` captions — collapsibility deferred as a follow-up enhancement, not a regression of the contract.

---

## Section B — Validation banner adoption (`WizardValidationBanner`)

| Aspect | Focas2 | Brother | Modbus | MQTT | OpcUa | Route |
|---|---|---|---|---|---|---|
| Renders `WizardValidationBanner` in template | ✓ M.2d.4 | ✓ M.2d.4 | ✓ M.2d.4 | ✓ M.2d.4 | ✓ M.2d.4 | ☐ deferred (Q7 partial fit) |
| Validation surface emits `WizardValidationMessage` list | ✓ razor `BuildValidationMessages()` | ✓ razor `BuildValidationMessages()` | ✓ razor `BuildValidationMessages()` (model `ValidateTag` per row) | ✓ model `Validate()` | ✓ model `Validate()` | ☐ inline today |
| Error severity mapping | ✓ | ✓ | ✓ | ✓ | ✓ | n/a |
| Banner renders zero DOM when empty | ✓ (Rule 5 — no success state) | ✓ | ✓ | ✓ | ✓ | n/a |
| Click-to-scroll on FieldAnchor click | ✓ JS interop (Step 3) | ✓ | ✓ (top-level only; per-row anchors deferred) | ✓ | ✓ | n/a |

**Status: closed for 5 protocol wizards.** All five consume `WizardValidationBanner` and the JS-interop scroll-to-field works via the shared `wizardValidation.scrollToFieldAnchor` helper (Step 3 deliverable). Route's banner wiring is intentionally deferred per ADR-0015 Q7 (route is a wiring wizard with partial fit; its inline `RouteFilterEditorModel` validation surface is already operator-visible).

**Modbus per-row caveat:** the per-row tag-table cell errors are aggregated into the banner with `FieldAnchor=null` (the table cells don't carry stable DOM ids yet). Clicking those messages is inert; they're still visible. Adding row-cell anchors is a tracked follow-up.

---

## Section C — Field anchor / scroll-to-field (R1 from v2.1 plan)

| Aspect | Focas2 | Brother | Modbus | MQTT | OpcUa | Route |
|---|---|---|---|---|---|---|
| Validatable fields carry `id="field-{anchor}"` via `UserAttributes` | ✓ (instance-id) | ✓ (instance-id) | ✓ (instance-id; per-row deferred) | ✓ (all model-validated fields) | ✓ (all model-validated fields) | ☐ deferred |
| `WizardValidationBanner` invokes JS interop on click | ✓ default behaviour | ✓ | ✓ | ✓ | ✓ | n/a |
| `WizardValidationBanner.OnMessageClick` implements scroll/focus | ✓ Step 3 — `wizardValidation.scrollToFieldAnchor` JS interop | — | — | — | — | — |
| Kebab-case path naming applied uniformly | ✓ | ✓ | ✓ | ✓ | ✓ | n/a |

**Status: closed.** Field anchors apply uniformly across the 5 protocol wizards for top-level identity + model-validated fields. Per-row table cells (Modbus tag rows) defer to a follow-up because the per-cell DOM contract requires designing stable ids for nested tables; the banner still surfaces these errors, just without click-to-scroll for now.

---

## Section D — Save-flow contract

| Aspect | Focas2 | Brother | Modbus | MQTT | OpcUa | Route |
|---|---|---|---|---|---|---|
| `CanSave()` returns true iff zero errors | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Warnings do not block Save | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Save commits to draft via POST, never bypasses draft flow | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Edit-mode PUT direct-apply path uses optimistic concurrency | ✓ M.2d.2 | ✓ M.2d.2 | ✓ M.2d.2 | ✓ M.2d.3 | ✓ M.2d.3 | ✓ M.2d.3 |
| Save button copy: "Save as draft" (Add) / "Save changes" (Edit) | ✓ (via WizardActions default) | ✓ | ✓ | ✓ | ✓ | ✓ |
| Cancel discards draft and navigates back | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| No auto-save / localStorage persistence | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |

**M.2d.4 closes:** Already consistent — documented in ADR-0015 §5 save-flow contract.

---

## Section E — Test Connection probe

| Aspect | Focas2 | Brother | Modbus | MQTT | OpcUa | Route |
|---|---|---|---|---|---|---|
| Has Test Connection / probe button | ✓ | ✓ | ✓ | ✓ | ✗ (acceptor design — bind side-effects) | n/a (route doesn't probe) |
| Button label | "Browse Controller" | "Test Connection" | "Test Connection" | "Test Connection" | n/a | — |
| Idempotent / no side effects on running adapter | ✓ M.2b.3 | ✓ M.2d.2 | ✓ M.2d.2 | ✓ M.2b.6 | n/a | — |
| Result surfaced as inline panel (not snackbar) | ✓ | ✓ | ✓ | ✓ inline alert | n/a | — |
| Uses wizard's current edited field values | ✓ M.2d.2 | ✓ M.2d.2 | ✓ M.2d.2 | ✓ M.2d.3 | n/a | — |

**M.2d.4 closes:** Label inconsistency — Focas2 uses product name "Browse Controller", others use generic "Test Connection". Decision: keep Focas2 as-is (product name is intentional, documented in ADR-0015). All others stay "Test Connection".

---

## Section F — Edit-mode behaviour (M.2d.2 + M.2d.3 deliverable)

| Aspect | Focas2 | Brother | Modbus | MQTT | OpcUa | Route |
|---|---|---|---|---|---|---|
| Has `HydrateFromExisting` on wizard model | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| `EditModeContext` parameter accepted | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| InstanceId disabled in edit mode | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Edit URL: `/{entity}/{id}/edit` via mirror-router | ✓ SourceEditRouter | ✓ SourceEditRouter | ✓ SourceEditRouter | ✓ SinkEditRouter | ✓ SinkEditRouter | ✓ RouteEditRouter |
| `StaleEditWarningBanner` on 409 | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Edit-mode hydration banner ("Editing runtime configuration") | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Pencil-icon edit entry from list page | ✓ Sources.razor | ✓ | ✓ | ✓ M.2d.3 | ✓ M.2d.3 | ✓ M.2d.3 |
| Pencil in Action column with stopPropagation | ✓ | ✓ | ✓ | ✓ M.2d.3 fix | ✓ M.2d.3 fix | ✓ M.2d.3 fix |

**M.2d.4 closes:** Already consistent post-M.2d.3.

---

## Section G — Per-instance validator composition

| Aspect | Focas2 | Brother | Modbus | MQTT | OpcUa | Route |
|---|---|---|---|---|---|---|
| Has per-instance items (tag table, NodeId list, etc.) | ✗ (paths only) | ✗ (fixed catalog) | ✓ tag table | ✗ (scalar topic) | ✗ (scalar endpoint) | partial (filter rules, transform rows) |
| Per-instance validator exists as `static class` | n/a | n/a | ✓ `ModbusTagValidator` | n/a | n/a | inline in `RouteFilterEditorModel` |
| Wizard composes validator (not inline rules) | n/a | n/a | ✓ `ComposeValidationForRow` | n/a | n/a | partial |
| Adapter `ValidateConfigAsync` shares validator | n/a | n/a | ✓ | n/a | n/a | n/a (route has no adapter) |

**M.2d.4 closes:** Scope is correctly collapsed (v2.1 §1 Q-locks). No new validators needed; ADR-0015 documents the pattern as canonical for protocols that DO have per-instance items, and explicitly carves out wizards that don't.

---

## Section H — UX polish (minor consistency)

| Aspect | Focas2 | Brother | Modbus | MQTT | OpcUa | Route |
|---|---|---|---|---|---|---|
| Title in `WizardShell` header band | ✓ | ✓ | ✓ | ✓ M.2d.4 | ✓ M.2d.4 | n/a (no shell) |
| Section numbers auto-driven by `WizardSection.Index` | ✓ | ✓ | ✓ | ✓ M.2d.4 | ✓ M.2d.4 | n/a |
| Loading state uses `MudProgressLinear` (not `MudSkeleton`) | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Snackbar copy template consistent | ✓ via service | ✓ | ✓ | ✓ | ✓ | ✓ |
| `MudGrid Spacing="2"` for field grids | ✓ | ✓ | ✓ | ✓ | ✓ | n/a |
| Back-arrow tooltip text | "Back" via shell | "Back" | "Back" | "Back" via shell | "Back" via shell | "Back to routes" |
| Save button label | "Save as draft" / "Save changes" via `WizardActions` | ✓ same | ✓ same | ✓ same | ✓ same | ✓ same |
| Test Connection label | "Browse Controller" (locked product name) | "Test Connection" | "Test Connection" | "Test Connection" | n/a (acceptor — no probe, ADR-0015 Rule 6 carve-out) | n/a |

**Status: closed.** WizardShell adoption auto-fixes title, section numbering, back-arrow tooltip for sinks.

---

## Summary scorecard (post-M.2d.4)

| Wizard | Status |
|---|---|
| **Focas2** | Shell ✓, sections ✓, actions ✓, banner ✓, anchors ✓ (instance-id) |
| **Brother** | Shell ✓, sections ✓, actions ✓, banner ✓, anchors ✓ (instance-id) |
| **Modbus** | Shell ✓, sections ✓, actions ✓, banner ✓ (incl. per-row tag errors), anchors ✓ (top-level; per-row deferred) |
| **MQTT** | Shell ✓ M.2d.4, sections ✓ M.2d.4, actions ✓, banner ✓ M.2d.4, anchors ✓ M.2d.4 |
| **OpcUa Server** | Shell ✓ M.2d.4, sections ✓ M.2d.4, actions ✓, banner ✓ M.2d.4, anchors ✓ M.2d.4 |
| **Route** | Manual layout (n/a for shell — ADR-0015 carve-out), actions ✓, banner deferred per Q7 (partial fit) |

**Closed in M.2d.4:**
1. ✓ `WizardValidationBanner` wired in all 5 protocol wizards.
2. ✓ `WizardShell + WizardSection` adopted in both sink wizards.
3. ✓ Field-anchor `id` attributes on all model-validated fields across the 5 protocol wizards.
4. ✓ `WizardValidationBanner.OnMessageClick` JS-interop scroll-to-field implementation.
5. ✓ ADR-0015 wizard contract locked.

**Deferred follow-ups (tracked, not regressions):**
- **Route banner wiring.** Route is a wiring wizard, not an authoring wizard; the per-instance-validator pattern is a partial fit (filter rules / transforms). Wire when those validation surfaces are next touched.
- **Modbus per-row cell anchors.** Banner aggregates per-row tag-table errors today but they're rendered with `FieldAnchor=null` (clicking is inert). Designing a stable per-cell DOM-id scheme is a future enhancement.
- **OpcUa Namespace / Capacity collapsibility.** Pre-M.2d.4 design used `MudExpansionPanel` for these "advanced" sections; the M.2d.4 refactor flattens them to standard `WizardSection`. If operator feedback wants them collapsed-by-default, add a collapsibility option to `WizardSection` rather than reverting to ad-hoc panels.
