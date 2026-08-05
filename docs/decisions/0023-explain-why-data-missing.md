# ADR-0023: Explain Why Data Is Missing — deterministic "because" chain for missing data verdicts

**Status:** Proposed (2026-05-30)
**Date:** 2026-05-30
**Framing:** When the Live Data Tap (ADR-0018) Compare mode produces a ⛔ "missing on sink" verdict for a point, the next question is *why*. This ADR specifies the deterministic walker that produces a structured because-chain from observable system state — never speculation, never LLM summary. The output is the canonical example of P7 (surfaces explain outcomes) in action.

## Context

The 2026-05-30 diagnostic-strategy review identified this as the killer feature in EdgeConnect's diagnostic surface set. ChatGPT's framing was direct:

> *Every industrial engineer eventually asks: "I know the PLC produced the value. Why didn't it reach the destination?" Most products show "Disconnected / Error / Timeout." You still have to investigate. Imagine instead: ✓ Source received, ✓ Route processed, ✓ Transform passed, ✗ Sink rejected publish — Reason: MQTT broker disconnected at 10:52:31. This is not diagnostics. This is explainability.*

The architectural foundations are already in place:

- Live Data Tap Compare mode (ADR-0018) detects missing-on-sink verdicts
- Route Flight Recorder (ADR-0021) carries recent state-transition events
- Per-sink connection state lives in the sink supervisor
- Buffer cursor state lives in the per-sink SQLite store-and-forward
- Per-route metrics carry queue depth, drop counts, dispatch counts
- Fault registry carries the current and recent fault list per adapter

The missing piece is the **walker** that, given a verdict, produces a structured because-chain from these sources. ADR-0023 specifies the walker's contract and the chain's shape.

## Decision

The Explain-Why walker conforms to the following five rules.

### Rule 1 — Trigger: any "missing-on-sink" or "extra-on-sink" verdict

The walker runs on-demand when the operator clicks "Why?" on a verdict row in Compare mode (ADR-0018), OR when a verdict is rendered into the Route Timeline (ADR-0026), OR when an API client requests `/api/v1/routes/{id}/diagnostics/explain-verdict?correlationId={cor}`.

The walker MUST be cheap (≤50 ms) because it runs interactively. All state it reads is already cached / in-memory in the existing diagnostic surfaces.

### Rule 2 — Output shape: ordered chain of structured checks

The walker produces an ordered list of `ExplainStep` records:

```csharp
public sealed record ExplainStep
{
    public required int Order { get; init; }                    // 1, 2, 3, ...
    public required ExplainStepKind Kind { get; init; }         // SourceCapture, RouteAccept, Transform, Buffer, SinkDispatch, SinkAck, ...
    public required ExplainStepStatus Status { get; init; }     // Pass, Fail, Unknown
    public required string OperatorLabel { get; init; }         // "Source captured the point"
    public string? StructuredEvidence { get; init; }            // "at 10:52:31.340, source=OPC-UA-Cli"
    public string? RemediationHint { get; init; }               // "Open MQTT sink detail to see the broker connection state"
    public string? FaultCode { get; init; }                     // CORE.SINK_DISCONNECTED if relevant
}
```

The chain is rendered as a vertical step list with status icons. Every step has `OperatorLabel` (P6 operator-language); `StructuredEvidence` carries the specific timestamp / ID / state value that backs the status; `RemediationHint` carries Level-3 guidance (P7).

### Rule 3 — Deterministic state walk, not heuristic

The walker is a deterministic function over observable state. The walk for a "missing-on-sink" verdict:

1. **Source capture**: did the source-side ring buffer capture this correlation id? → Pass / Fail
2. **Route accept**: did the RouteWorker's channel receive the point? (check the route's dispatch counter delta or the route ring buffer) → Pass / Fail / Unknown
3. **Transforms**: did the route's transforms accept the point? (check transform fault count for the relevant transform indices, check the route ring's transform-output ring) → Pass / Fail / Unknown
4. **Buffer enqueue**: did the per-sink buffer cursor advance past this point? (read SQLite buffer cursor; compare to the canonical point's offset) → Pass / Fail / Unknown
5. **Sink dispatch**: did the sink supervisor attempt to publish this point? (check sink dispatch counter, sink last-error)
6. **Sink ack**: did the sink confirm publication? (push sinks) or did the pull-cycle expose the new value? (pull sinks)
7. **External factors**: relevant Flight Recorder events in the window — sink disconnect, broker reject, cert expiry, license downgrade

For each step, the walker reads the current state value and decides Pass/Fail/Unknown by structured rule. The walker does not use heuristics, similarity scores, or LLM inference at any step.

### Rule 4 — "Unknown" is a first-class status

Per P7's honesty clause: where the walker cannot deterministically determine a step's status, it returns `Unknown` rather than guessing. Example: the buffer-cursor check returns Unknown if the buffer was reaped between when the point was enqueued and when the operator clicks "Why?" — the cursor moved past, but we cannot prove this specific point was the one that advanced it.

`Unknown` is rendered with a neutral icon (◯) and an explanation: *"This step's outcome cannot be reconstructed — buffer cursor advanced past this point but the per-point trail was reaped."* The operator knows the answer is "we don't know" rather than seeing a falsely-confident Pass.

### Rule 5 — Same walker, different verdict types

The same walker shape handles other verdict types with verdict-specific step sequences:

| Verdict | Step sequence |
|---|---|
| Missing-on-sink | Source → Route → Transforms → Buffer → SinkDispatch → SinkAck → External |
| Transform-altered | Source → RouteCapture → TransformStep₀ → TransformStep₁ → ... → SinkDispatch → SinkAck |
| Extra-on-sink | SinkAck → BufferReplay → RouteAttribution → SourceCapture |
| Source-side gap | DeviceLastSeen → SourceConnection → SourcePoll/Subscribe → SourceCapture |

Each verdict type registers its step sequence at composition time; the walker is verdict-dispatched.

## Consequences

**Positive:**

- The single highest-perceived-value diagnostic feature in EdgeConnect ships with a structurally honest implementation — no false confidence, no AI guessing
- Operators see the four-question framework materialised: *what happened (point captured by source), why (sink disconnected at 10:52:31), what changed (broker stopped accepting connections), what action (open MQTT sink detail / wait for store-and-forward drain)*
- The walker's output becomes a first-class part of the Bundle (ADR-0020) — the support engineer receives the customer's because-chain without needing to reproduce the verdict
- Composes with Route Timeline (ADR-0026) — every verdict in the timeline has a "Why?" expander that renders this walker's output
- The deterministic-only contract means the walker's behaviour is reviewable, testable, and reproducible — any state snapshot reliably produces the same chain

**Negative:**

- The walker's correctness depends on the underlying state being correctly populated. Edge cases where state lags (e.g., counter not yet incremented when "Why?" is clicked) produce Unknown-heavy chains. Acceptable, but the walker must avoid claiming Fail when the truth is Unknown.
- Adding a new verdict type requires adding its step sequence to the walker. Discipline cost; tractable.
- Adding a new sink with a non-standard ack model (e.g., a future fire-and-forget HTTP POST sink) requires the walker to handle "we cannot prove the sink got the point" honestly — that's an Unknown outcome at the SinkAck step.

**Forbidden patterns:**

- A step status derived from an LLM call ("the model thinks this point was probably dropped because the buffer was at 87%")
- A heuristic fallback ("if we don't know, guess Pass" or "guess Fail") — Unknown is mandatory
- A free-text "Reason" field that the walker fills with English prose; remediation is the structured `RemediationHint` field, not narrative
- A walker that reads private adapter state via reflection or back-channels — all state it reads MUST be from the public diagnostic surfaces (Flight Recorder, buffer cursor, supervisor counters, fault registry)

## Reference

- ADR-0017 — demand-driven diagnostic surfaces (the walker runs on-demand, qualifies as one)
- ADR-0018 — Live Data Tap Compare mode (produces the verdicts the walker explains)
- ADR-0021 — Route Flight Recorder (provides the External-factors step's data)
- ADR-0026 — Route Timeline (renders walker output inline per verdict)
- Platform principle P4 — preserve the explainability data path (the walker is the consumer of that path)
- Platform principle P7 — surfaces explain outcomes (this ADR is P7's canonical worked example)
- `docs/sessions/2026-05-30-diagnostic-strategy-handoff.md`
