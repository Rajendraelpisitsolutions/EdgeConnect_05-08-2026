# K3 Slice 5 — Review Bundle r2 (full wire preflight before the first-observed decision)

**Branch:** `feat/sparkplug-b-k3-session-actor` → PR #188 (master)
**Commit:** `ccba6b5` — *fix(sparkplug): K3 slice 5 review r2*
**Exact source diff:** `docs/sessions/2026-07-21-sparkplug-b-k3-slice5-r2-source-diff.md` (full `git show -W`, no ellipses, attached).
**Build:** SparkplugB src `0/0` (warnings-as-errors); tests `0/0`.
**Tests:** `455 passed / 0 / 0`, broker-free.

r1 was accepted for B1/B3/B4. r2 folds the one remaining code blocker (R2) and the four requested evidence completions. No architectural change; no Core change.

---

## R2 (code) — full wire preflight now precedes the first-observed rebirth decision

`PublishGatedAsync` order is now exactly what the review required:

```
classify every point (static schema)
→ if any material mutation: fail closed              (wins over first-observed)
→ validate the phase enum                            (fail closed on undefined)
→ build + validate EVERY point's sample AND wire state   (ToSample UTC + FromDataPoint value mapping)
→ if any first-observed metric: request SchemaChange rebirth
→ otherwise encode + publish
```

The first-observed return moved **below** the `ToSample`/`FromDataPoint` loop, so a malformed DATA point can no longer be concealed behind a supported schema-growth event. This closes the reachable bypass for: a non-UTC `DeviceTimestamp`, a CLR value that mismatches the declared canonical type, a pre-Unix-epoch timestamp, an unmappable datatype — **including on the first-observed point itself**. The samples + states are still pre-built, so nothing fallible runs after a successful MQTT publish.

**Evidence**
- `Publish_FirstObservedAndMalformedKnownPoint_FailsClosed_NoRebirth` (Theory, both orders) — a first-observed metric next to a known metric with `DeviceTimestamp.Kind = Unspecified` throws `ENCODE_TIMESTAMP_NOT_UTC`; no host request, no publish, no seq; actor Faulted.
- `Publish_FirstObservedPointItself_WrongClrValue_FailsClosed_NoRebirth` — a first-observed point carrying a string under a declared `Integer` type throws `ENCODE_VALUE_TYPE_MISMATCH`, winning over the schema-rebirth outcome.

---

## Evidence completions (production already accepted)

| Item | What was added |
|--|--|
| **B1** reverse order | `Cutover_MixedFirstObservedAndMaterialMutation_MaterialWins` is now a two-order `[InlineData(true/false)]` theory — material wins in both enumeration orders. |
| **B3** cutover cancellation | `Cutover_FinalUpdateCancellationAfterTransportEntry_MarksSuspect_NoSeq_NotLive_NotFaulted` — an in-transport OCE on the final-update send makes the authority suspect, consumes no seq, does not enter Live, and does not coarse-fault. |
| **seq wrap** | `Publish_SeqWrapsThrough255To0_WithWireEvidence` now **byte-compares** the `seq=255` and `seq=0` NDATA payloads against independently-built `EncodeNData(Create(255)/Create(0), …)` — wire-sequence evidence, not just the actor counter. |

B1, B3, B4 production code is unchanged from r1 (accepted). Only tests were added for the completions above; the only production change in r2 is the `PublishGatedAsync` reorder (R2).

---

## Carry-forwards still open (for slice 6)
- Slice 6 turns the suspect latch + active-generation disconnects into the coalesced Core rebirth request, with an authoritative generation gate (not relying on transport-side suppression).
- Generation-exhaustion (`long.MaxValue`) check before the `bdSeq` reservation.

The exact `git show -W` diff (both files, 0 ellipses) is attached for line-level sign-off. Slice 6 remains paused pending this pass.
