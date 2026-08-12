# ADR-0025: Last-Known-Good config pin + one-click rollback

**Status:** Proposed (2026-05-30)
**Date:** 2026-05-30
**Framing:** Operators already have draft / apply / rollback (the existing config flow). What they lack is a persistent "this config was working" pin — a marked-good anchor that survives across many subsequent applies, and a one-click rollback to that pin from any later state. This ADR adds the pin without disturbing the existing flow. It also becomes anchor #1 for What Changed (ADR-0024).

## Context

The existing config flow supports:

- **Draft** — operator edits config without applying
- **Apply** — draft becomes live; old config is archived in the audit chain
- **Rollback** — revert to the *immediately previous* applied version

The gap: after the operator has applied 17 changes since the last known-good config, the existing rollback walks back only one step. To return to "the version that was definitely working" the operator must navigate the audit chain and choose by timestamp — error-prone, requires reading commit-style messages, and offers no semantic anchor.

The Last-Known-Good (LKG) pin closes the gap with minimal surface area: one operator action ("pin this as Last-Known-Good") + one one-click button to revert. The pin is durable across many subsequent applies and becomes the anchor for What Changed.

## Decision

The LKG pin conforms to the following four rules.

### Rule 1 — Exactly one LKG pin per gateway at a time

There is exactly one LKG pin per gateway. Pinning a new config version replaces the previous pin (with an audit trail entry recording the replacement). This keeps the surface area small — one pin, not a managed list — and matches the operator's mental model ("the version I know works").

The pin references a specific entry in the config audit chain by `configVersionId` (already present in the audit chain).

### Rule 2 — Pinning is an explicit operator action

The LKG pin does NOT auto-update on any heuristic ("this version ran without faults for 24 hours, so pin it"). The pin is set ONLY by explicit operator action via the "Pin current config as Last-Known-Good" button on the Config page, OR via API `PUT /api/v1/config/last-known-good/{configVersionId}`.

Auto-pinning would create the same composite-score pathology ADR-0027 rejects: the system claims confidence it can't ground. P7's honesty clause applies — the system doesn't know which config the operator considers acceptable. Only the operator knows.

Optional UX nudge: after a config has been applied + stable + fault-free for 7 days, a non-intrusive notification suggests "Consider pinning this as Last-Known-Good." Operator confirms or dismisses. No auto-action.

### Rule 3 — One-click rollback to LKG with diff preview

The "Rollback to Last-Known-Good" affordance opens a confirmation modal that walks the four-question framework (P7):

```
Rollback to Last-Known-Good?

What will happen:
  Config version v17 (current) → v9 (pinned 2026-05-22 by ssudhakar)

Why this rollback is offered:
  You marked v9 as Last-Known-Good. 8 applies since then.

What will change (per ADR-0024 What Changed dimensions):
  • 3 routes will revert to earlier definitions
  • 1 sink will be reconfigured
  • 1 source will be removed
  • Brother-HTTP source added in v12 will NOT be removed (it's marked as keep-on-rollback)

What action you can take:
  [ Continue to rollback ]   [ Open What Changed for detail ]   [ Cancel ]
```

The modal renders the diff using ADR-0024's `StateChangeRecord` dataset. Operator confirms before any change applies.

### Rule 4 — LKG pin survives restart; rollback respects keep-on-rollback markers

The pin lives in the gateway's persistent state directory (alongside the audit chain SQLite database). Survives host restart, host upgrade, and config-directory reload (per ADR-0014).

Individual config entities may be marked `keepOnRollback = true` at apply time — e.g., a sink added because of a customer-side network change should not be removed by a rollback to an earlier version that predates the network change. The keep-on-rollback marker is per-entity, set at apply time, never auto-set. Rollback respects the markers and surfaces them in the diff preview (Rule 3) so the operator sees what's preserved.

## Consequences

**Positive:**

- "Roll back to what worked" becomes one click instead of audit-chain navigation
- LKG becomes the canonical anchor for What Changed (ADR-0024) — the rollback workflow and the "what changed" workflow share the same anchor source
- The pin gives the operator a clear mental model ("the version I trust") without introducing system-side judgement about which version is good
- Operators feel safer making aggressive config changes because rollback-to-good is one click

**Negative:**

- Adds one persistent state field + one UI button + one audit-entry kind. Small surface area but cross-cutting.
- Rollback-with-keep-on-rollback semantics needs careful UI explanation. The diff preview is the place; copywriting matters.
- An operator who never pins anything has no anchor and falls back to ADR-0024's secondary / tertiary anchor sources. The non-intrusive 7-day nudge mitigates but doesn't eliminate.

**Forbidden patterns:**

- Auto-pinning based on health heuristics
- A "multiple pins" list — the operator's mental model is one pin
- Rollback that bypasses the diff preview modal (Rule 3 is the trust gate)
- Silent rollback failures (e.g., a sink rollback that fails partially) — partial rollback must surface as a structured fault, never silently leave the system in a mixed state

## Reference

- ADR-0009 / 0010 — config audit chain (the storage substrate the pin references)
- ADR-0014 — config-directory reload (the rollback path uses the existing apply flow)
- ADR-0024 — What Changed (the LKG pin is anchor source #1 for this surface)
- Platform principle P6 — operational product (one-click rollback to known-good IS the operational gesture)
- Platform principle P7 — surfaces explain outcomes (the rollback modal answers all four questions before acting)
- `docs/sessions/2026-05-30-diagnostic-strategy-handoff.md`
