# FOCAS2 `SOCKET_ERROR` ("Socket communication lost") — troubleshooting & fix guide

**Date:** 2026-06-29
**Status:** operational troubleshooting guide (local, uncommitted).
**Trigger example:** `ALERT — FOCAS2 source '1420309-source' STOPPED producing data: FOCAS2.SOCKET_ERROR (Socket communication lost.)`
**Related:** `2026-06-24-focas2-stall-incident.md`, `2026-06-29-data-stops-after-time-root-cause-and-plan-v1.md`.

---

## 0. TL;DR

`FOCAS2.SOCKET_ERROR` (fwlib `EW_SOCKET`) means the **TCP connection to the CNC dropped**. Unlike the
silent stall, this failure **surfaces** — the adapter detects it, marks the source `Degraded`, raises the
alert, **disconnects, backs off, and auto-reconnects**. A single `STOPPED` followed by a `RECOVERED` is a
benign blip. Repeated flapping or a source **stuck Degraded** is a real **environmental** problem (network
or CNC), not an EdgeConnect code bug. Fix it by checking connectivity, the controller, and the
controller's FOCAS connection limit — then tune reconnect/keepalive.

---

## 0a. The single most important takeaway

The alert firing is **good news** — it means EdgeConnect **detected** the drop instead of going silent.
`SOCKET_ERROR` is a **retryable network error**: the adapter disconnects, backs off, and
**auto-reconnects**, logging `RECOVERED` when the link returns.

So the practical next step is to check **one thing first**: did `1420309-source` log a `RECOVERED` (or
return to `Running`) shortly after the `STOPPED`?

- **Yes** → it was a transient blip; **no action needed**.
- **No / it keeps flapping** → it's an **environmental** issue (network, CNC power/state, or the
  controller's FOCAS connection limit). Work through §4, starting with `ping <cnc-ip>` and
  `Test-NetConnection <cnc-ip> -Port 8193`.

---

## 1. What the alert means and what the adapter already does

`EW_SOCKET` is mapped to a **retryable Network** error (`Focas2SourceAdapter.MapFatalError`). On it, the
collect path:

1. catches `Focas2FatalException` and calls `ConnectionManager.HandleFatalError()` → **Disconnect()**
   (frees the handle) **+ IncrementBackoff()**;
2. rethrows to `PollAsync`, which records the failure, **emits the `STOPPED producing data` alert** on the
   `Running → Degraded` edge, and returns an empty batch;
3. on the next poll, `EnsureConnected()` respects backoff and **reconnects**; a successful poll flips the
   source back to `Running` and logs `RECOVERED`.

**Implication:** recovery is automatic *if the socket can be re-established*. The adapter is behaving
correctly. The question is therefore **why the socket keeps dropping**, which is almost always outside
EdgeConnect.

---

## 2. First triage — transient blip vs real problem

| Observation | Verdict | Action |
|-------------|---------|--------|
| One `STOPPED` then a `RECOVERED` shortly after; source returns to `Running` | Benign transient | None — normal network hiccup |
| Repeated `STOPPED`/`RECOVERED` flapping | Real, intermittent | Investigate network + connection limit (§3) |
| Source **stuck Degraded**, no `RECOVERED`, reconnect keeps failing | Real, persistent | Full diagnosis (§4); likely CNC down / unreachable / addressing |

Check: the EdgeConnect log for that source's `RECOVERED` line and its live state in the Studio
(source health / route timeline).

---

## 3. Root causes (ranked)

1. **Network path** — bad/loose cable, failing switch port, intermittent link; a **managed
   switch/firewall dropping idle or long-lived TCP sessions**; VLAN/routing/STP change.
2. **CNC controller** — powered off or rebooted, FOCAS (Ethernet) service not running, Ethernet board
   fault, controller in a state that closes the session.
3. **Connection-limit exhaustion** — FANUC controllers permit only a small number of simultaneous FOCAS
   clients. This gateway's sources/collectors **plus** other clients (HMIs, MTConnect, other gateways) can
   exceed the limit, and the controller then drops connections. (Named hypothesis in the 2026-06-24
   incident.)
4. **Addressing** — wrong or changed CNC IP (DHCP lease change), IP conflict, or wrong port (must be
   **8193**).
5. **Gateway side** — NIC/driver problem, resource exhaustion, or an **orphaned handle from a prior wedge**
   (RC-3) still holding a connection slot.

---

## 4. Diagnostic steps for `1420309-source`

1. **Get the target.** Studio → source `1420309-source` → note the CNC **IP** and **port**.
2. **Reachability.**
   - `ping <cnc-ip>` — packet loss / latency?
   - `Test-NetConnection <cnc-ip> -Port 8193` (PowerShell) — does TCP connect succeed?
3. **Controller.** Confirm the CNC is powered on, on the network, and its FOCAS/Ethernet service is up;
   check the Ethernet board / connector.
4. **Connection limit.** Count concurrent FOCAS clients on this controller and compare against its
   documented limit. Temporarily stop other clients and see if the drop stops.
5. **Network gear.** Inspect the switch port (errors, flaps, negotiated speed/duplex); check for an
   **idle-session timeout** on any firewall/switch between gateway and CNC.
6. **Pattern.** From the EdgeConnect log, measure flap frequency and whether `RECOVERED` follows each
   `STOPPED`; correlate timestamps with shift changes / machine power cycles.
7. **If persistent.** Capture a packet trace on port 8193 (gateway side) during a drop — this is the same
   evidence the QA field-measurement package collects; attach it to the incident record.

---

## 5. Fixes / mitigations (EdgeConnect side)

- **Static IP** for the CNC; verify port **8193** in the source config.
- **Tune backoff** so reconnect cadence fits the environment, without hammering the controller:
  `initialBackoffMs`, `maxBackoffMs`, `backoffMultiplier`, `maxConnectRetries`.
- **Reduce load** if hitting the controller's connection limit — fewer concurrent clients, trim the
  collected data-point set, or stagger polls.
- **Keep `dataTimeoutSeconds` set** (the per-handle data-read bound added as the silent-stall mitigation).
  It does not stop socket loss, but it prevents a half-open read from wedging the worker, which avoids the
  orphaned-handle variant of cause #5.
- **OS/network TCP keepalive** so idle sessions are not silently reaped by intermediate devices.

---

## 6. Do NOT conflate with the silent stall

This `SOCKET_ERROR` path is **detected and self-recovering today** — the connection drop surfaces, the
source goes `Degraded`, the alert fires, and it reconnects. That is the *healthy* failure mode.

The **silent stall** (data stops with **no** error, lifecycle stays `Running`, health stays green) is a
**different, harder** problem and is **not** what this alert represents. It requires the host-level
progress watchdog and the slice-0 **commit 3.1** supervisor deadline (Sony's track, blocked on the QA
FOCAS field measurement). See `2026-06-29-data-stops-after-time-root-cause-and-plan-v1.md`.

---

## 7. Escalation

If the source still drops after network + controller + connection-limit checks, capture (a) a port-8193
packet trace across a drop, (b) the gateway log window, and (c) the controller's concurrent-client count
and fwlib/model info, and add them to the incident evidence so the environmental trigger can be confirmed
rather than assumed.
