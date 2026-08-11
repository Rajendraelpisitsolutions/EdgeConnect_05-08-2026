# FOCAS2 deadline measurement — QA test package

**For:** QA / field test team
**Date:** 2026-06-26
**Owner:** EdgeConnect platform
**Time needed:** ~2–3 hours of controller time (mostly a passive recording window) + ~30 min for the
network-cut phase.

## What this is, in one paragraph

EdgeConnect reads data from FANUC CNCs over the FOCAS2 protocol. We are adding a safety feature that, when
a CNC source is being reconfigured or stopped, waits a bounded time for the controller's last in-flight
read to finish before swapping it out. To set that bound correctly we need two real-world numbers that we
**cannot** get from the office: (1) how long a *healthy* controller normally takes to answer EdgeConnect,
and (2) what happens to EdgeConnect when the network to the controller is cut **while a read is in
progress** — does the read fail within a few seconds, or does it hang indefinitely? This package is the
test that answers both.

> Why it matters: in a past production incident, 8 CNC sources sat "Running" but frozen for 14–18 hours.
> We believe a network read hung with no timeout. This test confirms that and measures the safe bound.

## What's in the package

| File | Purpose |
|------|---------|
| `README.md` | this overview |
| `test-plan.md` | the step-by-step test (3 phases) — follow this |
| `results-template.md` | the form to fill in and send back |

## What we need back from you

You do **not** need to compute any timings. Just capture the raw material and we extract the numbers:

1. **Packet captures** (`.pcapng`) — one per phase (Phase 1, 2, 3). This is the primary data.
2. **The filled-in `results-template.md`** — environment settings + what you observed (plain text).
3. **Studio screenshots** — the source's status / "last data point" indicator at the key moments
   (especially before and after the network cut).
4. **EdgeConnect log file** covering the test window.

Send those four back and we'll produce the deadline number. The packet captures are the most important
item — if anything else is missing we can usually still proceed, but **the pcaps are required**.

## Ground rules (read before starting)

- **Use a TEST controller or a maintenance window.** Phase 3 (network cut) can leave EdgeConnect's
  connection wedged until the service is restarted or the controller is power-cycled. **Do not run Phase 3
  on a machine that must keep producing.**
- Run against a **representative configuration** — same poll interval and data points you'd use in
  production, against a real controller doing real work (idle, running, tool-change, alarm if possible).
- If anything is unclear, capture more rather than less — an extra screenshot or a longer pcap never hurts.
