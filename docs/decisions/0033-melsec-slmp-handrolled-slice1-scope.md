# ADR-0033: MELSEC source — hand-rolled SLMP client, Slice-1 scope, and wire-safety rules

**Status:** Accepted (2026-06-30) — architecture locked. **Field verification of
device-code/word-order golden fixtures pending** the discovery Part B capture
(see Open / Pending); that does not block Slice-1 implementation.
**Date:** 2026-06-30
**Framing:** The Mitsubishi MELSEC source adapter is built cold (no prior project
or ADR). This ADR locks the decisions that are painful to reverse: how we talk to
the device (hand-rolled, no third-party dependency), exactly what Slice 1 supports
(MC 3E binary over TCP, read-only), the corrected device-address radix, and the
two wire-safety rules that follow from 3E framing. Plan trail:
`docs/sessions/2026-06-30-melsec-source-adapter-plan-v1/v2/v3.md`.

## Context

MELSEC is Mitsubishi's PLC family. Field devices speak **SLMP** / **MC Protocol**
in several frames (3E, 4E, 1E), two transports (TCP, UDP), and two encodings
(binary, ASCII). The `Sources.S7` adapter is the right structural template for the
adapter shell (config record, scan-plan → block-read → decode → emit-canonical,
connection manager, ADR-0020 redaction, ADR-0015 browse-exempt carve-out), but S7
got to wrap **Sharp7** (MIT, pure-C#). MELSEC has **no equally clean MIT library**:
the popular option (HslCommunication) carries commercial-license terms that are
unsafe to ship in a commercial product (Locked Decision context; cf. the
third-party-library posture in `docs/licensing/module-catalog.md`).

The customer's exact hardware is not yet confirmed, so the design must keep the
protocol/transport/frame **configurable** while not shipping unverified framing.

A subtle but high-cost MELSEC trap: the **device-number radix differs per device
type**, and getting it wrong does not error — it silently reads the wrong PLC
memory. Plan v1 mis-stated `ZR` as decimal; this ADR records the correction so it
is never relitigated.

## Decision

### Rule 1 — Hand-rolled SLMP/MC client, no third-party Mitsubishi dependency

The wire layer is implemented in pure C# in `ElpisEdgeConnect.Sources.Melsec` with
**no PackageReference to any Mitsubishi/SLMP library**. This mirrors exactly why S7
chose Sharp7 (pure-C#, no native dep, ship-safe license) — but because no clean MIT
MELSEC library exists, we own the framing. The wire surface sits behind an
`IMelsecClient` abstraction (like `IS7Client`) so the adapter is testable against a
fake.

### Rule 2 — Slice 1 supports exactly MC 3E binary over TCP, read-only

Slice 1 implements **batch read (command 0x0401, word-units subcommand 0x0000)**
over **MC 3E binary frames** on **TCP**. The config record carries forward-compatible
fields for other modes (`TransportProtocol` Tcp|Udp, `FrameMode`
Mc3EBinary|Mc3EAscii|Mc4EBinary|Mc1EBinary, `DeviceProfile`
ModernQL_iQ|QnA|ACpu), but anything outside the supported subset is
**accepted in config and rejected at validation** with a typed error
(`MELSEC.CONFIG_MODE_NOT_IMPLEMENTED` / `MELSEC.PROFILE_NOT_IMPLEMENTED`). A
profile can never "unlock" an unsupported frame. Unsupported modes are only built
when confirmed customer hardware requires them, each as its own scoped slice with
its own golden-byte tests.

**Read-only:** no write command (0x1401) in Slice 1. When writes are scoped later,
they are gated behind **all** of: a connection-level `AllowWrites` (default false),
a per-tag `Writable` opt-in, write-value validation, and an audit-chain entry per
write.

### Rule 3 — Device-address radix is per-device and pinned (the corrected table)

The device-**number** notation radix the parser encodes, per Mitsubishi SH-080008:

| Radix | Devices |
|-------|---------|
| **Hexadecimal** | `X`, `Y`, `B`, `W`, `SB`, `SW`, `DX`, `DY`, **`ZR`** |
| **Decimal** | `M`, `L`, `F`, `V`, `S`, `T`, `C`, `D`, `R`, `SM`, `SD`, `Z` |

`ZR` is **hexadecimal** (plan-v1 said decimal — corrected here). The binary
**device-code byte** (e.g. D=0xA8, W=0xB4, ZR=0xB0, M=0x90, X=0x9C, Y=0x9D,
B=0xA0, R=0xAF) is a separate table and is **pinned by golden-byte tests** against
SH-080008; no code-byte or radix is asserted from memory without a golden test.

### Rule 4 — Slice-1 implemented device set is explicit; radix-known ≠ supported

Knowing a device's radix does not make it supported. Slice 1 implements:
- **Word devices** (word batch read): `D`, `W`, `R`, `ZR`.
- **Bit devices** (read via word-units batch, bit-extracted): `M`, `X`, `Y`, `B`.
- **Word-bit** (`D100.3`, `W10.F`, `R200.0`, `ZR…`): the containing word is read
  and the bit (index `0`–`F` hex) extracted.

Every other recognized device (`SB`, `SW`, `DX`, `DY`, `T`, `C`, `L`, `F`, `V`,
`S`, `SM`, `SD`, `Z`, …) is **rejected at config validation** with
`MELSEC.DEVICE_NOT_IMPLEMENTED` — it must never parse silently and read wrong
memory. A bit suffix on an already-bit device (`M100.3`) is rejected.

### Rule 5 — TCP single-flight + drop/reconnect on timeout (3E has no req/resp serial)

3E binary frames carry **no serial number** to match a response to its request
(only 4E does). Therefore Slice 1:
- enforces **one in-flight request per connection** (the adapter serializes; the
  connection manager asserts it), and
- on any read timeout, **closes and reconnects the socket** rather than reusing it,
  so a late datagram dies with the socket and cannot be mis-parsed as the next
  read's reply. Reconnect flows through the normal backoff/circuit-breaker path.

This trades some throughput on flaky links (connection churn, bounded by the
breaker) for correctness. The eventual robustness upgrade is **4E** (carries the
serial) — explicitly out of Slice 1. UDP, being connectionless, breaks this model
entirely and is deferred to a separately-designed slice, not a copy of the TCP one.

### Rule 6 — Monitoring timer encodes in 250 ms units; never silently shortened

The MC monitoring-timer wire field is in **250 ms units** (`0` = wait
indefinitely). `MonitoringTimerMs` is **ceiled to the next 250 ms** (never
shortened — `1100 → 1250`), logged when rounded up, and rejected if the encoded
units exceed 65535 (`MELSEC.CONFIG_TIMER_RANGE`). The client socket timeout
(`RequestTimeoutMs`) must be **≥** the encoded monitoring timer, else the client
would abandon a read before the CPU could answer — rejected with
`MELSEC.CONFIG_TIMEOUT_INCOHERENT`.

## Consequences

**Positive:**
- No third-party license exposure on the MELSEC wire; ship-safe like Sharp7.
- The supported surface is honest: unsupported modes/devices fail loudly at config
  time, never silently misbehave on the wire.
- The `ZR`-radix correction and per-device radix table are locked before any code,
  killing the highest-probability silent-wrong-data bug class.
- 3E's missing serial is handled by structural single-flight + reconnect, not by
  hoping responses arrive in order.

**Negative / costs:**
- We maintain hand-rolled framing and a device-code table that must be kept correct
  against the manual (mitigated by golden-byte tests + the Part B capture gate).
- Single-flight + reconnect-on-timeout causes visible connection churn on flaky
  links (mitigated by the circuit breaker; 4E is the future upgrade).
- "Configurable but validation-rejected" fields can surprise an operator who sets
  `FrameMode=Mc4EBinary` and gets a rejection — mitigated by a clear typed message.

**Forbidden patterns:**
- Adding a third-party Mitsubishi/SLMP library dependency to ship the wire (Rule 1).
- Parsing a recognized-but-unimplemented device instead of rejecting it (Rule 4).
- Treating `ZR` (or any device) with the wrong radix (Rule 3).
- Reusing a TCP socket after a read timeout in 3E mode (Rule 5).
- Silently shortening a user-supplied monitoring timeout (Rule 6).
- Letting a `DeviceProfile` value imply an unsupported frame/transport (Rule 2).

## Open / Pending (does not block Slice-1 implementation)

1. **Field-verify the golden fixtures** — Slice 1 golden-byte tests are
   **spec-derived and labelled field-unverified** until the discovery **Part B
   known-good capture** confirms device-code bytes and word order against a real
   CPU. Required before field-verified / release signoff, **not** before coding.
2. **Customer hardware confirmation** — Part A Q1/Q3 (or explicit confirmation the
   target stays modern MC 3E binary/TCP) before the runtime slice is considered
   correct-by-scope. A 4E/1E/ASCII/UDP need re-scopes into its own slice.
3. **Non-modern profile caps** (`QnA`, `ACpu` `MaxPointsPerRequest`) — TBD; those
   profiles are rejected in Slice 1, so no guessed cap ships.

## Reference

- Plan trail: `docs/sessions/2026-06-30-melsec-source-adapter-plan-v1.md` (file map),
  `…-v2.md` (review deltas), `…-v3.md` (reality-check + final scope).
- `docs/sessions/2026-06-30-melsec-discovery-package.md` (Part A scope-confirm,
  Part B capture).
- **ADR-0015** wizard contract (MELSEC browse-exempt, Rule 6/9 carve-out).
- **ADR-0020** diagnostic-bundle redaction (MELSEC connection keys registration).
- Slice-0 source-generation foundation
  (`docs/sessions/2026-06-25-source-generation-foundation-slice-0-spec.md`) —
  MELSEC consumes `ISourceRetirement` only.
- Mitsubishi **SH(NA)-080008** SLMP/MC reference manual (frame layout, device
  codes, radix, 250 ms timer units, low-byte-first field encoding).
- `docs/licensing/module-catalog.md` (`source-melsec` = Premium; third-party
  library posture).
