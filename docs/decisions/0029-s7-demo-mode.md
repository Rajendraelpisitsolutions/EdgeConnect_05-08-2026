# ADR-0029: Siemens S7 demo mode — env-var-toggled synthetic PLC backend

**Status:** Accepted (2026-06-04)
**Date:** 2026-06-04
**Milestone:** M.2b.2 follow-up
**Framing:** A faithful instance of the [ADR-0012](0012-focas2-demo-mode.md)
pattern, which explicitly anticipated this: *"Future contributors who want a
Modbus or S7 demo mode should follow the same pattern — per-protocol second
implementation, env-var toggle, license-gated — NOT generalise demo mode to the
Core layer."* This ADR records the S7-specific instantiation.

## Context

The S7 source wizard (M.2b.2) is operator-available, but demonstrating it
end-to-end — add source → save → adapter Running → live values flowing — needs a
real S7-1200/1500 PLC on the network. That blocks sales demos and dev/CI testing
on machines with no PLC. S7 demo mode adds a process-wide synthetic backend with
the same production-safety constraints ADR-0012 established for FOCAS2.

Unlike FOCAS2 (native `Focas2.dll` via P/Invoke), the S7 transport (`Sharp7`) is
already pure managed C#, so there is no native-DLL-load concern — but a demo
backend must still avoid opening real sockets and must be loudly visible so it
can never be mistaken for real telemetry in production.

## Decision

1. A new `IS7Client` implementation, **`S7DemoClient`**, lives alongside
   `Sharp7Client` in `Sources.S7`. Pure managed C#, no sockets. `ConnectAsync`
   always succeeds; `ReadAreaAsync` fills the buffer with a deterministic,
   time-varying big-endian `uint16` ramp (sine, 30s period) so Word/Int tags
   oscillate and Bool tags toggle — a live-looking demo.
2. `S7SourceAdapter`'s production constructor dispatches via
   `ChooseProductionClient()`: when `EDGECONNECT_S7_FAKE_MODE` is truthy it
   builds `S7DemoClient`, otherwise `Sharp7Client`. The choice is frozen for the
   process lifetime (read-once cache).
3. The toggle is **process-wide and env-var-only** (`S7DemoModeOptions`).
   Saved configuration cannot enable it — exactly one writer (the env-var
   parser). Accepted truthy values (case-insensitive, trimmed): `true`, `1`,
   `yes`; everything else (incl. unset) is disabled.
4. Demo mode **does not bypass license gating** — S7 sources still require the
   `source-s7` module (the existing `S7RegistrationExtensions` check is
   unchanged). Demo mode is not a license escape hatch.
5. Loud visibility through independent signals, mirroring FOCAS2:
   - `Console.Error` startup line with the distinct `"S7 FAKE MODE ACTIVE"` marker.
   - Sticky amber Studio banner on every page (`MainLayout.razor`, gated on
     `ManagementOptions.S7FakeMode` via `LayoutChromeModel`).
   - Prometheus gauge `edgeconnect_s7_fake_mode_enabled` (0/1, always registered).
   - A `GatewayStartupEvent` (`s7.fake-mode.activated`) on the Diagnostics surface.
   - Per-adapter health metric `metrics["demoMode"] = true` on demo-backed sources.
6. The synthetic PLC drives off an **injectable clock** (`Func<DateTime>`,
   production `() => DateTime.UtcNow`) so tests advance state deterministically
   with no `Thread.Sleep`.

## Reasoning

The seam (`IS7Client`) and the read-once env-var cache already match ADR-0012's
rationale verbatim: smallest possible change (second implementation behind an
existing interface), unforgeable-from-config activation, preserved license
gating, time-driven animation, and multi-channel visibility so accidental
production activation is caught by any one channel. The only S7-specific
simplification is that the reflection-based "no P/Invoke" invariant FOCAS2
needs does not apply — `Sharp7` is already managed — so the demo client's
guarantee is simply "no sockets," covered by its construction (no transport) and
the deterministic-bytes tests.

## Consequences

- `IS7Client` and the Core layer are unchanged.
- `S7SourceAdapter` gains a one-line production-ctor dispatch, a `demoMode`
  health metric, and an internal `ClientForTesting` accessor.
- New files in `Sources.S7`: `S7DemoModeOptions`, `S7DemoClient`,
  `S7FakeModeMeter`. Composition wires the gauge (always) + stderr/event/
  materializer (when active). `ManagementOptions.S7FakeMode` + `LayoutChromeModel`
  drive the banner.
- License gating (`source-s7`) governs demo S7 sources identically to real ones.
  No new license module.

## Out-of-scope follow-ups

- **Demo personas / multiple PLC profiles.** Single canonical synthetic curve in
  v1; a secondary env var could select profiles later.
- **Runtime toggling.** Env var read once at startup; restart-to-toggle only.
- **Deliberate-error simulation.** The demo always connects and reads; failure-path
  UX is exercised by Test Connection against an unreachable host.
- **Banner unification.** FOCAS2 and S7 currently render parallel banners; if a
  third demo-capable protocol lands, fold them into one "demo mode: X, Y" banner.

## References

- [ADR-0012](0012-focas2-demo-mode.md) — the governing pattern (this ADR is its
  S7 instance).
- S7 wizard track: `docs/sessions/2026-06-03-s7-source-wizard-handoff.md`.
