# MTConnect source wizard (M.2b.4) — completion handoff

**Date:** 2026-06-01
**Status:** **DONE — MTConnect is operator-available.** The add-source wizard tile
is **Available**; an operator can add an MTConnect source end-to-end.

## What shipped

FOCAS2-style **semantic onboarding** wizard (NOT an OPC-UA-style dataItem picker):
the operator connects an agent and sees exactly which of the adapter's *fixed*
canonical CNC tags it exposes, plus its discovered axes. Built mockup-first
(signed off) on the plan-trail (v1 → review → reality-check → v2).

| Sub | What | Commit |
|-----|------|--------|
| M1 | Static HTML mockup (signed off, 5 revisions) | `fca3990`, `9954b9c` |
| M2a | `MTConnectSemanticMap` (single source of truth for both /current + /probe forms) + stream-parser refactor | `d51cea2` |
| M2b | `MTConnectProbeParser` — bounded /probe parse (availability + axis discovery, cap 12) | `d51cea2` |
| M2c | `MTConnectBrowseService` + `POST /api/v1/sources/browse/mtconnect` (8 statuses, fixed 10 s budget, license gate) | `ac30bb1` |
| M3a | `MTConnectSourceWizardModel` POCO + round-trip tests | `9384c1b` |
| M3b/c | `AddMTConnectSource.razor` + edit support (SourceEditRouter) | `86cbfdd` |
| M4 | Flip tile to Available + picker tests | `39d66be` |
| M5 | CLAUDE.md §8 update + this handoff + live verification | (this) |

## Live end-to-end verification (M5)

Drove the **real** browse wire against the public **demo.mtconnect.org** agent
(a self-hosted Management instance on an alt port):

```
status           : ReachableWithRecognisedTags
device           : OKUMA (mfr OKUMA)   availableDevices: [OKUMA, Mazak]  ← multi-device
axes             : X, Y, Z1, Z4        ← real dynamic axis names (not static X/Y/Z)
tags available   : 11/12  (production/cycle_time correctly greyed — no process-timer)
elapsedMs        : 1509  (well within the 10 s budget)
```

This exercises browse service → live /probe fetch → bounded parser → semantic
availability + axis discovery + multi-device → DTO, all green.

**Full runtime data-flow** (added after review — proves data actually flows, not just
discovery): ran the gateway with a hand-built config — MTConnect source `OKUMA-MTC`
(`https://demo.mtconnect.org`, device `OKUMA`, 2 s poll) → route → MQTT sink. The
source went **Running** and `pointsObserved` climbed continuously (437 → 760 → 798 →
836 over successive polls), `lastPointAtUtc` advancing in real time, **zero errors**.
The ~19 points/poll matches the OKUMA device's tag count (≈11 standard + 8 axis-position
+ alarms). So the `MTConnectSourceAdapter` genuinely polls `/current`, parses, and emits
canonical points from a real agent through the live pipeline.

**Not exercised:** the wizard UI click-through in a real browser, and delivery to a
live MQTT broker (none was running — but the source-side production is the
MTConnect-specific proof; route→sink delivery is generic, separately-tested pipeline).

## Tests

- MTConnect adapter: **53** (42 stream-parser, behaviour-preserved through the M2a
  refactor + 11 probe-parser fixtures incl. drift guard).
- Management: **876** (browse service/status-mapping 20, wizard model 5, edit-router
  + picker updates).
- Solution builds 0 errors.

## Key design decisions (locked in plan v2 + ChatGPT review)

- **Fixed semantic map, not arbitrary selection** — the operator never picks/renames
  dataItems. `MTConnectSemanticMap` is the single source of truth so the wizard's
  availability check can never disagree with what the runtime stream parser emits.
- **Enhanced /probe parse** — discover real axis names + grey out unavailable tags
  with a reason ("explain, don't hide").
- **Unavailable tags are shown but NOT activated** (no empty tags created).
- **Save-gating** — `ReachableNoRecognisedTags` blocks Save unless explicit
  "Save anyway".
- **Fixed 10 s probe budget**, independent of the runtime `TimeoutSeconds`.
- Conditions auto-aggregate to `alarms/count` + `alarms/first_fault` (informational,
  not selectable).

## Not done / follow-ups

- **M6 — MTConnect demo mode** (deferred, not a blocker): a `MTConnectDemoMode`
  (static /probe + generated /current) so sales/dev can exercise the wizard with no
  hardware, mirroring `Focas2DemoMode`. Tracked in plan v2 §5.
- **Siemens S7 wizard** (M.2b.2) — still Pending; same playbook (S7 needs a manual
  tag-address editor since it can't self-describe).
- The onboarding meta-wizard (`OnboardingFlow.razor`) does not embed MTConnect (same
  as OPC UA Client — both browse-based wizards use their standalone page). Could be
  embedded later if desired.

## Reference docs

- Plan trail: `docs/sessions/2026-05-31-mtconnect-source-wizard-plan-v1.md`,
  `…-premise-correction.md`, `…-plan-v2.md`.
- Mockup: `docs/sessions/2026-05-30-ux-mockups/7-mtconnect-source-wizard.html`.
