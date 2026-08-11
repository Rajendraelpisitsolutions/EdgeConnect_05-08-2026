# Slice 0 commit 3.0 — handoff to Sony / runtime-reconfigure workstream

**Date:** 2026-06-26
**Audience:** Sony (`Sony_Development`) and anyone on the runtime-reconfigure / diagnostic-strengthening tracks.
**Status:** landed on `master`, pushed.

> **➡ SUPERSEDED — start here instead:** `docs/sessions/2026-06-29-slice-0-commit-3.1-handoff-to-sony.md`
> is the current authoritative cold-start handoff (covers 3.0 as landed + the 3.1 cutover that's next).
> This 3.0 note is retained for context only.

## What landed

`master` commit **`4baa5cd`** — *runtime: add adapter retirement attestation across source adapters
(slice 0, commit 3.0)*. Slice 0 is now three commits on `master`:

| Commit | Hash | What |
|--------|------|------|
| 1 | `3203ecd` | source-generation lease + publish-fencing gate |
| 2 | `c498ca5` | stable source slot, generation model, scoped intake writer (the structural M1 fix) |
| 3.0 | `4baa5cd` | inert, opt-in `ISourceRetirement` quiescence attestation across all six source adapters + Core helpers + Host fail-closed discovery |

Decision record: `docs/sessions/2026-06-26-slice-0-commit-3-complete-diff.md`.
Cutover plan: `docs/sessions/2026-06-26-slice-0-commit-3-cutover-plan-v3.md`.

## Why it matters to you

This is the **shared foundation** both workstreams depend on:
- the stable-slot + swappable-generation model (commit 2) is what lets a source be reconfigured without
  tearing down its route intake (the runtime-reconfigure goal);
- the adapter retirement attestation (commit 3.0) is what lets the supervisor *prove* an old generation
  is quiescent before admitting a replacement — the safety gate for both reconfigure and the
  diagnostic-strengthening "self-detect a wedged source" goal.

Build it once, both tracks share it.

## Important: 3.0 is INERT

3.0 changes **no runtime behaviour**. There is **no live supervisor wiring** — nothing calls
`BeginRetirement` in production. The only live change is that MTConnect/Brother now carry a
**behaviour-neutral poll-path guard** (`PollQuiescenceGate.TryEnterPoll`/`ExitPoll` around `PollAsync`),
semantically inert while not retiring, covered by their existing suites + a poll-path smoke.

The behaviour-changing wiring (stable ingress, fences, reordered retirement, one absolute deadline,
generalized admission, route-cascade removal) is **3.1, the atomic supervisor cutover** — not yet started.

## Action for `Sony_Development`

- **Rebase `Sony_Development` onto `master@4baa5cd`** to pick up the shared source-generation +
  retirement foundation before doing more reconfigure work against an older base.
- Heads-up on in-flight cross-cutting work: the onboarding package (#158) and any Studio/Host changes may
  touch the same Host/Generation surface — reconcile rather than assume a clean fast-forward.
- The new Core types live under `ElpisEdgeConnect.Core/Adapters/Retirement/` (public contract +
  `PollQuiescenceGate`/`PullAdapterRetirement` utilities) and `ElpisEdgeConnect.Host/Generation/`
  (`SourceRetirementCapability`, `SourceLifecycleBlockReason` — both internal). They are additive; nothing
  existing was removed.

## Next gate (blocks 3.1)

3.1 is held until the **attestation proof-matrix + deadline-inputs lock** is drafted and reviewed
(`2026-06-26-slice-0-commit-3.1-proof-matrix-v1.md`). The single retirement deadline must come from
*verified per-adapter proof inputs*, not a guessed value. No 3.1 implementation until that matrix passes review.
