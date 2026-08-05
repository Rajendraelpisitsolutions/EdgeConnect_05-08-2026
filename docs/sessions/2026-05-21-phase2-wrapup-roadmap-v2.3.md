# Phase 2 wrap-up — roadmap (v2.3 amendment)

**Status:** v2.3 — three implementation-discipline guardrails from the Option-B ratification review.
**Date:** 2026-05-21
**Predecessor:** [v2.2 (locked)](2026-05-21-phase2-wrapup-roadmap-v2.2.md), which extends v2.1, which extends v2.
**Trigger:** ChatGPT ratified the "Option B + parallel Option A drafting" execution strategy AND surfaced three implementation-discipline guardrails. v2.3 locks them at the Roadmap layer so all 7 downstream plan trails + Chip 4/5/offline-test implementation inherit them.

---

## 1. The three guardrails

### 1.1 No-new-shared-abstractions during Option B implementation (LOCKED)

**The risk:** during Chip 4 + Chip 5 + offline-scenario test implementation, it's tempting to extract "helpful" abstractions — a small observability helper, a wizard utility, a runtime cache, a provisioning schema validator. These often become **accidental platform contracts** that conflict with the dedicated plan trails being drafted in parallel (M.2c, Chip 3, M.2d).

**Locked rule for the Option-B implementation window:**

| Allowed | Forbidden |
|---|---|
| Local implementation helpers (private methods, file-scoped types) | New shared framework abstractions |
| Bounded feature-specific code (lives inside one project, used by one consumer) | Reusable UI shells |
| Test fixtures (testing-only, scoped to one test project) | Runtime Tap helpers (M.2c territory) |
| Deployment utilities (scripts under `tools/` scoped to one operation) | Generic provisioning primitives (Chip 3 territory) |
| | Cross-wizard contracts (M.2d territory) |

**Trigger to pause + surface:** if implementing Chip 4 / Chip 5 / offline-scenario test tempts me to write something that fits a "Forbidden" row, **stop and report.** The right answer is almost always "defer until the dedicated plan trail lands." The wrong answer is "I'll do a temporary version and we'll harden it later."

This rule applies until the 7 dedicated plan trails are ratified. After that, the per-plan locks govern.

---

### 1.2 Terminology freeze (LOCKED)

**The risk:** ChatGPT observed that long-lived concepts are forming, and terminology drift becomes architectural drift later if alternate names creep into implementation docs / commit messages / tests.

**Canonical terminology — use these exact names; reject synonyms in code, comments, docs, commits:**

| Canonical term | Definition (one-line) | Anti-synonyms to reject |
|---|---|---|
| **Runtime Tap** | Observational read-only side-channel over the data path (P1). | "live stream", "data tap", "runtime stream", "telemetry tap" |
| **Watch session** | Operator-initiated, browser-bound, transient Runtime Tap consumption. | "subscription", "live view session", "watch socket" |
| **Operational Intelligence** | Working name for the emergent layer combining Runtime Tap + diagnostics + AI substrate. (Formal name deferred per v2 §4.7.) | "ops intelligence", "diagnostics platform", "observability layer" until formal ADR lands |
| **Provisioning Subsystem** | The Chip 3 deliverable — template + CSV + generator with provenance + identity. | "bulk-provision tooling", "config generator", "provisioning scripts" |
| **Golden-source template** | A version-controlled per-machine-type template that is the single source of truth; operators never hand-edit generated configs. | "master template", "reference template", "canonical template" |
| **`_provisioning` block** | The configuration-identity + provenance block stamped onto every generated `gateway.json` (per v2 §3.4.3). | "provenance header", "config metadata", "identity block" |
| **Configuration identity** | The `templateId` + `fleetId` + `configFingerprint` + `csvFingerprint` fields within `_provisioning`. | "config fingerprint" alone (ambiguous — that's only one field), "deployment metadata" |
| **Canonical tag path** | The hierarchical path in `BrotherTagMap` / `Focas2TagMap` / etc. that operators select via DataPoints filter; append-only stable within an M.PX.x line. | "tag name path", "topic path" (topics are MQTT-side artifacts, not canonical), "canonical name" |
| **Append-only catalog semantics** | The Brother (and future) tag-map catalog invariant — paths added, never renamed or repurposed, within a milestone line. | "stable catalog", "frozen catalog" |
| **Deterministic replay** | The runtime invariant that recorded canonical points re-played with tap on vs tap off must produce byte-identical output. | "reproducibility", "replay determinism" (close, but the canonical phrasing is "deterministic replay") |
| **Evidence packet** | Anticipated AI-substrate concept — a bounded, attribution-friendly bundle of canonical points + diagnostics + audit links that justifies an AI advisory recommendation. (No code today; reserved terminology.) | "context bundle", "AI context", "evidence bundle" |
| **Drift detection** | The reserved name for the future provisioning-vs-actual divergence detector (per v2 §3.4.1 future-work). NOT in v1 scope. | "config drift", "template drift", "deployment drift" |

**Enforcement:** apply during writing commit messages, plan trail docs, test names, code comments, README updates. If a non-canonical synonym sneaks in, flag and replace.

**Living list:** if a new long-lived concept emerges, add to this table BEFORE shipping the code that introduces it. Adding to this table is a small v2.X amendment, not a strategic-review-level change.

---

### 1.3 Platform contracts — deferred follow-up (NEW future-work)

**Observation (from ChatGPT review):** several long-lived behavioral guarantees are emerging as the real backbone of EdgeConnect:

- Runtime Tap observational-only (P1)
- Canonical tag-path stability (append-only catalog)
- Deterministic replay guarantees
- AI advisory-only (no AI in data path; Locked Decision #14)
- No fire-and-forget runtime tasks (v3.1 §B.4)
- No legacy DTO leaks into Core / sinks (M.P2.4 §2 lock)

These currently live scattered across `docs/platform-principles.md`, `docs/ARCHITECTURE_BLUEPRINT.md` Appendix A locked decisions, individual milestone plan trails, and the project's `CLAUDE.md`.

**Locked decision:** **No `docs/contracts/` folder now.** v2.3 does NOT create the folder, does NOT consolidate existing contracts into a new surface. The current sources of truth (platform-principles.md + ARCHITECTURE_BLUEPRINT.md Appendix A + per-milestone plan trails + CLAUDE.md) are sufficient.

**Trigger for future consolidation:** when there are **≥10 distinct platform-level behavioral contracts** scattered across the existing sources, OR when a single contract is found to be referenced from 4+ separate documents (suggesting it deserves a canonical home), consolidate into:

- `docs/contracts/<contract-id>.md` per contract.
- An index at `docs/contracts/README.md` mapping the contract back to its origin (P1, ADR, milestone plan, etc.).

**Until then:** new platform contracts get captured in the appropriate existing surface (platform-principles.md for cross-milestone, ADR for major architectural decision, milestone plan for implementation invariants). The principle-escalation threshold (v2.2 §1.1) governs movement between layers.

---

## 2. What stays unchanged

All v2 + v2.1 + v2.2 locks remain in force. v2.3 adds three new implementation-discipline guardrails; it retracts nothing.

---

## 3. Knock-on effects for the upcoming implementation work

- **Chip 4 + Chip 5 + offline-scenario test** (next implementation session): the no-shared-abstractions rule (§1.1) applies during this work. The terminology freeze (§1.2) applies to commit messages + code comments + test names.
- **7 dedicated plan trails** (drafted in parallel): each plan's "Out of scope" subsection inherits §1.1's Forbidden list as a hard input. Each plan uses the canonical terminology from §1.2.
- **M.2c plan trail specifically:** in addition to the 5 anti-scope bullets from v2.2 §1.3, the plan's introduction MUST use "Runtime Tap" + "Watch session" + "Operational Intelligence" terminology consistently.

---

## 4. Next steps (unchanged in shape from v2.2 §4)

1. **You ChatGPT-review v2.3** (short pass — just the three additions).
2. **In parallel:** I begin Chip 4 implementation. Chip 5 + offline-scenario test follow in the same implementation track.
3. **In parallel:** drafting M.2c v1 + Chip 3 v1 (the two highest-leverage architectural tracks) when implementation hits a natural pause.

If v2.3 needs adjustments (e.g., terminology in §1.2 needs additions or removals), the v2.X amendment cadence continues; we don't block Chip 4 implementation on terminology debates.

---

**End of v2.3 amendment. LOCKED — ready for review. Chip 4 implementation begins next.**
