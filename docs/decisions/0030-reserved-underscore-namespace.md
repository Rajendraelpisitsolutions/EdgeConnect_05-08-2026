# ADR-0030: Reserved underscore-prefix namespace at the `GatewayConfiguration` root

**Status:** Accepted (2026-05-21)
**Date:** 2026-05-21
**Milestone:** Chip 3 — Provisioning Subsystem (precondition)
**Framing:** Carve out a discoverable, parser-preserved namespace for *metadata layered onto* the canonical configuration shape — without weakening the type-safety guarantees that protect canonical fields from typos.

## Context

Chip 3 ([v2 plan](../sessions/2026-05-21-chip3-provisioning-subsystem-plan-v2.md)) ships a bulk-provisioning generator that stamps a `_provisioning` block onto every generated `gateway.json`. The block carries 9 fields of generator provenance: source CSV path, template id + version, generator version, `gatewayProvisioningId`, fleet id, timestamp, etc. It is **not** runtime configuration — the gateway runtime never reads it. But it MUST survive the round-trip through `ConfigurationManager.CreateDraftAsync → ValidateDraftAsync → ApplyDraftAsync` so post-deployment audits can answer "which generator + template produced this gateway?"

The [Chip 3 v3 reality-check §1.1](../sessions/2026-05-21-chip3-provisioning-subsystem-v3-reality-check.md) found that today's parser silently DROPS `_provisioning` on every Deserialize → Serialize round-trip:

- `ConfigurationManager.JsonOptions` ([line 48-54](../../src/ElpisEdgeConnect.Core/Configuration/ConfigurationManager.cs#L48)) does not set `JsonUnmappedMemberHandling.Disallow`.
- System.Text.Json's default is **silent ignore** of unknown members.
- `GatewayConfiguration` has no `[JsonExtensionData]` field to capture them.

So Chip 3 cannot ship until this is fixed. The fix is small (one annotated property), but the *contract* — which root keys are preserved, which are silently dropped, what stops a typo on `"Sources"` from being mistaken for new metadata — needs to be locked once, not relitigated.

Three alternatives were considered:

1. **Strict mode** — set `UnmappedMemberHandling.Disallow`; every unknown root throws. Maximally type-safe but breaks Chip 3's design entirely; no path for layered metadata.
2. **Permissive mode** — set `[JsonExtensionData]` on `GatewayConfiguration` with no filter; every unknown root is preserved. Solves Chip 3's needs but silently absorbs typos: `"Soures": [...]` (typo) would be preserved as extension data rather than rejected.
3. **Reserved-prefix mode** — `[JsonExtensionData]` captures all unknown roots; downstream consumers (validator, generator, future tools) treat **`_`-prefixed** roots as legitimate metadata and **non-`_`-prefixed** unknown roots as suspect-but-tolerated. Provides a discoverable namespace for layered metadata while keeping a guardrail against typos on canonical fields.

## Decision

1. **The `_`-prefix at the `GatewayConfiguration` root is a reserved namespace** for non-runtime metadata layered onto the canonical configuration shape. Root keys beginning with `_` carry semantics defined by tooling that produces them; the runtime never consults them.

2. **`GatewayConfiguration` gains a single `[JsonExtensionData]` property** named `ExtensionData` that captures every unknown root, irrespective of whether it begins with `_`. Capture is mechanically uniform; *interpretation* differs.

3. **Initial members of the reserved namespace:**

   - **`_provisioning`** — Chip 3 generator provenance. 9 fields per Chip 3 v2 §6.
   - **`_diagnostics`** — reserved for a future Operational Intelligence layer (Runtime Tap / EREMOS V2 contract bridge). No producer ships against it today; the reservation prevents accidental reuse.

4. **Non-`_`-prefixed unknown roots are silently captured but not actively used.** This means a typo like `"Soures"` (missing 'c') will survive the round-trip — at the cost of one round-trip's typo-protection — but is not interpreted as anything by tooling. To compensate:

   - `tools/ValidateConfig/` (added by v3 reality-check §1.2) **MUST log a warning** for every non-`_`-prefixed key in `ExtensionData`, listing the suspect keys so operators see them at validate time.
   - Schema validation via `NJsonSchemaConfigurationValidator` remains canonical for catching typos on canonical fields. The schema knows the canonical roots; anything outside them lands in `ExtensionData`, and the warning surface flags it.

5. **Future members of the reserved namespace** require an ADR amendment. The reservation list lives in this ADR; growth is governance-controlled.

## Reasoning

**Why a reserved prefix instead of strict typing.** Chip 3's `_provisioning` block has *9 fields today, will likely grow*, and is generator-versioned independently of `GatewayConfiguration`. Modeling it as a strongly-typed nested record on `GatewayConfiguration` would couple the canonical schema's version to the generator's version: every generator change would bump the canonical schema. The reserved-prefix approach decouples them — the canonical schema treats `_provisioning` as opaque metadata; the generator's contract is documented separately in Chip 3 v2 §6 and evolves on its own cadence.

**Why `_` specifically.** The underscore prefix is the most universally-recognised "internal / metadata / private" convention across configuration formats: Helm chart values, Kubernetes object metadata, Docker Compose, Terraform variables. Operators reading a gateway.json will recognise `_provisioning` as "not part of the configuration proper" by glance, without needing to read documentation.

**Why a single `ExtensionData` capture instead of per-prefix dispatch.** A per-prefix dispatcher (`[JsonExtensionData(MatchPrefix = "_")]` or similar) would require either (a) a custom converter — significant surface area — or (b) restricting `[JsonExtensionData]` to a side-property and post-processing — adds round-trip latency. The single-capture approach lets System.Text.Json do the work; interpretation happens at the tooling layer, not the parser layer.

**Why a warning instead of an error on non-`_`-prefixed unknown roots.** Forward compatibility. A future EdgeConnect version may add a new canonical root (e.g. `routing` next to `Routes`). Existing gateways serialized through older code would have that new root land in `ExtensionData`; if the contract rejected non-`_` unknown roots, every upgrade would require a coordinated config rewrite. The warning surface gives operators feedback ("this looks suspicious") without breaking deployments.

**Why both `_provisioning` and `_diagnostics` are reserved now even though only one ships.** Q-V2-D from Chip 3 v2 §10 specifically recommends pre-emptive reservation. The future Operational Intelligence layer is a known roadmap item; locking `_diagnostics` now prevents an accidental third-party tool from claiming the name in the meantime. Cost of reservation: zero. Cost of collision: a future ADR amendment + every existing user of the conflicting tool migrating.

## Consequences

- **`GatewayConfiguration` gains one new property**: `IDictionary<string, JsonElement>? ExtensionData` with `[JsonExtensionData]`. Nullable, init-only, optional in JSON.

- **`ConfigurationManager.JsonOptions` is unchanged.** No `UnmappedMemberHandling` setting added; default behavior (preserve via `[JsonExtensionData]`) is exactly what we want.

- **Round-trip preservation guaranteed.** `_provisioning` survives `Deserialize → typed → Serialize` byte-for-byte. The Chip 3 generator can stamp the block at generation time and trust that `ApplyDraftAsync` will not destroy it.

- **Typo protection on canonical roots is unchanged for `Sources`/`Sinks`/`Routes`/`Gateway`/`Schemas`/etc.** Those are typed properties; misspellings still fail schema validation and the `ExtensionData` warning system catches the leftover.

- **`tools/ValidateConfig/` must include a "suspect roots" warning.** Non-`_`-prefixed keys in `ExtensionData` are logged at warning level with the JSON path and suggestion ("did you mean `Sources`?"). Operators see suspicious keys at validate time, not at first-render-confusion time.

- **Future members of the `_` namespace are governance-controlled.** Adding `_telemetry` or `_audit` requires a new ADR (or this one's amendment) that documents the producer + the contract.

- **The future Operational Intelligence layer can land `_diagnostics` without further ADR work.** The reservation here is sufficient.

- **Other configuration formats in the repo are NOT affected.** Sink-specific configs (`MqttSinkConfiguration.PublishingSettings`, etc.) have their own `[JsonExtensionData]` patterns where appropriate; this ADR scopes to the `GatewayConfiguration` root.

## Out-of-scope follow-ups

- **Schema-level reservation.** `docs/config-schemas/gateway-configuration.schema.json` could document the `_`-prefix convention via a `patternProperties` declaration. That's an editorial improvement; the runtime contract here is sufficient without it. Defer to Chip 3 step 14 (schema update) or later.

- **`_provisioning` field-level schema.** The block's internal structure could itself be schema-validated. Chip 3 v2 §6 documents the 9 fields informally; a JSON Schema fragment could lock it. Defer — the generator is the source of truth for the block's shape; over-formalising before the generator stabilises adds friction.

- **Removing typo-warning duplication with NJsonSchema.** If `tools/ValidateConfig/` warns on suspect roots AND the JSON Schema schema-validates the canonical roots strictly, there's two surfaces saying "this key isn't expected." That's fine for now; consolidation is a future concern.

- **Cross-project recognition.** EREMOS V2 may eventually need to consume `_provisioning` for cross-fleet audit views. Coordinate via `shared-knowledge/contracts/` when that need lands. Not a prerequisite.

## References

- [Chip 3 v2 plan §3.3](../sessions/2026-05-21-chip3-provisioning-subsystem-plan-v2.md) — `_provisioning` 9-field block (the canonical producer of this namespace today)
- [Chip 3 v3 reality-check §1.1, §1.7](../sessions/2026-05-21-chip3-provisioning-subsystem-v3-reality-check.md) — finds parser-drops-roots problem, recommends ADR-0030 + Core change as precondition
- [`src/ElpisEdgeConnect.Core/Configuration/ConfigurationManager.cs`](../../src/ElpisEdgeConnect.Core/Configuration/ConfigurationManager.cs) — line 48-54 `JsonOptions`; lines 197, 352, 393, 433, 477 round-trip serialization sites
- [`src/ElpisEdgeConnect.Core/Configuration/GatewayConfiguration.cs`](../../src/ElpisEdgeConnect.Core/Configuration/GatewayConfiguration.cs) — the record gaining `ExtensionData`
- System.Text.Json `[JsonExtensionData]` reference — captures unknown JSON members at deserialize, emits them at serialize
- [`docs/sessions/2026-05-21-bulk-provision-ui-kickoff.md`](../sessions/2026-05-21-bulk-provision-ui-kickoff.md) — downstream consumer of this contract
- [`docs/platform-principles.md`](../platform-principles.md) P4 — preserve explainability data path; `_provisioning` is part of that data path post-deployment
