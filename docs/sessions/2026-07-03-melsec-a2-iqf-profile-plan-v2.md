# MELSEC A-2 — iQ-F / FX5 profile preparation — plan v2

**Date:** 2026-07-03
**Status:** v2 (post-review; supersedes v1 — v1 deliberately not committed)
**Parent:** Phase A plan v2 (§2 A-2) + roadmap v2 §7 Phase B checklist (both on master)
**Mode:** manual-driven + simulator-tested ONLY (no hardware; Gate B deferred)

**Plan trail:** v1 → review (2026-07-03) → **v2**. Review directives incorporated:
manual *bundle* pinning + A-2.0 discovery step, explicit gate ladder
(A-2D/A-2I/A-2S/A-2O/A-2H), static-C#-records registry decision, ADR-0034 scope
and timing, pure-data expectation with byte-identity tests, sim CLI-flag+env
mode, device-breadth discipline, FQP-as-docs-outcome, and the honest status
ceiling given the excluded profile selector.

---

## 0. Goal and hard boundaries

Prepare the **iQ-F / FX5 profile** so the MELSEC driver is FX5-ready the moment
hardware access exists — built strictly from officially pinned Mitsubishi manuals,
proven by golden vectors and the standalone simulator.

**Excluded without separate approval:** writes, UDP, 4E, 1E, ASCII, browse, demo
mode, CSV import, **profile-selector UI**. **Never:** change shipped Modern
behavior; mark anything Field-qualified or Certified.

### Status ceiling after A-2 (honest by construction)

| Level | iQ-F/FX5 after A-2 |
|---|---|
| Implemented | **yes**, if registry/tests land (Gate A-2I) |
| Simulator-tested | **yes**, if sim FX5 mode passes (Gate A-2S) |
| Supported in UI | **pending profile-selector approval** (Gate A-2O, deferred) |
| Field-qualified | **pending hardware** (Gate A-2H, deferred) |
| Certified | **pending broader hardware coverage** |

---

## Gates

| Gate | Meaning | Exit |
|---|---|---|
| **A-2D — Desk audit complete** | Manual bundle pinned; FX5 fact table filled with citations; **no code yet** | Audit doc merged |
| **A-2I — Internal implementation complete** | Profile registry holds Modern + iQ-F entries; **Modern defaults and request bytes provably unchanged**; iQ-F profile testable internally; **not operator-selectable** | Full suites green + byte-identity tests |
| **A-2S — Simulator complete** | Standalone sim runs FX5 mode; iQ-F cap/device/radix rules enforced; sim self-test passes | verify self-test green |
| **A-2O — Operator support** | **Deferred** until the profile selector is separately approved AND implemented | — |
| **A-2H — Hardware** | **Deferred** until a PLC/capture exists (FQP path) | — |

---

## A-2.0 Manual discovery (new — before any fact derivation)

Search official Mitsubishi FA sources and decide which documents are
**authoritative** for each concern — do **not** assume one SLMP manual contains
every cap/device/Ethernet setting:

| Concern | Authoritative manual to be confirmed |
|---|---|
| SLMP frame/function behavior | FX5 SLMP manual (candidate below) + SH(NA)-080956ENG-N cross-check |
| MC 3E batch-read command / device tables / caps | FX5 MC-protocol manual (candidate below) |
| FX5 built-in Ethernet / open (connection) settings | FX5 Ethernet manual, or the current combined Communication manual if Mitsubishi superseded the split docs |

The **plan records the obligation; the audit records the facts** — including which
candidate turned out to be authoritative (or superseded).

## A-2.1 Pin the manual **bundle**

Candidates (each pinned like A-1 — document number, revision, publication date
verified from the PDF's own revision page, official source URL, SHA-256, local
gitignored copy in `docs/vendor-manuals/`):

1. **JY997D56001** — MELSEC iQ-F FX5 User's Manual (**SLMP**) — primary SLMP candidate.
2. **JY997D60801** — MELSEC iQ-F FX5 User's Manual (**MELSEC Communication Protocol**) — MC-protocol candidate.
3. **JY997D56201** — MELSEC iQ-F FX5 User's Manual (**Ethernet Communication**) — Ethernet/settings candidate, **or SH-082625ENG** (newer combined Communication manual) if Mitsubishi has superseded/combined these.
4. **SH(NA)-080956ENG-N** — already pinned (A-1) — general SLMP cross-check.

## A-2.2 Derive the iQ-F profile facts (from the pinned bundle; zero assumptions)

| Fact | Notes |
|---|---|
| Supported devices + codes (which of D/W/R/ZR/M/X/Y/B exist on FX5; FX5-specific devices) | May narrow the per-profile device set (e.g. if FX5 lacks ZR) |
| **X/Y device-number notation on the wire for 3E BINARY** | Verify from manual — GX Works3 octal display ≠ wire encoding |
| Frame form accepted (3E binary subcmd 0000 vs FX5-specific) | Determines pure-data vs codec work (expected: identical) |
| **Max points for 0401 word-units read** | Do NOT carry Modern's 960 over |
| R/ZR availability + semantics | File-register handling |
| Bit-device word-read alignment rule | Planner rule check |
| Word-order expectation (32-bit) | Profile default preset |
| Built-in Ethernet SLMP settings (port/open method, connection count) | Feeds FQP + helper text |
| Route-header defaults (direct connection) | Profile defaults |
| FX5 end-code specifics | Diagnostics text accuracy |
| **SM/SD/SB/SW/T/C details, if the manual reveals them** | **Record in the audit table only — do NOT implement.** Device breadth stays in A-3 unless a specific device is explicitly approved into the iQ-F minimum |

**Output (Gate A-2D):** A-2 audit doc mirroring the A-1 format.

## A-2.3 Profile registry (Gate A-2I) — **static C# records in `Sources.Melsec`**

Decision (review-approved): **static typed C# records**, not JSON embedded
resources — compile-tested, refactor-safe, analyzer-safe. Core stays
protocol-agnostic.

Registry record fields (roadmap §4 provenance included): PLC family, model family,
frame, encoding, transport, route-header defaults, **device-code width**,
**head-device field width**, per-device radix, supported device set, per-command
point caps (word/bit access units), bit-alignment rule, supported commands,
word-order default, **manual document/revision provenance**, evidence links.

Entries: **Modern** (values sourced from the A-1 audit) + **iQ-F/FX5** (from
A-2.2). Runtime keeps resolving to Modern by default; the iQ-F entry is testable
internally only (selector excluded).

**ADR-0034 — General MELSEC profile-matrix strategy** is authored in this
increment (review-approved): profile matrix, support ladder, manual pinning,
evidence rules, profiles-as-data, **existing-config migration guarantee**
(existing `melsec` sources hydrate as Modern, never re-prompted or broken).
ADR-0033 remains the Slice-1 wire/scope ADR. **ADR-0034 must not claim iQ-F is
Supported or Field-qualified before selector/hardware respectively.**

### Pure-data expectation (review-approved)

If FX5's 3E-binary wire form is identical and only caps/devices/ranges differ,
iQ-F lands as **pure profile data — the codec is not touched**. The codec changes
only if the pinned manual proves a wire-shape difference (that finding comes back
for review first). **Byte-identity tests are mandatory either way:** golden tests
proving Modern request bytes are byte-identical before/after the registry
refactor.

## A-2.4 Golden vectors + tests (Gate A-2I evidence)

- iQ-F golden vectors derived from the pinned FX5 manuals' own examples (falling
  back to SH(NA)-080956ENG-N where the FX5 doc defers), stable citations as A-1.
- Registry tests pinning caps/devices/radix per profile.
- The Modern byte-identity suite (above).

## A-2.5 Simulator FX5 mode (Gate A-2S)

**CLI flag + env var, CLI wins** (review-approved; no separate script):

```
py server.py --profile fx5        # CLI (wins)
MELSEC_SIM_PROFILE=fx5 py server.py   # env fallback; default = modern
```

FX5 mode enforces the A-2.2 cap, restricts the device set to FX5's, keeps MC 3E
binary TCP. `verify.py` gains a profile-aware self-test.

## A-2.6 Docs/status + FQP (docs outcome only)

- Compatibility table iQ-F row set to the §0 status ceiling.
- **FQP appendix (docs only):** where to find SLMP/Ethernet settings in GX Works3,
  port/open method, suggested sample tags, capture checklist. Hardware capture
  stays pending — the FQP remains ready-to-send, gating nothing.

---

## Increments

| Inc | Content | Gate reached | Ship |
|---|---|---|---|
| 1 | A-2.0 discovery + A-2.1 bundle pin + A-2.2 audit doc | **A-2D** | docs-only PR |
| 2 | A-2.3 registry + ADR-0034 + A-2.4 tests | **A-2I** | code PR; full suites (Melsec + Management full + Host) + Modern byte-identity green |
| 3 | A-2.5 sim FX5 mode + A-2.6 docs/status/FQP appendix | **A-2S** | tooling+docs PR; sim self-test green |

Each increment: own plan-step commit/PR + full-test gate. Any wire-shape
discrepancy discovered in increment 1 **stops the line** and comes back for
review before increment 2.

## Scope guard (verbatim discipline)

No profile selector in A-2 unless separately approved. No writes, UDP, 4E, 1E,
ASCII, browse, demo mode, CSV import. No change to shipped Modern behavior. No
Field-qualified or Certified marks. Device breadth beyond the approved iQ-F
minimum stays in A-3.
