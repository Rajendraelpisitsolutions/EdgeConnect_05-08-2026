# ADR-0002: Configuration = inventory truth; Diagnostics = runtime enrichment

**Status:** Accepted
**Date:** 2026-05-14
**Milestone:** M.2b.1.1

## Context

After M.2b.1 (Modbus wizard) shipped, a source added via the wizard's
"Do not wire yet" branch did NOT appear on `/sources`. Root cause:
the page walked `IDiagnosticsService.GetAllRouteSnapshots()` to
discover sources — anything without a runtime supervisor was
invisible.

Operationally, this is the most damaging trust-failure mode for the
product: an operator who just ran the wizard cannot find the device
they just added.

## Decision

Every UI surface that lists "what is configured?" walks
`IConfigurationManager.Current` FIRST, and only THEN enriches each
row with runtime state from `IDiagnosticsService`. The configuration
defines the set of rows; diagnostics decorates them.

This rule generalises beyond sources:

| Surface | Status |
|---|---|
| Sources (`/sources`) | ✅ M.2b.1.1 |
| Routes (`/routes`) | ✅ M.P2.1 phase 3a |
| Destinations (`/destinations`) | 🟡 M.P2.1 phase 3b |
| Tags (per-source) | ⏳ M.2c |
| OPC UA exposed nodes | ⏳ M.3a |

## Reasoning

1. **Configuration is what the operator authored**, via wizard or
   JSON edit. They expect to see exactly that — additions appear,
   removals disappear — independent of whatever the runtime managed
   to start.

2. **Diagnostics is a runtime-derived view.** It's incomplete
   (instances pending init), it's lagging (snapshots are eventually
   consistent), and it can be stale (instances removed from config
   may still have lingering state). Treating it as inventory truth
   produces a UX that doesn't match operator intent.

3. **The pattern is symmetric across entity types.** Sources, sinks,
   routes, tags, and OPC UA exposed nodes all answer the same
   question: "what's in the config?" — and the same enrichment:
   "what's the runtime saying about each?"

## Consequences

- `SourceInventoryBuilder` (M.2b.1.1), `RouteInventoryBuilder`
  (M.P2.1 phase 3a), and the upcoming `SinkInventoryBuilder` all
  follow the same shape: take config + snapshots + faults, emit
  wire DTOs in configuration order.

- Stale snapshots (e.g., a source removed from config but still
  referenced by a leftover RouteHealthSnapshot) are silently
  ignored. This is correct behavior — config drives the view.

- 404 semantics on `GET /api/v1/{sources,destinations,routes}/{id}`
  shifted from "not in diagnostics" to "not in current config."
  Pre-existing in-process consumers updated; no external consumers
  affected.

- The pattern composes cleanly with ADR-0005 (faults are runtime
  state). Faults overlay the runtime-enrichment step; they don't
  add or remove rows.

## References

- Implementation: `SourceInventoryBuilder.cs`,
  `RouteInventoryBuilder.cs` (M.2b.1.1 / M.P2.1 phase 3a commit
  bodies)
- Locked Decision #2 (canonical data model) — analogous: Core's
  contracts define the shape, runtime implementations conform.
