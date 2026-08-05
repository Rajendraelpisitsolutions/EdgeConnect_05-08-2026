# ADR-0034 — General MELSEC profile-matrix strategy

**Status:** Accepted (2026-07-03)
**Relation:** Extends ADR-0033's *scope note*. ADR-0033 remains the Slice-1
wire/scope decision record (hand-rolled SLMP, 3E-binary-TCP pin, Slice-1 device
set); nothing here reopens its wire-layer decisions.
**References:** `docs/sessions/2026-07-03-melsec-general-compatibility-roadmap-v2.md`
(approved direction), `docs/sessions/2026-07-03-melsec-phase-a-qualification-plan-v2.md`,
`docs/sessions/2026-07-03-melsec-a2-iqf-profile-plan-v2.md`,
audits `…-melsec-phase-a1-audit.md` / `…-melsec-a2d-fx5-audit.md`.

## Context

The product goal is a **general Mitsubishi MELSEC driver** — supporting the
Mitsubishi families that expose MELSEC / MC / SLMP communication — through a
compatibility matrix, not one-off customer support. Slice 1 shipped exactly one
profile (Modern: iQ-R/Q/L, MC 3E binary over TCP, read-only). The Gate A-2D audit
proved the iQ-F/FX5 family shares the identical 3E-binary wire shape and differs
only in data (X/Y operator-address radix, device set). No hardware is currently
available; qualification evidence is desk + simulator until a PLC exists.

## Decision

1. **Profiles are data, not code paths.** Each PLC family is described by a
   static, typed C# record (`Profiles.MelsecProfiles` in
   `ElpisEdgeConnect.Sources.Melsec`) carrying: identity/envelope (family, model
   families, frame, transport, route defaults), wire-shape documentation
   (device-code width, head-field width, supported commands), device set with
   **per-profile radix**, limits (word cap, bit packing, alignment rule), default
   word order, operator-selectability gate, and **provenance** (pinned manual
   document+revision, evidence links). Adding a family means filling a record and
   its tests — not modifying the codec.
2. **The codec changes only for a proven wire-shape difference.** Profiles that
   share a wire shape (Modern, iQ-F) reuse the same codec byte-for-byte. Any
   manual-proven shape difference (e.g. future 4E, 1E, ASCII) is a separately
   scoped frame implementation, not a profile entry.
3. **Support ladder** (from the roadmap, applied per profile):
   *Implemented → Supported (operator-exposed in UI with validation) →
   Field-qualified (≥1 real PLC/capture) → Certified (broader hardware
   coverage)*. Simulator evidence proves internal consistency only — it never
   yields Field-qualified.
4. **Manual pinning is mandatory.** A profile's facts are derived only from
   officially pinned Mitsubishi manuals (document number, revision, date from
   the PDF's own revision page, source URL, SHA-256, local gitignored evidence
   copy). No profile fact is accepted from memory or generic claims.
5. **Existing-config migration guarantee.** Configurations with no profile field
   hydrate as **Modern** — the shipped default — and existing `melsec` sources
   keep working unmodified through every registry/selector change. A future
   profile selector must never break or re-prompt existing sources.
6. **Gated exposure.** A profile enters the UI (Supported) only via the
   separately approved profile-selector deliverable. Until then non-Modern
   profiles are internal/testable-only, and adapter validation continues to
   reject them at config time with a typed error.

## Status truthfulness (explicit)

As of this ADR: **Modern** is Implemented + Supported + Simulator-tested;
**Field-qualified: pending hardware; Certified: pending broader coverage.**
**iQ-F/FX5** is registry data + tests only — **NOT operator-Supported, NOT
Field-qualified, NOT Certified**; it awaits the profile-selector approval
(Supported) and real-PLC capture (Field-qualified). This ADR makes no claim of
"certified for all Mitsubishi PLCs".

## Consequences

- The wizard/UI copy stays profile-aware ("MC 3E binary TCP profile") and may
  not claim universal Mitsubishi support until certification coverage exists.
- The compatibility-status table (roadmap §5 / Phase A plan §3) is the single
  gate for claims; it is updated as gates (A-2D/A-2I/A-2S/A-2O/A-2H) complete.
- Byte-identity tests pin the shipped Modern behavior across every registry
  refactor; a drift fails the suite.
- QnA / A-series remain registry-absent until their (1E/serial-inclusive)
  scope is separately approved.
