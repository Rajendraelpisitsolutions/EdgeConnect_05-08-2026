# ADR-0003: Three-layer defence for safety invariants

**Status:** Accepted
**Date:** 2026-05-14
**Milestone:** M.2b.1

## Context

During M.2b.1 (Modbus wizard) smoke testing, the wizard's "Do not
wire yet" branch produced a source with `Enabled=true` and no route.
On gateway restart, Core's startup validator raised
`InvalidOperationException`, crashing the gateway with no in-product
recovery path.

The root invariant: **every enabled source must be referenced by
an enabled route.** It existed in exactly one place (Core's startup
registration extensions), so a misuse of the wizard, the management
API, or a hand-edit could each violate it.

## Decision

Safety invariants are enforced at **three independent layers**:

1. **Core startup validator** — last line of defence. Catches
   violations regardless of how they got into the config. Today
   (pre-M.P2.1) it threw; post-M.P2.1 it logs + registers a fault
   + continues fail-soft (see ADR-0004).

2. **Pure transformation layer** — for changes that flow through a
   wizard or API, the merger/builder refuses to produce a draft
   that would violate the invariant. Example: `WizardConfigMerger`
   throws `ArgumentException` when handed an enabled source +
   `RouteWiring.NotWired`. Caught at draft-build time, before any
   persistence.

3. **UI layer** — the wizard forces `Enabled=false` when the
   operator picks "Do not wire yet" and shows explicit text
   ("Source will be created as DISABLED"). Operator sees the
   policy directly; can't fat-finger past it without overriding
   the JSON manually.

## Reasoning

1. **Single-layer enforcement is fragile.** Any new entry point
   (next wizard, new API endpoint, hand-edit) can bypass it.
   Today's Modbus wizard demonstrated this exactly — the API let
   through what Core then rejected.

2. **Each layer catches a different failure class.** Core catches
   hand-edits + future API misuse. Merger catches programmatic
   bugs in wizard logic. UI catches operator mistakes via
   forcing the right defaults.

3. **The cost is low and the layers compose cleanly.** Adding a
   check in `WizardConfigMerger` is ~10 lines. The UI text is
   already needed for operator clarity. Core's validator
   pre-existed. Total cost: minor; total resilience: substantial.

## Consequences

- All future wizards (M.2b.2 S7, M.2b.3 FOCAS2, M.2b.4 MTConnect,
  destination wizards when they exist) reuse `WizardConfigMerger`
  unchanged — the invariant is enforced at the merger layer for
  every wizard, automatically.

- Future API endpoints that mutate configuration (M.2a's apply path,
  M.P2.2's hot-reload) must call the same merger or validator —
  bypassing it would let an invalid config slip past two of the
  three layers.

- The "Core never crashes the gateway on bad config" promise (ADR-
  0004) does NOT obsolete this pattern — the three layers still
  combine to make bad configs less likely AND less damaging when
  they do happen.

## References

- Implementation: `WizardConfigMerger.cs` (M.2b.1 commit body)
- Companion: ADR-0004 (fail-soft Core startup) — replaces the
  "Core crashes" behavior in layer 1 with "Core marks Faulted
  and continues"
