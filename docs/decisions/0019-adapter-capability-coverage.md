# ADR-0019: Adapter Capability Coverage — advertised flags MUST have runtime dispatchers

**Status:** Proposed (2026-05-30)
**Date:** 2026-05-30
**Framing:** When an adapter advertises a `SourceCapabilities` or `SinkCapabilities` flag, the host runtime MUST have a code path that consumes that capability. Without this rule, an adapter can ship a feature the runtime ignores — exactly what happened with OPC UA Client's `Subscription` capability and `SourceSupervisor.RunPollLoopAsync`.

## Context

The multi-protocol pilot debugging session (2026-05-30) was blocked for hours by bug #8: `OpcUaClientSourceAdapter` advertised `SourceCapabilities.Subscription` since PR 1, but `SourceSupervisor` unconditionally called `adapter.PollAsync(ct)`. Our adapter throws `NotSupportedException` on PollAsync because it's a subscription-mode adapter; the exception isn't an `AdapterException`, so the supervisor's catch blocks ignored it, the pump loop terminated silently, and the adapter sat in `Running` state with notifications stranded in its internal bounded channel.

The capability flag was decoration. The runtime never read it.

Fourteen PRs across the OPC UA Client series shipped without anyone noticing that no runtime code site honoured the `Subscription` flag. Five review passes across PRs #62–#65 missed it. The bug surfaced only when the operator read the stop-time stack trace.

This is not a one-off. Every new adapter capability runs the same risk:
- `SinkCapabilities.Browse` is advertised by `OpcUaServerSinkAdapter` — what code consumes it? If nothing, browsing the sink address space silently does nothing
- `SourceCapabilities.WriteBack` would be the natural addition for write-capable adapters — does the runtime accept writes back through the adapter? If nothing reads the flag, the writes go nowhere
- Future adapters advertising new flags (filter-pushdown, hot-reconfigure, partial-batches) repeat the pattern

The fix is structural: lock the requirement at the contract layer, enforce it with a test, and make missing coverage a compile-time / startup-time visible failure rather than a runtime-only silent one.

## Decision

The host runtime conforms to the following four rules.

### Rule 1 — Every advertised capability MUST have a documented runtime consumer

For every value in the `SourceCapabilities` flags enum AND the `SinkCapabilities` flags enum, the host runtime MUST contain at least one code path that consumes adapters advertising that flag. The consumer is documented in the enum's XML doc:

```csharp
/// <summary>
/// Adapter pulls data from the device via a polling loop.
/// CONSUMED BY: <see cref="SourceSupervisor.RunPollLoopAsync"/>
/// </summary>
Polling = 1 << 0,

/// <summary>
/// Adapter delivers data via a streaming subscription.
/// CONSUMED BY: <see cref="SourceSupervisor.RunSubscribeLoopAsync"/>
/// </summary>
Subscription = 1 << 1,
```

Adding a new enum value without a `CONSUMED BY:` doc tag is forbidden. The doc tag is the contract.

### Rule 2 — A startup self-check asserts coverage

At host startup (immediately after composition root completes), the runtime walks the registered source + sink list. For each adapter, the runtime inspects `Capabilities` and verifies that the supervisor's dispatcher logic handles every flag.

The check is enumeration-based, not adapter-based:

```csharp
foreach (var capabilityFlag in Enum.GetValues<SourceCapabilities>())
{
    if (capabilityFlag == SourceCapabilities.None) continue;
    if (!_supervisor.SupportsCapability(capabilityFlag))
    {
        throw new InvalidOperationException(
            $"STARTUP.CAPABILITY_UNHANDLED: {capabilityFlag} is declared in SourceCapabilities "
            + "but no host code path consumes it. Either add a dispatcher in SourceSupervisor "
            + "or remove the capability from the enum.");
    }
}
```

Crashing on startup with this error is preferable to running with the flag silently ignored. The error is operator-actionable: either add the runtime path or remove the flag.

`_supervisor.SupportsCapability(flag)` is an explicit method that returns true only when the supervisor has a code site that handles the flag. New supervisor code adds an entry; the test fails until both sides line up.

### Rule 3 — A unit test pins the runtime self-check shape

A Host.Tests test enumerates the `SourceCapabilities` and `SinkCapabilities` flag values and asserts the corresponding supervisor methods exist and are called.

The test is intentionally introspective — it walks the source tree, finds the `SourceSupervisor` and `SinkSupervisor` classes, and verifies each non-None capability has a corresponding handler. New adapters that add a capability flag without updating the supervisor MUST cause this test to fail at build time. The test failure becomes the carrot pulling the missing runtime code into existence.

### Rule 4 — The diagnostic surface exposes the capability matrix per ADR-0017

The Studio renders a "Capability Coverage" panel on the `/diagnostics` page. Per ADR-0017 it's demand-driven (the panel queries the runtime self-check result only when the operator opens it). The render shows:

| Capability | Advertised by (count) | Runtime consumer | Status |
|---|---|---|---|
| `Polling` | Modbus(2), FOCAS2(1), Brother(1), MTConnect(1), S7(0) | `SourceSupervisor.RunPollLoopAsync` | ✓ Handled |
| `Subscription` | OpcUaClient(1) | `SourceSupervisor.RunSubscribeLoopAsync` | ✓ Handled |
| `Browse` | OpcUaClient(1), Future(0) | `ITagBrowseService` consumers | ✓ Handled |
| `WriteBack` | (none yet) | (no consumer) | ⚠ No advertised use yet |

Operators see at a glance which capabilities are wired, which are advertised but unused, which would be unhandled if they tried. Adapter authors see the impact of a new flag before they write the code.

## Consequences

**Positive:**

- The exact failure mode that consumed today's debugging session becomes impossible — startup fails loudly with `STARTUP.CAPABILITY_UNHANDLED` rather than running with a silent gap
- Adds compile-time enforcement, not just review-time vigilance — Rule 3's test runs every build
- Surfaces capability-runtime gaps to adapter authors at design time, not at the first integration test
- Composes naturally with ADR-0017 — the diagnostic surface (Rule 4) follows the same demand-driven activation model

**Negative:**

- Adapters that legitimately advertise a capability the runtime will consume *eventually* (e.g., `WriteBack` declared early, runtime consumer planned for a later milestone) need a mechanism to opt out — a `[FutureCapability]` attribute on the enum value, removed when the consumer ships. Rare; tractable.
- Rule 3's introspective test is more complex than a typical unit test. Worth the cost given the bug class it prevents.
- The startup self-check (Rule 2) adds ~10 ms to startup. Acceptable.

**Forbidden patterns** (caught by Rule 2 / Rule 3):

- Adding a value to `SourceCapabilities` without a corresponding supervisor branch
- A supervisor catch block that swallows `NotSupportedException` on `PollAsync` (the actual bug — instead, the supervisor MUST not call PollAsync when the adapter doesn't support polling)
- A capability flag whose `CONSUMED BY:` doc references a method that doesn't exist (caught by Rule 3's signature check)

## Reference

- ADR-0017 — demand-driven diagnostic surfaces (Rule 4 composes with it)
- ADR-0018 — Live Data Tap (different surface; this ADR is the structural counterpart)
- Multi-protocol pilot session — `docs/sessions/2026-05-30-opcua-client-wizard-debugging-followups.md` (bug #8 is the load-bearing example)
- `src/ElpisEdgeConnect.Core/Adapters/SourceCapabilities.cs` — the enum this ADR governs
- `src/ElpisEdgeConnect.Host/Adapters/SourceSupervisor.cs` — the runtime dispatcher that must conform
