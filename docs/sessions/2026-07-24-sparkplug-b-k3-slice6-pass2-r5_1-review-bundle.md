# K3 Slice 6 pass 2 — Review Bundle r5.1 (in-attempt allocation guard — proactive)

**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master)
**Commit:** `7afae35` — *fix(sparkplug): K3 slice 6 pass 2 r5.1 - guard the in-attempt allocation window (proactive)*
**Exact source diff:** `docs/sessions/2026-07-24-sparkplug-b-k3-slice6-pass2-r5_1-source-diff.md` (full `git show -W`, 2 files, 0 elision).
**Build:** SparkplugB src `0/0`; tests `0/0`. **541 tests pass**, broker-free and deterministic.

> **This is NOT a reviewer blocker.** It is a proactive self-audit follow-on to r5, landed because the disposal↔recovery race was fixed at successively earlier await points across r3 → r4 → r5, and the same audit found one more window. It is a separate commit on top of r5 (`3513cdc`) so you can sign off the r5 blocker independently.

---

## What r5.1 closes

`AttemptConnectionAsync` allocates the durable **bdSeq**, the **generation**, and the **transport** synchronously at its top, *before* the first await (`ReserveNextBdSeq` → generation increment → `_transportFactory()` → `ConnectAsync`). The caller checks recovery ownership immediately before invoking it, but that check and these allocations are not one atomic step — so a concurrent disposal winning in between could still allocate.

**Fix:** re-check ownership as the first statement of `AttemptConnectionAsync`, immediately before the allocations. The check reuses the path-appropriate helper so each caller's non-faulting convention is preserved:

- **Begin** (passes `recoveryToken: null`) → `ThrowIfDisposed()` → `ObjectDisposedException`, which Begin's `catch (ObjectDisposedException)` passes through without faulting. This *newly* protects Begin against a disposal that wins during birth preparation (`PrepareBirth`/`ResolveAliases`), which previously had no re-check between Begin's entry guard and the first allocation.
- **Recovery** (passes its `token`) → `ValidateRecoveryOwnership(token)` → `OperationCanceledException`, consistent with the rest of the recovery loop.

An optional `recoveryToken` is threaded through `AttemptConnectionAsync` (null for Begin via `EstablishNewConnectionAsync`, the token for `SuspectRebirthAsync`).

## Test (deterministic, added)

`Begin_DisposalWinsDuringBirthPrep_FailsClosed_BeforeBdSeqOrTransport` parks Begin **inside** `PrepareBirth`'s alias resolution (via a new synchronous `ResolveGate`/`ResolveEntered` seam on `ScriptableStore`), while Begin holds the gate; disposal then wins ownership (installs the marker) and blocks on the gate; releasing prep drives Begin into the in-attempt guard. Asserts: `ObjectDisposedException`, `ReserveCalls == 0` (no bdSeq), `factoryCalls == 0` (no transport), `LastIssuedGeneration == 0` (no generation), terminal `Stopped/Stopped`, nothing promoted.

The Begin path is the one the guard is deterministically reachable on (its birth-prep is an injectable synchronous seam); on the recovery path the guard is defense-in-depth, redundant with the loop's pre-attempt `ValidateRecoveryOwnership` in every await-injectable ordering but kept for symmetry.

---

## Approved ownership contract (r5.1 design ruling — do NOT add an r5.2 lock)

Per the r5 review's design ruling, r5.1 is **defense-in-depth narrowing, not a hard no-gap linearization guarantee**, and that is the correct boundary. The frozen contract, used verbatim in the code comment:

> Disposal prevents admission of any new establishment attempt. An attempt already admitted under the actor gate may finish or abort; any committed-but-unused `bdSeq` and generation gap are intentional, monotonic and never reused, and disposal retires any resulting transport before completing.

Why this is the right boundary (and a lock is explicitly declined):

1. Once recovery validates ownership immediately before entering `AttemptConnectionAsync` **while holding the actor gate**, that complete attempt is already in flight. Disposal may install its terminal marker concurrently, but it **cannot run the gated retirement concurrently** — it waits for the attempt to yield or finish, then retires any resulting transport and publishes terminal `Stopped/Stopped`. The frozen recovery rule is that invalidation prevents a *new competing attempt*; it does not require interrupting synchronous instructions or partially unwinding an admitted attempt.
2. A reserved-but-unused `bdSeq` is an **explicitly supported store outcome** — reservations commit before CONNECT, are strictly monotonic, and an unused committed value is skipped rather than reused. The existing Begin evidence already accepts the same outcome when NBIRTH fails after reservation.

A lock across reservation / generation / transport creation would add a synchronization domain to the establishment hot path, complicate both Begin and recovery, still not safely interrupt CONNECT/SUBSCRIBE/NBIRTH once entered, and provide no stronger externally observable guarantee after `DisposeAsync` completes. **Declined by design ruling; not implemented.**

---

## Slice 6 status

Pass 1 approved; pass 2 folded r0 → r1 → r2 → r3 → r4 → **r5 (approved & locked)**, with r5.1 as a proactive hardening on top. The r5 blocker (pre-token window) stands at `3513cdc`. r5.1 narrows the last synchronous allocation window and hardens Begin against disposal-during-birth-prep; its ownership contract is the approved boundary above (no r5.2 lock). No Core change. The exact `git show -W` diff is attached for line-level sign-off.
