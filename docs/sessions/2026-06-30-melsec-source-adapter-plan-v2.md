# Mitsubishi MELSEC source adapter — Plan v2

**Date:** 2026-06-30
**Status:** Plan v2 — incorporates ChatGPT review of v1. **Direction approved;
implementation still gated** (→ reality-check → v3 or explicit go-ahead).
**Supersedes:** `2026-06-30-melsec-source-adapter-plan-v1.md` (read v1 for the
unchanged sections: architectural locks §2, full file-by-file table §3, backend
integration touch-points §5, sequencing §8 — those stand as written unless a
delta below overrides them).

## 0. Locked decisions (unchanged, re-affirmed by review)

- Default target **SLMP / MC 3E binary over TCP**.
- Keep protocol/transport **configurable** until customer confirms hardware.
- **Hand-roll the C# wire layer, no third-party Mitsubishi dependency.**
- **Backend-first** slice; wizard + demo mode follow.

## 1. Changes from v1 (the review deltas)

Each item below is a required v2 correction. They override the matching v1 text.

### Δ1 — Address radix table corrected (the critical fix)

v1 wrongly grouped `ZR` as decimal. **`ZR` is hexadecimal.** Corrected, complete
radix table (per Mitsubishi SH-080008 SLMP reference manual — the device number
**notation radix** the parser must encode per device type):

| Radix | Devices |
|-------|---------|
| **Hexadecimal** | `X`, `Y`, `B`, `W`, `SB`, `SW`, `DX`, `DY`, `ZR` |
| **Decimal** | `M`, `L`, `F`, `V`, `S`, `T`, `C`, `D`, `R`, `SM`, `SD`, `Z`, `SN` (timer/counter set/contact variants follow their base) |

> The **binary device-code byte** for each device (e.g. D=0xA8, W=0xB4, ZR=0xB0,
> M=0x90, X=0x9C, Y=0x9D, B=0xA0, R=0xAF) is a *separate* table from the radix and
> is equally silent if wrong — both the radix and the code byte are pinned by
> golden-byte tests against SH-080008 at implementation. Do not assert a code byte
> from memory in code without the golden test.

**Required tests (explicit, per review):** parser `Theory` cases proving
`ZR`, `W`, `B`, `X`, `Y` parse as **hex** and `D`, `M`, `R` parse as **decimal**
(e.g. `W1A` → 0x1A=26; `D26` → 26; `ZR1F` → 0x1F=31; `X20` → 0x20=32 hex;
`M100` → 100 decimal; `R200` → 200 decimal). A wrong-radix regression must fail
a named test.

### Δ2 — Slice 1 runtime support narrowed to MC 3E binary over TCP only

Config **fields** still exist for `TransportProtocol` (Tcp|Udp), `FrameMode`
(Mc3EBinary|Mc3EAscii|Mc4EBinary|Mc1EBinary) — so gateway.json forward-compat and
v2/v3 don't need a schema change. But **`ValidateConfigAsync` rejects anything
other than `FrameMode=Mc3EBinary` + `TransportProtocol=Tcp`** with a clear, typed
error:

```
MELSEC.CONFIG_MODE_NOT_IMPLEMENTED:
  "FrameMode 'Mc4EBinary' is accepted in config but not implemented in Slice 1
   (read support is MC 3E binary over TCP only). Set FrameMode=Mc3EBinary, or
   request the mode for your confirmed hardware."
```

Unsupported modes are only un-gated when confirmed customer hardware requires
them (then they become their own scoped slice with their own golden-byte tests).
This keeps "configurable" honest without shipping unverified framing.

### Δ3 — Per-tag `WordOrder` for 32/64-bit values

32-bit (`Int32`/`UInt32`/`Float32`) and 64-bit (`Int64`/`Float64`) values span
consecutive words. Add **per-tag** `WordOrder` enum (`LowWordFirst` default |
`HighWordFirst`). Decoder honors it; 16-bit and bit types ignore it. Rationale:
customer PLC/program conventions vary (some store 32-bit DINTs high-word-first),
and a global setting is wrong when one device mixes conventions.
Tests: Int32 and Float32 decoded both orders from the same byte buffer.

### Δ4 — Word-bit addresses (`D100.3`, `W10.F`, `R200.0`)

The parser must accept a **bit index suffix on word devices**, not just true bit
devices (`M100`, `X20`). Syntax: `<wordDevice><number>.<bit>` where `<bit>` is
**0–F hexadecimal** (0–15). `W10.F` = bit 15 of link register W10; `D100.3` =
bit 3 of data register D100. Decoder extracts the bit from the containing word
(`(word >> bit) & 1`). The scan planner reads the **word**; multiple word-bit tags
on the same word coalesce into one word read. Validation: bit index 0–15 only;
bit suffix illegal on already-bit devices (`M100.3` rejected). Tests cover
`D100.3`, `W10.F`, `R200.0`, out-of-range bit, and illegal-on-bit-device.

### Δ5 — `MonitoringTimerMs` encoding made explicit

The MC monitoring timer wire field is in **units of 250 ms** (`0x0000` = wait
indefinitely). So `MonitoringTimerMs` must be **divided by 250 and rounded** to
encode the 16-bit field, and validated:
- Must be `0` (= infinite) or a positive value; values not a multiple of 250 are
  **rounded to the nearest 250 ms** and a one-line warning is logged at config
  load (not silently truncated).
- Encoded units must fit `1..65535` (max ≈ 16383750 ms); reject above range.
- Default `MonitoringTimerMs = 4000` (→ 16 units) as a safe field default;
  per-read socket timeout (`RequestTimeoutMs`) remains the hard client-side bound.
Tests: 1000→4 units, 1100→rounds to 1000 (4 units)+warning, 0→infinite,
over-range→rejected.

### Δ6 — `MaxPointsPerRequest` is profile-aware

Add `DeviceProfile` (`ModernQL_iQ` default | `QnA` | `ACpu`). The word-batch-read
(cmd 0401 / subcmd 0000) point cap differs by family:
- `ModernQL_iQ` (iQ-R / iQ-L / Q / L, 3E/4E): **960 words** (bit-units cap higher,
  ~3584/7168 — only relevant when bit-batch reads land in a later slice).
- `QnA`: lower (e.g. 480 words) — documented, validated.
- `ACpu` (1E family): lower still — only relevant once 1E framing is implemented.

`MaxPointsPerRequest` is an optional override **clamped to the profile cap**
(reject an override exceeding the cap with a typed error). Slice 1 default profile
`ModernQL_iQ`, default cap 960, conservative override suggestion 480. Tests:
override within cap accepted, over-cap rejected, planner splits at the cap.

### Δ7 — Slice 1 is read-only (stated explicitly)

Slice 1 implements **batch read (cmd 0401) only**. No write command (1401). When
write support is scoped later it is gated behind **all** of: a connection-level
`AllowWrites` flag (default false), a **per-tag** `Writable` opt-in, write-value
validation, and an **audit-chain entry per write** (consistent with the Studio
audit model). This is recorded now so "read-only" is a stated boundary, not an
omission. Added to §Non-goals.

### Δ8 — TCP single-flight + drop/reconnect on timeout

3E binary frames carry **no serial number** for request/response matching (only
4E does). So a late response after a timeout would desync the next read's framing
(it would parse the previous reply as the current one). Slice 1 therefore:
- enforces **one in-flight request per connection** (the adapter already
  serializes; make it explicit and asserted in the connection manager), and
- on any read timeout, **closes and reconnects the socket** rather than reusing
  it — the late datagram dies with the socket. Reconnect goes through the normal
  backoff/breaker path.
Tests: a fake server that replies late proves the client discards it via
reconnect and the *next* read still frames correctly.

### Δ9 — Loopback fake SLMP server is a REQUIRED fixture

Promoted from "Integration (optional)" to a **required** test fixture: an in-proc
`TcpListener` speaking 3E binary (success, each end-code, late-reply, partial-read,
malformed-length). Lives in `Integration.Tests`; the adapter lifecycle +
single-flight + reconnect tests depend on it. No real PLC, per the mock-only rule.

### Δ10 — Customer hardware questionnaire (Appendix A)

Added below as Appendix A, and I will also spin it out as a standalone
field-discovery doc mirroring the Modbus RTU discovery package
(`2026-06-23-modbus-rtu-discovery-package.md`) so it can go to the customer
verbatim. Answers feed v3's validated support matrix.

## 2. Open-question resolutions (from review)

| Question | Resolution |
|----------|-----------|
| License tier | **Pro+ / Premium approved** for `source-melsec`. `module-catalog.md` row tier = Premium. |
| Customer hardware | **Create + send the discovery questionnaire now** (Appendix A + standalone doc). |
| ADR-0033 | **Yes, but draft after v2 corrections land** — i.e. once v3/go-ahead, write ADR-0033 reflecting the corrected radix, the Slice-1 support matrix, read-only boundary, and TCP single-flight/reconnect rule. |
| Slice boundary | Confirmed: backend slice ends at **Host DI + gateway.json instantiation + green tests**; **not "done"** by the operator-available bar until the wizard tile exists. |
| Sony / source-generation | **Confirm merge-order before branching.** MELSEC **only consumes `ISourceRetirement`** and implements **no** generation logic of its own (rides the supervisor lease). |

## 3. Updated config surface (consolidated)

Connection: `Host` (required, no default), `Port` (required, no universal default —
document common 5000/5001/5006/6000, do not default), `TransportProtocol`
(`Tcp` default; `Udp` config-accepted/validation-rejected in Slice 1),
`FrameMode` (`Mc3EBinary` default; others config-accepted/validation-rejected),
`DeviceProfile` (`ModernQL_iQ` default), `NetworkNo` (0x00), `PcNo` (0xFF),
`RequestDestModuleIoNo` (0x03FF), `RequestDestModuleStationNo` (0x00),
`MonitoringTimerMs` (4000; encoded ÷250, rounded, validated).
Timeouts/retry/backoff/breaker: copy S7's set verbatim.
Scan-planner: `MaxGapWords`, `MaxPointsPerRequest` (optional, clamped to profile).
Tags: `name`, `address` (incl. word-bit `Dn.b`), `datatype`, `wordOrder`
(`LowWordFirst` default), `scanRateMs`, `unit`, `scale`, `offset`.
**Read-only** in Slice 1 — no write fields exposed yet.

## 4. Non-goals (Slice 1)

No writes (Δ7). No 4E/1E/ASCII framing or UDP at runtime (Δ2, config-only). No
bit-batch-read (subcmd 0001) — word reads + word-bit extraction only. No browse
(ADR-0015 carve-out). No demo mode, no wizard, no CSV import (later slices). No
generation logic (rides supervisor lease, Δ Sony).

## 5. Test plan (updated)

All v1 categories stand (frame golden bytes, address parser, decoder, scan
planner, lifecycle, retirement, host wiring) **plus** the v2 additions:
- Radix `Theory` (Δ1), word-order both directions (Δ3), word-bit addresses (Δ4),
  monitoring-timer encoding/rounding (Δ5), points-cap clamp + planner split (Δ6),
  mode-not-implemented validation rejections (Δ2), TCP late-reply
  reconnect/resync (Δ8).
- **Required** loopback SLMP fake fixture (Δ9).
- Gate unchanged: full solution 0/0; run the **entire** `Host.Tests` +
  `Management.Tests` projects (not topic-filtered) before any PR.

---

## Appendix A — Customer hardware discovery questionnaire

To pin the validated Slice-1 support matrix (and decide whether any
config-accepted-but-unimplemented mode must be promoted). Send before/with v3.

**PLC / CPU**
1. Mitsubishi CPU model(s)? (e.g. iQ-R R08, iQ-F FX5U, Q03UDV, L26CPU, QnA, A-series)
2. Single model or mixed fleet? How many units?

**Network module / transport**
3. Built-in Ethernet, or a separate Ethernet module? Module model?
4. **TCP or UDP** configured on the module?
5. **IP address(es) and port(s)** of the Ethernet interface?
6. Communication setting: **MC Protocol 3E** (or 4E / 1E)? **Binary or ASCII**?
7. `NetworkNo` / `PC No` / station number, if non-default?
8. Are PUT/GET / external read access enabled? Any IP allow-list / firewall?

**Tags / data**
9. List of device addresses to read (e.g. `D100`, `W1A`, `M200`, `D200` as DINT,
   `D300` as REAL) with desired names, units, and scan rates.
10. For any 32/64-bit values: **word order** (low-word-first or high-word-first)?
11. Any **word-bit** points needed (e.g. `D100.3`)?
12. Approximate total point count and fastest required scan interval?

**Operational**
13. Any existing SCADA/HMI already polling these CPUs (contention / connection
    limits)?
14. Read-only acceptable for v1, or are writes required soon (drives Δ7 timing)?

---
*Next: reality-check pass → v3, or explicit user go-ahead. On go-ahead: confirm
Sony merge-order, then draft ADR-0033, then implement per v1 §8 sequencing with
the v2 corrections folded in.*
