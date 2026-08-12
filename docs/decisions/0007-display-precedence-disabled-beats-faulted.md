# ADR-0007: Display-state precedence — Disabled > Faulted > live > Configured/Not running

**Status:** Accepted
**Date:** 2026-05-15
**Milestone:** M.P2.1 phase 3

## Context

M.P2.1 introduces a `Faulted` state name that surfaces in
`/sources`, `/destinations`, and `/routes`. M.2b.1.1 already
introduced `Disabled`, `Configured`, and `Configured / Not running`
synthetic state names alongside the existing live state names from
`AdapterState` / `RouteState` (`Running`, `Degraded`, `Failed`,
`Stopped`, etc.).

Multiple states can apply simultaneously to one instance:
- An operator may have disabled an instance that was Running
- A fault may exist in the registry while the runtime snapshot
  still reports Running (rare race)
- A snapshot may be missing because the upstream source faulted

The UI shows one state per row. Which wins?

## Decision

Top-down precedence:

```
1. Disabled                  — operator's strongest intent signal
2. Faulted                   — config fault OR runtime AdapterState.Failed
3. Live state from snapshot  — Running, Degraded, Stopped, Starting, …
4. Configured / Not running  — enabled, route exists, no snapshot
5. Configured                — enabled, no route, no fault (rare with fail-soft)
```

`Disabled` beats `Faulted`. `Faulted` beats `Running`.

## Reasoning

1. **Disabled is the operator's strongest intent signal.** If
   they explicitly disabled an instance, that's the state they
   want shown regardless of any lingering runtime snapshot OR
   stale fault entry. Anything else lets stale state override
   explicit user action.

2. **Faulted next** because fault urgency outranks the live state.
   A "Running" Modbus adapter that's also Faulted should display
   Faulted — the operator needs to see the problem, not a
   misleading green dot.

3. **Live state > synthetic config-only states.** Once a snapshot
   exists, it's authoritative for the runtime view (Running,
   Degraded, etc.). The config-only states (Configured / Not
   running, Configured) are placeholders for "we don't know yet";
   any real snapshot displaces them.

4. **In practice, Disabled instances rarely produce faults.** The
   protocol registration extensions skip disabled instances
   BEFORE the route-check (per Locked Decision #10 — per-adapter
   isolation), so a disabled source can't carry a cross-record
   fault from the boot path. But the precedence holds defensively
   for races during M.P2.2 hot-reload (operator disables an
   instance that just faulted — Disabled wins).

5. **The synthetic "Faulted" label intentionally differs from
   Core's `AdapterState.Failed` enum name.** Operators see
   consistent "Faulted" vocabulary across config faults and
   runtime adapter failures; the engineering-internal label is
   "Failed" (enum value). Same wire shape in `LastErrorCode` /
   `LastErrorMessage` regardless of source.

## Consequences

- All three inventory builders (`SourceInventoryBuilder`,
  `SinkInventoryBuilder` [phase 3b], `RouteInventoryBuilder`)
  implement the same precedence.

- `Sources.razor` `StateColor` switch maps each state name to a
  MudBlazor `Color`:
  - `Disabled` → `Color.Dark`
  - `Faulted` / `Failed` → `Color.Error`
  - `Degraded` / `Configured / Not running` → `Color.Warning`
  - `Configured` → `Color.Info`
  - `Running` → `Color.Success`

- Faulted-state tooltip text is the lockstep
  `"{ErrorCode}: {Message}"` — no `Configuration fault` vs
  `Runtime fault` kind prefix. Operator hovers the chip and
  reads the code; clicks the row for full detail.

- When BOTH a config fault AND a runtime fault apply to one
  source, config fault wins on the assumption that
  registration-time problems are upstream root causes. Tested
  in `SourceInventoryBuilderTests`.

## References

- Implementation: `SourceInventoryBuilder.cs` /
  `RouteInventoryBuilder.cs` (M.P2.1 phase 3a, `2636105`)
- ADR-0002 (Configuration = inventory truth) — the builders are
  the pattern this ADR governs
- Decision review: ChatGPT review pass, 2026-05-15, explicitly
  ordered "Disabled > Faulted > Running > Degraded > Unknown" as
  the recommended display precedence
