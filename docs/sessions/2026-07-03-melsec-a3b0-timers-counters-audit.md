# MELSEC A-3b.0 — timer/counter manual audit (gate before code)

**Date:** 2026-07-03
**Parent:** A-3b plan v2 §A-3b.0 (audit gate). **Result: all nine sub-devices
pinned from the pinned manuals; same 0401/0000 word-unit wire path — no codec
change. Retentive timers ARE available on FX5 per [COM] → both profiles get all
nine. Current values are 1-word (16-bit); no 2-word form without the long family.**

**Sources (pinned earlier, SHA-256 in the A-1 / A-2D audits):**
`[MC]` = SH(NA)-080008-AB (May 2022) · `[COM]` = SH(NA)-082625ENG-J (April 2026).

## Per-sub-device facts (binary, subcommand 0000)

| Mnemonic | Logical meaning | Kind | Code (bin) | Radix | Datatype | Modern | iQ-F/FX5 |
|---|---|---|---|---|---|---|---|
| **TS** | Timer contact | Bit | **0xC1** | decimal | Bool | yes | yes |
| **TC** | Timer coil | Bit | **0xC0** | decimal | Bool | yes | yes |
| **TN** | Timer current value | Word | **0xC2** | decimal | Int16/UInt16 | yes | yes |
| **STS** | Retentive-timer contact | Bit | **0xC7** | decimal | Bool | yes | yes |
| **STC** | Retentive-timer coil | Bit | **0xC6** | decimal | Bool | yes | yes |
| **STN** | Retentive-timer current value | Word | **0xC8** | decimal | Int16/UInt16 | yes | yes |
| **CS** | Counter contact | Bit | **0xC4** | decimal | Bool | yes | yes |
| **CC** | Counter coil | Bit | **0xC3** | decimal | Bool | yes | yes |
| **CN** | Counter current value | Word | **0xC5** | decimal | Int16/UInt16 | yes | yes |

**Citations:**
- Codes/kind/radix (Modern Q/L subcmd-0000 column): [MC] §8.1 "Device code list"
  (p68) — "Timer Contact TS Bit Decimal … C1H", "Coil TC … C0H", "Current value
  TN Word … C2H"; "Retentive timer Contact STS Bit Decimal SS C7H … Coil STC SC
  C6H … Current value STN Word SN C8H"; "Counter Contact CS … C4H … Coil CC C3H
  … Current value CN Word CN C5H".
- **FX5 availability + binary codes + ranges:** [COM] FX5 SLMP device table —
  rows "Timer Contact Bit TS (TS**) C1H", "Current value Word TN (TN**) C2H",
  "Retentive timer Contact Bit SS (STS*) C7H", "Current value Word SN (STN*)
  C8H", "Counter Contact Bit CS (CS**) C4H", "Current value Word CN (CN**) C5H";
  FX5 ranges TS/TN **0–511**, CS/CN **0–255** (retentive-timer points are CPU-
  parameter-dependent). **ST retentive family is present in the FX5 table** → FX5
  supports it, so no cross-profile rejection among these nine.

## Current-value width (open-question 2 resolved)

TN / STN / CN are **1-word (16-bit) current values** ([MC] & [COM] mark them
"Word"). The **2-word / long** current values are the **LTN / LSTN / LCN** family,
which [MC] §8.2 (p89) requires "**four words per one device**" and lists as
subcommand-0002 / long-device territory. → **Regular TN/CN/STN stay 16-bit only
on the 0401/0000 path; 32-bit is NOT added.** Long family stays excluded.

## Batch-read access unit

All nine read via the existing **0401/0000 word-unit** path. Contacts/coils
(TS/TC/STS/STC/CS/CC) are bit devices → 16 points/word (existing bit path).
Current values (TN/STN/CN) are word devices → existing word path. **No codec
change.**

## Excluded (recorded, unchanged)

Long/extended: LTS/LTC/LTN, LSTS/LSTC/LSTN, LCS/LCC/LCN (codes 0x0051/0x0050/
0x0052, 0x0059/0x0058/0x005A, 0x0055/0x0054/0x0056 — **2-byte iQ-R-native codes,
subcommand-0002 territory**), LZ. Bare `T`/`C`/`ST` prefixes: rejected with a
suggestion (ambiguous — GX Works3 resolves by context; we do not guess). DX/DY/
L/F/V/S/Z: unchanged rejects. Every scope-guard item unchanged.

## Parser note (correctness risk)

Longest-known-prefix resolution must place the 3-char retentive symbols
(**STS/STC/STN**) ahead of the 2-char (`TS`/…) and 1-char (`S` step relay, `T`,
`C`) matches, so `STN10` → STN, not `S`+`TN` or `ST`+`N`. Pinned by tests.

## Simulator seed plan (both profiles)

`(TN,100)=1234` (timer current), `(TS,100)=0x0001` (timer contact word bit0),
`(CN,0)=7` (counter current), `(CS,0)=0x0001` (counter contact word bit0),
`(STN,0)=500` (retentive-timer current), `(STS,0)=0x0001`. verify.py asserts on
modern AND fx5 (all nine available on both).

## UI/helper copy (concise, no browse)

"Timers/counters use explicit MELSEC mnemonics: TN for timer current value, TS
for timer contact, TC for timer coil; STN/STS/STC for retentive timers;
CN/CS/CC for counters. Bare T100/C100/ST100 is ambiguous and is not accepted."
