# MELSEC Phase A-1 — manual pinning + golden-vector parity audit

**Date:** 2026-07-03
**Parent plan:** `2026-07-03-melsec-phase-a-qualification-plan-v2.md` (Gate A)
**Result: PASS — zero discrepancies. No runtime change required.**

---

## 1. Pinned manuals (A-1.1)

| Field | Manual 1 | Manual 2 |
|---|---|---|
| Title | MELSEC Communication Protocol Reference Manual | SLMP Reference Manual |
| Document number | **SH(NA)-080008** | **SH(NA)-080956ENG** |
| Revision | **AB** | **N** |
| Publication | **May 2022** (`SH(NA)-080008-AB(2205)KWIX`, verified on the PDF's revision page p496/back cover) | **October 2025** (`SH(NA)-080956ENG-N(2510)MEE`, verified p240/p244) |
| Source URL | `https://dl.mitsubishielectric.com/dl/fa/document/manual/plc/sh080008/sh080008ab.pdf` | `https://dl.mitsubishielectric.com/dl/fa/document/manual/plc/sh080956eng/sh080956engn.pdf` |
| Local evidence | `docs/vendor-manuals/sh080008ab.pdf` (7,018,994 bytes) | `docs/vendor-manuals/sh080956engn.pdf` (4,302,794 bytes) |
| SHA-256 | `b5b601f8d6b0eaa4e63d8ba7244301d009d671e406ea4ad62b1d06178d84ae02` | `3abef70db23577dded014af0f7fabd5d340284d590ff28dceb4442d8aea70ed4` |

Citation convention (used in tests): `[MC]` = SH(NA)-080008-AB, `[SLMP]` =
SH(NA)-080956ENG-N; **document + revision + section/table primary, page
secondary**. The PDFs are copyrighted Mitsubishi documents — kept locally under
`docs/vendor-manuals/` (gitignored), pinned here by URL + checksum.

## 2. Parity audit results (A-1.2)

Every wire element of the Slice-1 codec re-derived from the pinned manuals:

| Element | Our implementation | Manual says | Citation | Verdict |
|---|---|---|---|---|
| Subheader | req `50 00`, resp `D0 00`, fixed marker | identical, fixed value per frame type | [MC] §5.3 Subheader (p42) | ✅ |
| Data length fields | 2-byte LE | 2-byte, low byte first | [MC] §5.3 (p43) | ✅ |
| Monitoring timer | 2-byte LE, 250 ms units, 0 = infinite | identical; recommended 1–40 units host / 2–240 other station | [MC] §5.3 (p43) | ✅ |
| End code | 2-byte LE, 0 = normal | identical (example: C051H stored `51 C0`) | [MC] §5.3 (p44) | ✅ |
| 3E route order + defaults | `00 FF FF 03 00` (net, PC, dest I/O LE, station) | identical byte example for host-station access | [MC] §6.1 (p48) | ✅ |
| 0401 request layout | cmd LE, subcmd LE, head 3-byte LE, code 1 byte, count LE | identical; worked binary example `01 04 00 00 64 00 00 90 02 00` | [MC] §8.2 (p86–88); [SLMP] §5.2 (p49) | ✅ |
| Device codes (all 8) | D=A8 W=B4 R=AF ZR=B0 M=90 X=9C Y=9D B=A0 | identical (Q/L series column, subcmd 0000) | [MC] §8.1 Device code list (p68) | ✅ |
| Radix (all 8) | dec: D R M · hex: W ZR X Y B | identical (Notation column) — incl. **ZR = hexadecimal** | [MC] §8.1 (p68) | ✅ |
| Head device field | 3-byte LE (Q/L form) | 3 bytes LE for Q/L subcommands; (iQ-R native subcmd uses 4 bytes — not our form) | [MC] §8.1 Device number (p67) | ✅ |
| Word-units point cap | 960 | **1–960 points** for iQ-R / iQ-L / Q / L modules (read AND write) | [MC] §8.2 (p87, p93); [SLMP] §5.2 (p48) | ✅ |
| Bit devices via word read | 16 points/word | "Read 16-point bit device by specifying one point" | [MC] §8.2 (p87) | ✅ |
| Bit-device head alignment | planner uses head = min point (no 16-alignment) | 16-multiple head required **only for MELSEC-A series**; no constraint stated for Q/L/iQ-R | [MC] §8.2 (p87) | ✅ (still verify at Gate B) |

**Notable clarifications (not defects):**

1. **Device ranges are target-module-dependent.** [MC] §8.1 (p69): "The available
   device type and device range are in accordance with the device specifications of
   the access target module." The protocol does not fix universal ranges — so our
   design (accept any in-field address, let the PLC reject with an end code,
   surface it via diagnostics) is correct per manual. No client-side range table is
   required for the current profile.
2. **End-code descriptions are per-module.** [MC] §5.3 (p44) defers error-code
   content to the target module's user manual. Wire behavior (0 = success, else
   code) is confirmed; `MelsecEndCode.Describe` strings are advisory diagnostics
   text, not wire contract.
3. **iQ-R has a second, native command form** (subcommand 0002/0003: 2-byte device
   codes, 4-byte head numbers, extended devices such as LTN/LZ/RD). Our subcommand
   0000 form is the Q/L-compatible form, which [SLMP] documents identically and
   which iQ-R/iQ-L accept with the same 960-word cap.

## 3. Profile decision (A-1 step 5): **one Modern profile — no split**

iQ-R, iQ-L, Q, and L all accept the **same** wire form we implement (3E binary,
subcommand 0000, 1-byte codes, 3-byte heads) with the **same 960-word cap** and the
**same device codes/radix** — confirmed in both manuals independently. Therefore:

- **Keep a single "Modern (3E binary / subcmd 0000)" profile** for iQ-R/Q/L.
- Define a **future additive sub-profile "iQ-R native (subcommand 0002)"** only
  when device breadth (Phase A-3) needs extended devices (LTN, LSTN, LCN, LZ, RD)
  or >3-byte device numbers. It is additive — it does not change the current
  profile's behavior.

## 4. Simulator acceptance evidence (A-1 step 6)

- **Operator-path evidence (Studio against the standalone sim), 2026-07-02:**
  Test Connection (incl. one operator-error case: `http://` prefix in the host
  field correctly rejected as unreachable), Test Read of seeded tags, live values
  observed on D0/D1/D100 (walking) and D10/D20/W1A/ZR16384/M0 (static, as seeded),
  datatype/word-order decode verified (D10 Float32 = 250.5, D20 UInt32 = 1 234 567,
  ZR16384 hex addressing = 777, M0 Bool via bit0).
- **Wire round-trip evidence:** `MelsecSimulator/verify.py` passes all 8 checks
  against the sim (byte-identical request build + response decode paths).
- Test suites at this audit: Sources.Melsec 147/147 (see commit gate).

## 5. Compatibility table (A-1 step 7)

The hardware-deferred table in plan v2 §3 already carries the correct statuses for
the Modern profile row — confirmed unchanged by this audit:

**Implemented ✅ · Supported ✅ · Simulator-tested ✅ · Field-qualified: pending
hardware · Certified: pending broader hardware coverage.**

## 6. Scope + next

- No runtime behavior changed. Only: test-header citations
  (`SlmpFrameCodecTests.cs`), this audit doc, and a `.gitignore` entry for
  `docs/vendor-manuals/`.
- Gate A for the Modern profile is now **satisfied on the desk side**; the profile
  remains "Field-qualified: pending hardware" until a Gate-B capture.
- Next per plan v2: A-2 (iQ-F/FX5 profile from its own manual — new plan-trail) on
  approval.
