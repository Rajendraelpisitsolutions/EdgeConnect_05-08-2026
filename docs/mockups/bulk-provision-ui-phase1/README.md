# Bulk-Provision UI Phase 1 — static mockup

## What this is

A standalone static HTML mockup of the Phase 1 bulk-import wizard, built for operator sign-off **before** any Razor code lands. Per the standing rule (`feedback_static_html_ui_review.md` in the user's memory), every UI surface gets a mockup pass first.

## How to review

Open `index.html` in any browser:

```pwsh
# Windows
start docs\mockups\bulk-provision-ui-phase1\index.html
```

```bash
# macOS / Linux
open docs/mockups/bulk-provision-ui-phase1/index.html
xdg-open docs/mockups/bulk-provision-ui-phase1/index.html
```

The file is fully self-contained — inline CSS, no external resources, no JavaScript, no build step. Print to PDF or screenshot any state to share / mark up.

## What's covered

Nine operator-facing states grouped under section headers with v3 / v3.1 references:

1. **Sources page entry** — new "Bulk import" button beside "Add Source"
2. **Gateway context panel** — three sink variants: 1 sink (auto), N sinks (picker), 0 sinks (blocker)
3. **Protocol picker** — 4 templates
4. **CSV template download** — protocol-specific column shape + deviceId format hint
5. **Upload + parse preview** — per-row validation status table
6. **Optional Test connectivity** — MTConnect `/probe` enabled; FOCAS2/Brother/Modbus disabled with hover text
7. **Preview merge** — summary block + tag-availability warning (per v3.1 §7) + per-source table
8. **Submit confirmation** — ONE draft (not N), "Create another batch" CTA
9. **Error states** — 9 blockers + 3 warnings

## What's NOT covered (intentional)

- **Profile editor** — out of Phase 1 scope; Phase 2 territory
- **tagProfile per-row column** — Phase 2
- **Tag coverage dashboard** — Phase 1.1 if existing diagnostics make it cheap; otherwise Phase 2
- **FOCAS2/Brother/Modbus connectivity probes** — Phase 1.1 conditional
- **Multi-protocol CSV** — Phase 2
- **Optional `sourceNamePrefix` input** — v3 §7 lists this alongside "Import label" as an
  optional operator-facing field for disambiguating batches. The mockup ships
  **Import label only** in Phase 1. `sourceNamePrefix` is **deferred** to Phase 1.1
  pending operator feedback on whether it's actually useful — the v3 doc's mention
  is aspirational, not a Phase 1 commitment. PR I-2 ships the wizard without it; if
  customer trials surface a need, Phase 1.1 adds it without breaking the v3 contract.

## Sign-off

PR I-2 (Razor implementation) implements this mockup with **1:1 user-facing alignment** per v3 Q5: state count, wording, validation moments, and summary layouts match. The Razor component structure can differ internally.

Any user-visible flow change discovered during PR I-2 implementation returns to the mockup pass first (small amendment PR).

## Cross-references

- v3 lock: `docs/sessions/2026-06-14-bulk-provision-ui-phase1-v3-lock-final.md`
- v3.1 addendum (patched): `docs/sessions/2026-06-14-bulk-provision-ui-phase1-v3.1-addendum.md`
