# ADR-0020: Diagnostic Bundle redaction spec — what is included, masked, stripped, and excluded

**Status:** **Accepted (2026-05-31), as amended by [Amendment 1](#amendment-1--mechanism-reconciliation-2026-05-31-accepted).** The original Decision below is superseded *on mechanism only* by Amendment 1; its tier semantics (Rule 1) and Rules 2/3/5 stand. Amendment 1 reconciles the redaction mechanism with the two-world config reality and the already-shipped `Management/Backup/SecretRedactor`; it was drafted, taken through one ChatGPT review pass (O-1…O-4 resolved, R-1/R-2/R-3 release gates added), revised on operator steer (O-1 → unify the engine, pre-launch), and ratified by the operator.
**Date:** 2026-05-30 (original); 2026-05-31 (amendment 1 drafted, reviewed, revised, accepted)
**Framing:** Before a "Generate Diagnostic Bundle" feature ships, the exact contents of the bundle MUST be locked. Without an explicit redaction spec the bundle is a privacy footgun: an operator clicks one button and ships customer secrets, license material, or in-flight values to whoever requested the ZIP. The bundle's value (support workflow) depends on it being safe to email.

## Context

ADR-0021 (Route Flight Recorder), ADR-0023 (Explain Why Data Is Missing), ADR-0024 (What Changed), and ADR-0026 (Route Timeline) all produce diagnostic state that an operator may want to bundle and send to support. The proposed "Generate Diagnostic Bundle" feature (ChatGPT's #4 in the 2026-05-30 diagnostic-strategy review) is the workflow.

Without an explicit spec:

- The bundle includes the gateway's signed license file (RSA private material is not in the license, but the customer/site binding is — and shipping that to a third party can violate licensing terms)
- The bundle includes adapter secrets (MQTT passwords, OPC UA private cert keys, HTTP API tokens)
- The bundle includes the last-N data points captured by the Live Data Tap — which contains real customer values and timestamps from machine floors
- The bundle includes log lines that an adapter wrote at a level the operator never expected to be exported
- The bundle includes file paths that leak internal directory structures, usernames, and machine identifiers

This is the standard privacy-footgun shape of "support bundle" features. The spec must land before any bundling code is written. Per the operator's call during the 2026-05-30 review pass, **A (Bundle Redaction Spec) is a prerequisite for #4 (Bundle generation)**.

## Decision

The Diagnostic Bundle conforms to four content rules and one provenance rule.

### Rule 1 — Four-tier content classification

Every piece of state the runtime can emit into a bundle falls into exactly one of four tiers:

| Tier | Behaviour in bundle | Examples |
|---|---|---|
| **INCLUDE** | Verbatim, unmodified | Config schema version, route IDs, source/sink IDs (not connection strings), capability matrix, last-N flight-recorder events, health metric values, status codes |
| **MASK** | Field name retained, value replaced with `***` plus original byte-length annotation | Sensitive-tagged tag values, audit-trail user identifiers, fault-message free-text that may contain device data |
| **STRIP** | Field omitted entirely from the bundle (not even an empty placeholder — must not appear) | Adapter passwords, OPC UA private key bytes, MQTT cert PEM content, JWT signing keys, license file content, OS environment variables |
| **EXCLUDE** | File or surface not bundled at all | The license file (`license.json`), OS event log, `appsettings.Production.json` if it contains secrets, full Windows event log |

The classification is **per-field at the configuration model level**, not per-file. Every config record field carries a `[BundleTier]` attribute (`Include` / `Mask` / `Strip`); the bundler reads the attribute. Unattributed fields default to **STRIP** (fail-closed).

### Rule 2 — Per-tap-surface privacy honour

The Live Data Tap (ADR-0018) and Route Flight Recorder (ADR-0021) capture data with privacy masking applied at capture time per ADR-0017 Rule 7. The bundler MUST NOT undo that masking. If a value was masked when it entered the diagnostic ring buffer, it stays masked in the bundle. There is no "include real values for support" toggle anywhere.

### Rule 3 — Operator-visible manifest before bundling

The "Generate Diagnostic Bundle" UI MUST display a manifest of what will be included BEFORE the ZIP is created. The manifest groups by tier:

```
Will be INCLUDED (12 sources):
  ✓ Gateway identity (gateway ID, site binding, version)
  ✓ Configuration schema (no secrets)
  ✓ Route topology and state
  ✓ Capability matrix (ADR-0019)
  ✓ Last 500 Flight Recorder events per route
  ✓ Health metrics snapshot
  ...

Will be MASKED (3 sources):
  ✓ Tag values flagged sensitive (shown as ***)
  ✓ Audit-trail user identifiers
  ✓ Fault message free-text

Will be STRIPPED (5 sources):
  ✓ All adapter secrets (passwords, certs, tokens)
  ✓ License file content
  ...

Bundle size estimate: 4.2 MB
```

The operator confirms before the ZIP is written. The manifest is also embedded inside the ZIP at `manifest.json` so the support engineer who receives it sees what was redacted and what wasn't.

### Rule 4 — Bundle is self-describing and reproducible

Every bundle ZIP contains a top-level `bundle-info.json` with:
- `bundleSpecVersion`: integer; the version of this ADR's spec the bundle conforms to
- `gatewayId`: from the canonical gateway identity
- `bundleGeneratedAtUtc`: ISO 8601
- `generatorVersion`: EdgeConnect build version
- `redactionSummary`: count of fields included / masked / stripped / excluded
- `bundlerInvokedBy`: the operator session ID that initiated the bundle (matches audit trail)

A bundle from a v0.2.0 gateway can be loaded by a v0.3.0 EdgeConnect (per ADR-F, Bundle Replay) only if `bundleSpecVersion` is recognised. Older bundles surface as "bundle from older format; some fields unavailable" rather than crash the loader.

### Rule 5 — Provenance: bundle generation is itself an audit event

Generating a bundle writes an entry to the audit trail: `BUNDLE.GENERATED` with the operator identity, timestamp, manifest summary hash, and the bundler invocation reason (optional operator-supplied free-text). This means a customer can later ask "did we ever ship a bundle from this gateway?" and the answer lives in the audit chain alongside config changes.

## Consequences

**Positive:**

- The bundle becomes safe to email — the customer-perceived risk of "what if this contains our passwords?" is eliminated by the manifest pre-confirmation step
- Customer-side compliance reviews (pharma, energy, automotive) have a concrete document (this ADR) to point at when approving the bundle workflow
- Support engineers receive a manifest inside every bundle — they know what's redacted before they look for it
- Fail-closed default (unattributed fields → STRIP) prevents new config fields from accidentally landing in bundles before a redaction decision is made
- ADR-F (Bundle Replay) becomes meaningful — a replay loader can trust that a bundle from a different site honours the same tier rules

**Negative:**

- Every new config field requires a `[BundleTier]` attribute decision at PR time. Reviewable; tractable. Adding to the per-wizard razor smoke test convention (#50) is the natural enforcement point.
- Some support cases may need data the spec strips (e.g., the broker password really is wrong). Workflow: operator runs the adapter's Self-Test (per Phase C, ADR-B) which produces a structured connect-failure report with a redacted hint ("password length 14; broker rejected") rather than the password itself.
- The manifest UI is non-trivial to build (lists every field group, accurate size estimate). Worth the cost — this is the trust gate.

**Forbidden patterns** (caught at review):

- A `[BundleTier(Include)]` on a field that contains a secret (caught by Host.Tests test that snapshot-asserts the tier of every secret-typed field)
- A bundler code path that reads adapter state directly instead of going through the tier-attributed config model
- A "support mode" or "verbose bundle" toggle that bypasses tier rules
- An adapter writing raw values into the Flight Recorder without going through the per-surface mask filter

## Reference

- ADR-0017 — demand-driven diagnostic surfaces (Rule 7 privacy masking at capture time)
- ADR-0018 — Live Data Tap (the bundle includes its masked captures, never unmasked)
- ADR-0021 — Route Flight Recorder (the bundle includes its event log)
- ADR-0023 — Explain Why Data Is Missing (the bundle includes its because-chain output)
- ADR-F (proposed) — Bundle Replay (loader honours `bundleSpecVersion` from this ADR)
- Platform principle P7 — surfaces explain outcomes; the bundle is the off-gateway extension of that explainability
- `docs/sessions/2026-05-30-diagnostic-strategy-handoff.md` — where this ADR was scoped

---

## Amendment 1 — Mechanism reconciliation (2026-05-31, ACCEPTED)

**Status of this amendment:** **ACCEPTED (operator-ratified 2026-05-31).** Drafted before any attribute was placed or any redactor code touched, per P3 (security is spec-first) and the operator's pause-and-report standard. Trail: drafted → one ChatGPT review pass (accepted direction, resolved O-1…O-4, elevated release gates R-1/R-2/R-3) → revised on operator steer (O-1 → unify the engine, since the product is pre-launch with no compat constraint) → operator-ratified. Implementation may now proceed against this amendment; the R-gates (R-1/R-2/R-3) are binding release criteria.

### Why this amendment exists

The original Decision (Rule 1) assumes redaction classification is *"per-field at the configuration model level… every config record field carries a `[BundleTier]` attribute; the bundler reads the attribute."* Walking the actual config models (2026-05-31) showed that assumption does not survive contact with how `gateway.json` is persisted, and that a redactor with overlapping responsibilities already ships. The original mechanism cannot be implemented as written. This amendment reconciles the spec with reality **without** weakening the four-tier content classification (Rule 1's tier *semantics* — INCLUDE / MASK / STRIP / EXCLUDE — are retained verbatim; only the *mechanism* that assigns a field to a tier changes).

### Finding 1 — The config is two worlds, not one

There is no single "configuration model" with attributable fields. Persisted config splits into:

- **World 1 — Typed fields.** C# properties on records: `GatewaySettings`, the typed fields of `SourceInstanceConfig` / `SinkInstanceConfig` (`InstanceId`, `ProtocolName`, `DeviceId`, …), `PublishingSettings` typed fields, `RouteConfig`. These *can* carry a `[BundleTier]` attribute the bundler reads via reflection.
- **World 2 — Opaque JSON.** `SourceInstanceConfig.Connection` and `SinkInstanceConfig.Connection` are `JsonElement?`; `PublishingSettings.Extras` is a `[JsonExtensionData]` dictionary. **Every adapter secret is persisted here** — MQTT `password`, OPC UA `credentials.password` / `certificatePassword`, HTTP tokens. These bytes have no C# property to attribute. The typed projections that *do* expose these as attributable properties (`MqttSinkConfiguration.Password`, `OpcUaClientCredentials.Password`, `Focas2SourceConfiguration`) are **runtime-only**, built by `From*Instance(...)` factories and never serialized — so an attribute on them is invisible to a bundler reading `gateway.json`.

A bundler that reads only typed attributes would strip nothing real (the secrets aren't in typed fields) and, under Rule 1's fail-closed default, would strip the entire opaque Connection block — discarding the non-secret support data (endpoint, host, port) that is the bundle's reason to exist.

### Finding 2 — A name-based redactor already ships

`src/ElpisEdgeConnect.Management/Backup/` (Phase 4, M.1c.3) already implements most of original Rules 3–5 by a *different* mechanism:

- `SecretRedactor` walks **raw JSON** (`JsonNode`) and redacts by **property name**, emitting JSONPath-ish provenance for each replacement.
- `BackupSecretPatterns` is a case-insensitive name allowlist (`password`, `apiKey`, `privateKey`, `certificate`, `connectionString`, …), explicitly designed *"property-NAME based, not value-heuristic… false-negative leakage is a CVE; over-coverage is preferred."*
- `BackupManifest` + `BackupBuilder` already emit a manifest with per-file redaction paths and SHA-256 checksums, and deliberately read `gateway.json` byte-for-byte so operator-authored extras survive.

This is a **2-outcome** model (keep verbatim ↔ `<REDACTED>` sentinel), not the **4-tier** model of Rule 1, and it lives in `Management`, not `Core`. The original ADR was written unaware of it.

### Amended decision

#### A1.1 — Extend the shipped redactor from 2 outcomes to 4 tiers

`SecretRedactor` / `BackupSecretPatterns` become the single execution engine for **both** the backup feature and the diagnostic bundle. Extend its outcome space to the Rule 1 tiers:

| Tier | Outcome on a JSON property | Replaces today's |
|---|---|---|
| **INCLUDE** | value kept verbatim | "name not in allowlist" |
| **MASK** | value stays a string `"***"`; a **sibling** key carries the metadata — `"password": "***"` + `"password__redacted": {"masked": true, "originalByteLength": 14}`. Locked shape (Q-5, plan review pass): string-typed consumers are unaffected, metadata is ignorable. Key retained so restore tooling can prompt. | (new) |
| **STRIP** | property omitted entirely (key absent from output) | (new — stronger than today's `<REDACTED>`) |
| **EXCLUDE** | file-level; the whole file is never added to the archive | already handled by `BackupBuilder` file selection |

The existing `<REDACTED>` sentinel sits between MASK and STRIP (key kept, value blanked, no length). **Pre-launch (no customers, product not shipped), there is no backward-compat constraint on the backup export** — so the original hedge ("don't change what backup produces") is dropped. **Backup and bundle unify on the one 4-tier engine with one universal tier ruleset — no per-consumer semantics.** A field's tier is a property of the field, decided once, applied identically by both consumers. Backup's restore round-trip (the redacted value is re-entered on restore) does **not** require a backup-only policy: secrets that must be re-entered are tiered **MASK** (key retained so restore tooling detects and prompts; value blanked and invalid-for-direct-apply), and material that must never ship in any artifact is tiered **STRIP**. Restore-prompting therefore falls out of MASK's representation, not out of a second code path. (See O-1 below; refined by the implementation-plan review pass — `2026-05-31-adr0020-redaction-implementation-plan-v2.md`.)

#### A1.2 — Three mechanisms assign a tier; they are tried in order

Every byte the bundler can emit is classified by exactly one of three mechanisms, selected by where the byte lives:

1. **Typed field → `[BundleTier]` attribute (World 1).** Reflection over the typed config records. The attribute is the source of truth. **Fail-closed:** a typed field with no `[BundleTier]` → **STRIP**. (Retains original Rule 1's fail-closed intent, now scoped to the surface where it is actually enforceable.)

2. **Opaque-with-typed-counterpart → derived name→tier map (World 2a).** For a Connection block whose protocol has a typed projection (`Mqtt`, `OpcUaClient`, `Focas2`, … via their `From*Instance` factory), the factory already enumerates which JSON keys map to which typed fields (`conn["password"]` → `.Password`). We **derive** a property-name → tier map from the typed counterpart's `[BundleTier]` attributes and apply it to the raw JSON during the name-walk. **Fail-closed:** a JSON key the factory reads into a typed field that has no attribute → **STRIP**; the derived map is treated as authoritative for the keys it covers.

3. **Opaque-no-counterpart → per-protocol strip allowlist (World 2b).** For Connection/Extras bytes with no typed projection *and* for operator-authored extra keys that a factory ignores (unknown keys survive byte-for-byte today), there is no attribute and no derived mapping. Here a name allowlist is the only classifier. **Fail-OPEN:** default **INCLUDE**, redact only names matching the MASK/STRIP allowlist.

   **Ownership (O-2, resolved):** the unified redaction *engine* lives in `Management` (beside `Backup/`, which it absorbs), but **protocol-specific secret knowledge lives beside each adapter, not in a central file and never in `Core`.** Each protocol module declares its own rules — e.g. `MqttBundleRedactionRules`, `OpcUaBundleRedactionRules`, `Focas2BundleRedactionRules` — and the Management engine composes them with a shared cross-protocol baseline (the existing `BackupSecretPatterns` set: `password`, `apiKey`, `privateKey`, …). This keeps `Core` protocol-agnostic (CLAUDE.md §9 #1) and means adding a protocol adds its redaction rules in the same module as its adapter, not in a Management-side list that drifts from the adapters it describes. *Management coordinates; protocols declare.*

#### A1.3 — The fail-direction split is deliberate, and it is the load-bearing tradeoff

| Mechanism | Default for an un-classified field | Rationale |
|---|---|---|
| 1 Typed attribute | **STRIP** (fail-closed) | The typed surface is a closed, reviewable enumeration. A new field forces a `[BundleTier]` decision at PR time. Safe to fail-closed because nothing useful is silently lost — the author sees the build/test guard fire. |
| 2 Derived map | **STRIP** (fail-closed) | The factory's key set is likewise a closed enumeration. Same guarantee as mechanism 1, projected onto the JSON keys the factory reads. |
| 3 Strip allowlist | **INCLUDE** (fail-open) | Failing closed here would strip *every* operator-authored extra and every unknown-protocol Connection — gutting the support value that motivates the bundle (and contradicting `BackupBuilder`'s deliberate byte-for-byte read). |

**Honest statement of the residual risk:** mechanism 3 is fail-open, so a genuinely new secret whose property name is **not** in the allowlist **leaks into the bundle**. This is a real CVE-shaped surface, not a hypothetical. It is accepted (not eliminated) because the alternative — fail-closed on unknown opaque keys — destroys the feature's purpose. The risk is *narrowed*, not closed, by **deterministic protection applied first**: (a) the allowlist being deliberately over-broad and conservative; (b) the secret-shape detector (A1.4 #1), which runs over World-2b values *before* the operator ever sees the preview; and only then (c) the operator-visible manifest preview (Rule 3) showing exactly what will ship **before** the ZIP is written.

The spec position is therefore: **the system applies as much deterministic redaction as it can (mechanisms 1–3 plus the secret-shape detector); the preview is the operator's final verification opportunity over what remains, not the primary defence.** The operator is the final confirmation gate, not the first or only protection.

#### A1.4 — Corner cases this amendment explicitly does NOT fully close

1. **Runtime-discovered secret-shaped value, secret-unshaped name (World 2b).** An adapter writes an operator-authored or runtime-discovered key whose *name* isn't in the allowlist but whose *value* is a secret (PEM header, JWT, PKCS/SSH key, or high-entropy token). Fail-open includes it. **Mitigation — the secret-shape detector (see R-1), phased:** because World 2b is *knowingly* fail-open, this detector is the runtime mitigation between a leak and the operator's eyes. Per the implementation-plan review pass, the detector is split: **Phase 1 (ships in the first release, deterministic): PEM block markers, JWT structure, PKCS markers, SSH key markers** — near-zero false positives; this is the R-1 gate. **Phase 2 (fast-follow, does NOT block the first release): entropy / token-likelihood scoring** — where the false-positive/tuning burden lives, so deliberately decoupled. On a match it surfaces a **non-blocking warning** naming the JSON path. We deliberately **warn, not auto-strip** — value-heuristic *auto-redaction* was rejected as a primary mechanism (`BackupSecretPatterns` header). Honest note: until Phase 2 lands, a high-entropy *unstructured* token under a benign name is mitigated only by the static allowlist + preview (consistent with A1.3's accepted residual risk).

2. **Attributed INCLUDE field carrying pasted secret content.** A typed field correctly tiered INCLUDE (e.g. `topicTemplate`) into which an operator pastes a token. The attribute says INCLUDE; the bundler honours it. Same preview-warning escape hatch; not otherwise closeable without value heuristics in the data path.

3. **Derived-map drift (World 2a).** If a `From*Instance` factory changes which JSON keys it reads without a matching `[BundleTier]` update, the derived map silently under-covers. The amendment text alone does not close this; **R-2 (build-time / CI drift enforcement) is the required guard** — a source generator or snapshot test in `Management.Tests` that fails CI when a factory reads a key the derived map doesn't cover, or when a secret-typed field resolves to anything but MASK/STRIP. Listed as a release gate, not a follow-up.

4. **Nested opaque objects** (`connection.credentials.password`). The shipped walker already recurses by name; the derived map must therefore key on nested paths, not just leaf names. Implementation detail flagged so it isn't missed.

### Open questions — resolved by review pass (2026-05-31)

The review pass (ChatGPT) resolved all four; the operator ratified, revising O-1 (unify the engine, pre-launch). Answers are folded into the decision body above; recorded here for provenance.

- **O-1 — backup backward-compat. *Resolved by review pass, revised on operator steer, then refined by the plan review pass (2026-05-31).*** The first review pass said "preserve existing backup behaviour." The operator noted the product is **pre-launch with no customers**, voiding that risk. The implementation-plan review pass then pushed further: don't even keep a per-consumer tier policy. **Final resolution: one 4-tier engine, one universal tier ruleset, identical for backup and bundle — no consumer-specific semantics.** Restore-prompting is served by tiering re-enterable secrets as **MASK** (key retained, value invalid-for-apply) and never-ship material as **STRIP**; both fall out of the universal rules, not a backup mode. Revisit only if a shipped customer baseline ever creates a real compat surface.
- **O-2 — engine location. Resolved: unified engine in `Management`; protocol-specific rules declared beside each adapter; no protocol knowledge in `Core`.** Folded into A1.2 mechanism 3 ("Management coordinates; protocols declare").
- **O-3 — secret-shape detector. Resolved: ships in v1 (PEM / JWT / private-key markers / high-entropy).** It is the only runtime mitigation for the fail-open World-2b path, so it is MVP, not a fast-follow. Folded into A1.4 #1 and R-1.
- **O-4 — derived-map computation. Resolved: build-time / CI enforcement preferred** (source generator or a snapshot test that fails CI), because the failure mode to prevent is "factory changed → map forgotten → leak", and a compile/CI failure catches that *before* deployment whereas a runtime warning catches it after. Folded into corner case 3 and R-2.

### Required before implementation (R-gates — must be in the first bundle release)

These three are **release criteria**, not nice-to-haves. The review pass elevated them; implementation does not start treating them as optional.

- **R-1 — Secret-shape detector is MVP, phased.** The **deterministic** detectors — PEM, JWT, PKCS, SSH key markers — over World-2b values, surfaced as preview warnings (A1.4 #1), are the **first-release gate**. Entropy/token-likelihood scoring is **Phase 2** (fast-follow, does not block the first release; carries the tuning burden). Because World 2b is knowingly fail-open, shipping without the deterministic detectors means shipping the residual leak risk mitigated only by the static name allowlist — not acceptable for v1.
- **R-2 — Build-time / CI drift enforcement.** A source generator or a snapshot test that **fails CI** when a `From*Instance` factory reads a JSON key the derived map doesn't cover, or when a secret-typed field resolves to anything other than MASK/STRIP. Compile/CI failure over runtime warning, every time, given the security stakes.
- **R-3 — `redactionEngineVersion` in `bundle-info.json`.** This **amends Rule 4**: the bundle's `bundle-info.json` carries `redactionEngineVersion` (integer) *in addition to* `bundleSpecVersion`. Rationale: a future support engineer asking "why was this field masked / why was that one included?" needs to know which redaction-engine revision produced the bundle — that answer can change between engine versions independently of the bundle spec version. Small field, high forensic value. (Per the revised O-1, backup now shares the engine, so the backup manifest should stamp the same `redactionEngineVersion` for the same forensic reason.)

### What this amendment changes vs leaves intact

- **Intact:** Rule 1 tier *semantics*; Rule 2 (per-surface privacy honour — masked-at-capture stays masked); Rule 3 (operator-visible manifest); Rule 5 (bundle generation is an audit event). Retained unchanged.
- **Amended — Rule 1's *mechanism* sentence** ("every config record field carries a `[BundleTier]` attribute; the bundler reads the attribute; unattributed → STRIP") is replaced by the three-mechanism split (A1.2) with the fail-direction split (A1.3). The blanket "unattributed → STRIP" becomes true only for Worlds 1 and 2a.
- **Amended — Rule 4** gains a required `redactionEngineVersion` field in `bundle-info.json` alongside `bundleSpecVersion` (R-3).
- **Newly acknowledged dependency:** `Management/Backup/SecretRedactor` + `BackupSecretPatterns` + `BackupManifest` are now load-bearing for this ADR, not just for backup. Per the revised O-1 (pre-launch, no compat constraint), backup and bundle **share the single 4-tier engine**; backup's invalid-placeholder behaviour is preserved only as a per-consumer tier policy where restore round-trip needs it, not as a second engine.
- **Release gates (R-1/R-2/R-3):** secret-shape detector, build-time/CI drift enforcement, and `redactionEngineVersion` are required in the first bundle release, not deferrable.
