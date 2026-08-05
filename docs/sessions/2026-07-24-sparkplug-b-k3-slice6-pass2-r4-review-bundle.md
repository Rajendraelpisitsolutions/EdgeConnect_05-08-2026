# K3 Slice 6 pass 2 — Review Bundle r4 (recovery-invalidation linearization)

**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master)
**Commit:** `13e5973` — *fix(sparkplug): K3 slice 6 pass 2 review r4 - linearize recovery invalidation with disposal*
**Exact source diff:** `docs/sessions/2026-07-24-sparkplug-b-k3-slice6-pass2-r4-source-diff.md` (full `git show -W`, 2 files, 0 elision).
**Build:** SparkplugB src `0/0`; tests `0/0`. **539 tests pass**, broker-free and deterministic. All prior tests stay green.

Folds the single r3 re-review blocker (the marker-vs-token interval). No Core change.

---

## The defect (r3 re-review)

The r3 redesign closed the *public-surface* resurrection window, but disposal ownership and recovery invalidation were not yet linearized **together**. `DisposeAsync` installed the terminal marker (`_disposeTask`) first, while `_activeRecoveryToken` was nulled only later inside `DisposeCoreAsync`. That left the interleaving:

```
recovery A parked in gate-released backoff
→ DisposeAsync installs _disposeTask (disposal has won)
→ Dispose thread pre-empted before _activeRecoveryToken = null
→ recovery delay completes → reacquires the gate → token still current
→ BackoffWithGateReleasedAsync succeeds → another full AttemptConnectionAsync
→ reserves another bdSeq, issues another generation, creates a transport
→ Dispose later nulls the token and retires the result
```

This violated the terminal contract that no new transport resource, generation, or durable bdSeq reservation begins after disposal has won.

## The fix — one ordering decision, plus a defense-in-depth re-check

**1. Invalidate recovery before the ownership CAS.** `DisposeAsync` now nulls `_activeRecoveryToken` **before** the `Interlocked.CompareExchange` that installs `_disposeTask`. Nulling the token and winning ownership are now a single ordering decision, so a recovery reacquiring the gate after disposal has won can never observe a still-current token. Every concurrent Dispose caller nulls the token (idempotent); any Dispose supersedes recovery. The redundant null was removed from `DisposeCoreAsync`.

**2. The recovery loop honors the disposal linearization point directly.** `BackoffWithGateReleasedAsync`, after reacquiring the gate, now aborts on `DisposalWon || !ReferenceEquals(_activeRecoveryToken, token)` (was: token-only). The retry loop therefore cannot enter another `AttemptConnectionAsync` once disposal has won — the internal loop reads the *same* marker the public surfaces do.

Together these make the two orderings equivalent: whether the recovery thread or the Dispose thread reaches the gate first, the recovery aborts with `OperationCanceledException` and no candidate is established. Because the abort is an OCE and the token is already null, the outer catch skips Suspect-normalization and disposal owns the terminal `Stopped/Stopped` state.

## Required deterministic test (added, green)

`Dispose_DuringRecoveryBackoff_SupersedesRecovery_NoNewAttempt` drives the exact ordering the reviewer specified:

```
recovery enters controlled backoff (injected delay TCS)
→ disposal wins ownership
→ disposal retirement is held on a controlled transport-dispose barrier (fake DisposeGate)
→ release recovery backoff
→ recovery aborts
```

Asserts, against the values captured the instant recovery parked in backoff:

- **no next transport is created** — `factoryCalls` unchanged;
- **no next generation is issued** — `LastIssuedGeneration` unchanged;
- **no additional bdSeq is reserved** — `ScriptableStore.ReserveCalls` unchanged;
- **recovery ends `OperationCanceledException`**;
- **disposal completes `Stopped/Stopped`**;
- **no candidate authority is promoted** — `HasSession == false`.

The barrier is exact: the fake transport's `DisposeAsync` blocks on an injected TCS, so disposal provably holds the gate mid-retirement while the recovery backoff is released, forcing the marker-vs-token interleaving deterministically.

---

## Accepted r3 work (unchanged, retained)

`_disposeTask` as the permanent disposal marker; post-gate guards on every mutating public surface; End losing to disposal emits no NDEATH or clean DISCONNECT; queued Begin/Initialize cannot resurrect the actor; shared Dispose completion and single retirement; terminal `Stopped/Stopped`; identity-store failure fails once with no attempt/backoff; generation exhaustion consumes no bdSeq; End during backoff leaves `Running/Stopped`, sessionless; test-only reflection for generation exhaustion.

---

## Slice 6 status

Pass 1 approved; pass 2 folded across r0 → r1 → r2 → r3 → r4. The disposal linearization is now complete on **both** planes — public lifecycle surfaces *and* the internal recovery loop invalidate against the same ownership decision, so no transport resource, generation, or durable bdSeq can begin after disposal has won, in either thread ordering. No Core change. The exact `git show -W` diff (2 files) is attached for line-level sign-off.
