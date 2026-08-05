# ADR-0004: Fail-soft Core startup is default (no opt-out)

**Status:** Accepted
**Date:** 2026-05-15
**Milestone:** M.P2.1

## Context

Pre-M.P2.1, Core's startup registration extensions threw
`InvalidOperationException` on cross-record validation failures
(enabled source with no route, route referencing missing source,
route referencing missing sink, etc.). The hosting pipeline died,
which meant **the Studio also couldn't boot** — operator had no
in-product path to recover from a bad config. Only escape: SSH/RDP
to the gateway box and hand-edit `current.json`.

This guarantee — "I just need to reach the Studio to fix it" — is
the foundation of the product's operability promise. Fail-fast
startup broke it.

## Decision

Cross-record validation failures at gateway startup
**log + register a `ConfigurationFault` + skip the offending
instance + continue**. The gateway boots with whatever config IS
valid; faulted instances appear in the Studio with their error
codes for operator triage.

**No opt-out flag.** Every gateway (production, dev, CI) gets the
same fail-soft behavior.

## Reasoning

1. **Production-pilot risk.** Customer A (12 FOCAS2 CNCs) and
   Customer B (S7 + OPC UA) will hit cross-record validation
   issues during commissioning. Fail-fast at every misstep =
   support calls every time someone fat-fingers `gateway.json`.

2. **Schema-level validation still throws.** Malformed JSON,
   missing required fields, regex violations — these are caught
   by `IConfigurationManager.Load`'s schema check and still
   produce a hard failure. The fail-soft policy applies only to
   *cross-record* validation that runs AFTER the file has parsed
   cleanly.

3. **Opt-out via a `StrictStartup` flag was considered and
   rejected.** Arguments against:
   - One more config knob with unclear use case.
   - CI/test scenarios don't need fail-fast — they need fault
     visibility, which the registry provides.
   - "Strict mode for production" inverts the operability promise;
     production is exactly where fail-soft matters most.

4. **Three-layer defence (ADR-0003) makes "bad configs that reach
   Core" rare.** The wizard + the pure-transformation layer catch
   most violations at draft-build time. Core's fail-soft is the
   safety net for hand-edits and future API misuse.

## Consequences

- New service: `IConfigurationFaultRegistry` (Core/Diagnostics)
  with in-memory, runtime-only state (see ADR-0005). Surfaces
  faults via Studio.

- New audit-chain action: `RuntimeConfigurationFault` with
  `actor="system"` (see ADR-0006). Durable forensic record.

- Mechanical pattern across all 6 protocol registration extensions
  + `RouteDefinitionFactory.Build`: replace `throw new
  InvalidOperationException(...)` with `faultRegistry?.Register(...)
  + continue`. Identical diff per file; no protocol-specific
  redesigns inside the milestone (per ChatGPT review).

- `HostStartup` drains the in-memory registry into the audit chain
  AFTER `IConfigurationManager.InitializeAsync` completes (the
  audit log isn't writable any earlier).

- Disabled instances still skip registration unchanged; sink
  modules can still be disabled by license without triggering
  faults (Locked Decision #10 — per-adapter isolation — already
  governs this).

## References

- Implementation: M.P2.1 phase 1 (`6d85f38`), phase 2 (`7b5f3bc`)
- ADR-0003 (three-layer defence) — Core's layer is now fail-soft
  rather than fail-fast
- ADR-0005 (faults are runtime state) — defines the registry
  shape
- ADR-0006 (system-actor audit entries) — defines the durable
  record
