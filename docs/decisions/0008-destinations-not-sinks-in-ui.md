# ADR-0008: Operator-facing vocabulary — "Destinations" not "Sinks"

**Status:** Accepted
**Date:** 2026-05-15
**Milestone:** Standalone UI polish (`7b30e8e`)

## Context

"Sink" is the engineering-internal term used throughout Core
(`SinkInstanceConfig`, `ISinkAdapter`, `SinkHealthSnapshot`,
`RouteSinkSummaryDto`, etc.). It's idiomatic in stream-processing
literature (Flink, Kafka, NiFi).

UI-honesty check during M.2b.1 review: industrial-automation
operators on the factory floor recognise "Sources / Routes /
Destinations" — they do NOT recognise "Sinks." The word evokes
kitchen sinks or "drowning." Comparable industrial-gateway products
use "Destinations" (HighByte), "Targets," or "Outputs" — never
"Sinks" as the user-facing label.

## Decision

Split the vocabulary by audience:

- **Operator-facing labels** (Studio menu, page titles, button
  text, tooltips, alerts, empty-state copy): "Destinations" /
  "destination" (lowercase).

- **Engineering-internal identifiers** (type names, JSON keys in
  `gateway.json`, REST URL paths, Razor page routes, file names,
  test names, architectural code comments): **unchanged** — still
  `Sink*`. Same for the M.5 Device Inspector convention (ADR-0001):
  internal `IDeviceProbe` / `ProbeResult` / `ProbeCapability`;
  UI "Device Inspector."

## Reasoning

1. **Operators ≠ engineers.** The product's audience is plant
   operators and integrators, not data-engineering specialists.
   Using the engineering term as the UI label is a self-inflicted
   adoption friction.

2. **Internal stability matters.** Renaming Core types ripples
   into JSON keys, file names, audit chain hashes (audit codes
   like `DiagnosticsEventCodes.SinkDegraded` are SHA-256 chained
   so a rename requires a chain migration), test classes,
   sample configs, and existing customer documentation. The
   benefit doesn't justify the cost.

3. **The split has precedent in the codebase.** Phase 1 already
   distinguishes wire DTOs from Core records (e.g.,
   `RouteSummaryDto` vs `RouteHealthSnapshot`) — the same
   audience-aware separation, applied to vocabulary.

## Consequences

- Top-bar nav reads "Sources · Destinations · Routes · …"
- Page titles: `Destinations · Connectivity Studio`,
  `<id> · Destinations · Connectivity Studio`
- Tooltip / button / empty-state text uses "destination(s)"
  throughout the Studio
- REST URL stays `/api/v1/sinks`; page route stays
  `@page "/sinks"`; `SinkListItemDto` keeps its name
- JSON key `Sinks` in `gateway.json` unchanged — operators who
  paste backup JSON or import existing configs are unaffected
- `DiagnosticsEventCodes.Sink*` constants left in place
  (audit-chain hashed). A future migration ADR could revisit if
  the cost is ever justified.

## References

- Implementation: UI rename commit (`7b30e8e`, merged
  2026-05-15 onto master)
- 10 razor files touched, 0 type names changed
- Follow-up captured in todo list: optionally rename
  `DiagnosticsEventCodes.Sink*` if/when an audit-chain
  migration becomes worthwhile
