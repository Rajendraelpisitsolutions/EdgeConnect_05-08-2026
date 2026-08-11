# M.2b.6.2 — Smoke-driven wizard hardening v2 amendment

**Status:** **v2 AMENDMENT** — supersedes v1 only for the surfaces listed below; v1 stands for everything else.
**Date:** 2026-05-20
**Form:** Amendment file in the project's plan-trail discipline. Triggered by a tradeoff surfaced during implementation (smoke pass at the wizard tag table) that wasn't covered by v1. Anti-silent-scope-expansion §6 prohibits absorbing the change into the v1 PR without a written amendment — this file is the written amendment.

**Inputs:**
- [M.2b.6.2 v1 plan (locked)](2026-05-20-mp2b62-smoke-driven-ux-hardening-plan.md) — base plan
- [M.2b.6.2 kickoff](2026-05-20-mp2b62-smoke-driven-ux-hardening-kickoff.md) — §6 anti-silent-scope-expansion clause invoked
- User observation (2026-05-20 implementation session): "There is no support for String data type. Is there a reason for not supporting? or it's missed out?"

---

## 0. Why this v2 exists

While smoke-testing v1's tag-table cross-validation, the user noticed the wizard's **Datatypes dropdown is missing `string` entirely**, even though:

1. The Modbus adapter fully supports it end-to-end ([ModbusDatatype.cs](../../src/ElpisEdgeConnect.Sources.ModbusTcp/Decoding/ModbusDatatype.cs) handles `stringN` parsing; `ModbusDatatypeSpec.CanonicalType` maps to `CanonicalValueType.String`; the decoder slices payloads).
2. The **built-in CSV templates use it** — `generic-plc.csv` declares `part_id, string16`; `cnc-via-modbus-gateway.csv` declares `program_number, string8` and `operation_mode, string8`. The team clearly intended strings to be first-class.
3. M.2b.6.2 v1 just **tightened the validator for strings** (rejected byteOrder on `StringN`), proving the codepath is alive and supported.

The omission dates to the M.2b.1 commit (`ea66862`) that introduced the wizard. It is a real operator-facing gap: anyone needing a string tag must round-trip through CSV import even though the adapter would happily accept it via the wizard.

v1 did not anticipate this gap. Per kickoff §6, the resolution is a v2 amendment — not a silent absorption.

---

## 1. Scope delta vs. v1

**Added to scope (v2):**

- **D. Modbus wizard supports string datatype with explicit length entry.** A `string` choice in the Datatypes dropdown plus a conditional `String length` column for the per-tag character count. Composes with the same `ModbusTagValidator` v1 already wired in.

**Unchanged from v1:**
- A. cross-validation
- B. config-path surfacing
- C. port helper text
- Test count baseline (+18 from v1 still in scope)
- Locked-N composition discipline
- Out-of-scope deferrals from v1 §1
- Cadence (single implementation session — v2 doesn't reset the cadence)

**Not added in v2 (still deferred):**
- Bulk paste / CSV import into the wizard's tag table → M.2c
- Edit-via-Wizard for an existing string tag → M.2d
- String encoding choice (ASCII vs UTF-8 vs Modbus-specific) — adapter currently hardcodes ASCII high-char-first per Modbus convention; not a wizard-layer concern

---

## 2. Locked rules (governing the v2 amendment)

Per the user's directive (2026-05-20 implementation session):

| # | Rule |
|---|---|
| 1 | `String length` is **required** when datatype is `string`. |
| 2 | `String length` must be **positive** (`> 0`). |
| 3 | `ByteOrder` must be **disabled and cleared** for string datatype (the shared validator rejects it; v2 prevents the operator from setting it in the first place). |
| 4 | `Save` remains disabled while `String length` is missing or invalid. |
| 5 | CSV import continues to support the existing fixed-form aliases (`string8`, `string16`); the wizard UX uses proper any-length entry. **The wire shape stays `stringN`** — the wizard composes `string` + length into `stringN` when emitting the canonical `SourceInstanceConfig`. |

Rule 3 resolves the v2 lock between "inline error" vs "auto-clear" for the byteOrder+string interaction: **auto-clear and disable**, not inline error. Rationale: the operator cannot reach an invalid state by typing; the wizard guides them out of it.

---

## 3. Deliverables (v2 delta only)

| File | Status | Surface |
|---|---|---|
| `src/ElpisEdgeConnect.Management/Wizards/ModbusSourceWizardModel.cs` | edit | (1) Add `"string"` to the `Datatypes` list. (2) Add `int? StringLength` to `ModbusTagWizardRow`. (3) Extend `ValidateTag` per Locked rules 1+2: when `Datatype == "string"`, require `StringLength > 0` before delegating to `ModbusTagValidator`. (4) Compose `string` + `StringLength` into `stringN` when calling the underlying validator AND when emitting via `BuildSourceInstance`. |
| `src/ElpisEdgeConnect.Management/Components/Pages/SourceWizards/AddModbusSource.razor` | edit | (1) Add a 9th column "String length" to the tag table; render editable `MudNumericField` only when the row's datatype is `"string"`, otherwise render a dimmed placeholder. (2) Disable the ByteOrder cell when datatype is `"string"`; auto-clear any previously-set ByteOrder via a `@bind-Value:after` callback on the Datatype select. (3) `CanSave()` already gates on `ValidateTag(...)` having zero issues — no extra logic needed since the validator catches rule 1+2. |
| `tests/ElpisEdgeConnect.Management.Tests/ModbusSourceWizardModelTests.cs` | edit | Add the six tests enumerated in §4 below. |

**Estimate:** ~40–60 additional LOC + 6 additional tests on top of v1's totals.

---

## 4. Test plan (v2 delta)

Per the user's directive (2026-05-20 session):

1. **`ValidateTag_String_MissingLength_FlagsStringLength`** — `Datatype = "string"`, `StringLength = null` → issue with `Path = "StringLength"`, code `MODBUS.CONFIG_INVALID`.
2. **`ValidateTag_String_NonPositiveLength_FlagsStringLength`** — `Datatype = "string"`, `StringLength = 0` → same.
3. **`ValidateTag_String_PositiveLength_Valid`** — `Datatype = "string"`, `StringLength = 12` → no issues (happy path).
4. **`ValidateTag_NonString_StringLengthIgnored_Valid`** — `Datatype = "uint16"`, `StringLength = 8` → no issues (length is ignored when datatype is non-string).
5. **`BuildSourceInstance_StringDatatype_EmitsStringNComposed`** — wizard row with `Datatype = "string"`, `StringLength = 16` → emitted JSON has `"datatype": "string16"`. Pins the wire-shape contract that downstream `ModbusTcpSourceConfiguration.FromSourceInstance` consumes.
6. **`ValidateTag_String_WithByteOrder_FlagsByteOrder`** — `Datatype = "string"`, `StringLength = 16`, `ByteOrder = "ABCD"` → issue with `Path = "ByteOrder"`. (Razor-side rule 3 prevents this from being reachable through the UI, but the validator-level rejection stays in place as defence-in-depth and as a contract test for programmatic callers.)

**Total v2 new tests:** 6. **Total milestone test delta (v1 + v2):** 24 new.

---

## 5. Risks (v2-specific)

| Risk | Likelihood | Mitigation |
|---|---|---|
| Adding a 9th column to the existing `MudSimpleTable` pushes the table into horizontal scroll on narrow screens | Medium | The wizard's `MudSimpleTable` already declares `Style="overflow:auto;"` and the tag-table flow is desktop-first by design (operators use a workstation, not a phone). Acceptable. |
| Operator changes datatype from `"string"` to `"uint16"` and the stale `StringLength` value lingers in the row state | Low | The wire-shape composition (§3 row 1, deliverable 4) only emits `stringN` when datatype is `"string"`. A stale `StringLength` on a non-string row is ignored. We do **not** auto-clear it — operators flipping back to `"string"` then expect their length preserved. |
| Auto-clearing ByteOrder when datatype switches to `"string"` surprises operators | Low | The cell shows a disabled state with a tooltip explaining why; this is the same UX pattern as the M.2b.5 wizard's conditional fields. |

---

## 6. Acceptance signal for v2 lock

The user explicitly directed implementation (Option B, 2026-05-20 session) and laid out the locked rules above verbatim. v2 is locked at write-time; no separate review pass needed.

**End of M.2b.6.2 v2 amendment. Implementation proceeds.**
