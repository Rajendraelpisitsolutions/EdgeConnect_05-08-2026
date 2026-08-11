# FOCAS2 deadline measurement — QA test plan

Follow the phases in order. Phase 0 is setup, Phases 1–3 are the measurements. Capture a **separate
packet capture per phase** and name the files clearly (e.g. `phase1-baseline.pcapng`).

---

## Equipment & prerequisites

- A FANUC CNC reachable over the network (FOCAS2 on **TCP port 8193**). **Test controller or maintenance
  window** — see ground rules in the README.
- A Windows host running **EdgeConnect** (the service / app) configured with one FOCAS2 source pointing at
  that controller.
- **Wireshark** installed on the EdgeConnect host (or on a machine that can see the EdgeConnect↔CNC traffic
  via a SPAN/mirror port). Free download: <https://www.wireshark.org/>.
- Access to the **EdgeConnect Studio** in a browser (default `http://127.0.0.1:5080`) to watch source
  status and trigger reconfigure/stop.
- Access to the **EdgeConnect log file** location.
- A way to **cut the network** to the controller for Phase 3 — e.g. unplug the controller's network cable,
  disable the host NIC, or add a temporary firewall block. Have **two** cut methods if possible (see
  Phase 3).

---

## How to run a packet capture (do this each phase)

1. Open Wireshark, pick the network interface that carries traffic to the CNC.
2. In the capture filter box, enter: `tcp port 8193`
3. Start capture **before** the phase action, stop it **after**. Save as `.pcapng`.
4. Name it for the phase. One file per phase.

That's all the timing data we need — we extract the per-read durations from the capture. You don't compute
anything.

---

## Phase 0 — environment capture (5 min)

Fill these into `results-template.md` (some require asking the controller owner / checking the EdgeConnect
config):

- FOCAS library (fwlib) version on the EdgeConnect host (DLL file version of the FOCAS DLL, e.g.
  `Fwlib32.dll` → right-click → Properties → Details).
- Controller model and key options (e.g. `0i-TF Plus`, control series/options).
- EdgeConnect FOCAS2 source settings: host/IP, port (8193), **TimeoutSeconds**, **PollIntervalMs**, and the
  selected data points / collector set.
- How many other applications / clients are talking FOCAS to this controller at the same time (including
  any "alternate application" used as a backup).
- EdgeConnect build/version and the gateway id.

---

## Phase 1 — healthy baseline recording (≥ 1–2 hours passive)

**Goal:** record normal request/response timing while the controller runs real work.

1. Start a packet capture → `phase1-baseline.pcapng`.
2. Confirm in the Studio that the FOCAS2 source is **Running** and data is flowing (the "last data point"
   indicator updates).
3. Let it run for a **representative window** — ideally spanning idle, a running program, a tool change,
   and an alarm if one occurs naturally. Longer is better; **≥ 1 hour**, 2+ preferred.
4. Note in the template the wall-clock start/stop time of the capture and roughly what the machine was
   doing (so we can line up the worst-case moments).
5. Stop the capture and save.

*(No manual timing needed — we read the durations from the pcap.)*

---

## Phase 2 — reconfigure / stop while reading (15 min)

**Goal:** see how the source behaves when retired while a read is in flight (the real reconfigure path).

1. Start a packet capture → `phase2-reconfigure.pcapng`.
2. With the source **Running** and polling, perform each of these in the Studio and **note the wall-clock
   time** of each action:
   - **a.** Edit the source (e.g. change the timeout value) and **Apply** the config — the same action that
     triggered the incident.
   - **b.** **Stop** / disable the source.
   - Repeat a few times.
3. For each action, record in the template:
   - how long until the source visibly reaches **Stopped** / the new config is **active** (use the Studio;
     a stopwatch or the screen clock is fine);
   - whether it completed cleanly or appeared to hang.
4. Take a Studio screenshot of the source status right after each action.
5. Stop the capture and save.

---

## Phase 3 — network interruption (THE decisive test, 20–30 min)

**Goal:** determine whether a read that is in progress when the network drops **fails within a bound** or
**hangs indefinitely**. ⚠ This can wedge the connection — test controller / maintenance window only.

Do it twice, with two different cut styles if you can:

### 3a — "black-hole" cut (no clean close)
1. Start a packet capture → `phase3a-blackhole.pcapng`.
2. Confirm the source is Running and data is flowing. **Screenshot** the Studio "last data point"
   indicator.
3. **Cut the network** by a method that does NOT send a clean TCP reset — e.g. **unplug the controller's
   network cable**, or **disable the EdgeConnect host's NIC**, or a firewall **DROP** (not reject).
4. Start a stopwatch at the moment of the cut. Watch the Studio:
   - Does the source recover on its own when... no — leave the network cut and **observe how long the
     source stays frozen**. Record: does EdgeConnect log a read error / the source go Degraded/Failed within
     some number of seconds, or does it stay "Running" but frozen with no error?
   - Record the time to first error (if any). If it has not errored after **5 minutes**, record "still
     hung at 5:00" and stop waiting.
5. **Screenshot** the Studio after ~30 s, ~2 min, ~5 min (or until it errors).
6. **Restore the network** (replug / re-enable NIC / remove firewall rule). Record whether the source
   recovers on its own, and how long that takes — or whether you had to **restart the EdgeConnect service**
   / power-cycle the controller to recover (record which).
7. Stop the capture and save.

### 3b — "clean reset" cut (optional but valuable)
Repeat 3a but cut in a way that sends a clean TCP reset — e.g. stop the controller's FOCAS service, or a
firewall **REJECT** (sends RST), or disconnect at a switch that resets the link. Capture →
`phase3b-cleanreset.pcapng`. Record whether EdgeConnect surfaces an error **promptly** (and how fast)
versus the black-hole case.

---

## After the test — package up and send

Collect and send back (see README "What we need back"):

1. `phase1-baseline.pcapng`, `phase2-reconfigure.pcapng`, `phase3a-blackhole.pcapng`, and
   `phase3b-cleanreset.pcapng` (if done).
2. The filled-in `results-template.md`.
3. The Studio screenshots (name them by phase/time).
4. The EdgeConnect log file covering the whole test window.

Zip the folder and hand it back. We'll extract the timing numbers and report the deadline value.
