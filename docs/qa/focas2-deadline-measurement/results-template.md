# FOCAS2 deadline measurement — results template

Fill in everything you can. Leave a field blank (don't guess) if you couldn't get it. Send this back with
the packet captures, screenshots, and log file.

Tester: ____________________   Date(s) of test: ____________________

---

## Phase 0 — environment

| Item | Value |
|------|-------|
| FOCAS library (fwlib) DLL version | |
| Controller model / series | |
| Controller options (if known) | |
| EdgeConnect build / version | |
| Gateway id | |
| Source host / IP : port | ____________ : 8193 |
| `TimeoutSeconds` (from source config) | |
| `PollIntervalMs` (from source config) | |
| Data points / collector set selected | |
| Other FOCAS clients on this controller during the test (count + names) | |

---

## Phase 1 — healthy baseline

| Item | Value |
|------|-------|
| Capture file name | |
| Capture start time (wall clock) | |
| Capture stop time (wall clock) | |
| Total duration recorded | |
| What the machine was doing (idle / running / tool-change / alarm — and roughly when) | |
| Anything unusual observed | |

(We extract the per-read durations from `phase1-baseline.pcapng`.)

---

## Phase 2 — reconfigure / stop while reading

Repeat the block per action; add rows as needed.

| # | Action (apply-config / stop) | Wall-clock time | Time until Stopped / new config active | Clean or hung? | Screenshot file |
|---|------------------------------|-----------------|----------------------------------------|----------------|-----------------|
| 1 | | | | | |
| 2 | | | | | |
| 3 | | | | | |

Notes: ____________________________________________

---

## Phase 3a — black-hole network cut (the decisive one)

| Item | Value |
|------|-------|
| Capture file name | |
| Cut method used (unplug / NIC disable / firewall DROP) | |
| Time of cut (wall clock) | |
| Did EdgeConnect log a read error / source go Degraded/Failed? (yes/no) | |
| If yes — seconds from cut to first error | |
| If no — still hung at 5:00? (yes/no) | |
| Source status while cut (Running-frozen / Degraded / Failed) | |
| Screenshots taken (file names + approx times after cut) | |
| On network restore — did the source recover on its own? (yes/no) | |
| If yes — seconds to recover | |
| If no — what was required? (service restart / controller power-cycle / other) | |

---

## Phase 3b — clean-reset cut (optional)

| Item | Value |
|------|-------|
| Capture file name | |
| Cut method used (controller FOCAS service stop / firewall REJECT / switch reset) | |
| Time of cut (wall clock) | |
| Seconds from cut to EdgeConnect error | |
| Source status after cut | |
| Recovery behaviour on restore | |

---

## Free-form observations

Anything that surprised you, looked wrong, or seemed relevant — write it here. Over-sharing is welcome:

____________________________________________________________
____________________________________________________________
____________________________________________________________

---

## Checklist before sending back

- [ ] `phase1-baseline.pcapng`
- [ ] `phase2-reconfigure.pcapng`
- [ ] `phase3a-blackhole.pcapng`
- [ ] `phase3b-cleanreset.pcapng` (if done)
- [ ] this filled-in template
- [ ] Studio screenshots (named by phase/time)
- [ ] EdgeConnect log file covering the test window
