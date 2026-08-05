# Post-M.2b.6 product roadmap — v2 amendment

**Status:** v2 — LOCKED (ChatGPT strategic review folded in)
**Date:** 2026-05-19
**Form:** **TIGHT AMENDMENT** to v1. The [v1 roadmap](2026-05-18-post-mp2b6-product-roadmap.md) remains the load-bearing reference for everything not amended here.

---

## 0. What changed v1 → v2 (delta summary)

ChatGPT review on 2026-05-18/19 produced four architecturally-significant refinements. v2 folds them in:

1. **Live Tag Watch becomes a PLATFORM capability, not just a page.** Internal name: **Runtime Tap**. The Watch UI is one consumer; future consumers (sink diagnostics, MQTT payload inspector, OPC UA live node viewer, alarm debugging, OEE traces, fleet diagnostics, demo-mode telemetry) reuse the same infrastructure.
2. **M.2e + M.2f merge into a single "Shared List Infrastructure" milestone.** Selection state, filtering, grouping, paging, saved views, bulk actions, row virtualization all belong to one reusable entity-table system. Sources / Sinks / Routes / future Alarms / Licenses / Audit / Tags / Sessions / Fleet all configure it.
3. **Milestone K gains a spec-first phase.** Authentication model, certificate lifecycle, trust model, deployment UX, renewal behavior, backup semantics, role mapping, recovery flows, cluster/fleet implications must all be designed BEFORE code starts. Industrial buyers interpret OPC UA security weakness as platform immaturity disproportionately.
4. **New future milestone — Operational Explainability.** Deferred to Tier 4 but explicitly named so we don't lose it. The deterministic pipeline + audit chain + diff-reload architecture already gives us a unique foundation here.

Plus an elevated **EREMOS V2 strategic-positioning section** (§4) and a clearer separation between **"developer tool"** vs **"operational industrial product"** framing.

---

## 1. New locked architectural commitments

These are NOT prioritization changes — they're design discipline that must land BEFORE the next milestones start.

### Locked A — Runtime Tap is the architectural layer; UIs are consumers

When M.2c (Live Tag Watch) is built, the **Watch page is the FIRST consumer of a new platform capability**, not a one-off feature.

**Runtime Tap** (working name; finalize before M.2c v1 plan):
- Lives in `ElpisEdgeConnect.Core` (`Core.Observability` or `Core.RuntimeTap` namespace TBD)
- Publishes a typed event stream of: canonical points + pipeline-step decisions + sink-delivery outcomes + reload events
- Subscriber model: filtered + multiplexed; multiple subscribers can read concurrently without affecting the live pipeline
- Backpressure-safe: a slow subscriber must NEVER slow the runtime data path
- Optional gating: env var / config / license-module-keyed (sensitive deployments may want to disable runtime taps in production)

**Architectural pattern (Locked):**

```
Runtime data path  ──►  pipeline ──►  sink
                            │
                            ▼ (zero-cost when no subscribers)
                       Runtime Tap
                            │
                  ┌─────────┼─────────┬──────────────┬─────────────────┐
                  ▼         ▼         ▼              ▼                 ▼
              Watch UI    MQTT      OPC UA       Alarm debug      Fleet diag
              (M.2c)      payload   node viewer  (future)         (future)
                          inspector (future)
                          (future)
```

The first PR for M.2c ships the Tap + the Watch UI. Future PRs ship more consumers without touching the Tap implementation.

### Locked B — Shared List Infrastructure (one system, three immediate consumers)

What v1 roadmap called M.2e + M.2f are now a single milestone: **M.2e — Shared List Infrastructure**.

Components (Locked at this level; detailed spec lands in M.2e plan):
- `EntityListView<T>` — generic virtualised table component
- `EntityListToolbar` — search, sort, group, column-toggle, saved-views, bulk-actions menu
- `IEntityListState<T>` — per-page persisted view preferences
- `EntityListBulkAction<T>` — strongly-typed bulk operation descriptor (enable / disable / delete / clone / export)

Three immediate consumers: `Sources.razor`, `Sinks.razor`, `Routes.razor`. Future consumers (without modifying the infrastructure): alarms list, license modules, audit log, tags, device sessions, fleet gateways.

**Why this matters strategically:** if M.2e and M.2f shipped separately, the list-chrome and bulk-ops surfaces would diverge across pages. As soon as a 4th list page exists (alarms, fleet, audit), the divergence becomes permanent. Merging now is the cheapest moment to enforce consistency.

### Locked C — Milestone K is spec-first

OPC UA Security hardening does NOT start in code. It starts with a written specification covering:

| Spec section | Content |
|---|---|
| Authentication model | Which OPC UA UserTokenTypes are supported? (Anonymous / Username / X.509 / IssuedToken) Defaults? |
| Certificate lifecycle | Auto-generate at first start (today's MVP behaviour) vs operator-supplied. Renewal triggers. Revocation handling. |
| Trust model | Trust list management UI + storage. CA chains. Self-signed-cert acceptance rules. Pinning. |
| Deployment UX | Where do certs live on disk? How does the operator copy them? Studio-side trust list editing? |
| Renewal behavior | Auto-renew thresholds (30 days before expiry?). Restart vs hot-reload renewal. |
| Backup semantics | Do cert + trust list survive a backup/restore? Encrypted at rest? |
| Role mapping | Which OPC UA users get which operator roles? Read-only vs read-write? |
| Recovery flows | Lost cert / compromised CA / forgotten password — operator remediation paths |
| Cluster / fleet implications | Multiple gateways with the same ApplicationUri? Cert distribution? |

**Spec-first deadline:** complete the written spec BEFORE the first line of K implementation code. Locked-decision documents (ADRs) emerge from this spec.

### Locked D — Operational Explainability is a future Tier 4 milestone (not built now)

Named so we don't lose it. Locked here so contributors don't ship dead-end features that contradict it later.

The intent: a Studio-side "why?" page surfacing operationally-explainable decisions per route / per point / per session:

- Why was this route disabled? (manifest, license, fault, operator action — with timestamps + audit chain link)
- Why was this point dropped? (filter / deadband / rate-limit / backpressure — which step + which threshold)
- Why did retry happen? (failure code + cumulative retry count + backoff state)
- Why did failover activate? (sink-pool health + circuit-breaker state)
- Why did reload restart this source? (diff classifier output + recovery synthesis trace from ADR-0010)
- Why was a point rejected? (validation step + schema diff)

The architecture for this already exists: deterministic pipeline + audit chain + diff-reload classifier. **No competitor has this depth of explainability.** Building it later when the platform is otherwise mature would be a major differentiator.

**Not now.** But every Tier 1-3 milestone should be implemented in ways that preserve the explainability data path — no opaque short-circuits, no "swallow the error" patterns.

---

## 2. Revised priority sequencing (delta from v1 §"Recommended sequencing")

```
Now ────────────────────────────────────────────────────────────────────►
│
├─ M.2b.5 Route Wizard           [in flight, v3 locked]
├─ M.2b.6 Destination Wizard     [v3 locked, stacks on M.2b.5]
│
├─────────── Tier 1 (close commercial-product gap) ───────────────────►
│
├─ M.2c   Live Tag Watch          ⭐ START NEXT
│         ↳ builds Runtime Tap PLATFORM under the hood
│
├─ M.2d   Edit-via-Wizard
│
├─ M.2e   Shared List Infrastructure   (merged from v1's M.2e + M.2f)
│         ↳ rolls out across Sources/Sinks/Routes pages
│
├─────────── Tier 2 (strategic differentiation) ──────────────────────►
│
├─ M.2g   First-run onboarding wizard
├─ M.2h   Tag tree explorer
├─ M.2i   Kafka sink
├─ M.2j   Dark mode
│
├─────────── Tier 3 (release prerequisites; parallel track) ──────────►
│
├─ Milestone K  OPC UA security hardening    🚨 RELEASE BLOCKER
│              ↳ SPEC-FIRST phase begins now in parallel with M.2c
│              ↳ Implementation lands after spec is locked
│
├─ M.2l   License management UI
│
├─ M.2k   Fleet management        (defer until ≥5 multi-gateway customers)
│
├─────────── Tier 4 (future expansion) ────────────────────────────────►
│
├─ M.2m   Cloud sink expansion (Azure / AWS / Snowflake / generic HTTP)
├─ M.2n   Operational Explainability  ⭐ unique-differentiator candidate
└─ M.2o   Source protocol expansion (EtherNet/IP, BACnet, IEC 61850, DNP3)
```

Tier 4 ordering note: M.2n (Explainability) deliberately ranked above M.2o (more protocols). Protocols are catch-up; explainability is genuine differentiation.

---

## 3. Sequencing implications

### Parallel-friendly streams

- **Stream A (UX completeness):** M.2c → M.2d → M.2e (sequential because each builds on the prior)
- **Stream B (security):** Milestone K spec-first → K implementation (sequential)
- **Stream C (quick wins):** M.2j (dark mode), M.2l (license UI) — small, parallel-safe

Two engineers can parallelize Stream A + Stream B safely. Stream C lands as filler between Stream A milestones.

### Critical-path observation (refined from v1)

The single biggest schedule risk remains **Milestone K**. v2 reinforces this by making K spec-first — the spec work absorbs schedule risk even when implementation hasn't started.

Practical target: **K spec locked before M.2d ships.** That gives K implementation a runway parallel to M.2e + Tier 2 work.

---

## 4. EREMOS V2 — elevated to a primary positioning statement

ChatGPT's review correctly elevated EREMOS integration from "mentioned" to "strategic differentiator." Locking the language here.

### What competitors sell

> A connectivity component (Kepware, HighByte, Litmus, MatrikonOPC) that integrates with someone else's MQTT broker + someone else's historian + someone else's dashboard + someone else's MES.

Buyers stitch together 4-5 vendors. Each has its own UX, billing, support contract, and integration headaches.

### What we sell

> A **vertically integrated industrial stack** — EdgeConnect (edge connectivity + canonical pipeline) + EREMOS V2 (analytics, OEE, MES integration, dashboards). One vendor, one design language, one billing relationship, one support contact.

### Why this matters

In our target segments — **India + Middle East mid-market manufacturing**, **CNC machine builders**, **brownfield modernization**, **MQTT-first plants** — the stitched-together-5-vendors model is the dominant pain point. We compete by being the integrated alternative.

This positioning shapes:
- **Marketing**: "the OT/IT integration platform" not "the gateway you buy in addition to your other tools"
- **Pricing**: bundled tiers across EdgeConnect + EREMOS, not à-la-carte
- **Roadmap**: every cross-EREMOS integration milestone (canonical-point schema, shared license modules, unified diagnostics) is high-priority — it widens the moat competitors can't cross
- **Architecture**: shared knowledge base (`C:\dev\shared-knowledge\`) and MQTT contract stability are load-bearing investments

**Not just one milestone — a multi-year strategic commitment.** Specific cross-EREMOS milestones can land in any Tier as appropriate.

---

## 5. Transition statement (the framing that emerged from the review)

The roadmap now reflects a meaningful transition in product identity:

> **We are no longer planning a developer tool. We are planning an operational industrial product.**

Concretely:
- v1 roadmap was structured around "what's missing from feature parity with Kepware"
- v2 is structured around "what makes an industrial operator successful with this product"

That shift makes **onboarding / diagnostics / live visibility / ergonomics / explainability / commissioning workflows** more important than protocol count. The Tier 1 + Tier 2 sequencing now reflects this.

---

## 6. Task chip plan

Per ChatGPT recommendation, spawn backlog chips selectively. Four chips create now; the rest stay milestone-level until nearer execution.

### Spawn now

| Chip | Why |
|---|---|
| **M.2c — Live Tag Watch + Runtime Tap platform** | Affects architectural direction; the Tap must be designed up-front, not retrofitted |
| **M.2d — Edit-via-Wizard** | Affects every existing wizard; needs scope-level planning |
| **M.2e — Shared List Infrastructure** | Affects three immediate pages + future consumers; merging M.2e + M.2f early prevents pattern divergence |
| **Milestone K — Spec-first specification** | Multi-month effort; spec-first work must start now in parallel with M.2c |

### Stay milestone-level (don't decompose yet)

- M.2g First-run onboarding
- M.2h Tag tree explorer
- M.2i Kafka sink
- M.2j Dark mode
- M.2k Fleet management
- M.2l License management UI
- M.2m Cloud sink expansion
- M.2n Operational Explainability
- M.2o Source protocol expansion

These are deferred not because they're unimportant — but because **deeply pre-decomposing milestones we'll touch 6+ months from now wastes design budget** on premature locked-in choices. Lock them at planning time.

---

**End of v2 amendment. LOCKED 2026-05-19 after ChatGPT strategic review. Next: spawn 4 chips per §6; otherwise the pre-M.2b.5 implementation pause remains active.**
