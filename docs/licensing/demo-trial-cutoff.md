# Elpis EdgeConnect — Demo / Unlicensed Runtime Cutoff (2-Hour Trial)

**What it is:** a hard runtime limit for gateways that are **not running under a
valid license**. Such an install ("demo"/unlicensed) runs for a fixed window
(default **2 hours**) and then **stops the application**.
**Status:** implemented; governed by **ADR-0035** (owner-approved override of locked
decision #7). Shipped in installer **v1.2.0.0**.
**Code:** `src/ElpisEdgeConnect.Host/LicenseTrialEnforcer.cs`.

> ⚠️ This deliberately overrides the original rule *"never cut customer data to
> enforce licensing"* (Blueprint Appendix A #7 / CLAUDE.md §3 #7). See ADR-0035 for
> the decision, reasoning, and accepted consequences.

---

## 1. Behavior in one paragraph

On startup the gateway loads its license (if any). A background supervisor,
`LicenseTrialEnforcer`, watches the license **status**. While the status is
**anything other than `Valid`** (`NotLoaded`, `InGracePeriod`, `Expired`,
`Invalid`), a timer runs. If the license is still not `Valid` when the window
elapses, the supervisor **stops the whole application** — halting the data pipeline
and, when installed as the Windows service, the service itself. A `Valid` license
observed before the deadline resets the timer and the supervisor goes dormant.

---

## 2. Exactly when does it stop?

| License status | Meaning | Counts toward the cutoff? |
|----------------|---------|---------------------------|
| `Valid` | valid, unexpired license loaded | **No** — enforcer dormant, runs indefinitely |
| `NotLoaded` | no license file present at all | **Yes** |
| `InGracePeriod` | expired, within the former 30-day grace | **Yes** (ADR-0035 hard cutoff) |
| `Expired` | expired past grace | **Yes** |
| `Invalid` | signature/parse failed on load | **Yes** |

- The window is measured **from the first non-`Valid` observation** — effectively
  process start for a demo/unlicensed install.
- If the license becomes `Valid` before the deadline, the timer **clears**; if it
  lapses again later, the window **restarts** from that point.
- Default window: **2 hours**. Status is re-checked every **30 seconds**.

> Because the hard cutoff includes `InGracePeriod`, the former 30-day grace period
> is effectively capped at 2 hours of runtime for enforcement purposes. This is the
> explicitly chosen behavior (ADR-0035).

---

## 3. What "stop" means

The enforcer calls `IHostApplicationLifetime.StopApplication()`:

- **As the installed Windows service** (`ElpisEdgeConnect`): the service stops. With
  default recovery settings it stays stopped (no auto-restart). The Studio UI at
  `http://127.0.0.1:5080` also goes down (the Studio process *is* the service).
- **As a console/headless run**: the process shuts down gracefully and exits.

The data pipeline (sources → routing → store-and-forward → sinks) stops with the
host. Store-and-forward buffers on disk are preserved and resume when a licensed
instance starts again.

---

## 4. Log signature (what operators see)

At the start of an unlicensed spell (WARNING):

```
Running WITHOUT a valid license (status NotLoaded). The gateway will STOP in
approximately 01:59:30 unless a valid license is installed (ADR-0035).
```

When the window elapses (CRITICAL), immediately before shutdown:

```
No valid license (status NotLoaded). The 120-minute unlicensed runtime window has
elapsed; STOPPING the gateway per ADR-0035. Install a valid license and restart the
service to resume.
Application is shutting down...
```

---

## 5. Configuration

| Setting | Default | Notes |
|---------|---------|-------|
| Trial window | **120 minutes** | Override with env var `EDGECONNECT_LICENSE_TRIAL_MINUTES`. |
| Check cadence | 30 seconds | Not externally configurable. |

`EDGECONNECT_LICENSE_TRIAL_MINUTES` accepts a positive number (fractional allowed,
e.g. `0.1` = 6 s for testing). Unparseable or non-positive values fall back to the
2-hour default. For a machine-wide Windows service, set it at machine scope and
restart the service:

```powershell
setx EDGECONNECT_LICENSE_TRIAL_MINUTES 120 /M
Restart-Service ElpisEdgeConnect
```

---

## 6. How to recover / make it run indefinitely

Install a valid license and restart — there is no hot reload:

1. Place the signed license at `C:\ProgramData\EdgeConnect\license.json` (or set
   `EDGECONNECT_LICENSE_PATH`). See `docs/licensing/licensing-complete-guide.md`
   for issuing one with `tools/LicenseGen`.
2. `Restart-Service ElpisEdgeConnect`.
3. On startup the status becomes `Valid`, the enforcer stays dormant, and the
   gateway runs without the cutoff.

---

## 7. Implementation

- **`LicenseTrialEnforcer : BackgroundService`** (`src/ElpisEdgeConnect.Host/`).
  - Pure decision function `ShouldStop(status, nowUtc, ref unlicensedSinceUtc,
    trialDuration)` — deterministic, unit-tested without timers.
  - `ExecuteAsync` loop: `_license.Tick()` → evaluate → on stop, log CRITICAL and
    call `StopApplication()`; otherwise warn/heartbeat and `Task.Delay(cadence)`.
  - Clock and durations are injectable for tests.
- **Registration:** `CompositionRoot.AddElpisEdgeConnectHost` registers it as a
  hosted service, so it applies to **both** the headless Host and the
  Management/Studio service (both build the same composition).
- **Design note:** the cutoff lives in the **Host** layer as an outer supervisor.
  Core stays protocol-agnostic and the pipeline code itself contains **no** license
  check — the stop is effected via the host lifetime, not inside the data path.

### Tests
`tests/ElpisEdgeConnect.Host.Tests/LicenseTrialEnforcerTests.cs` (15 cases):
valid-never-stops, first-observation timestamping, within-window, at/after-window
for every non-`Valid` status, valid-then-unlicensed window restart, and
`EDGECONNECT_LICENSE_TRIAL_MINUTES` parsing (default/override/invalid).

### Manual verification
Run unlicensed with a short window and watch it stop:

```bash
EDGECONNECT_DATA_ROOT=/tmp/ec-demo \
EDGECONNECT_ENDPOINTS_DISABLED=true \
EDGECONNECT_LICENSE_TRIAL_MINUTES=0.1 \
dotnet run --project src/ElpisEdgeConnect.Host
# -> WARNING "...will STOP in ~00:00:06...", then CRITICAL "...STOPPING the gateway...", then shutdown
```

---

## 8. Consequences & risk (from ADR-0035)

- Overrides locked decision #7: a gateway with **no / invalid / expired** license
  (including within the old grace period) stops after the window.
- **Accepted risk:** a previously-paying customer whose license lapses can have
  their gateway stop mid-operation. Mitigate operationally — expiry warnings are
  emitted at 30/7/1 days and on entering grace; monitor the service and renew
  before expiry.
- To protect expired-but-previously-valid licenses instead (the "trial only when
  never licensed" variant), change the `ShouldStop` predicate to treat
  `InGracePeriod`/`Expired` as non-cutoff — a one-line change plus an ADR update.

---

*See also: `docs/decisions/0035-unlicensed-runtime-cutoff.md`,
`docs/licensing/licensing-complete-guide.md`,
`docs/installer/creating-the-installer.md`.*
