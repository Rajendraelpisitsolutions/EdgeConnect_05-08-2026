# 0031 — EtherNet/IP source adapter ships as an MVP slice (manual tags) before UDT browse

**Status:** Accepted (2026-06-19)
**Relates to:** multi-protocol pilot expansion plan v2.1 (`docs/sessions/2026-05-28-multi-protocol-pilot-plan-v2.1.md`) §3, §5.2; ADR-0015 (wizard contract)

## Context

v2.1 §5.2 specifies a full browse-capable EtherNet/IP adapter (UDT tree walker,
vendored `TagInfo`/`UdtInfo` decoders, client-side COV, `ReconfigureAsync`
hot-swap override, browse wizard reusing `TagBrowseTreeView`). That is ~2,400
production LOC and depends on the same browse infrastructure the OPC UA Client
track built.

The pilot needs an operator-shippable EtherNet/IP tile sooner than the full
browse feature can land, and the Modbus/S7 adapters already prove a manual
tag-list polling adapter is sufficient for the first wave of Allen-Bradley
deployments (operators paste known tag names from Studio 5000).

## Decision

Ship EtherNet/IP in two stages. **Stage 1 (this change) is an MVP slice** that
mirrors the Modbus/S7 vertical-slice pattern:

- `ISourceAdapter` with `Polling | Browse` capabilities, where Browse returns the
  **configured** tag list (no controller introspection) — identical to Modbus.
- Manual tag editor wizard tile (`/sources/new/ethernet-ip`), Available.
- libplctag-backed client behind an `IEthernetIpClient` seam (real
  `LibPlcTagClient` + test fakes), connection manager with backoff + circuit
  breaker, per-tag scan scheduling, scale/offset, Bad-quality points on read
  failure, read-only "Test read" probe.
- Atomic element types (BOOL/SINT/INT/DINT/LINT/REAL/LREAL) + AB STRING only.

**Stage 2 (deferred, tracked):** `Browse/UdtTreeWalker` + `EthernetIpBrowseService`
+ browse API, vendored `TagInfo`/`UdtInfo` decoders (mapper-deprecation hedge
per v2.1 §3.2), `Cov/ClientSideCovLayer`, `ReconfigureAsync` hot tag add/remove
override, `TagBrowseTreeView` wiring, full CPU-family smoke matrix.

### Supporting choices

1. **libplctag (MPL-2.0) is the CIP stack** — already pinned in the csproj per
   v2.1 §3. Native binaries are embedded in `libplctag.NativeImport` and
   auto-extract at first use; confirmed working on Windows via a throwaway
   `plc_tag_create` smoke (returned `ErrorTimeout`, not `DllNotFoundException`).
2. **Lazy-connect model.** libplctag establishes and self-heals CIP sessions on
   first read per gateway+path. `LibPlcTagClient.ConnectAsync` records the target
   and marks the client live; genuine transport failures surface on
   `ReadTagAsync` as `EthernetIpFatalException`, which the connection manager
   turns into backoff + breaker behaviour — the same shape as the Modbus
   executor's fatal-transport path.
3. **Error classification by `Status` parsed from the exception message.**
   `libplctag.LibPlcTagException` exposes no `Status` property in v1.5; it sets
   `Message` to the `Status` enum name (e.g. `"ErrorTimeout"`). We
   `Enum.TryParse<Status>(ex.Message)` to split tag-level (non-fatal: not-found,
   bad-param, unsupported) from transport-fatal errors.
4. **L8x default path `"1,0"`** baked into the CPU-family defaults and applied by
   the wizard on family change, per v2.1 §3.1 risk register.

## Consequences

- An EtherNet/IP tile is Available now; operators can connect to Logix
  controllers with known tag names. Tile being Available does **not** imply
  browse/UDT support — the wizard is a manual editor and the handoff/docs say so.
- The `IEthernetIpClient` seam already matches what the Stage-2 browse + COV work
  needs; Stage 2 adds files, it does not rewrite Stage 1.
- Live-PLC validation is deferred to whoever has hardware/a simulator (Studio
  5000 Logix Emulate / CCW); CI exercises the adapter against in-memory fakes.
- MPL-2.0 attribution for libplctag is required — see
  `docs/licensing/third-party-notices.md`.
