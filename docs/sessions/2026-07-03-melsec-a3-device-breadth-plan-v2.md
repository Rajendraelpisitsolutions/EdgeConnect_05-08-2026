# MELSEC A-3 — device breadth — plan v2

**Date:** 2026-07-03
**Status:** v2 (post-review; supersedes v1 — v1 deliberately not committed).
**Authorization:** **A-3a (SM/SD/SB/SW) implementation approved.** A-3b
(timers/counters) is described here but **remains plan-only**: its own
review/authorization comes after A-3a lands.
**Parent:** roadmap v2 §7 Phase C; ADR-0034 (profiles-as-data)
**Plan trail:** v1 → review (2026-07-03) → **v2**. All 12 review directives and
5 open-question answers incorporated.

---

## 0. Locked review decisions

1. **Split approved:** A-3a = SM/SD/SB/SW first; A-3b = timer/counter families as
   a separate decision-heavy slice. **Never bundled into one PR.**
2. **A-3.0 manual audit gate before code** — per profile, from the pinned manuals
   (SH(NA)-080008-AB, SH(NA)-082625ENG-J; already pinned with SHA-256 in the
   A-1/A-2D audits): device code, bit/word access, radix, valid range, supported
   profiles, batch-read access unit, simulator seed behavior, UI validation
   text. **No device is added from memory.** (Audit doc ships with the A-3a PR.)
3. **A-3a devices** (codes/ranges from the audit, added as **profile data** —
   never scattered conditionals): SM special relay (bit), SD special register
   (word), SB link special relay (bit), SW link special register (word).
4. **Profile behavior:** availability is profile-specific. A device absent from a
   profile rejects with typed `DEVICE_NOT_IMPLEMENTED`. Existing Modern/iQ-F
   D/W/R/ZR/M/X/Y/B behavior stays **byte-identical** (suite-pinned).
5. **Wire behavior:** no codec change unless a manual proves a wire-shape
   difference (audit shows none — same 0401/0000 path). SM/SB ride the existing
   bit-device word-unit read path; SD/SW the existing word-device path.
6. **A-3b rules approved in principle:** explicit mnemonics only (no bare
   `T100`/`C100`); contact/coil/current-value are distinct addresses; mnemonics
   (TS/TC/TN, CS/CC/CN, STS/STC/STN) acceptable **only as pinned from the
   manuals**; strict coherence (contact/coil ⇒ Bool only; current value ⇒
   numeric word types only); **long/extended families excluded** while they
   require iQ-R-native subcommand 0002 / extended fields / any non-current wire
   shape.
7. **A-3b stays plan-only** until its post-A-3a review.
8. **Curated SM/SD hints approved — minimal + manual-pinned.** Format:
   "SM/SD are special system devices. Use only documented addresses for your
   CPU. Common examples shown for convenience; verify against your PLC manual."
   Pinned example available: **SM400 (RUN monitor)** — used in
   SH(NA)-082625ENG-J's own communication examples. **This must not become
   browse or an in-UI device manual.**
9. **UI/helper copy:** update the profile-aware supported-device text; no
   universal-Mitsubishi claims; no new UI surface — if the change grows beyond
   helper text/table wording, a static mockup gates it first.
10. **Simulator:** deterministic SM/SD/SB/SW seeds in both profiles;
    `verify.py` tests modern AND fx5; rejection tests where a device is
    profile-unsupported; sim remains internal-consistency evidence only.
11. **Status language:** new devices may reach Implemented + Supported in UI +
    Simulator-tested; **Field-qualified stays pending hardware; Certified stays
    pending broader hardware coverage.**
12. **Scope guard (this slice):** no writes, UDP, 4E, 1E, ASCII, browse, demo
    mode, CSV import, QnA/ACpu, or iQ-R-native subcommand 0002.

## 1. A-3a facts (from the A-3.0 audit; full citations in the audit doc)

| Device | Kind | Code (subcmd 0000) | Radix | Modern range | FX5U/FX5UC range | Access unit |
|---|---|---|---|---|---|---|
| SM | Bit | 0x91 | decimal | target-module-dependent ([MC] §8.1) | 0–9999 ([COM]) | word units (16 pts/word) |
| SD | Word | 0xA9 | decimal | target-module-dependent | 0–11999 | word units |
| SB | Bit | 0xA1 | hex | target-module-dependent | 0–7FFF | word units (16 pts/word) |
| SW | Word | 0xB5 | hex | target-module-dependent | 0–7FFF | word units |

Source rows: [MC] SH(NA)-080008-AB §8.1 "Device code list" (p68) — "Special
relay SM Bit Decimal … 91H", "Special register SD Word Decimal … A9H", "Link
special relay SB Bit Hexadecimal … A1H", "Link special register SW Word
Hexadecimal … B5H". FX5 availability/ranges: [COM] SH(NA)-082625ENG-J SLMP
accessible-device list. **All four exist on BOTH profiles** → no cross-profile
rejection among these; ZR-on-iQ-F remains the profile-rejection example.

## 2. A-3a increments

| Inc | Content | Gate |
|---|---|---|
| 1 | A-3.0 audit doc + this plan v2 (docs) | citations complete |
| 2 | Registry/device additions (shared descriptors + both profile entries + supported-list updates), golden vectors, radix pins, wizard helper copy + minimal SM/SD hint, sim seeds + verify.py both profiles, test updates (incl. the deliberate flip of "SM rejected" pins to "SM supported") | Full suites (Melsec + Management unfiltered + Host) + Modern byte-identity + solution build |
| 3 | Docs/status: compatibility-table Devices column, FQP tag-suggestion touch-up | docs |

## 3. Test plan (headline)

- Golden request vectors for SM0/SD0/SB0/SW0 (codes 91/A9/A1/B5) on both profiles.
- Radix pins: SM/SD decimal; SB/SW hexadecimal (per profile).
- Byte-identity: existing 8 devices untouched (existing suite must stay green).
- Deliberate pin updates: previous "SM/SD/SB/SW rejected" assertions flip to
  supported; T/C/L/F/V/S/Z/DX/DY remain rejected with the UPDATED supported-list
  text.
- Wizard: new devices validate profile-aware; helper copy asserts.
- Sim: seeded reads both profiles; ZR-on-fx5 rejection still pinned.

## 4. Planned later vs intentionally excluded (clarified: scope guards are NOT permanent rejections — the product goal remains broad MELSEC support through the profile matrix)

**Planned later:**
- SM/SD/SB/SW — **A-3a (this slice)**
- Timers/counters — A-3b (post-A-3a review)
- 4E binary TCP — Phase D
- ASCII and 1E — Phase E, demand-driven
- Writes — Phase F, safety-gated
- UDP / serial — Phase G, separately scoped transport design
- CSV import / demo mode — optional later UI/product convenience

**Conditionally planned:**
- Browse — only if a real Mitsubishi label/import mechanism is scoped (not
  generic online browse)
- QnA / ACpu / legacy — only through their correct frame/transport profiles

**Not allowed:**
- Claiming Certified for all Mitsubishi PLCs without hardware evidence
- Mixing features into one PR without manual pinning, tests, simulator
  evidence, and profile-matrix updates

## 5. A-3b sketch (plan-only; re-reviewed after A-3a)

Mnemonic set to pin from [MC] §8.1: TS/TC/TN (0xC1/0xC0/0xC2), STS/STC/STN
(0xC7/0xC6/0xC8), CS/CC/CN (0xC4/0xC3/0xC5) — all decimal. Coherence: TS/TC/
STS/STC/CS/CC ⇒ Bool; TN/STN/CN ⇒ numeric word. Long families (LTS/LTC/LTN,
LSTS/LSTC/LSTN, LCS/LCC/LCN, LZ) excluded (subcommand-0002 territory). FX5
T/ST/C availability + ranges to be audited from [COM] before authorization.
