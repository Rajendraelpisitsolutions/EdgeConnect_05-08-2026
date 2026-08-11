# Session handoff — 2026-07-19 — Sparkplug B K1.3 complete, PR #186 open

## Status: K1.3 done, awaiting merge

**PR [#186](https://github.com/elpisitsolutions/EdgeConnect/pull/186)** (`feat/sparkplug-b-k1.3-route-wiring` → `master`) is **OPEN**. **Merge is pending — the owner's call.**

K1.3 wires the Core routing engine to drive a replay-aware sink's full lifecycle
(birth → replay → catch-up → live → rebirth → end, per ADR-0036). **Core stays
protocol-neutral — no Sparkplug types in `ElpisEdgeConnect.Core`.** It builds on
the merged K1.1–K1.2d contract chain (#180–#184).

### Slices 1–5 — all complete and externally approved

| Slice | Landed | Review |
|-------|--------|--------|
| 1 | `IReplayRouteBuffer` capability + activation-at-commit | r1, r2 |
| 2 | Tracked-append intake at the fixed generation | r1, r2 |
| 3 | `ReplayRouteDriver` (birth → replay → cutover → live) | r1, r2 |
| 4 | Coalescing rebirth host + epoch gating + empty-route wake | r1 |
| 5 | Graceful end-session + config-replace reason + hot-replace guard | r1, r2, r3 |

Every external-review round was folded. Final reviewer verdict: no remaining
K1.3 blocker requires reopening the Core replay architecture.

### Final verification (HEAD `1a94f31`)
- **Core.Tests 1251** · **Host.Tests 225** · **Management.Tests 1149** — all green
- Solution **0 warnings / 0 errors** (`TreatWarningsAsErrors` on Core)

### Plan trail (durable evidence, on the branch under `docs/sessions/`)
`…-k1.3-route-wiring-plan-v1.md` → `-v2.md` → `-v3.md` (frozen) →
`-v3.1-amendment.md` → `-v3.2-amendment.md`. Per-slice review evidence lives in
the PR description, the commit chain, and CI.

## K2 — the actual Sparkplug B sink (next milestone)

Core is wired and protocol-neutral; **K2 implements the concrete Sparkplug B
sink adapter** (`IReplayAwareSinkAdapter`) and its licensing/config/DI/Studio.

**K2 MUST retain these named K1.3 follow-ups** (deferred intentionally, not gaps):

1. **Material-schema / generation-changing rebirth** — needs an authoritative
   new-generation manifest seed that Core lacks today. `AdvanceGenerationAsync`
   stays off the K1.3 route capability until this lands.
2. **Coordinated replay-sink hot replacement** — the full coordinator ↔ driver
   dance. K1.3 restricts an in-place replay-sink change to a fail-closed reject
   (new sink identity + new route id); the live/dependent routes are left
   untouched. K2 builds the real coordinated hot-replace on top.
3. **Production `ISinkReplayCapabilityClassifier` registration** — the incoming-
   side classifier is an inert optional seam in K1.3 (unregistered → null → the
   incoming branch never fires; the live-adapter branch always does). K2
   registers the real classifier alongside the Sparkplug sink so an
   ordinary→replay-aware in-place swap is rejected in production.

## Post-merge housekeeping
- The repo-root `k1.3-slice-*.diff` / `k1.3-slice-*-review-bundle.md` working
  artifacts were removed after this handoff was committed (superseded; durable
  evidence is the plan trail, this note, PR #186, commits, and CI).
