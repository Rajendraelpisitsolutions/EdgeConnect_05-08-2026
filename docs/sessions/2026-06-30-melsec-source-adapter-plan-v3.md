# Mitsubishi MELSEC source adapter — Plan v3 (candidate lock)

**Date:** 2026-06-30
**Status:** v3 — **APPROVED 2026-06-30** as the candidate lock. Reality-check
folded in + the five v2.x tightenings applied. Timer policy confirmed (ceil-up,
§2.1). Gate wording adjusted per approval (§4). Implementation cleared to begin
Slice 1 backend (ADR-0033 drafted alongside; see `docs/decisions/0033-*`).
**Reads with:** [v1](2026-06-30-melsec-source-adapter-plan-v1.md) (file-by-file,
integration touch-points, sequencing) and
[v2](2026-06-30-melsec-source-adapter-plan-v2.md) (review deltas Δ1–Δ10). v3 only
records the reality-check findings, the five tightenings, and the device matrix.
Everything in v1/v2 stands unless a §2/§3 item overrides it.

---

## 1. Reality-check findings (the critical pass)

These are the honest soft spots in v1/v2. Several resolve into §4 gates rather
than design changes — that's the point of the pass.

**RC-1 — The golden-byte tests need a real source of truth we don't yet have.**
v2 repeatedly says "pin device-code bytes / word order against SH-080008 via
golden-byte tests." But the manual is **not in-repo and we have no capture**.
Without the spec PDF or a known-good request/response capture, golden tests are
**self-referential** (they assert what we coded, not what the PLC expects). → This
is exactly what **Part B of the discovery package** secures; it is a hard
pre-implementation gate (§4), not a thing we can paper over.

**RC-2 — Effort/date is unanchored, same failure mode the Modbus RTU package
called out.** No customer hardware, no bench CPU, no capture. We can write the
**design** and spec-derived golden tests now, but **real-CPU validation is gated**
on the questionnaire return. v3 locks design, not a ship date.

**RC-3 — 3E "no serial number" reconnect has a real throughput cost.** Δ8's
drop-and-reconnect-on-timeout is correct for safety, but on a flaky link with
aggressive scan rates it can **thrash** (every timeout = TCP teardown + re-open).
The circuit breaker bounds it, but operators will see connection churn in
diagnostics. Documented as expected behavior; the eventual robustness upgrade is
**4E** (carries the serial for request/response matching) — explicitly out of
Slice 1. Honest tradeoff, not a defect.

**RC-4 — Monitoring timer vs socket timeout must be coherent.** The device-side
monitoring timer (250 ms units) and our client `RequestTimeoutMs` interact: if the
socket timeout is **shorter** than the device's monitoring timer, the client gives
up (and reconnects, RC-3) before the CPU would have answered — pure waste. → New
validation in §2.1.

**RC-5 — Scan-planner coalescing boundaries were implicit.** A single batch read
(cmd 0401) is **one device code**. The planner must **never coalesce across device
codes** (D and W in one read is impossible) and never across the word/bit-area
boundary. v1/v2 implied this; v3 states it as a planner invariant with a test.

**RC-6 — The implemented device set was never enumerated** (only radix was). Listing
radix for `SB/SW/DX/DY` made them look supported. Resolved by the explicit matrix
in §3 + tightening #4 (`MELSEC.DEVICE_NOT_IMPLEMENTED`).

**RC-7 — Sony / source-generation merge-order is an external pre-branch gate**, not
a design item. MELSEC only consumes `ISourceRetirement`. Confirm before branching
(§4).

**RC-8 — UDP is not just "more framing."** It's connectionless, so it breaks the
single-flight + reconnect reliability model entirely (no socket to drop). Correctly
deferred — noting it so a future slice budgets a *different* reliability design,
not a copy of the TCP one.

---

## 2. The five tightenings (applied)

### 2.1 — `MonitoringTimerMs`: never silently shorten (tightening #1)

**Resolution (CONFIRMED): ceil to the next 250 ms** (never shortens a
user-supplied timeout), with a one-line info log when the value is rounded up.
Example: `1100 → 1250 ms (5 units)`, **not** `1000`. `0` = wait indefinitely
(unchanged). Over-range (encoded units > 65535) → reject with
`MELSEC.CONFIG_TIMER_RANGE`. (Reject-non-multiples was the alternative; user
confirmed ceil-up.)

**New coherence validation (RC-4):** if `RequestTimeoutMs` < encoded monitoring
timer (in ms), reject with `MELSEC.CONFIG_TIMEOUT_INCOHERENT` ("client socket
timeout {x}ms is shorter than the device monitoring timer {y}ms; the client would
abandon reads before the CPU responds"). Tests: 1100→1250+log; incoherent pair
rejected; 0→infinite accepted.

### 2.2 — `DeviceProfile` cannot imply unsupported framing (tightening #2)

Slice 1 supports **only** `DeviceProfile = ModernQL_iQ` (iQ-R / iQ-L / Q / L,
3E binary). `QnA` and `ACpu` are **config-accepted but validation-rejected** in
Slice 1 with `MELSEC.PROFILE_NOT_IMPLEMENTED`. Crucially, contradictory
combinations fail clearly:
- `ACpu` implies 1E/A-series framing → rejected (1E not implemented).
- Any profile combined with `FrameMode != Mc3EBinary` or `TransportProtocol != Tcp`
  → rejected per Δ2 (`MELSEC.CONFIG_MODE_NOT_IMPLEMENTED`), so a profile can never
  "unlock" an unsupported frame.

This makes the profile field forward-compatible without letting it imply behavior
we haven't built. Tests: ModernQL_iQ accepted; QnA/ACpu rejected;
ACpu+3E rejected as contradictory.

### 2.3 — `MaxPointsPerRequest`: pin modern, mark the rest TBD (tightening #3)

- `ModernQL_iQ`, 3E binary, **word batch read (cmd 0401 / subcmd 0000): 960 words**
  — pinned and documented. Default override suggestion stays conservative but the
  **hard cap is 960**; an override > 960 is rejected (`MELSEC.CONFIG_POINTS_CAP`).
- `QnA` / `ACpu` caps: **TBD — not shipped in Slice 1** (the profiles are rejected,
  §2.2). No guessed "480" ships. When a customer confirms a non-modern family, the
  cap is pinned from that family's manual in its own scoped slice.

Tests: override ≤ 960 accepted; > 960 rejected; planner splits a >960-word demand
into multiple reads.

### 2.4 — Device matrix: radix-known ≠ Slice-1-supported (tightening #4)

See the explicit matrix in §3. Any device whose radix we know but which is **not in
the Slice-1 implemented set** (e.g. `SB`, `SW`, `DX`, `DY`, `T`, `C`, `L`, `F`,
`SM`, `SD`, `Z`) is rejected at config validation with
`MELSEC.DEVICE_NOT_IMPLEMENTED` ("device 'SW' is recognized but not supported in
this release; supported: D, W, R, ZR, M, X, Y, B"). It must **not** parse silently
and read wrong memory. Tests: each unsupported device rejected by name; each
supported device accepted.

### 2.5 — ADR-0033 deferred to post-approval (tightening #5)

ADR-0033 is **not written now** (per your explicit hold). On v3 approval it will be
drafted to capture: the corrected **`ZR` = hexadecimal** radix; **Slice-1 = MC 3E
binary over TCP, read-only**; the **TCP single-flight + drop/reconnect-on-timeout**
rule (and why — 3E has no req/resp serial); and the **hand-rolled, no-third-party**
decision. Until then these live in this plan trail as the binding record.

---

## 3. Slice-1 device support matrix

| Device | Meaning | Radix | Read as | **Slice-1 status** |
|--------|---------|-------|---------|--------------------|
| `D` | Data register | dec | word | **Supported** |
| `W` | Link register | hex | word | **Supported** |
| `R` | File register | dec | word | **Supported** |
| `ZR` | File register (serial) | **hex** | word | **Supported** |
| `M` | Internal relay | dec | bit (via word-units) | **Supported** |
| `X` | Input | hex | bit (via word-units) | **Supported** |
| `Y` | Output | hex | bit (via word-units) | **Supported** |
| `B` | Link relay | hex | bit (via word-units) | **Supported** |
| `D.b`/`W.b`/`R.b`/`ZR.b` | Word-bit (bit in a word device) | host word's radix; bit 0–F hex | bit extracted from word | **Supported** |
| `SB`,`SW`,`DX`,`DY` | Special/direct link | hex | — | **Rejected** (`DEVICE_NOT_IMPLEMENTED`) |
| `T`,`C`,`L`,`F`,`V`,`S`,`SM`,`SD`,`Z` | timers/counters/latch/special/etc. | dec | — | **Rejected** (`DEVICE_NOT_IMPLEMENTED`) |

Notes: bit devices (`M/X/Y/B`) are read with **word-units batch read** (subcmd
0000) and bit-extracted in the decoder — consistent with v2's "word reads only, no
subcmd-0001 bit-batch-read." Word-bit reads the containing word and extracts the
bit. Radix per Mitsubishi SH-080008 (pinned by golden tests once §4 RC-1 is met).

---

## 4. Gates (adjusted at approval — what blocks what)

The reality-check gate (RC-1) is split into two by approval, so capture work
doesn't needlessly block backend coding:

1. **Before building the runtime slice** — **discovery Part A Q1/Q3**, *or* explicit
   customer confirmation that the target **remains modern MC 3E binary over TCP**.
   This prevents building the wrong runtime slice. (If the customer comes back
   needing 4E/1E/ASCII/UDP, that re-scopes before code.)
2. **Part B known-good capture** is **required before field-verified / release
   signoff** and before any golden-byte test may be called **customer-verified**.
   It does **NOT** block *starting* Slice 1 backend implementation — that proceeds
   against **spec-derived fixtures clearly labelled "field-unverified"** (RC-1).
3. **Sony merge-order — CONFIRMED** (RC-7): the retirement foundation
   (`ISourceRetirement`, `AdapterRetirementOperation`) is **already on master**
   (slice-0 commit 3.0). Sony's `Sony_Development` is building **EtherNet/IP** and
   touches the same shared Host files (`LicenseModuleKeys`, `RegistrationFactory`,
   `BundleRedactionRulesRegistration`, `EdgeConnectComposition`, `Host.csproj`) —
   **additive edits**, coordinate at merge; new MELSEC project files don't conflict.
   MELSEC consumes `ISourceRetirement` only, no generation logic.
4. **License tier** — `source-melsec` = **Premium** (approved). Recorded.
5. **v3 + timer policy — APPROVED** (ceil-up, §2.1).

## 5. What's unchanged and carried from v1/v2

- Architecture/locks, file-by-file deliverables, backend integration touch-points,
  sequencing — **v1 §2/§3/§5/§8**.
- Slice-1 = MC 3E binary / TCP / **read-only**; other modes config-accepted but
  validation-rejected — **v2 Δ2/Δ7**.
- Per-tag `WordOrder` (default low-word-first), word-bit addresses, profile-aware
  caps, required loopback SLMP fake fixture — **v2 Δ3/Δ4/Δ6/Δ9**.
- Test categories — **v1 §6 + v2 §5**, plus the §2 tightening tests above.
- Gate: full solution 0/0; run **entire** `Host.Tests` + `Management.Tests`
  (not topic-filtered) before any PR.

---
*On approval: confirm RC-7 (Sony), confirm §2.1 timer policy, draft ADR-0033 (§2.5),
then implement per v1 §8 with all v2/v3 corrections folded in. Implementation
remains gated until then.*
