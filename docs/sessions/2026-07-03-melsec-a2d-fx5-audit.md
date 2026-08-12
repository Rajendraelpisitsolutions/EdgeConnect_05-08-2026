# MELSEC A-2 Gate A-2D — FX5/iQ-F manual discovery, bundle pin + fact audit

**Date:** 2026-07-03
**Parent plan:** `2026-07-03-melsec-a2-iqf-profile-plan-v2.md` (Increment 1)
**Result: Gate A-2D complete. FX5 3E-binary wire shape is IDENTICAL to Modern —
the iQ-F profile is PURE PROFILE DATA (no codec change). Two data deltas: X/Y
operator-address radix = octal, and ZR excluded from the FX5 device set.**
No code, no runtime change, no registry yet (Increment 2 pending approval).

---

## 1. A-2.0 Manual discovery — authoritative-document decisions

| Concern | Authoritative document | Rationale |
|---|---|---|
| SLMP frame/function behavior (FX5 CPU built-in Ethernet) | **SH(NA)-082625ENG-J** (Communication) | Current combined FX5-CPU manual (Apr 2026); contains the SLMP chapter, the full 3E/1E MESSAGE FORMAT chapters (§37) and 3E FRAME COMMANDS (§38) |
| MC 3E batch-read command / device tables / caps | **SH(NA)-082625ENG-J** §37.1, §38.1–38.2 | Same manual carries the command list, caps, and device-range tables for the CPU |
| FX5 built-in Ethernet / SLMP connection (open) settings | **SH(NA)-082625ENG-J** (Ethernet/SLMP settings sections; GX Works3 "SLMP Connection" / "Set Processing Counts") | Combined manual absorbed the split Ethernet manual's role for the CPU |
| Cross-checks | JY997D60801-G (MC Protocol, split, Apr 2022), JY997D56201-U (Ethernet Communication, split, Apr 2023), SH(NA)-080956ENG-N (generic SLMP, already pinned) | Split manuals predate the combined manual; retained as cross-checks |
| FX5 expansion-module SLMP (NOT the CPU built-in port) | JY997D56001-K | **Proven by the PDF's own cover page**, which scopes the manual to "Ethernet module –FX5-ENET –FX5-ENET/IP, CC-Link IE TSN master/local –FX5-CCLGN-MS, CC-Link IE Field –FX5-CCLIEF, Motion –FX5-40SSC-G/–FX5-80SSC-G". Not authoritative for the customer's FX5U-32MT built-in port — relevant only if an FX5-ENET module is ever used |

**Discovery outcome:** **SH-082625ENG-J is authoritative for FX5 CPU built-in
Ethernet / Communication behavior. JY997D56001, JY997D60801, and JY997D56201 are
older split-manual cross-checks or secondary sources.** For JY997D56001-K
specifically, its own cover page proves it is scoped to the FX5 expansion modules
(FX5-ENET / FX5-ENET/IP etc.), not the CPU's built-in port — validating the
review's "don't assume one SLMP manual suffices".

## 2. A-2.1 Pinned bundle

All from the official `dl.mitsubishielectric.com/dl/fa/document/manual/plcf/…`
host; revisions verified from each PDF's own revision page; local copies under
gitignored `docs/vendor-manuals/`.

| Doc | Rev | Date (rev page) | File (local) | SHA-256 |
|---|---|---|---|---|
| **SH(NA)-082625ENG** — FX5 User's Manual (Communication), FX5 CPU module, 960 pp | **J** | **April 2026** | `sh082625engj.pdf` (…/sh082625eng/sh082625engj.pdf) | `a3b6781f99c890c84d116edb9753a8f54a7f93f5e163ce866fd687deb2e6185a` |
| JY997D56001 — FX5 User's Manual (SLMP), FX5-ENET module scope | K | April 2022 | `jy997d56001k.pdf` | `f4c9ed4c2ae709ede18be8a9abce7e17f09c4daa801abeed516b681509ffb9bb` |
| JY997D60801 — FX5 User's Manual (MELSEC Communication Protocol) | G | April 2022 | `jy997d60801g.pdf` | `a2210a085ce325c080ccc01eb5e23c0e323c8d4e4c7045b927e96db6678fb468` |
| JY997D56201 — FX5 User's Manual (Ethernet Communication) | U | April 2023 | `jy997d56201u.pdf` | `ff451f8a12f243d7b94473b13117219b0681543a701a2a96665f1b08f2f6d30f` |
| SH(NA)-080956ENG — SLMP Reference | N | October 2025 | (pinned in A-1) | `3abef70db23577dded014af0f7fabd5d340284d590ff28dceb4442d8aea70ed4` |

Citation key below: `[COM]` = SH(NA)-082625ENG-J. Page numbers are the PDF's
printed pages (secondary to section numbers).

## 3. A-2.2 FX5/iQ-F fact table (FX5U/FX5UC CPU, built-in Ethernet, 3E binary/TCP)

| Fact | Finding | Citation |
|---|---|---|
| **Frame form** | 3E frame identical to Modern: subheader `5000`/`D000`, same field order; device specification offers the same "2-digit code / 6-digit number" form we use (**1-byte device code + 3-byte LE head**) plus the 4-digit/8-digit alternative | [COM] §37.1 (p592–596) |
| **Head-number encoding (binary)** | 3-byte LE; decimal devices converted to hex value; hex devices as-is — worked examples M1234→`D2 04 00`, B1234→`34 12 00` (identical convention to Modern) | [COM] §37.1 "Start device No." (p595) |
| **X/Y radix — THE FX5 DELTA** | Operator numbering is **OCTAL** (X/Y "0 to 1777", "Octal notation is used" — FX5U/FX5UC; FX5S 0–377). On the wire: "ASCII code (X, Y OCT): Octal · ASCII code (X, Y HEX), **Binary code: Hexadecimal**" — i.e. the **binary head field carries the numeric value; the octal form is the operator label** (and an ASCII-mode option). GX Works3 octal display ≠ wire encoding, exactly as the review predicted | [COM] SLMP accessible-device list (p81–82 block for FX5U/FX5UC); §38.2 Device range footnote (p624) |
| **Parsing consequence** | FX5 profile must parse operator X/Y addresses as **octal labels** → numeric wire value (e.g. `X10` = point 8 → head `08 00 00`). Modern parses X/Y as hex. This is a per-profile, per-device **radix table entry — pure data, no codec change** | derived from the two rows above |
| **0401/0000 word-units cap** | **1–960 words (BIN)** — confirmed twice: 3E command list ("BIN: 960 words (15360 points)") and the FX5S/FX5UJ/FX5U/FX5UC processing table ("0401 0000 — 1/960") | [COM] §38.1 (p614); SLMP ch. processing counts (p107) |
| **Device codes** | Standard bytes present in the FX5 3E device table — X=9C (worked example), Y=9D, M=90, B=A0, D=A8, W=B4, R=AF (+ SM=91, SD=A9, SB=A1, SW=B5, T/C family, Z=CC) — same values as Modern | [COM] §37.1 example (p594), §38.2 table (p623–624) |
| **Device set on FX5 CPU (our 8)** | **Available: D, W, R, M, X, Y, B.** **ZR: NOT accessible on the FX5 CPU** (absent from the FX5U/FX5UC accessible-device block; the ZR code B0 in the generic table applies to other access targets). R follows the CPU's "File Register Setting" | [COM] SLMP accessible-device list (p81–82); §38.2 (p624) |
| **FX5U/FX5UC ranges (defaults)** | X/Y 0–1777 (octal, 1024 pts) · M 0–32767 · L 0–32767 · B/SB 0–7FFF (hex) · D 0–7999 · W/SW 0–7FFF (hex) · SM 0–9999 · SD 0–11999 · R 0–32767 (parameter-dependent where noted) | [COM] SLMP accessible-device list (p81–82) |
| **Special devices observed (record-only)** | SM/SD/SB/SW + timers/counters (T/ST/C/LC codes in the 3E table) exist on FX5 — **recorded for A-3; NOT implemented in A-2** per review directive 10 | [COM] §38.2 |
| **Bit-device word reads** | Same convention (bit devices readable in word units, 16 points/word; caps stated as "960 words (15360 points)"); no A-series-style 16-multiple head restriction stated for FX5 | [COM] §38.1 (p614) |
| **Ethernet/SLMP settings** | SLMP connection configured in GX Works3 (External Device Configuration → SLMP connection); port operator-configured; TCP and UDP both offered (we use TCP only); "Set Processing Counts" affects service capacity | [COM] SLMP chapter (p56–66, p107) — details to be lifted into the FQP appendix in Increment 3 |
| **Word order (32-bit)** | Not explicitly restated in the sections read; standard MELSEC low-word-first assumed **pending Increment-2 golden-vector check** against [COM]/cross-check examples — flagged, not assumed silently | open item → Inc 2 |
| **End codes** | FX5-specific end-code listing exists in [COM]; to be referenced for diagnostics text accuracy in Inc 2 (advisory only, not wire contract) | [COM] troubleshooting section |

## 4. Verdict against the plan's stop-gate

**The FX5 3E-binary wire shape is IDENTICAL to Modern** — same subheader, route,
lengths, timer, command/subcommand, 1-byte codes, 3-byte LE heads, same 960 cap,
same code bytes. **No codec change is needed or permitted.** The iQ-F profile is
**pure profile data**, differing from Modern only in:

1. **X/Y address radix: octal operator labels** (parse octal → numeric wire value);
2. **Device set: ZR excluded** (D, W, R, M, X, Y, B remain);
3. **Range guidance** (documentation/FQP-level; ranges stay PLC-enforced per the
   A-1 finding that ranges are target-module-dependent);
4. Provenance fields (manual/revision, evidence links).

Per plan: this result comes back for approval, then **Increment 2** (registry +
ADR-0034 + tests, incl. Modern byte-identity suite) may start.

## 5. Status (unchanged claims)

iQ-F/FX5 profile: **not implemented yet** (that's Increment 2). Modern profile
statuses unchanged. No Field-qualified/Certified claims. Profile selector still
excluded. Scope freeze intact (no writes/UDP/4E/1E/ASCII/browse/demo/CSV).
