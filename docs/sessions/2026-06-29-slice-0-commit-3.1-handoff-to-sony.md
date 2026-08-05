# Handoff to Sony — runtime reconfigure & adapter retirement (slice 0 → commit 3.1)

**Date:** 2026-06-29
**To:** Sony (working from her own PC + Claude account; dev env already set up)
**Where:** everything below is on **`master`** (HEAD `e19e47a`). `git pull` master and you have it all —
no branch dependency, nothing stuck on another branch.

---

## 0. TL;DR — what you're taking over

The task: let EdgeConnect **reconfigure / replace a source at runtime without the silent-stall class**
(the FOCAS2 incident where 8 sources sat "Running" but frozen for 14–18 h). It's being built as
**"slice 0"** in small commits.

- **Landed on master (done):** commit 1 (generation lease + publish-fencing gate), commit 2 (stable source
  slot + generation model + scoped intake writer), **commit 3.0 (inert adapter retirement attestation
  across all six source adapters)**.
- **Your next piece:** **commit 3.1 — the atomic supervisor cutover** (the behaviour-changing wiring). It
  is **fully specified but BLOCKED**.
- **Do not write 3.1 code yet.** It is gated on a FOCAS2 field measurement (QA is running it) plus a few
  bench/code-design confirmations. See §4.

---

## 1. Start here — read in this order

All paths are in the repo. Read top-to-bottom; each builds on the last.

1. `CLAUDE.md` — repo conventions, architectural locks, anti-patterns. Non-negotiable.
2. `docs/sessions/2026-06-24-focas2-stall-incident.md` — the production incident that started this.
3. Reconfigure + diagnostics plan trail (skim v1→v2, then the v3s):
   - `2026-06-23-runtime-reconfigure-systemic-plan-v1.md` / `-v2.md`
   - `2026-06-24-diagnostic-strengthening-plan-v1.md` → `2026-06-25-...-v2.md` →
     `...-reality-check.md` → `...-v3.md`
4. Slice 0 design:
   - `2026-06-25-source-generation-foundation-slice-0-spec.md`
   - `2026-06-25-slice-0-implementation-plan-v2.md`
5. The cutover plan: `2026-06-26-slice-0-commit-3-cutover-plan-v3.md`.
6. **3.0 as built (decision record):** `2026-06-26-slice-0-commit-3-complete-diff.md`.
7. **⭐ Your main doc — the 3.1 lock:** `2026-06-26-slice-0-commit-3.1-proof-matrix-v3.md`. This is what
   3.1 implements. It is **BLOCKED** at the top; §A–§I are the locked semantics, §K is the blocker list.
8. FOCAS measurement: `2026-06-26-focas2-field-measurement-procedure.md` and the QA package under
   `docs/qa/focas2-deadline-measurement/`.
9. The earlier foundation handoff (context on the 3.0/reconfigure split):
   `2026-06-26-slice-0-commit-3.0-sony-reconfigure-handoff.md`.

> Tip for your Claude session: open Claude Code at the repo root (it auto-reads `CLAUDE.md`), then point it
> at **this file** and the **v3 proof-matrix** first, and ask it to confirm the §4 blockers before any
> 3.1 code.

---

## 2. What's DONE on master

| Commit | Hash | What |
|--------|------|------|
| 1 | `3203ecd` | source-generation lease + publish-fencing gate |
| 2 | `c498ca5` | stable source slot, generation model, scoped intake writer (structural M1 fix) |
| 3.0 | `4baa5cd` | inert, opt-in `ISourceRetirement` quiescence attestation across all six adapters + Core helpers (`PollQuiescenceGate`, `PullAdapterRetirement`) + Host fail-closed discovery (`SourceRetirementCapability`) |

- Build **0/0**; full gate green at 3.0 (Core 969 · Host 211 · Management 1074 · Integration 87+1skip ·
  OPC UA 291 · MTConnect 59 · Brother 182 · FOCAS2 140 · Modbus 245 · S7 213).
- **3.0 is INERT:** no supervisor wiring (nothing calls `BeginRetirement` in production). The only live
  change is MTConnect/Brother's **behaviour-neutral poll-path guard** around `PollAsync`.
- Per-adapter proof classes (locked): Modbus/S7 = wire-idle; FOCAS2 = true dedicated-thread exit;
  OPC UA = callback-drain + reconnect-coordinator (Worker NotApplicable); MTConnect/Brother = in-flight
  poll drain.

---

## 3. What 3.1 is (your work, once unblocked)

The atomic supervisor cutover. Per the v3 proof-matrix it wires together, in one commit:
stable ingress + fences + reordered retirement + **one absolute monotonic deadline per retiring
generation** + composite admission proof + source-id permit before resourceful init + route-cascade
removal. The non-adapter acceptance gates are in v3 §I. Use the **implement → focused diff → finalize**
cadence, and the **plan-trail cadence** the team uses (v1 → review → v2 …, each its own dated
`docs/sessions/` file).

---

## 4. BLOCKERS before any 3.1 code (v3 §K)

1. **FOCAS2 field measurement (external blocker).** QA is running
   `docs/qa/focas2-deadline-measurement/`. When results come back, the deadline input is the **measured
   healthy max data-call duration + margin** — NOT the nominal 10 s (`TimeoutSeconds` only bounds handle
   allocation, and the code never calls `cnc_setdtimeout`, so data reads are unbounded). Paste the result
   into **v3 §F**. Until then v3 stays BLOCKED.
2. **Bench confirmations:** Modbus/S7 socket-timeout actually aborts a hung read; OPC UA worst-case drain
   rate. (You can do these against the code + ModSim without CNC time.)
3. **Code-design:** monotonic `TimeProvider` source; `HOST_CAP` / `MARGIN` constants — with v3 §B's
   **block-on-exceed** rule (never silently clamp a verified-healthy duration).
4. **First wiring step:** lock the surface → `GenerationRetirementCompletion` component mapping (v3 §C).

---

## 5. Branch strategy for you

- `git checkout master && git pull` → HEAD `e19e47a`. Everything you need is here.
- For 3.1, **branch from master** (a fresh `slice-0/commit-3.1-cutover`, or rebase your
  `Sony_Development` onto master HEAD).
- ⚠ Your `Sony_Development` may carry in-flight cross-cutting work (onboarding package #158) that touches
  the same Host/Generation surface. **Reconcile, don't blind-merge/fast-forward** — flag conflicts rather
  than assume a clean rebase.

---

## 6. QA + deployment context (in flight — coordinate)

- **QA is running the FOCAS field test** (package: `docs/qa/focas2-deadline-measurement/`). They return
  packet captures + a filled `results-template.md` + screenshots + logs. The timing numbers get extracted
  from the pcaps and pasted into v3 §F. That's the gate that unblocks you — track it.
- **A self-contained Studio+API deployment ZIP** was built for QA (win-x64, .NET runtime bundled, **FOCAS
  `Fwlib64.dll` + model loaders bundled for internal testing only**). It is a **build artifact — NOT in
  git** (it contains proprietary FANUC DLLs; keep it out of the repo). Rebuild if needed:
  ```
  dotnet publish src/ElpisEdgeConnect.Management/ElpisEdgeConnect.Management.csproj \
    -c Release -r win-x64 --self-contained true -p:NuGetAudit=false -o <out>/app
  # then copy the Fwlib64*.dll set into <out>/app and zip <out>
  ```
  The app is the all-in-one entry point (Studio + REST API + runtime); it binds `127.0.0.1:5080` and uses
  `C:\ProgramData\EdgeConnect\` as its data root (must be writable).

---

## 7. Working conventions / gotchas (learned on this work)

- **Planning cadence:** v1 → (external/ChatGPT) review → v2 → … each in its own dated `docs/sessions/`
  file. Don't skip the review pass.
- **Run the FULL `Management.Tests`** before any PR — topic filters silently skip cross-cutting isolation
  guards (a PR shipped broken that way).
- **Verify the branch** (`git branch --show-current`) before every commit; the prompt header goes stale.
- **Commit only on explicit instruction** (the user controls cadence). Push/PR after a clean milestone is
  fine; merges are the user's call.
- **Don't relitigate locks:** scan `docs/decisions/` (ADRs) and the architectural locks in `CLAUDE.md`
  before any design choice.

---

## 8. Open follow-ups (not 3.1, but on the radar)

- **Security:** `MessagePack 2.5.187` has known moderate CVEs (`NU1902`, transitive). Publish currently
  needs `-p:NuGetAudit=false`. Bump to a patched version and re-run the gate.
- **FOCAS mitigation (separate from 3.1):** the adapter never calls `cnc_setdtimeout`, so a wedged data
  call hangs the dedicated thread indefinitely. Evaluate setting a per-handle data timeout as an
  operational mitigation (shrinks the wedge window; doesn't change the 3.1 proof model).

---

**Bottom line:** pull master, read §1 in order, watch for the QA FOCAS result, fill v3 §F + clear the §4
bench/code-design items, then start 3.1 on the implement → focused-diff → finalize cadence. Ping back if
the `Sony_Development` rebase hits the #158 cross-cutting work.
