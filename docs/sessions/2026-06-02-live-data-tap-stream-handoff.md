# Live Data Tap — Stream slice completion handoff

**Date:** 2026-06-02
**Status:** **DONE — Live Data Tap (Stream mode) is operator-available.** The page
is at `/diagnostics/tap`, reachable from a **Live Data Tap** button on the
Diagnostics page. Live-verified against the real gateway (MTConnect + Modbus).

## Why it was built

During the 2026-06-01 data-delivery incident the operator went looking for a
surface to answer "what data comes from the source, what reaches the sink?" and
couldn't find one — it was designed (ADR-0018 / ADR-0017 + mockups, 2026-05-30)
but never built. This session built the **Stream slice** end-to-end following
the plan-trail (`…live-data-tap-plan-v1.md` → ChatGPT review → `…-plan-v2.md`).

## What shipped (M0–M4, all on `master`)

| M | Commit | What |
|---|--------|------|
| M0 | `ad086b9` | Mockups re-reviewed + refreshed to plan v2; pipeline-trace deferred |
| M1 | `1c74319` | `IRouteTap` Core capture service — demand-driven, O(1) hot-path guard, per-sink bounded rings (BoundedEventLog), clock-based cooldown, correlationId |
| M1.5 | `8c822f8` | **ADR-0018A** tap value-privacy policy — `gateway.sensitiveTags` allowlist + `SensitiveTagPolicy` + `TapValueMasker` (value-only mask) |
| M2 | `43291d1` | Capture hooks on the data path — source (post-filter, **pre-transform**) + per-sink (pre-publish), behind `IsTapActive`; DI-wired with a reload-correct masker |
| M3 | `8b2d709` | SSE endpoint `GET /api/v1/diagnostics/tap/{routeId}` + `TapStreamWriter` (subscribe-on-open / unsubscribe-on-close) + wire DTOs |
| M4 | `59eec30`, `87002f5` | `Tap.razor` Stream page (HTTP/SSE consumer per the Management↔Core isolation rule); entry point on the Diagnostics page header (not a top-nav button — kept the nav uncluttered) |

## Architecture decisions worth remembering

- **Demand-driven, P1-observational.** Capture is a no-op at idle; the ONLY
  data-path cost when no one watches is one `IsTapActive` volatile read per
  batch. The integration test asserts zero capture with no subscriber.
- **Source tap = pre-transform** (the load-bearing review correction) so Compare
  can see the transform delta; sink tap = the batch handed to `PublishAsync`.
- **Mask at capture, never at render** (ADR-0017 Rule 7). Cleartext sensitive
  values never enter a ring or the SSE stream. The masker reads a LIVE policy
  (`SensitiveTagPolicyProvider`, rebuilt on config reload) — a privacy control
  must not go stale.
- **Razor consumes the REST API only.** The page is a pure HTTP/SSE consumer —
  `ManagementProjectIsolationTests` forbids injecting Core/Host services into
  components. (First attempt injected `IRouteTap` directly and was rejected by
  that test; rewrote to consume the SSE endpoint.)
- **correlationId tie-breaker:** `…|deviceTimestampTicks|sequenceNumber` — poll
  adapters emit several points sharing a device timestamp; the sequence number
  prevents Compare mis-pairing.

## Verification

- **Tests:** 953 Core + 152 Host + 879 Management green; 33 tap tests
  (RouteTap activation/isolation/fan-out/eviction/cooldown/masker hook,
  TapValuePrivacy, RoutingEngineTap integration hot-path-clean, TapStreamWriter
  lifecycle + masking).
- **Live (real gateway):** SSE streamed **684/684** balanced source/sink
  captures for MTConnect (real OKUMA data incl. `production/parts_count` typed
  `Long`) and Modbus (`Tempture=1335`); same `correlationId` on both ends
  (Compare-ready); `active:true`, demand-driven; page serves HTTP 200 with the
  rendered shell + nav. Operator confirmed the UI.
- **Not headlessly verifiable:** the interactive browser render + the
  stop-sink-watch-sink-go-quiet incident-signature test (browser automation
  unavailable this session) — left to operator confirmation.

## Deferred follow-ups (ADR-0018 / ADR-0018A remain the contract)

- **Inspect mode** (v1.1) — expand one capture's full canonical record;
  click-through to Source/Sink detail. (The per-stage "point trace" card from
  the mockup was explicitly deferred at M0 — needs hot-path instrumentation that
  conflicts with ADR-0017.)
- **Compare mode** (v1.2) — comparator + verdicts + counters + snapshot export;
  ships **transform-naive** with a banner first; transform-aware verdicts need a
  transform `DescribeFieldChanges` (ADR-0015 amendment).
- **True rate-based reservoir sampling** (ADR-0018 Rule 5) — OPC-UA scale only;
  today `RouteTapStatus.Truncated` is the honest "recent sample" signal.
- **Source/route-scoped sensitive-tag masking** — ADR-0018A defers the scoping
  qualifier; v1 masks by tag-name pattern.
- **Route-detail "Tap this route" link** — optional convenience entry point.

## Config note

New gateway config field: `gateway.sensitiveTags` (list of exact/glob tag-name
patterns; case-insensitive). Empty by default → masks nothing. Patterns are not
secret (`[BundleTier.Include]`; appear in diagnostic bundles). A no-UI field for
now — operators set it in config JSON; a Settings surface could expose it later.

## Reference

- ADR-0018 (Live Data Tap, now Accepted), ADR-0018A (tap value privacy), ADR-0017 (demand-driven)
- Plan trail: `2026-06-01-live-data-tap-plan-v1.md`, `…-plan-v2.md`
- Mockups: `docs/sessions/2026-05-30-ux-mockups/{1-tap-stream,2-tap-compare,3-tap-inspect}.html`
- Key code: `Core/Diagnostics/{IRouteTap,RouteTap,TapValueMasker,SensitiveTagPolicy}.cs`,
  `Core/Routing/RouteWorker.cs` (hooks), `Management/Api/TapApi.cs`,
  `Management/Diagnostics/TapStreamWriter.cs`, `Management/Components/Pages/Tap.razor`
