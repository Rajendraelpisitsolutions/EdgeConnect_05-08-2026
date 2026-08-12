# Chip 3 — Provisioning Subsystem v3 reality-check

**Status:** v3 reality-check **COMPLETE** — 2026-05-21
**Form:** Resolves the five open questions from [chip-3 v2 plan §10](2026-05-21-chip3-provisioning-subsystem-plan-v2.md). No ChatGPT pass — v3 reality-check is internal-source-reading only per the v2 plan's locked cadence.
**Predecessor:** [v2 plan, LOCKED](2026-05-21-chip3-provisioning-subsystem-plan-v2.md)

---

## 0. Resolution summary

| # | Question | Resolution | Impact on v2 plan |
|---|---|---|---|
| **Q-V1-A** | NJsonSchema PS-interop vs shell-out | **Build `tools/ValidateConfig/` CLI; PS wrapper shells out.** No existing validator tool. | +~0.5 day to implementation |
| **Q-V1-B** | Canonical parser accepts unknown `_`-prefix roots | **Parser ignores them silently → DOES NOT preserve through round-trip.** ADR-0030 + small Core change to `GatewayConfiguration` is a **Chip-3 precondition**, not in-band. | +~2-3 days; ADR lands before step 3 of §9 sequence |
| **Q-V1-C** | Customer PowerShell version | **Defer.** v1's PS 5.1 floor stands until customer engineering confirms. | None — confirmed assumption |
| **Q-V2-A** | TagMap iteration order | **Non-issue.** Pure literal substitution in templates (§5.1.4 anti-templating-engine boundary) means catalog order is template-authoring discipline, not runtime concern. | None |
| **Q-V2-B** | `pwsh.exe` from xUnit reliable on CI | **No CI configured today.** Tests run locally on Windows where pwsh is reliably available. | None today; non-blocking for future CI |
| **Q-V2-C** | ADR-0030 number | **0030** (plan's tentative "0015" was based on stale assumption; many ADRs landed since v2 plan was written). | ADR file naming only |
| **Q-V2-D** | Reserve `_diagnostics` alongside `_provisioning` | **Yes.** Confirmed; landed in ADR-0030. | ADR scope addition |
| **Q-V2-E** | `_provisioning.csvLineNumber` debug field | **Defer to future-work.** Keep block stable at 9 fields for v1. | None |

**Net effect on v2 scope**: +~2.5-3 days to implementation estimate (ADR-0030 + Core change for Q-V1-B + `ValidateConfig` CLI for Q-V1-A). v2 estimate of 1.5-2 weeks becomes **2-2.5 weeks**. Scope itself unchanged; the additions are correctness preconditions.

---

## 1. Detailed findings

### 1.1 Q-V1-B: parser behavior on unknown `_` roots

**Source read:** [`src/ElpisEdgeConnect.Core/Configuration/ConfigurationManager.cs:48-54`](src/ElpisEdgeConnect.Core/Configuration/ConfigurationManager.cs#L48):

```csharp
private static readonly JsonSerializerOptions JsonOptions = new()
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = { new JsonStringEnumConverter() },
};
```

`JsonOptions` does NOT set `UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow`. System.Text.Json's default behavior is **silent ignore** of unknown members during deserialize. So:

- ✅ Parser does NOT throw / reject when a JSON file contains `_provisioning` at the root → no error path during draft create or apply.
- ❌ Parser does NOT preserve `_provisioning` either. After `JsonSerializer.Deserialize<GatewayConfiguration>(json, JsonOptions)`, the `_provisioning` data is **silently dropped** — there's no `[JsonExtensionData]` field on `GatewayConfiguration` to capture it.

**Round-trip impact** (lines 197, 352, 393, 433, 477 in `ConfigurationManager.cs`):

```
operator-edited JSON with _provisioning
   ↓ JsonSerializer.Deserialize → typed GatewayConfiguration  (_provisioning DROPPED)
   ↓ ConfigurationManager creates draft
   ↓ JsonSerializer.Serialize → re-serialized JSON for draft storage  (_provisioning ABSENT)
   ↓ Validate → Apply → current.json (no _provisioning)
```

This is exactly the precondition path the v2 plan §10.3 anticipated.

**Required Core change** (lands before step 3 of v2 §9 sequence):

Add `[JsonExtensionData]`-annotated property to `GatewayConfiguration` to preserve unknown roots through round-trips:

```csharp
public sealed record GatewayConfiguration
{
    // ... existing required fields ...

    /// <summary>
    /// Captures unknown root-level JSON members (e.g. <c>_provisioning</c>) so
    /// they survive deserialize → serialize round-trips. Reserved <c>_</c>-prefix
    /// namespace governed by ADR-0030.
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; init; }
}
```

Tests: 2 new — one round-trip test asserting `_provisioning` survives, one negative test asserting non-`_`-prefixed unknown keys do NOT survive (preserves typo-protection on canonical keys).

### 1.2 Q-V1-A: schema validation from PowerShell

**Source read:**
- [`tools/SchemaGen/Program.cs`](tools/SchemaGen/Program.cs) — emits schemas; does not validate against them.
- No `tools/ValidateConfig/` or analogous CLI exists.
- [`src/ElpisEdgeConnect.SchemaValidation/NJsonSchemaConfigurationValidator.cs`](src/ElpisEdgeConnect.SchemaValidation) — the validator class used by `ConfigurationManager` internally.

**Resolution**: build a new `tools/ValidateConfig/` CLI alongside the chip-3 implementation. Pattern matches existing `tools/ModbusCsvImport/` and `tools/SchemaGen/`. Single-file `Program.cs`:

```csharp
// tools/ValidateConfig/Program.cs (~50 LOC)
// Usage: dotnet run --project tools/ValidateConfig -- <path-to-gateway.json>
// Exit codes: 0 valid, 1 schema-violation, 2 file-not-found, 3 unexpected-error
```

PS wrapper `tools/bulk-provision/lib/Validate-AgainstSchema.ps1` shells out via:

```powershell
$result = & dotnet run --project tools/ValidateConfig -- $JsonPath 2>&1
if ($LASTEXITCODE -ne 0) { throw "Schema validation failed: $result" }
```

Adds `tools/ValidateConfig/` to v2 §7 deliverables list.

### 1.3 Q-V2-A: catalog iteration order

**Source read:**
- [`src/ElpisEdgeConnect.Sources.Focas2/Focas2TagMap.cs:352`](src/ElpisEdgeConnect.Sources.Focas2/Focas2TagMap.cs#L352): `internal static readonly FrozenSet<TagMapEntry> StaticTags = new TagMapEntry[] { ... }.ToFrozenSet();`
- [`src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherTagMap.cs:439`](src/ElpisEdgeConnect.Sources.BrotherHttp/BrotherTagMap.cs#L439): identical pattern.

Both catalogs use `FrozenSet<>` which does not guarantee iteration order. This WOULD matter if a code-driven generator dynamically read the catalog at runtime.

**Resolution as non-issue:** v2 §5.1.4 (anti-templating-engine boundary) locks the generator to pure literal substitution. Tag paths are LITERAL STRINGS in `template-fanuc-v1.json` / `template-brother-v1.json`, ordered by human author at template-authoring time. JSON array order is preserved by System.Text.Json round-trips. The "catalog order, not alphabetical" guidance in v2 §5.4.2 applies to template authoring discipline (human author groups paths by category for readability), not runtime ordering.

No code change needed. Catalog order question dissolves under the anti-templating-engine boundary.

### 1.4 Q-V2-B: pwsh.exe invocation from xUnit

**Source read:** searched for `.github/workflows/`, `azure-pipelines.yml`, `ci.yml`, `gitlab-ci.yml`. **None exist**.

**Resolution**: no CI to constrain today. Tests run on developer Windows machines. pwsh.exe (PS 7+) is reliably available; powershell.exe (PS 5.1) is the fallback. The C# E2E test in `ProvisioningSubsystemTests.cs` detects which is present:

```csharp
var pwsh = File.Exists(@"C:\Program Files\PowerShell\7\pwsh.exe")
    ? @"C:\Program Files\PowerShell\7\pwsh.exe"
    : "powershell.exe";  // PS 5.1 fallback on every Windows install
```

If CI is added later (GitHub Actions Windows runners have pwsh; Linux runners would need `apt install powershell`), small documentation update needed at that time. Non-blocking now.

### 1.5 Q-V2-C: ADR number

**Source read:** `ls docs/decisions/` — most recent ADR is `0029-s7-demo-mode.md`.

**Resolution**: ADR-0030 is the next available number. The v2 plan's tentative "0015" was stale by ~15 ADRs.

### 1.6 Q-V1-C: customer PowerShell version

Not resolvable without customer engineering confirmation. v1 plan's PS 5.1 floor is safe. Note for install playbook.

### 1.7 Q-V2-D + Q-V2-E

- **Q-V2-D**: ADR-0030 explicitly names `_diagnostics` as a reserved future namespace alongside `_provisioning`. Costs nothing, prevents accidental reuse by the future Operational Intelligence layer.
- **Q-V2-E**: `_provisioning.csvLineNumber` deferred to future-work per recommendation. `_provisioning` block stays at 9 fields for v1.

---

## 2. Updated implementation sequence

Insert ADR-0030 + Core change as **steps 2a and 2b** before step 3 of v2 §9 sequence:

| # | Step | Notes |
|---|---|---|
| 1 | v3 reality-check pass | ✅ This document |
| **2** | **ADR-0030 — reserved underscore namespace** | Lock `_provisioning` + `_diagnostics` as `_`-prefix reserved roots |
| **2a** | **Core change: `GatewayConfiguration.ExtensionData`** | Add `[JsonExtensionData]` to preserve unknown roots. ~5 LOC + 2 tests. |
| **2b** | **Core change: small test pass** | Run Core tests to verify no regression. |
| 3 | Project scaffolding — `tools/bulk-provision/` folders | No `.csproj`. |
| **3a** | **New `tools/ValidateConfig/` CLI** | ~50 LOC C#. Wraps `NJsonSchemaConfigurationValidator`. |
| 4-10 | Per v2 §9 unchanged | Canonicalization helper, templates, MANIFEST, CLI skeleton, rendering pipeline |
| 11 | Validation pipeline — `Validate-AgainstSchema.ps1` | Shells out to `tools/ValidateConfig/` from step 3a. |
| 12-18 | Per v2 §9 unchanged | Provenance, samples, schema, Pester, C# E2E, README, sweep |

**Updated estimate**: 2-2.5 weeks (was 1.5-2). Two sessions still feasible: session 1 = steps 1-10 (including ADR + Core change + ValidateConfig CLI), session 2 = steps 11-18.

---

## 3. Updated DoD additions

To v2 §8 DoD:

- [ ] **ADR-0030 landed** documenting reserved `_`-prefix namespace + initial members (`_provisioning`, `_diagnostics`).
- [ ] **`GatewayConfiguration.ExtensionData` round-trip test**: a JSON file with `_provisioning` survives `JsonSerializer.Deserialize → Serialize` byte-equivalent.
- [ ] **Negative test**: a JSON file with a non-`_`-prefixed unknown root key (e.g. `typo_sources`) is silently dropped on round-trip (preserves typo-protection on canonical keys).
- [ ] **`tools/ValidateConfig/` CLI builds + runs**: `dotnet run --project tools/ValidateConfig -- docs/samples/gateway-modbus-s7-1200.json` exits 0. Same against a deliberately invalid JSON exits non-zero.

---

## 4. Items NOT changing in v2

The following v2 §3 / §4 / §5 locks are unaffected by this v3 reality-check:

- All `_provisioning` block field semantics + counts (9 fields).
- Template schema + naming convention.
- Route generation strategy (§5.1.1).
- MQTT sink placeholder injection (§5.1.2).
- Anti-templating-engine boundary (§5.1.4).
- Canonicalization spec (§5.4.1) + array ordering (§5.4.2) + timezone/locale immunity (§5.4.3).
- Deterministic-output guarantee (§5.4.4).
- Generator CLI shape (§5.5).
- v2.3 §1.1 no-new-shared-abstractions + §1.2 terminology freeze + §1.3 platform-contracts-deferred.
- v1 → v2 locked decision verdicts (Q-V1-D through Q-V1-G remain as v2 left them).
- Out-of-scope deferrals (§2 "What this is NOT").

---

## 5. Implementer's starting point

The next session that picks up Chip 3 implementation:

1. **Reads this document first** (~10 min).
2. **Writes ADR-0030** with the `_`-prefix reserved namespace rule + initial members + parser contract (preserve unknown `_*` roots; ignore non-`_`-prefixed unknown keys).
3. **Implements the Core change** — add `[JsonExtensionData]` to `GatewayConfiguration`. 2 new round-trip tests in `tests/ElpisEdgeConnect.Core.Tests/`.
4. **Builds `tools/ValidateConfig/`** — ~50 LOC C# CLI + project file. Runs `dotnet build` to confirm.
5. **Proceeds with v2 §9 steps 3-18** unchanged.

End of session 1 marker: ADR-0030 merged + Core change merged + `tools/ValidateConfig/` building. Session 2 picks up at v2 §9 step 11.

---

**End of v3 reality-check. v2 plan + this document are the implementation contract. No v3 ChatGPT pass; v3 reality-check IS the v3.**
