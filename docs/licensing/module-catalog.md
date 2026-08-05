# License Module Catalog

**Status:** authoritative module-key list for v1 (Phase 4).
**Owner:** EdgeConnect runtime + licensing team.

This document is the canonical source of truth for module keys that
`ILicenseManager.IsModuleEnabled(string moduleKey)` accepts. Adapters,
sinks, and packaging editions reference these keys; renaming or
repurposing any key is a breaking change requiring a license-schema
revision (which the signature-validation layer would reject without
re-issuing licenses).

## Why module-level licensing

Per blueprint Locked Decision #5, EdgeConnect ships with three-layer
licensing: packaging (which editions exist), runtime activation (which
modules a deployed license enables), and UI/API enforcement (which
operations the management surface allows). This catalog is the runtime
layer's vocabulary.

Per Locked Decision #10 (per-adapter isolation), a license that disables
one source module must not affect any other source. The DI registration
hook in each `Add{Protocol}SourcesFromGatewayConfig` extension checks
`IsModuleEnabled` per source and skips registration cleanly when the
module is disabled. Other sources continue to register.

## Module key naming

```
{role}-{protocol}        e.g. source-modbus-tcp, sink-mqtt
{role}-{vendor}-{family} e.g. source-fanuc-focas2 (where vendor matters)
{role}-{feature}         e.g. connectivity-studio
```

Keys are lowercase, hyphen-separated, ASCII-only. They are STABLE
identifiers — once a license has been issued referencing a key, that key
cannot be renamed. Add new keys for new modules; never rename old ones.

## Canonical catalog (v1)

| Module key | Tier | Role | Notes |
|---|---|---|---|
| `core-runtime` | Base | Required | Always enabled when license is valid. No source / sink module gates run if this is missing. |
| `sink-mqtt` | Standard | Sink | MQTT publish (PerTag + Batch modes). |
| `sink-opc-ua-server` | Premium | Sink | OPC UA Server endpoint (Phase 4 Milestone H). |
| `sink-http` | Standard | Sink | Reserved — Phase 5. |
| `sink-tcp` | Standard | Sink | Reserved — Phase 5. |
| `source-modbus-tcp` | Standard | Source | Modbus TCP (Phase 3 shipped). |
| `source-focas2` | Premium | Source | Fanuc FOCAS2 (Phase 2 shipped; production-ship Phase 4 Milestone G). Customer must also have a Fanuc DLL license — distinct from this license module. |
| `source-mtconnect` | Standard | Source | MTConnect agent (Phase 2 shipped). |
| `source-brother-http` | Standard | Source | Brother Speedio (and other Brother CNCs) via built-in port-80 web-monitoring interface (Phase 2 — M.P2.4). No proprietary Brother license required; gate exists in the EdgeConnect license only. |
| `source-s7` | Premium | Source | Siemens S7 via Sharp7 (Phase 4 Milestone I). |
| `source-opc-ua-client` | Premium | Source | Reserved — Phase 4 Milestone J fork if Customer B's ABB needs it. |
| `source-ethernet-ip` | Premium | Source | Allen-Bradley EtherNet/IP (ControlLogix / CompactLogix / GuardLogix / MicroLogix / Micro800) via libplctag. Multi-protocol pilot expansion — MVP slice (manual tag list, polling). |
| `connectivity-studio` | Premium | UI | Management REST API + Blazor Server pages (Phase 4 Milestone M). Without this module, the host runs headless; data still flows. |
| `historian-bridge` | Premium | Integration | Reserved — Phase 5 EREMOS V2 historian-direct integration. |

## Tiering guidance for packaging

These are **defaults**, not contractual. Sales / commercial decisions
override. The categories exist to give a starting structure to the
packaging editions:

- **Base** — included in every edition, gated only on license validity
- **Standard** — included in mid-tier editions (e.g., "Connect" /
  "Plus")
- **Premium** — top-tier editions only (e.g., "Enterprise") or sold
  as module add-ons

The license file format (see `docs/licensing/license-file-format.md`)
already supports per-module enable/disable, so any tiering scheme can
be implemented by configuring the license's `modules` map.

## Enforcement points

The runtime enforces module enablement in two places:

### 1. Adapter DI registration (Phase 4 Milestone G.7)

Each `Add{Protocol}SourcesFromGatewayConfig` and
`Add{Protocol}SinksFromGatewayConfig` extension accepts an optional
`ILicenseManager?` parameter. When supplied AND the license is loaded
(`Current is not null`), the extension calls
`IsModuleEnabled(LicenseModuleKey)` per source/sink. Disabled modules
are skipped with a clear warning logged to the host's stdout — the
host continues starting up with the licensed modules.

**Permissive fallback**: when no license is loaded (dev / sim / soak
runs without a license file), enforcement is bypassed and every
configured source/sink registers. This is the right behavior for
non-production environments per blueprint Locked Decision #7
("Never cut customer data to enforce licensing").

### 2. Management API (Phase 4 Milestone M)

The Connectivity Studio API checks `IsModuleEnabled("connectivity-studio")`
at bootstrap. If the module is disabled, the management endpoints
return `403 LICENSE_MODULE_NOT_ENABLED` — the runtime continues
without a management surface; the host is headless.

## Adding a new module

When adding a new protocol or feature:

1. Add a new row to the table above with a stable module key.
2. Define `public const string LicenseModuleKey = "..."` on the
   protocol's primary configuration record (e.g., add it to
   `S7SourceConfiguration` when building Milestone I).
3. Update the protocol's `Add{Protocol}SourcesFromGatewayConfig` to
   accept `ILicenseManager?` and gate on `IsModuleEnabled`.
4. Reference the new key from `LicenseModuleKeys` in Core for code
   that needs a typed handle.

## Third-party library licensing (distinct from module licensing)

Module licensing (above) is what EdgeConnect sells. **Third-party
library licensing** is what governs whether we are allowed to *ship*
those modules as binaries to customers. The two are independent: a
customer can purchase the `sink-opc-ua-server` module from us, but we
ourselves must be properly licensed for any third-party library we
link against to legally distribute the build.

The cases that matter for v1:

| Library | License posture | Implication |
|---|---|---|
| **OPCFoundation/UA-.NETStandard** | Dual-licensed: GPL-2.0 OR OPC Foundation Reciprocal Community License (RCL). RCL granted to OPC Foundation Corporate Members. | We DEVELOP against the GPL-2.0 NuGet (no procurement needed). We SHIP under RCL once Corporate membership clears. **Customer distribution is gated on the membership.** Until then, the `sink-opc-ua-server` module is buildable + testable in-house but not redistributable. |
| **Sharp7** | MIT | Free for any use. No procurement gate. Ship-safe from day one. |
| **MQTTnet** | MIT | Free for any use. No procurement gate. |
| **Microsoft.Data.Sqlite** | MIT | Free for any use. |
| **Fanuc FOCAS2 DLLs** | Per-customer Fanuc commercial license | Customer is responsible for their own Fanuc DLL license. EdgeConnect does NOT redistribute the FOCAS2 DLLs — they are loaded from the customer's machine. This is documented in `docs/samples/gateway-focas2-cnc.json`. |

**OPC Foundation Corporate membership status**: procurement initiated
Phase 4 week 1; tracking as a release blocker for Milestone L (Customer
B pilot). H.1 / H.2 / H.3 development proceed against the GPL-2.0
package; the H milestone's "Definition of done" includes the explicit
clause *"commercial license confirmed acquired before any external
customer demo."*

**Multi-protocol pilot expansion (Week 1, PR 7a)**: the in-process
`OpcUaClientInProcessServerFixture` (under
`tests/ElpisEdgeConnect.Integration.Tests/OpcUaClient/`) links
`OPCFoundation.NetStandard.Opc.Ua.Server` for test-only purposes and
inherits this exact posture. Test projects are not redistributed; the
fixture's licensing exposure is strictly less than the production
`Sinks.OpcUaServer` adapter it parallels.

## See also

- `docs/licensing/license-file-format.md` — the on-disk license shape
- `src/ElpisEdgeConnect.Core/Licensing/LicenseModuleKeys.cs` — code-side
  constants matching this catalog
- `docs/ARCHITECTURE_BLUEPRINT.md` Locked Decisions #5, #6, #7, #10 —
  the licensing architecture's invariants
