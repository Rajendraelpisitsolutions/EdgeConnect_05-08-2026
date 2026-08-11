# ADR-0020 redaction engine — M-B implementation plan v2

**Status:** v2 — **review-approved, implementation-ready.** One ChatGPT review pass folded into v1 (approved the architecture + B1→B5 sequencing; requested five edits, now applied — see §0a). No v3 required. Ready to start B1 on operator go.
**Date:** 2026-05-31
**Implements:** `docs/decisions/0020-diagnostic-bundle-redaction-spec.md` (Accepted, as amended) + M-B row of `2026-05-31-adr0020-redaction-implementation-plan-v2.md` §3.
**Branch:** `feat/bundle-redaction-spec`
**Builds on:** M-A (commit `38be433`). **Supersedes:** M-B plan v1 (`9cdb4c1`, retained for trail).

---

## 0. Scope

M-B turns the M-A engine from a **name-only** redactor into a **schema-aware** one that distinguishes the three worlds of ADR-0020 Amendment 1 and applies the right mechanism + fail-direction to each. It also lands `[BundleTier]` placement on Core records and the per-protocol rules (both deferred from M-A).

**In scope:** World boundary (§1); World determinism invariant (§1b); M1/M2/M3 pipeline (§2); `*BundleRedactionRules` per adapter + composition (§3); engine refactor (§4); R-2 drift guard (§5). **Out of scope:** secret-shape detector (R-1 → M-C); bundle ZIP/preview (downstream).

## 0a. Review pass (2026-05-31) — approved, five edits applied

Q-B1…Q-B4 accepted as proposed; Q-B5 amended. Five edits, all applied below:

1. **World determinism invariant** added (§1b): a path classifies into exactly one world — never multiple, never zero.
2. **SchemaModel snapshot test** added to B1 (§6, §7): canonical schema-model dump stored as a snapshot so a `JsonElement?`→other-type change shows as a reviewable diff in PR.
3. **Drift guard simplified** (§5, Q-B3): compares the protocol's declared **key-constant set ↔ `KnownKeys`**, not factory implementation details. Q-B3 dissolves into Q-B2.
4. **Explicit provenance warning when protocol rules are unavailable** (§1a, Q-B5): the fail-open baseline stays, but the result/manifest records a warning naming the path so a support engineer never assumes rules ran when they didn't.
5. **`ExtraNameOverrides` treated as exceptional, not expected** (§3): most adapters start with an empty override map; overrides are added only when a real key justifies one.

## 0b. Resolved questions

- **Q-B1 → reflection-derived World boundary (locked).** A hardcoded seam list drifts the moment someone adds a new `JsonElement?`; reflection makes any new opaque property World 2 automatically — security by architecture.
- **Q-B2 → shared key constants + CI guard (locked).** No full factory inversion. The invariant "every parsed key has a tier" is delivered by both factory and rules referencing the same key constants, with CI asserting coverage.
- **Q-B3 → dissolved into Q-B2.** Because factories name keys only via the shared constants, `FactoryReadSet == ConstantSet` by construction; the drift guard compares `ConstantSet == KnownKeys` and never inspects factory internals.
- **Q-B4 → `IBundleRedactionRules` in Core (locked).** It is adapter *metadata* (`ProtocolName`, `KnownKeys`, `ExtraNameOverrides`), carrying only `BundleTier` (already in Core) + the protocol's own key names. Not redaction logic, not cross-protocol knowledge. Management owns composition/classification/execution; adapters own "what keys exist and their tier."
- **Q-B5 → fail-open baseline + explicit provenance warning (amended).** Outcome unchanged (a missing/garbage `protocolName` falls to World 2b baseline); observability improved (warning emitted).

---

## 1. The World boundary — derived from the typed graph (Q-B1 locked)

A property is an **opaque boundary (World 2)** iff it is typed `JsonElement?` or carries `[JsonExtensionData]`. Every other property is a **typed node (World 1)**. Computed by reflecting the `GatewayConfiguration` type graph (cached once, deterministically). Self-maintaining: a new typed field is World 1 automatically; a new opaque block is a boundary automatically.

Three opaque seams today (verified 2026-05-31):

| Path (schema) | Type | World |
|---|---|---|
| `sources[*].connection` | `JsonElement?` | 2 — protocol from sibling `protocolName` |
| `sinks[*].connection` | `JsonElement?` | 2 — protocol from sibling `protocolName` |
| `sinks[*].publishing` extras | `PublishingSettings` typed + `[JsonExtensionData] Extras` | 1 for typed keys; 2b for overflow |

Engine builds a **config schema model** (`TypedNode` carrying each property's `[BundleTier]`; `OpaqueBoundaryNode` marking where name-based classification takes over) and walks JSON + schema in lockstep.

## 1a. World 2a vs 2b — and the missing-rules warning (Q-B5 amended)

Inside an opaque boundary, the protocol (from sibling `protocolName`) decides 2a vs 2b:

- **2a — protocol has a registered `*BundleRedactionRules`.** Its `KnownKeys` classify known keys (fail-closed); unknown keys fall to 2b handling.
- **2b — no rules for the protocol, an unknown key, or a `[JsonExtensionData]` overflow key.** Baseline + protocol fail-open overrides; default INCLUDE.

**When no rules are found for a present `protocolName` (or it is missing/garbage), the engine emits a provenance warning** — e.g. `{ "path": "sources[0].connection", "warning": "Protocol rules unavailable; baseline classification applied." }` — carried on the redaction result and surfaced in the backup/bundle manifest. The fallback is correct; the warning prevents a support engineer assuming protocol rules executed when they didn't. (Adds a `Warnings` collection to `ConfigRedactionResult`; minor.)

## 1b. World determinism invariant (LOCKED)

> **Every JSON path classifies into exactly one world — World 1, World 2a, or World 2b. Never multiple, never zero.**

Asserted by a dedicated test. Prevents future "hybrid" classifications and guarantees the fail-direction (§2) is always well-defined for every byte the walker reaches.

---

## 2. Classification pipeline + fail-direction

| World | Mechanism | Source | Unclassified default |
|---|---|---|---|
| 1 (typed node) | **M1** | `[BundleTier]` on the typed property (reflection) | **STRIP** (fail-closed) |
| 2a (opaque, known protocol+key) | **M2** | protocol `KnownKeys` inventory | **STRIP** (fail-closed) |
| 2b (opaque overflow / unknown) | **M3** | baseline (`BackupSecretPatterns`) + protocol fail-open overrides | **INCLUDE** (fail-open) |

The M-A name-only walk **becomes the M3 layer** unchanged. MASK/STRIP tier *rendering* is already implemented (M-A); M-B only changes *which tier a property gets*.

---

## 3. `*BundleRedactionRules` — per-protocol single source of truth

`IBundleRedactionRules` lives in **Core** (Q-B4); each adapter module implements one:

```
// in ElpisEdgeConnect.Sinks.Mqtt (references Core for BundleTier + the interface)
public static class MqttConnectionKeys           // shared constants (Q-B2)
{
    public const string Password   = "password";
    public const string BrokerHost = "brokerHost";
    // ...one constant per connection key the protocol understands
}

public sealed class MqttBundleRedactionRules : IBundleRedactionRules
{
    public string ProtocolName => "mqtt";

    // EXHAUSTIVE over MqttConnectionKeys (enforced by the drift guard).
    public IReadOnlyDictionary<string, BundleTier> KnownKeys { get; } = new(...)
    {
        [MqttConnectionKeys.Password]   = BundleTier.Mask,
        [MqttConnectionKeys.BrokerHost] = BundleTier.Include,
        // ...
    };

    // Exceptional, not expected (edit 5): empty for most adapters.
    public IReadOnlyDictionary<string, BundleTier> ExtraNameOverrides { get; } =
        new Dictionary<string, BundleTier>();
}
```

- **Factory uses the same constants:** `MqttSinkConfiguration.FromSinkInstance` reads `conn[MqttConnectionKeys.Password]`. Because keys are only ever named via constants, `FactoryReadSet == ConstantSet` by construction (Q-B3).
- **Composition:** a Management registry discovers all `IBundleRedactionRules` (DI) + the baseline, keyed by `ProtocolName`. Registry/engine in Management; rules metadata in adapters; interface in Core.

---

## 3a. Locked adapter-redaction pattern (B3 review, 2026-05-31)

The Mqtt template (`daad534`) was reviewed and **approved as the shape to replicate
across all 9 adapters**, with two amendments. Every adapter follows this exactly:

```
ProtocolConnectionKeys.All          // ordered IReadOnlyList<string> of every key the factory reads
ProtocolBundleRedactionRules.KnownKeys  // exhaustive over .All; tier per key
KnownKeys == All test               // key-coverage drift guard
Factory reads only ProtocolConnectionKeys.*   // "can't parse a key without a tier"
```

**Amendment 1 — `All` is an ordered `IReadOnlyList<string>`** (not a `HashSet`), so it
renders deterministically in snapshots/manifests/diffs.

**Amendment 2 — where each key's tier belongs (locked rule table):**

| Case | Where it belongs |
|---|---|
| Factory reads the key | `KnownKeys` + a `ProtocolConnectionKeys` constant |
| Factory does NOT read it but the name is secret-shaped (e.g. OPC UA `certificatePassword`) | `ExtraNameOverrides` |
| Common secret name across protocols (`password`, `apiKey`, …) | shared baseline (`BackupSecretPatterns`) |
| File / private-key material (PEM bodies, `privateKey`) | tier = `Strip` |

`KnownKeys` means *"keys the factory understands and parses."* Putting a
factory-unread key there weakens the drift guard, so it goes in `ExtraNameOverrides`.

**Discipline (review rule):** `ExtraNameOverrides` must not become a dumping ground —
**every entry carries an inline comment explaining why it is not in `KnownKeys`.**

**Resolution granularity:** the registry resolves opaque keys by **leaf name** (the
walker matches `password`, `certificatePassword` at any nesting depth). This is
sufficient for the current protocols' distinctive secret names. If a future protocol
has a benign key colliding with a secret leaf name in a different context, add
path-aware resolution (`credentials.certificatePassword`) before that protocol's rules
land — not needed for the B3/B4 set.

## 4. Engine refactor (schema-aware walker)

`ConfigRedactionEngine.Redact` gains an overload taking a **classification context** (schema model + protocol registry). The M-A parameterless walk stays as the M3-only path (arbitrary fragments). The walker descends JSON + schema in lockstep: `TypedNode` → M1 per property; `OpaqueBoundaryNode` → read sibling `protocolName`, switch to M2→M3 name-walk; `[JsonExtensionData]` node → typed keys M1, overflow keys M3. Determinism invariant (§1c of the substrate plan) preserved — reflection cached once, traversal order unchanged. Result gains `Warnings` (§1a).

---

## 5. Drift guard (R-2) — declaration-driven, fails CI

A test in `ElpisEdgeConnect.Management.Tests`:

1. **Boundary coverage:** reflect `GatewayConfiguration`; every typed property has a `[BundleTier]` (no unattributed World-1 field, which would silently STRIP).
2. **Key coverage (per protocol):** the protocol's **declared key-constant set == `KnownKeys` keyset** (Q-B3). No inspection of factory internals — the shared-constant discipline (§3) makes `FactoryReadSet == ConstantSet` hold by construction.
3. **Secret-typed safety:** no field whose name indicates a secret resolves to INCLUDE.

---

## 6. Sequencing (sub-milestones)

1. **B1 — `IBundleRedactionRules` (Core) + registry (Management) + schema-model reflection + engine schema-aware overload + `SchemaModel_Dump_IsStable` snapshot test (edit 2).** Baseline still classifies; engine routes World 1 (M1) vs World 2 (M3) correctly. Green.
2. **B2 — `[BundleTier]` on all Core typed records** (M1 real) + drift guard part 1 (boundary coverage). Green.
3. **B3 — Mqtt + OpcUaClient rules** (shared key constants, `KnownKeys`, factory references constants) + drift guard part 2 (key coverage). **Review checkpoint before fan-out.**
4. **B4 — fan out** to Focas2, ModbusTcp, MTConnect, BrotherHttp, S7, OpcUaServer sink, EthernetIp.
5. **B5 — drift guard part 3** (secret-typed safety) + World determinism test + full-graph integration test.

Each sub-milestone: 0 warnings, deterministic tests, backup output re-verified unchanged for non-secret data.

---

## 7. Test strategy

- **SchemaModel snapshot (B1):** canonical dump stored; reflection finds exactly the 3 seams; a new typed field appears as World 1; a new `JsonElement?` appears as a boundary — all visible as snapshot diffs.
- **World determinism (§1b):** every path → exactly one world.
- **Pipeline:** World-1 unattributed → STRIP; 2a known secret → its tier; 2a unknown → fail-open; 2b overflow → fail-open + baseline.
- **Missing-rules warning:** absent/garbage `protocolName` → baseline applied **and** a provenance warning emitted.
- **Per-protocol:** every secret connection key → MASK/STRIP; benign → INCLUDE.
- **Drift guard:** the three coverage assertions.
- **Backup regression:** full sample gateway.json (secrets in connection blocks + operator extras) → secrets redacted, extras preserved, restore-prompting intact.

---

## 8. Risks

- **Schema-aware walker is the most intricate code in the effort.** Mitigate: B1 lands the walker with baseline-only classification first + the snapshot test, so the boundary logic is pinned before any tier decision rides on it.
- **9-factory key-constant refactor (Q-B2).** Largest surface; B3 establishes the pattern on 2 protocols under review before B4 fan-out.
- **Layering** — `IBundleRedactionRules` in Core, rules in adapters, registry/engine in Management. Strict Core ← adapters ← Management; the drift guard also guards that no rules type references Management.

---

**Next step:** plan is review-approved. On operator go → start **B1**.
