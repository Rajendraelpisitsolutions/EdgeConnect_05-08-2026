# MTConnect source wizard — premise correction (reality-check before v2)

**Date:** 2026-05-31
**Status:** supersedes the framing of plan v1 + the ChatGPT review of it. Read this
before re-reviewing. The strategic call (MTConnect before S7, mockup-first,
tile-flip-last) is unchanged; the **wizard model** is different from what v1 assumed.

---

## 1. The correction

Plan v1 — and therefore the ChatGPT review — assumed an **OPC-UA-style** wizard:
operator browses the agent's full `/probe` dataItem tree and **selects/renames
arbitrary dataItems**. That is **wrong for this adapter.**

The MTConnect adapter is **FOCAS2-style: a fixed, semantic canonical tag map.**
Evidence in the tree:

- `MTConnectSourceAdapter.BrowseTagsAsync()` returns a **fixed static list** of
  canonical tags (`status/run_state`, `status/controller_mode`,
  `status/emergency_stop`, `program/main_program`, `spindle/speed`, `spindle/load`,
  `axes/feed_rate`, `production/parts_count`, `production/cycle_time`,
  `alarms/count`, `alarms/first_fault`) + a representative X/Y/Z axis set. Its own
  comment: *"Static set… operators should cross-reference with the Agent's own
  /probe output."* It does **not** contact the agent.
- `MTConnectTagMap` is a **static** registry; names *deliberately mirror FOCAS2*
  so a downstream MQTT topic (`status/run_state`, …) looks identical regardless of
  which CNC protocol produced it.
- `Condition` dataItems are **not** per-item tags — the stream parser auto-aggregates
  them into `alarms/count` + `alarms/first_fault`.
- `Focas2SourceAdapter.BrowseTagsAsync` is the **identical pattern**, and
  **`AddFocas2Source.razor` + `Focas2BrowseService` already ship.** That is the
  template — not OPC UA Client.

**So the operator never picks/renames dataItems.** The adapter knows the standard
CNC semantics. The wizard's job is the FOCAS2 flow: *connect → confirm reachable →
show the (fixed) tags you'll get → name + route → save.*

## 2. Impact on the v1 review

| From the review | Verdict under the corrected premise |
|---|---|
| Q-1 parser/discovery in adapter, DTO/API/UI in Management | **Still correct** — and FOCAS2 already does exactly this (`Focas2BrowseService` in adapter-referencing Management API). |
| Q-2 fixtures-first, optional integration, demo-mode follow-up | **Still valid.** |
| Mockup-first, tile-flip-last, "don't copy OPC UA's lazy tree" | **Still valid** (even more so — it's a fixed list, not a tree). |
| Q-3 Condition dataItem **selection** | **N/A** — conditions auto-map to `alarms/*`; not selectable. |
| Q-4 tag-map **editing** depth | **N/A** — tags are fixed semantic; operator doesn't pick/rename (same as FOCAS2). |
| Q-5 dataItem **selection** ergonomics | **N/A** — no selection; fixed set. |
| v2 add: dataItem-tree parser DTO, deterministic tag naming, duplicate handling, unsupported-item copy | **Mostly N/A** — those solve an arbitrary-selection model we don't have. |
| v2 add: Review summary | **Partly survives** — but as "12 standard tags, N axes discovered, M unavailable", not "92 samples / 31 events". |

Net: the wizard is **simpler** and has a **closer, already-shipped template (FOCAS2)**.
Plan v1's "net-new general `/probe` parser" is **not** needed.

## 3. Corrected approach (to be detailed in v2)

Mirror the shipped FOCAS2 onboarding stack:

- **`AddMTConnectSource.razor`** ≈ `AddFocas2Source.razor`: connection step
  (agent base URL, optional device name, timeout) → **Browse/Test** → show the
  resulting tag set (read-only) → name + route → **save via draft→apply**.
- **`MTConnectBrowseService`** (Management, references `Sources.MTConnect`) ≈
  `Focas2BrowseService`: on a throwaway adapter, do a **real reachability probe**
  (HTTP GET `/probe`), confirm the device is reachable, then return the tag set +
  a browse status. Behind **`POST /api/v1/sources/browse/mtconnect`** with a
  status enum + status→HTTP mapping (mirroring FOCAS2/OPC UA).
- **Tile flip last:** `SourceProtocolPickerModel` `mtconnect` → Available only
  after end-to-end verification. S7 stays Pending.

### The chosen enhancement — "Enhanced /probe parse" (user decision)

The browse probe does **more than reachability**: it parses `/probe` (a **bounded**
parse, NOT a general dataItem tree) to make the tag list accurate for *this* machine:

1. **Discover actual axis names** — replace the static X/Y/Z with the real
   `Linear`/`Rotary` axis components found in `/probe` (e.g. X/Y/Z/A, or only X/Y).
2. **Grey out unavailable semantic tags** — determine which of the ~12 fixed tags'
   *source* dataItem types are actually present in `/probe`, and mark the rest as
   "not exposed by this agent" so the operator sees exactly what this machine emits.

The dataItem-type → semantic-tag mapping (Execution → `run_state`, ControllerMode →
`controller_mode`, PathFeedrate → `axes/feed_rate`, RotaryVelocity/Load →
`spindle/*`, PartCount → `production/parts_count`, etc.) is **already encoded in the
stream parser** — the bounded probe parser reuses that same mapping to check
presence, rather than inventing a new one.

## 4. Revised work breakdown (FOCAS2-grounded)

- **M1 — Mockup (sign-off gate):** FOCAS2-style wizard mockup; show connect,
  browse result (tags grouped, axes discovered, unavailable tags greyed), and error
  states (agent unreachable, invalid `/probe`, reachable-but-no-recognised-dataItems).
- **M2 — Browse backend:** `MTConnectBrowseService` (reachability probe) + the
  bounded `/probe` parser (axis discovery + semantic-tag presence) in the adapter +
  `POST /api/v1/sources/browse/mtconnect` + status mapping + fixture-based parser
  tests + service tests.
- **M3 — Wizard:** `AddMTConnectSource.razor` mirroring FOCAS2; testable wizard-model
  POCO + tests; edit support via `SourceEditRouter`.
- **M4 — Tile flip** + picker test + onboarding copy (S7 stays Pending).
- **M5 — End-to-end verify** (real/demo agent) + build/tests green + handoff +
  `CLAUDE.md` §8 → MTConnect operator-available.

## 5. Open questions for the corrected re-review (ChatGPT)

- **QC-1 — Axis discovery in `/probe`.** Derive axis letters from `Linear`/`Rotary`
  Component `name`/`nativeName`, or from `Position` dataItems' parent component? Both
  ACTUAL + MACHINE position tags per axis, as today? Cap on axis count?
- **QC-2 — "Unavailable" semantics.** When a tag's source dataItem type is absent
  from `/probe`: **grey out** (show, disabled) vs **hide** vs **show-anyway** (the
  adapter would simply emit nothing). Lean: grey out with a reason — honest, matches
  the "explain, don't hide" principle.
- **QC-3 — Reachability vs recognition.** Two distinct states: agent reachable but
  `/probe` exposes **none** of our recognised dataItem types (operator should know it
  will produce ~no data) vs agent unreachable. Both need clear browse-status codes.
- **QC-4 — Probe budget.** FOCAS2 uses a fixed 15 s budget (not config-derived).
  MTConnect is one HTTP GET — propose a short fixed budget (e.g. 8–10 s) independent
  of the source's configured `TimeoutSeconds`. Confirm.
- **QC-5 — Demo mode.** Still a follow-up (own small milestone) rather than a blocker?
  An MTConnect demo agent (static `/probe` + `/current`) would let sales/dev exercise
  the wizard with no hardware, mirroring FOCAS2 demo mode.
- **QC-6 — Review summary.** Keep a short pre-save summary ("12 standard tags · 3
  axes discovered (X/Y/Z) · 2 tags unavailable on this agent")? Low cost, high
  operator confidence.

## 6. Process note

Cadence so far: v1 → ChatGPT review → **this reality-check** (premise was wrong) →
re-review on the corrected basis → **v2**. No code until v2 is approved. The mockup
(M1) is still the first build artifact and still needs sign-off before any wizard
wiring.
