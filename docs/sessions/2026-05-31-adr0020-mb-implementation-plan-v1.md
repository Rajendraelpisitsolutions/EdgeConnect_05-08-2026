# ADR-0020 redaction engine — M-B implementation plan v1

**Status:** v1 — DRAFT, pre-review. Detailed execution plan for milestone **M-B** only. Awaiting a ChatGPT review pass before code, per the plan-trail cadence.
**Date:** 2026-05-31
**Implements:** `docs/decisions/0020-diagnostic-bundle-redaction-spec.md` (Accepted, as amended) + the M-B row of `2026-05-31-adr0020-redaction-implementation-plan-v2.md` §3.
**Branch:** `feat/bundle-redaction-spec`
**Builds on:** M-A (commit `38be433`) — tier-aware `ConfigRedactionEngine`, `[BundleTier]` attribute, name→tier baseline, backup unified, determinism invariant.

---

## 0. Scope

M-B turns the M-A engine from a **name-only** redactor (baseline allowlist) into a **schema-aware** one that distinguishes the three worlds of ADR-0020 Amendment 1 and applies the right mechanism + fail-direction to each. It also lands `[BundleTier]` placement on Core records and the per-protocol rules (both deferred from M-A).

**In scope:** the World boundary (§1); M1/M2/M3 classification pipeline (§2); `*BundleRedactionRules` per adapter + composition (§3); engine refactor to a schema-aware walker (§4); the R-2 drift guard (§5). **Out of scope:** the secret-shape detector (R-1 → M-C), bundle ZIP/preview workflow (downstream).

---

## 1. The World boundary — derived from the typed graph, not hardcoded

**Core design decision (proposed): the World boundary is computed by reflecting the `GatewayConfiguration` type graph, not by hardcoding paths.** A property is an **opaque boundary (World 2)** iff it is typed `JsonElement?` or carries `[JsonExtensionData]`. Every other property is a **typed node (World 1)**. This is self-maintaining: a new typed field is World 1 automatically; a new opaque block is a World 2 boundary automatically.

The persisted graph has exactly **three** opaque seams today (verified 2026-05-31):

| Path (schema) | Type | World |
|---|---|---|
| `sources[*].connection` | `JsonElement?` | 2 — protocol from sibling `protocolName` |
| `sinks[*].connection` | `JsonElement?` | 2 — protocol from sibling `protocolName` |
| `sinks[*].publishing` (extras) | `PublishingSettings` typed + `[JsonExtensionData] Extras` | 1 for typed keys; 2b for overflow keys |

Everything above/around these (`gateway`, the typed fields of `sources[*]`/`sinks[*]`, `routes[*]`, nested `PollingSettings`/`WatchdogSettings`/etc.) is World 1.

**Engine builds a "config schema model" once** (reflection over `GatewayConfiguration`, cached): a tree of `TypedNode` (carrying each property's `[BundleTier]`) and `OpaqueBoundaryNode` (marking where name-based classification takes over). The walker carries the schema node alongside the JSON node as it descends.

> **Open question Q-B1 (for review):** reflection-derived boundary (self-maintaining, aligns with "security by architecture") vs a small hardcoded boundary set (simpler, explicit, only 3 seams today). Recommendation: reflection-derived, because it makes the drift guard (R-2) trivial and can't silently miss a future opaque block. Cost: more machinery up front.

## 1a. World 2a vs 2b — does the opaque block have a typed counterpart?

Inside an opaque boundary, the protocol (from sibling `protocolName`) decides 2a vs 2b:

- **2a — protocol has a registered `*BundleRedactionRules`** (Mqtt, OpcUaClient, Focas2, …). Its declared key→tier inventory classifies known keys (fail-closed); unknown keys in that block fall to 2b handling.
- **2b — no rules for the protocol, OR a key not in the protocol's inventory, OR a `[JsonExtensionData]` overflow key.** Baseline allowlist + the protocol's fail-open extra names classify; default INCLUDE.

---

## 2. Classification pipeline + fail-direction

For each JSON property the walker reaches, tier is resolved by the first applicable mechanism:

| World | Mechanism | Source | Unclassified default |
|---|---|---|---|
| 1 (typed node) | **M1** | `[BundleTier]` on the typed property (reflection) | **STRIP** (fail-closed) |
| 2a (opaque, known protocol+key) | **M2** | protocol's `*BundleRedactionRules` key→tier inventory | **STRIP** (fail-closed) |
| 2b (opaque overflow / unknown) | **M3** | baseline (`BackupSecretPatterns`) + protocol fail-open extras | **INCLUDE** (fail-open) |

This is the A1.2 / A1.3 split made executable. The M-A engine's name-only walk **becomes the M3 layer** unchanged — no work thrown away.

**MASK/STRIP semantics** (tier outcomes) are already implemented in M-A; M-B only changes *which tier a property gets*, not how a tier renders.

---

## 3. `*BundleRedactionRules` — the per-protocol single source of truth

Each adapter module declares one rules type (metadata-only — must not reference any Management type, per plan v2 §1c):

```
// in ElpisEdgeConnect.Sinks.Mqtt (references Core for BundleTier only)
public sealed class MqttBundleRedactionRules : IBundleRedactionRules
{
    public string ProtocolName => "mqtt";

    // Every connection key the protocol understands + its tier.
    // EXHAUSTIVE over the protocol's known keys (enforced by R-2).
    public IReadOnlyDictionary<string, BundleTier> KnownKeys { get; } = new(...) {
        ["brokerHost"] = Include, ["brokerPort"] = Include, ["clientId"] = Include,
        ["username"] = Include, ["password"] = Mask, ...
    };

    // Fail-open extras for unknown keys beyond the baseline (usually empty).
    public IReadOnlyDictionary<string, BundleTier> ExtraNameOverrides { get; } = new(...);
}
```

- **Composition:** a Management-side registry discovers all `IBundleRedactionRules` (DI) + the baseline, keyed by `ProtocolName`. `IBundleRedactionRules` lives in **Core** (so adapter modules implement it without referencing Management); the registry that *composes* them lives in Management.
- **Q-1 "a key can't be parsed without a tier — by construction":** each protocol defines its connection key names as **shared string constants** that BOTH `From*Instance` and `KnownKeys` reference. The factory reads via the constant; `KnownKeys` is exhaustive over the constants; R-2 fails CI if any constant lacks a tier or any factory-read key isn't a constant. Parsing therefore cannot reach a key that has no tier. (Full factory-inversion — factory literally loops the rules — is heavier and **deferred**; this gives the same guarantee at the key-name level. **Q-B2 for review.**)

---

## 4. Engine refactor (schema-aware walker)

- `ConfigRedactionEngine.Redact(json)` gains an overload taking a **classification context** (the schema model + protocol registry). The current parameterless walk stays as the M3-only path (used where there is no schema, e.g. redacting an arbitrary fragment).
- The walker descends JSON and schema in lockstep. At a `TypedNode`: classify each property by M1. At an `OpaqueBoundaryNode`: read sibling `protocolName`, switch to M2→M3 name-walk for that subtree. For a `[JsonExtensionData]` node: typed keys → M1, overflow keys → M3.
- **Determinism invariant (§1c) preserved:** schema reflection is computed once and cached deterministically; traversal order unchanged.

---

## 5. Drift guard (R-2)

A declaration-driven test in `ElpisEdgeConnect.Management.Tests` (per O-4: fails CI):

1. **Boundary coverage:** reflect `GatewayConfiguration`; assert every typed property has a `[BundleTier]` (no unattributed World-1 field → would silently STRIP).
2. **Factory coverage (per protocol):** assert every connection key the `From*Instance` factory reads is a declared key constant, and every constant is in `KnownKeys` with a tier. (Enumerate factory reads via the shared key constants — see Q-1.)
3. **Secret-typed safety:** assert no field whose name/type indicates a secret resolves to INCLUDE.

> **Q-B3 for review:** how the test enumerates "keys the factory reads." Cleanest if the shared key constants are the only way the factory names a key (then the test asserts `constants ⊇ factoryReadSet` trivially because they're identical by construction). Confirms the Q-1 mechanism.

---

## 6. Sequencing (sub-milestones)

1. **B1 — `IBundleRedactionRules` (Core) + registry (Management) + schema-model reflection + engine schema-aware overload.** No protocol rules yet; baseline still classifies. Engine routes World 1 (M1) vs World 2 (M3) correctly. Green.
2. **B2 — `[BundleTier]` on all Core typed records** (M1 becomes real). Drift guard part 1 (boundary coverage). Green.
3. **B3 — Mqtt + OpcUaClient rules** (the reviewed pattern: shared key constants, `KnownKeys`, factory references constants). M2 live for these two. Drift guard part 2 (factory coverage). **Review checkpoint before fan-out.**
4. **B4 — fan out** to Focas2, ModbusTcp, MTConnect, BrotherHttp, S7, OpcUaServer sink, EthernetIp.
5. **B5 — drift guard part 3** (secret-typed safety) + full-graph integration test.

Each sub-milestone: 0 warnings, deterministic tests, backup output re-verified unchanged for non-secret data.

---

## 7. Test strategy

- **Schema model:** reflection finds exactly the 3 opaque seams; a new typed field appears as World 1; a new `JsonElement?` appears as a boundary.
- **Pipeline:** World-1 unattributed → STRIP; World-2a known secret → its tier; World-2a unknown key → fail-open; World-2b overflow → fail-open + baseline.
- **Per-protocol:** every secret connection key for each adapter → MASK/STRIP; benign → INCLUDE.
- **Drift guard:** the three coverage assertions (the cross-cutting safety net).
- **Backup regression:** a full sample gateway.json with secrets in connection blocks + operator extras → secrets redacted, extras preserved, restore-prompting intact.

---

## 8. Open questions for the review pass

- **Q-B1** — reflection-derived World boundary vs hardcoded 3-seam set. (Recommend reflection.)
- **Q-B2** — Q-1 "by construction": shared key constants + CI guard (proposed) vs full factory inversion (deferred). Is the key-constant guarantee strong enough to satisfy "cannot parse a key without a tier"?
- **Q-B3** — drift-guard enumeration of factory-read keys (ties to Q-B2).
- **Q-B4** — does `IBundleRedactionRules` belong in Core (so adapters implement it without a Management ref) — confirm this doesn't smuggle redaction policy into Core. It carries only `BundleTier` (already in Core) + the protocol's own key names, so it is adapter-local metadata, not cross-protocol knowledge. Confirm the layering reading.
- **Q-B5** — `protocolName` is read from the JSON sibling to pick the protocol's rules. If `protocolName` is itself missing/garbage in a malformed config, the block falls to 2b (fail-open baseline). Acceptable? (Recommend yes — fail-open + baseline still redacts known secret names.)

---

## 9. Risks

- **Schema-aware walker is the most intricate code in the whole effort** — the M-A discovery is precisely this boundary. Mitigate: B1 lands the walker with baseline-only classification first, so the boundary logic is tested before any tier decision rides on it.
- **9-factory key-constant refactor (Q-1)** — largest surface; B3 establishes the pattern on 2 protocols under review before B4 fan-out.
- **Layering** — `IBundleRedactionRules` in Core, rules in adapters, registry/engine in Management. Strict Core ← adapters ← Management; the drift guard also guards that no rules type references Management.

---

**Next step:** route this M-B v1 through a ChatGPT review pass; produce v2; then start B1. No code until reviewed.
