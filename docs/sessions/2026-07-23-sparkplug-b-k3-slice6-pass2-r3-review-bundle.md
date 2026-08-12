# K3 Slice 6 pass 2 — Review Bundle r3 (disposal linearization)

**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master)
**Commit:** `ebd7278` — *fix(sparkplug): K3 slice 6 pass 2 review r3 - disposal linearization*
**Exact source diff:** `docs/sessions/2026-07-23-sparkplug-b-k3-slice6-pass2-r3-source-diff.md` (full `git show -W`, 2 files, 0 elision).
**Build:** SparkplugB src `0/0`; tests `0/0`. **538 tests pass**, broker-free and deterministic. All prior tests stay green.

Folds the single r2 re-review blocker (R3) and the three focused-evidence completions. No Core change.

---

## R3 — disposal ownership, guarding, and completion are one synchronized decision

**The defect (r2):** `_disposeTask` was installed the instant disposal won ownership, but the fail-closed guard still read a *separate* `_disposed` int that `DisposeCoreAsync` set only later (after taking the gate). Between the ownership win and that write there was a window where a `Begin`/`Initialize` already queued on the gate could acquire it, observe `_disposed == 0`, and proceed — resurrecting a disposing actor.

**The fix:** collapse ownership, guarding, and shared completion onto the **single** marker `_disposeTask`:

- `DisposeAsync` installs `_disposeTask` via `Interlocked.CompareExchange(ref _disposeTask, mine.Task, null)` **before** `DisposeCoreAsync` takes the gate. The `_disposed` int is **deleted**.
- `DisposalWon => Volatile.Read(ref _disposeTask) is not null` is the one predicate every surface reads. `ThrowIfDisposed()` now derives from it.
- Because the marker is set at the ownership win (before the gate), any call that acquires the gate *after* disposal won reads `DisposalWon == true` and fails closed — the window is gone. Ownership and guarding can no longer disagree.

**Missing guards added (r2):**
- **`InitializeAsync`** — `ThrowIfDisposed()` after the gate is acquired (previously ungated; a queued Initialize could re-initialize a disposed actor).
- **`EndSessionAsync`** — an End that **loses** to disposal is now an explicit no-op (`if (DisposalWon) return;`) at the top of the gated block: it must **not** publish an NDEATH or a clean DISCONNECT. Disposal owns the abort-retirement (the broker publishes the Will).
- `StopAsync`'s existing guard was rewritten to the same `DisposalWon` predicate.

**Shared completion (unchanged, re-verified):** concurrent `DisposeAsync` callers whose CAS fails return the *installed* task; that task completes only in `DisposeCoreAsync`'s `finally`, after retirement — so caller B never completes before caller A's retirement finishes.

### Required deterministic tests (all added, all green)

| Test | Proves |
|------|--------|
| `LifecycleCall_AfterDispose_FailsClosed_NoStateMutation` (Theory **extended** to `initialize`/`start`/`begin`/`rebirth`/`publish`/`cutover`) | every surface throws `ObjectDisposedException` after disposal, terminal `Stopped/Stopped` stands, no new birth/transport |
| `LifecycleCall_QueuedBehindDisposal_FailsClosed_NoResurrection` | a `Begin` **and** an `Initialize` queued on the gate *while disposal holds it* (transport retirement blocked on a TCS) both fail closed on release, and the queued Begin opens **no** new transport (factory count unchanged) |
| `Dispose_ConcurrentCaller_DoesNotCompleteBeforeRetirementReleased` | caller B (`IsCompleted == false`) does not complete until caller A's blocked retirement is released; the transport is retired exactly once |
| `EndSession_LosingToDisposal_EmitsNoDeathOrDisconnect` | an End after disposal wins emits no NDEATH and no clean DISCONNECT |
| `Dispose_LeavesCoherentTerminalStoppedState` (existing) | completed disposal reads `Stopped/Stopped` with no further mutation |

The queued-behind-disposal proof is exact: the fake transport's `DisposeAsync` now blocks on an injected `DisposeGate` TCS, so disposal provably **holds the gate** while `Begin`/`Initialize` are queued; releasing the gate then drives them through the post-gate `DisposalWon` guard.

---

## Focused evidence completions

| Test | Proves |
|------|--------|
| `Rebirth_Recovery_StoreFailureDuringPrepare_FailsOnce_NoBackoff_NoNewTransport` | an identity-store failure (`IDENTITY_STORE_UNAVAILABLE`) in `PrepareBirth` fails **once** — zero backoff delays, **no** new transport opened (factory count unchanged), actor `Failed` |
| `Rebirth_Recovery_GenerationExhausted_FailsOnce_NoBackoff_NoBdSeqReserved` | with the generation counter at `long.MaxValue`, recovery fails with `GENERATION_OVERFLOW` — zero backoff, and **no** bdSeq is reserved (the overflow check precedes `ReserveNextBdSeq`), actor `Failed` |
| `Rebirth_Recovery_AbortedByEndDuringBackoff_EndsCleanly_ReadyNoSession` | an authoritative `End` arriving while recovery is parked in gate-released backoff takes the gate, nulls the recovery token; recovery reacquires, sees the invalid token, and aborts with `OperationCanceledException`; the actor is left **ready-no-session** (`Running` / protocol `Stopped`, no session) |

Store-failure and generation-exhaustion use `ScriptableStore`, a thin decorator over the real durable store that counts `ReserveNextBdSeq` calls and can force a fail-closed `ResolveAliases`. The End-during-backoff test mirrors the already-approved Stop variant (`Rebirth_Recovery_AbortedByStopDuringBackoff`) with the End teardown path and the ready-no-session assertion.

---

## Surfaced design decision (needs your call, non-blocking)

The generation-exhaustion test seeds the private `_lastIssuedConnectionGeneration` to `long.MaxValue` via **test-only reflection** (`SeedGeneration`), so the deterministic overflow branch can be exercised without adding a production mutation seam. There is already a symmetric read-only test seam (`internal long LastIssuedGeneration`). If you'd prefer an explicit `internal` setter over reflection for symmetry and rename-safety, say so and I'll switch it — it's a one-line change and touches no behavior. I chose reflection to keep the r3 production diff limited to the disposal-linearization work.

---

## Slice 6 status

Pass 1 approved; pass 2 folded across r0 → r1 → r2 → r3. The K3 session actor's lifecycle is now fully hardened and **terminal-safe**: disposal ownership, fail-closed guarding, and shared completion are a single synchronized decision on one marker, with every lifecycle surface (Initialize/Start/Begin/Rebirth/Publish/CompleteCatchUp/Stop/End) proven to fail closed or no-op against a disposing/disposed actor — and the bounded, gate-released, single-owner recovery is proven fatal-once (store failure, generation exhaustion) and cleanly abortable by an authoritative End. No Core change. The exact `git show -W` diff (2 files) is attached for line-level sign-off.
