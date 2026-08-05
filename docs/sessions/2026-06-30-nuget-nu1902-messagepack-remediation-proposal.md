# NU1902 / MessagePack build blocker — remediation record

**Date:** 2026-06-30
**Status:** ACCEPTED — fix applied in **PR #162**
(https://github.com/elpisitsolutions/EdgeConnect/pull/162); merge gated on green CI.
**Scope:** Repo-wide security/build blocker. Landed as its own commit/PR,
**separate from the MELSEC feature** (not on `feat/melsec-source`).
**Owner:** Core/runtime DRI.

## 1. Problem

A fresh `dotnet restore`/`build` **fails for every project in the solution**:

```
error NU1902: Warning As Error: Package 'MessagePack' 2.5.187 has a known
moderate severity vulnerability
```

`TreatWarningsAsErrors=true` promotes the NuGet-audit warning to an error at
restore time. This blocks the documented "0 warnings / 0 errors" gate for
**everyone, including Sony's `Sony_Development` and any CI**. Confirmed it is
**pre-existing, not caused by feature work** — the existing S7 project fails
identically. It surfaced now because a fresh restore pulls current advisory data
(new-laptop context in CLAUDE.md §8).

## 2. Root cause

`MessagePack` is a **direct** `PackageReference` in
`src/ElpisEdgeConnect.Core/ElpisEdgeConnect.Core.csproj:46` (v2.5.187), used by the
Buffer subsystem (C2a serialization/compression). Every other project gets it
transitively through Core, so all of them fail.

`dotnet list package --vulnerable` reports **2 High + 9 Moderate** advisories
against MessagePack 2.5.187. Core already suppresses the **High** `NU1903`
(`Core.csproj:31-36`, GHSA-hv8m-jj95-wg3x) from a prior "chip-3" surgical unblock,
with a comment deferring "the proper MessagePack 2.x → 3.x upgrade." The build
default audit mode audits **direct** dependencies only, so the direct-dep
MessagePack **Moderate** advisories now fire as `NU1902` errors (not covered by the
`NU1903` suppression).

Dependency path (`dotnet nuget why`): `MessagePack (v2.5.187)` — top-level, no parent.

## 3. Fix applied (real dependency upgrade — no policy exception)

**MessagePack `2.5.187` → `2.5.302` in `Core.csproj`.** This is an **in-line
2.5.x patch upgrade — NOT the feared 2.x → 3.x breaking migration.**

- The blocking advisories are **fixed in 2.5.301** on the v2 branch (verified from
  the advisories, §5). `2.5.302` is the current highest 2.5.x.
- Same `2.5` minor line → no API break expected; Core's MessagePack usage
  (Buffer serialization/LZ4) is unchanged.
- The 3.x migration the earlier comment feared is **avoided entirely.**

**Also removed the now-dead `NU1903` from `Core.csproj` NoWarn** — 2.5.302 fixes
GHSA-hv8m-jj95-wg3x too, so keeping the suppression would silently mask any
*future* MessagePack High advisory. (`CA1720;CA1822` left untouched.)

No `NuGetAudit=false` and no audit-policy exception is required — a real fix exists,
so §5-of-the-brief's temporary-exception fallback does **not** apply.

## 4. Validation (on `fix/nuget-nu1902-messagepack`)

With MessagePack at 2.5.302 and the NU1903 suppression removed, all **without** any
audit flag:

- `dotnet list package --vulnerable --include-transitive`: **MessagePack no longer
  appears** — all 11 advisories cleared. (Only the transitive SQLitePCLRaw item in
  §6 remains.)
- **Full-solution `dotnet build`: 0 errors.** The only 2 warnings are pre-existing
  legacy `CS1998` in `ElpisEdgeConnect/Services/MachineManagerService.cs`, unrelated
  to this change (that legacy project does not set `TreatWarningsAsErrors`).
- **`Core.Tests`: 969/969 passed** (exercises the MessagePack-backed
  Buffer/SqliteBuffer serialization round-trip — no regression from the bump).
- **`Host.Tests`: 211/211 passed** (incl. the redaction drift guard).

## 5. Advisory references (patched-in versions)

| Advisory | Severity | Fixed in (v2) | Note |
|----------|----------|---------------|------|
| GHSA-hv8m-jj95-wg3x (CVE-2026-48109) | High | 2.5.301 | LZ4 decompression OOB read (was NU1903-suppressed; suppression removed by this fix) |
| GHSA-2f33-pr97-265q (CVE-2026-48509) | Moderate | 2.5.301 | Insecure default in `MessagePackInputFormatter` (hash-collision DoS) |
| (remaining 1 High + 8 Moderate) | High/Mod | ≤ 2.5.301/302 | All cleared per `list --vulnerable` after bump (§4) |

Exposure note: the High LZ4 CVE affects deserializing **untrusted** LZ4 data.
Core's Buffer is a **local, self-produced** SQLite store-and-forward (trusted
input), so real-world exploitability is low — but the upgrade is still the correct
fix and clears the build gate.

## 6. Secondary finding (NOT this fix — track separately)

`SQLitePCLRaw.lib.e_sqlite3 2.1.6` — **transitive** via `Microsoft.Data.Sqlite
8.0.10` → `SQLitePCLRaw.bundle_e_sqlite3 2.1.6` — carries a **High** advisory
(GHSA-2m69-gcr7-jv3q, bundled SQLite < 3.50.2 memory corruption). It is
**transitive**, so the default (direct) audit mode does **not** fail the build on
it today. The advisory lists **no patched SQLitePCLRaw version yet** (needs a build
bundling SQLite ≥ 3.50.2). **Recommendation:** track it; bump `Microsoft.Data.Sqlite`
(and/or pin a patched `SQLitePCLRaw.bundle_e_sqlite3`) when a fixed native bundle
ships. Do **not** couple it to the MessagePack fix.

## 7. Rollout (as applied)

1. Branch `fix/nuget-nu1902-messagepack` off master (separate from
   `feat/melsec-source`). ✓
2. `Core.csproj`: MessagePack `2.5.187` → `2.5.302`; dropped `NU1903` from NoWarn. ✓
3. Full-solution `dotnet build` (no audit flag) 0 errors; `Core.Tests` +
   `Host.Tests` green (§4). ✓
4. Committed + pushed as **PR #162**. **Pending:** merge to master once CI is green;
   then `feat/melsec-source` rebases on it (unblocks the MELSEC 0/0 gate) and
   `Sony_Development` picks it up.

## 8. Scope guardrails honored

- **Only** `Core.csproj` (dependency version + NoWarn) changed for the fix — no
  source or behavior change.
- **No** `NuGetAudit=false` and **no** weakened `NuGetAuditMode` / audit-policy
  exception.
- **Not** landed on the MELSEC feature branch; kept as its own repo-wide PR.
- The secondary transitive SQLitePCLRaw advisory (§6) is deliberately **not**
  bundled into this fix.
