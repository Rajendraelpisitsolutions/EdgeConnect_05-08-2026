<!--
File:        docs/sessions/2026-06-04-phase2-marketing-complete-handoff.md
Purpose:     Phase 2 marketing wave closing handoff — captures the
             10 LOCKED page specs + 2 upstream v2.1 amendments + 15
             PR merges that landed during the wave, the cross-cutting
             governance patterns proven, the outstanding side-flags
             that didn't block Phase 2 close but need handling before
             publish-live, and the recommended next priorities.
Audience:    Next-session author (cold-start orientation). Also: user
             reviewing the wave, future Phase 3 / Phase E authors who
             will inherit the governance patterns.
Format:      Markdown session handoff per CLAUDE.md §"Decision records,
             platform principles, and session handoffs" governance.
Date:        2026-06-04
Status:      Phase 2 marketing wave COMPLETE.
-->

# Phase 2 marketing wave — closing handoff

**Wave complete: 10 of 10 per-page specs LOCKED and MERGED to master.** Plus 4 foundation governance docs (design-system v3 + Phase 2 IA memo + buyer-taxonomy v1 + proof-architecture v1), 2 retroactive v2.1 upstream amendments, and an established source-of-truth alignment + pre-lock validation workflow discipline that's worth carrying forward into Phase E + Phase 3.

---

## ⚡ 2026-06-04 status update (post-handoff-merge)

After this handoff was committed, two additional governance items resolved in the same session:

### Side-flag #1 — MT-LINKi publish-live gate: PARTIALLY RESOLVED + INTENTIONALLY DEFERRED

The original handoff (below) flagged MT-LINKi as a publish-live blocker because 4 marketing specs claimed MT-LINKi as operator-available today while CLAUDE.md §8 + the actual `SourceProtocolPickerModel.Tiles` catalog (6 tiles: Modbus / FOCAS2 / Brother HTTP / OPC UA Client / S7 / MTConnect — no MT-LINKi) confirmed it's NOT operator-available. Investigation confirmed:

- No wizard tile exists in `src/ElpisEdgeConnect.Management/Wizards/SourceProtocolPickerModel.cs`
- Backend code exists ONLY in legacy `src/ElpisEdgeConnect/DataSources/MtLinkiRestDataSource.cs` (not extracted to `Sources.MtLinki` modular adapter per ARCHITECTURE_BLUEPRINT §791 deliverable)
- `Sources.Focas2/Collectors/MtLinkiCollector.cs` is misleadingly named — it's a FOCAS2 internal sub-collector that gathers "MT-LINKi-compatible diagnostic fields" via direct FOCAS2, NOT an MT-LINKi protocol adapter

**User direction 2026-06-04: "We don't have any customer asking for MT-LINKi as of now. We can push it later as low priority."**

**Resolution applied via PR #84 (merged 2026-06-04):**

- **4 Phase 2 page specs amended** to drop MT-LINKi from today-list and frame as roadmap:
  - `/capabilities/connectivity-edge` v2.1 → **v2.2**
  - `/architecture` v2.1 → **v2.2**
  - `/solutions/edge-connectivity` v2 → **v2.1**
  - `/platform` v2 → **v2.1** (§6 anti-pattern row updated from "verification gate" to **RESOLVED** with re-add governance)
- **53 OTHER marketing files** with MT-LINKi mentions (homepage, datasheet, pitch deck, 5 existing v2 solution pages, SVG architecture diagrams, ROI calculator, sales-objection guide, foundation docs, build scripts, earlier-version superseded files) **intentionally LEFT UNTOUCHED** per user direction: *"adding UI for MT-LINKi for the existing migration won't take much time to implement."*

**Pre-launch governance reasoning** (per user-memory rule "prefer clean redesign/unification over compat hedges"): cost of amending 53 files ≈ cost of implementing the MT-LINKi engineering milestone, and engineering produces a real shipped capability whereas amendments only produce true copy. Schedule the engineering when bandwidth allows; don't sweep the broader marketing.

### Re-add governance (when MT-LINKi engineering ships)

Locked in `/platform` v2.1 §6 anti-pattern row:

> *"Future edits must NOT re-add MT-LINKi to the today protocol list until the engineering milestone ships (`Sources.MtLinki` modular adapter + Studio wizard + tests, per ARCHITECTURE_BLUEPRINT.md §791 deliverable). At ship-time, file v2.2/v2.3 amendments to re-add MT-LINKi to the today-list."*

The MT-LINKi engineering milestone scope (mirrors M.2b.2 S7 / M.2b.4 MTConnect pattern):

1. Extract `MtLinkiRestDataSource` from legacy → new `src/ElpisEdgeConnect.Sources.MtLinki/` modular assembly
2. Refactor → `MtLinkiSourceAdapter : ISourceAdapter` per Core abstractions
3. Backend Management APIs (TestConnect + Browse against MT-LINKi REST `:3000/api/v1/equipment/CNC/monitorings`)
4. Wizard tile in `SourceProtocolPickerModel.Tiles` (Pending → Available)
5. Wizard pages — `AddMtLinkiSource.razor` + model + tag-mapping editor
6. Host DI registration + license module catalog entry
7. Unit + integration tests (fake MT-LINKi REST fixture, similar to OPC UA in-process fixture from PR #60)
8. Update CLAUDE.md §8 current-state when shipped
9. File v2.2/v2.3 amendments on the 4 Phase 2 specs re-adding MT-LINKi to today-list

Estimated milestone size: ~1.5-2 weeks focused engineering. User assessment: *"won't take much time to implement"* given the existing legacy code as starting point.

### Side-flag #5 — PR merge status verification: RESOLVED

All Phase 2 marketing PRs (15 total) merged to master as part of the original wave closing. No outstanding PR merge dependencies for the 4 Phase 2 specs that are publish-ready.

---

## What landed on master (15 merges + 1 close, plus PR #84 v2.1/v2.2 amendments)

### Foundation governance (4 PRs)

| PR | Spec | Role |
|---|---|---|
| #42 | design-system v3 + Phase 2 IA memo | Foundation IA + design-system; everything else cites |
| #68 | design-system v3 revision (post-self-audit) | Locks v3 with cuts |
| #69 | buyer-taxonomy v1 | 7-buyer canonical model |
| #70 | proof-architecture v1 | Proof placement discipline |

PR #41 (original IA memo) was CLOSED as superseded by #42.

### Page specs (11 PRs, 10 page specs — one was amended after lock)

| PR | Spec | Version | Notes |
|---|---|---|---|
| #71 | `/capabilities` hub | v1 LOCKED | Canonical §9 template + per-page-type FAQ + metadata + emerging-pattern governance |
| #72 | `/capabilities/condition-monitoring` | v1.1 LOCKED | CapabilityDeepDive template-setter; v1.1 amendment 2026-05-29 expanded VAS equipment list with maintenance-buyer-tuned vocabulary |
| #73 | `/capabilities/connectivity-edge` | v2.1 LOCKED | Pillar 1; v2.1 amendment 2026-05-29 fixed OPC UA Server → OPC UA Client typo |
| #74 | `/capabilities/data-acquisition` | v2 LOCKED | Pillar 2 (mDAQ) |
| #75 | `/capabilities/asset-intelligence` | v2 LOCKED | Pillar 3 (mTracker) |
| #76 | `/capabilities/operational-intelligence` | v1 LOCKED | Pillar 5 (EREMOS V2) |
| #77 | `/architecture` | v2.1 LOCKED | v2.1 amendment 2026-05-29 retroactively M2-softened §3.7 FAQ Q4 historian-vendor naming |
| #78 | `/solutions` hub | v2 LOCKED | 7-solution outcome-first directory |
| #79 | `/solutions/predictive-maintenance` | v2 LOCKED | First solution depth-example using SolutionPanel §15 |
| #80 | `/solutions/edge-connectivity` | v2 LOCKED | Second solution depth-example; baked source-of-truth discipline from /predictive-maintenance v2 workflow lessons |
| #82 | `/platform` | v2 LOCKED | Vendor-worldview synthesis (FINAL Phase 2 spec) |

**Total ~17,000 words of LOCKED page-spec content** on master across 10 specs.

### Two v2.1 upstream amendments (filed during merge governance pass)

| Spec | Amendment | What it fixed |
|---|---|---|
| connectivity-edge v2.1 | §3.2 Card 1 protocol list | "OPC UA Server" (sink/expose mechanism) was mistakenly listed in source-polling slot. Corrected to "OPC UA Client" (source-polling protocol). Surfaced by /solutions/edge-connectivity v2 pre-lock workflow's cross-spec drift validator. |
| architecture v2.1 | §3.7 FAQ Q4 historian-vendor naming | The original v2 M2 softening pass on §3.6 Pattern B removed named-vendor list (PI / Wonderware / Aveva / Snowflake / Databricks / BigQuery); §3.7 FAQ Q4 was missed and retained the named list. v2.1 retroactively M2-softens Q4 to match Pattern B. Surfaced by /solutions/edge-connectivity v2 pre-lock workflow's cross-spec drift validator. |

---

## Cross-cutting governance patterns proven during the wave

These are worth preserving and re-using on Phase E + Phase 3 + future content waves.

### 1. The v1 → ChatGPT review → v2 LOCK cadence

Every page spec cycled through:
1. **v1 draft** — proposed content commitments to user, then drafted ~1,500-1,800 words
2. **ChatGPT review** — user runs ChatGPT review with provided prompt; pastes verdict verbatim
3. **Refinement categorization** — agent categorizes (ACCEPT / REJECT-WITH-EVIDENCE / USER'S CALL)
4. **User direction** — user picks via AskUserQuestion which to apply
5. **v2 LOCKED** — apply, commit, push, lock

Worked across 10 specs without breaking. The cadence is robust.

### 2. The §9 canonical template (`/capabilities` hub spec)

§9 of `page-capabilities-hub-spec-v1.md` is now the **canonical template for all per-page specs** in the program. It locks:
- 8-section spec template (header + §1 IA + §2 sections + §3 detail + §4 components + §5 verbatim copy + §6 anti-patterns + §7 sign-off + §8 out of scope)
- Per-page-type FAQ governance (inline FAQ YES/NO per page type)
- Per-page metadata governance (§1.4 metadata block required on every spec)
- Emerging-pattern governance ("How this differs from..." callout discipline)
- Template-evolution-by-amendment governance

Any future content spec (Phase E product pages, Phase 3 industries pages, future revisions) should follow this template unless explicitly amended.

### 3. The pre-lock validation workflow discipline

Proved 4 times during the wave:

| Spec | Workflow severity caught | Without workflow, would have shipped |
|---|---|---|
| /solutions hub v1 | LOW (judgement) | E-IDOS thermal/electrical fabrication; cross-lens CONTACT preset violation; OEM truck-rolls absolute claim |
| **/solutions/predictive-maintenance v1** | **HIGH (blocking)** | **E-IDOS-streams-today regression; mDAQ-E-IDOS conflation; VAS acronym drift** |
| /solutions/edge-connectivity v1 | CLEAN (polish only) | Q2 terminology consistency; whatsIncluded governance pattern |
| **/platform v1.5** | **HIGH (blocking)** | **OPC UA Server misattribution on FAQ Q5; hero subhead locked-anchor capitalization drift** |

**2 of 4 workflow passes caught HIGH-severity blocking drifts that ChatGPT entirely missed.** Pattern shape:

- 3 parallel validators (lenses tuned per spec)
  - Cross-spec drift (against LOCKED parent specs)
  - SolutionPanel §15 / CapabilityDeepDive coverage
  - Discipline-lock guard (verify accumulated discipline locks across all locations)
- 1 synthesizer rolls up into unified v2 plan

Cost: ~280-300k subagent tokens per pass, ~5-6 min wall-clock. Genuinely catches errors humans (including ChatGPT) miss when consulting locked corpus discipline.

### 4. Source-of-truth alignment baked into drafting

After /predictive-maintenance v1 had HIGH blockers, every subsequent spec drafted source-of-truth alignment INTO the v1 draft from the start. Edge-connectivity v1 came back CLEAN at discipline-lock level. /platform v1.5 still had drift but discipline-lock guard returned CLEAN.

**Pattern: discipline-during-drafting + workflow-validation-before-lock is the durable shape.** Workflow value scales inversely with drafting discipline — but workflow still catches genuine factual drift even on well-disciplined drafts.

### 5. Upstream-amendment governance (vs revert-downstream)

When workflow surfaced a cross-spec drift, the governance choice was: amend the parent (upstream) or revert the child (downstream)?

Applied 3 times during the wave:

1. **condition-monitoring v1 → v1.1** — VAS equipment list expanded with maintenance-buyer-tuned vocabulary (during predictive-maintenance v2 cycle)
2. **connectivity-edge v2 → v2.1** — OPC UA Server → OPC UA Client typo (during merge governance pass)
3. **architecture v2 → v2.1** — §3.7 FAQ Q4 retroactive M2 softening (during merge governance pass)

Pattern: per pre-launch governance rule ("prefer clean redesign/unification over compat hedges until a customer baseline exists"), amend upstream when the child spec's framing is the correct one and the parent has drift. Mark the amendment with explicit reasoning + cross-spec workflow attribution.

### 6. Locked trust-anchor phrasing discipline

Per positioning v3 §4 + amendment v4 §3 + §5, three trust anchors are LOCKED VERBATIM:
- *"Operating across India and the Middle East"*
- *"Deployed in defense and space-agency programs"*
- *"Maintenance and AMC providers across India and the Middle East"*

The /platform spec carries all 3 across multiple locations. The pre-lock workflow's locked-anchor verification caught a mid-sentence capitalization drift on the /platform v1.5 hero subhead and a parenthetical breaking the locked sentence boundary on FAQ Q6 — both fixed before lock. Future specs referencing these anchors must preserve them verbatim; the §3.6 governance cue in /platform documents the discipline for engineering and copy teams.

### 7. Emerging-pattern governance ("How this differs from..." callout)

The /capabilities/asset-intelligence v2 spec introduced a "How this differs from controller monitoring" callout that ChatGPT called *"the single most important improvement before lock."* The pattern was then formalized in /capabilities hub §9 governance and applied across:

| Spec | Adjacent category | Status |
|---|---|---|
| /capabilities/asset-intelligence v2 | Controller monitoring | ✓ applied |
| /capabilities/connectivity-edge v2 | General-purpose IoT gateway | ✓ applied |
| /capabilities/data-acquisition v2 | PLC-based data acquisition | ✓ applied |
| /solutions/predictive-maintenance v2 | Calendar-based preventive maintenance | ✓ applied |
| /solutions/edge-connectivity v2 | Per-vendor monitoring tools | ✓ applied |
| condition-monitoring v1, operational-intelligence v1 | (not retrofitted) | locked specs; not reopened |

Pattern is **available-but-optional** per §9 governance — only worth the page-space cost when there's a real adjacent category the buyer will confuse the spec's topic with.

### 8. SolutionPanel §15 schema variation pattern

SolutionPanel §15 schema defines 3 optional buckets (`edgeConnect?`, `eremosV2?`, `hardwareProducts?`). Two solution specs resolved the schema differently:

- **predictive-maintenance v2 (J1):** 2 buckets — `hardwareProducts` (mDAQ + VAS + E-IDOS sub-grouping) + `eremosV2`. EdgeConnect omitted (not used by this solution).
- **edge-connectivity v2 (J1):** 2 buckets — `hardwareProducts` relabeled "From the Edge Layer" (EdgeConnect software + Edge Gateway appliance sub-grouping) + `eremosV2`. Acquisition peer omitted.

Both resolutions per the pattern "**solution-page `whatsIncluded` buckets follow product-narrative groupings, not literal schema field names**." Documented in edge-connectivity §3.4 preamble as a governance pattern for Phase E v3 migration of the 5 existing v2 solution pages (cnc-machining, precision-manufacturing, brownfield-modernization, oem-machine-monitoring, multi-site-operations).

### 9. Anti-overclaim "cut" verb hedging precedent (OEM v2)

The locked `solution-oem-machine-monitoring-v2.md` deliberately uses "Cut truck rolls" (never "no truck rolls" or "eliminate truck rolls"). The pattern carried forward into:
- /solutions/predictive-maintenance v2 outcomes ("Cut unscheduled downtime", "Cut emergency dispatches")
- /solutions/edge-connectivity v2 outcomes ("Cut per-vendor monitoring tool sprawl", "Cut custom protocol scripting")
- /platform v2 anti-pattern table (explicit "no absolute outcome claims" guard)

Future content authors must use "cut" / "reduce" verbs, never "eliminate" / "no" / "zero".

---

## Outstanding side-flags (not Phase 2 blockers, but need handling before publish-live)

### SIDE-FLAG #1 — MT-LINKi platform-team verification (publish-live gate)

**Severity: BLOCKING for publish-live.** The 4 marketing specs that list MT-LINKi as operator-available today:

- /capabilities/connectivity-edge v2.1 §3.2 Card 1 + §1.2 vocab list
- /architecture v2.1 §3.2 annotation + §3.5 deployment paths grid
- /solutions/edge-connectivity v2 §3.1 hero subhead + §3.3 ¶1 + §3.4 + §3.5 FAQ Q4 + §3.7 + §1.2
- /platform v2 §3.3 Pillar 1 paragraph

vs **CLAUDE.md §8 "Current state (2026-05-31)"** which lists operator-available source protocols as: Modbus TCP, FANUC FOCAS2 (incl. demo mode), MTConnect, Brother HTTP, OPC UA Client, Siemens S7 — **MT-LINKi is NOT in that list.**

**Action needed:** Platform team verifies whether MT-LINKi is actually operator-available today (wizard tile = Available).

- **If YES:** Update CLAUDE.md §8 current-state to add MT-LINKi.
- **If NO:** File v1.1 amendments on all 4 marketing specs to drop MT-LINKi from today-list and move it to a roadmap mention.

Either path needs to land before any of these pages goes live on the website.

### SIDE-FLAG #4 — /solutions hub Card status-pill swaps when solution pages ship live

**Severity: PUBLISH-ORCHESTRATION concern.** The /solutions hub v2 §3.2 Card 1 (Edge Connectivity) and Card 2 (Predictive Maintenance) currently display "Coming soon" status pills and link to the underlying capability pages (pre-live link policy per §6 anti-pattern).

When `/solutions/edge-connectivity` and `/solutions/predictive-maintenance` actually ship live on the website, the hub Card 1 + Card 2 need:
- "Coming soon" status pill REMOVED
- Pre-live link (`/capabilities/connectivity-edge` and `/capabilities/condition-monitoring`) SWAPPED to live link (`/solutions/edge-connectivity` and `/solutions/predictive-maintenance`)

This is a publish-time configuration, not a spec amendment. Engineering should handle during the live-launch sequence.

### SIDE-FLAG #5 — PR merge status verification before publish

**Severity: RESOLVED.** All 15 Phase 2 PRs are now MERGED to master (this handoff is on master). The original concern was that the 4 LOCKED-on-branch specs (PR #72, #73, etc.) were being cross-referenced as authoritative while not yet merged. With master sync, the cross-references are now stable.

---

## Recommended next priorities

### Priority 1 — Resolve MT-LINKi platform-team verification (side-flag #1)

Single biggest blocker for publish-live. Either update CLAUDE.md §8 or file 4 v1.1 amendments. Should happen before any other publish-related work.

### Priority 2 — Phase E migration of existing v2 solution pages to SolutionPanel §15

The 5 existing-v2 solution pages (cnc-machining, precision-manufacturing, brownfield-modernization, oem-machine-monitoring, multi-site-operations) were authored in the Phase 1 marketing track BEFORE SolutionPanel §15 existed. They need migration to:
- §15 SolutionPanel 10-section structure
- §15 whatsIncluded bucket-narrative pattern (documented in edge-connectivity v2 §3.4 preamble)
- Inline FAQ per /capabilities hub §9 governance (already partially present in solution-cnc-machining-v2 §5)
- §1.4 metadata block per §9 governance
- "How this differs from..." callout governance where applicable
- Source-of-truth alignment discipline (post-predictive-maintenance v2 workflow lessons)

5 specs × the v1 → ChatGPT → v2 lock cadence = ~5 next-session cycles, each ~ size of one Phase 2 solution-page cycle.

### Priority 3 — Phase E product pages

10 product pages haven't been drafted yet:
- `/edgeconnect` (Pillar 1 software product detail)
- `/edge-gateway` (Pillar 1 hardware product detail)
- `/mdaq` (Pillar 2)
- `/mtracker` (Pillar 3)
- `/vas` (Pillar 4)
- `/e-idos` (Pillar 4)
- `/eremos-v2` (Pillar 5)
- (and 3 other deferred Phase E product pages)

Each product page is its own spec with its own structure (likely different from CapabilityDeepDive and SolutionPanel — product detail is yet another shape). Phase E work was deferred from Phase 2 per amendment v3.

### Priority 4 — Phase 3 customer-story registry + named-customer authorization

The current locked discipline has no named customers anywhere. Phase 3 introduces a customer-story sign-off process for named deployments. Until that lands, the trust-anchor governance (defense + space-agency + AMC + geography anchors) stays the only proof on every Phase 2 page.

### Priority 5 — Phase 3 industries pages

Per amendment v3 §1.4, industries are Phase 3 (not Phase 2). Phase 2.5 single-industry exception governance is in place if a specific deal / expo / channel motion requires it.

### Priority 6 — Phase 4 partner portal

Per amendment v4 §5, AMC partner names + OEM-channel partner content arrive with the Phase 4 partner portal. Until then, AMC channel remains anonymized per the locked phrasing.

---

## Quick reference for next-session orientation

**Most important files to read for cold-start orientation:**

1. `docs/marketing/page-capabilities-hub-spec-v1.md` §9 — the canonical template every per-page spec follows
2. `docs/marketing/design-system-v3.md` §15 SolutionPanel + §17 cross-lens preset table (line 554-560)
3. `docs/marketing/buyer-taxonomy-v1.md` — 7-buyer canonical model
4. `docs/marketing/proof-architecture-v1.md` — anti-overclaim discipline
5. `docs/marketing/industrial-intelligence-ecosystem-positioning-v3.md` §4 — locked trust-anchor phrasings + §6 commitment #3 (E-IDOS roadmap)
6. `docs/marketing/positioning-amendment-v4.md` §3 + §5 — customer-name unlock + AMC partner anonymization
7. **This handoff doc** — cross-cutting governance patterns proven during Phase 2

**Most important workflow patterns to re-use:**

- Pre-lock validation workflow (3 validators + 1 synthesizer)
- Source-of-truth alignment discipline (bake into v1 drafting from start)
- Upstream-amendment vs revert-downstream governance
- Locked trust-anchor verbatim preservation
- §15 SolutionPanel bucket-narrative pattern (for Phase E migration)

**Branches to know about:**

- `master` — all Phase 2 marketing content is here as of 2026-06-04
- Workflow scripts under `subagents/workflows/scripts/` — can be re-run with `{scriptPath, resumeFromRunId}` for resumable workflow testing
- 14 merged-and-stale `claude/marketing-*` branches — safe to delete after this handoff lands (see branch-cleanup task)

---

## Phase 2 marketing wave — done

Closing this session with master cleanly containing all 10 LOCKED page specs + 4 foundation governance docs + 2 v2.1 upstream amendments. The wave's cross-cutting governance patterns are documented above and should be re-used in Phase E + Phase 3.

Next session can start cold by reading this handoff + the 7 most-important files listed above.

---

*Phase 2 marketing wave closing handoff. Date: 2026-06-04. Status: COMPLETE. 10 page specs LOCKED + MERGED. 4 foundation docs LOCKED + MERGED. 2 v2.1 upstream amendments applied during merge governance pass. 5 cross-spec governance side-flags carried forward (1 publish-live blocker: MT-LINKi platform-team verification; 1 publish-orchestration concern: /solutions hub Card status-pill swaps; 3 resolved by merge pass). Next priorities: MT-LINKi verification → Phase E migration of existing v2 solution pages → Phase E product pages → Phase 3 customer-story registry → Phase 3 industries → Phase 4 partner portal.*
