# Bulk-Provision UI Phase 1 — v3 (reality-check + LOCK with architecture amendment)

**Date:** 2026-06-14
**Author:** Claude (post-v2-review + repo-grounded reality-check)
**Status:** **LOCK** — implementation may begin against this doc. Supersedes v1 and v2 entirely; the architecture amendment in §2 reshapes the wizard's behavior.
**Predecessor:** `docs/sessions/2026-06-14-bulk-provision-ui-phase1-v2-plan.md`
**Cadence position:** v1 → ChatGPT v1 review → v2 → ChatGPT v2 review (approve direction, require architecture amendment before lock) → **v3 reality-check + LOCK (this doc)**.

---

## 0. ChatGPT v2 verdict + architecture amendment

**Verdict:** "Proceed with Claude's v3 reality-check, but require an architecture amendment before lock."

The amendment is the core message. v2 still described the Studio wizard as a UI wrapper around the chip-3 offline generator — "Studio wraps the generator and creates N validated gateway.json files." That's the wrong model for the customer's real topology (**2-3 EdgeConnect gateways for 100 CNCs**, not 100 gateways).

### The architecture amendment (LOCKED in v3)

> **The Studio wizard adds N sources to the CURRENT gateway's existing config and creates ONE draft.**
> The offline tool (`tools/bulk-provision/`) stays as-is — row → standalone `gateway.json` file, fit for the "1 EdgeConnect per CNC" deployment model.
> **These are intentionally DIFFERENT product surfaces.** The Studio wizard is NOT a UI clone of the offline tool.

Per ChatGPT's roadmap:

```text
Phase 1:    Gateway-local Studio bulk import. Same tag set per run.
            One protocol per run. FOCAS2 + MTConnect supported.
            Add N sources to THIS gateway. Create ONE draft.
Phase 1.1:  Optional connectivity / tag coverage enhancements,
            only if existing diagnostics can be reused cheaply.
Phase 2:    Different tags per CNC. Tooling profiles.
            Per-row tagProfile. Possibly multi-protocol CSV.
            Profile editor later.
```

---

## 1. Lock decisions (folded from ChatGPT v2 review)

| Topic | LOCKED decision |
|---|---|
| **Gateway model** | 2-3 gateways. Each wizard run imports sources into the current gateway only. The wizard runs inside that gateway's Studio. |
| **Draft model** | One submit = **one draft** of the current gateway config with N new sources merged in. No N-drafts. No partial-draft creation. |
| **Protocol model** | One protocol per run in Phase 1. Operator runs the wizard once per protocol per gateway. Mixed-protocol CSV deferred to Phase 2. |
| **CSV shape** | Protocol-specific CSV templates. FOCAS2/Brother use `host`. MTConnect uses `baseUrl`. **No unified `endpoint` column in Phase 1.** |
| **Sidecar UI** | **Removed from operator UX.** Studio auto-populates gateway identity, sink, route from the current config. Operator sees a read-only "Gateway context" panel. |
| **Missing tags** | **Warn, do not block.** Missing tags do not publish; other tags continue. |
| **Connectivity test** | Optional, lightweight, Phase 1 only if cheap (MTConnect `/probe`, FOCAS2 minimal handshake). Submit not blocked on failure. |
| **Tag Coverage report** | **Deferred** to Phase 1.1 / Phase 2 unless existing diagnostics make it nearly free. Phase 1's post-apply guidance is a one-paragraph hint pointing operators at existing 3-way diagnostics. |
| **Profiles** | **Deferred entirely to Phase 2.** No `tagProfile` column, no profile JSONs, no `extends` mechanism, no profile editor in Phase 1. Phase 1 uses fixed per-protocol baseline templates only. |
| **MTConnect baseline** | Must be derived from the customer's current 64-tag requirement via the parity artifact (§6). Not a blind universal list. |
| **Mockup** | Still required first. Revised around the gateway-local merge model (§9). |

---

## 2. Gateway-local model (the architecturally consequential lock)

### 2.1 Two distinct product surfaces

| Surface | Input | Output | Deployment fit |
|---|---|---|---|
| **`tools/bulk-provision/` (offline tool)** | CSV + sidecar + template | N standalone `gateway.json` files, one per row | "1 EdgeConnect per CNC" model. Edge-everywhere. |
| **Studio wizard (NEW — Phase 1)** | CSV (no sidecar from operator) | ONE draft of the current gateway config with N new sources merged in | "Few EdgeConnects, many CNCs each" — the actual customer model. |

These are different products. v3 makes that explicit so future planning doesn't conflate them again.

### 2.2 Service architecture (reality-check grounded)

Repo evidence:
- `GatewayConfiguration.Sources` is `IReadOnlyList<SourceInstanceConfig>` — a flat list.
- `IConfigurationManager.CreateDraftAsync(GatewayConfiguration draft)` accepts a WHOLE config record.
- Chip-3 templates emit FULL `gateway.json` shape (Gateway + Sources[1] + Sinks[1] + Routes[1]).

Two ways to implement the Studio service:

**(a) Reuse chip-3 generator + extract** — service invokes `generate.ps1` against the CSV, gets N standalone `gateway.json` files, then EXTRACTS each Source block and merges into the current gateway config (current Gateway + Sinks + Routes stay; new Sources appended; routes added per source pointing at existing sinks). Reuses chip-3 logic + Pester coverage + sidecar validation.

**(b) C#-only Source builder** — service skips the generator entirely. Reads the protocol-specific template's `Sources[0]` and `Routes[0]` blocks from a known location, substitutes per-row placeholders directly in C#, appends to current config. Doesn't shell out to pwsh; simpler runtime; loses chip-3's sidecar validation + deterministic-output guarantees.

**LOCKED choice: (b) C#-only Source builder.**

Reasoning:
- The chip-3 sidecar is mostly unnecessary for the Studio case — Studio already knows the gateway identity. Generating a sidecar just to satisfy the offline tool's contract is wasted ceremony.
- Shelling out to pwsh from a Studio service introduces real attack surface (already locked down in v2 §7 but still complex).
- The wizard processes per-row CSV-driven substitution into a known template shape — that's straightforward C# work, ~150 LOC.
- Chip-3 stays the source of truth for the OFFLINE workflow; the Studio's `BulkSourceMergeService` is a sibling.
- Templates remain the source of truth for the per-protocol Source + Route shape. The C# service reads the template's `Sources[0]` element + `Routes[0]` element and applies per-row substitution.

**Implication:** `BulkSourceMergeService` does NOT depend on pwsh, the chip-3 `generate.ps1`, or `BulkProvisionService` (the latter is dropped from Phase 1 scope; the name was misleading anyway).

### 2.3 Source merge semantics (LOCKED)

For each CSV row, the service produces:
1. **One `SourceInstanceConfig`** derived from the protocol template's `Sources[0]` with per-row placeholder substitution (`deviceId`, `deviceName`, `host`/`baseUrl`, `enabled`).
2. **One `RouteConfig`** that points the new source at the gateway's existing primary sink (the first MQTT sink in the current `Sinks[]`; if no sink exists, blocker — operator must add a sink first via the existing Sinks wizard).

Merge rules:
- New sources APPEND to current `Sources[]`.
- New routes APPEND to current `Routes[]`.
- Current `Gateway`, `Sinks`, `_provisioning`, `ExtensionData` are passed through unchanged.
- **`InstanceId` collision** → blocker (per row). Existing `Sources[].InstanceId` set is loaded; any new InstanceId already present rejects the row.
- **`DeviceId` collision within batch** → blocker (CSV-internal).
- **`DeviceName` collision within batch or against existing** → warning, not blocker.

---

## 3. CSV shape (LOCKED — protocol-specific)

Each protocol gets its own column convention. The wizard shows a download link for the matching template after the operator picks the protocol.

### FOCAS2 / Brother (host-based)
```csv
deviceId,deviceName,host,enabled
cnc-001,Lathe-Bay-1,192.168.10.21,true
```

### MTConnect (baseUrl-based)
```csv
deviceId,deviceName,baseUrl,enabled
cnc-011,Okuma-VMC-1,http://192.168.10.51:5000/,true
```

### Modbus (host-based; inherited from chip-3)
```csv
deviceId,deviceName,host,enabled
```

**No unified `endpoint` column in Phase 1.** Per ChatGPT: "For Phase 2, a unified `endpoint` column may be acceptable, but for Phase 1 I would keep the CSV boring and protocol-specific."

---

## 4. Validation policy (LOCKED block/warn matrix)

| Issue | Behavior |
|---|---|
| CSV missing required column | **Block** |
| CSV row missing required value | **Block** |
| Duplicate `deviceId` in upload | **Block** |
| Duplicate `InstanceId` against existing gateway sources | **Block** |
| Invalid MTConnect `baseUrl` format (must be `http://` or `https://`) | **Block** |
| Generated source fragment fails `GatewayConfiguration` schema validation | **Block** |
| No sink exists on current gateway | **Block** (operator must add a sink first) |
| Duplicate `deviceName` (against batch or existing) | **Warn** |
| Optional connectivity test fails for some rows | **Warn** |
| Operator-projected tag missing from one CNC (not knowable in Phase 1 wizard) | N/A at wizard; **runtime concern**, see §5 |

---

## 5. Missing-tag handling (LOCKED — runtime concern, not wizard concern)

**Wizard CANNOT know** at preview/submit time whether a specific CNC's firmware supports every tag in the protocol's baseline template. That's a runtime discovery.

**Phase 1 behavior:**

- Wizard submits successfully even if some tags would be unavailable on some CNCs.
- Runtime adapters skip missing tags silently (existing platform behavior); other tags continue publishing.
- Post-apply, the Studio confirmation screen shows:
  > *"Draft created with N new sources. After applying the draft, verify source health and per-tag diagnostics on the Sources page. Some CNCs may not expose every tag in the protocol baseline; missing tags will not publish but other tags continue normally."*

- 3-way diagnostics (existing) surfaces per-tag read errors for operators who want to dig in.

**Tag Coverage dashboard deferred to Phase 1.1 / Phase 2** per ChatGPT: "I would not make it a Phase 1 requirement unless the diagnostics aggregation already exists and can be reused with very little work." It doesn't exist yet, so it's not Phase 1 scope.

---

## 6. MTConnect 64-tag parity artifact (LOCKED requirement)

**Deliverable:** `docs/sessions/2026-06-14-bulk-provision-ui-phase1-64-tag-parity.md`

Required content:

| Column | What it lists |
|---|---|
| Customer tag (canonical name) | E.g. `spindleSpeed`, `partCount`, `executionState` |
| FOCAS2 group / dataPoint | E.g. `Spindle/Speed`, `Production/PartsCount`, `Status/RunState` |
| MTConnect observation equivalent | E.g. `spindlespeed`, `partcount`, `execution` |
| Required / optional in 64-tag baseline | yes/no |
| Notes | Vendor variances, common gaps, Tooling exclusion |

**Blocks PR I-0** (MTConnect template freeze) until either:
- (a) Customer enumeration of the actual 64 tags lands, OR
- (b) Interim baseline is committed with `STATUS: interim baseline pending customer 64-tag enumeration` flag at the top. PR I-0 may ship with interim; the file ships with a clear "subject to update" status header.

---

## 7. Removed sidecar form + Gateway context panel (LOCKED)

The 9-field sidecar form from v1/v2 is **removed** from the operator UX. Studio already knows the gateway's identity. v3 replaces it with a read-only Gateway context panel:

```
Gateway: edge-acme-site-a (00000000-0000-0000-0000-000000000001)
Existing sources: 12
Existing sinks: edge-acme-mqtt (MQTT, broker 127.0.0.1:1883)
Existing routes: 12 (one per source → edge-acme-mqtt)
Draft target: current gateway config + N new sources
```

Optional operator inputs in Phase 1:
- **Import label** (e.g. "FOCAS2 batch 2026-06-14") — saved as a tag/comment on the draft for audit
- **Optional source-name prefix** (e.g. `cell-A-` to disambiguate batches)

No operator-entered fields for `gatewayId`, `fleetId`, `mqttHost`, `mqttQos`, etc. — those are inferred from the current config.

If the current gateway has NO sinks → wizard blocks with "Add an MQTT sink to this gateway before bulk-importing sources."

---

## 8. Implementation order (LOCKED — revised PR split)

| PR | Scope | Notes |
|---|---|---|
| **PR M** | Static HTML mockup revised around gateway-local merge | First deliverable. Mockup states locked in §9 below. |
| **PR I-0** | MTConnect template baseline + 64-tag parity artifact + Pester template-driven coverage | Chip-3 followup. Smaller than v2 estimate because no sidecar work needed. |
| **PR I-1** | `BulkSourceMergeService` + API endpoint + DTOs + service-layer tests | C#-only per §2.2; no pwsh dependency. ~200 LOC + 12 tests. |
| **PR I-2** | Razor page + Sources page entry + UI/model tests | ~300 LOC + ~10 tests. 1:1 user-facing alignment with mockup. |

Renamed from v2's `BulkProvisionService` → **`BulkSourceMergeService`** to reflect the actual semantic (merge sources, not generate configs).

---

## 9. Mockup requirements (REVISED around gateway-local merge)

Mockup PR M shows these 9 states:

1. **Sources page entry** — header gets "Bulk import" button alongside existing "Add Source".
2. **Gateway context** — read-only panel: "You are importing into edge-acme-site-a. Existing sources: 12. Existing sink: edge-acme-mqtt."
3. **Protocol picker** — FOCAS2 / MTConnect / Brother (inherited) / Modbus (inherited). One protocol per run.
4. **Download CSV template** — protocol-specific CSV download (FOCAS2/Brother/Modbus → `host` column; MTConnect → `baseUrl` column).
5. **Upload CSV + parse preview** — file picker; parsed rows table; per-row validation status.
6. **Optional Test connectivity** — button. If pressed, shows reachable/unreachable + observation count for MTConnect.
7. **Preview** — "35 sources will be added to current config. Duplicate rows: 0. Existing source conflicts: 0. Disabled rows: 2." + per-source summary table.
8. **Submit confirmation** — "Created one draft with 35 new sources. View draft. Create another batch."
9. **Error states** — duplicate `deviceId`, existing-source conflict, no sink on gateway, malformed `baseUrl`, generated config invalid.

Removed from v1/v2 mockup states: the 9-field sidecar form. Removed: "Submit creates N drafts." Removed: partial-failure UI for N drafts.

---

## 10. v3 reality-check answers (grounded in repo)

| Question (from v2 §11) | Answer |
|---|---|
| **RC1 — Single gateway.json with N sources vs N files?** | **SINGLE gateway.json with N sources merged in.** This is THE architecture amendment. Existing chip-3 offline tool stays at N files (different deployment model). |
| **RC2 — Does `sidecar-schema.json` match the 9 fields the form will render?** | N/A — sidecar form removed from operator UX entirely per §7. |
| **RC3 — Does chip-3 generator accept `baseUrl` as a CSV column?** | N/A — Studio wizard does NOT invoke the chip-3 generator per §2.2. C#-only service reads template `Sources[0]` directly. |
| **RC4 — What field name does `MTConnectSourceConfiguration` expect?** | `AgentBaseUrl` (verified in `src/ElpisEdgeConnect.Sources.MTConnect/MTConnectSourceConfiguration.cs`). Template `Sources[0].Connection.AgentBaseUrl = {{ baseUrl }}`. |
| **RC5 — Does `AddMTConnectSource.razor` define reusable observation defaults?** | Has a `DataPointGroups` model. Phase 1 wizard uses the protocol-specific baseline template's `dataPoints[]` block; Phase 2 might reuse the AddMTConnectSource catalog UI for profile editing. |
| **RC6 — Does `POST /api/v1/config/drafts` support 100 sequential creates?** | N/A — only ONE create per submit per §2.3. The 100-draft model is obsolete. |

**Plus new questions surfaced by the architecture amendment, answered inline:**

| Question | Answer |
|---|---|
| **Does the wizard need a new API endpoint, or can it reuse `POST /api/v1/config/drafts`?** | **Reuses** the existing endpoint. Service constructs the merged `GatewayConfiguration` in memory and submits. No new API surface needed. |
| **What if the current config has NO sinks?** | Block at preview. Surface: "Add an MQTT sink before bulk-importing sources." |
| **What if the operator runs the wizard twice in a row on the same gateway?** | Each run produces its own draft; subsequent runs read the LATEST applied config (not stale state). Drafts created in between are visible per existing Studio behavior. |
| **Does PR I-1 need pwsh on the gateway host?** | **No.** C#-only service per §2.2. Reduces dependency surface for Studio deployment. |

---

## 11. Out of scope for Phase 1 (LOCKED)

Confirmed deferrals per ChatGPT lock table + v3 amendments:

| Deferral | Goes to |
|---|---|
| Per-row tag variation (Tooling) | Phase 2 |
| `tagProfile` CSV column | Phase 2 |
| Profile JSON files | Phase 2 |
| `extends` mechanism in profiles | Phase 2 |
| Profile editor UI | Phase 2 (Slice 2) |
| Unified `endpoint` CSV column | Phase 2 (if useful) |
| Mixed-protocol CSV in one run | Phase 2 |
| Per-source Tag Coverage report dashboard | Phase 1.1 if existing diagnostics make it cheap; else Phase 2 |
| Optional connectivity test for FOCAS2 / Brother / Modbus | Phase 1.1 if cheap (MTConnect `/probe` is cheap → INCLUDED in Phase 1) |
| Template authoring in Studio | Out of all near-term phases |
| Multi-gateway bulk-import dispatcher (one CSV → drafts on multiple gateways) | Out of all near-term phases |

---

## 12. Phase 2 carry-forward note

Phase 2 will pick up:
- Per-machine tag variation via `tagProfile` column
- Profile JSONs with `extends` mechanism
- Built-in baseline profiles + customer-defined profiles + profile editor UI
- Possibly `endpoint` unified column
- Possibly per-row `protocol` column
- Tag Coverage dashboard if not built in Phase 1.1
- Connectivity test extensions for FOCAS2 / Brother / Modbus

These are FROZEN as out-of-scope for Phase 1 to keep Phase 1 shippable.

---

## 13. Size estimate (revised — smaller than v2)

| PR | LOC | Tests | Notes |
|---|---|---|---|
| PR M — static HTML mockup | ~350 HTML/CSS | 0 | 0.5-1 session |
| PR I-0 — MTConnect template + parity artifact + Pester coverage | ~80 (template) + 100 (parity doc) | 2 template-driven tests (auto) | 0.5 session (interim baseline) or +1 session (after customer 64-tag enumeration) |
| PR I-1 — `BulkSourceMergeService` + API + tests | ~200 C# | ~12 tests (merge semantics, collision, sink-required, schema validation) | 1 session |
| PR I-2 — Razor + Sources entry + tests | ~300 C#/Razor | ~10 UI/model tests | 1-1.5 sessions |

**Total: ~3-4 sessions.** Smaller than v2's estimate because:
- No sidecar form work (removed from operator UX)
- No N-draft partial-failure UI (one draft per submit)
- No pwsh invocation (C#-only service)
- No new API endpoint (reuse existing draft creation)

Phase 1.1 (optional connectivity test for FOCAS2/Brother/Modbus, Tag Coverage report if existing diagnostics make it cheap) is its own ~1-2 sessions later, depending on existing diagnostic surface.

---

## 14. Cadence position

1. ✅ v1
2. ✅ ChatGPT v1 review
3. ✅ v2 synthesis
4. ✅ ChatGPT v2 review (approve direction, require architecture amendment)
5. ✅ **v3 reality-check + LOCK (this doc)** — supersedes v1 + v2 entirely.
6. ⏳ User approval of v3
7. ⏳ PR M (static HTML mockup) — first deliverable
8. ⏳ PR I-0 (MTConnect template + parity artifact)
9. ⏳ PR I-1 (`BulkSourceMergeService` + API + tests)
10. ⏳ PR I-2 (Razor + Sources entry + tests)
11. ⏳ Phase 1.1 kickoff (after Phase 1 merges, if connectivity / coverage are needed)
12. ⏳ Phase 2 kickoff (per-machine tags / profiles)

User actions required to start implementation:

1. **Approve v3 lock** — or push back on any §1 decision or §2 amendment.
2. **Customer 64-tag enumeration** — when convenient. PR I-0 can ship interim, but final commits with the customer-confirmed list.
3. **Approve PR M mockup** — gates PR I-1 and PR I-2.
