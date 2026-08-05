# FOCAS2 field-measurement procedure — the blocking deadline input for 3.1

**Date:** 2026-06-26
**Why this exists:** the slice-0 commit-3.1 deadline lock cannot close without the FOCAS2
*verified healthy max in-flight duration*. It is the one input that cannot be inferred from unit tests,
and FOCAS2 is the production-incident surface (8 sources frozen-but-Running 14–18 h). Output of this
procedure feeds `2026-06-26-slice-0-commit-3.1-proof-matrix-v3.md` §F.

## What the code audit already tells us (read first)

- `Focas2SourceConfiguration.TimeoutSeconds` (default **10 s**) is passed **only** to
  `AllocLibHandle` / `cnc_allclibhndl3` — i.e. it bounds **handle allocation (connect)**, not data reads.
- EdgeConnect **does not call `cnc_setdtimeout`**, so the per-data-call timeout is whatever fwlib defaults
  to. For many `cnc_*` data calls over a half-open TCP connection that can be **effectively unbounded** —
  which matches the incident (a data read wedged the dedicated thread for hours).
- Therefore the deadline input is almost certainly **not** "10 s." It is "the measured healthy max data-call
  duration + margin," and the wedge case (indefinite hang) is **expected** and must be handled as
  durable-pending + operator action — not as a timeout we can rely on.

So the field test has two jobs: (1) measure the healthy max in-flight duration per call group;
(2) determine empirically whether a network cut produces a *bounded error* or an *indefinite hang*.

## Record (minimum dataset — from the v2 review)

1. fwlib version (DLL file version) and controller model + relevant options (e.g. 0i-TF Plus / G342).
2. configured `TimeoutSeconds` (handle alloc) and whether `cnc_setdtimeout` is set (currently **no**).
3. poll interval (`PollIntervalMs`) and the active collector / data-point set.
4. measured **healthy max** duration per representative fwlib call group (baseline).
5. max duration during **restart/retirement while a normal call is in-flight** (responsive max).
6. controlled **network interruption** result: bounded error (record the bound) **or** indefinite hang.
7. chosen **margin** and why it is safe.
8. number of **concurrent FOCAS clients** against the controller during the run.

## Method

> Run on a **test/non-production controller** or in a **maintenance window**. The network-cut phase can
> wedge fwlib until the handle is torn down or the controller is power-cycled — do not do Phase 3 on a
> line that must keep producing.

### Phase 0 — environment capture
Record items 1–3 and 8 above. Note the EdgeConnect build/commit, the gateway id, and the source config
(host, port 8193, `TimeoutSeconds`, `PollIntervalMs`, data-point selection).

### Phase 1 — healthy baseline (per-call duration)
The FOCAS2 adapter has **no per-call timing today**. Capture it one of two ways:
- **Preferred (temporary diagnostic build):** wrap each fwlib call group in the dedicated-thread work
  item with `Stopwatch.GetTimestamp()` start/stop and log `{call, elapsedMs}` at Debug. (Group =
  statinfo/rdexecprg/rdparam/rddynamic/etc. as the collector issues them.) Do **not** ship this build —
  it is a measurement aid; the permanent equivalent belongs to the diagnostic-strengthening track.
- **Alternative:** if the diagnostic tap can already record per-poll/per-call durations, use it instead.

Run for a **representative window** (≥ several hours, spanning a real production cycle: idle, running,
tool-change, alarm). Capture **max** and p99 per call group. This `healthy_max` is the candidate input.

### Phase 2 — retirement-in-flight (responsive max)
While the source is polling normally, trigger a **reconfigure/stop** (the real retirement path) repeatedly
and record the max time for the **in-flight** call to complete after cleanup is initiated. This is the
responsive in-flight proof duration (should be ≤ `healthy_max`).

### Phase 3 — controlled network interruption (the decisive test)
With a data call in-flight, **cut the network** to the controller (unplug, or firewall-drop the TCP
connection — a clean RST vs. a black-hole drop behave differently, test **both**):
- **Black-hole drop (no RST):** the expected wedge — does the `cnc_*` call return an error within some
  bound, or hang indefinitely? Record the observed bound or "indefinite (> N minutes, aborted)."
- **Clean RST (cable to a switch that resets):** does fwlib surface an error promptly?

This determines whether any enforced bound exists. If black-hole → indefinite hang, **there is no
app-enforced data-call timeout**, confirming the incident mechanism.

### Phase 4 — derive the input
```
focas2_deadline_input = healthy_max (Phase 1, worst call group) + MARGIN
```
- If Phase 3 shows an enforced bound B < that, note it but still use `healthy_max + MARGIN` (the deadline
  is about distinguishing healthy from wedged, not about fwlib's own error bound).
- If Phase 3 shows indefinite hang, the deadline input stands as `healthy_max + MARGIN`; the wedge beyond
  it is expected and resolves via durable-pending + `QuiescenceTerminallyUnproven`/operator action — **not**
  via a fwlib timeout.
- **Margin rationale:** choose so that normal worst-case (cold cache, tool-change, alarm burst, max
  concurrent clients) sits comfortably below the deadline. State the reasoning explicitly.

## Results template (paste filled into v3 §F)

```
fwlib version:                         ____
controller model / options:            ____
TimeoutSeconds (handle alloc):         10 (default)   cnc_setdtimeout set? NO
poll interval / collector set:         ____ ms / ____
concurrent FOCAS clients:              ____
healthy max per call group (p99/max):  statinfo ___/___  rdexecprg ___/___  rdparam ___/___  ...
responsive in-flight max (retire):     ____ ms
network-cut black-hole result:         bounded @ ____  OR  indefinite hang (aborted @ ____)
network-cut clean-RST result:          error @ ____ ms
chosen focas2_deadline_input:          healthy_max ____ + margin ____ = ____ ms
margin rationale:                      ____
```

## Companion finding (route to diagnostic/reconfigure tracks, not 3.1)
EdgeConnect not calling `cnc_setdtimeout` means there is no app-level data-call bound. Evaluate setting a
per-handle data timeout as a **mitigation** (separate from the retirement deadline) so a wedged data call
self-aborts instead of hanging the dedicated thread indefinitely. This does not change the 3.1 proof model
(the thread-exit proof already handles a late return), but it would shrink the wedge window operationally.
