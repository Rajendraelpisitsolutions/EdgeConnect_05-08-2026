# MELSEC A-3.0 — SM/SD/SB/SW manual audit (gate before code)

**Date:** 2026-07-03
**Parent:** A-3 plan v2 §0.2 (audit gate). **Result: all four devices pinned
from the already-pinned manuals; same 0401/0000 wire path — no codec change.**

**Sources (pinned previously, SHA-256 in the A-1 / A-2D audits):**
`[MC]` = SH(NA)-080008-AB (May 2022) · `[COM]` = SH(NA)-082625ENG-J (April 2026).

## Per-device facts

| Fact | SM | SD | SB | SW |
|---|---|---|---|---|
| Name | Special relay | Special register | Link special relay | Link special register |
| Kind (access) | Bit | Word | Bit | Word |
| Device code (3E binary, subcmd 0000) | **0x91** | **0xA9** | **0xA1** | **0xB5** |
| Device-number radix | **decimal** | **decimal** | **hexadecimal** | **hexadecimal** |
| Citation (code/kind/radix) | [MC] §8.1 Device code list (p68): "Special relay SM Bit Decimal SM 91H" | [MC] §8.1 (p68): "Special register SD Word Decimal SD A9H" | [MC] §8.1 (p68): "Link special relay SB Bit Hexadecimal SB A1H" | [MC] §8.1 (p68): "Link special register SW Word Hexadecimal SW B5H" |
| Modern (iQ-R/Q/L) availability | yes | yes | yes | yes |
| Modern valid range | target-module-dependent ([MC] §8.1 p69 rule; PLC rejects out-of-range with an end code, as for all devices) | same | same | same |
| iQ-F/FX5 availability | **yes** | **yes** | **yes** | **yes** |
| FX5U/FX5UC range | 0–9999 | 0–11999 | 0x0–0x7FFF | 0x0–0x7FFF |
| Citation (FX5) | [COM] SLMP accessible-device list, FX5U/FX5UC block (p81–82) | same | same | same |
| Batch-read access unit | word units, 16 points/word (bit device) | word units | word units, 16 points/word | word units |
| Wire path | existing bit-device path (no codec change) | existing word-device path | existing bit-device path | existing word-device path |
| Simulator seed | `(SM,0)=0x0001` (bit0 ON) | `(SD,0)=42` | `(SB,0)=0x0001` | `(SW,0x10)=0x0AB0` |
| UI validation text | dec number; Bool datatypes | dec number; word datatypes | hex number; Bool | hex number; word |

## Cross-profile conclusion

All four devices exist on **both** profiles → both profile entries gain them;
no new cross-profile rejection (ZR-on-iQ-F remains the rejection example).
Existing 8-device behavior untouched (byte-identity suite).

## Curated hint (minimal, manual-pinned)

Wizard helper gains two sentences only:
"SM/SD are special system devices — use only documented addresses for your CPU.
Common example: **SM400 (RUN monitor)** — verify against your PLC manual."
SM400 pinning: used as the RUN-monitor special relay in [COM]'s own
communication examples (multiple occurrences). No browse, no in-UI device manual.

## Excluded in A-3a (recorded, unchanged)

Timers/counters (A-3b, post-A-3a review), long/extended families (subcommand
0002 territory), DX/DY/L/F/V/S/Z, and every scope-guard item (no writes / UDP /
4E / 1E / ASCII / browse / demo / CSV / QnA / ACpu).
