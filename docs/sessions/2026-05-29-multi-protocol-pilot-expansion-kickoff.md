# Multi-protocol pilot expansion — kickoff handoff

**Status:** Kickoff. Implementation may begin once §1 week-0 checklist completes.
**Date:** 2026-05-29.
**Plan trail:** [v1](2026-05-28-multi-protocol-pilot-plan-v1.md) → [v2](2026-05-28-multi-protocol-pilot-plan-v2.md) → **[v2.1 LOCKED](2026-05-28-multi-protocol-pilot-plan-v2.1.md)** (this handoff references v2.1; v1/v2 archived but kept for traceability).
**Benchmark governance:** [`docs/benchmarks/multi-protocol-workload-profiles.md`](../benchmarks/multi-protocol-workload-profiles.md) (locked rules + profiles) + [`docs/benchmarks/phase2-multi-protocol-baseline.md`](../benchmarks/phase2-multi-protocol-baseline.md) (scaffold for measurements).

> **For an implementation agent starting cold:** read this doc + plan v2.1 §1–§7. Skip v1 and v2 unless you need to understand why a decision changed. Skip the architecture blueprint sections that v2.1 hasn't touched.

---

## §0 What we're building (one paragraph)

Two new source adapters at Kepware/Matrikon quality: **OPC UA Client** (consumes FactoryTalk SCADA + other OPC UA servers) and **EtherNet/IP** (native Allen-Bradley CompactLogix / ControlLogix / MicroLogix / Micro800 reads). Plus shared `ITagBrowseService` + `TagBrowseTreeView` + `ReconfigureAsync` runtime infrastructure both adapters consume. MELSEC and S7 deferred to their own future plan trails. Pilot start target: week 7–8.

---

## §1 Week-0 operational checklist (before week-1 starts)

Operations work, not engineering. Must all complete before week-1 implementation kicks off.

| # | Task | Owner | Status | Blocks |
|---|---|---|---|---|
| 1 | Merge **PR #45** (OPC UA Server port-conflict diagnostics) | Sudhakar | ✅ Done (`51846f6`) | — |
| 2 | Tag `v0.2.0-qa-baseline` on the merged master | Sudhakar | ✅ Done | — |
| 3 | Branch `feat/multi-protocol-pilot-expansion` off master | Sudhakar | ⏳ | First code commit |
| 4 | Provision **dedicated benchmark host** (Linux, NVMe SSD, pinned-core capable) | Sudhakar / ops | ⏳ | Week-1 stack-ceiling measurement |
| 5 | ~~Confirm pilot customer OPC UA endpoint specifics~~ → **Adapter commits to support ALL endpoint specs** (user lock 2026-05-29) | Sudhakar | ✅ Locked | — |
| 6 | ~~Confirm pilot customer Rockwell CPU families~~ → **ALL families committed; timeline extension authorized** (user lock 2026-05-29) | Sudhakar | ✅ Locked | — |
| 7 | Source simulator licenses per §1.1 below (Studio 5000 Logix Emulate primary; CCW free) | Sudhakar / ops | ⏳ | Pre-week-5 EtherNet/IP smoke test |
| 8 | Confirm `dotnet --version` + capture pinned SDK version in `phase2-baseline.md` §7 | Implementation agent | ⏳ | Benchmark reproducibility |

**Definition of week-0-complete:** Items 1–4 must complete. Items 5/6 are LOCKED at user direction (no longer customer-blocked). Items 7/8 may proceed in parallel with early week-1 work but must complete before their respective gating items.

## §1.1 Simulator license procurement plan (Q11 lock)

Locked at user direction 2026-05-29. Concrete simulators required for the all-CPU-families commitment in §1 item 6:

| Need | Simulator | License path | Cost (indicative) | Decision status |
|---|---|---|---|---|
| **ControlLogix L7x/L8x + CompactLogix L1x/L3x/L4x** | Studio 5000 Logix Emulate (one product covers all five) | Rockwell subscription, per seat | ~$500–1,500/yr | **Primary purchase recommended** — single highest-leverage license |
| **Micro800 (Micro820/850)** | Connected Components Workbench with built-in simulator | Rockwell free download | **$0** | Procure immediately (zero cost) |
| MicroLogix 1100/1400 | RSLogix Emulate 500 (legacy) OR real dev hardware | Rockwell paid (~$200/yr) OR hardware ~$300 one-time | Either path | **Deferred** — confirm pilot customer uses MicroLogix before procurement |
| GuardLogix 5570/5380 safety | Logix Emulate + safety add-on | Add-on to Emulate | +~$500/yr | **Deferred** — confirm pilot customer uses GuardLogix safety before procurement |

**Procurement priority order:**
1. **Now**: Download CCW (free, covers Micro800) — no procurement work beyond installation.
2. **Pre-week-4**: Purchase Studio 5000 Logix Emulate subscription — primary covers 5 of 7 families.
3. **Pre-week-4 (conditional)**: If pilot customer confirms MicroLogix usage → procure RSLogix Emulate 500 OR ship a MicroLogix 1400 dev board.
4. **Pre-pilot-start (conditional)**: If pilot customer confirms GuardLogix safety usage → purchase Logix Emulate safety add-on.

**Alternative for budget-conscious teams**: real hardware dev kits (Micro820 ~$500 one-time, CompactLogix L18 ~$1,500 one-time, pre-owned ControlLogix L73 ~$2-3K on industrial resale channels) — slower to provision but more authoritative for pilot calibration. Studio 5000 Logix Emulate is recommended for the milestone work, hardware kits optional secondary.

---

## §2 Locked answers at v2.1 sign-off (quick reference)

### OPC UA Client — engineering baseline (Q7)

| Setting | Value |
|---|---|
| Auth | Anonymous |
| SecurityMode | SignAndEncrypt |
| SecurityPolicy | Basic256Sha256 |
| Cert trust | Auto-trust in lab; explicit trust-store in pilot |
| Endpoint | `opc.tcp://` |

Customer-facing throughput numbers held until real pilot endpoint specs arrive (item 5 above).

### EtherNet/IP CPU scope (Q8)

**ALL families committed** per user call. Engineering caveats:

- ✅ **In libplctag tested matrix** (low risk): L7x, L8x (`path=1,0`), CompactLogix L3x/L4x, MicroLogix 1100/1400, GuardLogix 5570/5380
- ⚠️ **NOT in libplctag tested matrix** (pre-week-5 smoke required, medium risk): **L1x**, **Micro800 (820 + 850)**

If pre-week-5 smoke fails on L1x or Micro800: surface to pilot customer as scope conversation, NOT silent narrowing.

### Stack-ceiling measurement sequence (Q9)

Exact order, all profiles per `multi-protocol-workload-profiles.md` §3:

1. **15K warmup** — 5 minutes, no measurement (avoids cold-JIT noise as "steady-state")
2. **30K sustained gate** — Profile A, 30 min, all 7 sustainability gates must hold (PRIMARY TARGET)
3. **50K stretch attempt** — Profile B, 30 min, informational unless all gates green
4. **75K exploratory ceiling** — Profile B′, 30 min, informational only (NEVER customer-facing)

### Benchmark governance (Q10)

- **Nightly dedicated host** — ONLY source for locked baseline numbers in `phase2-multi-protocol-baseline.md` §1–§5.
- **PR smoke shared infra** — 30-second smoke variant only; informational; never overwrites locked baselines.

---

## §3 Week-1 implementation work plan

Five concurrent work streams. Standard plan-trail kickoff: implementation agent owns sequencing; this list is the work-item set, not a sequential script.

### Stream 1 — OPC UA Foundation stack-ceiling measurement (HARD GATE)

**Goal:** lock the 30K primary + decide 50K stretch fate before any production code lands.

- Build throwaway benchmark project: `tests/ElpisEdgeConnect.Benchmarks.StackCeiling/`
- Subscribe UA Sample Server at 15K warmup → 30K → 50K → 75K per Q9 sequence
- Apply v2.1 §2.5 tuning knobs (12 specific defaults — `PublishingInterval=50ms`, `KeepAliveCount=20`, `LifetimeCount=60`, `MaxNotificationsPerPublish=1000`, `MinPublishRequestCount=subscription_count+2`, `SequentialPublishing=true`, `DeleteSubscriptionsOnClose=false`, etc.)
- Apply v2.1 §2.5 GC config to `Host.csproj` (`ServerGarbageCollection=true` + `ConcurrentGarbageCollection=true`)
- Workload per `workload-profiles.md` §3 Profile A — industrial-COV distribution, industrial payload mix, SignAndEncrypt baseline
- Capture results into `phase2-multi-protocol-baseline.md` §2 (50K) and §6 (75K informational)
- **Output:** locked 30K-primary number + 50K-stretch decision

### Stream 2 — Sink throughput audit

**Goal:** confirm MQTT + OPC UA Server sinks can drain at 30K/sec under Profile A workload BEFORE we declare end-to-end 30K achievable.

- Build sink-only benchmarks: existing MQTT sink + existing OPC UA Server sink at 30K CDPs/sec input
- Measure ceiling for each sink; identify whichever sink (if any) bottlenecks below 30K
- If either sink can't drain 30K → that becomes the new end-to-end gate; revisit v2.1 §1.3 work scope
- **Output:** per-sink locked ceiling numbers in `phase2-multi-protocol-baseline.md`

### Stream 3 — `ReconfigureAsync` default member (~2–3 days)

**Goal:** add v2.1 §1.3.5 contract to `ISourceAdapter` without breaking the 5 existing adapters.

- Edit `src/ElpisEdgeConnect.Core/Adapters/ISourceAdapter.cs` — add `ReconfigureAsync(SourceConfiguration newConfig, CancellationToken ct)` default member with safe Stop+Initialize+Start fallback
- Add tests in each existing adapter's test project verifying default-impl works (no behavioural regression)
- Document the contract for new adapters to override per v2.1 §1.3.5 invariants (active-set snapshot at batch boundary, reconfigure-during-reconfigure throws `RECONFIGURE_IN_PROGRESS`, validation BEFORE swap)
- **Output:** `ReconfigureAsync` available for OPC UA Client + EtherNet/IP to override starting week 2

### Stream 4 — `ITagBrowseService` + `TagBrowseTreeView` infrastructure

**Goal:** build the shared abstractions OPC UA Client consumes in week 2 (and EtherNet/IP in week 3).

- New: `src/ElpisEdgeConnect.Core/Browse/ITagBrowseService.cs` + `BrowseResult.cs` + `BrowseNode.cs` per v2.1 §1.3 / §4.1
- New: `src/ElpisEdgeConnect.Management/Components/Shared/TagBrowseTreeView.razor` — lazy-loading MudTreeView with multi-select + checkbox column + "Add selected" / "Add all under this node" actions
- bUnit tests for tree-view behaviour
- **Output:** shared infrastructure ready for OPC UA Client wizard work in week 2

### Stream 5 — ADR-0015 amendment commit

**Goal:** lock Rules 9 / 10 / 11 / 11.1 (drafted in v2.1 §4) as a real ADR amendment so all new wizards inherit them from day 1.

- Edit `docs/decisions/0015-wizard-contract.md` — append Rules 9 / 10 / 11 / 11.1 per v2.1 §4 wording
- Update ADR-0015 amendment log
- **Output:** wizard contract locked before any new wizard work starts

---

## §4 Doc map for implementation agents

| Doc | Purpose |
|---|---|
| `docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md` | **The plan.** Read §1–§7 cold. |
| `docs/benchmarks/multi-protocol-workload-profiles.md` | Locked benchmark rules + 5 active profiles + 2 future (F/G). |
| `docs/benchmarks/phase2-multi-protocol-baseline.md` | Scaffold for week-1 + week-7 measurements. |
| `docs/decisions/0015-wizard-contract.md` | Wizard contract (Rules 9/10/11 land in week-1 via Stream 5). |
| `docs/decisions/0016-onboarding-meta-wizard.md` | Onboarding meta-wizard rules — every new wizard inherits these unchanged. |
| `docs/ARCHITECTURE_BLUEPRINT.md` §19 | Routing semantics (per-sink isolation, ordering, backpressure) — already satisfied by Phase 1 architecture per v2.1 §1.1. |
| `docs/core/buffer-contract.md` | Buffer contract — locked behaviour the EtherNet/IP / OPC UA Client work consumes unchanged. |
| `CLAUDE.md` | Project guardrails. Specifically §3 (locked decisions) and §7 (working conventions). |
| **This handoff** | Operational checklist + week-1 work streams. |

---

## §5 Decision authority during implementation

| Decision class | Owner | Examples |
|---|---|---|
| Lock criteria from v2.1 §7 | **User (Sudhakar)** — locked at sign-off | Stack ceiling primary target, K1 protocol scope, dedicated host |
| Tuning-knob refinements within v2.1 §2.5 | Implementation agent + user review | Adjusting `KeepAliveCount` if measurement shows benefit |
| Scope changes (add/remove a protocol; add a feature beyond §1.1/§1.2) | **User only** — surface before implementing | "Add MELSEC after all?" |
| Test surface additions | Implementation agent | Adding a regression test for a discovered edge case |
| File-by-file deliverable adjustments | Implementation agent + user review | LOC estimates shift; structure changes minor |
| Risk-register additions | Implementation agent (append; never silently drop existing risks) | New risk found during library integration |
| **Pause-and-report triggers** | Implementation agent MUST pause | Stack ceiling measurement < 30K; L1x or Micro800 smoke fails; libplctag mapper deprecated faster than expected; ANY of v2.1 §1.3 invariants prove unenforceable in practice |

Per `feedback_pause_and_report.md`: surface every tradeoff, never decide unilaterally on scope.

---

## §6 Top risks to watch (compressed from v2.1 §5.6, updated 2026-05-29)

1. **OPC UA stack ceiling below 30K on pilot-class hardware** (Issue #2276 evidence — degradation at ~30K) — week-1 measurement is the load-bearing gate. Lock measured number honestly.
2. **L1x / Micro800 smoke fails** — pre-week-5 surfacing. **User has authorized timeline extension** rather than scope narrowing. Worst case: +1–2 weeks if BOTH need adapter-level adjustments.
3. **libplctag mapper removed in next major** (Issue #406 open since Jul-2024) — mitigation already locked: vendor ~250 LOC of `TagInfo` + `UdtInfo` decoders from day-one of EtherNet/IP work.
4. **Sink throughput audit reveals MQTT or OPC UA Server below 30K drain** — Stream 2's outcome may force a v2.1 §1.3 scope revisit.
5. **Channel-based dispatch race conditions under sustained 30K load** — pin via integration test; drop accounting MUST surface in diagnostics.
6. **Full endpoint matrix test surface bloat** — Q7 lock means ~12 sensible auth × security combinations need explicit test coverage. Mitigation: shared test harness fixture so adding a combination = one new test data row, not a new test method.
7. **Simulator license procurement delay** — Studio 5000 Logix Emulate is the primary purchase. If procurement takes >2 weeks, pre-week-5 smoke schedule shifts. Mitigation: download CCW now (covers Micro800 at $0); start Emulate procurement in week 0.

---

## §7 Definition of "kickoff complete"

Kickoff is complete when ALL of the following are true:

1. PR #45 merged + `v0.2.0-qa-baseline` tagged + `feat/multi-protocol-pilot-expansion` branched off master.
2. Dedicated benchmark host provisioned + concrete spec captured in `phase2-multi-protocol-baseline.md` §7.
3. Stream 5 ADR-0015 amendment committed.
4. Stream 1 (stack-ceiling measurement) is **running** OR has produced its first 30K-gate result.

When all 4 hold, the milestone has officially started. Stream 2/3/4 may already be in flight in parallel.

---

## §8 Pilot start ETA

Per v2.1 §5.4 (Option B hybrid sequencing): **end of week 7 or week 8 nominal, up to week 9–10 if L1x/Micro800 adapter work extends the milestone** (user authorized this extension 2026-05-29 — Q8 lock).

The 7–8 week nominal window absorbs:
- One operator-UX feedback cycle on the OPC UA Client wizard (~week 4 outcome)
- Calibration runs against real FactoryTalk + real ControlLogix (week 6)
- Combined QA cycle on the pilot zip (week 7)

The +1–2 week contingency (week 9–10) covers:
- L1x adapter-level adjustment if libplctag smoke fails on the L1x family
- Micro800 adapter-level adjustment if CCW + libplctag combination doesn't handle the Micro800 CIP variant cleanly

Slippage risks (in descending likelihood):
1. Stack ceiling forces 30K → 25K honest lock (week 1 outcome) — affects messaging, NOT pilot date
2. L1x AND Micro800 BOTH fail smoke (week 4–5) — triggers full 2-week extension
3. L1x OR Micro800 fails (only one) — triggers 1-week extension
4. Studio 5000 Logix Emulate procurement >2 weeks — shifts pre-week-5 smoke; may shift weeks 4–5 schedule
5. Full endpoint matrix testing reveals a UA stack quirk in a less-common auth × security combo — investigate but unlikely to be pilot-blocking

---

## §9 First commit

When `feat/multi-protocol-pilot-expansion` is branched and Stream 5 lands, the first production code commit should be the ADR-0015 amendment + the empty project skeletons for `Sources.OpcUaClient` and `Sources.EthernetIp` (csproj + namespace stubs only — no logic). This sets the milestone marker visible in `git log` and unblocks parallel streams.

Then week-1 measurement and infrastructure work runs from there.
