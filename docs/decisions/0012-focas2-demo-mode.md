# ADR-0012: FOCAS2 demo mode — env-var-toggled simulation backend for operator demos and CI

**Status:** Accepted (2026-05-18)
**Date:** 2026-05-18
**Milestone:** M.2b.3.1
**Framing:** **Demo mode is a simulation backend for operator demos and CI. It is NOT a protocol-abstraction concept.** Future contributors who want a Modbus or S7 demo mode should follow the same pattern (per-protocol `IXxxApi` second implementation, env-var toggle, license-gated) — NOT generalise demo mode to the Core layer.

## Context

M.2b.3 shipped the FOCAS2 Studio wizard with a Browse Controller probe ([ADR-0011](0011-browse-controller-reuses-browsetagsasync.md)). Without a real Fanuc CNC or the FOCAS2 native library installed, the wizard's happy path — Browse-returns-axes-and-tags, save-draft, source-comes-up-Running emitting canonical points — cannot be demonstrated end-to-end. This blocks two recurring workflows:

1. **Sales demos.** No hardware to ship; no customer-site access to coordinate.
2. **Dev testing.** Engineer laptops typically don't have the Fanuc native library installed.

M.2b.3.1 adds a process-wide demo-mode toggle. Two constraints shape the design:

1. **The `ISourceAdapter` contract is LOCKED.** Demo mode must not require a contract revision.
2. **Production safety.** An accidentally-enabled fake source in production would silently feed synthetic data instead of real telemetry. The activation path must be loud, narrow, and unforgeable from saved configuration.

## Decision

1. A new `IFocas2Api` implementation, `Focas2DemoApi`, lives alongside `Focas2NativeApi` in `Sources.Focas2`. It's pure managed C# with **NO P/Invoke or `Focas2Interop` reference**. Verified by reflection tests + a no-DLL behavioural test.
2. `Focas2SourceAdapter`'s production constructor dispatches: when `EDGECONNECT_FOCAS2_FAKE_MODE` is truthy, it constructs the demo API; otherwise the native API. The same dispatch applies to runtime adapters AND the Browse Controller throwaway adapter.
3. The toggle is **process-wide and env-var-only**. Saved configuration (`gateway.json`, draft API, any persisted store) **cannot** enable it. `Focas2DemoModeOptions.IsEnabled` has exactly one writer (the env-var parser) — verified by code review.
4. The toggle **does not bypass license gating**. FOCAS2 sources still require the `source-focas2` license module to be enabled (via the existing `Focas2RegistrationExtensions.ResolveSourceRegistrationInputs` check). Demo mode is NOT a hidden license escape hatch.
5. Demo mode is loudly visible through **four independent signals**:
   - `Console.Error.WriteLine` at startup with the distinctive `"FOCAS2 FAKE MODE ACTIVE"` marker phrase.
   - Sticky amber Studio banner across every Studio page (`MainLayout.razor`).
   - Prometheus gauge `edgeconnect_focas2_fake_mode_enabled` (value 0 or 1, always registered).
   - Per-source health metric `metrics["demoMode"] = true` on every `Focas2DemoApi`-backed adapter.
   - One `GatewayStartupEvent` appended to the new `IGatewayStartupEventStore` (surfaces on the Studio's Diagnostics page).
6. The synthetic CNC drives off an **injectable monotonic clock** (`Func<DateTime>`) so unit tests can advance state deterministically without `Thread.Sleep`. Production wires `() => DateTime.UtcNow`.

### Accepted toggle values (env var)

`EDGECONNECT_FOCAS2_FAKE_MODE` parses (case-insensitive, trimmed):

- **Truthy:** `true`, `1`, `yes`
- **Falsy/unset:** `false`, `0`, `no`, empty string, missing, anything else

### Synthetic CNC profile (canonical, single persona for v1)

| Aspect | Behaviour |
|---|---|
| Identity | series `31i-B5`, type `M` (machining centre), version `1.00` |
| Axes | 3 (X / Y / Z), sinusoidal positions bounded ±100 mm |
| Cycle | 60s: Reset 10s → Start 40s (cutting) → Stop 10s |
| Spindle | 0 in Reset/Stop, ramps 0→3000 rpm linearly during Start |
| Parts counter | increments at each Start→Stop transition |
| Tool | cycles T1 / T5 / T9 every 3 cycles |
| Alarm | SV0432 fires for ~5s every 4th cycle |
| MtLinki | servo temps ~35±3°C, fans OK, batteries OK |

## Reasoning

1. **`ISourceAdapter` is preserved.** Adding `TestConnectAsync` (ADR-0011's Path B alternative) would have been disproportionate scope. The `IFocas2Api` seam already exists for `FakeFocas2Api` in tests; adding a second production implementation is the smallest possible change.

2. **Env-var-only activation.** Saved configuration can be corrupted, copied between environments, or altered without a redeploy. The env var requires explicit operator action at process start. Combined with the cache-on-first-read pattern, demo mode is impossible to enable mid-process or from rogue config.

3. **License gating preserved.** A FOCAS2 source registered in demo mode flows through the same registration helper as a real source; the license check (`source-focas2` module enabled) fires identically. Sales-demo distributions either ship with no license loaded (permissive dev path) or with a demo license that has the module enabled.

4. **Time-driven state machine.** A demo dashboard showing frozen values would look broken. Animation via `DateTime.UtcNow - _startedAt` keeps the demo alive. The injectable clock (Locked G) gives tests deterministic state without any sleep-based timing.

5. **Four signals, not one.** Single-channel visibility is brittle (log aggregator down, monitoring down, operator missed banner). Four orthogonal signals — stderr, Studio banner, Prometheus gauge, Diagnostics event — make accidental production activation loud enough to be caught by any one channel.

6. **No `Critical` severity in `DiagnosticsSeverity`.** The wire enum has `Info` / `Warning` / `Error`. The `GatewayStartupEvent.Severity = "Critical"` projects to `DiagnosticsSeverity.Error` at aggregation time (the most severe wire value). This avoids a contract change to the public severity enum.

## Consequences

- `ISourceAdapter` and the Core layer are unchanged.
- `Focas2SourceAdapter` gains a one-line dispatch in its production ctor + a conditional health-metric (`demoMode: true` when `_api is Focas2DemoApi`) + an internal test accessor (`ApiForTesting`).
- A new env-var-only safety surface exists: a code-review invariant that `Focas2DemoModeOptions.IsEnabled` has exactly one writer (the env-var parser). Pinned by manual code review per Locked H.
- A new test invariant exists: `Focas2DemoApi` cannot acquire a P/Invoke reference. Pinned by reflection tests (`Focas2DemoApiTests.DemoApiType_HasNoDllImportAttributes` and `DemoApiType_HasNoStaticConstructor`) + the behavioural `DemoApi_FullMethodSweep_NeverThrowsDllNotFoundException` test.
- License gating (`source-focas2`) governs demo-mode FOCAS2 sources identically to real ones. No new license module.

### v3 amendment — new Core surface for boot-time signals

A new general-purpose `IGatewayStartupEventStore` is introduced in `ElpisEdgeConnect.Core/Diagnostics/` to surface boot-time process-state observations to the Studio's Diagnostics surface without abusing the audit chain (which describes config CHANGES per ADR-0006) or the per-route event ring buffers (which require a route scope).

The demo-mode activation is the first consumer; future use cases include license-state alerts, native-library-load warnings, and manifest-mismatch signals. The store is append-only for the process lifetime, in-memory only, bounded retention via `BoundedEventLog<T>` (256-entry default), and lives in Core so both `EdgeConnectComposition` (Host) can write to it and `DiagnosticsEventAggregator` (Management) can read from it without inverting the project dependency graph.

This new surface mirrors the established `IConfigurationFaultRegistry` pattern (stateful registry in Core, written by Host, read by Management).

## Out-of-scope follow-ups

- **Demo mode for other protocols** (Modbus / S7 / MTConnect). Each protocol's demo mode is its own follow-up if/when needed, following the same pattern: per-protocol `IXxxApi` second implementation + env-var toggle + license-gated.
- **Demo personas** (lathe vs machining centre, multiple axis counts). Single canonical profile in v1. Multiple personas via a secondary env var like `EDGECONNECT_FOCAS2_FAKE_PROFILE=lathe` is a future enhancement.
- **Runtime toggling.** Env var is read once at process start (Locked F). Restart-to-toggle is the only path; mid-process toggle adds complexity and a race window where some adapters are real and some are fake.
- **Per-source `fakeMode` config field.** Process-wide only. Per-source granularity would fuzz the prod-safety story.
- **Deliberate-error simulation hooks.** The demo fake always succeeds. Failure-path UX is already exercised by Browse against an unreachable IP.

## References

- [ADR-0011](0011-browse-controller-reuses-browsetagsasync.md) — Browse Controller reuses `BrowseTagsAsync`. Demo mode applies symmetrically to the Browse probe and the runtime adapter without disturbing ADR-0011's "discovery is management-plane ephemeral" principle.
- ADR-0006 — System-actor audit entries (the audit chain describes config CHANGES; the new `IGatewayStartupEventStore` describes process-state OBSERVATIONS — distinct purposes).
- M.2b.3.1 plan trail:
  - [`v1`](../sessions/2026-05-18-mp2b31-focas2-demo-mode-plan.md)
  - [`v2`](../sessions/2026-05-18-mp2b31-focas2-demo-mode-plan-v2.md) (ChatGPT review folded)
  - [`v3`](../sessions/2026-05-18-mp2b31-focas2-demo-mode-plan-v3.md) (Step 1 reality-check folded — added `IGatewayStartupEventStore` surface)
