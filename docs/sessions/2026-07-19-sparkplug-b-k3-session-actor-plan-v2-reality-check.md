# Sparkplug B — K3 Session Actor Plan v2 Reality-Check

**Date:** 2026-07-19
**Review target:** `2026-07-19-sparkplug-b-k3-session-actor-plan-v2.md`
**Verdict:** **CONDITIONAL GO to v3, with three targeted corrections before freeze.**

> Verbatim record of the external (ChatGPT) reality-check pass over K3 plan v2, preserved
> for the plan trail. Its F1–F3 + non-blocking corrections are folded into
> `…-plan-v3.md` (FROZEN).

The v2 fold is faithful and substantially resolves the v1 blockers. The Core/cursor
ownership model is now coherent, the cutover-suspect reality-check is convincing, the
actor concurrency model is selected, first-observed growth is correctly separated from
material schema mutation, and the gateway identity-store boundary is executable. The
decision to keep bounded recovery inside the Core-driven suspect `RebirthAsync` while
surfacing sustained-outage handling as a named Core follow-up is appropriate for K3.

I agree that the sustained-outage Core gap is not a K3 blocker, provided it remains
explicit in the handoff and is not represented as operational parity with the legacy MQTT
sink.

Three items still need tightening before calling v3 frozen.

## F1 — Define the complete bounded recovery attempt

The plan currently says "bounded reconnect-with-backoff," but it does not unambiguously
define whether the retry unit is only MQTT CONNECT or the complete Sparkplug session
establishment.

It must be the complete atomic attempt:

```text
reserve and commit fresh bdSeq
→ construct fresh NDEATH Will
→ create new transport generation/client
→ CONNECT
→ SUBSCRIBE exact NCMD
→ publish NBIRTH
```

Any failure or uncertain completion at CONNECT, SUBSCRIBE, or NBIRTH must:

1. abandon that transport generation;
2. retain the consumed `bdSeq` as permanently used;
3. wait according to the bounded backoff policy;
4. begin the next attempt with another newly reserved `bdSeq`.

The candidate Core epoch, manifest, baseline, alias map, and `seq=0` state promote only
when NBIRTH succeeds.

This also resolves a textual contradiction:

- §4.5 currently says a suspect `RebirthAsync` CONNECT/SUBSCRIBE/NBIRTH failure throws.
- §10.2 says it retries within a bound.

Revise §4.5 to distinguish:

- healthy-transport rebirth NBIRTH failure: immediately fatal;
- transport-suspect recovery attempt failure: retry the full establishment attempt within
  the configured budget;
- budget exhausted: throw, candidate epoch remains unpromoted, route becomes terminal
  `Failed`.

Add acceptance evidence that failed attempts consume distinct monotonic `bdSeq` values and
that no failed attempt's client can later affect the successful replacement through delayed
callbacks.

## F2 — Freeze one retry-budget contract

"Bounded attempts/time" is still two possible policies rather than one implementation
contract. Freeze one model in v3.

Recommended contract:

- `TransportRecoveryMaxAttempts`, counting complete session-establishment attempts;
- `TransportRecoveryInitialDelay`;
- `TransportRecoveryMaxDelay`;
- exponential backoff capped at the maximum;
- no random jitter in K3, unless an injected jitter source is explicitly part of the
  design;
- cancellation terminates immediately;
- delay occurs without holding the actor's serialization gate;
- only one recovery operation exists per actor.

A suitable default posture is three attempts, but the exact default should be selected and
documented in v3 rather than left to implementation.

Releasing the gate during delay requires a recovery-operation token or substate so that
another lifecycle call cannot begin a competing transition. The actor should:

1. enter `RecoveringTransport` under the gate;
2. install the active recovery token/task;
3. release the gate during the delay;
4. reacquire and verify the same token before the next attempt;
5. allow cancellation/End/Stop to invalidate the token.

MQTT callbacks still only update atomic latches; they do not start another loop.

If the implementation instead holds the serialization gate throughout the bounded
operation, say so explicitly and prove that cancellation and shutdown cannot be blocked
beyond the configured recovery bound. The current text leaves this concurrency behavior
undecided.

## F3 — Pin identity comparison semantics

Section 5.7 says to pin the comparer and collation but does not actually choose them. That
remains an architectural decision because it affects durable alias identity and
duplicate-name behavior across restarts.

Recommended freeze:

- canonical identity components: ordinal, case-sensitive;
- published metric-name duplicate detection: ordinal, case-sensitive;
- SQLite canonical-key uniqueness: `BINARY` collation or an equivalent explicit binary
  representation;
- normalization is limited to the canonical transformations already governed by the
  source/tag contracts — no case folding, culture-sensitive comparison, whitespace
  trimming, or Unicode compatibility folding inside the store;
- the exact canonical-key encoding is versioned and unambiguous, not delimiter-
  concatenated without length framing or escaping.

Required tests:

- identities differing only by case receive distinct stable aliases;
- identical ordinal identities cannot receive two aliases;
- culture changes do not alter lookup;
- component values containing separators cannot collide;
- reopen preserves the same comparison behavior.

If ADR-0036 or K2 already mandates a different comparer, v3 should cite and adopt that
exact rule instead.

## Non-blocking corrections

### Pre-authoritative transport callbacks

Clarify callback behavior during initial Begin, before a successful NBIRTH has installed
an authoritative Core session/epoch:

- validate the transport-generation token;
- latch failure for the in-progress Begin;
- do not call `RequestRebirthAsync`, because no authoritative birth exists;
- let Begin fail through its normal fatal path.

The current idle-disconnect wording assumes an authoritative session exists.

### Exit-criteria wording

Section 12 says: *No change to `ElpisEdgeConnect.Core` or any existing project.* This
conflicts with §9's better wording allowing necessary solution, project, and
test-infrastructure metadata. Replace it with: *No Core API or behavior change. Changes are
confined to the Sparkplug B implementation/tests and necessary
solution/project/test-infrastructure metadata.*

### Core follow-up acceptance

Keep the Core follow-up post-K3, but K3 documentation and health diagnostics must state
the supported envelope honestly:

- short outages within the configured actor recovery budget can recover;
- sustained outages beyond that budget terminally fail the route;
- operator configuration re-apply is currently required after terminal failure.

Do not describe K3 as having full store-and-forward outage parity with the legacy sink
until the Core follow-up lands.

## Freeze decision

After F1–F3 are incorporated, I see no remaining architecture blocker to v3 freeze. The v3
delta should therefore be narrow:

1. define the full suspect-recovery attempt and reconcile §4.5 with §10.2;
2. freeze one bounded-backoff configuration and concurrency contract;
3. select the durable identity/name comparer and key encoding;
4. add the pre-authoritative callback and wording corrections.

No scope expansion into Core is required, and the seven-slice implementation sequence can
remain unchanged.
