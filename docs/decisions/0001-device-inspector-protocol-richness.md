# ADR-0001: Device Inspector richness varies by protocol

**Status:** Accepted
**Date:** 2026-05-15
**Milestone:** M.5 (Device Inspector — Phase 4 extension)

## Context

Operator UX gap surfaced during M.2b.1 review: the Fanuc MT-LINKi suite
shows configured options, available parameters, axis/spindle layout,
loaded tools, and live "is the CNC online?" state for every connected
machine. EdgeConnect today shows runtime adapter state but no
device-inspection surface.

Customer A (FOCAS2-based CNCs) commissioning would be transformed if
the Modbus wizard's "Try Read" affordance — currently a placeholder —
could connect to a CNC, read back its model + options bitmap + axis
configuration + active tools, and pre-populate tag definitions.

The natural question: should EdgeConnect ship a "Device Inspector"
surface for all protocols, or only those that support introspection?

## Decision

Build a Device Inspector milestone (`M.5`) with **per-protocol scope
explicitly varying by what the protocol natively supports**. Do NOT
force a uniform "rich inspection" abstraction across all protocols.

Locked richness levels:

| Protocol | Inspector richness |
|---|---|
| FOCAS2  | Full — model, options bitmap, axes, spindles, tools, alarms, live status |
| MTConnect | Full — render `probe.xml` as a navigable tree |
| OPC UA Client (future) | Full — address-space browsing is native |
| S7 | **Partial** — connectivity check + "try read this DB/address" only |
| Modbus | **Partial** — connectivity check + "try read this register" only |

## Sub-milestones

- **M.5a** — `IDeviceProbe` SDK in Core + FOCAS2 implementation +
  `/sources/{id}/probe` Studio page. Customer A unblocker.
  Schedule **after M.P2.1 + M.P2.2** (substrate must be in place),
  **before M.2b.3** (FOCAS2 wizard) so the wizard can lean on
  auto-discovery for tag pre-population.

- **M.5b** — MTConnect `probe.xml` display. Schedule with or before
  M.2b.4 (MTConnect wizard).

- **M.5c** — S7 + Modbus connectivity check + "try read" probe.
  Smaller scope (no rich inspector — protocols can't self-describe).
  Bundle with M.2b.2 (S7 wizard) and a Modbus revision pass.

- **M.5d** — Wizard probe integration. Each wizard's "Try Read"
  button calls the relevant probe; tag auto-discovery for FOCAS2 /
  MTConnect / OPC UA Client.

## Naming

- **UI label (operator-facing):** "Device Inspector"
- **Code identifiers (engineering):** `IDeviceProbe`, `ProbeResult`,
  `ProbeCapability`

This split mirrors the M.2b.1.1 / UI-rename pattern: the operator
mental model uses friendly nouns ("Inspector," "Destinations") while
internal code keeps engineering-precise terms ("Probe," "Sink").

## Reasoning

1. **Forcing rich inspection abstractions onto S7/Modbus would be
   dishonest.** Modbus has no concept of "what data does this device
   expose" — addresses are agreed by convention, not advertised. An
   abstraction that pretends otherwise would either lie (synthesise
   fake capabilities) or be useless on those protocols.

2. **FOCAS2 + MTConnect natively support exactly this.** FOCAS2's
   API surfaces machine metadata directly; MTConnect's `probe.xml`
   IS the self-description document. We'd be wasting their native
   capability if we forced a least-common-denominator abstraction.

3. **The strongest customer use case is Customer A's FOCAS2 fleet.**
   Twelve CNCs to commission; hand-entering tag definitions per
   machine is the major commissioning friction. Auto-discovery from
   the device IS the win. Modbus auto-discovery doesn't exist
   architecturally — Customer B (S7) gets the connectivity-check
   sub-feature but not the rich inspector.

4. **Operationally, Sources → Routes → Destinations is the operator's
   mental model.** A "Device Inspector" surface that's protocol-aware
   fits in as the **per-source detail enrichment** (`/sources/{id}/probe`)
   without breaking that model.

## Consequences

- New Core SDK: `IDeviceProbe`, `ProbeResult`, `ProbeCapability` —
  protocol-agnostic shape that allows partial implementations.
- Studio gets a new page route `/sources/{id}/probe` per source detail.
- Wizard milestones (M.2b.2/.3/.4) get auto-discovery features
  bundled in for protocols that support them; placeholder "Try Read"
  for those that don't.
- Operators with FOCAS2/MTConnect/OPC UA can probe; operators with
  Modbus/S7 see "connectivity OK, read address X returned Y" only.
- This is the first concrete locked architectural extension beyond
  Phase 4 — M.5 sits structurally as a Phase 4 sub-track (rather
  than its own phase) because it directly supports the existing
  wizard milestones.

## References

- Discussion: M.P2.1 Phase 3 planning session, 2026-05-15
- Related: Locked Decision #1 (Core is protocol-agnostic) — M.5's
  protocol-aware UI lives in Management, not Core; Core gets only
  the abstract `IDeviceProbe` SDK.
- Related: Locked Decision #10 (per-adapter isolation) — a failed
  probe call doesn't affect adapter polling.
