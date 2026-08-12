# M.2d.4 — Cross-wizard consistency sweep (v1 plan, BRIEF)

**Status:** v1 — DRAFT, OPEN QUESTIONS BELOW, pending ChatGPT review pass
**Date:** 2026-05-21
**Form:** Brief v1 per roadmap §2 — "brief v1 per sub-milestone"
**Predecessor (roadmap):** [v2 wrap-up roadmap §3.7.4](2026-05-21-phase2-wrapup-roadmap-v2.md)
**Hard precondition:** M.2d.1 + M.2d.2 + M.2d.3 all landed (this is the LAST M.2d sub-milestone — it sweeps across what the prior three built)
**Estimated size:** ~2-3 days per roadmap §3.7.4

---

## 1. Goal

The final consistency sweep across the six wizards (Brother HTTP source, FOCAS2 source, Modbus source, MQTT sink, OPC UA Server sink, Route). M.2d.1 built the shared primitives, M.2d.2 + M.2d.3 retro-fitted them onto the wizards. **M.2d.4 makes the wizards interchangeable in feel and identical in contract:** all six speak the same per-instance validation idiom (the `ModbusTagValidator` composition pattern, generalised), surface validation banners with the same severity classes and link-to-field behaviour, and conform to a written-down wizard contract captured as an ADR. The deliverable answers "what is a wizard, exactly?" — not in code, but in a single doc that future protocols (OPC UA Client, S7, Modbus RTU, MTConnect, ...) extend without re-inventing.

---

## 2. Hard precondition (LOCKED)

This sub-milestone CANNOT start until M.2d.1, M.2d.2, AND M.2d.3 are merged to `master`. The sequencing is non-negotiable per roadmap §3.7 and §4.6 coordination-risk mitigation:

| Sub-milestone | Provides | Why M.2d.4 needs it |
|---|---|---|
| M.2d.1 | `WizardShell`, `WizardValidationBanner`, `WizardWatchSlot`, `WizardActions`, `EditModeContext` | M.2d.4 unifies the *behaviour* of these primitives; they must exist and be in use everywhere first. |
| M.2d.2 | Source wizards (Brother HTTP, FOCAS2, Modbus) on the shared shell + Test Connection on all three | M.2d.4 generalises the Modbus per-instance validator to FOCAS2 + Brother. Both source wizards must be on the shared shell before that's even meaningful. |
| M.2d.3 | Sink wizards (MQTT, OPC UA Server) + Route wizard on the shared shell | M.2d.4 sweeps all six, not three. Sink wizards must be on the shared shell to participate in the validation-banner audit. |

**Pause-point:** if any of M.2d.1/.2/.3 ships with deferred items that affect the wizard contract (e.g., M.2d.3 punts the route wizard's Test Connection semantics), surface and resolve BEFORE the ADR is drafted. The ADR documents the actual contract, not an aspirational one.

---

## 3. The `ModbusTagValidator` composition pattern — generalised

### What it does today (M.2b.6.2 baseline)

`src/ElpisEdgeConnect.Sources.ModbusTcp/ModbusTagValidator.cs` is a **pure static helper** that validates a single `ModbusTagDefinition` and appends `ValidationIssue` entries to a caller-supplied list. Three callers — the adapter's `ValidateConfigAsync`, the F4 CSV importer, and `ModbusSourceWizardModel.ValidateTag(...)` — all delegate to it. Zero rule duplication; when the adapter learns a new datatype, every caller inherits the rule for free.

The wizard does NOT re-implement the rules. It composes:

```
ModbusSourceWizardModel.ValidateTag(ModbusTagWizardRow row)
  → map row to ModbusTagDefinition
  → call ModbusTagValidator.Validate(definition, pathPrefix: "TagDefinitions[i]", errors)
  → surface errors on the wizard row (Error + ErrorText per cell)
```

This is the **Locked-N composition pattern** — N callers all bind to one validator, the validator is single source of truth, no rule lives in more than one place.

### Generalising — the per-instance validator interface (NEW)

M.2d.4 lifts the pattern into a Core-level interface (name OPEN — see Q1 below) and generalises to every protocol's per-instance object:

| Protocol | Per-instance validator | What it validates |
|---|---|---|
| Modbus TCP | `ModbusTagValidator` (already exists) | A `ModbusTagDefinition` — datatype/register-class/byte-order compatibility, scale/offset applicability, string-length, etc. |
| FOCAS2 | NEW `Focas2RegisterValidator` (or similar — name OPEN) | A `Focas2DataPointPath` — must resolve to a known canonical-catalog path; deprecated paths emit Warning. |
| Brother HTTP | NEW `BrotherDataPointValidator` | A Brother `DataPoints` filter entry — must resolve to the Brother catalog path set. |
| MQTT sink | NEW `MqttTopicTemplateValidator` (if applicable — see Q2) | Per-topic-template validation: forbidden chars, placeholder resolution, length limits. |
| OPC UA Server sink | NEW `OpcUaServerNodeIdValidator` | Per-NodeId validation: namespace index range, identifier-type/encoding pairing. |
| Route | NEW `RouteFilterValidator` (if applicable — Route already has `RouteFilterEditorModel`) | Per-filter-rule validation: tag-path syntax, range bounds. |

**Common shape (sketch — name + signature OPEN for v2):**

```csharp
public interface IPerInstanceValidator<TItem>
{
    void Validate(TItem item, string pathPrefix, List<ValidationIssue> errors);
}
```

Notes:
- Static helper or interface-implementing class — open question (Q3). `ModbusTagValidator` today is a `static class`; lifting to an interface gives DI registration + mocking. Trade-off vs. ceremony.
- The `pathPrefix` discipline (so issues can be qualified `"TagDefinitions[3].ByteOrder"` from wizard / `"row 17"` from CSV) is preserved across protocols.
- The wizard-side composition is identical everywhere: `WizardModel.ValidateTag(row)` → map → call validator → surface per-row errors.

### Out of scope for M.2d.4

- **Cross-instance validation** (e.g., duplicate `instanceId` across sources) stays in `WizardConfigMerger`. The per-instance validators are scoped per item.
- **Adapter-side per-tag wiring beyond what already exists.** If a protocol's adapter doesn't already do per-item validation in `ValidateConfigAsync`, M.2d.4 does NOT add it. The deliverable is to *generalise existing patterns*, not to invent new adapter contracts.
- **CSV importers for protocols that don't have them.** Modbus has F4; nothing else does. M.2d.4 doesn't add importers.

---

## 4. Validation-banner unification

Every wizard surfaces validation results through `WizardValidationBanner` (M.2d.1 deliverable). M.2d.4 audits and unifies the banner's behaviour across all six wizards:

| Aspect | Locked target |
|---|---|
| Severity classes | **Error / Warning / Info** — same three across all wizards. Maps 1:1 to `ValidationIssueSeverity`. |
| Visual treatment | Same `MudAlert` `Severity` mapping (`Error`, `Warning`, `Info`). Same outline / fill / icon vocabulary. |
| Link-to-field behaviour | Clicking an issue with a `Path` scrolls to + focuses the field. Same DOM-anchor / `id` discipline across wizards. |
| Auto-clear-on-fix | When the operator fixes the field, the issue disappears from the banner without a banner-level "Re-validate" button. Driven by `WizardModel.Validate()` being called on every field change. |
| Issue ordering | Errors first, then Warnings, then Info. Within a severity, ordered by `Path` lexicographically (deterministic). |
| Empty state | Banner not rendered when issue list is empty. No "All good!" green banner — absence is the success signal (already established UX baseline). |
| Save-gating | `CanSave()` returns true iff there are zero Error-severity issues. Warnings do NOT block save. Info NEVER blocks. |

**Audit deliverable:** a short markdown checklist in `tests/ElpisEdgeConnect.Management.Tests/Wizards/CrossWizardConsistencyAuditChecklist.md` (or similar — see Q4 below) that lists each row above and which wizards comply today. M.2d.4 closes every open box.

---

## 5. Final UX polish

Roadmap §3.7.4 says "Final UX polish + docs updates." This is intentionally a **brief v1** — the concrete polish list emerges from M.2d.2/.3 implementation experience. v2 will name specific items.

Candidate polish items to watch for during M.2d.2/.3 (working hypotheses, not commitments):

- **Section numbering / step indicator consistency** — every wizard numbers its sections the same way (1, 2, 3, ... not "Step 1 of N" mixed with bare numerals).
- **Keyboard navigation** — Enter / Esc / Tab semantics identical across wizards (Esc cancels with confirm-dialog; Ctrl+Enter saves if `CanSave()`).
- **Loading-state placeholders** — `MudProgressLinear` vs `MudSkeleton` chosen consistently for "current config loading" states.
- **Snackbar copy** — Save-success / save-failure messages use the same template (`Source 'X' saved as draft. Apply on /config.`).
- **Test Connection button placement + states** — same position within `WizardActions` across all three source wizards; same idle / probing / success / failure states.
- **Edit-mode banner** — "Editing existing source 'X'" header treatment consistent (M.2d.1's `EditModeContext` discriminator drives this).

v2 will lock the polish list after M.2d.2/.3 ship. No silent scope creep — anything not in the v2 list defers to a follow-up.

---

## 6. The wizard-contract ADR

The ADR is the load-bearing deliverable. Until it lands, "we have six consistent wizards" is a transient claim. The ADR writes down what consistency *means*, so the next protocol's wizard can be reviewed against the contract instead of against six existing implementations.

**Working name:** "Wizard contract for protocol-instance authoring surfaces."
**Proposed number:** ADR-0015 (current latest is ADR-0014; see Q5 below — confirm at write-time, may shift if other M.2d sub-milestones spawn their own ADRs).

The ADR documents (at minimum):

1. **The component hierarchy** — `WizardShell` (header + sections + footer) wraps protocol-specific sections; `WizardActions` (Save / Cancel / Test Connection) lives in the footer; `WizardValidationBanner` lives between header and sections; `WizardWatchSlot` is reserved for M.2c Live Tag Watch embedding (renders nothing today but the contract is locked).
2. **The per-instance validator interface** — `IPerInstanceValidator<TItem>` (name pending Q1). Every wizard composes one or more of these; the wizard never inlines validation rules.
3. **The Edit vs Add discrimination contract** — `EditModeContext` (M.2d.1 deliverable) discriminates Add from Edit. Loading an existing config into the wizard model is `EditModeContext`'s job; the wizard renders identically in both modes except for the edit-mode header banner.
4. **Test Connection probe semantics** — Test Connection is a read-only protocol probe (e.g., FOCAS2 Browse Controller, Brother `HTTPD_MCNINFO`, Modbus TCP socket open, MQTT broker connect, OPC UA Server endpoint reach). Idempotent, no side effects on the gateway's running adapter or its draft. Result surfaces as a banner Info (success) or Warning/Error (failure) — never as a snackbar.
5. **The save-flow contract** — `draft → validate → apply → rollback` honored. Save commits the wizard's output into a **draft**, NEVER directly into the running config. The Configuration page is where the operator validates + applies the draft. The wizard NEVER bypasses this flow.
6. **The persistence boundary** — only the Save button commits the wizard's in-memory state into a draft. Cancel discards. Closing the browser tab discards. There is no auto-save, no localStorage persistence, no "resume where you left off." This is locked behaviour and the ADR records why (anti-thrash, anti-confusion, the draft IS the persistence model).

**Cross-references:** ADR-0014 (config state vs runtime state — wizard touches config only); ADR-0002 (configuration as inventory truth); ADR-0008 (destinations not sinks in UI — wizard copy honors this).

---

## 7. Deliverables

| Deliverable | Type | Notes |
|---|---|---|
| `IPerInstanceValidator<TItem>` interface | New code | Location open — likely `src/ElpisEdgeConnect.Core/Adapters/` or `src/ElpisEdgeConnect.Core/Validation/`. Name + namespace OPEN in Q1. |
| `Focas2RegisterValidator` (or equivalent) | New code | Per-instance FOCAS2 DataPoints path validator. Reused by FOCAS2 adapter `ValidateConfigAsync` AND `Focas2SourceWizardModel`. |
| `BrotherDataPointValidator` | New code | Per-instance Brother DataPoints validator. Reused by Brother adapter `ValidateConfigAsync` AND `BrotherHttpSourceWizardModel`. |
| `MqttTopicTemplateValidator` (if scope confirmed in Q2) | New code | Per-template MQTT topic validation. Reused by MQTT sink + wizard. |
| `OpcUaServerNodeIdValidator` | New code | Per-NodeId OPC UA Server validation. Reused by sink + wizard. |
| Modbus refactor (if needed) | Edit | If `ModbusTagValidator` is lifted to implement `IPerInstanceValidator<ModbusTagDefinition>`, update its three callers. May be a no-op if static-class shape is kept (Q3). |
| Validation-banner unification edits | Edit (small) | Audit and close gaps across all six wizards. |
| UX polish edits | Edit (small) | v2-locked list. |
| `docs/decisions/0015-wizard-contract.md` (number pending Q5) | New ADR | THE load-bearing deliverable. |
| Cross-wizard consistency audit checklist | New doc | `tests/ElpisEdgeConnect.Management.Tests/Wizards/CrossWizardConsistencyAuditChecklist.md` or a docs-side file (Q4). |
| Tests | New tests | Per-validator unit tests (one suite per new validator); wizard-model composition tests; audit-checklist drives manual or automated verification (Q4). |
| `docs/sessions/2026-05-XX-m2d4-handoff.md` | Handoff | End-of-session. |

---

## 8. Definition of done

Per roadmap §3.7.4:

- [ ] All six wizards pass the cross-wizard consistency audit checklist with zero open gaps.
- [ ] Per-instance validators exist for FOCAS2, Brother, OPC UA Server, and Modbus (already done); MQTT optional per Q2.
- [ ] Each per-instance validator has ≥1 caller from the adapter side AND ≥1 caller from the wizard side. Zero rule duplication.
- [ ] Validation banner severity / link / auto-clear / save-gating behaviour identical across all six wizards (mechanically verified per §4 table).
- [ ] ADR-0015 (or whatever number — Q5) "Wizard contract for protocol-instance authoring surfaces" landed.
- [ ] Final UX polish list (v2-locked) complete; none deferred silently.
- [ ] Solution-wide test sweep clean: `dotnet test ElpisEdgeConnect.sln --filter "Category!=Flaky"`.
- [ ] Zero new warnings (`TreatWarningsAsErrors` honored).
- [ ] M.2d closed — handoff doc cross-references all four sub-milestone handoffs and the new ADR.

---

## 9. Step-by-step implementation sequence (8-10 steps)

This is a draft for shape, not the final order. v2 will lock after Q resolution.

1. **Verify hard precondition.** All of M.2d.1, M.2d.2, M.2d.3 are merged. Any deferred items from those flagged. STOP if not.
2. **Inventory pass.** Walk all six wizards + their models + their adapter validators. Produce the cross-wizard consistency audit checklist (§4 table extended into a mechanical row-per-wizard checklist). Identify every open gap. This is the planning artifact for v2.
3. **Lock the validator interface shape (Q1 + Q3).** Pick a name and a static-vs-interface choice. Commit a tiny scaffold change first so subsequent validator extractions can target it.
4. **Extract `Focas2RegisterValidator`.** Read the FOCAS2 adapter's existing DataPoints-path validation logic; lift to a pure helper; wire wizard model to compose. Tests: per-validator suite + wizard-side composition test.
5. **Extract `BrotherDataPointValidator`.** Same shape. Tests: same shape.
6. **Extract sink validators (Modbus + MQTT if Q2 = yes + OPC UA Server).** Same shape. Tests: same shape.
7. **Validation-banner audit + unification.** Walk the §4 table mechanically across all six wizards; close every open box. Add per-wizard banner-behaviour tests.
8. **UX polish (v2-locked list).** Apply the v2 list mechanically across all six wizards.
9. **ADR drafting + review.** Draft `docs/decisions/0015-wizard-contract.md` (number pending Q5). Cross-reference ADR-0014, ADR-0002, ADR-0008. Surface for user review BEFORE merge.
10. **Solution-wide regression + handoff.** Full test sweep, zero warnings, handoff doc, M.2d closure cross-reference into the v2 roadmap.

---

## 10. Open questions for v2 ratification

### Q1 — Validator interface name

`IPerInstanceValidator<TItem>` is a placeholder. Alternatives: `IItemValidator<T>`, `IConfigItemValidator<T>`, `IWizardItemValidator<T>` (UI-leaning), `IInstanceValidator<T>`. Recommendation: `IPerInstanceValidator<T>` — describes the scoping (per item, not cross-item) and is symmetric with `WizardConfigMerger` owning cross-instance validation. Lock in v2.

### Q2 — MQTT topic-template validator: in scope?

Today's `MqttSinkWizardModel` validates topic-template syntax inline. Does extracting a `MqttTopicTemplateValidator` clear the bar for M.2d.4 (per-instance validator with ≥2 callers), or is MQTT genuinely a "no per-instance items" wizard? **Reality-check:** read `src/ElpisEdgeConnect.Sinks.Mqtt/` for any per-topic-template validation the adapter does today. If the adapter does its own template validation, extract. If not, leave the wizard's inline validation alone and document in the ADR that "not all wizards have per-instance items."

### Q3 — Static helper vs interface-implementing class

`ModbusTagValidator` is a `static class` today. Options:
- **(a) Lift to interface-implementing class.** DI-registerable, mockable in tests, but ceremony.
- **(b) Keep static.** No DI, no mocking, but matches `ModbusTagValidator` shape exactly. Wizards and adapters call the static method directly.
- **(c) Hybrid** — static helper PLUS a thin interface-implementing wrapper for cases that need DI.

Recommendation: **(b) static** for v1 simplicity, **(a) lift later if a real DI need emerges.** The "interface" is a documented convention, not a literal C# `interface`. Lock in v2.

### Q4 — Consistency audit: automated tooling or manual checklist?

Two shapes:
- **(a) Manual checklist** — markdown file, audited at PR review time. Cheap. Drifts over time.
- **(b) Automated test** — a single `CrossWizardConsistencyAuditTests.cs` that walks each wizard's model + razor and asserts the §4 table mechanically. Expensive to write but doesn't drift.

Recommendation: **(a) v1, (b) deferred to a follow-up** if drift becomes a problem. The audit checklist itself becomes the planning artifact for the v2 polish list.

### Q5 — Next ADR number

Latest ADR on master at write-time is **ADR-0014** (`0014-config-state-vs-runtime-state.md`). If no other ADRs land before M.2d.4 (none expected in M.2d.1/.2/.3 unless one of those surfaces an architectural decision worth locking), the wizard-contract ADR is **ADR-0015**. Reality-check at write-time. If M.2d.1/.2/.3 each spawn an ADR (e.g., one for the shared-shell component hierarchy, one for Test Connection probe semantics), M.2d.4's ADR could be 0016 / 0017 / 0018. The actual number is mechanically determined by `ls docs/decisions/` at write-time.

### Q6 — Does the ADR need to commit to a future protocol surface?

The ADR describes today's six wizards. Future protocols (OPC UA Client, S7, Modbus RTU, MTConnect) will extend the contract. **Should the ADR include a "how to add a new wizard" section?** Recommendation: yes, brief — a 5-step list ("create a `WizardModel`, compose validators, wrap in `WizardShell`, register the route, add a picker card to `Choose<X>Protocol.razor`"). Acts as the contract's operational surface for future contributors.

### Q7 — Does Route belong in this sweep at all?

The Route wizard (`RouteWizardModel` + `AddRoute.razor`) is structurally different from source/sink wizards — it wires existing sources to existing sinks, not configures a new protocol instance. Question: **does the per-instance-validator pattern even apply to the Route wizard?** Possibilities:
- **(a) Yes** — `RouteFilterValidator` validates a per-filter-rule entry on a route.
- **(b) Partially** — Route has no "per-instance items" in the source/sink/tag sense, but it has filter rules and transforms. Validate those per item.
- **(c) No** — Route is a wiring wizard, not an authoring wizard; the ADR explicitly carves it out as a different shape.

Recommendation: **(b)** — apply the pattern where it fits (filter rules, transforms) and document the partial fit in the ADR. Lock in v2.

---

## 11. Cross-references

- Roadmap parent: [v2 wrap-up roadmap §3.7.4](2026-05-21-phase2-wrapup-roadmap-v2.md)
- Sibling plans: M.2d.1 shared primitives, M.2d.2 source wizards, M.2d.3 sink + route editors (all written 2026-05-21 in parallel)
- M.2b.6.2 handoff (origin of the `ModbusTagValidator` composition pattern): [2026-05-20-mp2b62-handoff.md](2026-05-20-mp2b62-handoff.md)
- ADR-0002 (configuration as inventory truth)
- ADR-0008 (destinations not sinks in UI)
- ADR-0014 (config state vs runtime state are operationally distinct surfaces)
- Modbus tag validator source: `src/ElpisEdgeConnect.Sources.ModbusTcp/ModbusTagValidator.cs`
- Wizard models: `src/ElpisEdgeConnect.Management/Wizards/*.cs`
- Wizard razor pages: `src/ElpisEdgeConnect.Management/Components/Pages/{SourceWizards,SinkWizards,RouteWizards}/*.razor`

---

**End of v1 brief draft. Awaiting ChatGPT review pass. Seven open questions (Q1–Q7) need verdicts before v2 locks; v2 also locks the UX polish list using M.2d.2/.3 implementation experience.**
