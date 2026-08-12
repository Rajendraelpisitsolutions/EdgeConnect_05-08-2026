# M.2d.2 Step 12 — classifier-trust gap (follow-up)

**Date:** 2026-05-23
**Status:** Captured as a follow-up; not addressed in M.2d.2.
**Resolved-in-session decision:** Reframe Step 12's integration test to pin
what the architecture guarantees today, defer the classifier amendment.

---

## What v2 §5.6 expected

The M.2d.2 v2 plan §5.6 specified an integration test asserting that a
cosmetic-field-only Edit (changing only `DeviceName`) **does not** restart
the source — i.e., `ReloadOutcomeDto.RestartedInstances` would not include
the source's `InstanceId` post-apply.

The plan author reasoned:

> The wizard layer **does not need its own no-op-no-restart invariant**.
> It needs to **trust the classifier and prove it through the Edit path**...
> If the assertion ever fails, the bug is in `RuntimeReloadClassifier`,
> not the wizard — that's the right architectural placement.

## What the code actually guarantees today

Two locked architectural pieces collide with that expectation:

1. **`ConfigurationDiffer.AreStructurallyEqual`**
   ([src/ElpisEdgeConnect.Core/Configuration/ConfigurationDiffer.cs:56](../../src/ElpisEdgeConnect.Core/Configuration/ConfigurationDiffer.cs#L56))
   compares old vs new `SourceInstanceConfig` via full JSON-serialised
   equality. Any field change — including `DeviceName` — produces one
   `ConfigurationChange { Kind = Modified, ... }`.

2. **`RuntimeReloadClassifier`** (per ADR-0009 Decision 3, pinned in
   [RuntimeReloadClassifierTests.cs:87-101](../../tests/ElpisEdgeConnect.Core.Tests/Configuration/RuntimeReloadClassifierTests.cs#L87-L101))
   maps every `Modified` source → `Restart`. Comment in test:
   *"Locked per ADR-0009 Decision 3: Modified always resolves to Restart
   in v1. No in-place reconfigure path."*

Therefore: a DeviceName-only Edit save **does** restart the source today.
The §5.6 assertion would fail.

## What the reframed Step 12 test pins instead

`SourcesUpdateApiIntegrationTests.EditMode_DeviceNameChange_PreservesRoutesAndAppliesNewName_EndToEnd`
exercises the real `ConfigurationManager` + `FileSystemConfigurationStore`
through `SourcesUpdateApi.DispatchAsync` and asserts:

1. PUT round-trips through real draft → validate → apply pipeline (200 OK).
2. Post-apply `RouteConfig[]` is byte-identical to pre-apply (v2 §5.5
   route-preservation invariant — pinned end-to-end including
   disk-persistence round-trip).
3. Post-apply current config carries the new `DeviceName`.
4. Post-apply `CurrentVersionId` differs from the pre-apply `BaseVersionId`
   (the apply produced a fresh version).

Plus a `StaleBaseVersionId_Returns409_EvenAgainstRealManager` belt-and-braces
test that the unit-level fake's 409 behaviour matches the real manager's.

The "no-restart" property is **NOT** pinned by Step 12 — that's the gap.

## Why we're not closing the gap in M.2d.2

Closing the gap requires one of:

- **(a)** Amend `ConfigurationDiffer` to emit `Modified` only when
  non-cosmetic fields change. Requires defining the cosmetic-field set
  (`DeviceName`, `DeviceClass`, `DeviceId`?, anything else?) and pinning
  it via an ADR amendment. Touches Core; ripples to every other place
  that consumes `Modified` (audit summaries, history page diff display).
- **(b)** Amend `RuntimeReloadClassifier` to inspect the `Modified` payload
  field list and choose `Restart` vs no-op per field. Same ADR amendment;
  changes the contract `RuntimeReloadCoordinator` consumes.
- **(c)** Both, depending on which layer owns the cosmetic-bypass policy.

Either option is a Core/Host change with broader implications than the
Management-layer M.2d.2 milestone. It's out of M.2d.2 scope per the
2026-05-23 user verdict ("Reframe — pin what's actually true").

## Resolution path (for the next planning pass)

1. **Decide where the policy lives** — differ (option a) or classifier
   (option b). Option a is simpler but couples the diff to the runtime;
   option b keeps the diff "informational" and lets the classifier own
   the policy. Recommendation: option b — keep diff as ground-truth
   "what changed", let the classifier interpret.

2. **Define the cosmetic-field set per entity kind:**
   - `SourceInstanceConfig`: `DeviceName`, `DeviceClass` (TBD),
     `DeviceId` (TBD). NOT `Polling.IntervalMs`, NOT `Enabled`, NOT
     anything in `Connection` (protocol-specific connection details
     are not cosmetic).
   - `SinkInstanceConfig`: TBD per sink.
   - `RouteConfig`: TBD.

3. **Amend ADR-0009 Decision 3** to capture the cosmetic-bypass exception.

4. **Implement** the classifier change + revise the pinned tests in
   `RuntimeReloadClassifierTests` + add the v2 §5.6 assertion back into
   the Step 12 integration test as a positive pin.

5. **Re-derive** `RuntimeReloadCoordinator` + audit-trail summarisation
   if needed (cosmetic-only changes still produce an audit entry, just
   not a Restart action).

This is a self-contained 2-3 session effort. Not blocking M.2d.2 close-out.

## Cross-reference

- v1 plan: [`docs/sessions/2026-05-22-m2d2-steps-8-10-plan.md`](2026-05-22-m2d2-steps-8-10-plan.md)
- v2 plan: [`docs/sessions/2026-05-22-m2d2-steps-8-10-plan-v2.md`](2026-05-22-m2d2-steps-8-10-plan-v2.md)
- M.2d.2 v2 §5.6: [`docs/sessions/2026-05-22-m2d2-source-wizards-plan-v2.md`](2026-05-22-m2d2-source-wizards-plan-v2.md)
- ADR-0009 (the locked decision): `docs/decisions/0009-*.md`
- Integration test landing the reframed assertion:
  [`tests/ElpisEdgeConnect.Management.Tests/SourcesUpdateApiIntegrationTests.cs`](../../tests/ElpisEdgeConnect.Management.Tests/SourcesUpdateApiIntegrationTests.cs)
