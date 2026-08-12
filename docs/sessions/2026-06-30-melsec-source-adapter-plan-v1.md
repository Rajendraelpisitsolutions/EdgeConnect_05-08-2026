# Mitsubishi MELSEC source adapter — Plan v1

**Date:** 2026-06-30
**Status:** Plan v1 — for ChatGPT review pass (→ v2 → reality-check → v3)
**Driver:** Customer requirement is back; wants ASAP. MELSEC was deferred on
2026-05-28/29 ("MELSEC and S7 deferred to their own future plan trail," customer
signed off). This starts it cold — no project, no prior ADR.

## 0. User decisions locked at kickoff (2026-06-30)

These came from the kickoff scoping questions and are **not** open for relitigation
without explicit user sign-off:

1. **Default target:** SLMP / MC Protocol **3E binary over TCP** — but keep
   **protocol/transport configurable** (PLC model, Ethernet module, TCP vs UDP,
   port, MC frame mode) until the customer confirms their hardware. We do not
   hard-code a single device family.
2. **I/O layer:** **hand-rolled in pure C#**, **no third-party Mitsubishi
   dependency**. This mirrors exactly why S7 chose Sharp7 (MIT, pure-C#, no native
   dep) — but for MELSEC there is no equally-clean MIT library, so we own the wire.
   (HslCommunication is the popular option but carries commercial-license strings;
   rejected for a shipped product.)
3. **Delivery shape: backend-first.** Ship, in order: connection config → read
   polling → address parsing → value decoding → health/diagnostics → Host DI
   integration. **Wizard tile and demo mode follow in a later slice.**

> **"Done" reminder (CLAUDE.md §8):** operator-available = backend + tests + Host DI
> **+ an Available wizard tile**. This plan's backend-first slice is therefore
> **explicitly NOT "done"** by the project's bar — it is Slice 1 of (at least) two.
> We say so out loud rather than letting backend-only read as shippable.

## 1. What MELSEC is, and the one place S7 stops being a clean template

**MELSEC** is Mitsubishi's PLC family. Field devices speak **SLMP** (the modern
unified protocol, iQ-R/iQ-F and newer) or its predecessor **MC Protocol** (3E/4E
frames on Q/L-series, 1E frames on legacy A-series), over **TCP or UDP**, in
**binary or ASCII** encoding. The 3E-binary-over-TCP subset is what the large
majority of in-field Mitsubishi Ethernet PLCs speak and is our default target.

`Sources.S7` is the right structural template for **everything except the wire
layer**: same `ISourceAdapter` + `ISourceRetirement` shape, same
config-record/`FromSourceInstance` pattern, same scan-planner → block-read →
decode → emit-canonical loop, same connection-manager + circuit-breaker, same
ADR-0020 connection-key redaction, same ADR-0015 Rule 6/9 carve-out (MELSEC is
**operator-defined tag lists only — no browse service**, confirmed in ADR-0015
and the pilot plans).

The wire layer is where we cannot copy: S7 wraps Sharp7; **we hand-roll SLMP/MC
framing**. That is the novel risk and the bulk of the new design below (§4).

## 2. Architectural fit / locks honored

- **Protocol-agnostic Core** (lock #1): all new code lives in a new
  `ElpisEdgeConnect.Sources.Melsec` assembly that references Core only.
- **Canonical model** (lock #2): decoded values become `CanonicalDataPoint` via
  `CanonicalDataPointFactory`, exactly as S7 does. No sink-specific formatting.
- **Modular, license-gated DI** (lock #4, #5): new license key `source-melsec`,
  gated at registration time. No dynamic plugin loading.
- **Per-adapter isolation** (lock #10): one MELSEC instance failing never affects
  others — inherited from the supervisor + connection-manager pattern.
- **ADR-0015 wizard contract:** MELSEC wizard (later slice) is browse-exempt;
  documents the absence with an Info alert (Rule 6 carve-out), same as S7.
- **Source-generation lease (slice-0 spec, 2026-06-25):** the adapter **rides**
  the supervisor's generation/retirement primitive by implementing
  `ISourceRetirement` correctly (revoke/detach before cancel). It must **not**
  embed any generation logic itself, and must **not** opt into replacement
  overlap (slice-0 §8 default: no overlap until proven). This keeps us out of
  Sony's in-flight runtime-reconfigure workstream's way.

## 3. Backend slice — file-by-file deliverables

New project: **`src/ElpisEdgeConnect.Sources.Melsec/`** (csproj mirrors
`Sources.S7.csproj`: net8, nullable, `TreatWarningsAsErrors`, doc file,
`InternalsVisibleTo` the test + integration projects; **no PackageReference for
any Mitsubishi library** — that's the whole point).

| File | Purpose | Template |
|------|---------|----------|
| `MelsecSourceConfiguration.cs` | `sealed record : SourceConfiguration`; `ProtocolNameConstant="melsec"`, `LicenseModuleKey="source-melsec"`; connection + transport + frame knobs (§3.1); `FromSourceInstance` projection | `S7SourceConfiguration.cs` |
| `MelsecConnectionKeys.cs` | JSON key constants + `All` list (ADR-0020 drift guard) | `S7ConnectionKeys.cs` |
| `MelsecBundleRedactionRules.cs` | `IBundleRedactionRules` — protocol name + `KnownKeys` tiers | `S7BundleRedactionRules.cs` |
| `IMelsecClient.cs` | Thin transport abstraction (Connect/Disconnect/ReadDeviceBlock) + `MelsecOperationResult` | `IS7Client.cs` |
| `SlmpClient.cs` | **Hand-rolled** SLMP/MC client over `TcpClient`/`UdpClient` (§4) | `Sharp7Client.cs` (structure only) |
| `MelsecAddress.cs` + `MelsecAddressParser.cs` | Device code + number parsing, per-device radix (§4.3) | `S7Address.cs` |
| `MelsecDeviceCode.cs` | Device enum (D, W, M, X, Y, B, R, ZR, …) + binary code table + word/bit classification | `S7MemoryArea.cs` |
| `MelsecDatatype.cs` | Datatype enum + parser + canonical-type mapping | `S7Datatype.cs` |
| `Decoding/MelsecDecoder.cs` | Word-buffer → typed value; MELSEC endianness/word-order (§4.4) | `S7Decoder.cs` |
| `Scanning/MelsecScanPlan.cs` + `MelsecScanPlanner.cs` | Coalesce contiguous same-device word ranges into batch reads; respect per-request point cap | `S7ScanPlan*.cs` |
| `MelsecConnectionManager.cs` | Connect/backoff/circuit-breaker; `WaitForWireIdleAsync` for retirement | `S7ConnectionManager.cs` |
| `MelsecSourceAdapter.cs` | `ISourceAdapter, ISourceRetirement`; Initialize/Start/Poll/Stop/Health; per-tag quality state machine | `S7SourceAdapter.cs` |
| `Retirement/MelsecRetirement.cs` | static `Begin(...)` helper | `Retirement/S7Retirement.cs` |

**Deferred to later slice (not in this slice):** `Import/` CSV importer, demo
client + `MelsecDemoModeOptions`, the Blazor wizard, the `SourceProtocolPicker`
tile. (Demo mode would get its own ADR mirroring ADR-0029 S7 demo mode.)

### 3.1 Config knobs (kept configurable per decision #1)

Connection: `Host`, `Port` (no universal default — Mitsubishi modules are
user-configured; validate required, common values 5000/5001/5006/6000 documented
not defaulted), `TransportProtocol` (`Tcp`|`Udp`, default `Tcp`),
`FrameMode` (`Mc3EBinary` default | `Mc3EAscii` | `Mc1EBinary` | `Mc4EBinary`),
`NetworkNo` (default 0x00), `PcNo` (default 0xFF), `RequestDestModuleIoNo`
(default 0x03FF), `RequestDestModuleStationNo` (default 0x00), `MonitoringTimerMs`.
Timeouts/retry/backoff/breaker: copy S7's set verbatim (same semantics).
Scan-planner: `MaxGapWords`, `MaxPointsPerRequest` (3E binary word-read cap is
**960 words**; default conservative, e.g. 480). Tag set: `TagDefinitions`
(name, address, datatype, scanRateMs, unit, scale, offset) — identical shape to S7.

## 4. Hand-rolled SLMP/MC wire layer (the novel risk)

This is the part with no template. Design notes for review:

### 4.1 3E binary request frame (batch read, command 0x0401)
`subheader(0x5000)` · `networkNo` · `pcNo` · `reqDestModuleIoNo(2,LE)` ·
`reqDestModuleStationNo` · `requestDataLength(2,LE)` · `monitoringTimer(2,LE)` ·
`command(0x0401,LE)` · `subcommand(0x0000 word | 0x0001 bit, LE)` ·
`headDevice(3,LE)` · `deviceCode(1)` · `devicePointCount(2,LE)`.
**4E** prepends a 2-byte serial + 2-byte reserved and the response echoes the
serial (request/response matching). **1E** is a different layout entirely (legacy
A-series) — implement behind `FrameMode` but mark 1E/ASCII as "config-time
selectable, validated, but field-unverified until a customer needs it."

### 4.2 Response parsing
`subheader(0xD000)` · header echo · `responseDataLength(2,LE)` ·
`endCode(2,LE)` then payload. `endCode==0` → success; non-zero → map to
`MelsecOperationResult.Fail(endCode, text)` with a code table
(`CORE`-style `MELSEC.*` error catalog entries). Transport faults (socket
closed/timeout) classified `ErrorCategory.Network`; non-zero end codes
`ErrorCategory.Protocol` — same split S7 uses.

### 4.3 Address parsing — the classic MELSEC gotcha (high test priority)
Device number **radix depends on device type**: `X`/`Y`/`B`/`W`/`SB`/`SW` are
**hex**; `D`/`M`/`R`/`T`/`C`/`L`/`ZR` are **decimal**. Parser must encode this
per-device, and validation must reject out-of-range / wrong-radix at config load
(mirror S7's "pre-parse every tag at Initialize" so operators see errors at load,
not first poll). This is the #1 source of silent wrong-data bugs in MELSEC
integrations — it gets dedicated `Theory` coverage.

### 4.4 Decoding & endianness
MELSEC word data is **little-endian**; 32-bit values (DInt/DWord/Float) span two
consecutive words, **low word first** (`value = w[n] | w[n+1]<<16`). Bit devices
read in word units pack 16 bits/word (bit n of word). Decoder must handle:
Bool, Int16, UInt16, Int32, UInt32, Float32, (Float64/strings later). Scale/offset
applied exactly as S7 (`ApplyScaleOffset`).

### 4.5 Transport
`Tcp` via `TcpClient` (keep-alive between scans like S7's `KeepAlive`); `Udp` via
`UdpClient` (connectionless — no persistent session; each read is request/await
datagram with the monitoring timer as the bound). Single-threaded per instance
(adapter serializes), same contract note as `IS7Client`.

## 5. Backend integration touch-points (from recon — verify line numbers at impl)

New files:
- `src/ElpisEdgeConnect.Host/Adapters/MelsecRegistrationExtensions.cs`
  (`AddMelsecSource*`, `IsMelsecProtocol`, resolve/build helpers, license gate)

Existing files to modify:
- `src/ElpisEdgeConnect.Core/Licensing/LicenseModuleKeys.cs` — add
  `SourceMelsec = "source-melsec"`
- `src/ElpisEdgeConnect.Host/Adapters/RegistrationFactory.cs` — add the
  `IsMelsecProtocol` dispatch branch (alongside the S7 branch)
- `src/ElpisEdgeConnect.Host/EdgeConnectComposition.cs` — eager
  `AddMelsecSourcesFromGatewayConfig(...)` at startup (next to the S7 call)
- `src/ElpisEdgeConnect.Host/Adapters/BundleRedactionRulesRegistration.cs` —
  `AddSingleton<IBundleRedactionRules, MelsecBundleRedactionRules>()`
- `docs/licensing/module-catalog.md` — add `source-melsec` row (tier TBD §7)
- `ElpisEdgeConnect.sln` — add the new src + test projects

**Cross-cutting test that will FAIL until MELSEC is wired** (good — it's a drift
guard): `tests/ElpisEdgeConnect.Host.Tests/BundleRedactionRulesRegistrationTests.cs`
enumerates every adapter's redaction rules; add `melsec` to its expected set.

Schema validation needs **no** new file: `SourceInstanceConfig.Connection` is an
opaque `JsonElement`; `ConfigurationSchemaFactory` reflects the record. (Verify at
impl that no test enumerates concrete config types and needs MELSEC added.)

> Recon line numbers (e.g. LicenseModuleKeys.cs:68, RegistrationFactory.cs:86-89)
> are from a sub-agent sweep and are **approximate** — confirm each at edit time.

## 6. Test plan (backend slice)

New project `tests/ElpisEdgeConnect.Sources.Melsec.Tests/` (xUnit + FluentAssertions
+ NSubstitute), mirroring `Sources.S7.Tests`:
- **Frame codec round-trip** — build request bytes for known (device, head, count)
  and assert exact bytes; parse canned response bytes (success + each end-code).
  Golden-byte tests against the SLMP spec examples.
- **Address parser** — `Theory` over per-device radix (X/Y/B hex, D/M/R decimal),
  range limits, malformed input → typed validation failure.
- **Decoder** — endianness/word-order for Int32/Float32, bit unpacking, scale/offset.
- **Scan planner** — coalescing contiguous words, gap cap, per-request point cap
  splitting, mixed device types not coalesced across device boundaries.
- **Adapter lifecycle** — Initialize/Start/Poll/Stop against a fake `IMelsecClient`;
  per-tag Good/Uncertain/Bad quality state machine; Degraded↔Running transitions;
  partial-block success accounting.
- **Retirement** — `BeginRetirement` idempotent; revoke/detach-before-cancel
  ordering; `WaitForWireIdleAsync` honored (mirror `S7SourceRetirementTests`).
- **Config projection** — `FromSourceInstance` round-trips every knob incl.
  transport/frame mode; defaults applied; missing host rejected.
- **Host wiring** — `RegistrationFactory.BuildSource_Melsec_*` happy path;
  redaction-registration drift test updated and green.
- Integration: a **loopback SLMP server fake** (in-proc TcpListener speaking 3E
  binary) under `Integration.Tests` — no real PLC, mirrors the mock-adapter rule.

**Gate:** full solution 0 warnings / 0 errors; run the **entire**
`Management.Tests` + `Host.Tests` projects (not topic-filtered — per the
filtered-run-misses-cross-cutting-guards lesson) before any PR.

## 7. Open questions for the review pass / user

1. **License tier for `source-melsec`** — opcua-client and ethernet-ip are Pro+.
   Recommend **Pro+ (Premium)** for parity. Confirm.
2. **Customer hardware** — still unconfirmed (PLC model, Ethernet module, TCP/UDP,
   port, frame mode). Default 3E-binary/TCP proceeds; **a field-discovery
   questionnaire** (mirror the Modbus RTU discovery package, 2026-06-23) should go
   to the customer in parallel so v2 can pin the validated subset.
3. **ADR needed?** Propose **ADR-0033 — "MELSEC: hand-rolled SLMP client, no
   third-party dependency; transport/frame mode configurable"** (locks decisions
   #1/#2 so they're not relitigated). Write it when the user approves this plan.
4. **Slice boundary** — confirm backend slice ends at "Host DI + green tests,
   instantiable from gateway.json," with wizard + demo mode as Slice 2 (which will
   need a **static HTML mockup** for operator sign-off before any Razor, per
   standing UX rule).
5. **Sony coordination** — the source-generation lease foundation is in flight on
   her runtime-reconfigure workstream. MELSEC only *consumes* `ISourceRetirement`;
   confirm no merge-order dependency before we branch.

## 8. Proposed sequencing (backend slice)

1. Project skeleton + csproj + sln + config record + connection keys + redaction
   rules + license key. (Compiles, registered, redaction drift test green.)
2. Wire codec (`SlmpClient` + `IMelsecClient`) + frame round-trip tests.
3. Address parser + device codes + decoder + their tests.
4. Scan planner + tests.
5. Adapter (lifecycle + quality + retirement) against fake client + tests.
6. Host DI (`MelsecRegistrationExtensions` + `RegistrationFactory` dispatch +
   eager registration) + wiring tests + loopback integration fake.
7. Full-solution gate; update `module-catalog.md` + CLAUDE.md §8 current-state.

Each step is its own commit; PR opened after step 7 gate is green (push+PR per
standing autonomy rule; merge remains user's call).

---
*Next: user/ChatGPT review → v2. Do not start implementation until v3 (or explicit
user go-ahead).*
