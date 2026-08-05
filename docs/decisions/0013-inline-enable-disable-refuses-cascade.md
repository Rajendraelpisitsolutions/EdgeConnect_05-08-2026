# ADR-0013: Inline Enable/Disable refuses cascade — explicit dependency list, no auto-disable of dependents

**Status:** Accepted (2026-05-19)
**Date:** 2026-05-19
**Milestone:** M.2b.6.1
**Framing:** Every operator-driven config-state change produces exactly one audit-chain entry; no synthetic multi-record commits from a single gesture.

## Context

M.2b.6.1 introduces row-level Enable/Disable buttons on the Sources / Sinks / Routes list pages, addressing the kickoff §0 trigger scenario: a fresh-gateway operator creates source + sink + route via the wizards, each lands disabled, and the only path to recovery today is editing `gateway.json` by hand.

The implementation must answer a question every UI toggle eventually faces: **what happens when the operator disables an entity that other enabled entities depend on?**

Concretely:

- Disabling a source that an enabled route references would leave Core's startup validator unhappy on next reload.
- Disabling a sink referenced by an enabled route is symmetric.
- Enabling a route whose source or sinks are disabled is the inverse.

Three design paths were considered (kickoff §4 Q3; v1 plan Locked C):

1. **Refuse with clear error listing dependents.** Operator manually disables dependents first. Safe; transparent; no hidden multi-state change.
2. **Offer one-click cascade button** ("Disable source and its 2 dependent routes"). Operator-friendlier; opaque from an audit-trail perspective unless the cascade emits N audit entries that are visibly grouped.
3. **Refuse with dependency list + deep links to each dependent row.** Same as (1) but the error message is actionable — operator clicks a link, lands on the dependent row, disables it, returns and retries.

## Decision

**Path (3) is locked.** The planner returns `EnableDisablePlanOutcome.CrossRecordRefused` with a structured `Blockers` list; the API returns 409 `CONFIG.CROSS_RECORD_REFUSED`; the drawer renders the blockers as deep-linkable rows pointing at `/{kind}s?focus=<id>`.

**Auto-cascade is explicitly NOT introduced in any current milestone.** Bulk multi-row operations are deferred to M.2e Shared List Infrastructure, where they belong to a richer interaction primitive that can carry per-row consent / preview / rollback.

## Reasoning

1. **The audit chain stays 1:1 with operator gestures.** Anti-pattern #9 of CLAUDE.md (no silent multi-state change) is a load-bearing architectural commitment. Every audit-record represents a single operator-confirmed state change. A cascade would either emit N audit records from one gesture (operationally confusing — which gesture did Operator B perform?) or emit one "compound" record (loses individual rollback granularity).

2. **Platform principle P4 (preserve the explainability data path) is honoured.** The Operational Explainability future surface depends on a clean 1:1 mapping between intents and audit entries. A cascade synthesises intent.

3. **Operator pain is bounded by typical fanout.** Fresh-gateway scenarios have 1–3 dependents at most; the deep-link "go fix this row, come back" loop is quick enough. Production gateways with deep fanout will eventually need bulk operations — that's exactly when M.2e's multi-row primitive becomes the right relief valve.

4. **Forward-compat with M.2d Edit-via-Wizard.** When edit-wizard lands, the same Locked-C blocker-list pattern extends to richer field changes ("can't change this transform because route X uses it"). The cascade rejection here doesn't preclude any future feature; it just keeps M.2b.6.1's scope narrow.

5. **(2) was rejected explicitly** to keep M.2b.6.1 a one-boolean milestone. Once a "cascade" feature exists, every future related milestone has to consider whether to extend it. Defer until M.2e or until operator data shows demand.

## Consequences

- **`EnableDisablePlanner.Plan` is the single source of truth for cross-record reasoning.** Mirrors Core's `CrossRecordValidator` rules but lives at the wizard layer for defence-in-depth; if the rules drift, the planner test suite + the API integration test will surface it.

- **`EnableDisableConfirmDrawerModel.Blockers` is populated only on the `CrossRecord` state.** The drawer renders deep-linkable rows; the deep-link format is locked by ADR-companion Locked C.1 (`/{kind}s?focus=<id>`).

- **No cascade button exists.** Code search for "cascade" in the milestone surface returns zero matches. Future milestones that want to introduce one must amend this ADR.

- **Future cascade work, if it lands, is via M.2e Shared List Infrastructure.** That milestone owns multi-row selection + bulk-action UI primitives. Cascade is a special case of bulk operations and belongs there if anywhere.

- **The "one gesture = one audit entry" invariant generalises.** Any future toggle / batch / scheduled-state-change feature must adhere or amend.

## Out-of-scope follow-ups

- **Cascade auto-disable.** Deferred — see Decision and Reasoning. Revisit if operator data shows the manual chain becomes painful at large fanout.

- **Bulk multi-row Enable/Disable.** M.2e Shared List Infrastructure.

- **Audit-grouping for related operator gestures.** A future Operational Explainability feature might group a sequence of single-entity toggles by operator session — that's an additive view, not a synthetic audit record.

## References

- M.2b.6.1 v1 plan: [`docs/sessions/2026-05-19-mp2b61-inline-enable-disable-plan.md`](../sessions/2026-05-19-mp2b61-inline-enable-disable-plan.md) §3 Locked C
- M.2b.6.1 v2 amendment: [`docs/sessions/2026-05-19-mp2b61-inline-enable-disable-plan-v2.md`](../sessions/2026-05-19-mp2b61-inline-enable-disable-plan-v2.md) §5 Locked C.1
- M.2b.6.1 v3 amendment: [`docs/sessions/2026-05-19-mp2b61-inline-enable-disable-plan-v3.md`](../sessions/2026-05-19-mp2b61-inline-enable-disable-plan-v3.md) §5 Locked N (operator-facing copy)
- Implementation handoff: [`docs/sessions/2026-05-19-mp2b61-implementation-kickoff.md`](../sessions/2026-05-19-mp2b61-implementation-kickoff.md) §6
- CLAUDE.md §9 anti-patterns — anti-pattern #9 (no silent state changes)
- ADR-0010 — Coordinator synthesises cross-record recovery (the runtime-side mirror of the same principle)
- Platform principles P4 (explainability data path)
