# MELSEC A-2O — profile selector + iQ-F operator support — plan v2

**Date:** 2026-07-03
**Status:** v2 (post-review; supersedes v1 — v1 deliberately not committed)
**Parent:** A-2 plan v2 Gate A-2O (approved), roadmap v2 §6, ADR-0034
**Prereqs on master:** profile registry + iQ-F entry (PR #171), sim FX5 mode (PR #172)
**Plan trail:** v1 → review (2026-07-03) → **v2**. All 12 review directives and 5
open-question answers incorporated. **No Razor/code until the O-1 mockup is
signed off.**

---

## 0. Goal and status outcome

Operator selects the **PLC family profile** in the MELSEC wizard — **Modern
(iQ-R/Q/L)** or **iQ-F / FX5** — making iQ-F genuinely **Supported**. License
module unchanged (`source-melsec`).

**After O-4:** iQ-F/FX5 = Implemented ✅ · Simulator-tested ✅ · **Supported in
UI ✅** · Field-qualified: **pending hardware** · Certified: **pending broader
hardware coverage**. Never "supports all Mitsubishi PLCs".

## 1. Locked review decisions

1. **Selector = two radio tiles/cards** in the PLC-connection section (not a
   dropdown) — two profiles today, operators need explanatory helper text.
   Revisit the control only if more profiles arrive.
2. **Selector copy (approved; tune after mockup):**
   - *Modern iQ-R/Q/L:* "For iQ-R, Q, and L series using MC 3E binary over TCP.
     X/Y/W/B/ZR addresses use hexadecimal notation."
   - *iQ-F / FX5:* "For FX5 CPU built-in Ethernet using MC 3E binary over TCP.
     Enter X/Y addresses as FX5 / GX Works3 notation, for example X10. X/Y digits
     must be octal-style 0–7; EdgeConnect converts to the binary head device
     number. ZR is not available on the FX5 CPU profile."
   - Fixed protocol summary stays: **"MC 3E binary over TCP, read-only."**
     No frame/transport dropdowns.
3. **Adapter acceptance rule:** accept a profile only when it (a) resolves in
   `MelsecProfiles`, (b) `IsOperatorSelectable == true`, (c) supports the
   configured frame/encoding/transport, and (d) the source remains MC 3E
   binary / TCP / read-only. Unknown / non-selectable / unsupported combinations
   → **typed config errors** (extending the existing
   `CONFIG_PROFILE_NOT_IMPLEMENTED` / `CONFIG_MODE_NOT_IMPLEMENTED` pattern),
   never generic exceptions.
4. **Probe compatibility:** absent profile field on probe requests **and** older
   saved configs = **Modern**. The UI always sends the selected profile on new
   requests.
5. **Migration guarantee (explicit, regression-tested):** existing `melsec`
   configs with no profile field hydrate as Modern, are **never re-prompted**,
   and **gateway.json is never silently rewritten in the background**. Writing
   the explicit Modern profile on an operator's explicit Save/Edit is acceptable
   (normal save behavior).
6. **Profile flows through every planning path:** config projection → adapter
   validation → address parser → scan planner → probe test-read → wizard
   validation → diagnostics display. Modern stays byte- and behavior-identical.
7. **iQ-F behavior tests mandatory** (see §3 matrix).
8. **Modern regression tests mandatory** (see §3 matrix).
9. **Diagnostics header:** profile display name + frame/encoding/transport
   summary + read-only label. Field-status text, if shown at all, is one honest
   line — *"Hardware field qualification pending."* Never "Certified".
10. **O-4 docs:** FQP appendix gains one line about selecting the PLC family
    profile in Studio; compatibility row flips Supported → yes (other statuses
    unchanged).
11. **PR shape:** focused commits; separate green master-safe PRs allowed. If
    O-2 lands before O-3, `IqF.IsOperatorSelectable` stays **false**. The flip
    to **true** is the FINAL operator-support commit, after wizard + probe +
    diagnostics are ready.
12. **Scope guard:** no writes, UDP, 4E, 1E, ASCII, browse, demo mode, CSV
    import, QnA/ACpu, or SM/SD/SB/SW/T/C device breadth.

## 2. Increments

| Inc | Content | Gate |
|---|---|---|
| **O-1** | **Static HTML mockup** (deliverable alongside this plan): (a) Modern selected, (b) iQ-F selected, (c) iQ-F helper text, (d) ZR row invalid on iQ-F, (e) X10 valid on iQ-F, (f) profile in diagnostics header | **Operator sign-off — blocks all Razor/code** |
| **O-2** | Backend: adapter acceptance rule (§1.3) + profile-aware parse in adapter/planner path; wizard-model profile field + profile-aware `ValidateTag`; probe request profile field (absent = Modern). `IsOperatorSelectable` stays false | Full suites + Modern byte-identity + migration regressions green |
| **O-3** | Wizard Razor radio tiles + helper copy + diagnostics header per signed-off mockup; Edit-mode round-trip; **final commit flips `IqF.IsOperatorSelectable = true`** | bUnit + full Management.Tests (unfiltered) + sim `--profile fx5` probe run |
| **O-4** | Docs/status: roadmap §5 row → Supported ✅; FQP one-liner; CLAUDE.md §8 note; claim-language check | docs PR |

Every increment: verified branch → focused commit → full-test gate (Melsec +
Management full + Host + solution build) → PR.

## 3. Test matrix (mandatory)

**iQ-F behavior (once the flip lands):**
- iQ-F config initializes (adapter Running against sim `--profile fx5`).
- `X10` parses per operator notation → head 8; `X18` rejected (octal rule).
- `ZR…` rejected with `DEVICE_NOT_IMPLEMENTED` (or equivalent typed config error)
  at wizard AND adapter.
- D/W/R/M/X/Y/B all accepted; max points cap remains 960.
- Probe test-read vs sim `--profile fx5`: valid tags succeed; ZR skipped/rejected
  cleanly (never sent where the planner can exclude it).
- Diagnostics header shows the selected profile.

**Modern regression:**
- Profile-less config hydrates Modern; explicit-Modern config hydrates Modern.
- Modern X/Y/W/B/ZR behavior exactly as shipped (parse + errors + bytes).
- Modern request bytes byte-identical (existing suite stays green).
- Existing Modern sources unaffected end-to-end (adapter accepts + behaves
  identically; no background gateway.json rewrite).

## 4. Out of scope (unchanged)

Writes, UDP, 4E, 1E, ASCII, browse, demo mode, CSV import, QnA/ACpu profiles,
SM/SD/SB/SW/T/C devices, hardware dependency of any kind. Field-qualification
remains Gate A-2H / FQP.
