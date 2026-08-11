# General MELSEC Compatibility Roadmap — plan v2

**Date:** 2026-07-03
**Status:** v2 (post-review; supersedes v1 dated 2026-07-02 — v1 deliberately not committed)
**Owner:** EdgeConnect / MELSEC adapter
**Relation to ADRs:** ADR-0033 remains the Slice-1 decision record; §11 recommends a
new ADR for the profile-matrix strategy.

**Plan trail:** v1 → review (this pass, 2026-07-03) → **v2**. Review directives
incorporated: evidence-ladder softening, implemented/supported/qualified/certified
separation, tiered "all MELSEC" claim, expanded profile schema, status table,
Phase 0 tooling, iQ-F reframe, config migration guarantee, manual source-of-truth,
UI claim language, ADR recommendation.

---

## 0. Reframe — what MELSEC support actually is

The product goal is a **general Mitsubishi driver**: EdgeConnect collects from
Mitsubishi PLCs that expose MELSEC / MC Protocol / SLMP communication through a
**compatibility/profile matrix** — Kepware-style multi-model support, not one-off
customer support.

- **Slice 1 is not the final MELSEC product scope.** It is the *first supported
  profile*: MC 3E binary over TCP, read-only.
- **Field data is not a per-customer release gate.** The driver is shipped and
  operator-available; it waits on no customer.
- **Per-customer input is only the tag list** (which devices/addresses to read) —
  ordinary driver configuration, never the customer's ladder program.
- The FX5U-32MT (first customer field device, MELSEC iQ-F) is our **first
  field-qualification device**, not a bespoke integration.

### Evidence model (corrected from v1)

v1 overstated "one capture certifies a whole profile/family." The correct ladder:

1. **Manuals define support.** The official Mitsubishi manual (exact document +
   revision, §9) is the implementation source for every profile.
2. **Golden tests verify implementation.** Hand-encoded request/response byte
   vectors from the manual, pinned in unit tests.
3. **Simulator verifies internal behavior.** The standalone sim proves the adapter
   and our spec reading agree end-to-end — internal consistency only.
4. **Real captures qualify representative device/profile combinations.** A capture
   is *profile-level qualification evidence* for the tested device — **not**
   universal proof for every CPU/module/firmware in that family.

Broad claims come from **coverage across the matrix** (§5), never from one capture.

### Support-level definitions (used throughout)

| Level | Meaning |
|---|---|
| **Implemented** | Code exists and passes unit/golden/sim tests |
| **Supported** | Exposed to operators in the Studio with validation (wizard + config gate) |
| **Field-qualified** | Confirmed against ≥1 real PLC or known-good capture for that profile |
| **Certified / released** | Enough model/profile coverage (multiple representative devices) to make a broad public claim for that family |

**Current Slice 1 status: Implemented + Supported, field-unverified** (golden bytes
spec-derived from SH(NA)-080008; no real-silicon confirmation yet).

---

## 1. Current supported profile (shipped — Slice 1)

| Dimension | Value |
|---|---|
| Frame / encoding / transport | MC **3E / binary / TCP** |
| Direction | **Read-only** |
| Command | Batch read, word units (`0x0401` / subcmd `0x0000`) |
| Devices | `D`, `W`, `R`, `ZR`, `M`, `X`, `Y`, `B` + word-bit forms (`D100.3`) |
| Radix (current impl) | decimal: `D R M`; hex: `W ZR X Y B` |
| Max points / request | 960 words (pinned "Modern = Q/L/iQ-R" profile) |
| Browse / writes | none / none |
| Profile gate | `DeviceProfile.Modern` only; other modes config-accepted but validation-rejected (`CONFIG_MODE_NOT_IMPLEMENTED`) |
| Support level | **Implemented + Supported; NOT field-qualified** |

---

## 2. What "all Mitsubishi PLC models that support MELSEC" means (tiered claim)

"All MELSEC" is **all profiles in the compatibility matrix**, not one universal frame.

- **Tier 1 — primary target:** Ethernet **SLMP / MC-capable** PLCs (iQ-R, Q, L,
  iQ-F/FX5, and QnA where its Ethernet module speaks a supported MC frame).
- **Tier 2 — legacy, separately scoped:** A-series / QnA / FX legacy reachable only
  via **MC 1E** or **serial / computer-link**. These are distinct frames and (for
  serial) a distinct physical transport — supported only if explicitly scoped
  (Phases E/G), never implied by Tier-1 claims.

### Target model matrix

| Family | Comms path | Roadmap intent |
|---|---|---|
| **iQ-R** | SLMP / MC 3E, 4E over Ethernet | Phase A profile (primary) |
| **Q series** | MC 3E (QnUDE etc.), SLMP | Phase A profile* |
| **L series** | MC 3E, SLMP | Phase A profile* |
| **iQ-F / FX5** | SLMP over built-in Ethernet | **Phase B** — dedicated profile from the iQ-F manual |
| **QnA** | where its Ethernet/MC module supports a covered frame | Later; verify per manual |
| **A / FX legacy** | MC 1E or serial/computer-link only | Tier 2 — Phases E/G if demanded |

\* Review resolution: iQ-R/Q/L **start as one profile only if their manuals and
point caps align**; if device ranges/caps diverge, split into sub-profiles. This is
a Phase-A verification task, not an assumption.

---

## 3. Frame / encoding / transport matrix

| Frame / encoding / transport | Status | Phase | Notes |
|---|---|---|---|
| **3E / binary / TCP** | Implemented + Supported | A (qualify) | current profile |
| 3E / ASCII / TCP | not implemented | E | ASCII brings device-number modes incl. X/Y OCT vs HEX |
| 4E / binary / TCP | not implemented | D | request serial number → correlation + parallelism |
| 4E / ASCII / TCP | not implemented | E | |
| 1E / binary+ASCII | not implemented | E (conditional) | legacy fleets only |
| UDP (any frame) | not implemented | G | **separate reliability design** (retransmit/dedup/ordering), not a flag on TCP |
| Serial / computer-link | not implemented | G (conditional) | separate adapter; different framing + physical layer |

---

## 4. Profile registry schema (expanded)

Profiles are **data** (a registry), not scattered conditionals — adding a family is
"fill a profile + tests," not codec surgery. Each profile record specifies, sourced
from the named manual (§9):

**Identity & envelope**
1. PLC family (e.g. iQ-R, iQ-F/FX5)
2. CPU/module model family covered (e.g. FX5U/FX5UC built-in Ethernet)
3. Frame type: 3E / 4E / 1E
4. Encoding: binary / ASCII
5. Transport: TCP / UDP / serial
6. Route-header defaults (network, PC, dest module I/O, dest station)

**Wire shape**
7. Device code table (on-wire code byte(s) per device for binary; mnemonics for ASCII)
8. Device-code width/format (1-byte vs 2-byte codes where applicable)
9. Head-device field width (3-byte vs 4-byte head-number encodings)
10. Device number radix — per device, **per frame/encoding**
11. Bit-device word-read alignment rule (points-per-word, head alignment)

**Limits & capabilities**
12. Supported device ranges (valid head-number bounds per device)
13. Max batch read/write points **by command and access unit** (word vs bit)
14. Supported commands (batch read; later: random read, batch/random write)
15. Special devices: `SM`, `SD`, `SB`, `SW`, timers (`T`), counters (`C`), retentive timers
16. File registers & extended devices (`R`, `ZR`, block switching where relevant)
17. Word order defaults / presets for 32-bit values (operator-overridable)
18. Known end-code mappings (family-specific codes + descriptions)

**Provenance**
19. Tested manual: exact document number + revision (§9)
20. Certification evidence: links to captures / qualification records per §5

---

## 5. Compatibility-status table (living table — the release gate for claims)

Statuses per §0 definitions. This table (or its successor in `docs/`) is the single
place "what do we support?" gets answered from.

| Family/Profile | Frame/enc/transport | Devices covered | Implemented | Sim-tested | Real capture | Certified | Notes / limits |
|---|---|---|---|---|---|---|---|
| Modern (iQ-R/Q/L, pinned) | 3E/bin/TCP | D W R ZR M X Y B **+ SM SD SB SW (A-3a) + TS/TC/TN STS/STC/STN CS/CC/CN (A-3b)** + word-bit | ✅ | ✅ (loopback + standalone sim) | ❌ | ❌ | 960-word cap pinned; read-only; Supported in UI ✅ |
| iQ-F / FX5 | 3E/bin/TCP | D W R M X Y B **+ SM SD SB SW (A-3a) + TS/TC/TN STS/STC/STN CS/CC/CN (A-3b)** + word-bit (no ZR — FX5 CPU cannot access it) | ✅ (registry, PR #171) | ✅ (sim `--profile fx5`, Gate A-2S) | ❌ pending hardware (customer FX5U-32MT known; no test access) | ❌ pending broader coverage | **Supported in UI: ✅ (profile selector shipped, Gate A-2O)**. X/Y operator labels octal, binary wire numeric (SH-082625ENG-J §38.2); 960-word cap confirmed; Field-qualified stays pending hardware |
| QnA | TBD | TBD | ❌ | ❌ | ❌ | ❌ | verify Ethernet-module frame support |
| A/FX legacy | 1E or serial | TBD | ❌ | ❌ | ❌ | ❌ | Tier 2, demand-gated |
| Any / 4E | 4E/bin/TCP | — | ❌ | ❌ | ❌ | ❌ | Phase D |
| Any / ASCII | 3E-4E/ASCII/TCP | — | ❌ | ❌ | ❌ | ❌ | Phase E; X/Y OCT-vs-HEX modes live here |
| Writes (any) | — | — | ❌ | ❌ | ❌ | ❌ | Phase F, safety-gated |

---

## 6. Qualification strategy

1. **Manuals define support** — official Mitsubishi documents, named per profile (§9).
2. **Golden-frame tests per profile** — manual-derived byte vectors in unit tests.
3. **Simulator tests** — the standalone sim (§7 Phase 0) per profile. The sim encodes
   *our* assumptions: it proves internal consistency, **never** field truth.
4. **≥1 real PLC or known-good capture per profile** → *Field-qualified* for that
   profile; **multiple representative devices** across a family → *Certified*.
5. **Never require a customer's ladder program.** Tag list + a capture of a handful
   of known reads only.

### Field Qualification Package (FQP)

The canonical repo artifact is
`docs/sessions/2026-06-30-melsec-discovery-package.md` (a PDF export may be
generated from it as an optional sendable copy; the markdown is the source of
truth). Purpose reframed: **not** "waiting on the customer to release MELSEC" — it
is the **field qualification package / real-PLC certification capture** request. Per
device it collects: PLC/CPU model, Ethernet module + SLMP connection settings, frame
mode, binary/ASCII, TCP/UDP + port, route fields, representative tag list, word
order, and a known-good request/response capture. Evidence is reusable across every
future customer on that profile.

---

## 7. Roadmap phases

| Phase | Scope | Exit |
|---|---|---|
| **0 — Tooling** | **Standalone MELSEC simulator** (proposed in **pending PR #165**: `tests/ElpisEdgeConnect.Integration.Tests/MelsecSimulator/`, Python stdlib). Dev/test tooling ONLY: drives Studio test-connection/test-read/diagnostics without hardware; speaks MC 3E binary TCP first; extend per profile as phases land. **Not a substitute for real-PLC qualification.** | PR #165 merged + sim exercised against the Studio (already done in dev) |
| **A** | **Qualify the current 3E-binary-TCP profile** (Modern pin). Real capture(s), golden-frame parity, confirm 960 cap + device ranges; resolve whether iQ-R/Q/L stay one profile or split (per §2). | Profile *Field-qualified* against ≥1 real device |
| **B** | **iQ-F / FX5 profile** — built from the official iQ-F SLMP manual (§9), certify against the FX5U-32MT. See verification checklist below. | FX5 profile Implemented + Supported + Field-qualified; profile selector ships (§8) |
| **C** | **Device breadth** — `SM`, `SD`, `SB`, `SW`, timers, counters (+ per-family extended devices). | Devices added with golden + sim tests |
| **D** | **4E binary / TCP** — request serial numbers, correlation, parallelism. *Review resolution: comes after B and C unless field demand requires parallelism earlier.* | 4E field-qualified on ≥1 profile |
| **E** | **ASCII / 1E** — only if a legacy fleet requires it; ASCII includes X/Y OCT/HEX device-number modes. | Demand-gated |
| **F** | **Writes** — later, behind safety gates (per-tag opt-in, confirmation, audit; batch/random write commands). | Write path field-qualified + gated |
| **G** | **UDP / serial** — separately scoped transports with their own reliability/framing designs. | Demand-gated |

### Phase B — iQ-F/FX5 verification checklist (manual-driven, no assumptions)

Do **not** frame FX5 as an "octal problem." From the official iQ-F SLMP/MC manual,
verify each of:

- device availability and valid ranges (which of D/W/R/ZR/M/X/Y/B exist, plus FX5-specific devices);
- **X/Y wire notation per frame/encoding** — for ASCII there are X/Y-OCT and X/Y-HEX
  modes; for **binary** the manual indicates **hexadecimal**. GX Works3's octal I/O
  *display* is a tooling convention, not wire encoding. Encode whatever the manual
  says for the exact frame+encoding in use;
- built-in Ethernet / SLMP connection settings (port is operator-configured; no universal default);
- max points per request (do not carry the 960 "Modern" cap over unverified);
- `R` / `ZR` availability and semantics vs iQ-R;
- bit-device word-read alignment rule;
- word-order expectations for 32-bit values.

---

## 8. UI changes

- **PLC family / profile selector** in the wizard once >1 profile exists; frame/
  transport choices shown **per profile** (only supported combinations selectable).
- **Migration guarantee:** existing Slice-1 `melsec` sources **must keep working
  unmodified** — on hydrate, a config with no profile field maps to the current
  default profile (Modern / 3E binary TCP). The profile selector must not break or
  re-prompt existing configured sources; edit opens them with the default profile
  pre-selected.
- **Validation explains unsupported combinations** — e.g. "iQ-F + 1E is not
  supported; choose 3E binary" — extending the `CONFIG_MODE_NOT_IMPLEMENTED`
  pattern; never a silent reject.
- **Claim language:** the UI must **not** say "supports all Mitsubishi PLCs" until
  enough profiles are *Certified* (§5). Use profile-aware text: "MELSEC / MC 3E
  binary TCP profile", "iQ-R/Q/L profile", "iQ-F/FX5 profile"; unsupported
  combinations state *what is missing*, not just "unsupported".
- **Manual tag entry stays.** MELSEC **browse remains out** unless a real
  Mitsubishi label/import mechanism (e.g. GX Works3 label export) is separately
  scoped — there is no generic MELSEC browse. *(Review resolution: confirmed.)*
- Diagnostics panel header gains profile/frame context.

---

## 9. Manual / source-of-truth section

Every profile names the **exact Mitsubishi manual (document number + revision)** it
was implemented and verified against, recorded in the profile registry (§4 item 19).
**No profile is marked Field-qualified or Certified from memory or from generic
"supports MELSEC" statements.**

| Profile | Source manual (to be pinned with revision at implementation time) |
|---|---|
| Modern 3E binary (current) | SH(NA)-080008 (MC Protocol reference) — revision to be pinned in Phase A |
| iQ-F / FX5 | iQ-F SLMP reference (e.g. JY997D56001 family) — exact doc + revision pinned in Phase B |
| SLMP general | SH(NA)-080956 (SLMP reference) — as cross-check |

(Exact document numbers are confirmed and pinned when the phase starts; the table
records the *obligation*, the registry records the *fact*.)

---

## 10. Open-question resolutions (from v1 §review)

| v1 open question | v2 resolution |
|---|---|
| One profile for iQ-R/Q/L? | One profile **only if manuals + caps align**; else sub-profiles (Phase A verifies) |
| 4E priority | After iQ-F/FX5 (B) and device breadth (C), unless field demand for parallelism arrives earlier |
| Writes | Remain a later, safety-gated phase (F) |
| Browse | Out, unless a real Mitsubishi label/import mechanism is separately scoped |
| 960 cap on iQ-F? / ZR on FX5? | Explicit Phase-B checklist items — verify from manual, never carried over |
| Profile registry location | Inside `Sources.Melsec` (Core stays protocol-agnostic); decided at Phase-B design |
| New ADR? | **Yes — see §11** |

---

## 11. ADR recommendation

Author a **new ADR** when Phase A/B implementation is approved:

- **ADR-0033 stands** as the Slice-1 decision record (hand-rolled wire, Slice-1
  device set, 3E-binary-TCP pin).
- The **new ADR** defines the **general MELSEC profile-matrix strategy**: profiles
  as data, the support-level ladder (Implemented → Supported → Field-qualified →
  Certified), the tiered "all MELSEC" claim, manual-revision pinning, and the
  qualification evidence rules — extending/superseding ADR-0033's *scope note*
  without reopening its wire-layer decisions.

---

## Scope discipline (unchanged)

This is a **plan**, not an implementation authorization. The Slice-1 freeze holds
until a phase is explicitly approved. No writes / UDP / 4E / 1E / ASCII / browse /
demo / CSV land without sign-off on the corresponding phase. This v2 is committed as
a **docs-only** change; v1 remains uncommitted by direction.
