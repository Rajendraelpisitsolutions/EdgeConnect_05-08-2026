<!--
File:        docs/sessions/2026-06-04-phase-e-solution-migration-plan.md
Purpose:     Phase E opening deliverable — plan-trail + kickoff for the
             BULK migration of the 5 existing Phase 1 v2 solution pages
             into the SolutionPanel §15 per-page-spec format (design-
             system v3 §15 Q3 LOCKED "bulk migration"). Captures scope,
             per-page buyer/pillar mapping, the cross-cutting migration
             decisions locked this session, MT-LINKi handling, the
             pattern-setter sequencing, and the validation plan.
Audience:    Next-session author (Phase E continuation), user + ChatGPT
             (reviewers), future Phase E product-page authors.
Format:      Markdown session plan-trail per CLAUDE.md §"Decision records,
             platform principles, and session handoffs" governance + the
             v1 → ChatGPT review → v2 plan cadence.
Date:        2026-06-04
Status:      COMPLETE — all 5 solution-page v3 specs LOCKED 2026-06-04
             (CNC pattern-setter + the batch of 4). See "Pattern-setter
             outcome" and "Batch wave outcome" below. Ready to ship as one
             wave; merge of PR #86 is the maintainer's call.
-->

> **⚡ Pattern-setter outcome (2026-06-04).** `page-solutions-cnc-machining-spec-v1.md` (page content v3) is **LOCKED** after ChatGPT review ("Approve with changes"; R1-R5 applied) + the pre-lock validation workflow (run `wf_9cb7e5e6-f68`; 0 HIGH / 6 MED, verdict LOCK-AFTER-FIXES; all 6 must-fixes + 3 optional-polish items applied). The locked precedents the other 4 migrations inherit (full detail in the CNC spec's header workflow note):
>
> - **P-A** — Typical Engagement is an optional §15 section; include where deployment anxiety is a real buyer objection, documenting rationale + word-budget consequence (10-section core stays in-band; the optional section is the documented over-ceiling reason). CNC = INCLUDED.
> - **P-B** — whatsIncluded buckets follow product-narrative groupings, not literal schema field names; document the bucket choice + omissions.
> - **P-C** — when a solution touches multiple pillars, the §17 cross-lens related-pillar card leads with the **differentiating** capability, not the outcome capability (others cross-linked inline in §3.3).
> - **P-D** — trust-cue placement follows the realized exemplar order (after Architecture), not the literal §15 prose (flagged for a future design-system v3.x reconciliation).
> - **P-E** — architecture-annotation eyebrow doubles as the ≤4-word title.
> - **P-F** — vertical solution-page hero subhead carries the relevant protocol **subset**; full list in the trust strip / §3.4.
> - **P-G** — MT-LINKi → roadmap only; S7 + OPC UA Client → today (the two correctness fixes the migration surfaces vs the stale Phase 1 v2 pages). **Apply P-G to all 4 remaining pages** — they carry the same stale claims.

---

## ⚡ Batch wave outcome (2026-06-04) — Phase E migration COMPLETE

The 4 remaining specs were drafted in parallel from the CNC pattern-setter (workflow `wf_dbe99974-ef1`), passed a batch ChatGPT review ("Approve the batch direction"), and a batch pre-lock validation workflow (`wf_e86046ac-cdb`; 3 validators × 4 pages reading the actual files + synthesizer). The validation returned **BLOCK (1 HIGH + 6 MED)**; all resolved, then **all 5 LOCKED + ready to ship as one wave**.

**Per-page lock state:** CNC v3 (pattern-setter) · precision-manufacturing v3 · brownfield-modernization v3 · oem-machine-monitoring v3 · multi-site-operations v3 — all **LOCKED**.

**The HIGH (a genuine pattern-propagation catch the ChatGPT pass missed):** brownfield inherited CNC's `"Book a scoping call"` CTA verbatim while flipping its primary buyer to the **OT Architect (§2.3)**, who *rejects* that CTA — and the §3.11 note miscited §2.3 as endorsing it. Fixed: primary CTA → `"Request an architecture review"` (§2.3-endorsed), `"Bring us your oldest CNC"` retained as the §2.3-compatible headline.

**New precedent locked:**

> - **P-H** — a migrated page that flips its **primary buyer off §2.2** must **RE-DERIVE its CTA from the new buyer's profile** — never inherit the CNC pattern-setter's §2.2 `"Book a scoping call"` verbatim. (OEM §2.6 = `"Request an OEM scoping call"` ✓; brownfield §2.3 = `"Request an architecture review"` ✓; precision + multi-site stay §2.2 = `"Book a scoping call"` ✓.)

**MED fixes (all governance-parity, same MF1/MF4/MF5 classes the CNC lock fixed):** meta-title bands (brownfield 49→53, multi-site 46→59); multi-site word-count self-cert (+~200→+~210 so the §7 "all three agree" is true) + meta-desc 161→160 + §6 banned-vocab parity (Industry 4.0 / AI-powered); OEM §5 table-sum reconciling clause + §6 store-and-forward guard completeness; precision §5 reconciling parenthetical. **Validated clean:** multi-site per-site/per-gateway identity discipline (all 4 live-copy locations) and the P-G protocol lists across all 4.

**Open questions resolved:** Q-A (cross-lens lead) — resolved at CNC lock (R4, lead with the differentiator); each batch page applied it (precision/multi-site → Operational Intelligence; brownfield → Connectivity & Edge; OEM → Asset Intelligence). Q-B (Typical Engagement) — included on CNC/precision/brownfield/multi-site, **omitted on OEM** (P-A counter-example). Q-C (v2 file retention) — the old `solution-*-v2.md` files are **retained** as voice-precedent references until the v3 pages ship live, then archived (not deleted at lock).

**Commit trail (PR #86, branch `claude/phase-e-cnc-migration`):** (1) lock CNC pattern-setter + plan; (2) draft batch baseline; (3) ChatGPT review fixes; (4) pre-lock validation fixes; (5) lock all 5 as one wave. Merge is the maintainer's call.

**Carry-forward (publish-orchestration, side-flag #4):** when the 5 solution pages ship live, the `/solutions` hub Cards' "Coming soon" status pills + pre-live links must swap to the live solution links. Engineering handles at launch.

# Phase E — solution-page SolutionPanel migration (plan-trail v1)

**This is the first Phase E deliverable** (design-system v3 §15 Q3 + §24 Q3 LOCKED): migrate all 5 existing Phase 1 v2 solution pages onto the `SolutionPanel` §15 layout / §9 canonical per-page-spec template, so the full solution set is visually + editorially consistent before the rest of Phase E (pitch deck v6, datasheet v4, security page v3, sales objection guide v3, ROI calc v3) builds on the migrated foundation.

The 5 pages:

| # | Source (Phase 1 page copy) | Migration target (new page spec) | Primary buyer |
|---|---|---|---|
| 1 | `solution-cnc-machining-v2.md` | `page-solutions-cnc-machining-spec-v1.md` | Plant manager / Ops VP (§2.2) |
| 2 | `solution-precision-manufacturing-v2.md` | `page-solutions-precision-manufacturing-spec-v1.md` | Plant manager / Ops VP (§2.2) |
| 3 | `solution-brownfield-modernization-v2.md` | `page-solutions-brownfield-modernization-spec-v1.md` | OT Architect (§2.3) / Plant manager |
| 4 | `solution-oem-machine-monitoring-v2.md` | `page-solutions-oem-machine-monitoring-spec-v1.md` | OEM machine builder (§2.6) |
| 5 | `solution-multi-site-operations-v2.md` | `page-solutions-multi-site-operations-spec-v1.md` | Plant manager / Ops VP (§2.2, multi-site) + CTO (§2.1) |

---

## 1. The "bulk vs cycles" reconciliation

Two governance signals are in tension:

- **design-system v3 §15 Q3 (LOCKED): "Bulk migration."** All 5 migrate *together*; piecemeal would create inconsistency in language / layout / pillar-reference patterns across the solution set during the transition window.
- **Phase 2 closing handoff:** frames it as "~5 next-session cycles."

**Reconciliation (this session's working model):** the lock is about the *shipping unit* (the 5 lock + go live as one consistent set, cross-referencing each other), NOT about doing all drafting in one pass. We therefore:

1. Draft **CNC as the pattern-setter** (design-system §15 explicitly anchors the SolutionPanel structural baseline to `solution-cnc-machining-v2.md`).
2. Run CNC v3 through the established **v1 → ChatGPT review → pre-lock validation workflow → v2 LOCK** cadence.
3. Lock the *migration pattern* on CNC.
4. Batch-apply the proven pattern to the other 4, cross-referenced for consistency.
5. **Lock + ship all 5 as one wave** — honoring the bulk-migration lock. No solution page goes live in v3 while a sibling is still v2.

User decision 2026-06-04: **pattern-setter first**, and **use the pre-lock validation workflow** at lock time.

---

## 2. Cross-cutting migration decisions (locked this session — apply to all 5)

These are the decisions that make the migration consistent. They are the contract the other 4 inherit from the CNC pattern-setter.

### D1 — Target format
Each migrated page is a **per-page spec following the §9 canonical template** (8-section spec doc: header + §1 IA/buyer + §2 structure-at-a-glance + §3 section-by-section + §4 components + §5 verbatim-copy summary + §6 anti-patterns + §7 sign-off + §8 out-of-scope), wrapping a **`SolutionPanel` §15** page layout. Same shape as the LOCKED `/solutions/edge-connectivity` and `/solutions/predictive-maintenance` specs.

### D2 — Filename + version convention
- New file: `page-solutions-<solution>-spec-v1.md` (matches the Phase 2 page-spec naming).
- The **page-spec doc** is "Page Spec v1" in the §9 sense.
- The **page content** is **v3** (the solution-page copy was v1 → v2 in Phase 1; the SolutionPanel migration is v3).
- The new spec **supersedes** the old `solution-<x>-v2.md` page copy. The v2 files are retained as Phase-1 voice-precedent references (not deleted) until the v3 specs lock + the live pages ship.

### D3 — The four §15 ecosystem-framing additions (NEW vs the Phase 1 CNC template)
Every migrated page gains:
1. **Pillar cross-references** — "How Elpis solves this" names the contributing capability pillar(s), each with an inline `/capabilities/<pillar>` cross-link.
2. **Trust cue** (§16 content pattern) — 2 cues max, cross-linking `/security`.
3. **`ArchitecturePanel.interactive` variant=`solution-annotated`** (§5.A) replacing the Phase 1 static SVG diagram, with solution-specific annotations + a "this solution doesn't use peer X" clarifier.
4. **Cross-lens navigation** (§17 LOCKED preset for `/solutions/<solution>`): `/capabilities/<related-pillar>` + `/architecture` + `/solutions`.

Plus the §9 governance additions: **§1.4 metadata block**, **inline FAQ with `FAQPage` schema** (`/solutions/<solution>` = YES, already locked in §15), and the **"How this differs from…" callout** where a real adjacent category exists.

### D4 — "Typical Engagement" is an optional-but-earned section
§15's structural baseline marks "Typical engagement (4-step rollout timeline)" **optional** ("earned by verticals where deployment anxiety is real"). The two Phase 2 SolutionPanel exemplars (edge-connectivity, predictive-maintenance) **omitted** it. Per-page decision for the migration:

| Page | Typical Engagement? | Rationale |
|---|---|---|
| CNC machining | **YES** | Plant-manager objection "how long does this take?" is explicit in buyer-taxonomy §2.2; the Phase 1 v2 timeline is strong content |
| Precision manufacturing | YES (likely) | Same Plant-manager buyer; tolerance/quality rollout cadence |
| Brownfield modernization | YES (likely) | Modernization-without-disruption is a phased story by nature |
| OEM machine monitoring | TBD | OEM buyer cares more about fleet/service-economics than a plant rollout timeline; decide at draft |
| Multi-site operations | YES (likely) | Multi-plant phased rollout is core to the narrative |

Including the optional section is **within** the locked SolutionPanel contract (not template evolution per §9) because §15 already lists it as an optional baseline section. Each migrated spec states explicitly whether it includes it and why.

### D5 — Source-of-truth alignment (bake into v1 drafting, per the predictive-maintenance v2 workflow lesson)
- **MT-LINKi → roadmap mention, NOT today-list.** Per side-flag #1 resolution (2026-06-04) + the locked re-add governance in `/platform` v2.1 §6. Migrating these 5 pages is exactly where their stale "MT-LINKi today" claims (carried since Phase 1) get corrected — these 5 were among the "53 untouched" files, and the migration resolves them naturally.
- **Today protocol list:** FOCAS2, MTConnect, Brother HTTP, Modbus TCP, OPC UA Client, S7 (per CLAUDE.md §8 + locked connectivity-edge v2). Per-page, surface the CNC-/vertical-relevant subset; cross-link Phase E `/edgeconnect` for the full matrix.
- **EdgeConnect = Windows today, Linux near-term roadmap (on Edge Gateway).** Honest-framing callouts required wherever Linux/appliance is mentioned.
- **Edge Gateway dual identity** (standalone PLC-to-cloud today → canonical EdgeConnect appliance once Linux ships); never "the only deployment path."
- **Per-gateway identity / anti-multi-plant-EdgeConnect** — each plant runs its own runtime; multi-site visibility from EREMOS V2 aggregation. (Especially load-bearing on multi-site-operations.)
- **"Beside, not replacing"** SCADA / historian / MES framing.
- **Anti-overclaim hedging:** "cut" / "reduce" verbs, never "eliminate" / "no" / "zero" (OEM v2 precedent).
- **No customer names / logos / fabricated metrics / competitor names** (proof-architecture v1 §3/§4/§8).

### D6 — Preserve Phase 1 voice character
The Phase 1 v2 pages have deliberate voice choices the migration must NOT sand off (e.g., CNC v2 kept "each dashboard speaks its own dialect" and "reports your team will actually read" per its own changelog). Migration restructures into 11 sections + adds ecosystem framing; it does not flatten the voice.

---

## 3. Per-page buyer + pillar mapping

| Page | Primary buyer | Secondary | Lead pillar (cross-ref + cross-lens card) | Other pillar(s) inline | "How this differs from…" |
|---|---|---|---|---|---|
| CNC machining | Plant mgr / Ops VP (§2.2) | OT Architect (§2.3) | Connectivity & Edge | Operational Intelligence | per-vendor CNC monitoring tools (NCStudio/BCMS/SmartBox) |
| Precision mfg | Plant mgr / Ops VP (§2.2) | OT Architect | Operational Intelligence | Connectivity & Edge | (TBD — quality/SPC tools?) |
| Brownfield | OT Architect (§2.3) | Plant mgr | Connectivity & Edge | Operational Intelligence | rip-and-replace modernization |
| OEM machine monitoring | OEM builder (§2.6) | — | Asset Intelligence | Connectivity & Edge | per-OEM proprietary telemetry |
| Multi-site ops | Plant mgr (§2.2) / CTO (§2.1) | OT Architect | Operational Intelligence | Connectivity & Edge | single-plant monitoring scaled by hand |

(Secondary buyers are served via cross-lens nav, not in primary page content — buyer-taxonomy §5 step 3.)

---

## 4. Sequencing + validation plan

1. **CNC v3 pattern-setter** — draft v1 (this session) → user runs ChatGPT review → categorize verdict (ACCEPT / REJECT-WITH-EVIDENCE / USER'S CALL) → user picks via AskUserQuestion → apply → **pre-lock validation workflow** (3 validators: cross-spec drift vs locked parents · SolutionPanel §15 coverage · discipline-lock guard; + 1 synthesizer) → v2 LOCK.
2. **Batch the other 4** — apply the proven pattern; one coordinated drafting pass for cross-page consistency.
3. **One ChatGPT review + one pre-lock workflow over the batch of 4.**
4. **Lock all 5 together; ship as one wave.**

Publish-time orchestration carry-forward (side-flag #4): when each `/solutions/<x>` v3 ships live, the `/solutions` hub Card status-pill ("Coming soon") + pre-live link must swap to the live solution link. Engineering handles at launch.

---

## 5. Open questions for the user / ChatGPT review

- **Q-A (CNC cross-lens related-pillar):** the §17 preset card is singular `/capabilities/<related-pillar>`, but CNC touches two pillars meaningfully. Pattern-setter choice: lead the cross-lens card with **Connectivity & Edge** (the protocol-coverage differentiator), with Operational Intelligence cross-linked inline in §3.3. Confirm or flip.
- **Q-B (Typical Engagement inclusion):** confirmed YES for CNC (D4). The other 4 decided at draft.
- **Q-C (v2 file retention):** keep the old `solution-*-v2.md` files as voice-precedent references until v3 ships live, then archive — confirm not delete-on-lock.

---

*Phase E solution-migration plan-trail v1 — 2026-06-04. Pattern-setter: CNC v3 (`page-solutions-cnc-machining-spec-v1.md`). Honors design-system v3 §15 Q3 bulk-migration lock (5 lock + ship as one wave) while de-risking drafting via a single pattern-setter cycle. Next: draft CNC v3 → ChatGPT review → pre-lock workflow → lock → batch the other 4.*
