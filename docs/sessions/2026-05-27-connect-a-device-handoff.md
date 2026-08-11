# Connect-a-device — implementation handoff

**Status:** Implementation complete. Awaiting user smoke-pass + PR merge.
**Date:** 2026-05-27
**Branch:** `claude/connect-a-device-impl`
**Plan trail:**
- v1: [`2026-05-27-connect-a-device-plan-v1.md`](./2026-05-27-connect-a-device-plan-v1.md)
- v2: [`2026-05-27-connect-a-device-plan-v2.md`](./2026-05-27-connect-a-device-plan-v2.md)
- v2.1 (locked): [`2026-05-27-connect-a-device-plan-v2.1.md`](./2026-05-27-connect-a-device-plan-v2.1.md)
**ADR:** [`ADR-0016 — Onboarding meta-wizard`](../decisions/0016-onboarding-meta-wizard.md)
**Step 0 reality-check:** [`2026-05-27-connect-a-device-step0-reality-check.md`](./2026-05-27-connect-a-device-step0-reality-check.md)

---

## 1. What shipped

A new guided flow at `/onboard` that composes the existing source / destination / route wizards into one ceremony with a single atomic apply. The 3-wizards × 3-Applies × 4-page-jumps friction that triggered this milestone is gone.

### 1.1 New runtime behaviour (visible to operators)

| Surface | What changed |
|---------|--------------|
| Studio first launch with no `current.json` | **No longer crashes.** ConfigurationManager auto-provisions a hostname-derived seed config (ADR-0016 R5) and emits an `AutoProvisioned` audit entry. |
| `/onboard` | New page — 7-step guided flow (Welcome → Source picker → Destination picker → Configure source → Configure destination → Configure route → Review & connect). |
| Top nav | New **Connect a device** button (Primary, filled) between Overview and Sources — always visible. |
| Empty-state CTAs | Overview, Sources, Destinations, Routes pages now surface `[Connect a device]` as the primary CTA when their list is empty; existing `Add ... only` button stays as secondary. |
| Bundled apply endpoint | `POST /api/v1/onboarding/apply` — atomic source + sink + route creation. |

### 1.2 New developer-visible surfaces

| Type | What |
|------|------|
| ADR | [ADR-0016 — Onboarding meta-wizard for first-run + multi-entity authoring flows](../decisions/0016-onboarding-meta-wizard.md). 6 rules across structure / contracts / runtime / lifecycle. Extends ADR-0015 wizard contract. |
| Audit action | `ConfigurationAuditAction.AutoProvisioned = 5` — captures the first-run self-provision event. |
| Embedded-mode contract | Every protocol wizard (5) + both protocol pickers (2) accept `[Parameter] bool EmbeddedMode` + `[Parameter] EventCallback<TInstance?> OnInstanceBuilt`. Default `false` means standalone behaviour is unchanged. |
| Merger method | `WizardConfigMerger.BuildBundledOnboardingDraft(current, source, sink, route, ...)`. |
| API endpoint | `OnboardingApi.cs` — `POST /api/v1/onboarding/apply`. |
| Components | `OnboardingFlow.razor`, `OnboardingProgress.razor`, `OnboardingNavigation.razor`, `WelcomeStep.razor`, `ReviewAndConnect.razor`. |

---

## 2. Step-by-step shipment record

| # | Step (per v2.1) | Commit | Notes |
|---|------|--------|-------|
| 0 | Reality-check pass | (none — doc only) | All 5 wizards passed EmbeddedMode feasibility check |
| 1 | Branch + v2.1 plan | `8bd670a` (same commit as #2) | |
| 2 | ADR-0016 + first-run self-provisioning | `8bd670a` | ConfigurationManager.InitializeAsync auto-provisions seed |
| 3 | (folded into #2) | — | First-run handling integrated with ADR work |
| 4 | EmbeddedMode mechanic | `3488862` | 5 protocol wizards + 2 pickers; standalone behaviour bit-identical |
| 5 | OnboardingFlow skeleton + chrome | `21fd2c1` *(approx — see git log)* + fix `3c0b9bf` | Visibility-toggle pattern (N1) per ADR-0016 R2 |
| 6 | Wire embedded source/dest/route | `0774ab0` + fix `3032a28` | Protocol-key alignment fix during smoke ("modbus" not "modbustcp") |
| 7 | Bundled apply (headline deliverable) | `8c5d2e3` *(approx)* | BuildBundledOnboardingDraft + OnboardingApi + ReviewAndConnect |
| 8 | Empty-state CTAs + nav | `fa6e0f8` + fix `9e59f33` + fix `87be4d6` | Race-window guard + WelcomeStep UserAttributes conflict fix |
| 9 | Smoke + handoff + PR | (this doc) | |

(See `git log claude/connect-a-device-impl --oneline` for the canonical sequence — the table above lists representative commits, not every fix-up.)

---

## 3. Tests

| Suite | Baseline (start) | After Connect-a-device |
|-------|------------------|------------------------|
| Core | 887 | **903** (+16 — first-run self-provisioning) |
| Management | 678 | **695** (+17 — merger + API tests for bundled apply) |
| Solution total | ~2,525 | **2,542** passing, 1 skipped (pre-existing flaky MQTT reconnect) |

Test files added:
- `tests/.../Configuration/ConfigurationManagerTests.cs` — 5 new facts + 11 theory rows (hostname slug edge cases) for auto-provisioning
- `tests/.../WizardConfigMergerBundledOnboardingTests.cs` — 11 facts (happy path + uniqueness violations + identity override + multi-sink)
- `tests/.../OnboardingApiTests.cs` — 6 facts (happy path + 400 cases + 200 cases)

---

## 4. Known deferrals (followed up via chips / future milestones)

### 4.1 Auto-probe in onboarding flow (Q4 partial)

v2.1 Q4 verdict said "auto-run Test Connection in steps 3 and 4 (non-blocking Warning on failure)." The current implementation embeds the wizards with their probe buttons intact, but does NOT trigger them automatically. The operator still clicks Test Connection manually if they want.

Adding auto-probe requires a `[Parameter] AutoProbeOnFirstValid` flag on four wizards (Focas2 / Brother / Modbus / MQTT) plus a small piece of state to track whether the probe has been auto-fired once for the current model. Not blocking; deferred to keep this PR scope-disciplined.

### 4.2 StatusFooter poll-during-nav race

`Components/Layout/StatusFooter.razor` has the same `PeriodicTimer` + `InvokeAsync(StateHasChanged)` pattern as Overview's poll loop. Step 8 fixed Overview's race (commit `9e59f33`); StatusFooter was left alone because it's in the layout (doesn't unmount on page navigation) so the race window is much narrower. If the same `ObjectDisposedException` ever shows up against StatusFooter, apply the same cancellation-token guard pattern.

### 4.3 Modbus per-row tag-cell anchors

Carryover from M.2d.4 (documented in the cross-wizard audit checklist). The Modbus tag table's per-cell errors aggregate into the banner with `FieldAnchor=null`. Clicking them is inert — they're visible but not clickable-to-scroll. Adding row-cell anchors requires a stable DOM-id contract for nested tables; tracked as a M.2d.4 follow-up.

### 4.4 QA package launcher still references seed/current.json

`publish/edgeconnect-qa-2026-05-27/start-studio.cmd` was updated on the filesystem (no longer copies the seed) but `publish/` is gitignored. The `seed/current.json` file still ships in the package zip until the next rebuild. The fallback isn't harmful — if QA's binaries are the new self-provisioning version, the seed is ignored.

---

## 5. Smoke checklist for the user-driven verification pass

After this PR merges, exercise these paths to confirm nothing regresses:

### 5.1 First-run path (validates ADR-0016 R5)

1. Stop Studio
2. Delete `C:\ProgramData\EdgeConnect\` entirely (or set `EDGECONNECT_DATA_ROOT` to a fresh path)
3. Start Studio — it should NOT crash. Look for the `[config] current.json not found at startup; auto-provisioned empty seed` line in stderr.
4. Studio loads; Overview shows the **Get started** empty-state CTA.
5. Click `[Connect a device]` → land on `/onboard` Step 0 (Welcome — auto-provisioned identity matches the pattern)

### 5.2 Custom-identity path (validates Welcome skip)

1. From a config with a custom GatewayId (e.g. `qa-gateway`, not matching `^gw-[a-z0-9._-]+$`)
2. Visit `/onboard` — should land on Step 1 (Source picker) directly, Welcome bypassed
3. Progress indicator shows step 0 as complete

### 5.3 Full bundled apply (the headline)

1. `/onboard` → Modbus TCP → fill (host 127.0.0.1, port 5020, unit id 1, at least one tag like `spindle_rpm`) → Next
2. → MQTT → fill (broker 127.0.0.1, port 1883, anonymous, default per-tag topic) → Next
3. → Route is pre-populated; click Next
4. Review screen lists 3 entities + (optionally) identity change → **[Connect]**
5. Success panel appears with "See live data" link → navigates to `/sources/{id}`
6. `mosquitto_sub -h localhost -t "eremos/+/cnc/+/+" -v` should be receiving messages within 2 seconds

### 5.4 Standalone-wizard regression (the EmbeddedMode invisibility check)

Pick one wizard you used before this PR, e.g. `/sources/new/modbus`:
1. Open it directly (NOT through `/onboard`)
2. Confirm the wizard renders identically — footer with Cancel + Test Connection + Save buttons present, snackbar messages appear on save, navigation works
3. Save as draft, apply via /config — exactly as before

### 5.5 Cancel + Back protocol-change confirms

1. `/onboard` → pick Modbus → fill enough to build → reach Step 4
2. Click Back to Step 3 — confirm dialog should NOT appear (going back without changing protocol is allowed)
3. Click Back again to Step 1 — confirm dialog SHOULD appear because the source wizard is built. Decline → stay on Step 3.
4. Click Cancel from any step — "Exit setup?" dialog → confirm → returns to Overview.

---

## 6. Cross-references

- ADR-0014: Config state vs runtime state
- ADR-0015: Wizard contract (the spec this meta-wizard extends)
- ADR-0016: Onboarding meta-wizard (this milestone's locking decision)
- M.2d.4 merge commit (precondition): `b813410` on master
- Follow-up chips doc: [`2026-05-27-followup-chips.md`](./2026-05-27-followup-chips.md)

---

## 7. Effort

v2.1 estimate: 5–6 days realistic. Actual: ~1 day of focused implementation (Step 0 came in under budget; the embedding work in Step 4 was largely mechanical; the bundled apply in Step 7 reused existing primitives heavily).

The reality-check pass (Step 0) was the most valuable part of the plan-trail — it converted what could have been a structural-refactor surprise into a mechanical edit.
