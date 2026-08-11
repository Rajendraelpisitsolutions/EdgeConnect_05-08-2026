# Autonomous run handoff — 2026-05-22

**Status:** master is in a healthy state. No in-flight work. Next session opens fresh on a bounded job.

**Read first** if you're a new Claude session picking this up.

---

## 1. State of master

| Commit | What landed |
|---|---|
| `331f7a0` | Phase 2 wrap-up — Chip 4/5 implementation + Wave 1/2 plan trails + offline-scenario parity tests |
| `9cb492b` | Phase 2 wrap-up roadmap v1 → v2.3 |
| `9bb624e` | Bug 3 (P2) investigation plan |
| `afbf5d7` | EREMOS V2 contract revalidation (mock-fallback + SlowSinkDecorator + E2E foundation + Gate 5 deferred-with-finding + Gate 8) |
| `1dfd1d1` | M.2d.1 shared wizard primitives (6 components, 59 tests, no wizard touched) |

Plus the close-out commit landing now (this session) — adds Bug 3 to deployment-readiness §9 risks + §11 acceptance signal, and locks the Bug-3-before-M.2c dependency in the M.2c v2 plan §16.1.

Cross-project: `C:\dev\shared-knowledge\contracts\eremos-per-tag-mqtt.md` was updated (commit `42d8403`) with the locked tag-path sanitization rule.

---

## 2. Open work — what the next session inherits

### 2.1 Plan trails awaiting ChatGPT review (user-driven)

The user takes plans to ChatGPT, then pastes feedback back. The new Claude session does the agree/reject/alter verdict + v2 draft + commit + PR + merge. **Recommended order (per user 2026-05-22):**

1. **M.2d.2 Source Wizards** — `docs/sessions/2026-05-21-m2d2-source-wizards-plan.md` (255 lines, on master)
2. **M.2d.3 Sink + Route Editors** — `docs/sessions/2026-05-21-m2d3-sink-route-editors-plan.md` (306 lines, on master)
3. **M.2d.4 Cross-Wizard Sweep** — `docs/sessions/2026-05-21-m2d4-cross-wizard-sweep-plan.md` (251 lines, on master)

Then either Chip 3 Provisioning Subsystem implementation OR Bug 3 investigation — user's choice at that point.

### 2.2 Hard locks for downstream sessions

- **M.2c implementation does NOT start until Bug 3 is at least understood** (M.2c v2 plan §16.1, added this session). Both touch runtime confidence + operator-facing diagnostics; Bug 3 may reveal a deeper reconnect issue that affects Runtime Tap as well.
- **Bug 3 (P2)** must stay visible as an active readiness risk. Now tracked at:
  - GitHub issue [#24](https://github.com/elpisitsolutions/EdgeConnect/issues/24)
  - Investigation plan: `docs/sessions/2026-05-22-bug3-mqtt-reconnect-investigation.md`
  - Deployment readiness §9 risk row + §11 acceptance signal checkbox
  - M.2c v2 plan §16.1 dependency lock

### 2.3 EREMOS V2 — what's measured vs deferred

| Gate | Status |
|---|---|
| Gate 1 (MQTT stability), Gate 2 (emit/receive parity), Gate 3 (schema), Gate 4 (topic determinism), Gate 8 (backpressure) | ✅ PASSING via integration tests |
| **Gate 5 (broker-outage reconnect)** | **⚠️ [Skip] in test, real finding tracked as Bug 3 issue #24** |
| Gate 6 (EREMOS ingestion), Gate 7 (duplicate detection) | 🚫 Skip on mock-fallback path (real-EREMOS-only) |

Real-EREMOS path is gated on the customer-bound EREMOS V2 binary being reachable in the in-house lab. `BuildMockFallbackReport` enforces the v2 §4.3 invariant — explicit skip reasons, never silent passes.

### 2.4 Customer-deployment readiness checklist (deployment-readiness §11)

- [x] §7 customer answers locked
- [x] M.P2.4 Brother HTTP migration COMPLETE
- [ ] Bulk-provision generator + templates (Chip 3 — plan trail at v2 on master; implementation pending)
- [ ] 7-day in-house soak
- [ ] 48-hour customer-site acceptance plan
- [ ] EREMOS V2 contract validation — **5 of 6 measurable gates landed; Gate 5 deferred pending Bug 3**
- [ ] **Bug 3 understood OR resolved before 7-day soak gate** (new row added this session)

---

## 3. Established patterns (use these — they work)

### 3.1 Plan-trail review cycle

When the user pastes ChatGPT feedback on a v1 plan:

1. Produce a brief verdict table — every item gets `✅ Agree` / `🔧 Alter` / `🔴 Reject` with reason
2. Per-item brief reasoning (no padding)
3. Surface any architectural insight / supersession explicitly (e.g., Chip 3's Q-V1-H was retracted by MQTT placeholder injection)
4. Size estimate for v2 (lines, sections)
5. Recommendation: proceed to v2 OR take one more review pass

If user says "Draft V2", produce a complete v2 file at `docs/sessions/<date>-<topic>-plan-v2.md`. v1 stays on disk as the open-questions reference.

### 3.2 Commit + PR + merge ownership

The user authorized: **"All the commits and merge I depend on you"** (2026-05-22).

- Branch from origin/master for each PR
- Commit with detailed multi-line messages (the existing master history is your style guide — look at recent commits)
- Push + `gh pr create` with a thorough body (test plan, deferrals, findings)
- `gh pr merge --squash --delete-branch` after PR opens cleanly
- Local cleanup may fail with `fatal: 'master' is already used by worktree at 'C:/dev/EdgeConnect/.claude/worktrees/happy-cartwright-465442'` — that's the user's main workspace, **don't try to fix it**. The remote merge succeeded regardless.

### 3.3 Honest deferral over fake completion

When something doesn't work as planned (e.g., Gate 5 reconnect threshold not met):

- **Don't** fudge the test to pass
- **Don't** hide the finding
- **Do** mark `[Fact(Skip = "...")]` with the FULL observation in the skip reason
- **Do** surface in commit messages + PR descriptions
- **Do** create a Bug N tracking issue + investigation plan if it's a real concern
- **Do** add to deployment-readiness §9 if it affects customer install

### 3.4 Cross-project edits

For `C:\dev\shared-knowledge\` changes:

- The user authorized cross-project edits in autonomous mode (2026-05-22)
- `cd "C:/dev/shared-knowledge"` — separate git repo
- Commit with clear message referencing the EdgeConnect-side source
- Push to `main` (their main branch is `main`, not `master`)

### 3.5 Worktree hygiene

This worktree is `C:\dev\EdgeConnect\.claude\worktrees\tender-edison-639a71`. The user's main workspace at `C:\dev\EdgeConnect\` is checked out on a different branch (master). **Don't try to check out master in this worktree** — it will fail with the "already used by worktree" error.

When you need to refresh local state: `git fetch origin` + `git reset --hard origin/master` (on a NEW branch off master) is the safe pattern.

---

## 4. Key files for the next session to read

Read in this order:

1. **`CLAUDE.md`** (project root) — locked architectural invariants, working conventions
2. **`docs/platform-principles.md`** — P1 Runtime Tap (relevant to M.2c), P2 POCO+Razor (relevant to M.2d.2)
3. **This handoff doc** (you're reading it)
4. **For M.2d.2 review:** `docs/sessions/2026-05-21-m2d2-source-wizards-plan.md` (the v1 plan), then `docs/sessions/2026-05-21-m2d1-shared-primitives-plan-v2.md` (the primitives M.2d.2 adopts)
5. **For Bug 3 follow-up:** `docs/sessions/2026-05-22-bug3-mqtt-reconnect-investigation.md` + `tests/ElpisEdgeConnect.Integration.Tests/Eremos/EremosV2EndToEndTests.cs` (Gate 5 test method with the deferred-finding `[Skip]` reason)
6. **For Chip 3 implementation:** `docs/sessions/2026-05-21-chip3-provisioning-subsystem-plan-v2.md` (the LOCKED v2 plan, 749 lines, 16 review items folded in)

---

## 5. Recent open questions the next session may face

These came up but were resolved or deferred. New-session-Claude can refer here if the user references them:

- **`pwsh.exe` invocation from xUnit** (Chip 3 Q-V1-E, Q-V2-B) — v3 reality-check items, not architectural; addressed at implementation time.
- **Real EREMOS V2 binary in lab** (EREMOS V2 v2 §6.1 path 1) — gated on customer engineering providing the binary.
- **MQTTnet client session-state coupling** (Bug 3 H1) — primary investigation hypothesis.
- **Modbus operator-declared deviceClass** (EREMOS V2 v2 §8 audit) — correct behaviour, not a bug; surfaces only if the customer ever adds Modbus under non-`cnc` class.

---

## 6. What this session deliberately did NOT do

- Did not start any of: M.2d.2 implementation, M.2d.3 implementation, M.2d.4 implementation, Chip 3 implementation, Bug 3 root-cause investigation.
- Did not pin GitHub issue #24 (that's a UI action only the user can perform).
- Did not approve or alter any architectural decisions without the user's review cycle.

---

**End of handoff. Master is at a clean checkpoint. Next session can pick up cold with a focused job from §2.1.**
