# Multi-protocol pilot expansion — plan v1

**Status:** v1 — first draft, awaiting review.
**Date:** 2026-05-28.
**Driver:** Customer pilot starting in **2–4 weeks**. Pilot site has three independent PLC families that all need to be readable by EdgeConnect at pilot start:

1. **Mitsubishi MELSEC** (iQ-R / Q-Series) — direct PLC read via SLMP.
2. **Allen-Bradley / Rockwell** (CompactLogix / ControlLogix) — direct PLC read via EtherNet/IP CIP.
3. **FactoryTalk SCADA** — re-exposes some plant data via its built-in **OPC UA Server**, which EdgeConnect must consume via an **OPC UA Client** adapter.

The user confirmed that FactoryTalk is **separate** from the Rockwell PLCs at the pilot site, so OPC UA Client → FactoryTalk does **not** bridge Rockwell PLC reads. All three protocols are independent pilot blockers.

This plan trail follows the v1 → review → v2 → reality-check → v2.1 cadence (per `feedback_planning_cadence.md`).

---

## §0 What's on the table

### The three new source adapters

| Protocol | Library | Risk | Effort estimate |
|---|---|---|---|
| **OPC UA Client** | `Opc.Ua.Client` (already vendored for the Server sink) | Low — same stack we ship today | **~1.5–2 weeks** |
| **EtherNet/IP (Allen-Bradley)** | `libplctag.NET` (MPL-2.0, commercial-friendly) + native `libplctag` DLL | Medium — native lib per RID; tag-name addressing UX | **~2–3 weeks** |
| **Mitsubishi MELSEC (SLMP)** | None acceptable OSS (HslCommunication is LGPL, incompatible) — **we write the transport** | Highest — we own the binary protocol | **~2–3 weeks** |

Cumulative sequential: **5.5–8 weeks**, against a 2–4 week pilot start. The math doesn't fit without either parallel work, scope cuts, or both.

### Existing pattern we inherit

- **Adapter SDK** (Core ← Adapters) is mature. 5 sources already shipped (FOCAS2, BrotherHttp, ModbusTcp, MTConnect, S7).
- **ADR-0015 wizard contract** (M.2d.4) gives a locked 8-rule template; every new wizard inherits it.
- **ADR-0016 onboarding meta-wizard** (Connect-a-device) means every new protocol also needs an entry in the protocol picker AND an EmbeddedMode-capable wizard.
- **`SourceProtocolPickerModel.cs`** lists tiles; new protocol → one new tile entry.
- **Test surface convention**: each protocol gets a `Sources.{Protocol}.Tests` project + a `{Protocol}SourceWizardModelTests` in `Management.Tests`.

---

## §1 Per-protocol MVP scope (pilot-minimum)

Locking scope tightly for the pilot. Anything not in this list is post-pilot work.

### 1.1 OPC UA Client — MVP

**In scope:**
- Connect to one OPC UA Server endpoint (FactoryTalk SCADA at the pilot site).
- `Anonymous` + `UserName` auth (FactoryTalk typically uses UserName).
- `SecurityMode = None / Sign / SignAndEncrypt` (Basic256Sha256). Defaults to None for first pilot; encrypt for production.
- **Static tag list** — operator enters NodeId paths or browse-paths in the wizard. NO interactive tag browse for v1 (deferred).
- Subscription-based read (monitored items) with configurable publishing interval per source.
- Reuses existing `OpcUaCertManager`, `OpcUaSecurityConfig`, `OpcUaCredential` from the Server sink.
- Wizard sections: Identity / Endpoint / Security / Tags / Routing. Mirrors `AddOpcUaServerDestination.razor` shape.

**Out of scope (v1):**
- Interactive tag browse (the "Live tag watch" M.2c equivalent).
- Historical data access (HDA).
- Method calls / write capability.
- Complex array / struct value type mapping (atomic scalars only).
- Server-discovered endpoint security policy negotiation (use what's configured).

### 1.2 EtherNet/IP (Allen-Bradley) — MVP

**In scope:**
- One connection per source instance (per PLC).
- CompactLogix / ControlLogix / MicroLogix 1400 controllers.
- Tag-name addressing (`Program:MainProgram.MyTag`, `MyArray[3]`).
- Atomic types: `BOOL`, `SINT`, `INT`, `DINT`, `LINT`, `REAL`, `STRING`.
- Polling per source (no UDP I/O messaging — pure explicit messaging via `libplctag`).
- `libplctag.NET` wrapper; native `libplctag` DLL deployed per RID (win-x64 + linux-x64).
- Wizard sections: Identity / Connection (host / slot / CPU type) / Tags / Routing.

**Out of scope (v1):**
- UDP class-1 I/O messaging.
- Symbolic browse (controller tag dictionary download).
- Structured tag types (UDTs read element-by-element if needed).
- PLC-5 / SLC-500 legacy controllers.
- Implicit messaging.

### 1.3 Mitsubishi MELSEC (SLMP) — MVP

**In scope:**
- iQ-R, Q-Series, FX5U with Ethernet adapter.
- SLMP binary frame over TCP (port 5007 / 5562 typical).
- Device areas: **D** (data registers), **M** (internal relays), **X** (input), **Y** (output), **W** (link registers), **DM** (data memory on FX-series).
- Read-only access (no writes from the source adapter).
- Atomic types: 16-bit / 32-bit signed/unsigned integers, 32-bit float, BOOL bits.
- We write the transport ourselves (the spec is published; binary request/response is straightforward).
- Wizard sections: Identity / Connection (host / port / network / station) / Tags / Routing.

**Out of scope (v1):**
- ASCII frame mode (binary only).
- MX Component COM-based access.
- File register (R) access (deferred).
- Multiple block read optimisation in v1 — naive one-request-per-tag first; optimise after pilot confirms latency.

### 1.4 Cross-cutting MVP rules

- **License gating**: each protocol gets a feature-flag entry. Defaults to enabled in pilot edition; locked-down in lower SKUs.
- **No new shared abstractions** during this implementation window (per roadmap v2.3 §1.1). Per-protocol code stays inside its own project.
- **Terminology freeze** still in force — use canonical names.
- **ADR-0015 inherited as-is.** Each wizard adopts WizardShell + WizardSection + WizardValidationBanner + field-anchor ids.
- **ADR-0016 inherited as-is.** Each wizard implements `EmbeddedMode` + `OnInstanceBuilt` for the onboarding flow.

---

## §2 Sequencing options

### Option A — Three parallel agents, three worktrees

**Plan:** spawn three agents simultaneously, each owning one protocol end-to-end. Each works in its own git worktree. Merge each on completion.

| ✅ Pro | ❌ Con |
|---|---|
| Wall-clock fastest — all three could land in ~2.5–3 weeks if libraries cooperate | Coordination cost: every wizard-contract gap QA finds forces a 3-way retrofit |
| Each agent fully owns its stack; no cross-contamination | Three concurrent worktrees → harder to keep the wizard pattern consistent |
| Merges sequential so review is bounded | Higher risk of one stalling on a library issue (MELSEC most likely) |

### Option B — Sequential, OPC UA Client first

**Plan:** ship OPC UA Client end-to-end (week 1–2), then EtherNet/IP (week 3–5), then MELSEC (week 6–8).

| ✅ Pro | ❌ Con |
|---|---|
| Template proves itself before fanning out | **Pilot slips** — only 1 of 3 protocols ready by week 2, only 2 of 3 by week 5 |
| Each QA cycle is contained | Customer expectation gap |
| Lowest risk | Doesn't meet the 2–4 week pilot target |

### Option C — Hybrid: OPC UA Client first, then MELSEC + EtherNet/IP parallel

**Plan:** OPC UA Client first (week 1–2) — proves the template AND lets the cert / security UX bed in against a real FactoryTalk endpoint at the pilot site. THEN run MELSEC + EtherNet/IP in parallel (week 3–5).

| ✅ Pro | ❌ Con |
|---|---|
| Pilot demo-ready by **end of week 5** with all 3 protocols | Still misses 2-week pilot target if that's hard |
| Template hardens on the cheapest protocol first | Two-track parallel work in weeks 3–5 needs sharp coordination |
| MELSEC and EtherNet/IP are protocol-independent — no merge conflicts expected | If OPC UA Client takes >2 weeks, the parallel phase compresses |
| If pilot starts at week 2, OPC UA Client alone unblocks the FactoryTalk pull — partial value | Partial-value start may not satisfy the customer |

### Recommendation

**Option C, hybrid.** Reasoning:

1. The 2-week corner of the pilot range is aggressive but not impossible if OPC UA Client lands fast and the customer accepts a phased rollout (FactoryTalk pull live at pilot start; direct PLC reads live by week 5).
2. OPC UA Client is the lowest-risk protocol — proving the template here de-risks the parallel phase.
3. MELSEC and EtherNet/IP are independent (different .NET projects, different library stacks). Parallel work has low conflict risk.
4. The 5–7 day buffer before the pilot's latest start (week 4) leaves room for one operator-UX feedback cycle on the new protocols.

**Decision pending:** customer's willingness to start pilot with OPC UA Client only (FactoryTalk pull) and have MELSEC + EtherNet/IP land in the first 2–3 weeks of the pilot itself. This is the key open question for v2.

---

## §3 Wizard contract & onboarding implications

### ADR-0015 (wizard contract) — no amendments expected

All three new wizards adopt the contract as-is:
- WizardShell + WizardSection layout.
- WizardValidationBanner wired to `_model.Validate()`.
- Field-anchor `id="field-{path}"` per Rule 3.
- Test Connection probe — **carve-out applies for MELSEC and EtherNet/IP** (binding has side effects, the probe would actually connect; defer the Test Connection button until M.2c-equivalent live-watch lands). OPC UA Client SHOULD have a Test Connection probe (read-only OPC UA handshake) — to be confirmed in reality-check.

### ADR-0016 (onboarding meta-wizard) — three new picker entries + EmbeddedMode

Each wizard implements:
- `[Parameter] bool EmbeddedMode`
- `[Parameter] EventCallback<SourceInstanceConfig?> OnInstanceBuilt`
- `_lastNotifiedValid` field + `OnAfterRenderAsync` notify pattern
- EmbeddedMode guards on `OnSaveAsync` / `OnCancel` / Snackbar / Nav calls
- Conditional Footer (omit WizardActions in EmbeddedMode)

`SourceProtocolPickerModel.cs` gets three new tiles, flipped to `Available`:

```csharp
new() { Key = "opcua-client", DisplayName = "OPC UA Client", Description = "Read from OPC UA Servers (Kepware, FactoryTalk, vendor PLCs).", Status = Available, TargetHref = "/sources/new/opcua-client", IconSvg = Icons.Material.Filled.Hub },
new() { Key = "ethernet-ip", DisplayName = "Allen-Bradley (EtherNet/IP)", Description = "Rockwell CompactLogix / ControlLogix / MicroLogix via CIP.", Status = Available, TargetHref = "/sources/new/ethernet-ip", IconSvg = Icons.Material.Filled.AccountTree },
new() { Key = "melsec", DisplayName = "Mitsubishi MELSEC (SLMP)", Description = "Mitsubishi iQ-R / Q-Series / FX5U via SLMP.", Status = Available, TargetHref = "/sources/new/melsec", IconSvg = Icons.Material.Filled.DeveloperBoard },
```

The Siemens S7 tile (currently `Pending — M.2b.2`) and MTConnect tile (`Pending — M.2b.4`) stay as-is — those are separate decisions.

---

## §4 Test surface

Per protocol, three test surfaces:

1. **`tests/ElpisEdgeConnect.Sources.{Protocol}.Tests/`** — adapter-level: lifecycle, polling, value type mapping, error classification, retry behaviour.
2. **`tests/ElpisEdgeConnect.Management.Tests/{Protocol}SourceWizardModelTests.cs`** — wizard model: defaults, validation, BuildSourceInstance roundtrip, hydrate-from-existing for Edit mode.
3. **`tests/ElpisEdgeConnect.Management.Tests/SourceProtocolPickerModelTests.cs`** — gains 3 new theory rows pinning the new tiles' status + href.

Test budget per protocol: **40–80 tests** (matches MQTT sink at 41, OPC UA Server at 52, Modbus probe service at higher). Total new test surface for the expansion: **150–240 tests**.

**No real-PLC integration tests in v1.** Adapter-level tests use mocked transports. Real-PLC validation happens in QA cycles against pilot hardware (or vendor simulators where available).

---

## §5 QA implications

The current QA cycle (67 test cases against `edgeconnect-qa-2026-05-27-v2`) is **mid-flight**. Adding three protocols means:

- **Don't ship into the in-flight QA package.** Branch the protocol work off `master` AFTER PR #45 (port diagnostics) merges. Tag the in-flight QA baseline (e.g. `v0.2.0-qa-baseline`) before starting multi-protocol work.
- **Update the QA test plan** (`docs/qa/2026-05-27-modbus-to-opcua-pipeline-qa-plan.md`) with **per-protocol test addenda** — one new section per protocol with happy-path / connection-failure / value-type / wizard tests.
- **New QA publish zip** at the end of the multi-protocol expansion (week 5 in Option C). Likely tagged `v0.3.0-multi-protocol`.

---

## §6 License gating

Each new protocol needs a license-feature key. Proposed names (pending license-spec confirmation in reality-check):

| Protocol | Feature key | Edition tier |
|---|---|---|
| OPC UA Client | `source.opcua-client` | Pro+ |
| EtherNet/IP | `source.ethernet-ip` | Pro+ |
| MELSEC | `source.melsec` | Pro+ |

For the pilot, the license file shipped to the customer enables all three. Lower-edition installers will reject the protocol at DI registration time per the existing license-gated registration pattern.

---

## §7 Open questions for the review pass

1. **Pilot phasing**: is the customer OK starting with OPC UA Client only (FactoryTalk pull live), with MELSEC + EtherNet/IP landing in pilot weeks 1–3? Or must all three be live at pilot start (in which case we need Option A / parallel)?
2. **MELSEC scope detail**: does the pilot site use binary or ASCII frame mode? What device areas specifically (just D + M, or also W / DM)? Network/station number defaults?
3. **EtherNet/IP scope detail**: which CPU family at the pilot site (CompactLogix / ControlLogix / MicroLogix)? Anything older that would need PLC-5 / SLC-500 support?
4. **OPC UA Client scope detail**: does the FactoryTalk endpoint use UserName auth or Anonymous? Security mode (None / Sign / SignAndEncrypt)? Cert chain we need to trust?
5. **Wizard browse**: does any of the three protocols need an interactive "live tag watch / browse" facility for the pilot, or is static tag-list configuration acceptable? (M.2c Runtime Tap is the long-term answer; for the pilot we'd ship static-only.)
6. **Test Connection probe**: which of the three protocols should have a Test Connection button in the wizard? OPC UA Client is the easiest (read-only handshake). EtherNet/IP and MELSEC would actually connect — same side-effect concern as the OPC UA Server "can we bind?" carve-out.
7. **License keys**: are `source.opcua-client` / `source.ethernet-ip` / `source.melsec` the right names? Should they be one umbrella `source.industrial-plc` key or three separate keys?
8. **Native dependency deployment** (EtherNet/IP only): `libplctag` native DLL needs to ship per RID. Confirm the publish + installer paths handle this — possibly affects the QA zip layout.
9. **Roadmap interaction**: this work would compete for attention with M.2c (Live Tag Watch / Runtime Tap) and Chip 3 (Provisioning Subsystem) if those were already in flight. Are either of those in-flight? Confirm before committing the parallel phase.
10. **Are we adding S7 native too?** S7 has a tile in the picker (`Pending — M.2b.2`) and a `Sources.S7` project exists in the tree. If it's already half-built, it's a candidate to fold into this expansion. Reality-check pass should confirm S7's current state.

---

## §8 Out-of-scope (explicit)

- M.2c Runtime Tap / live tag watch for the new protocols — deferred to M.2c proper.
- Interactive tag browse (we ship static tag-list configuration only).
- HDA / historical data access.
- Write capability from any source adapter (sources are read-only by contract).
- New shared abstractions across the three protocols (per roadmap v2.3 §1.1).
- AI assistance / agents in the data path (Locked Decision #14 — unchanged).
- Vendor-specific simulators in CI (we test against mocked transports).

---

## §9 What v2 needs to settle

After the review pass on v1, v2 must lock:

1. **Pilot phasing decision** (Q1) — drives sequencing choice (A / B / C).
2. **Per-protocol scope confirmations** (Q2 / Q3 / Q4) — drives effort estimates.
3. **License-key naming** (Q7) — feeds into license-spec amendment.
4. **S7 inclusion decision** (Q10) — may grow the expansion to 4 protocols.
5. **PR #45 merge ordering** — multi-protocol branch starts AFTER #45 merges. Tag the QA baseline before branching.

v2 will not yet enumerate file-by-file deliverables — that's v2.1 (reality check) territory after we've inspected the existing `Sources.S7` skeleton, the `libplctag.NET` API surface, and the `Opc.Ua.Client` API surface against our adapter SDK.

---

## §10 What v2.1 (reality check) needs to add

Reality check before implementation:

1. **Library-availability audit**: confirm `libplctag.NET` MPL-2.0 compatibility with our license model; confirm native DLL deployment story; confirm `Opc.Ua.Client` API surface matches our adapter contract.
2. **`Sources.S7` skeleton inspection**: how much is already there? Worth folding into the expansion?
3. **Existing wizard-contract gaps**: re-walk ADR-0015 against the three new protocols' wizards — any rule that doesn't fit cleanly (security mode UX for OPC UA Client, native lib feedback for EtherNet/IP, ASCII-vs-binary toggle for MELSEC)?
4. **Onboarding meta-wizard load**: 5 wizards in the picker today, 8 (or 9 with S7) after the expansion. Does the picker UX still hold? Does it need pagination / filtering?
5. **License-gated DI registration**: confirm the three new modules slot cleanly into the existing pattern.
6. **Test budget calibration**: do the 40–80 test counts hold once we look at real protocol surface? Wide-deviation areas are the implementation-risk markers.

---

## §11 Sign-off

This is v1. Ratification path:

1. **You read v1**, push back / ask questions / answer the §7 open questions.
2. **I draft v2** folding in your answers + any ChatGPT review notes.
3. **I run the v2.1 reality check** (existing code inspection, library audits).
4. **You lock v2.1**, I implement.

I am **not** starting implementation off this v1. Locked decision: nothing branches off `master` until v2.1 is locked.
