# MELSEC A-3b — timers / counters — plan v2

**Date:** 2026-07-03
**Status:** v2 (post-review; supersedes v1 — v1 deliberately not committed).
**Authorization:** implementation approved **subject to this v2 + the A-3b.0
audit** (both delivered now). A-3a-style two-commit shape (audit/doc gate first).
**Parent:** A-3 plan v2 §5; audit `2026-07-03-melsec-a3b0-timers-counters-audit.md`.
**Plan trail:** v1 → review (2026-07-03) → **v2**. All 12 review decisions +
5 open-question answers incorporated. **A-3b.0 audit is complete and its facts
are pinned below.**

---

## 0. Locked decisions (review)

1. **Mnemonic set (confirmed from manuals — see audit):** Timer **TS/TC/TN**,
   Retentive timer **STS/STC/STN**, Counter **CS/CC/CN**. No bare `T100`/`C100`/
   `ST100`.
2. **Bare-prefix UX:** reject with a suggestion, never auto-convert —
   *"T100 is ambiguous. Use TN100 for timer current value, TS100 for timer
   contact, or TC100 for timer coil."* (analogous for `C` → CN/CS/CC and `ST` →
   STN/STS/STC).
3. **Datatype rules:** TS/TC/STS/STC/CS/CC ⇒ **Bool only**; TN/STN/CN ⇒
   **Int16/UInt16 only** — the audit confirms current values are **1-word**;
   **no Int32/UInt32/Float32** (2-word current values are the excluded long family).
4. **Long/extended families excluded:** LTS/LTC/LTN, LSTS/LSTC/LSTN, LCS/LCC/LCN,
   LZ — 2-byte iQ-R-native codes / subcommand-0002 / 4-words-per-device. Rejected
   with a typed not-implemented error.
5. **Retentive timers (audit result):** [COM] shows **STS/STC/STN ARE available
   on FX5** → all nine devices exist on **both** profiles; no cross-profile
   rejection among them. (Had the FX5 manual lacked ST, it would have been
   Modern-only — decision was profile-specific-by-audit, and the audit says both.)
6. **Planner grouping:** contact/coil/current use different device codes →
   **separate blocks; no coalescing across sub-devices.** `TS100`, `TC100`,
   `TN100` produce three blocks. Tested.
7. **Parser: longest-prefix mandatory** — STS/STC/STN (3-char) resolved before
   TS/… (2-char) and S/T/C (1-char). Tested (see §3).
8. **A-3b.0 audit output** delivered (per-profile, per-sub-device, cited) — no
   device from memory.
9. **Simulator:** deterministic seeds (§audit seed plan) added *after* this audit
   approval; verify.py tests modern AND fx5.
10. **UI/helper copy:** concise mnemonic guidance (audit §UI copy); no big UI
    surface, no browse. ST* included because it is manual-pinned + supported.
11. **Status (if implemented):** Implemented + Supported in UI + Simulator-tested;
    **Field-qualified pending hardware; Certified pending broader coverage.**
12. **Scope guard:** no writes/UDP/4E/1E/ASCII/browse/demo/CSV/QnA/ACpu/
    subcommand-0002/long families/extended device forms.

## 1. Pinned facts (from A-3b.0 audit)

Nine sub-devices, all decimal, all on the 0401/0000 word-unit path, on **both**
profiles: TS 0xC1 (bit), TC 0xC0 (bit), TN 0xC2 (word), STS 0xC7 (bit), STC 0xC6
(bit), STN 0xC8 (word), CS 0xC4 (bit), CC 0xC3 (bit), CN 0xC5 (word). FX5 ranges:
T 0–511, C 0–255, ST parameter-dependent (ranges are target-module-dependent —
accept-and-let-PLC-reject, as for all devices).

## 2. Increments (each full-test gate + PR)

| Inc | Content | Gate |
|---|---|---|
| 1 | A-3b.0 audit + this plan v2 (docs) | citations complete ✓ (done) |
| 2 | Registry sub-device additions (both profiles), **parser longest-prefix fix**, datatype-coherence + bare-prefix-suggestion messages, golden vectors, sim seeds + verify.py both profiles, wizard helper copy, tests | Full suites (Melsec + Management unfiltered + Host) + A-3a/Modern byte-identity + solution build + sim self-test both profiles |
| 3 | Docs/status: compatibility Devices column, FQP tag suggestions | docs |

## 3. Test plan (mandatory)

**Parser correctness (open-question 7):**
- `STN10` → STN + 10; `STS10` → STS + 10; `STC10` → STC + 10.
- `S10` stays unsupported (step relay S is not implemented) and **does not steal**
  the `ST*`/`STN` prefixes.
- `TN100`/`TS5`/`CN0`/`CS3` resolve correctly (codes C2/C1/C5/C4).
- `T100`/`C100`/`ST100` reject with the **suggestion** message (not a generic error).
- Long families (`LTN0`, `LCN0`, `LSTN0`, `LZ0`) reject with typed
  DEVICE_NOT_IMPLEMENTED.

**Datatype coherence:** `TN100` as Bool → error; `TS5` as Int16 → error;
`CN0` as Float32 → error.

**Planner grouping:** `TS100`+`TC100`+`TN100` → three separate blocks.

**Golden vectors:** request bytes per sub-device code, both profiles.

**Byte-identity:** Slice-1 + A-3a devices untouched (existing suites stay green).

**Simulator:** seeded TN/TS/CN/CS/STN/STS reads; verify.py OK modern + fx5.

## 4. Scope + status (unchanged)

No writes/UDP/4E/1E/ASCII/browse/demo/CSV/QnA/ACpu/subcmd-0002/long families.
New sub-devices cap at Implemented + Supported + Simulator-tested;
Field-qualified pending hardware; Certified pending broader coverage; no
universal-Mitsubishi claims.

## 5. Residual open items for the reviewer (small)

1. Bare-prefix message wording — the §0.2 draft, or tweak?
2. Wizard helper: single sentence (audit §UI copy) vs a tiny 3-row mnemonic
   table? (Proposal: single sentence — no new UI surface.)
3. FX5 range display: rely on PLC rejection (proposed) vs pre-validate T≤511 /
   C≤255 in the wizard? (Proposal: PLC-rejection + diagnostics, consistent with
   the "ranges are target-module-dependent" principle.)
