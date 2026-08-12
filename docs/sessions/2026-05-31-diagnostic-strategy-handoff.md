# 2026-05-30/31 — Diagnostic strategy review & handoff

**Status:** Closed. Strategic review complete; ADRs filed; tasks queued.
**Authors of decisions:** operator (driver) + ChatGPT (review pass) + Claude (synthesis & filing)
**Filing date:** 2026-05-31

---

## Why this session existed

Right after the OPC UA Client end-to-end pipeline finally worked (PR 7c-4 + 8 hotfixes; see `2026-05-30-opcua-client-wizard-debugging-followups.md`), the operator asked:

> *"Will it be good idea to have a page, where we can select the Source and see what data we are receiving it from? and select Destination and see what data we are sending? We can view this data side by side also, so that we can compare what we recieve and what we send also if needed. What's your suggestion? I am asking based on the current experience, but you can suggest what are the other items that will be useful for debugging and Diagnostics purpose... Lets list."*

That request seeded a multi-day diagnostic-strategy review:

1. Claude proposed an initial diagnostic surface set → 3 ADRs (0017 / 0018 / 0019) + 5 static HTML mockups.
2. Operator reviewed the mockups → 5 feedback points → round-2 mockup revisions.
3. ChatGPT review pass produced 14 additional feature proposals across 4 tiers + a 4-phase roadmap.
4. Claude's review: accept/reject/alter per proposal + 6 own additions.
5. ChatGPT's reaction to Claude's review: elevated 3 items above all, proposed Root Cause Timeline + Confidence Score.
6. Operator's final pass: locked Route Timeline (no causality claim), structured chips (no composite score), and P7 amendment to platform principles.

This session note is the record of that synthesis.

---

## What was decided

### 1. P7 amended into `docs/platform-principles.md`

> P7. Surfaces explain outcomes, not just observations.

Wording adapted from ChatGPT's proposed draft, lightly normalized for tone consistency with P1–P6. The four-question framework — *what happened, why, what changed, what action* — is the load-bearing piece. Every future surface gets evaluated against whether it answers all four (or honestly marks which it can't). The explicit *"never from LLM summarisation"* clause is the long-term guardrail.

The principle also locks two derived disciplines:

- **No composite scores**: collapsing multi-dimensional health into a single number is forbidden. ADR-0027 is the worked example.
- **Three operational levels** (Observation / Explanation / Guidance) — EdgeConnect surfaces aim for Level 3.

### 2. Eight ADRs filed (0020 through 0027)

| ADR | Title | Tier in roadmap |
|---|---|---|
| 0020 | Diagnostic Bundle redaction spec | Phase A (prerequisite for 0021's bundle inclusion) |
| 0021 | Route Flight Recorder | Phase A |
| 0022 | Certificate Trust Center (promotes task #53) | Phase A |
| 0023 | Explain Why Data Is Missing | Phase B |
| 0024 | What Changed | Phase B |
| 0025 | Last-Known-Good config pin | Phase B |
| 0026 | Route Timeline (renders Recorder + Explain + What Changed) | Phase B |
| 0027 | Route Health Surface (chips + status; no score) | Phase B |

All eight share the same evidence model — `StateChangeRecord` (0024) and `FlightRecorderEvent` (0021) are the load-bearing data shapes; the rest are renderings or anchors over them.

### 3. Roadmap reconciled

Final phasing (rejecting full implementations of #12 Route Simulator, #14 SLA Dashboard, Confidence Score variant; adopting four-phase structure):

**In-flight (already filed pre-2026-05-30, will land first):**
- #54 SourceDetail metrics
- #57 ADR-0017 IsAnyoneListening primitive
- #58/59/60 Live Data Tap (Stream / Compare / Inspect)
- #61/62 Capability Coverage (Health Checklist)

**Phase A — Trust Foundation:**
- ADR-0020 spec lands → Bundle generation → Flight Recorder → Cert Trust Center (#53 promoted)

**Phase B — Explainability:**
- Explain Why → What Changed → Last-Known-Good pin → Route Timeline → Route Health Surface

**Phase C — Operator Trust mini-milestone (small, additive, premium-feel):**
- Quiet Hours / change-window guard
- Adapter Self-Test surface
- Impact Analysis (pre-delete dependency walk)
- Transform Rename Preview

**Phase D — Differentiation:**
- Tag Search across gateway
- Data Lineage
- Bundle Replay (engineer-side load of a customer bundle)
- Connectivity Digital Twin (deferred form; Phase 5 fleet view)

### 4. Explicitly rejected items (do not re-propose without a strong new argument)

| Item | Why rejected |
|---|---|
| Route Simulator (#12 in ChatGPT's list) | High engineering cost; few customers will use; Adapter Self-Test (Phase C) is the better FAT/SAT story at a fraction of the cost |
| Route SLA Dashboard (#14) | Duplicates Prometheus; customers running production already have a dashboard. Revisit only if we move to hosted/SaaS. |
| Confidence Score / Route Health Score | Composite scores collapse dimensions, mask signal, drive operator-management-to-the-score pathology. Forbidden by ADR-0027. |
| "Root Cause Timeline" naming | Claims causality the system can't prove. Renamed to Route Timeline (ADR-0026). Correlation indicators ≠ causality claims. |
| Full Time Machine (data-flow replay) | Storage cost; conflicts with demand-driven principle. Narrow state-replay variant subsumed by Route Timeline's time window selector. |
| LLM-generated diagnostic summaries | Violates P7's honesty clause. AI agents may converse over already-explained surfaces; they don't generate the explanation. |
| Auto-pinning of Last-Known-Good | Auto-pins create the same composite-score pathology — system claims confidence about which config is "good." Operator-explicit only. |
| "Trust everything from this CA" toggle | Violates Trust Center's per-cert audit-trail requirement (ADR-0022 Rule 5). |

---

## Static HTML mockups status

Round-2 mockups committed at `b9df3ef` (branch `feat/opcua-client-wizard`). Five surfaces:

- `1-tap-stream.html` — Live Data Tap Stream mode (task #58)
- `2-tap-compare.html` — Live Data Tap Compare mode (task #59)
- `3-tap-inspect.html` — Live Data Tap Inspect mode (task #60)
- `4-capability-coverage.html` — Capability Coverage / Health Checklist (task #62)
- `5-source-detail-metrics.html` — SourceDetail with metrics panel (task #54)

The mockups predate ADRs 0020–0027. Round 3 (when the Phase A ADRs reach implementation) should produce mockups for:

- Bundle generation manifest preview (ADR-0020)
- Flight Recorder viewer (ADR-0021)
- Trust Center (ADR-0022)

Round 4 covers Phase B:

- Explain-Why "Why?" expander (ADR-0023)
- What Changed surface (ADR-0024)
- Last-Known-Good pin UI + diff preview (ADR-0025)
- Route Timeline (ADR-0026)
- Route Health Surface chips (ADR-0027)

---

## Open follow-ups

1. **Phase C and D items** are listed but not yet filed as ADRs. File closer to implementation — drafting ADRs now would be premature for surfaces that won't ship for many milestones.
2. **Bundle Replay** (Phase D, my addition **F**) needs a small companion ADR when it's ready to start — out of scope here.
3. **`StateChangeRecord` source-of-truth integration** across the Flight Recorder, Trust Center, and License Manager is cross-cutting work. The ADRs (0021 + 0022 + 0024) reference it; the integration touches three subsystems. Plan as part of the Phase B implementation kickoff.
4. **Adapter SDK guide update** — ADR-0021 mentions emitters need structured-context guidance. Add to the adapter SDK docs when next touched.

---

## Branch + commit state at session close

- **Branch:** `feat/opcua-client-wizard` — already carries the 14-PR OPC UA Client adapter + 8 hotfixes + ADRs 0017/0018/0019 + UX mockups
- **Pending PR:** the wizard work is reviewable; the diagnostic-strategy ADRs (0020–0027 + P7 amendment + this session note) ride along on the same branch since they emerged directly from that work
- **Tests:** unchanged from `11f5674` baseline — 901 tests passing across 7 projects. No code changes in this filing pass; ADRs and docs only.

The next session should pick up by reading `docs/platform-principles.md` (P7 now included) + this note + the relevant ADR for the implementation task being picked up.

---

## What this session does NOT close

The strategic alignment is complete. The implementation is not started. Phase A ADRs (0020/0021/0022) are next-up; each has its own task in the task list (filed alongside this session note). Closing the strategic review unblocks implementation; it does not perform implementation.

---

**End of session note. P7 + ADRs 0020–0027 + roadmap reconciliation locked. Next session: start Phase A implementation by reading ADR-0020 (Bundle redaction spec) and walking the per-field tier classification across the existing config model.**
