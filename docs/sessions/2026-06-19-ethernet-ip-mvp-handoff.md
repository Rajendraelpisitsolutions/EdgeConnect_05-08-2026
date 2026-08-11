# EtherNet/IP MVP slice — session handoff (2026-06-19)

**Outcome:** Allen-Bradley EtherNet/IP source adapter shipped as an
operator-available MVP slice (manual tag list, polling). Tile is Available at
`/sources/new/ethernet-ip`. See ADR-0031 for the decision record.

## What shipped

**New adapter project** `src/ElpisEdgeConnect.Sources.EthernetIp/` (replaced the
skeleton `AssemblyMarker.cs`):
- `EthernetIpSourceAdapter` (`ISourceAdapter`, `Polling | Browse`), config +
  `FromSourceInstance`, `EthernetIpConnectionManager` (backoff + circuit
  breaker), `IEthernetIpClient` seam + `LibPlcTagClient` (libplctag), element-type
  + CPU-family mappers, connection keys, error catalog, tag validator, bundle
  redaction rules.

**Host wiring:** `EthernetIpRegistrationExtensions` (license-gated on
`source-ethernet-ip`), dispatch in `RegistrationFactory.BuildSource`, redaction
registration, `EdgeConnectComposition` boot registration,
`LicenseModuleKeys.SourceEthernetIp`.

**Management:** `EthernetIpProbeService` + `EthernetIpProbeApi`
(`POST /api/v1/sources/browse/ethernetip/test-read`), `EthernetIpSourceWizardModel`,
`AddEthernetIpSource.razor`, picker tile, `SourceEditRouter` edit dispatch,
hosting registration.

**Tests:** 64 in `Sources.EthernetIp.Tests` (element types, CPU family, config
parse, validation, redaction, connection-manager backoff/breaker, adapter
lifecycle/poll/decode/scale/bad-point/fatal/scan-rate/browse/health). Wizard-model
+ probe-service tests added to `Management.Tests`. Pinned-list tests updated
(picker tiles, edit-router scope, Host redaction registration).

## Verification

- Whole solution builds 0 warnings / 0 errors.
- `Sources.EthernetIp.Tests` 64/64, `Management.Tests` 1088/1088,
  `Host.Tests` 152/152 (one flaky startup-ordering test passed on re-run; not
  related to this change).
- **Native libplctag confirmed on Windows** via a throwaway `plc_tag_create`
  smoke (`ErrorTimeout` against an unreachable host — native extracted, not a
  `DllNotFoundException`). Harness deleted.
- **No live-PLC validation** — no hardware/simulator on this machine. All tests
  use in-memory fakes. Live validation deferred to a Studio 5000 Logix Emulate /
  CCW seat.

## Deferred follow-ups (per v2.1 §5.2 — Stage 2)

- `Browse/UdtTreeWalker` + `EthernetIpBrowseService` + browse API; vendored
  `TagInfoDecoder` / `UdtInfoDecoder` (mapper-deprecation hedge §3.2).
- `Cov/ClientSideCovLayer`; `ReconfigureAsync` hot tag add/remove override
  (currently inherits the default Stop→Init→Start).
- `TagBrowseTreeView` wizard wiring (browse "Connect & Browse" + auto-load).
- Per-tag diagnostics collector (currently inline health metrics only).
- "Queue all, await all" block-read optimization (MVP reads tags sequentially).
- Full CPU-family smoke matrix (L1x / Micro800 pre-week-5 smoke per v2.1 §Q8).

## Notes for the next session

- The `IEthernetIpClient` seam already matches Stage-2 needs — browse/COV add
  files rather than rewriting.
- Error classification parses the `Status` enum name out of
  `LibPlcTagException.Message` (v1.5 exposes no `Status` property).
- L8x front-port path default `"1,0"` is baked into the CPU-family defaults and
  re-applied by the wizard on family change.
