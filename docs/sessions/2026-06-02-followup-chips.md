# Follow-up chips — 2026-06-02 (Live Data Tap session)

Durable record of follow-ups spun off as CCD chips this session, so a dismissed
chip never loses the note. Each block is the self-contained prompt for its chip.

---

## 1. Settings UI for `gateway.sensitiveTags` (Live Tap value-privacy)

**Priority:** low (fine for launch — the field works; this is operator
ergonomics). **Spun off as a chip:** yes.

`gateway.sensitiveTags` was added in the Live Data Tap work (M1.5 / ADR-0018A).
It is the allowlist of tag-name patterns (exact or glob, case-insensitive) whose
live VALUES are masked (`***`) on the Live Data Tap — a value-privacy control
for diagnostics. Today it is **config-JSON only**: an operator must hand-edit
`current.json` (`gateway.sensitiveTags: ["recipe/*", ...]`) to use it. There is
no Studio surface to view/edit it.

**Task:** add a small Settings surface to manage `gateway.sensitiveTags` —
list/add/remove patterns, going through the normal draft → validate → apply
config flow (NOT a direct write). Likely lives on a Settings/Config page.

**Pointers:**
- Field: `src/ElpisEdgeConnect.Core/Configuration/GatewaySettings.cs`
  → `SensitiveTags` (`IReadOnlyList<string>`, `[BundleTier.Include]`).
- Policy/masker it drives: `Core/Diagnostics/SensitiveTagPolicy.cs`,
  `TapValueMasker.cs`; live-reloaded via `SensitiveTagPolicyProvider`
  (wired in `Host/CompositionRoot.cs` → `IRouteTap`).
- ADR-0018A documents the policy semantics.
- Config edits MUST honor the draft/validate/apply/rollback flow (CLAUDE.md §9.10).
- Patterns are not secret, so no redaction concerns in the UI.

**Acceptance:** an operator can add/remove a sensitive-tag pattern in the Studio,
apply it, and see the Live Data Tap immediately mask matching tag values
(the masker is reload-correct, so no restart needed).

---

## (For the fuller Live Tap roadmap — Inspect v1.1, Compare v1.2, reservoir
sampling, source/route-scoped masking — see
`2026-06-02-live-data-tap-stream-handoff.md` "Deferred follow-ups" and
ADR-0018 "Implementation status". Those are larger tracks, not chips.)
