# Single-Machine Licensing — Uses, Implementation & DPAPI Seal

**Purpose:** step-wise guide to how EdgeConnect locks a license to one machine, how
to use it, and how to make the machine identity **copy-proof** with a Windows DPAPI
seal.
**Status:** the binding is implemented (ADR-0036). The DPAPI seal (§4) is the
proposed hardening (closes the "soft identity" limitation).

---

## 1. What "single-machine licensing" means

A license is bound to **one gateway**. The license file carries a `gatewayId`; at
load the runtime compares it to the gateway's own identity and **rejects** the
license if they differ. A special `gatewayId` of `*` means "any machine" (floating).

- One machine → one license.
- The same `license.json` **will not activate** on a different machine
  (`CORE.LICENSE_GATEWAY_MISMATCH`).

---

## 2. Uses (when to use it)

| Use case | Gateway ID to issue for |
|----------|-------------------------|
| Per-customer / per-site license that must not be reused elsewhere | that gateway's real identity id |
| Trial / evaluation locked to one machine | that machine's id |
| Preventing one purchased license from being copied across a fleet | per-machine ids (one license each) |
| Deliberate multi-machine / floating license | `*` |

**Benefit:** revenue protection — a customer can't buy one license and run it on many
gateways; each machine needs its own license.

---

## 3. Implementation (as-built, step by step)

### 3.1 How the pieces fit

```
Gateway identity (per machine)      License file
  <dataRoot>/identity  ── UUID ──┐     gatewayId ──┐
                                 ▼                 ▼
        LicenseManager.EnforceGatewayBinding(license)
              gatewayId == identity ?  → load / Valid
              else                     → reject (LICENSE_GATEWAY_MISMATCH)
```

### 3.2 Implementation steps (already done)

1. **Gateway identity.** `FileSystemGatewayIdentity` generates a UUID on first start
   and persists it at `<dataRoot>/identity` (`HostOptions.GatewayIdentityPath`).
   Exposed as `IGatewayIdentity.GatewayId`.
2. **Binding check in the license manager.** `LicenseManager` takes an
   `expectedGatewayIdProvider`; after signature + payload validation it runs
   `EnforceGatewayBinding(info)`:
   - if the provider returns null (identity not established yet) → skip;
   - if `gatewayId == "*"` → floating, allow;
   - else require `license.gatewayId == expected` (case-insensitive, trimmed),
     otherwise throw `LicenseException(CORE.LICENSE_GATEWAY_MISMATCH)`.
   Because it throws **before** the snapshot swap, a wrong-gateway license never
   takes effect and every downstream gate treats the machine as unlicensed.
3. **Wire the identity into the license providers.** Both the eager DI-registration
   load (`EdgeConnectComposition`) and the `CompositionRoot` registration construct
   `LicenseManager` with
   `() => FileSystemGatewayIdentity.TryReadPersisted(GatewayIdentityPath)`.
   `TryReadPersisted` reads the id **without** creating it (null on first boot).
4. **UI + activation guard.** The Studio License page shows *"This gateway's ID"*;
   the Activate flow pre-checks binding and rejects a wrong-gateway license with a
   clear message before saving it.
5. **Issuance.** The License Generator app writes `gatewayId` = the value the admin
   enters (a specific id, or `*`).
6. **Tests.** `LicenseManagerBindingTests` covers match, mismatch → mismatch code,
   floating `*`, no-identity skip, and case-insensitive/trimmed match.

### 3.3 Operator workflow to issue + use a single-machine license

1. Install EdgeConnect on the **target** machine → first start generates its identity.
2. Open **Studio → License** → copy **"This gateway's ID"**.
3. In the **License Generator**: enter that **exact** id (not `*`), pick edition /
   protocols / expiry → **Submit** → `license.json`.
4. On the target gateway: **License → Activate License** → upload → **Licensed ✓**.
5. The same file on any other machine → **rejected** (mismatch).

---

## 4. DPAPI seal — making the identity copy-proof

### 4.1 Why

Today the identity is a **plaintext UUID in a file**. Copying that `identity` file
(plus the license) to another machine would move the binding. The DPAPI seal keeps
the **same UUID** but stores it as a **machine-encrypted blob**, so the file is
**useless if copied** to another machine.

DPAPI (Windows Data Protection API) encrypts with a key derived from the machine's
own master key (never stored in your files). `LocalMachine` scope is used because the
gateway runs as a service (LocalSystem), not a user profile. An app-specific
**entropy** (salt) is added so no other app on the same machine can unseal it.

### 4.2 Creation — step by step

**Step 1 — add the package (Host project).**
```xml
<PackageReference Include="System.Security.Cryptography.ProtectedData" Version="8.0.0" />
```

**Step 2 — define an embedded entropy (app salt).**
```csharp
// 32 fixed random bytes compiled into the binary; NOT secret-critical, just binds
// the seal to this product so another app can't unseal it.
private static readonly byte[] Entropy = new byte[] { /* 32 bytes */ };
```

**Step 3 — add seal / unseal helpers (Windows-guarded).**
```csharp
[SupportedOSPlatform("windows")]
private static string Seal(string uuid)
{
    var blob = ProtectedData.Protect(
        Encoding.UTF8.GetBytes(uuid), Entropy, DataProtectionScope.LocalMachine);
    return Convert.ToBase64String(blob);
}

[SupportedOSPlatform("windows")]
private static string Unseal(string sealedBase64)
{
    var blob = Convert.FromBase64String(sealedBase64);
    var bytes = ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.LocalMachine);
    return Encoding.UTF8.GetString(bytes); // throws CryptographicException on another machine
}
```

**Step 4 — change identity generation/read (`InitializeAsync`).**
```csharp
// FIRST BOOT: generate UUID, seal it, store the blob (not the plaintext).
var id = Guid.NewGuid().ToString("D");
File.WriteAllText(path, Sealing ? Seal(id) : id);

// SUBSEQUENT BOOT: read file, recover the UUID.
var content = File.ReadAllText(path).Trim();
if (Guid.TryParse(content, out _))
{
    // Legacy PLAINTEXT id (pre-seal). Keep the same UUID so existing licenses
    // still match, and (if sealing enabled) re-seal it in place -> copy-proof.
    _gatewayId = content;
    if (Sealing) File.WriteAllText(path, Seal(content));
}
else
{
    // Sealed blob -> unseal. Fails if copied from another machine.
    try { _gatewayId = Unseal(content); }
    catch (CryptographicException)
    {
        // Not this machine (copied file) OR OS reimaged. Treat as no valid
        // identity: regenerate a fresh sealed identity (old license will no
        // longer match -> re-license required).
        var fresh = Guid.NewGuid().ToString("D");
        File.WriteAllText(path, Seal(fresh));
        _gatewayId = fresh;
    }
}
```

**Step 5 — mode flag + platform fallback.**
- `Sealing` = `OperatingSystem.IsWindows()` AND `EDGECONNECT_IDENTITY_SEALED != "false"`.
- On non-Windows (Linux), skip DPAPI (fall back to plaintext UUID or the hardware
  approach) — guard all `ProtectedData` calls with `OperatingSystem.IsWindows()`.

**Step 6 — redaction & logging.**
- Never log the sealed blob or the UUID at info level. The License page continues to
  show the recovered UUID (that's the gateway id operators send to Elpis) — that's
  fine; the *file* is what's now unreadable off-machine.

**Step 7 — tests.**
- Seal→Unseal round-trips to the same UUID on this machine.
- A blob "from another machine" (simulate by corrupting/altering) → `Unseal` throws →
  identity regenerated.
- Legacy plaintext id is preserved (same UUID) and re-sealed.

### 4.3 File contents — before vs after

| | Content of `<dataRoot>/identity` |
|---|---|
| Before (plaintext) | `44d3a7fa-5ac5-4820-a70f-d74be266f9fc` (copyable) |
| After (DPAPI sealed) | `AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA…` (machine-locked blob) |

### 4.4 Behaviour when the file is copied to another machine

`Unseal` → `CryptographicException` → no valid identity recovered → the runtime
regenerates a new (different) sealed identity → the copied license's `gatewayId` no
longer matches → **license rejected**. Net effect: the license does **not** work on
the other machine, even with both files copied.

### 4.5 Caveats

- **OS reimage / reinstall** changes the DPAPI master key → the sealed identity can't
  be recovered → a new identity is generated → operator must request a re-issued
  license (documented re-registration flow).
- **Windows-only:** DPAPI needs the `System.Security.Cryptography.ProtectedData`
  package and `OperatingSystem.IsWindows()` guards; Linux uses a different path.
- **VMs:** works per-VM; provision the identity *after* cloning a template, or use a
  floating (`*`) license for clones.
- **Strength:** DPAPI seals to the OS install; a SYSTEM-level attacker could in theory
  extract the master key. For a hardware root of trust use the **TPM** variant.

---

## 5. Summary

- **Single-machine licensing is implemented** (ADR-0036): issue the license with the
  gateway's real id (not `*`) and it activates only on that machine.
- Today the machine identity is a **plaintext file UUID** — strong against casual
  copying but movable by copying the file.
- The **DPAPI seal** (§4) keeps the same UUID but stores it machine-encrypted, making
  the identity file **copy-proof**, with a legacy-preserving migration and a
  re-registration path for reimaged machines.
- For maximum assurance, escalate to a **TPM-backed** seal.

*Companions: `docs/decisions/0036-single-machine-license-binding.md`,
`docs/licensing/license-validation-and-tamper-plan.md` (hardening roadmap),
`EdgeConnect-HardwareBind-GatewayIdentity-Plan.md`.*
