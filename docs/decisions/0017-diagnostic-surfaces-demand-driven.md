# ADR-0017: Diagnostic surfaces are demand-driven, not always-on

**Status:** Proposed (2026-05-30)
**Date:** 2026-05-30
**Framing:** What's the activation model for observational diagnostic surfaces (live taps, traces, per-item statuses, capture rings)? Always-on writes to logs and consumes CPU/memory whether anyone's looking; demand-driven activates only while an operator is actively viewing.

## Context

Today's multi-protocol pilot debugging session (3rd-party OPC server → EdgeConnect OPC Client source → route → EdgeConnect OPC Server sink → UaExpert) surfaced eight distinct bugs, several of which would have been visible immediately had the right diagnostic surfaces existed (per-MonitoredItem status, live data tap, supervisor capability coverage). The natural reaction is "add diagnostic logging everywhere."

That reaction is wrong. The runtime targets 30K monitored items per session at 50 ms publishing interval — sustained at peak load that's ~600K notifications per second. Always-on per-item logging at that rate would:

- Saturate disk I/O with log writes
- Dominate per-point CPU with serialisation overhead
- Surface customer data in log files (privacy)
- Produce log volumes that nobody reads (operator fatigue)

The operator's quote during this session was load-bearing:

> *"These logs, we need to log/show only when user in that page and wants to see it. We don't want to dump all the message when no one wants it."*

The principle is right. This ADR generalises it across every observational diagnostic surface.

P1 (Runtime Tap is strictly observational) already locks the read-direction asymmetry: nothing in the runtime hot path can read from subscribers. This ADR adds the activation-direction asymmetry: nothing in the runtime hot path materialises observable state until a subscriber requests it.

## Decision

Every observational diagnostic surface that captures, retains, serialises, or transmits per-point data MUST be **demand-driven**. The activation model:

### Rule 1 — Off by default, zero-cost when off

A diagnostic surface in its inactive state MUST add zero per-point work in the runtime hot path. Concretely:
- No per-point serialisation
- No per-point ring-buffer writes
- No per-point counter increments beyond what's already required for non-diagnostic state
- No per-point log lines

An adapter must not need to know whether anyone is watching to maintain correct adapter behaviour.

### Rule 2 — Activated by subscriber presence

A surface becomes active when at least one subscriber (typically a Studio page, occasionally an API client) opens an active connection to that surface. Activation is reference-counted: surfaces deactivate when the last subscriber disconnects.

Activation MUST be observable from the runtime side via a single check (typically a `volatile int _subscribers > 0` or an `IsAnyoneListening` flag exposed by the diagnostic service). The check MUST be O(1) and not lock-protected on the hot path.

### Rule 3 — Activation is bounded in scope

Activating a surface MUST NOT activate every related surface. A subscriber requesting the live data tap for `Source A` MUST NOT trigger capture on `Source B` or on any sink. Each surface has its own subscriber count.

### Rule 4 — Activation has bounded lifetime

A surface MUST disable itself after the last subscriber disconnects, plus an optional cooldown window (default: 60 seconds) to avoid flapping when an operator briefly navigates away and returns. The cooldown is the only state that persists past disconnect.

Implementation note: SignalR / Server-Sent Events connection lifecycle is the natural trigger. The Blazor circuit's `OnDispose` decrements the subscriber count.

### Rule 5 — Capture is bounded in volume

While a surface is active, the runtime captures into a **bounded ring buffer**, never an unbounded queue. The bound is per-surface (typically: 1,000 points for Live Data Tap, 100 events for protocol Connection Negotiation log, 50 traces for distributed trace). When the ring fills, oldest evictions are silent — no log line, no fault.

### Rule 6 — Sampling preferred over mirroring at high rates

For high-rate surfaces (Live Data Tap at 30K/sec), the runtime SHOULD sample rather than capture every point. Sampling strategy is per-surface (random N-of-M, deterministic every-Kth, reservoir sampling — whichever fits the surface's UX). Sampling MUST be documented per surface.

The UX must communicate sampling honestly: *"showing a sample of N of approximately M captured per second"*.

### Rule 7 — Privacy masking applies at capture time

When customer-sensitive fields cross a diagnostic surface (credentials, tag values flagged sensitive, certificate material), masking MUST happen at capture time, not at render time. A mistaken render path cannot leak data the capture path never accepted.

The masking policy is defined per-surface via the same allowlist mechanism the existing diagnostic plumbing uses (`_sensitiveTags` set + `SecretRedactor` from Phase 4 Backup work).

## Consequences

**Positive:**

- Runtime overhead at idle is zero — diagnostic surfaces don't compete with the hot path for CPU or I/O
- Production gateways under no-one-watching load behave identically to a build with diagnostic surfaces absent
- Operator fatigue from log noise is eliminated by construction
- Privacy footprint is bounded — sensitive data exists in the system only while someone is actively looking
- ADR composes with P1 (observational only) and existing route-events ring (per-route bounded buffer)

**Negative:**

- Operators get no diagnostic history before they open the page. *"Why was the source degraded 30 seconds ago?"* requires either reconstructing from coarser persistent metrics or running the system again with the page open. Mitigation: persistent counters and state-transition events stay always-on (those are low-cost). Only per-point capture is demand-driven.
- More surface area to test — every diagnostic surface needs an inactive-mode test and an active-mode test. The lint is an enforceable hot-path-clean test that runs at idle and asserts no per-point work happens.
- Hot-reload-while-watching can briefly miss data during the reconnect — acceptable.

**Forbidden patterns** (caught at review):

- A diagnostic surface that writes to disk even when no subscriber is attached
- A `_logger.LogInformation(...)` per point on the hot path (use the diagnostic surface, not the logger)
- A capture call that materialises a full clone of the point regardless of subscriber count
- A configuration flag that "turns on diagnostics" globally without subscriber tracking

## Reference

- Platform principle P1 — `docs/platform-principles.md`
- Multi-protocol pilot session — `docs/sessions/2026-05-30-opcua-client-wizard-debugging-followups.md`
- ADR-0018 (live data tap) — companion ADR that applies these rules to the specific tap surface
- ADR-0019 (capability coverage) — its diagnostic surface follows these rules
