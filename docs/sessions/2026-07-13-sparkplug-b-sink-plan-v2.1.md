# Sparkplug B Sink — Plan (v2.1) — Spike Charter

**Date:** 2026-07-13
**Author:** Session with Sudhakar
**Status:** **Approved as the spike charter** (2nd conformance review). NOT yet an
implementation plan — the spike converts it into one (v2.2/impl).
**Trail:** v1 → review → v2 → **2nd review** (approved v2 as spike charter, 6
tightening items) → **v2.1 (this)**.
**ADRs (committed, corrected — doc-control verified this session):**
`0035-sparkplug-b-northbound-standard.md` (amended),
`0036-sparkplug-replay-then-rebirth.md` (replaced; file slug is legacy, content is
v2 — QoS-0, birth-then-replay, node-only).

> **Doc-control note (review item 6):** the 2nd review saw *stale* ADR attachments
> (old QoS-1 / DBIRTH text). The committed on-disk ADRs are the corrected versions —
> verified by grep this session (0036 "QoS 1" appears only where the premise is
> *withdrawn* + the legitimate NCMD QoS-1 subscription). Implementation review must
> use the committed ADRs, not the review's attachments.

---

## 0. Verdict absorbed

v2 approved as the spike charter; scope increase judged **protocol-minimum, not
feature bloat**. Six tightening items (below) folded into the ADRs and this charter.
The static wizard mockup may proceed in parallel but stays **non-wired** until the
spike locks data contracts and route cardinality.

## 1. The six tightening items — disposition

| # | Item | Disposition |
|---|------|-------------|
| 1 | Delivery: make #12 change formal + a typed capability + **route-level rejection** | ADR-0036 Rule 1 now defines `DeliveryCapabilities{SupportsStoreAndForward, AcknowledgementBoundary=LocalTransport, SupportsBrokerAcknowledgedAtLeastOnce=false}`; **route validation rejects** broker-acked AtLeastOnce bound to a Sparkplug dest (typed error, not a warning). CLAUDE.md #12 amendment → **§6, needs your go-ahead** (governance edit). |
| 2 | Lock routes-per-Edge-Node | ADR-0036 Rule 7: **v1 = one route per Edge Node**; descriptor reuse rejected. Multi-route aggregation is a later explicit capability. |
| 3 | Fix node-level naming default | ADR-0036 Rule 5 + ADR-0035 Rule 4: default is **source-qualified** `{SourceInstanceId}/{DeviceId?}/{relative TagPath}` (bare TagPath collides across sources in one NBIRTH namespace); reserved names `bdSeq`/`Node Control/Rebirth` validated; alias key = canonical identity; **duplicate published names fail validation before CONNECT**. |
| 4 | Exact replay→live cutover | Working algorithm pinned in §3; the **spike must select and prove** it (incl. Rebirth-during-replay). |
| 5 | Add omitted protocol + Test-Connection assertions | Test gates §4; Test-Connection safety now an ADR-0036 forbidden pattern. |
| 6 | ADR sync / doc-control | Closed — committed ADRs verified corrected (see doc-control note). |

## 2. Delivery capability (locked, item 1)

`PublishResult.Success` = local MQTTnet send completed, no observable error — **not**
broker acceptance. Exposed as a typed capability so enforcement is structural:

```
DeliveryCapabilities(
    SupportsStoreAndForward               = true,
    AcknowledgementBoundary               = LocalTransport,   // None|LocalTransport|Broker|Application
    SupportsBrokerAcknowledgedAtLeastOnce = false)
```

**Route validation rejects** an explicit broker-acked `AtLeastOnce` bound to a
Sparkplug destination (the delivery mode lives at the route, so a wizard warning is
insufficient). QoS-0 ambiguity window is documented, not hidden.

## 3. Replay → live cutover (working algorithm; spike must confirm — item 4)

```
1. Capture replay high-water mark H.
2. Load complete manifest + latest-value snapshot (incl. metrics not recently changed).
3. Publish NBIRTH from that snapshot (seq 0, Node Control/Rebirth=false, no alias on it).
4. Publish buffered points ≤ H as NDATA with:
       is_historical   = true
       metric.timestamp  = acquisition (UTC)
       payload.timestamp = publication (UTC)
5. Hold points arriving after H during the drain.
6. Drain held delayed points as is_historical=true.
7. Publish ONE final non-historical latest-value update per metric whose value
   changed since the birth snapshot (so the host's current value never steps
   backward through the backlog).
8. Cross the route barrier → Live.
```
**Rebirth NCMD mid-replay:** the actor stops DATA, publishes a complete new birth
sequence, **reuses the current session's `bdSeq`**, then resumes. `Node
Control/Rebirth` is Boolean false in NBIRTH and has no alias. The exact final-update
policy (step 7) is selectable but must be *selected*, not implicit — that is a spike
exit decision.

## 4. Test / release gates (expanded — item 5)

**Protocol golden / actor tests must prove:**
- exact NCMD subscription at **QoS 1 before NBIRTH**;
- NBIRTH carries the `bdSeq` metric **matching the NDEATH Will**;
- `Node Control/Rebirth` has **no alias**;
- same-session Rebirth **retains** `bdSeq`; a new CONNECT **reserves an incremented**
  `bdSeq`;
- NDEATH payload = only its `bdSeq` metric, **no Sparkplug `seq`**; Will is **QoS 1,
  retain=false**;
- **no DATA leaves the actor** between accepting Rebirth and completing NBIRTH;
- graceful MQTT 3.1.1 shutdown **publishes NDEATH before DISCONNECT**;
- NBIRTH `seq=0` **physically encoded** (Tahu #260 presence bug); seq wrap 255→0;
  `is_historical` + dual timestamps; `is_null` metrics; Quality property.

**Test Connection profile (Studio):** unique temporary Client ID; connect /
authenticate / TLS-check / disconnect only; **no** production Will, NBIRTH, NCMD
subscription, or Edge Node ownership.

**Broker integration runs unconditionally in ≥1 CI/release job** (container broker).
*Intersects the repo's CI gap — no CI exists yet; ties to the deferred "stand up CI"
task (`2026-07-13-post-roadmap-sequencing-decision.md`).*

**Independent interop before release:** own mock-host subscriber **+** independent
Sparkplug decoder **+** real **Ignition + MQTT Engine** (replayed values don't
overwrite current value; Rebirth; alias resolution post-rebirth; near-max-packet
births).

## 5. Technical-spike charter (adopted from the review)

Produce **decisions + executable evidence**, not production Sparkplug code. A small
throwaway NBIRTH/NDATA probe is fine only to verify MQTTnet/broker behavior.

| Workstream | Required evidence | Exit decision |
|---|---|---|
| Replay context | Trace `ReplayCoordinator`/`RetryStateMachine`/`SinkPublisher`/cursor; prototype propagating phase, route id, epoch, high-water mark | Existing APIs sufficient, or exact optional Core interface approved |
| Birth inputs | Complete manifest + current snapshot on cold start, incl. a not-recently-changed metric | Selected manifest/snapshot source + consistency model |
| Route cardinality | Two publishers → one Edge Node actor | Confirm one-route restriction (or design aggregation) |
| MQTTnet QoS 0 | Forced socket loss before/during/after `PublishAsync`; packet trace if practical | Precise meaning of "local completion" |
| Identity persistence | Crash before/after `bdSeq` reservation, during + after CONNECT | Atomic `bdSeq` reservation algorithm + state-store key |
| Delivery capability | Route config = Sparkplug + requested AtLeastOnce | Typed validation behavior + UI wording |
| Replay cutover | Backlog + continuously arriving live points | Exact Replay/CatchUp/Live algorithm (§3) |
| Session ownership | Two destinations, same broker/group/node | Deterministic rejection (or coordination) |

**Spike deliverables:** (1) code-path + sequence diagram; (2) prototype tests for
replay context + MQTTnet QoS-0; (3) manifest/snapshot design; (4) route-cardinality
decision; (5) concrete `PublishContext`/capability API proposal if needed; (6) plan
**v2.2** + any final ADR corrections; (7) **go/no-go** for kernel implementation.

## 6. Governance decision needed from you — CLAUDE.md #12

The delivery decision is made (QoS 0 accepted). The 2nd review says the #12 change
should now be **formal**, not a "proposed footnote." I have **not** edited project
law unilaterally. Proposed amendment to CLAUDE.md §3 #12 (and mirror in
ARCHITECTURE_BLUEPRINT.md Appendix A / delivery-modes §):

> **#12 (amended).** `AtMostOnce` and `AtLeastOnce` only in v1; `ExactlyOnce`
> rejected at config validation. **Broker-acknowledged `AtLeastOnce` is available
> only where the destination protocol exposes a positive broker/application
> acknowledgment.** A destination whose acknowledgment boundary is local-transport
> (e.g. Sparkplug B v1, QoS 0) supports durable store-and-forward but **not**
> broker-acknowledged `AtLeastOnce`; route validation rejects that pairing.

**DONE (2026-07-13, user-approved).** Applied to CLAUDE.md §3 #12 and
`ARCHITECTURE_BLUEPRINT.md` §19.7 (delivery-modes table + acknowledgment-boundary
paragraph) + Appendix A locked-decisions row. #12 is no longer read as absolute;
the Sparkplug B v1 QoS-0 carve-out is now platform law, consistent with the typed
`DeliveryCapabilities` + route-validation rejection.

## 7. Parallel wizard mockup (non-wired) — fields locked

**Show:** broker host/port · TLS + credentials · Group ID · Edge Node ID · MQTT
Client ID · **source-qualified metric-naming preview** · topic preview ·
**QoS-0 / store-and-forward delivery notice** · Test Connection.
**Do NOT show (v1):** Device ID · Primary Host ID · MQTT-version selector · QoS
selector · AtLeastOnce selector · DCMD/general-command options.
**Do not wire** to config/validation until the spike resolves route cardinality,
delivery-capability validation, and manifest/snapshot ownership.

## 8. Next move

Recommended: **run the technical spike (§5) first**; allow the **static non-wired
wizard mockup (§7) in parallel**. The three locked product decisions (QoS 0,
node-only, MQTT 3.1.1) are not reopened. Open governance item: §6 (#12 amendment).
