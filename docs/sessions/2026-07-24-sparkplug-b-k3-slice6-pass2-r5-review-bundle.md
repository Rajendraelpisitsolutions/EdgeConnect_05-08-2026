# K3 Slice 6 pass 2 — Review Bundle r5 (early atomic recovery claim)

**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master)
**Commit:** `3513cdc` — *fix(sparkplug): K3 slice 6 pass 2 review r5 - claim recovery ownership before the first await*
**Exact source diff:** `docs/sessions/2026-07-24-sparkplug-b-k3-slice6-pass2-r5-source-diff.md` (full `git show -W`, 2 files, 0 elision).
**Build:** SparkplugB src `0/0`; tests `0/0`. **540 tests pass**, broker-free and deterministic. All prior tests stay green.

Folds the single r4 re-review blocker (the pre-token window). No Core change.

---

## The defect (r4 re-review)

`SuspectRebirthAsync` published recovery ownership (`_activeRecoveryToken = token`) only **after** two awaits: the initial suspect-transport retirement (`await previous.Transport.DisposeAsync()`) and non-retryable birth preparation. Between entry and that assignment the token was still null, so a disposal that won during the initial retirement was not observed — the recovering call could resume, install a fresh token, reserve another bdSeq, issue a generation, and open a transport after disposal had already won. The r4 test only covered disposal during backoff (token already exists), not this earlier window.

## The fix — claim ownership atomically before the first await, validate after every await

**1. Atomic early claim.** The token is now created and installed as the **very first statement**, before any await:

```csharp
var token = new object();
if (Interlocked.CompareExchange(ref _activeRecoveryToken, token, null) is not null)
    throw new OperationCanceledException("a transport recovery is already in flight ...");
```

The CAS does double duty: it establishes ownership before the initial retirement/prep, **and** enforces the single-recovery invariant (a second recovery fails the CAS and is rejected nonfatally with `OperationCanceledException`, so `RebirthAsync`'s OCE passthrough does not `SetFaulted` — same semantics as before, now race-free).

**2. One ownership check, everywhere.** A single helper is the sole validation:

```csharp
private void ValidateRecoveryOwnership(object token)
{
    if (DisposalWon || !ReferenceEquals(_activeRecoveryToken, token))
        throw new OperationCanceledException("the transport recovery was superseded by disposal or another lifecycle call.");
}
```

It is called **before the first await**, **after the initial suspect-transport retirement** (before `PrepareBirth`), **before each connection attempt**, and **inside `BackoffWithGateReleasedAsync`** (now routed through the same helper). So no transport / generation / durable bdSeq work proceeds once ownership is lost, at any await boundary.

**3. Symmetric release.** The `finally` now clears the token with `Interlocked.CompareExchange(ref _activeRecoveryToken, null, token)` — it clears **only if still ours**, never stomping a superseding owner.

Because ownership exists before the first await and `DisposalWon` is checked after every await, the two orderings converge: whether disposal wins before the CAS (the CAS still succeeds, but the immediate `ValidateRecoveryOwnership` sees `DisposalWon` and aborts) or after (disposal's unconditional token-null makes the token check fail), the recovery aborts with `OperationCanceledException` and establishes nothing.

## Required deterministic test (added, green)

`Dispose_DuringInitialSuspectRetirement_SupersedesRecovery_BeforePrepareOrAttempt` drives the exact pre-attempt ordering the reviewer specified:

```
suspect recovery blocks in the initial previous-transport DisposeAsync
→ DisposeAsync wins ownership
→ release the previous-transport disposal
→ recovery aborts before PrepareBirth or the first connection attempt
```

Asserts, against the baseline captured right after birth:

- **no new transport** — `factoryCalls` unchanged;
- **no new generation** — `LastIssuedGeneration` unchanged;
- **no bdSeq reserved** — `ScriptableStore.ReserveCalls` unchanged;
- **aborted before PrepareBirth** — `ScriptableStore.ResolveCalls` unchanged (no alias resolution);
- **recovery ends `OperationCanceledException`**;
- **disposal completes `Stopped/Stopped`**;
- **no candidate authority promoted** — `HasSession == false`.

The fake transport's `DisposeAsync` now signals a `DisposeEntered` TCS before blocking on its `DisposeGate`, so the test provably parks recovery *inside* the initial retirement (holding the gate) before disposal wins — forcing the pre-token interleaving deterministically. `ScriptableStore` gained a `ResolveCalls` counter to prove `PrepareBirth` was never entered.

---

## Accepted r1–r4 work (unchanged, retained)

`_disposeTask` as the permanent disposal marker; nulling the recovery token before the ownership CAS (r4); post-gate guards on every mutating public surface; End losing to disposal emits no NDEATH or clean DISCONNECT; queued Begin/Initialize cannot resurrect the actor; shared Dispose completion and single retirement; terminal `Stopped/Stopped`; retry-vs-fatal classification; NDEATH-gated clean DISCONNECT; authoritative End; identity-store failure fails once with no attempt/backoff; generation exhaustion consumes no bdSeq; End during backoff leaves `Running/Stopped`, sessionless; the r4 during-backoff disposal-supersession test.

---

## Slice 6 status

Pass 1 approved; pass 2 folded across r0 → r1 → r2 → r3 → r4 → r5. Recovery ownership is now claimed atomically before the first await and revalidated (`DisposalWon || token-mismatch`) after every await boundary through a single helper, so a disposal winning at **any** point of the recovery — the initial suspect retirement, birth preparation, a connection attempt, or backoff — aborts the recovery before it can begin any new transport, generation or durable bdSeq work, in every thread ordering. No Core change. The exact `git show -W` diff (2 files) is attached for line-level sign-off.
