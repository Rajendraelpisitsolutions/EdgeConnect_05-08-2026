# MELSEC Phase A — Desk + Simulator Qualification (Software Completion without Hardware) — plan v2

**Date:** 2026-07-03
**Status:** v2 (post-review; supersedes v1 — v1 deliberately not committed)
**Parent:** `2026-07-03-melsec-general-compatibility-roadmap-v2.md` §7 (approved direction)
**Review correction driving v2:** **no Mitsubishi PLC hardware is available now** —
no iQ-R, Q, L, FX5U, or any other Mitsubishi device. Nothing in this phase may
depend on immediate real-capture access; hardware testing becomes a later Field
Qualification step when a PLC becomes available. **FX5U/iQ-F hardware validation is
equally deferred** — v1's "run Phase B first because the FX5U is in hand" option is
withdrawn (the customer owns that device; we have no test access to it now).

---

## 0. Goal and framing

**Goal:** complete MELSEC driver support **as far as possible without hardware**,
using: official manuals (pinned revisions), golden-byte tests, simulator tests, and
the compatibility/profile matrix. Hardware evidence is a deferred, non-blocking
qualification step.

### Status language (mandatory)

No profile is marked **Field-qualified** or **Certified** without real PLC/capture
evidence — under any circumstances. Profiles completed in this track are marked:

- **Implemented** — code exists, passes unit/golden tests
- **Supported** — exposed to operators in the Studio with validation
- **Simulator-tested** — proven against the standalone simulator end-to-end
- **Field-qualified: pending hardware**
- **Certified: pending broader hardware coverage**

UI claim language stays **profile-supported**, never universally certified
("MELSEC / MC 3E binary TCP profile", "iQ-F/FX5 profile", …). No claim of
"Certified for all Mitsubishi PLCs" until real hardware evidence exists.

---

## 1. Two gates (replaces v1's single field-qualification exit)

### Gate A — Software-complete (the exit for this phase)

A profile passes Gate A when ALL of:

1. **Manuals pinned** — exact Mitsubishi document numbers + revisions recorded.
2. **Frame / device / range / cap audit complete** — every wire byte, device code,
   address range, and point cap re-derived from the pinned manual.
3. **Golden vectors annotated** — each golden test carries its manual
   section/page citation; discrepancies fixed (spec bugs, gated by full suites).
4. **Simulator tests pass** — loopback + standalone sim cover the profile's frames.
5. **UI/config validation matches the profile matrix** — the wizard/config gate
   accepts exactly what the profile supports and explains what it doesn't.
6. Status recorded as **Implemented + Supported + Simulator-tested**;
   Field-qualified/Certified marked **pending hardware**.

### Gate B — Hardware qualification (deferred, non-blocking)

Deferred until a PLC is available (any source: customer site window, lab unit,
partner). When one is:

1. Send the FQP; collect a real request/response capture + PLC-side truth values.
2. Verify word order, device codes, bit-device word-read alignment, end codes
   against the capture (byte-diff pass criteria from v1 §A4 carry over unchanged).
3. **Only then** flip that profile to **Field-qualified**. Certified requires
   broader coverage across representative devices.

Gate B never blocks Gate-A software work on any profile.

---

## 2. Work plan (no-hardware track)

### A-1. Current profile (Modern 3E binary/TCP) → Gate A

| Step | Work |
|---|---|
| A-1.1 | Pin SH(NA)-080008 (MC Protocol ref) + SH(NA)-080956 (SLMP ref): exact revisions |
| A-1.2 | Parity audit: re-derive `SlmpFrameCodecTests.cs` golden vectors from the manual; annotate with citations |
| A-1.3 | Confirm 960-word cap + D/W/R/ZR/M/X/Y/B ranges for iQ-R/Q/L; decide one-profile-vs-subprofiles (split if manuals show differences) |
| A-1.4 | Re-run + record the simulator acceptance evidence (§4) |
| A-1.5 | Update compatibility table (§3): row = Implemented ✅ Supported ✅ Sim-tested ✅ Field-qualified ⏳ pending hardware |

### A-2. iQ-F / FX5 profile — manual-driven preparation (software only)

Implementation **from the official iQ-F SLMP manual** (device availability/ranges,
X/Y wire notation per frame/encoding, built-in-Ethernet settings, max points, R/ZR
semantics, bit alignment, word order — the roadmap §7 Phase-B checklist), plus:
profile registry entry, golden vectors from the iQ-F manual, simulator profile
variant, wizard profile selector (with the roadmap §8 migration guarantee — existing
`melsec` configs hydrate as the default Modern profile, never broken). Hardware
validation deferred to Gate B like everything else.

### A-3. Device breadth from manuals (roadmap Phase C pulled into the software track)

`SM`, `SD`, `SB`, `SW`, timers (`T`), counters (`C`) + per-family extended devices —
each with manual-derived codes/ranges, golden vectors, sim support, and UI
validation. Sequenced after A-2 (registry exists by then).

### A-4. Later frames, by priority (software-first, each gated the same way)

4E binary/TCP → ASCII → 1E; **UDP/serial only if separately scoped** (own
reliability/transport designs per roadmap §3). Each lands as: manual pin → codec +
golden vectors → sim → UI validation → Gate A; Gate B deferred.

**Sequencing:** A-1 first (it's the shipped profile — close its software gate), then
A-2, then A-3; A-4 by demand/priority afterwards. Each item is its own plan-trail +
PR chain; this plan authorizes A-1 immediately and sets direction for A-2/A-3.

---

## 3. Hardware-deferred compatibility table (living)

| Profile | Frame/enc/transport | Devices | Implemented | Supported in UI | Sim-tested | Field-qualified | Certified | Hardware evidence status |
|---|---|---|---|---|---|---|---|---|
| Modern (iQ-R/Q/L pin) | 3E/bin/TCP | D W R ZR M X Y B + word-bit | ✅ | ✅ | ✅ (loopback + standalone sim) | **Pending hardware** | **Pending broader coverage** | None — no PLC access; FQP ready |
| iQ-F / FX5 | 3E/bin/TCP | per iQ-F manual (A-2) | planned | planned | planned | Pending hardware | Pending | None — customer FX5U known to exist; no test access |
| iQ-R/Q/L subprofiles (if split) | 3E/bin/TCP | per manuals (A-1.3) | TBD | TBD | TBD | Pending hardware | Pending | None |
| Device breadth (SM/SD/SB/SW/T/C) | 3E/bin/TCP | A-3 | planned | planned | planned | Pending hardware | Pending | None |
| 4E / ASCII / 1E | per §A-4 | — | not started | — | — | — | — | — |

---

## 4. Acceptance for the no-hardware phase (Gate A evidence pack)

1. **Full relevant test suites pass** — Sources.Melsec.Tests, Management.Tests
   (full, unfiltered), Host.Tests, full-solution build.
2. **Simulator proves the operator path**: Studio **Test Connection**, **Test
   Read (selected)**, **Read-all-valid** (planned blocks + skipped invalid tags),
   and the SourceDetail **diagnostics** panel — against the standalone sim,
   recorded (screenshots or a session note).
3. **Documentation states hardware qualification is pending** — roadmap status
   table + module-catalog wording stay honest.
4. **Compatibility matrix updated** with "Field-qualified: pending hardware".

---

## 5. Field Qualification Package (FQP) — ready, not blocking

The FQP (`docs/sessions/2026-06-30-melsec-discovery-package.md`; PDF export
optional) **stays ready to send the moment any hardware becomes available** —
customer site window, lab purchase, or partner device. Its purpose is **later field
verification**; it gates nothing in this phase. (Optional low-cost action: a small
lab FX5U or Q-series CPU would unlock Gate B for a profile at any time.)

---

## 6. Scope discipline

- **No writes** unless separately approved (roadmap Phase F, safety-gated).
- **No browse** unless a real Mitsubishi label/import mechanism is separately scoped.
- No UDP/serial without their own scoped designs.
- **No "Certified for all Mitsubishi PLCs" claim** until real hardware evidence
  exists; UI text stays profile-aware.
- Each work item ships via the normal plan-trail + full-test gate + PR flow.

---

## 7. Record-keeping

- **ADR-0034 (profile-matrix strategy)** — author alongside A-2 (when the profile
  registry becomes code), citing this no-hardware framing; ADR-0033 remains the
  Slice-1 record.
- Compatibility table updates land with each Gate-A completion.
- Session handoffs per milestone; memory updated at each gate.

---

## Immediate next actions (on approval of this v2)

1. **A-1.1** — acquire/pin SH(NA)-080008 + SH(NA)-080956 revisions (user to confirm
   whether official PDFs are already on hand, else download from Mitsubishi FA).
2. **A-1.2/A-1.3** — parity audit + caps/ranges + split decision (desk).
3. **A-1.4/A-1.5** — sim acceptance evidence + table update.
4. Then A-2 planning (iQ-F profile, its own plan-trail).
