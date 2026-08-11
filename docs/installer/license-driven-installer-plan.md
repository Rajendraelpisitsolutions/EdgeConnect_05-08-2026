# License-Driven Installer — Design & Implementation Plan

**Status:** PROPOSAL. Nothing here is implemented. No ADR has been written yet.
**Date:** 2026-08-06
**Scope:** Licensing Layer 1 ("Packaging") per `ARCHITECTURE_BLUEPRINT.md` §7.1 and
Appendix A (`Module delivery | Compile-time assemblies, per-edition installers | **LOCKED**`).
**Non-scope:** Layers 2 and 3 (runtime activation, UI/API enforcement) already exist and
this plan must not weaken either.

---

## 1. Recommendation

**Build N per-edition MSIs from N per-edition publishes (Mechanism A1). Do not read the
license file at install time (Mechanisms A2/A4). Do not use the manual feature tree for
edition gating (Mechanism A3).**

The decisive reason is not preference, it is sequencing: **ADR-0036** and **ADR-0038**
node-lock a license to a gateway id that *does not exist until the product has been
installed and started once*. The documented issuance flow is literally:

> 1. Install → first start generates the gateway UUID at `<dataRoot>/identity`.
> 2. Operator reads the id from the Studio License page…
> 3. Elpis issues a license with `gatewayId` = that id…
> — `docs/decisions/0036-single-machine-license-binding.md`, "Issuance flow"

On a fresh machine there is **no license file to point the installer at**. Any design that
asks the customer to supply `edgelicense.json` during setup is inverted with respect to the
product's own activation model, and would only ever work on re-installs or on floating
(`gatewayId == "*"`) licenses.

Three further findings shape the recommendation:

1. **The runtime almost certainly cannot survive a missing adapter DLL today.** This is the
   biggest technical risk and it blocks every mechanism equally. See §5.
2. **The strongest driver for this work is redistribution compliance, not download size.**
   The current MSI ships **~23.5 MB of Fanuc-licensed FOCAS2 native DLLs** to every
   customer, and `docs/licensing/module-catalog.md` states we do *not* redistribute them.
   See §6.3. This is a live contradiction, not a hypothetical.
3. **This is not a new idea — it is an unshipped locked deliverable.** Blueprint Phase 3
   lists "Edition-based installer builds (Starter/Professional/Enterprise)"; CLAUDE.md §8
   Phase 3 lists "**Not started:** … edition installers".

Consequently the plan below does **not** open with "slice five editions". It opens with a
half-day spike that decides whether the rest is even buildable, then a runtime
graceful-absence refactor, then a **two-edition** first shippable slice split on the one
axis that has a real legal driver.

### The owner's framing, validated

> "I want to create an installer application or option based on license type — not in the
> EdgeConnect UI."

The research **supports** the stated interpretation. Blueprint §7.1 says it in as many words:

> "Build and ship edition-specific installers. Each edition includes only the protocol
> module assemblies for that tier. Customers on Starter edition literally don't receive the
> Modbus or OPC UA assemblies."

One qualification: the phrase *"installer application"* most literally describes Mechanism
A4 (a licensing-aware launcher EXE). That specific reading is the one the evidence
contradicts, for the ADR-0036 reason above. Everything else in the ask holds.

---

## 2. Evidence base

Everything below was read in this repository at commit-time of writing.

| Source | What it establishes |
|---|---|
| `docs/ARCHITECTURE_BLUEPRINT.md` §7.1 | Packaging layer intent, verbatim. Edition table (**stale** — see §3.3). |
| `docs/ARCHITECTURE_BLUEPRINT.md` Appendix A | `Module delivery — compile-time assemblies, per-edition installers — LOCKED`; `Licensing enforcement — 3 layers — LOCKED`; `License mode — fully offline, no phone-home — LOCKED`. |
| `docs/ARCHITECTURE_BLUEPRINT.md` §Phase 3 | "Edition-based installer builds (Starter/Professional/Enterprise)" — a listed, never-delivered deliverable. |
| `src/ElpisEdgeConnect.Core/Licensing/LicenseEdition.cs` | The **actual** five editions. |
| `src/ElpisEdgeConnect.Core/Licensing/LicenseModuleKeys.cs` | The **actual** module key constants. |
| `src/ElpisEdgeConnect.Core/Licensing/LicenseEditionCatalog.cs` | The **actual** edition→module offering matrix. |
| `src/ElpisEdgeConnect.Host/EdgeConnectComposition.cs` | Composition root; unconditional adapter wiring. **Primary risk site.** |
| `src/ElpisEdgeConnect.Management/Hosting/ManagementHostingExtensions.cs` | Studio startup wiring; unconditional per-protocol probe services + endpoint mapping. **Secondary risk site.** |
| `installer/ElpisEdgeConnect.wxs` | Single MSI, `WixUI_FeatureTree`, two harvest ComponentGroups, fixed `UpgradeCode`. |
| `installer/ElpisEdgeConnect.Bundle.wxs` | Burn bundle, its own `UpgradeCode`, single `<MsiPackage>`. |
| `docs/installer/creating-the-installer.md` | Current build recipe: `-d Version=`, two `-ext`, three bindpaths. |
| `docs/decisions/0035-unlicensed-runtime-cutoff.md` | 2h hard stop when status ≠ `Valid`. |
| `docs/decisions/0036-single-machine-license-binding.md` | Node-locking + issuance flow. **Decisive.** |
| `docs/decisions/0038-machine-anchored-gateway-identity.md` | Identity survives data-root deletion (HKLM + machine-wide file). |
| `docs/licensing/module-catalog.md` | Module keys, DI enforcement semantics, **third-party redistribution posture**. |
| `docs/licensing/license-generator-app.md` | Edition dropdown drives offered modules via `LicenseEditionCatalog`. |
| `publish/Management/` (real build output on disk) | Actual DLL names and byte sizes quoted throughout. |

---

## 3. What actually exists today

### 3.1 Editions (code truth)

`src/ElpisEdgeConnect.Core/Licensing/LicenseEdition.cs`:

```csharp
public enum LicenseEdition
{
    Unknown = 0, Starter = 1, Professional = 2,
    Enterprise = 3, CncPro = 4, TrialPeriod = 5,
}
```

### 3.2 Edition → offered modules (code truth)

From the header of `src/ElpisEdgeConnect.Core/Licensing/LicenseEditionCatalog.cs`, verbatim:

```
  Edition       Sources                                 Destinations
  ----------    -----------------------------------     ------------------
  Unknown(dev)  all                                     all
  Starter       Modbus TCP only                         MQTT only
  Professional  all EXCEPT OPC UA Client                MQTT only
  CncPro        FOCAS2 + Brother HTTP + MTConnect only   MQTT only
  Enterprise    all                                     MQTT + OPC UA Server
  TrialPeriod   all                                     all
```

Module keys (`LicenseModuleKeys.cs`), quoted:

`core-runtime`, `sink-mqtt`, `sink-opc-ua-server`, `sink-http` *(reserved)*,
`sink-tcp` *(reserved)*, `source-modbus-tcp`, `source-focas2`, `source-mtconnect`,
`source-brother-http`, `source-s7`, `source-opc-ua-client`, `source-ethernet-ip`,
`source-melsec`, `connectivity-studio`, `historian-bridge` *(reserved)*.

> **Trap.** `LicenseModuleKeys.SourceOpcUaClient` is `"source-opc-ua-client"`, but the
> adapter, the issued licenses, the Studio picker and the generator all use
> **`"source-opcua-client"`**. `LicenseEditionCatalog` hard-codes the literal and warns:
> *"do NOT swap in the mismatched constant."* Any module→assembly map built for packaging
> must use the **real** key, not the constant.

### 3.3 Documented editions ≠ coded editions (drift to resolve before building)

| | Blueprint §7.1 table | Code (`LicenseEditionCatalog`) |
|---|---|---|
| Starter | 2 of {Focas2, MtLinki, MTConnect, BrotherHttp} | **Modbus TCP only** |
| Professional | All base + Modbus; MQTT/HTTP/TCP sinks | All sources **except** OPC UA Client; MQTT only |
| Enterprise | All + S7, OPC UA Client; all sinks + OPC UA Server | All sources; MQTT + OPC UA Server |
| CncPro | *absent* | FOCAS2 + Brother HTTP + MTConnect |
| TrialPeriod | *absent* | everything |
| MtLinki | referenced | **no adapter project exists** (CLAUDE.md §8) |

Blueprint §7.2's module identifiers (`source.focas2`, dotted) and
`docs/licensing/license-file-format.md` §4.3's stated rule (`source.{protocolName}`) also
disagree with the shipped hyphenated keys (`source-focas2`). **The code is the truth;
those two documents are stale.** Recommendation: `LicenseEditionCatalog` becomes the
declared single source of truth (ADR candidate, §11), and blueprint §7.1/§7.2 plus
license-file-format §4.3 get corrected as part of the first slice.

### 3.4 The installer today

`installer/ElpisEdgeConnect.wxs` — one MSI, `Version="$(Version)"` (currently `2.1.0.1`
from `Directory.Build.props`), `UpgradeCode="b7e2c1a4-9f3d-4c8e-8a21-5f6d7e8c9b01"`,
`Scope="perMachine"`. Two harvesting component groups:

- `ManagementComponents` → `<Files Include="!(bindpath.MgmtSrc)\**">` minus `.pdb`, `.xml`,
  `web.config`; plus `<Files Include="!(bindpath.FocasSrc)\*.dll" />`.
- `HostComponents` → same harvest for `HostSrc`, plus **six explicitly-named** FOCAS
  native `<File>` elements (the comment explains `<Files>` de-dupes by source path and
  would silently skip them).

Two features: `MainApplication` (`AllowAbsent="no"`) and `DesktopShortcutFeature`. The
`WixUI_FeatureTree` wizard exists **only** to toggle the desktop shortcut.

Build requires `-d Version=`, `-ext WixToolset.UI.wixext`, `-ext WixToolset.Util.wixext`,
and three bindpaths (`HostSrc`, `MgmtSrc`, `FocasSrc`).

There is **no CI** in this repository (no `.github/`, no build scripts at root). Releases
are produced by hand from `docs/installer/creating-the-installer.md`.

### 3.5 UI gating today (reference only — layer 3, already built)

`Wizards/SourceProtocolPickerModel.Resolve(isModuleEnabled, edition)` does two things:
edition-level tiles are **hidden entirely** via `LicenseEditionCatalog.IsSourceModuleOffered`,
and surviving tiles whose module is disabled are downgraded to `RequiresLicense` and lose
their `TargetHref`. `DestinationProtocolPickerModel` mirrors it. Both carry the same
comment: *"The UI is explanatory only; the backend gate remains authoritative on save."*
That is the correct posture and packaging must adopt the same humility.

---

## 4. Mechanism comparison (Question A)

| | A1 — N MSIs per edition | A2 — one MSI, license read at install | A3 — one MSI, manual feature tree | A4 — launcher EXE reads license, drives MSI |
|---|---|---|---|---|
| **Build cost** | Medium–High. Needs per-edition publish + graceful-absence refactor (§5). | High. Needs §5 refactor **plus** a signed-JSON-parsing custom action **plus** per-file feature partitioning of a 398-file flat publish. | Low. Feature partitioning only, no license logic. | Highest. Everything in A2 plus a new Burn BA / custom bootstrapper app. |
| **Release cost per version** | N publish pairs, N MSIs, N EXE bundles. At 5 editions: 10 publishes (~1.3 GB), 10 artifacts. | 1 publish pair, 1 MSI, 1 EXE — unchanged. | Unchanged. | 1 MSI + 1 EXE (+ the launcher). |
| **What it actually prevents** | The unlicensed binaries **are not on the media**. Real. | Nothing durable — `msiexec /i … EDITION=Enterprise` overrides any property a custom action set. | Nothing — the customer *chooses*. | Nothing durable — same property-override bypass, plus the launcher itself is replaceable. |
| **Compliance value (Fanuc / OPC Foundation)** | **High** — the restricted binaries genuinely never leave Elpis. | **Zero** — they are inside the MSI regardless of what is installed. | Zero. | Zero. |
| **Works on a fresh machine** | Yes. | **No** — no gateway id yet, therefore no license yet (ADR-0036). | Yes. | **No** — same reason. |
| **How it fails** | Wrong edition shipped to a customer; a missing DLL crashes startup (§5); N-way test matrix rot. | Custom action can't find/parse/verify the license → must either block the install (bad) or fall through to "install everything" (pointless). | Customer deselects a protocol they bought, or selects one they didn't; support burden. | All of A2's failure modes plus a second UI to maintain and localise. |
| **Offline-safe** | Yes. | Yes. | Yes. | Yes. |
| **Verdict** | **RECOMMENDED** | Reject as the gating mechanism | Reject for edition gating | Reject |

### Why A2/A4 are rejected in detail

1. **Sequencing (fatal).** ADR-0036 §"Issuance flow" — the license is issued *after* first
   start. Fresh installs have nothing to read.
2. **No enforcement value.** MSI properties are settable from the command line. A custom
   action that sets `EDITION` is overridden by `msiexec /i pkg.msi EDITION=Enterprise`
   before the action even runs (public properties supplied on the command line win). The
   only way to make it stick would be to *conditionally fail the install*, which converts a
   licensing nicety into a customer-blocking failure mode.
3. **Signature verification inside the installer buys nothing** (see §7).
4. **The binaries would still ship.** Which means the Fanuc/OPC-Foundation redistribution
   exposure (§6.3) is completely untouched — the single most concrete reason to do this
   work at all.

### Why A3 is rejected for *edition* gating

A feature tree lets the operator pick. That is the opposite of a license constraint; it is
a customisation menu. It is fine for the existing desktop-shortcut toggle and should stay
for that. It is **not** licensing, and presenting it as licensing would be misleading.

### The one A4-shaped idea worth keeping

A *post-install* "Activate" experience is genuinely useful and already partially exists —
the Studio License page shows *"This gateway's ID"* and pre-checks binding on activation
(ADR-0036, "Operator UX"). If the owner's real goal is "cleaner customer experience", that
is the surface to invest in, not the installer. Flagged in §12.

---

## 5. THE BIGGEST TECHNICAL RISK — omitting an adapter DLL (Question D)

**Claim: dropping a protocol assembly from the publish output today will hard-fail startup,
not degrade cleanly. I am confident of the mechanism; the exact failure point must be
confirmed empirically (Slice N.0) because I was instructed not to build or run anything.**

There are **three independent** reasons, any one of which is sufficient.

### 5.1 Host-level: `hostpolicy` validates the deps.json TPA list

`publish/Management/ElpisEdgeConnect.Management.deps.json` lists all thirteen first-party
assemblies (verified by inspecting the real file on disk):

```
ElpisEdgeConnect.Core.dll                    ElpisEdgeConnect.Sources.EthernetIp.dll
ElpisEdgeConnect.Host.dll                    ElpisEdgeConnect.Sources.Focas2.dll
ElpisEdgeConnect.Management.dll              ElpisEdgeConnect.Sources.MTConnect.dll
ElpisEdgeConnect.Sinks.Mqtt.dll              ElpisEdgeConnect.Sources.Melsec.dll
ElpisEdgeConnect.Sinks.OpcUaServer.dll       ElpisEdgeConnect.Sources.ModbusTcp.dll
ElpisEdgeConnect.Sources.BrotherHttp.dll     ElpisEdgeConnect.Sources.OpcUaClient.dll
                                             ElpisEdgeConnect.Sources.S7.dll
```

For a **self-contained** app, `hostpolicy` builds the trusted-platform-assemblies list from
`deps.json` before a single line of managed code runs. The expected failure for a deleted
file is the well-known host error:

```
Error: An assembly specified in the application dependencies manifest
(ElpisEdgeConnect.Management.deps.json) was not found: ... path: 'ElpisEdgeConnect.Sources.S7.dll'
```

This fails **before `Main`**, so no fail-soft, no diagnostics entry, no Studio, nothing —
the Windows service simply does not start. **INFERRED, not confirmed** — this is the single
most important thing Slice N.0 must verify.

**Implication:** even with a perfect runtime refactor, you cannot simply exclude files in
the `.wxs`. The *publish itself* must not contain the assembly, so its `deps.json` doesn't
either. That means **per-edition builds**, not per-edition file lists.

### 5.2 Composition-root level: unconditional static references

`src/ElpisEdgeConnect.Host/EdgeConnectComposition.cs` — `ConfigureRuntimeAsync` is a single
method that calls all ten registration extensions in one straight-line block:

```csharp
services.AddFocas2SourcesFromGatewayConfig(preloadedConfig, eagerLicense, faultRegistry);
services.AddMTConnectSourcesFromGatewayConfig(...);
services.AddModbusTcpSourcesFromGatewayConfig(...);
services.AddS7SourcesFromGatewayConfig(...);
services.AddBrotherHttpSourcesFromGatewayConfig(...);
services.AddOpcUaClientSourcesFromGatewayConfig(...);
services.AddEthernetIpSourcesFromGatewayConfig(...);
services.AddMelsecSourcesFromGatewayConfig(...);
services.AddMqttSinksFromGatewayConfig(...);
services.AddOpcUaServerSinksFromGatewayConfig(...);
```

Those extension methods are defined in **Host** (`src/ElpisEdgeConnect.Host/Adapters/*RegistrationExtensions.cs`),
so this block alone does not force-load the protocol assemblies. But the *bodies* do —
e.g. `MelsecRegistrationExtensions` references `MelsecSourceConfiguration.LicenseModuleKey`
and constructs `SourceRegistration` from Melsec types. Each is JITted on first call, which
resolves `ElpisEdgeConnect.Sources.Melsec.dll`. The call is unconditional whenever
`preloadedConfig is not null`, i.e. on every real gateway.

Worse, and unconditionally — outside the `if (preloadedConfig is not null)` block:

```csharp
using ElpisEdgeConnect.Sources.Focas2;   // file-top
...
services.AddSingleton<Focas2FakeModeMeter>();   // Sources.Focas2
...
services.AddSingleton<S7FakeModeMeter>();       // Sources.S7
if (Focas2DemoModeOptions.IsEnabled) { ... }
if (S7DemoModeOptions.IsEnabled)     { ... }
```

`typeof(Focas2FakeModeMeter)` and `typeof(S7FakeModeMeter)` appear directly in
`ConfigureRuntimeAsync`'s own IL. **JITting `ConfigureRuntimeAsync` therefore requires
`Sources.Focas2.dll` and `Sources.S7.dll` to be loadable — even on a Starter gateway with
zero FOCAS2 and zero S7 sources configured.**

### 5.3 Studio level: unconditional per-protocol services and endpoints

`src/ElpisEdgeConnect.Management/Hosting/ManagementHostingExtensions.cs` registers a probe
service and maps an API for **every** protocol, unconditionally:

```csharp
builder.Services.AddSingleton<Api.Focas2BrowseService>();
builder.Services.AddSingleton<Api.MqttTestConnectionService>();
builder.Services.AddSingleton<Api.BrotherHttpProbeService>();
builder.Services.AddSingleton<Api.ModbusProbeService>();
builder.Services.AddSingleton<Api.OpcUaClientTestConnectionService>();
builder.Services.AddSingleton<Api.OpcUaClientBrowseApiService>();
builder.Services.AddSingleton<Api.MTConnectBrowseService>();
builder.Services.AddSingleton<Api.S7ProbeService>();
builder.Services.AddSingleton<Api.EthernetIpProbeService>();
builder.Services.AddSingleton<Api.MelsecProbeService>();
...
app.MapFocas2BrowseApi(); app.MapModbusProbeApi(); app.MapS7ProbeApi();
app.MapEthernetIpProbeApi(); app.MapMelsecProbeApi(); app.MapMelsecDiagnosticsApi();
app.MapS7AddressValidationApi(); app.MapS7TagTemplateApi();  // and more
```

and lines 98–99 read `Focas2DemoModeOptions.IsEnabled` / `S7DemoModeOptions.IsEnabled`
directly. **28 Management files** carry a `using ElpisEdgeConnect.Sources.*` /
`Sinks.*` — wizard models, probe services, Razor pages (`AddS7Source.razor`,
`AddMelsecSource.razor`, `AddOpcUaServerDestination.razor`, `MelsecDiagnosticsPanel.razor`, …).
The `Map*Api` calls execute at startup, so their JIT happens during boot.

`ElpisEdgeConnect.Management.csproj` also carries a **direct** `PackageReference` to
`FluentModbus 5.2.0` for `ModbusProbeService` — so `FluentModbus.dll` can never be dropped
while the Studio ships, no matter what the license says about `source-modbus-tcp`.

### 5.4 What this means for the design

Two viable routes, and they trade differently:

| | **Route R1 — graceful absence** (runtime tolerates a missing assembly) | **Route R2 — per-edition compile** (`ProjectReference` conditioned on `$(EdgeConnectEdition)`) |
|---|---|---|
| Composition root | Wrap each adapter's wiring in a `[MethodImpl(MethodImplOptions.NoInlining)]` shim + `try/catch (FileNotFoundException)`; move the FakeModeMeter registrations behind the same shims. | `#if EDITION_HAS_S7` around the call; assembly genuinely not referenced. |
| deps.json problem (§5.1) | **Not solved.** Still fails in `hostpolicy` before managed code. Needs the assembly excluded from publish *and* deps.json — which means a conditioned build anyway. | Solved by construction. |
| Management (28 files, Razor) | Needs the same treatment across probe services + endpoint maps; Razor pages must be conditionally excluded from compilation. | Same problem, expressed as `#if` / conditional `<Compile Remove>`. Razor conditional compilation is awkward. |
| Tension with Locked #4 / anti-pattern #2 | Mild — this is *tolerating absence*, not dynamic loading. But it deserves an explicit ADR ruling. | None. |
| Test matrix | 1 build, N runtime configs. | N builds, N test runs. |

**Recommendation: R2, because §5.1 forces it.** Route R1 cannot fix the `hostpolicy`
failure without also conditioning the build, so R1's extra runtime machinery would be paid
for and still not be sufficient. R2 with a **coarse edition axis** (see §8) keeps the `#if`
count tractable.

**If Slice N.0 disproves §5.1** (i.e. `hostpolicy` tolerates a missing TPA entry and the
failure is a lazy `FileNotFoundException` at JIT time), then R1 becomes viable and much
cheaper, and the plan should pivot. That is precisely why N.0 comes first.

---

## 6. What actually differs per edition (Question D, concrete)

Measured from the real `publish/Management/` on disk (byte sizes, 2026-08-06 build).

### 6.1 Module key → files

| Module key | First-party assembly | Bytes | Third-party payload it pulls in | Bytes |
|---|---|---|---|---|
| `core-runtime` | `ElpisEdgeConnect.Core.dll` | 595,968 | *(always)* | — |
| *(host)* | `ElpisEdgeConnect.Host.dll` | 204,288 | *(always)* | — |
| `connectivity-studio` | `ElpisEdgeConnect.Management.dll` | 2,292,736 | `MudBlazor.dll` 9,145,856; `Swashbuckle.*` ~2,406,912 | ~11.5 MB |
| `source-modbus-tcp` | `…Sources.ModbusTcp.dll` | 175,616 | `FluentModbus.dll` 151,040 — **also a direct Management dependency, cannot be dropped** | 151,040 |
| `source-focas2` | `…Sources.Focas2.dll` | 99,328 | **6 native FOCAS DLLs, 11,760,600 B, shipped into BOTH dirs → ~23.5 MB** | 23,521,200 |
| `source-mtconnect` | `…Sources.MTConnect.dll` | 63,488 | none (HttpClient) | 0 |
| `source-brother-http` | `…Sources.BrotherHttp.dll` | 81,920 | none | 0 |
| `source-s7` | `…Sources.S7.dll` | 108,544 | `Sharp7.dll` 44,544 | 44,544 |
| `source-opcua-client` ⚠ | `…Sources.OpcUaClient.dll` | 147,968 | `Opc.Ua.Client.dll` 421,448 + **shared** `Opc.Ua.Core.dll` 7,770,184, `Opc.Ua.Configuration.dll` 76,360, `Opc.Ua.Security.Certificates.dll` 66,120 | 8,334,112 |
| `source-ethernet-ip` | `…Sources.EthernetIp.dll` | 63,488 | `libplctag.NativeImport.dll` 3,230,208 + `libplctag.dll` 78,848 | 3,309,056 |
| `source-melsec` | `…Sources.Melsec.dll` | 132,608 | none — hand-rolled SLMP (ADR-0033) | 0 |
| `sink-mqtt` | `…Sinks.Mqtt.dll` | 47,104 | `MQTTnet.dll` 348,728 | 348,728 |
| `sink-opc-ua-server` | `…Sinks.OpcUaServer.dll` | 61,440 | `Opc.Ua.Server.dll` 429,640 + **shared** `Opc.Ua.Core/Configuration/Security.Certificates` | 429,640 (+shared) |
| `sink-http`, `sink-tcp`, `historian-bridge` | *reserved, no assembly* | — | — | — |

⚠ Real key is `source-opcua-client`, not the `LicenseModuleKeys.SourceOpcUaClient` constant.

**Not in the picture:** `src/ElpisEdgeConnect.Sinks.SparkplugB/` exists in the solution but
is **not** referenced by `ElpisEdgeConnect.Host.csproj`, is **not** in the published output,
has **no** `LicenseModuleKeys` entry, and has **no** tile in `DestinationProtocolPickerModel`.
It must be given a module key and an edition placement before, or explicitly deferred past,
this work. Flagged in §12.

### 6.2 Exclusion is a graph difference, not a list

`Opc.Ua.Core.dll` (7.77 MB) is shared by `source-opcua-client` **and**
`sink-opc-ua-server`. It only drops when **both** are excluded. A naïve "one module → one
file list" mapping will either retain 7.77 MB unnecessarily or delete a file another
retained module needs. The exclusion set must be computed as
`publish(FullEdition) − publish(TargetEdition)` from two real builds, not authored by hand.

### 6.3 The compliance finding (surface this to the owner)

`docs/licensing/module-catalog.md`, "Third-party library licensing":

> **Fanuc FOCAS2 DLLs** — Per-customer Fanuc commercial license. *"Customer is responsible
> for their own Fanuc DLL license. **EdgeConnect does NOT redistribute the FOCAS2 DLLs** —
> they are loaded from the customer's machine."*

> **OPCFoundation/UA-.NETStandard** — dual GPL-2.0 / OPC Foundation RCL. *"We SHIP under RCL
> once Corporate membership clears. **Customer distribution is gated on the membership.**
> Until then, the `sink-opc-ua-server` module is buildable + testable in-house but **not
> redistributable**."*

But `installer/ElpisEdgeConnect.wxs` ships **all six FOCAS natives twice**:

```xml
<!-- ManagementComponents -->
<Files Include="!(bindpath.FocasSrc)\*.dll" />
<!-- HostComponents -->
<File Id="HostFocas_Fwlib64_dll"    Source="!(bindpath.FocasSrc)\Fwlib64.dll" />
<File Id="HostFocas_fwlib0DN64_dll" Source="!(bindpath.FocasSrc)\fwlib0DN64.dll" />
... four more ...
```

and `docs/installer/creating-the-installer.md` makes it mandatory:

> "`FocasSrc` is not optional: omit it and the build fails on the unresolved
> `!(bindpath.FocasSrc)` reference."

**Every customer today — including a Starter customer who bought only Modbus TCP —
receives ~23.5 MB of Fanuc-licensed native libraries and (pending membership) ~16.7 MB of
OPC Foundation assemblies.** This is a direct contradiction between the licensing
documentation and the shipped artifact. It is the strongest, most concrete argument for
packaging-time gating, and it is independent of any download-size or IP-protection
argument. **It should be raised with the owner regardless of whether this plan proceeds.**

### 6.4 Size, honestly

| | Bytes |
|---|---|
| `publish/Management` total (self-contained, 398 files) | ~134 MB |
| All first-party adapter/sink DLLs combined | ~1.13 MB (**0.8 %**) |
| Removable third-party for a Starter edition (Opc.Ua.* + libplctag + Sharp7, ×2 dirs) | ~23.4 MB |
| Removable FOCAS natives (×2 dirs) | ~23.5 MB |
| **Total removable for Starter, uncompressed across both app dirs** | **~48.5 MB of ~268 MB (~18 %)** |

**If the goal is download size, packaging gating is a weak lever** — the .NET self-contained
runtime dominates, and `MudBlazor.dll` alone (9.1 MB) exceeds every adapter DLL combined.
If the goal is compliance or IP protection, it is the right lever. This distinction changes
the design and is the first open question in §12.

---

## 7. Trust boundary (Question B)

**State this plainly in the ADR and in customer-facing material: packaging is a
distribution-hygiene and compliance control, not a security control.**

What per-edition packaging **does** buy:

- The restricted binaries are **not on the installation media**. For Fanuc and OPC
  Foundation redistribution obligations, that is the whole point and it is fully effective.
- It raises the effort to use an unbought protocol from "edit a config file" to "obtain the
  assembly and its native dependencies from somewhere else, place them correctly, and defeat
  the runtime gate as well".
- It removes an entire class of accidental misconfiguration (an operator configuring a
  protocol the site never licensed).

What it **does not** buy:

- **Enforcement.** The customer is administrator on the machine. They can copy DLLs in,
  replace the MSI's payload, or install the Enterprise MSI if they obtain it. Packaging is
  a speed bump for a determined party.
- **Any reduction in the need for layers 2 and 3.** Locked decision #5 requires all three.
  The runtime gate (`IsModuleEnabled` at DI registration, `LicenseGate`, ADR-0035's 2h
  cutoff, ADR-0036's node-lock) stays **authoritative and unchanged**. This plan proposes
  **no** change to any of it.

### On verifying the RSA signature inside a custom action

If a custom action were to verify a license (Mechanism A2/A4 — which this plan rejects), it
would need to ship, inside the installer:

1. The **RSA public key** — currently `EmbeddedPublicKey.Pem` in
   `src/ElpisEdgeConnect.Core/Licensing/EmbeddedPublicKey.cs`, still flagged
   **"!!! DEV KEY - REPLACE BEFORE PRODUCTION !!!"**.
2. The **canonicalisation logic** — `CanonicalJson`, because
   `docs/licensing/license-file-format.md` §5 is explicit that the signed bytes are *not*
   the literal file content: keys are sorted lexicographically at every depth. A
   reimplementation in a custom action would drift and produce false rejections.
3. RSA-PSS / SHA-256 / MGF1-SHA256 / 32-byte-salt parameters matching §3 of that spec
   exactly.

Is that acceptable? **The public key is safe to ship — it is already inside every shipped
binary.** The problem is not secrecy, it is (a) *duplication* of security-critical
canonicalisation logic into a second implementation that can drift from
`Core.Licensing.CanonicalJson`, and (b) *zero benefit*, since the result is an MSI property
that the command line can override anyway. **Recommendation: do not verify licenses in the
installer at all.** If a future slice ever needs it, invoke the shipped
`ElpisEdgeConnect.Core` rather than reimplementing — never a second copy of the
canonicaliser.

---

## 8. Offline constraint (Question C)

Locked decision #6 / Appendix A: *"License mode — fully offline, no phone-home — **LOCKED**."*
Everything proposed here satisfies it trivially, because the recommended mechanism moves
**all** license reasoning to Elpis's build machine, before the artifact ever leaves. The
customer's machine performs no license-related network activity during install. Explicitly
ruled out and never to be proposed:

- Downloading edition payloads at install time (a "web installer").
- Any install-time entitlement check against an Elpis service.
- Telemetry reporting which edition was installed.

The MSI and the Burn bundle both remain fully self-contained, installable from a USB stick
on an air-gapped plant network, exactly as today.

---

## 9. Proposed edition axis

Five `LicenseEdition` values × 2 apps × 2 artifact types = an unmanageable 20-artifact
release for a team with no CI. Two observations collapse it:

1. **`TrialPeriod` offers everything** (`LicenseEditionCatalog`: `_ => true`). A trial
   installer must therefore be the **full** build. If most customers evaluate before
   buying, the full binary set reaches them anyway — which substantially weakens the
   IP-protection rationale and is a question for the owner (§12).
2. **`Professional` differs from `Enterprise` only by OPC UA Client and OPC UA Server.**
   `Starter` and `CncPro` are proper subsets of `Professional`.

Proposed **three** build variants, not five:

| Variant | Contains | Serves editions | Rationale |
|---|---|---|---|
| **Full** | everything | `Enterprise`, `TrialPeriod`, `Unknown`/dev | The only variant that ships `Opc.Ua.*`. Gated on OPC Foundation membership. |
| **Standard** | everything **except** `Sources.OpcUaClient`, `Sinks.OpcUaServer`, and all `Opc.Ua.*` | `Professional` | Removes the entire OPC Foundation redistribution exposure. Matches `Professional`'s catalog exactly. |
| **Cnc** *(later)* | FOCAS2 + Brother HTTP + MTConnect + MQTT only | `CncPro`, `Starter`* | Smallest surface. \*Starter needs Modbus, not the CNC set — see §12. |

The **Standard** variant is the first shippable slice because it is the one split with a
documented legal driver, and it is a single clean cut through the dependency graph
(§6.2 — dropping both OPC UA modules is exactly what releases `Opc.Ua.Core.dll`).

---

## 10. Milestones (Question G)

Repo convention: milestone-numbered plans under `docs/sessions/<date>-<milestone>-*.md`,
stacked PRs, definition of done per slice. Proposed milestone letter **N** (Packaging) —
confirm with the owner that `N` is free; `M.*` is the Studio milestone and `M.P2.*` is in use.

### N.0 — Spike: does a missing assembly actually break startup? *(½ day, no PR)*

**This slice decides the shape of everything after it. Do not skip it.**

Against the existing `publish/Management` on disk (copy it first — do not mutate the real
publish output):

1. Delete `ElpisEdgeConnect.Sources.Melsec.dll` (zero third-party deps, cleanest probe).
   Run `ElpisEdgeConnect.Management.exe`. Record the **exact** error and whether it occurs
   before or after managed `Main`.
2. Repeat with the deps.json entry **also removed** (hand-edited). Does it now start? Does
   it crash later at the `AddMelsecSourcesFromGatewayConfig` JIT?
3. Repeat with `ElpisEdgeConnect.Sources.S7.dll` — this one is referenced *unconditionally*
   by `EdgeConnectComposition` (`services.AddSingleton<S7FakeModeMeter>()`), so it should
   fail even with an empty config.
4. Repeat for `Host.exe`.

**Definition of done:** a written finding in `docs/sessions/<date>-n0-missing-assembly-spike.md`
stating, with captured output, (a) whether `hostpolicy` fails pre-`Main` on a deps.json
entry with no file, (b) whether a hand-trimmed deps.json permits startup, (c) the first
managed failure point if it does. **Route R1 vs R2 (§5.4) is chosen here, in writing.**

### N.1 — Edition truth + build parameterisation *(1–2 days)*

1. Reconcile the drift in §3.3: correct `ARCHITECTURE_BLUEPRINT.md` §7.1/§7.2 and
   `docs/licensing/license-file-format.md` §4.3 to match `LicenseEditionCatalog` and the
   hyphenated module keys. Declare `LicenseEditionCatalog` the single source of truth.
2. Introduce `$(EdgeConnectEdition)` (default `Full`) in `Directory.Build.props`, plus the
   variant→module mapping expressed **once**, derived from `LicenseEditionCatalog`.
3. `installer/Build-Edition.ps1 -Edition <Full|Standard>` — publishes both apps, builds MSI
   + Setup.exe, stamps edition into the artifact name and into `ARPCOMMENTS`.

**Definition of done:** `./installer/Build-Edition.ps1 -Edition Full` reproduces byte-for-byte
what the manual recipe in `creating-the-installer.md` produces today. **No behaviour change
whatsoever.** Docs updated. Zero warnings, zero errors.

### N.2 — Make Host composable per edition *(3–5 days)*

Per Route R2: condition the `ProjectReference`s in `ElpisEdgeConnect.Host.csproj` on
`$(EdgeConnectEdition)`, and `#if` the corresponding calls in `EdgeConnectComposition.cs`.
Move `Focas2FakeModeMeter` / `S7FakeModeMeter` registration into per-protocol wiring so the
unconditional `typeof` references (§5.2) disappear.

**Definition of done:** `Host.exe` built as `Standard` starts, runs a Modbus→MQTT route
end-to-end, and shows no OPC UA assemblies in its publish folder or deps.json. `Full` is
unchanged. A test asserts that a `Standard` build's deps.json contains **no** `Opc.Ua.*`
entry. All existing Host tests pass on both variants.

### N.3 — Make the Studio composable per edition *(5–8 days — the expensive slice)*

The 28 protocol-coupled Management files (§5.3). Condition probe services, `Map*Api` calls,
wizard models and the Razor wizard pages. `SourceProtocolPickerModel` /
`DestinationProtocolPickerModel` tiles for absent protocols must not merely be hidden by
edition — the tile entry itself must be compiled out, or `Resolve` must tolerate a tile
whose wizard page does not exist.

**Definition of done:** `Standard` Studio boots, `/sources/new` shows no OPC UA Client tile,
`/destinations/new` shows no OPC UA Server tile, `/swagger` lists no OPC UA endpoints, and
the assembly-isolation test in `ElpisEdgeConnect.Management.Tests` still passes. **No
`Opc.Ua.*` file in the publish output.** `Full` Studio is byte-identical in behaviour to
today.

### N.4 — Two-edition release *(2–3 days)* ← **first customer-visible slice**

Ship `ElpisEdgeConnect-Full-2.x.y.z.msi` + `-Setup.exe` and
`ElpisEdgeConnect-Standard-2.x.y.z.msi` + `-Setup.exe`. Add `AllowSameVersionUpgrades="yes"`
to `<MajorUpgrade>` and keep the single shared `UpgradeCode` (§11). Stamp the built edition
into HKLM (`Software\Elpis IT Solutions\EdgeConnect\PackagedEdition`) and surface it on the
Studio License page next to the licensed edition, so a mismatch is diagnosable.

**Definition of done:** clean install of each; **Standard → Full** upgrade at the same
version succeeds, leaves one ARP entry, preserves `C:\ProgramData\EdgeConnect` (config,
identity, license), and the service restarts Running. **Full → Standard** downgrade at the
same version succeeds and any now-unsupported configured source **faults cleanly** and is
quarantined (ADR-0028) rather than killing startup — this is an explicit test, not an
assumption. `docs/installer/creating-the-installer.md` rewritten for two editions.

### N.5 — Cnc variant, and FOCAS-free variants *(sizing TBD)*

Add the `Cnc` variant. Separately and independently: make the `FocasSrc` bindpath
**optional** so non-FOCAS editions do not redistribute Fanuc binaries (§6.3). Note this
currently fails the build by design — changing it is a deliberate decision, not a fix.

**Definition of done:** a `Standard`-without-FOCAS MSI builds and installs; the FOCAS2 tile
is absent; ~23.5 MB smaller; the redistribution contradiction in §6.3 is closed and
`module-catalog.md` re-verified as accurate.

---

## 11. Upgrade / edition change (Question E) — and the `UpgradeCode` trap

Today: `UpgradeCode="b7e2c1a4-9f3d-4c8e-8a21-5f6d7e8c9b01"` with a bare
`<MajorUpgrade DowngradeErrorMessage="…"/>`.

| Option | Consequence |
|---|---|
| **Shared `UpgradeCode` across editions** (recommended) | Installing Standard over Full is a **major upgrade**: the old product is removed, the new installed, one ARP entry. Correct behaviour. |
| **Per-edition `UpgradeCode`** | The two editions do **not** see each other. Both install into `%ProgramFiles%\Elpis EdgeConnect\`, both try to register the service named `ElpisEdgeConnect`. Broken. **Reject.** |

**The trap:** with a shared `UpgradeCode`, `<MajorUpgrade>` by default only removes products
with a version **strictly less** than the new one. A Starter customer buying Professional at
the *same* `2.1.0.1` would get the upgrade silently skipped — two ProductCodes, two ARP
entries, one directory, one service name. **`AllowSameVersionUpgrades="yes"` is mandatory**
for edition changes, and it must land in the same slice (N.4) as the second edition, never
after.

**What survives an edition change:** `docs/installer/creating-the-installer.md` §6.2 —
*"Uninstall preserves data. `C:\ProgramData\EdgeConnect` (including any `license.json`) is
**not** owned by the MSI and is left in place."* Combined with ADR-0038's machine-anchored
identity (HKLM `SOFTWARE\Elpis\EdgeConnect\GatewayId` + a machine-wide file), the **gateway
id survives**, so the customer's existing license keeps binding. That makes the upgrade path
genuinely clean:

> **Starter → Professional:** install the Standard MSI (major upgrade) → drop the new
> `edgelicense.json` → restart the service. No re-identification, no re-configuration.

**Downgrade is the risky direction** and must be tested explicitly in N.4: a Full→Standard
move leaves `current.json` referencing an OPC UA source whose adapter is now absent. That
must produce a clean configuration fault (the existing `IConfigurationFaultRegistry` /
quarantine path, ADR-0028), **not** a startup crash. If it crashes, the fail-soft principle
(ADR-0004) is violated and the slice is not done.

**Repair** (`msiexec /f`) reinstalls the *installed* edition's file set from its own cached
MSI — it cannot change edition and needs no special handling.

---

## 12. Build & release impact (Question F)

Today: one manual publish pair → one MSI → one optional Setup.exe, per
`docs/installer/creating-the-installer.md`. No CI exists.

| | Today | After N.4 (2 editions) | After N.5 (3 editions) |
|---|---|---|---|
| `dotnet publish` invocations | 2 | 4 | 6 |
| `wix build` invocations | 2 | 4 | 6 |
| Publish output on disk | ~268 MB | ~530 MB | ~790 MB |
| Shipped artifacts | 2 | 4 | 6 |
| Manual steps to get them wrong | several | **double** | **triple** |

The manual-error surface is the real cost, not the wall clock. `installer/Build-Edition.ps1`
(slice N.1) is therefore not optional polish — it is the thing that makes N.4 safe. It must:

- take `-Edition` and refuse an unknown value;
- read `Version` from `Directory.Build.props` (never accept it as an argument — the existing
  doc already insists on this so MSI and assembly versions cannot drift);
- clean the publish directory first (the doc's existing warning about stale harvested files
  becomes far more dangerous with multiple editions sharing `publish/`);
- name outputs `ElpisEdgeConnect-<Edition>-<Version>.msi`;
- fail loudly if a variant's publish output contains a file it should not (an assertion, run
  as part of the build, comparing against the expected exclusion set).

`docs/installer/creating-the-installer.md` needs: a new §0.1 explaining variants; the
build recipe replaced by the script; the `-bindpath FocasSrc` note updated once N.5 makes it
conditional; and §5.1 amended for `AllowSameVersionUpgrades`.

**CI:** none exists. A minimal GitHub Actions matrix over `EdgeConnectEdition` would be the
natural home for the "no forbidden file in publish" assertion and the N-way test run. Out of
scope for N.0–N.4; recommend raising it as a separate decision once N.4 proves the shape.

---

## 13. ADR candidates (Question H)

Existing ADRs top out at **0038**. These are the decisions that are architecturally
locked-in and painful to reverse, and should be written **before** N.2 begins.

| # | Proposed title | Why it must be an ADR |
|---|---|---|
| **0039** | *Per-edition packaging is built, not installed* | Locks the mechanism: edition is chosen at **build** time on Elpis's machine; the installer never reads, parses, or verifies a license. Painful to reverse because it shapes the build system, the release process, and every downstream artifact name. Must record the ADR-0036 sequencing argument explicitly, or someone will re-propose install-time license reading in a year. |
| **0040** | *Packaging is a compliance control, not an enforcement control* | Locks the trust boundary. States that layers 2 and 3 remain authoritative and unchanged, that omitted binaries are a speed bump, and that no customer-facing material may claim packaging enforces licensing. Prevents the slow erosion of runtime enforcement "because the installer already handles it". |
| **0041** | *`LicenseEditionCatalog` is the single source of truth for edition→module mapping* | Blueprint §7.1, license-file-format §4.3 and the code currently disagree (§3.3). Without a ruling, packaging will be built against the wrong table. Also settles the `source-opcua-client` vs `source-opc-ua-client` key. |
| **0042** | *Single `UpgradeCode` across editions with `AllowSameVersionUpgrades`* | Once shipped, the `UpgradeCode` cannot change without breaking every installed customer's upgrade path. Getting this wrong is discovered only in the field. |
| **0043** *(conditional — only if N.0 chooses Route R1)* | *Tolerating an absent adapter assembly is not dynamic plugin loading* | Resolves the tension with Locked #4 and anti-pattern #2. Only needed if the spike shows R1 is viable. |

---

## 14. Open questions for the product owner (Question I)

These cannot be decided from the repository, and they lead to **materially different
designs**. Recommend answering Q1 and Q2 before N.1 starts.

**Q1 — What is the actual goal?** The four plausible answers point different directions:

| If the goal is… | …then the right design is |
|---|---|
| **Redistribution compliance** (Fanuc, OPC Foundation) | Exactly this plan. One cut per restricted dependency. §6.3 is already a live problem. **Strongest evidence-backed case.** |
| **IP protection** (competitors can't decompile unbought adapters) | This plan, but the `TrialPeriod` variant undermines it (Q3) and .NET IL is decompilable anyway — obfuscation would be the real answer, not packaging. |
| **Download size** | **Weak lever.** ~18 % of raw payload, ~9 % of a single app dir; the .NET runtime and `MudBlazor.dll` dominate (§6.4). Not worth N.2+N.3. |
| **Cleaner customer experience** | **Don't touch the installer.** Invest in the post-install Activate flow on the Studio License page, which ADR-0036 already started. Much cheaper, much lower risk. |

**Q2 — Which edition list is authoritative?** Blueprint §7.1 (Starter / Professional /
Enterprise / Custom, with `MtLinki` which has no adapter project) or the code
(`Starter / Professional / Enterprise / CncPro / TrialPeriod`)? I recommend the code, but
this is a commercial decision, not an engineering one.

**Q3 — Does `TrialPeriod` get the full binary set?** `LicenseEditionCatalog` offers it
everything. If every evaluator receives the Full installer, then packaging provides no IP
protection to anyone who trials first — only compliance value on paid installs. Is that
acceptable, or should there be a restricted trial build?

**Q4 — Has OPC Foundation Corporate membership cleared?** `module-catalog.md` records it as
"procurement initiated Phase 4 week 1; tracking as a release blocker for Milestone L". If it
has cleared, the single strongest driver for the Full/Standard split weakens considerably.
I could not determine the current status from the repository.

**Q5 — Are we knowingly redistributing the Fanuc FOCAS2 DLLs?** §6.3 shows the installer
does, and `module-catalog.md` says we do not. One of the two is wrong. This needs an answer
independently of whether this plan proceeds.

**Q6 — What does "installer application" mean literally?** A separate licensing-aware
launcher EXE (rejected here for ADR-0036 reasons), or simply "edition-specific installers"?
If the owner genuinely wants a launcher, the honest answer is that it can select an edition
but cannot *verify* entitlement on a fresh machine, and I would want that confirmed as
understood before building one.

**Q7 — Sparkplug B.** Built (`src/ElpisEdgeConnect.Sinks.SparkplugB/`, ADR-0035/0036-sparkplug)
but not referenced by the Host, not published, no module key, no picker tile. Does it get a
module key and an edition placement before N.1, or is it explicitly deferred?

**Q8 — Where does `Starter` sit?** `Starter` is Modbus-only, so the proposed `Cnc` variant
(FOCAS2 + Brother + MTConnect) does not serve it. Does `Starter` justify a fourth build
variant, or does it ship the `Standard` binaries and rely on the runtime gate alone? The
latter is cheaper and, given §7, loses nothing in enforcement terms.

---

## 15. Things I could not determine from the repository

Stated explicitly so nothing here is mistaken for a confirmed finding.

1. **Whether `hostpolicy` hard-fails pre-`Main` on a deps.json entry with no file, for this
   specific self-contained publish.** This is the central technical premise of §5.1. I was
   instructed not to build or run anything. Slice N.0 exists solely to settle it, and the
   plan explicitly pivots to Route R1 if the premise is wrong.
2. **Whether the exclusion set can be computed cleanly** for a variant without two real
   builds to diff (§6.2). Asserted from the dependency graph, not measured.
3. **The current OPC Foundation Corporate membership status** (Q4).
4. **Whether any CI exists outside this repository.** No `.github/`, no build scripts at
   root — but that only proves it is not *here*.
5. **Whether `AllowSameVersionUpgrades` interacts badly with the Burn bundle's separate
   `UpgradeCode`** (`c8f3d2b5-a04e-4d19-9b32-6a7e8f9d0c12`). Bundle-level upgrade semantics
   for edition changes were not investigated and must be covered in N.4's test matrix.
6. **How `WixUI_FeatureTree` should present a per-edition MSI.** With one required feature
   and a desktop-shortcut toggle it is already thin; whether it should be replaced by
   `WixUI_InstallDir` or `WixUI_Minimal` for edition builds is a UX call not made here.

---

## 16. Related installer work — uninstall data retention

Tracked separately in `docs/installer/uninstall-complete-delete-design.md` (2026-08-08):
a single **"Also delete all EdgeConnect data"** checkbox on uninstall which, when ticked,
removes `config\`, `buffer\` and `logs\` from the data root while keeping
`edgelicense.json`.

It is listed here because it touches the same manifest and shares one constraint with this
plan: **the license is node-locked to a gateway id** (ADR-0036) whose anchors live outside
the data root (ADR-0038). Any installer work that clears machine state has to decide, on
purpose, whether a retained license file remains usable afterwards. The uninstall design
answers "yes, keep the identity anchors"; a per-edition installer that ever re-provisions
identity would have to answer the same question.

The two efforts are otherwise independent and can ship in either order.

---

*This document is a proposal. No code, installer manifest, or configuration was changed in
producing it.*
