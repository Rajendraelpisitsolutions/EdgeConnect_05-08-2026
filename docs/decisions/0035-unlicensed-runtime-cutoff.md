# ADR-0035 — Unlicensed runtime cutoff (2-hour hard stop)

**Status:** Accepted (2026-07-07). **Overrides** ARCHITECTURE_BLUEPRINT Appendix A
locked decision **#7** and CLAUDE.md §3 #7 / §9 anti-pattern #? for the
non-`Valid` license case.

## Context

Locked decision #7 states: *"License expiration behavior. Continue data flow, block
config changes. **Never cut customer data to enforce licensing.**"* The platform
principle was that licensing gates configuration/activation only and **never** the
data path (the pipeline stays deterministic and always flows).

Product/commercial direction now requires a hard enforcement boundary: an
installation that is **not running under a valid license** must not operate
indefinitely. The gateway may run for a short grace window and then **stop**.

The product owner was presented with the conflict and three options
(no-license-only trial; hard cutoff for any non-valid state; keep #7 unchanged) and
explicitly chose the **hard cutoff for any unlicensed/expired state**, accepting
that this can also stop a previously-licensed gateway whose license has expired.

## Decision

While the active license status is anything other than
`LicenseStatus.Valid` (i.e. `NotLoaded`, `InGracePeriod`, `Expired`, or `Invalid`),
the runtime starts a **2-hour** timer. If the license is still not `Valid` when the
timer elapses, the host **stops the application** (`IHostApplicationLifetime.StopApplication()`),
halting the data pipeline and, when running as the installed Windows service, the
service itself.

- The window is measured from the moment a non-`Valid` status is first observed
  (effectively process start for an unlicensed install).
- If a `Valid` license is observed at any point before the deadline, the timer is
  reset and enforcement is dormant.
- Duration is configurable via `EDGECONNECT_LICENSE_TRIAL_MINUTES` (default `120`),
  primarily for testing/tuning.
- Implemented by `ElpisEdgeConnect.Host.LicenseTrialEnforcer` (a `BackgroundService`
  registered in `CompositionRoot.AddElpisEdgeConnectHost`, so it applies to **both**
  the headless Host and the Management/Studio service — which share the composition).
- The enforcement lives in the **Host** layer, not Core. Core remains
  protocol-agnostic and its pipeline remains free of license checks; the stop is
  effected by the host lifetime, not by a check inside the data path.

## Reasoning

- The commercial requirement (no indefinite unlicensed operation) outweighs, for
  this product, the original "never cut data" stance — the owner accepted the
  trade-off explicitly.
- Stopping via the host lifetime (rather than a conditional inside the pipeline)
  keeps the pipeline code itself deterministic and license-free; the cutoff is an
  outer supervisor.
- Making the window observable (loud, escalating warnings before the stop) and
  configurable keeps the behavior debuggable and testable.

## Consequences

- **Overrides locked decision #7.** A gateway with no license, an invalid license,
  or an expired license (including one within the former 30-day grace) will stop
  after 2 hours. The 30-day grace period is therefore effectively capped at 2 hours
  of runtime for enforcement purposes — operators must re-license and restart.
- **Risk accepted:** a previously-paying customer whose license lapses can have
  their gateway stop mid-operation. This is the explicitly chosen behavior; mitigate
  operationally with expiry warnings (already emitted at 30/7/1 days) and monitoring.
- No hot reload: recovering requires placing a valid `license.json` and restarting
  the service (consistent with the existing startup-only license load).
- `LicenseGate`/config enforcement is unchanged; this ADR only adds the runtime
  cutoff.
- ARCHITECTURE_BLUEPRINT Appendix A #7 and the CLAUDE.md anti-pattern should be read
  as superseded by this ADR for the non-`Valid` case.

## Alternatives considered

- **Trial only when NotLoaded** (never-licensed installs), leaving expired/grace
  flowing — rejected by the owner in favor of the stronger cutoff.
- **Keep #7 unchanged** (never stop data; enforce at config only) — rejected;
  does not meet the commercial requirement.
