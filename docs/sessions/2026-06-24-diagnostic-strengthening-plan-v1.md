# Diagnostic strengthening + FOCAS2 stall root-cause methodology — Plan v1

**Date:** 2026-06-24
**Status:** v1 — draft for review pass (ChatGPT/user) before v2 lock. Do not implement from v1.
**Author:** session handoff
**Trigger:** The 2026-06-24 FOCAS2 stall incident (`2026-06-24-focas2-stall-incident.md`). All 8 sources
silently stopped for ~18h while the Studio showed "All systems healthy." The root cause was reachable
only by reading source code + screenshots — **EdgeConnect's own diagnostics did not surface it.**
**Confirmed cross-protocol (2026-06-24):** the same silent-stall symptom occurs with **Modbus TCP**,
not just FOCAS2 — see §1.5. This is a *shared* defect class, not a FOCAS-only bug.
**Goal (user, verbatim intent):** find the root cause of the FOCAS wedge, **and** strengthen the
diagnostic system so that next time *this class of issue — or any other — is caught by our diagnostic
system itself*, not by manual source reading.

> Cadence: v1 → review → v2 → reality-check → v3, each its own dated file. v1 is not a lock.

---

## 0. The reframe

Two goals, deliberately coupled:

- **A. Detection** — the product must *raise the alarm* when data silently stops (no more 18h blind
  spot). Proactive, always-on, operator-visible.
- **B. Forensics** — when the alarm fires, the product must *capture enough to root-cause it* (which
  call hung, socket state, timing, logs) — for this FOCAS wedge and for unknown future issues.

These are coupled because **we cannot fully root-cause the FOCAS wedge from here right now**: there is
no wedged process to inspect (production is currently on the alternate app, nothing is stuck). The
strengthened diagnostics (B) are the *instrument* that will capture the wedge when it recurs. So the
honest sequence is: build detection + forensics → let EdgeConnect run/wedge under observation →
capture → root-cause the trigger. Detection is also the prerequisite for root cause, not a detour.

---

## 1. Why our diagnostics missed an 18-hour total outage (gap analysis, grounded)

| # | Gap | Evidence | Consequence in this incident |
|---|-----|----------|------------------------------|
| G1 | **Health is state-based, not liveness-based.** "Running" never becomes "Stale," even with no data for 18h. The last-observation timestamp IS tracked (`SourceSupervisor` → `RecordSourceObservation`) but nothing converts its *age* into a health verdict. | Sources page showed Running + "last point 18h ago" + green; footer "All systems healthy." | The outage was invisible for ~18h. **This is the single highest-value fix and it's cheap** — the timestamp already exists. |
| G2 | **The diagnostic bundle is config-only.** Contributors = identity, inventory, config, history, audit. No runtime contributor (health, metrics, logs, faults, flight-recorder, buffer stats). | `V1Contributors.cs`; captured bundle had 10 files, all config/audit. | The bundle was useless for triage; root cause needed live screenshots + source reading. |
| G3 | **"Explain Why Data Is Missing" (ADR-0023) is Proposed, reactive, and assumes data exists.** It triggers on a Live Data Tap *Compare* verdict (a per-point "missing-on-sink"), which requires reference data to compare against. | ADR-0023 Status: Proposed (2026-05-30); Rule 1 trigger = a Compare verdict. | With zero data flowing, no verdict is produced → the explainability feature wouldn't even fire. The "source-side gap" case (Rule 5) has no *proactive* trigger. |
| G4 | **No per-adapter call instrumentation.** Health exposes poll/success counters but not per-collector/per-native-call timing or last-success-per-call. | FOCAS2 `CheckHealthAsync` metrics are aggregate; no "which call, how long." | We could not see *which* fwlib call hung — the key fact for root-causing the wedge. |
| G5 | **Flight recorder captures state transitions, not silent stalls.** A source that stops without throwing emits no transition, so the recorder/timeline shows nothing. | Route detail "Recent events": only the 20h-ago "entered Running." | No timeline marker at the moment data stopped. |

---

## 1.5 Shared root-cause *class* (confirmed cross-protocol)

The silent stall is **not** a FOCAS-only bug. It is a defect class shared by every adapter that wraps
blocking device I/O:

- **EdgeConnect places no enforced upper bound on how long `PollAsync` may block**, and the
  supervisor's only protection — the cancellation token — **cannot interrupt an in-flight blocking
  device read.**
- **FOCAS2:** native fwlib call on the dedicated `Focas2Thread` — `ct` cannot abort it; unbounded hang
  (`Focas2Thread.cs`, `Focas2SourceAdapter.cs:345`).
- **Modbus TCP:** synchronous `_client.ReadHoldingRegisters(...)` wrapped in `Task.Run(..., ct)`
  (`FluentModbusClient.cs:213`). `Task.Run`'s `ct` only prevents the task from *starting* — once the
  blocking socket read is in flight, `ct` cannot cancel it. Socket `ReadTimeout` bounds *some* wedge
  cases but not all, can't be cancelled, and burns a threadpool thread while blocked.
- **The same exposure exists for S7, OPC UA Client, EtherNet/IP** — any adapter doing blocking device
  I/O. The supervisor `await adapter.PollAsync(ct)` trusts `ct` to bound the call; no adapter can
  honor that for an in-flight blocking read.

Result: when a device/network wedges, the poll never returns, the source goes silent, and — because
health is state-based — nothing notices. **One symptom, one class, all protocols.** This means the
primary resilience fix belongs at the **supervisor level (shared), not per-adapter** — see L1.5.

The *trigger* of the wedge is still per-environment (network idle-drop, device limit, etc., per the
incident doc) and must be root-caused separately on site; this class explains why the wedge becomes a
*silent, unrecoverable* outage regardless of trigger.

## 2. Strengthening plan (layers; A = detection, B = forensics)

### L1 — Proactive staleness / liveness health (A) — *highest value, lowest cost*
- Add a **staleness rule** to source (and route) health: if `now - lastObservation > K × expectedInterval`
  (or an absolute floor for event/subscription sources), the source is **Stale/Degraded**, regardless
  of adapter `State`. The `lastObservation` timestamp already exists.
- Surface it everywhere the operator looks: Sources list state chip, route health, and the **"All
  systems healthy" footer** must become **"1 source stale (18h)"**. The green banner that lies is the
  worst offender.
- Define "expected interval" per capability: polling = poll interval; subscription/event = a
  configurable max-silence (CNCs in cut should emit regularly; a long-idle machine is legitimately
  quiet — so the threshold is per-source-tunable with a sane default, and "stale" vs "idle" must be
  distinguishable; see open question Q2).

### L1.5 — Supervisor-level poll watchdog (A+B, shared resilience) — *the cross-protocol fix*
- Bound every `adapter.PollAsync(ct)` (and the subscribe pump's liveness) at the **supervisor**, e.g.
  `CancelAfter(K × expectedInterval)`. This is the shared analog of the per-adapter watchdog noted in
  the incident doc — one mechanism covers FOCAS2, Modbus, S7, OPC UA, EtherNet/IP.
- On timeout: record an **in-flight-too-long** diagnostic (which adapter, how long), flip the source to
  **Stale/Degraded** (feeds L1), and trigger a **recycle** of that source.
- **Honest caveat (must be in the design):** a supervisor `CancelAfter` cancels the *await*, but the
  orphaned blocking call keeps running (FOCAS dedicated thread / Modbus threadpool thread). Full
  recovery therefore still needs **per-adapter cleanup of the orphaned generation** (recycle the
  `Focas2Thread` / reconnect the Modbus socket) — the supervisor watchdog gives detection + "stop
  waiting"; the adapter gives the clean teardown. Both are needed; detection ships first.
- Keep the *detection* half strictly observational (P1) so it can ship ahead of the *recycle* half.

### L2 — Runtime diagnostic-bundle contributors (B) — *makes the bundle useful for incidents*
Add a `Runtime` bundle capability with contributors capturing a point-in-time snapshot:
- **Health snapshot** per source/sink/route (state, level, lastObservation age, connection state,
  poll/success/fail counters, last error).
- **Recent logs** (last N minutes / N KB, redacted) — currently absent entirely.
- **Fault registry** dump.
- **Flight-recorder events** per route (ADR-0021) + **buffer stats** (depth, enqueued/drained/dropped).
- **Per-adapter diagnostics** (the L3 call instrumentation).
Fail-soft per contributor (a stuck adapter must not block the bundle); note any skipped surface in the
manifest (the redaction/exclude pattern already exists).

### L3 — Per-adapter call instrumentation (A+B) — *detects the hang AND names the culprit*
- Each source records **per-collector / per-native-call**: last-attempt time, last-success time,
  in-flight start time, duration histogram, consecutive-failure count.
- A call **in-flight longer than a threshold** is itself a first-class health signal ("collector
  `ToolCollector` in-flight 47s") — this is what would have told us *which* fwlib call hung.
- Generalize the shape across adapters (FOCAS2 collectors, Modbus blocks, OPC UA subscriptions) so the
  signal is uniform in the bundle and Studio.

### L4 — Proactive "source-side gap" explainer (A) — *extend ADR-0023 beyond Compare*
- Add a **proactive trigger** for the explainer's "source-side gap" walk (ADR-0023 Rule 5 row 4):
  fire on the L1 staleness signal, not only on a Compare verdict. Output the because-chain
  `DeviceLastSeen → SourceConnection → SourcePoll/Subscribe → SourceCapture` so the operator sees
  *"source stopped capturing at HH:MM — last successful poll at …, connection state …, last call
  in-flight since …"* without running a Compare.
- This is the bridge that would have turned "All systems healthy" into a one-click root-cause for the
  operator.

### L5 — FOCAS2 wedge root-cause methodology (the immediate "find the root cause" ask)
The wedge trigger is environmental and must be captured live. Until L1–L3 land, use a manual capture
kit; after they land, the bundle captures most of it automatically. **When a source next wedges,
before restarting**, capture:
- **Process thread/stack dump** (the single most valuable artifact) — shows exactly which fwlib
  function each `Focas2-<id>` thread is parked in → identifies the hanging call.
- `netstat -ano | findstr :8193` — are the FOCAS sockets ESTABLISHED-but-dead (idle-drop signature)?
- Continuous `ping` / `Test-NetConnection -Port 8193` to each CNC during the wedge.
- Firewall / managed-switch **TCP idle/session-timeout** config on the gateway↔CNC path.
- **fwlib version** on the gateway + CNC models/options (FOCAS simultaneous-connection limit per model).
- Whether the **alternate app is connected to the same CNCs concurrently** (FOCAS client-limit contention).
- Correlate the original stall time (~06-23 evening IST) with any network-event log.

Ranked trigger hypotheses (from the incident doc): (1) network idle-drop of the long-lived FOCAS
socket; (2) concurrent FOCAS clients exceeding the CNC limit; (3) fwlib-level contention / a specific
collector call. EdgeConnect-side amplifiers to check: no TCP keepalive on the FOCAS socket, the
`KeepAlive` persistent-handle mode, 8 concurrent handles per process, no per-call timeout.

**Reproduction path:** since production is safe on the alternate app, re-point **one or two CNCs** at
EdgeConnect (or run in parallel where the CNC's client limit allows) with L1–L3 instrumentation live,
and wait for the wedge under observation. This converts "we couldn't inspect it" into a captured event.

---

## 3. Sequencing (proposal)

1. **L1 (staleness health)** first — smallest diff, ends the silent-outage blind spot, and is the
   alarm that everything else hangs off. No ADR blocker (extends ADR-0027 route-health).
2. **L1.5 (supervisor poll watchdog, detection half)** — bounds every poll and feeds the Stale signal;
   the shared cross-protocol fix. Detection half is observational and ships with L1.
3. **L3 (per-call instrumentation)** — needed to name the culprit call; feeds L2 and the root cause.
4. **L2 (runtime bundle)** — package L1/L1.5/L3 + logs/faults so an incident bundle is actually useful.
5. **L4 (proactive explainer)** — wire ADR-0023's source-side-gap walk to the L1 trigger.
6. **L1.5 recycle half + per-adapter cleanup** — the resilience half (recycle the wedged source +
   clean up the orphaned blocking call). Deliberately after detection, per user: detection first.
7. **L5 (trigger root cause)** — run the capture kit / reproduction with the new instrumentation live;
   then fix the per-environment *trigger* (keepalive / firewall / device connection-count).

L1–L4 are **protocol-agnostic** — they catch *any* source/sink silent stall, which is the "or anything
other issues" the user asked for, not just FOCAS or Modbus.

---

## 4. ADR implications (surface in review, don't silently decide)

- **ADR-0027 (route-health surface):** extend with a liveness/staleness dimension (L1). Likely an
  amendment, not a new ADR.
- **ADR-0023 (explain-why-data-missing):** currently Proposed + reactive. L4 adds a proactive trigger;
  decide whether to amend 0023 or advance it to Accepted with the trigger extension.
- **ADR-0020 (diagnostic-bundle):** add a `Runtime` capability + contributors (L2). The redaction spec
  already governs content; runtime logs/health need their own redaction review (secrets in logs).
- Possible **new ADR**: "Liveness is a health dimension" — if the staleness model proves to be a
  cross-cutting commitment (it likely is — it governs every adapter's health semantics).

---

## 5. Open questions for the review pass

1. **Staleness threshold model:** absolute, multiple-of-interval, or per-source configurable? How do we
   distinguish a *stalled* source from a legitimately *idle* machine (powered off, no production) so we
   don't cry wolf? (Q2 — likely: per-source expected-cadence + a "machine idle" vs "source stalled"
   distinction using connection state.)
2. **Does L1 belong in Core (health model) or Host (supervisor)?** The `lastObservation` lives in the
   health sink; the staleness verdict should be where health is computed.
3. **Runtime bundle size/redaction:** how much log history, and what's the secret-redaction story for
   free-text logs (vs the structured config the engine already redacts)?
4. **In-flight-call detection without a watchdog:** L3 can *observe* an in-flight call exceeding a
   threshold for diagnostics without *acting* on it (the watchdog acts). Confirm L3 stays observational
   (P1) so it ships independently of the deferred watchdog.
5. **Reproduction appetite:** is the user willing to re-point 1–2 CNCs at EdgeConnect to capture the
   wedge live, or do we ship L1–L3 and wait for the next production occurrence to auto-capture?
6. **Alert delivery:** is in-Studio surfacing enough for v1, or do we need push (the gateway has a
   health-check port / watchdog) so a silent stop pages someone even when no one's looking at the Studio?

---

## 6. The one-line lesson

The incident's real finding isn't "FOCAS hangs" — it's **"a total data outage can be invisible to our
own product for 18 hours, on any protocol."** The confirmation that Modbus stalls the same way proves
it's a shared class: blocking device I/O that no cancellation token can interrupt, with no bounded
poll and no liveness check. L1 (liveness health) + L1.5 (supervisor poll watchdog) are the shared
fixes for *that*, and L1 is cheap because the timestamp already exists. Everything else makes the next
root cause findable from the product instead of from the source tree.
