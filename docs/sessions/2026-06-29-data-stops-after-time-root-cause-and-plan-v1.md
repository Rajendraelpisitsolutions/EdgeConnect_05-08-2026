# "Data stops reading from devices after a particular time" — root-cause analysis & remediation plan

**Date:** 2026-06-29
**Status:** analysis (code-confirmed) + plan. Structural cure is the existing slice-0 /
diagnostic-strengthening track — this note consolidates the *why* and the *sequence*, it does not
introduce new architecture.
**Scope:** the FOCAS2 / Modbus silent-stall class — sources report `Running` / "All systems healthy"
while data has actually stopped flowing (the 2026-06-24 incident, `gw-desktop-019ln49`, 8× FOCAS2 →
1 MQTT sink, silent for 14–20 h).
**Read alongside:** `2026-06-24-focas2-stall-incident.md`,
`2026-06-25-diagnostic-strengthening-reality-check.md` → `…-v3.md`,
`2026-06-26-slice-0-commit-3.1-proof-matrix-v3.md`,
`2026-06-29-slice-0-commit-3.1-handoff-to-sony.md`.

---

## 1. Symptom

A source reads normally, then **after some point in time stops producing data and never resumes** on
its own; only a gateway-service restart (or a source reconfigure, briefly) recovers it. Crucially the
lifecycle stays `Running` and global health stays green, so the stop is **silent**.

---

## 2. Root causes

The failure needs **RC-1 and RC-2 together**: RC-1 makes a source stop; RC-2 makes the stop invisible.
RC-3 explains why ad-hoc recovery doesn't hold.

### RC-1 (PRIMARY, code-proven) — a blocking read wedges the worker and never returns

A device read enters a blocking native/socket call that does not return. From that instant the source
produces nothing.

- **FOCAS2:** `await _thread.RunAsync(() => CollectAll(), ct)` has **no per-read deadline**
  (`Focas2SourceAdapter.cs:392`). `TimeoutSeconds` bounds only *handle allocation*
  (`Focas2ConnectionManager.cs:219`); **`cnc_setdtimeout` is never called**, so individual fwlib reads
  are unbounded. Cancellation only releases the *awaiter* (`Focas2Thread.cs:73`); the native call keeps
  the single thread-affine worker wedged for the life of the process.
- **Modbus:** synchronous read wrapped in `Task.Run(…, ct)` — the token cannot cancel an in-flight
  socket read. It *does* set `ReadTimeout`/`WriteTimeout` (`FluentModbusClient.cs:86-87`), so Modbus is
  **less exposed** than FOCAS2 — but whether that timeout reliably aborts a *black-hole* (half-open)
  connection is **unverified** (bench blocker).

### RC-2 (PRIMARY, code-proven) — health is "last observation", not "live progress" → silent

The supervisor parks on `await adapter.PollAsync(ct)` (`SourceSupervisor.cs:632`). If it never returns:
no exception is thrown → the source is never marked `Failed`; `RecordSourceObservation` never re-fires
→ the last recorded state stays `Running` and the footer stays "All systems healthy." There is **no
independent watchdog** asking "has this source produced a point in the last N seconds?" — and the
component that would notice is itself blocked on the same `await`.

### RC-3 (secondary, wedge-induced) — orphan resource accumulation

A wedged thread never reaches `Disconnect`/`FreeLibHandle`, so each "edit source to recover" leaks a
thread + handle + TCP session. These orphans can starve the controller's connection limit, which is why
reconfigure restores data for only ~1 minute before re-stalling. (Normal reconnect *does* free handles —
the leak is specific to the wedge path.)

### Trigger scenarios for RC-1 (which environmental event starts the wedge — UNPROVEN; need a live capture)

1. **Black-hole network / half-open TCP** — switch, firewall DROP, or NIC event leaves the connection
   "established" but no bytes flow.
2. **Controller connection-limit / concurrency cap** — the CNC silently parks a session.
3. **fwlib defect / internal lock** in the deployed `Fwlib64.dll`.
4. **Controller-state-dependent collector** — a specific fwlib call that does not return in some machine
   state (alarm / tool-change / power-save).

### Explicitly RULED OUT (do not chase)

- MQTT sink / store-and-forward — enqueued == drained, dropped 0, sink `Running`; boundary is upstream.
- Overflow backpressure — `DropOldest` is non-blocking.
- **Backoff growing to infinity** — capped via `Math.Min(…, MaxBackoffMs)` on both adapters (verified).
- License expiration — locked decision #7: data keeps flowing on expiry.
- A config/reconfigure event at stall onset — none recorded in the incident window.

### Confidence boundary

RC-1 and RC-2 are **proven by code**. The *trigger* (scenarios 1–4) is a ranked hypothesis set — no live
native/thread stack was ever captured. This is exactly why the structural fix's deadline value is
**measured, not guessed** (see Plan C).

---

## 3. Remediation plans

### Plan A — Operational mitigations (do now; reduce blast radius without the full fix)

| ID | Action | Effect | Notes |
|----|--------|--------|-------|
| A1 | Set a per-handle FOCAS data timeout (`cnc_setdtimeout`) | Bounds the wedge window so a hung read errors instead of hanging forever | Open follow-up (3.1 handoff §8); a *mitigation*, does not change the 3.1 proof model |
| A2 | Verify/lower Modbus `ReadTimeout`; bench a black-hole cut | Confirms Modbus self-heals on a dead connection | One of the §4 bench blockers |
| A3 | Operational watchdog / external alert on "last-point age > N min" | Removes the *silent* part — someone is paged before the platform fix lands | Achievable in monitoring today |
| A4 | Keep the service-restart runbook as the documented stopgap | Known recovery | Already in the incident doc |

### Plan B — Structural fix (the real cure; finish the slice-0 / diagnostic-strengthening track)

Already designed and partly landed — the plan is to *finish* it, not invent new architecture:

1. **Independent source-progress liveness** (RC-2 fix): a non-returning poll flips to
   `Degraded`/`Unhealthy` without waiting for the poll to return. *(diag-strengthening v3 — not landed)*
2. **Per-generation absolute monotonic deadline + composite admission proof** (RC-1 fix): the supervisor
   stops waiting on a wedged generation. *(slice-0 commit 3.1 — specified, BLOCKED)*
3. **Generation fencing + stable slot + scoped intake writer** (RC-3 fix): retire/replace a source
   without leaking publish authority or orphaning unboundedly. *(commits 1 & 2 — LANDED on master)*
4. **Retirement quiescence attestation** across all six source adapters. *(commit 3.0 — LANDED but INERT
   until 3.1 wires it)*
5. **Bounded orphan budget + terminal escalation** (RC-3 fix): cap abandoned threads/handles; escalate to
   process-restart / isolated adapter-worker if safe in-process abort is not available.

### Plan C — The gate that unblocks Plan B (critical path; a people-loop, not a git one)

Commit 3.1's deadline value **cannot be guessed** — the nominal 10 s is handle-alloc only. It needs the
**QA FOCAS2 field measurement** (`docs/qa/focas2-deadline-measurement/`): measured healthy-max read
duration + margin → pasted into proof-matrix **v3 §F**. Until that returns, 3.1 stays BLOCKED. Also
gated: bench items (Modbus/S7 socket-abort, OPC UA worst-case drain) and code-design items (monotonic
`TimeProvider`, `HOST_CAP` / `MARGIN`).

### Plan D — Verification (prove the fix; do not assume)

1. Deterministic blocking-call shims — a FOCAS hang shim + a non-responsive Modbus server — so the wedge
   is reproducible in tests (incident §12 action).
2. Black-hole network-cut test (QA plan Phase 3a) — the decisive case.
3. Re-run the leak harness post-3.1 to confirm no orphan/handle growth under repeated retirement.
4. Capture a live process dump at the next production recurrence — the one piece of evidence that would
   promote a trigger scenario from hypothesis to confirmed cause.

---

## 4. Recommended sequence

1. **Now:** A1 + A3 (shrink the wedge window + kill the silence) — biggest risk reduction for least
   effort.
2. **In parallel:** drive Plan C (QA measurement) — it is the long pole and a human handoff.
3. **On unblock:** land 3.1 (Plan B steps 1–2, 5), verified by Plan D.

---

## 5. Cause → fix traceability

| Cause | Corrective piece | Status |
|-------|------------------|--------|
| RC-1 unbounded read | per-generation absolute monotonic deadline (B2) + `cnc_setdtimeout` mitigation (A1) | 3.1 not built / blocked; A1 open |
| RC-1 can't-abort worker | retirement + quiescence attestation (B4) | 3.0 landed but inert |
| RC-2 no progress detection | independent source-progress liveness (B1) + watchdog alert (A3) | B1 not landed; A3 doable now |
| RC-3 orphan re-wedge | stable slot + publish fencing + scoped intake (B3) + bounded orphan budget (B5) | B3 landed; B5 not built |
| RC-1 trigger (env) | live dump (D4) + black-hole bench (A2/D2) | pending |

**Bottom line:** the diagnosis is code-confirmed (RC-1 + RC-2, with RC-3 explaining failed ad-hoc
recovery); the trigger remains a ranked hypothesis pending a live capture; and the structural cure is
~40% landed (commits 1/2/3.0) with the behaviour-changing piece (3.1) blocked on the QA measurement.
