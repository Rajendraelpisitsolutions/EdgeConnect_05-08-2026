# Next session — FOCAS2 Studio wizard (M.2b.3)

**Date:** 2026-05-17
**For:** the next Claude Code session
**Repo state:** master at `545f722`
**Test baseline:** 1755/1755 passing, 0 warnings, 0 errors

---

## What just shipped (closing context)

| Milestone | PR | Status |
|---|---|---|
| M.P2.2 (runtime hot-reload + Studio reload-outcome surface) | [#3](https://github.com/elpisitsolutions/EdgeConnect/pull/3) | Merged |
| M.P2.3 (close the hot-reload startup-skip seam — ADR-0010) | [#4](https://github.com/elpisitsolutions/EdgeConnect/pull/4) | Merged |

Both via rebase merge. Recent master commits:

```
545f722 feat(host): M.P2.3 — coordinator synthesizes cross-record recovery
cfb0881 docs(planning): M.P2.3 plan reality-check — confirm 4 cross-record codes
9e0cfc6 docs(planning): M.P2.3 tactical plan v2 — locked
c29339b fix(management,docs): smoke-driven follow-ups for M.P2.2 phase 3
e993232 feat(host,management): M.P2.2 phase 3 — reload outcome surfaced end-to-end
```

Hot-reload is end-to-end and the startup-skip seam is closed. ADR-0009 and ADR-0010 are the design records.

---

## What "FOCAS2 work" means here

The user said "FOCAS2" but the source adapter itself was already migrated in earlier work — `src/ElpisEdgeConnect.Sources.Focas2/` exists and is fully covered (75 tests). The natural next FOCAS2 work is the **Studio wizard** for guided FOCAS2 source setup, per the M.P2.2 phase 3 plan's "What this milestone unlocks" list:

> M.2b.3 (FOCAS2 wizard) — wizards become operator-pleasant when Apply doesn't require a restart AND the Studio shows the reconcile outcome.

The hot-reload + reload-panel work that just shipped is what made wizards viable as a smooth operator experience — every wizard-driven apply now shows the operator exactly what came up.

**Confirm with the user at session start** whether they want the wizard, or something else FOCAS2-shaped (adapter polish, additional protocol coverage, etc.). Don't assume.

---

## Where to start

### Existing reference: Modbus wizard

```
src/ElpisEdgeConnect.Management/Wizards/ModbusSourceWizardModel.cs
src/ElpisEdgeConnect.Management/Wizards/WizardConfigMerger.cs
tests/ElpisEdgeConnect.Management.Tests/ModbusSourceWizardModelTests.cs
tests/ElpisEdgeConnect.Management.Tests/WizardConfigMergerTests.cs
```

Plus the Razor component(s) that drive the wizard UI — find them via `grep -rn "ModbusSourceWizard" src/ElpisEdgeConnect.Management/Components/`.

The FOCAS2 wizard follows the same shape: view-model + config-merger reuse + Razor stepper.

### Existing FOCAS2 source adapter (what the wizard configures)

```
src/ElpisEdgeConnect.Sources.Focas2/
  Focas2SourceAdapter.cs              -- the adapter
  Focas2SourceConfiguration.cs         -- the config record the wizard emits
  Focas2ConnectionManager.cs           -- HSSB / TCP / Ethernet handle management
  Focas2Interop.cs                     -- DllImport surface
  Focas2NativeApi.cs                   -- managed wrapper
  Focas2TagMap.cs                      -- tag-id → FOCAS2 register mapping
  README.md                            -- protocol notes
tests/ElpisEdgeConnect.Sources.Focas2.Tests/   (75 tests)
```

Key contract: `Focas2SourceConfiguration` is what the wizard produces and what the adapter consumes.

### FOCAS2 adapter constraints to surface in the wizard

From CLAUDE.md §8 and the README:

- **Handle limit:** ~8 simultaneous handles per controller. The wizard should warn (not block) when adding a source that would push a known controller past this limit.
- **Connection modes:** TCP, HSSB, Ethernet. Different connection-string shapes per mode.
- **Tag categorisation:** position / spindle / feed / parts-count / etc. — the wizard probably offers grouped tag-pick rather than raw FOCAS2 codes.

---

## How to start the new session

```powershell
cd C:\dev\EdgeConnect
git pull                                  # confirm master is at 545f722
git checkout -b claude/m2b3-focas2-wizard  # new branch from master
```

Or via worktree if preferred:

```powershell
cd C:\dev\EdgeConnect
git worktree add .\.claude\worktrees\m2b3-focas2-wizard -b claude/m2b3-focas2-wizard master
cd .\.claude\worktrees\m2b3-focas2-wizard
```

Then start Claude Code in that directory. Hand it this kickoff doc as the first message:

> Start M.2b.3 — FOCAS2 Studio wizard. Context handoff in `docs/sessions/2026-05-17-focas2-wizard-kickoff.md`. Confirm scope with me before writing any code.

---

## Suggested session flow (lifted from M.P2.2 / M.P2.3 pattern)

1. **Confirm scope** — Studio wizard for FOCAS2 source setup, or something else?
2. **Draft v1 plan** — `docs/sessions/2026-05-17-mp23-focas2-wizard-plan.md` (or appropriate name). Cover: scope, locked decisions carried from Modbus wizard, OPEN questions, deliverables, test plan, gate cadence.
3. **ChatGPT review pass** — same shape as M.P2.2 phase 3 and M.P2.3. Fold verdicts into v2 (locked).
4. **Commit v2.** Implementation per the sequence.
5. **Regression gates** at internal checkpoints.
6. **PR + rebase merge** to master.

This kickoff doc is itself the lightweight equivalent of the prior session's handoffs (e.g. `docs/sessions/2026-05-15-mp21-phase3b-handoff.md`).

---

## House-keeping leftover from this session

Three untracked items in `C:\dev\EdgeConnect`:

- `.claude/` — session metadata, leave alone.
- `CLAUDE.zip` — pre-existing user artifact.
- `current.json` (project root) — left over from manual smoke; verify content and remove if not needed.

Plus this worktree at `.claude/worktrees/inspiring-mcclintock-0ac389` (where M.P2.2 + M.P2.3 ran) can be removed once the new session is up:

```powershell
git worktree remove C:\dev\EdgeConnect\.claude\worktrees\inspiring-mcclintock-0ac389
```

Optional — doesn't block anything.

---

**End of handoff. Master is at 545f722, tests are clean, hot-reload story is shipped. New session starts fresh.**

---

## Resolution (2026-05-17)

M.2b.3 implemented per the cadence in this kickoff doc:

1. **Scope confirmation** — user picked "collect-all + group picker" for Data Points, "live probe IN v1", "warn but allow" for dup IP:Port. Locked.
2. **Plan v1** — `docs/sessions/2026-05-17-mp2b3-focas2-wizard-plan.md` (Locked A–K).
3. **Plan v2** — ChatGPT review folded in: `docs/sessions/2026-05-17-mp2b3-focas2-wizard-plan-v2.md` (added Locked L–R; "management-plane ephemeral" framing sentence ratified).
4. **Plan v3** — Step 1 reality-check on FOCAS2 adapter lifecycle: `docs/sessions/2026-05-17-mp2b3-focas2-wizard-plan-v3.md`. Added Locked S (probe override forcing `MaxConnectRetries=1` + `TimeoutSeconds≤8`) and revised Locked P (combined Stop+Dispose bounded at 12s, not Dispose-only at 5s).
5. **Implementation** — wizard model + Razor + Browse service + endpoint + tile catalogue. Locked R guarded via POCO model + 3 tests rather than bUnit (matches existing project pattern; no new test-framework dependency).
6. **ADR-0011** at `docs/decisions/0011-browse-controller-reuses-browsetagsasync.md`. Pins the "discovery is management-plane ephemeral" principle for future browse endpoints.
7. **Adapter SDK** doc gained a "Studio wizard" section linking the Browse Controller workflow.

Test target was ~1773; landed at 1782 (extra coverage on status-mapping and Locked S sub-assertion). The MTConnect end-to-end integration test flaked on the first run but passed on retry — pre-existing flake, unrelated to this milestone.
