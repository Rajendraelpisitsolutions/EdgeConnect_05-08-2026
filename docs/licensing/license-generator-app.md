# Elpis EdgeConnect — License Generator App

**Audience:** Elpis admin / licensing operator (internal tool — never shipped to customers).
**Purpose:** produce a **signed `license.json`** that operators load on a gateway to
activate EdgeConnect.
**Type:** Windows desktop app (WinForms, .NET 8, self-contained).

---

## 1. Where it lives

| Item | Location |
|------|----------|
| Runnable app (self-contained) | `D:\Work Backup\LicenseGeneratorApp\ElpisLicenseGenerator.exe` |
| README | `D:\Work Backup\LicenseGeneratorApp\README.txt` |
| Source project | `tools/LicenseGeneratorApp/` (in the repo; **not** in `ElpisEdgeConnect.sln` — Windows-only) |

The published app is self-contained (bundles the .NET runtime) — it runs on any
Windows x64 machine with no .NET install.

---

## 2. What it produces

A single signed `license.json` — the same file format the runtime validates
(`docs/licensing/license-file-format.md`). Example:

```json
{
  "customer": "Sony",
  "edition": "Professional",
  "gatewayId": "44d3a7fa-5ac5-4820-a70f-d74be266f9fc",
  "issuedAt": "2026-07-08",
  "expiresAt": "2027-07-08",
  "licenseId": "LIC-20260708001500",
  "limits": { "maxSourceInstances": 50, "maxSinkInstances": 5, "maxRoutes": 100 },
  "modules": {
    "core-runtime":        { "enabled": true },
    "connectivity-studio": { "enabled": true },
    "source-focas2":       { "enabled": true },
    "sink-mqtt":           { "enabled": true }
  },
  "signature": "base64-rsa-pss-signature"
}
```

---

## 3. The form

| Field | Control | Notes |
|-------|---------|-------|
| **Client name** | text | Customer / organisation. Required. |
| **License type** | dropdown | `Starter` / `Professional` / `Enterprise` (edition label). **Drives which Sources/Destinations are offered** — see §4.1. |
| **Gateway ID** | text | The gateway's identity id (shown on the Studio **License** page as *"This gateway's ID"*). Required. Enter `*` for a license valid on any machine. |
| **Expiry date** | date picker | License expiry (interpreted as end-of-day UTC). |
| **License ID** | text | Auto-filled `LIC-<utc timestamp>`; editable. |
| **Sources** | multi-select dropdown | Source protocols **offered for the selected edition** (§4.1). Tick any number. |
| **Destinations** | multi-select dropdown | Destination protocols **offered for the selected edition** (§4.1). |
| **Limits (max)** | numeric | Global caps: sources / sinks / routes. Defaults 50 / 5 / 100. |
| **Submit** | button | Signs and writes `license.json` (Save dialog, defaults to `D:\Work Backup`). |

`core-runtime` and `connectivity-studio` (the Studio UI) are **always included**
automatically so every generated license is functional.

---

## 4. Available protocols (and the exact license keys)

The dropdowns mirror the Studio's source/destination pickers. Each name maps to the
**license module key the runtime's DI gate actually checks** — i.e. each adapter's
`*Configuration.LicenseModuleKey` constant.

### Sources
| Display name | Module key |
|---|---|
| Modbus TCP | `source-modbus-tcp` |
| Modbus RTU | `source-modbus-tcp` *(shares the Modbus module — ADR-0033)* |
| FANUC FOCAS2 | `source-focas2` |
| Brother HTTP | `source-brother-http` |
| OPC UA Client | `source-opcua-client` ⚠️ *(note: not `source-opc-ua-client`)* |
| Siemens S7 | `source-s7` |
| MTConnect | `source-mtconnect` |
| EtherNet/IP | `source-ethernet-ip` |
| Mitsubishi MELSEC | `source-melsec` |

### Destinations
| Display name | Module key |
|---|---|
| MQTT | `sink-mqtt` |
| OPC UA Server | `sink-opc-ua-server` |

> **Key-naming caveat.** The runtime's DI gate keys the OPC UA Client on
> `source-opcua-client` (the adapter constant), while the `LicenseModuleKeys`
> catalog constant is `source-opc-ua-client`. The generator deliberately emits the
> **adapter** value so the license actually enables the protocol. This divergence is
> a known latent bug in the platform worth reconciling (see the validation plan doc).

### 4.1 Edition gates which protocols the dropdowns show (new 2026-07)

Selecting the **License type** now filters the Sources/Destinations dropdowns to the
protocols that edition is allowed to offer, mirroring the Studio wizards. This is
driven by `LicenseEditionCatalog` in Core (`MainForm.PopulateProtocolsForEdition()`),
so the generator and the runtime can't drift apart.

| Edition | Sources shown | Destinations shown |
|---------|---------------|--------------------|
| **Starter** | **Modbus TCP only** | **MQTT only** |
| **Professional** | all sources | **MQTT only** |
| **Enterprise** | all sources | **MQTT + OPC UA Server** |

Behaviour:
- Switching edition **repopulates** both dropdowns immediately.
- Where an edition offers exactly **one** destination (MQTT under Starter /
  Professional), it is **auto-ticked**; likewise Modbus TCP under Starter. When several
  are offered (Enterprise destinations), prior ticks are preserved and the operator
  chooses.
- Starter shows Modbus **TCP** only — not Modbus **RTU** (both share
  `source-modbus-tcp`, but only TCP is surfaced).
- Because the dropdowns only expose offered protocols, a restricted-edition license is
  written with **only** those modules — so the runtime DI gate and config-save gate
  agree with the UI. See the complete guide §9.6.

---

## 5. How it signs (why the output is trustworthy)

The app does **not** reimplement crypto. It references
`ElpisEdgeConnect.Core` and uses the exact same primitives the runtime verifier
uses, so the output is byte-for-byte compatible:

1. Build the payload (`SortedDictionary`, ordinal-sorted).
2. `CanonicalJson.Canonicalize(unsignedJson)` — Core's canonicalizer (sorted keys at
   every depth, no insignificant whitespace, `signature` excluded, UTF-8 no BOM).
3. `LicenseSignatureValidator.SignWithPrivateKey(canonical, privatePem)` — **RSA-PSS
   / SHA-256** signature, base64-encoded.
4. Re-serialize the payload indented and append `"signature"`.

This is identical to the `tools/LicenseGen` CLI signing path, which is verified to
produce licenses that activate to `Valid`.

### Signing key
- The app currently embeds the **development private key** (`DevSigningKey.cs`) that
  matches the public key compiled into the current EdgeConnect build — so generated
  licenses activate out of the box for testing.
- ⚠️ **Production:** rebuild the tool with your **real private key** (the counterpart
  of the public key in `EmbeddedPublicKey.cs`) and keep that key secret — never ship
  a private key inside a customer-facing binary. See
  `docs/licensing/license-file-format.md` §10 (key custody / rotation).

---

## 6. Operator workflow (end-to-end)

1. Install EdgeConnect on the gateway → first start generates its identity UUID.
2. Operator opens the Studio **License** page → copies *"This gateway's ID"*.
3. Admin opens the License Generator → enters client, edition, that gateway ID,
   expiry → ticks the licensed **Sources** and **Destinations** → **Submit** →
   saves `license.json`.
4. Admin sends `license.json` to the operator.
5. Operator: Studio **License → Activate License** → picks the file → status flips
   to **Licensed ✓** (a service restart starts any newly-licensed protocols).

---

## 7. Build & publish (for maintainers)

```bash
# from the repo root
dotnet build   tools/LicenseGeneratorApp/LicenseGeneratorApp.csproj -c Release
dotnet publish tools/LicenseGeneratorApp/LicenseGeneratorApp.csproj \
    -c Release -r win-x64 --self-contained true -o "D:\Work Backup\LicenseGeneratorApp"
```

Project files: `LicenseGeneratorApp.csproj` (references Core), `Program.cs`,
`MainForm.cs` (form + signing), `CheckedDropDown.cs` (multi-select dropdown),
`DevSigningKey.cs` (the signing key — replace for production).

---

## 8. Notes & limitations

- The tool trusts the operator to enter the correct **gateway ID**; a typo yields a
  license that will be rejected on that gateway (clear mismatch message).
- It does not talk to the gateway or any server — it is a pure offline generator.
- Editing a generated `license.json` by hand **invalidates it** (the signature no
  longer matches) — see the *License Validation & Tamper-Prevention Plan*.
