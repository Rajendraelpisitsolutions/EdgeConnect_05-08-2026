# REVIEW.md — Code Review Checklist for Elpis EdgeConnect

This checklist applies to every pull request that touches `ElpisEdgeConnect.Core` or any protocol module. Reviewers (human or AI) should walk through every relevant section and not approve a PR until all applicable items pass.

Items are grouped by concern. Skip sections that don't apply to a given change, but don't skip items within a relevant section.

---

## Severity Rubric

Every review finding must be annotated with one of three severity levels. This keeps review outputs consistent across humans and AI reviewers and makes it obvious what must be fixed before merge versus what can be deferred.

| Severity | Meaning | Action |
|----------|---------|--------|
| **🔴 Blocking** | The PR violates a LOCKED architectural decision, breaks a contract, introduces a correctness bug, leaks secrets, or fails a milestone exit gate. | Must be fixed before merge. No exceptions. |
| **🟡 Important** | The PR is correct but has a quality, performance, test coverage, or documentation gap that should be addressed before merge. Rare exceptions permitted with explicit user approval and a tracked follow-up. | Should be fixed before merge; document any deferral. |
| **🟢 Suggestion** | A refinement, style preference, or nice-to-have that the author can take or leave. | Optional. Do not block merge on these alone. |

**Default severity by section:**
- Section 0 (Scope and Size): **🔴 Blocking**
- Section 1 (Architectural Alignment): **🔴 Blocking**
- Section 2 (Contract Stability): **🔴 Blocking** for breaking changes, **🟡 Important** for additive
- Section 3 (Section 19 Traceability): **🔴 Blocking** when applicable
- Section 4 (Code Logic and Behavior): **🔴 Blocking** for correctness items (behavioral mismatch, off-by-one, invariant violations, tests that cannot detect mutations), **🟡 Important** for quality items
- Sections 5-13: **🟡 Important** unless the item text explicitly says otherwise
- Section 14 (Final Gate): **🔴 Blocking**

Reviewers must state severity explicitly in their comments (e.g., *"🔴 Blocking — Core references `ElpisEdgeConnect.Sources.Modbus` in `AdapterRegistration.cs:42`, violating REVIEW.md §1 Core purity"*).

---

## 0. Scope and Size (Gate Before Everything Else)

This section runs *before* any other review work. If any item here fails, stop and return the PR to the author before spending time on deeper review.

- [ ] **🔴 Milestone scope** — Does this PR implement work outside the current Phase 1 milestone without explicit user approval? Check `docs/PHASE1_EXECUTION_PLAN.md` to identify the milestone this PR targets (A1, A2, A3, B1, B2, B3, C1, C2a, C2b, C3, C4, D1, D2, D3, D4, D5). Work that belongs to a future milestone (e.g., buffer code in an A2 PR, transform pipeline code in a B1 PR) is **rejected** unless the user explicitly authorized the scope expansion.
- [ ] **🔴 PR size** — Is the PR too large to review reliably? Rough thresholds for AI-assisted work:
  - ≤ 500 lines of non-test diff: reviewable in one pass
  - 500–1500 lines: reviewable but requires careful segmentation
  - \> 1500 lines: **request a split** before continuing review
  - One full milestone with its tests and docs is typically acceptable even if it exceeds these limits, but only if it is cohesive and the diff is mechanical. A sprawling cross-cutting change that mixes unrelated concerns must be split regardless of line count.
- [ ] **🔴 Cohesion** — Does the PR mix unrelated changes (e.g., "add B1 config models + fix unrelated bug in A3 errors")? If yes, request a split.
- [ ] **🟡 Placeholder folders and files** — The repo already contains folders for future milestones (e.g., `Core/Buffer/`, `Core/Routing/`, `Core/Pipeline/`, `Core/Licensing/`, `Core/Configuration/`, `Core/Diagnostics/`). **Empty placeholder folders are acceptable.** What is NOT acceptable in an early-milestone PR is meaningful logic added to those future-milestone areas. A B1 PR should not contain routing engine code even if the `Core/Routing/` folder exists. Reviewers should treat any non-trivial code in a folder belonging to a later milestone as a scope violation (first item above).
- [ ] **🔴 Milestone prerequisites** — Does this PR depend on a milestone that has not yet merged? If yes, **reject** and note the prerequisite. Example: a C2b PR cannot merge before C2a has merged and passed its gate review.

---

## 1. Architectural Alignment (Blocking)

These checks enforce the LOCKED decisions in `docs/ARCHITECTURE_BLUEPRINT.md` Appendix A. A failure on any of these is a blocking issue that must be fixed before merge.

- [ ] **Core purity** — Does any new code in `ElpisEdgeConnect.Core` reference a protocol module? If yes, **reject**. Core must be protocol-agnostic.
- [ ] **Dependency direction** — Do any Source or Sink modules reference each other? If yes, **reject**. Allowed direction is strictly `Core ← Adapters`.
- [ ] **Canonical data model** — Does any adapter emit data in a form other than `CanonicalDataPoint`? Does any sink accept a protocol-specific payload instead of `CanonicalDataPoint`? If yes, **reject**.
- [ ] **Route-first** — Is any data flow being added that bypasses the Route concept? If yes, **reject**.
- [ ] **Dynamic plugin loading** — Does any code call `Assembly.LoadFrom`, `AssemblyLoadContext`, or similar to load protocol modules at runtime? If yes, **reject**. v1 uses compile-time projects.
- [ ] **License gating** — Does any new adapter skip the license check at DI registration? If yes, **reject**.
- [ ] **Store-and-forward bypass** — Does any new code path skip the per-route buffer for production (non-test) traffic? If yes, **reject** unless the route is explicitly configured with `BufferMode.None` and delivery mode `AtMostOnce`.
- [ ] **Fanout atomicity** — Does any code path make one sink's progress dependent on another sink's progress? If yes, **reject**. Sinks are independent per blueprint Section 19.2.
- [ ] **Per-adapter isolation** — Can an exception in one adapter propagate to another adapter, the routing engine, or the host? If yes, **reject**.
- [ ] **AI in data path** — Does any pipeline step, transform, buffer, or sink call an AI provider at runtime? If yes, **reject**. AI lives in the management layer only.
- [ ] **ExactlyOnce delivery** — Is `ExactlyOnce` being implemented or enabled? If yes, **reject**. Out of scope for v1 per Section 19.7.
- [ ] **Global ordering** — Does any code promise ordering across sources? If yes, **reject**. Only per-source ordering is guaranteed per Section 19.6.
- [ ] **Cloud-only feature** — Does any feature require internet access to function? If yes, **reject** unless it's an explicit optional cloud integration with a local fallback.
- [ ] **Phone-home licensing** — Does any license code make a network call? If yes, **reject**. Licenses are fully offline.
- [ ] **Silent AI actions** — Does any AI agent change state without explicit user confirmation in the chat interface? If yes, **reject**.

---

## 2. Contract Stability

Contracts in `ElpisEdgeConnect.Core.Adapters` (`ISourceAdapter`, `ISinkAdapter`) and `ElpisEdgeConnect.Core.Model` (`CanonicalDataPoint`) are LOCKED after Milestone A completes.

- [ ] **Breaking changes** — Does the PR change the signature of `ISourceAdapter`, `ISinkAdapter`, or the shape of `CanonicalDataPoint` in a way that breaks existing implementations? If yes, the PR requires explicit blueprint revision before merge.
- [ ] **Additive changes** — New optional members on contracts are acceptable. Do they have sensible defaults that existing implementations can rely on without changes?
- [ ] **Capabilities flags** — If a new capability is being added, is the corresponding `SourceCapabilities` or `SinkCapabilities` flag defined, and is the contract honoring it (not calling the new method when the flag is absent)?
- [ ] **State machine** — If lifecycle behavior is changing, is `AdapterStateTransitions` updated and are there tests covering the new transitions?

---

## 3. Section 19 Traceability (Routing Engine PRs)

Any PR touching `ElpisEdgeConnect.Core.Routing` or `ElpisEdgeConnect.Core.Buffer` must trace to one or more subsections of blueprint Section 19.

- [ ] **Traceability note** — Does the PR description cite the Section 19 subsections it implements or modifies?
- [ ] **19.2 Fanout independence** — If changes affect fanout, is there a test proving one sink's failure does not block another sink?
- [ ] **19.3 Buffer granularity** — If changes affect buffer storage, is storage per-route and cursors per-sink?
- [ ] **19.4 Retry tracking** — If changes affect retry, is retry state per-sink, per-batch, in-memory only (not persisted)?
- [ ] **19.5 Replay ordering** — If changes affect recovery, is replay sequential per sink and do live messages wait for drain?
- [ ] **19.6 Ordering guarantees** — Are per-source ordering guarantees preserved? Is cross-source ordering NOT promised?
- [ ] **19.7 Delivery modes** — Only `AtMostOnce` and `AtLeastOnce` are implemented? `ExactlyOnce` throws at validation?
- [ ] **19.8 Backpressure** — Does backpressure propagate via buffer spill and drop policy without blocking the source acquisition loop?
- [ ] **19.9 Lifecycle** — Are state transitions validated against the allowed transitions table?

---

## 4. Code Logic and Behavior

This is where reviewers verify that the code actually does what it is supposed to do — not just that it compiles, not just that its interface matches the blueprint, not just that tests pass. **This is the most important section of the review, because every other section can be green while the logic is silently wrong.**

Default severity for this section: **🔴 Blocking** for correctness items and **🟡 Important** for quality items. Individual items are tagged explicitly below.

### 4.1 Intended behavior vs observable behavior

- [ ] **🔴 Does the code match the intended behavior, not just the interface?** Read the relevant blueprint section and the method's XML doc comment first. Then read the code. Do they describe the same thing? If the blueprint says "per-sink cursors advance independently" and the code locks both cursors under one mutex, the interface matches but the behavior is wrong.
- [ ] **🔴 Does the code match its own XML doc?** If the doc says "returns null when not found" and the method throws, either the code or the doc is wrong. Silent disagreement is a latent bug and will surface at the worst possible time.
- [ ] **🔴 Does the implementation match the scenario described in the tests?** Tests sometimes drift from the behavior they claim to exercise. Read the test name, read the setup, read the assertion — do they describe the same scenario the code actually executes?
- [ ] **🟡 Are behavioral invariants from the blueprint enforced where they are claimed?** Example: blueprint §19.6 promises "per-source monotonic sequence numbers." Where in the code is that guarantee actually enforced? Where is it tested under the realistic conditions it's claimed to hold for?

### 4.2 Happy path, boundary cases, failure cases

- [ ] **🔴 Trace the happy path by hand.** Pick a realistic input, mentally execute the code end-to-end, verify it produces the expected output. Do not skip this because "the tests pass." Tracing by hand catches bugs tests don't.
- [ ] **🔴 Trace at least one failure path by hand.** Pick the most likely failure point (network timeout, disk error, cancellation, bad input). Mentally execute from that failure. Verify: is state left consistent? are resources released? are caller expectations met? is the correct error code produced?
- [ ] **🔴 Are boundary cases handled?** For every collection parameter: what if it's empty? What if it has exactly one element? What if it has the maximum allowed? Batching, pagination, and "process items in groups of N" code almost always breaks at N=0 and N=1.
- [ ] **🔴 Are numeric edge values handled?** Zero, one, negative values (if the type allows them), `int.MaxValue`, `long.MaxValue`, `double.NaN`, `double.Infinity`, timestamps at the Unix epoch, `DateTime.MinValue`, `TimeSpan.Zero`.
- [ ] **🟡 Null, empty, and default inputs.** Every nullable parameter needs a documented decision: reject, coerce, or accept. The decision must be consistent across similar methods in the same subsystem. A method that rejects null while its sibling silently accepts it is a bug.
- [ ] **🟡 Cancellation at every await point.** If a method has multiple awaits, cancellation can fire between any two of them. Does the code handle cancellation cleanly at each point? Are partial operations rolled back or left in a documented consistent state?

### 4.3 Off-by-one, ordering, state transitions

- [ ] **🔴 Off-by-one audit.** Look at every loop boundary, every range check, every index calculation, every `< vs <=`, every `> vs >=`. These are the most common and most silent bugs in any codebase. Ask at every comparison: "which side of the boundary is correct, and would swapping it fail any test?"
- [ ] **🔴 Ordering assumptions that aren't guaranteed.** Dictionary iteration order is not portable across frameworks. `DateTime.Now` on two adjacent calls is not strictly monotonic. `Task.WhenAll` does not guarantee the tasks complete in the order they were started. LINQ `OrderBy` is stable; `Array.Sort` is not. Any code that relies on one of these without explicitly using a stable-order primitive is a latent bug.
- [ ] **🔴 State transition atomicity.** For every state change: is the check done before the side effect, or after? (Check-then-act must be atomic or the act must be reversible.) Is the new state observable during the transition, or only after it completes? Can a concurrent observer see an intermediate state that would be invalid according to the state model?
- [ ] **🔴 TOCTOU (time-of-check to time-of-use) races.** A common pattern: read shared state, release the lock, act on the value read. By the time the action runs, the value may have changed. Anywhere the code reads shared state outside a lock and then uses that value to make a decision, ask whether that's a race.
- [ ] **🟡 Sequence allocation before or after side effects.** If a point is assigned a sequence number and then persisted, and the persistence fails, what happens to the sequence? Is it leaked (gap in the sequence) or reused (potential duplicate)? Is either behavior documented?

### 4.4 Invariants

- [ ] **🔴 List the invariants for the subsystem being reviewed.** Read the blueprint sections that govern it. Write down every `must`, `always`, `never`, and `guaranteed` statement. For each invariant, find in the code:
  1. Where it is **established** (the code that makes it true)
  2. Where it could be **violated** (the code paths that might break it)
  3. Where it is **verified** (assertions, tests, or observable contracts that would catch a violation)

  Example invariants for the buffer subsystem (§19.3):
  - "`min(sink_cursors.committed_sequence) > sequence` before a point is deletable" → find eviction code, verify the check.
  - "Per-source sequence numbers are strictly monotonic" (§19.6) → find the factory, verify there is no path that could allocate the same number twice.
  - "Routes never drop data in `AtLeastOnce` mode unless buffer retention is exhausted" (§19.7) → find every drop site, verify the condition.

- [ ] **🟡 Are invariants temporarily violated during legitimate transitions?** Some code legitimately violates an invariant briefly (e.g., doubly-linked-list insertion has a window where the list is malformed). Is that window observable to concurrent readers? Is it bounded?
- [ ] **🟡 An invariant that isn't tested isn't an invariant — it's a wish.** Every claimed invariant should have at least one test that would fail if the invariant were broken.

### 4.5 Tests prove behavior, not coverage

- [ ] **🔴 Mutation test in your head.** For every new method, ask: *if I mutated this line, would any existing test fail?* Walk through candidate mutations — flip a `<` to `<=`, swap two arguments, change a constant, delete an `if`, silently swallow an exception. For each mutation, ask whether the test suite would catch it. If the answer is "no" for a mutation that would cause real damage in production, the tests are too weak. Add a test.
- [ ] **🔴 Could this code pass tests but still be logically wrong?** This is the summary question for this section. If you can construct a realistic bug that the tests would not catch, the review is incomplete.
- [ ] **🔴 Concurrency tests must actually exercise concurrency.** Look for `Task.Run` that isn't awaited, `Thread.Sleep` substituting for synchronization, tests that would pass even if the code were single-threaded. The 1M-point concurrent sequence test in `CanonicalDataPointFactoryTests.SequenceNumbers_MonotonicUnderConcurrentLoad` is the reference pattern: multiple genuine threads, assertion on a global invariant (sorted result equals `[1..1_000_000]`), deterministic outcome.
- [ ] **🟡 Do tests assert invariants, not just return values?** A test that calls `Foo()` and asserts `result == 42` is weaker than one that asserts both the return value AND the observable side effect on state. Prefer the latter.
- [ ] **🟡 Negative tests.** For every documented failure mode, is there a test that triggers it and verifies the correct error code is produced? Absence of negative tests is a strong signal of weak behavioral coverage.
- [ ] **🟡 Tests use realistic inputs.** A test that uses `new CanonicalDataPoint { Value = 1 }` is weaker than one that uses a value the actual adapter would produce. Tests that trivialize inputs can hide bugs that only surface under realistic load.
- [ ] **🟡 Test setup is minimal and isolated.** Tests that share extensive setup often hide implicit dependencies between tests. Each test should set up what it needs and nothing more.

### 4.6 "Silently wrong" bug patterns to actively look for

Specific patterns that pass tests but are wrong. Treat each as a 🔴 finding when encountered:

- **Swallowed exceptions.** A `catch { }` or `catch (Exception) { }` block that does nothing. Almost always a bug. The one exception: intentional fire-and-forget with a comment explaining why.
- **Unreachable error handling with "this shouldn't happen" comments.** These often handle exactly the case that happens in production, silently. Either the code is correct and the handler is dead (delete it) or the handler is needed and the comment is wrong.
- **Tests that assert the default value of a type.** If the test asserts `result.Count == 0` and `result` was never populated, the test passes even if the code never ran. Prefer asserting a non-default value whenever possible.
- **Tests that use loose equality.** `.Should().BeEquivalentTo(expected)` can pass for subset matches depending on configuration. Use `.Should().Equal(expected)` when exact membership and order matter. Use `.Should().HaveCount(n)` alongside `Contain` checks.
- **`Task.Run` without await in tests.** The test returns before the task runs; assertions fire on partial or empty state.
- **Mocks that always succeed.** If the test mock always returns `Success = true`, the code's failure paths are untested. Explicitly simulate failure.
- **Assertions on "not null" instead of "equals expected."** `.Should().NotBeNull()` is weaker than `.Should().Be(expected)`. Use it only when the exact value is genuinely unknowable.
- **Tests that don't assert counts.** `.Should().Contain(x)` passes if the collection contains `x` even if it also contains 50 extras. Use `.Should().ContainSingle(...)` or assert count separately.
- **Clock-dependent tests with narrow windows.** Two `DateTime.UtcNow` calls on adjacent lines may differ by 0, 1, or 16 ms depending on OS scheduler quantum. Tests that assert exact timing differences are flaky. Use `BeCloseTo` or bracket-range assertions.
- **Shared mutable test state.** Two tests that modify a static field will pass or fail based on execution order. This is a latent bug — fix by isolating state per test.
- **Copy-paste errors in arguments.** When a method takes multiple same-type parameters (`CreatePoint(string tagName, string tagPath, ...)`), a swapped pair compiles cleanly and often passes tests. Look for calls where named arguments would help and aren't used.
- **Off-by-one in batch splitting.** Code that processes items in batches of `N` often mishandles the final partial batch. Verify with a test that uses a count that is NOT a multiple of the batch size.

### 4.7 Logic-review procedure (do this for every non-trivial change)

1. **Read the blueprint section and the method's XML doc before reading the code.** Know the intended behavior first; otherwise you'll fall into the trap of "the code looks consistent with itself."
2. **Trace the happy path by hand with a realistic input.** Do not trust that passing tests mean the code is correct.
3. **Trace one failure path by hand.** Pick the most likely failure point and mentally execute from there. Verify state consistency and resource release.
4. **List the invariants** the subsystem claims and locate each in the code (established, potentially violated, verified).
5. **Mutation-test the tests in your head.** For each line you'd want to mutate, ask whether the test suite would catch it.
6. **Assume a hostile caller.** What can they pass? Null? Empty? Wrong type? Concurrent calls? Immediate cancellation? A 10-second delay? A collection of 1M items? For each, is the behavior correct and documented?
7. **Search for the "silently wrong" bug patterns in §4.6.** Grep if needed.

### 4.8 When logic review is lighter

Pure refactoring PRs that move code without changing behavior warrant a lighter logic review: verify the refactoring preserves behavior (tests still pass, no new control-flow branches), but skip the invariant-tracing and hostile-caller checks. Document in the review summary: *"Logic review: refactoring only, behavior preservation verified."* The procedure in §4.7 is for PRs that introduce or change behavior.

---

## 5. Error Handling and Taxonomy

- [ ] **Structured errors** — Every new failure path throws an `AdapterException` (or subclass) carrying a well-formed `AdapterError`, not a raw `Exception` or `InvalidOperationException`.
- [ ] **Error code catalog** — Every new error code is added to `CoreErrors.cs` (for Core) or the equivalent protocol module catalog. No string literal error codes embedded in throw sites.
- [ ] **Naming convention** — Error codes follow `MODULE.CATEGORY_SUBCATEGORY` format (e.g., `CORE.CONFIG_INVALID`, `FOCAS2.HANDLE_EXHAUSTED`).
- [ ] **Retryable flag** — Is the `Retryable` flag set correctly? (Network, DeviceState, ResourceExhausted, Protocol = usually true; Configuration, Authentication, License = always false.)
- [ ] **Error category** — Does the category match the nature of the failure? (No Configuration errors thrown for network problems, etc.)
- [ ] **Inner exception preservation** — If wrapping a lower-level exception, is the inner exception preserved (not swallowed) for logging?
- [ ] **PII safety** — Does the error message include credentials, secrets, or personally identifying data? If yes, **reject**.

---

## 6. Threading and Async

- [ ] **Cancellation tokens** — Does every public async method accept and honor a `CancellationToken`?
- [ ] **No `async void`** — Except for event handlers, are all async methods returning `Task` or `ValueTask`?
- [ ] **No blocking calls** — Is `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` used anywhere in async code? If yes, **reject** unless there's a documented reason.
- [ ] **`ConfigureAwait(false)`** — Library async calls in Core use `ConfigureAwait(false)` where the continuation doesn't need the original context.
- [ ] **Thread-safety** — Are shared mutable fields protected by `Interlocked`, `lock`, or a concurrent collection? For factory classes like `CanonicalDataPointFactory`, is there a concurrency test covering the new behavior?
- [ ] **Deadlock audit** — Are any new locks introduced? Is the locking order documented to prevent deadlocks with existing locks?

---

## 7. Performance and Allocations

See blueprint Section 18 for performance targets.

- [ ] **Hot-path allocations** — Hot-path code (pipeline steps, routing worker, buffer enqueue) should allocate minimally. Is there a benchmark covering the change?
- [ ] **Benchmark added** — If the PR introduces a new component that will run at high rate, is there a BenchmarkDotNet benchmark with `[MemoryDiagnoser]`?
- [ ] **Benchmark target** — Does the benchmark meet the target specified in `docs/PHASE1_EXECUTION_PLAN.md` D4?
- [ ] **Regression check** — If the PR modifies hot-path code, has the benchmark been re-run and compared against `docs/benchmarks/phase1-baseline.md`?
- [ ] **LINQ in hot paths** — Is LINQ used in hot-path code where a `for` loop would allocate less? If yes, consider rewriting.
- [ ] **String concatenation** — Is `StringBuilder` or interpolated handlers used for anything beyond trivial string building?

---

## 8. Configuration and Licensing

- [ ] **Config schema** — Does the PR add a new config field? If yes, is the JSON schema regenerated and committed to `docs/config-schemas/`?
- [ ] **Backward compatibility** — Will existing config files still load after this change? If not, is there a migration path documented?
- [ ] **Hot-reload safety** — Can this config change be applied to a running gateway without restart? If not, is it flagged as restart-required in validation?
- [ ] **Draft lifecycle** — Does the change respect the draft → validate → apply → rollback flow? No code paths write directly to `config/current.json` outside the `ConfigurationManager`.
- [ ] **License check** — Does the new feature or adapter have a license check at DI registration? Is it enforced at all three layers (packaging, runtime, UI/API)?
- [ ] **License grace period** — Does the change respect the "continue data flow, block config changes" expiration behavior?

---

## 9. Testing

- [ ] **Unit test coverage** — Does every new public method in Core have at least one unit test?
- [ ] **Edge case coverage** — Are null inputs, empty collections, concurrent access, and failure paths tested?
- [ ] **Locked requirement tests** — For any code implementing a LOCKED blueprint requirement, is there a named test that would fail if the requirement were broken?
- [ ] **Test determinism** — Are tests deterministic? No `Thread.Sleep`, no `DateTime.Now` dependencies, no random ordering without fixed seeds.
- [ ] **Arrange-Act-Assert** — Are tests structured with clear AAA phases, separated by blank lines?
- [ ] **Naming convention** — Are test methods named `MethodName_Condition_ExpectedResult`?
- [ ] **FluentAssertions** — Are assertions using `FluentAssertions` for readability, not raw `Assert.Equal`?
- [ ] **Integration test impact** — If the change touches the routing engine, buffer, or pipeline, is there an integration test scenario (from Phase 1 plan D3) updated or added?
- [ ] **Mock adapters only** — Integration tests use `MockSourceAdapter` and `MockSinkAdapter`, never real protocol modules, in Phase 1.

---

## 10. Documentation

- [ ] **🟡 File header** — Does every new source file start with a header comment (file name, purpose, blueprint section reference)?
- [ ] **🟡 XML docs** — Does every public member in Core have XML doc comments? (`CS1591` is suppressed but docs are still required on public API.)
- [ ] **🟡 LOCKED markers** — If the file implements a LOCKED architectural decision, does the header say so explicitly?
- [ ] **🔴 Blueprint updates** — If the change touches an area covered by `ARCHITECTURE_BLUEPRINT.md`, has the blueprint been updated accordingly? If the change contradicts the blueprint, the PR is blocked pending blueprint revision.
- [ ] **🔴 Docs-to-code mismatch** — Does the implementation silently differ from what `ARCHITECTURE_BLUEPRINT.md`, `PHASE1_EXECUTION_PLAN.md`, or `docs/adapter-sdk/` describe, even if tests pass? Tests can validate the wrong behavior if the implementation drifted early. The reviewer must read the relevant doc section and compare it to the implementation — not just trust the test suite. If a mismatch is found, one of three things must happen: (a) fix the implementation, (b) update the docs with explicit user approval, or (c) reject the PR. Silent drift is the most expensive bug class because it compounds across future milestones.
- [ ] **🟡 Phase 1 plan updates** — If the change affects a milestone's scope or deliverables, has `PHASE1_EXECUTION_PLAN.md` been updated?
- [ ] **🟡 Adapter SDK docs** — If the change affects the adapter contract or conventions, is `docs/adapter-sdk/` updated?

---

## 11. Build and CI Hygiene

- [ ] **Zero warnings** — Does the solution build with zero warnings? `TreatWarningsAsErrors=true` in Core must not have any new suppressions.
- [ ] **Suppressions justified** — If a new warning suppression is added (via `<NoWarn>`, `#pragma`, or `[SuppressMessage]`), is there a comment explaining why?
- [ ] **Analyzer clean** — Are all analyzer warnings at Error level resolved, not just suppressed?
- [ ] **No commented-out code** — Has dead or commented-out code been deleted?
- [ ] **No debug output** — Are there any `Console.WriteLine`, `Debug.WriteLine`, or temporary debugging statements? Remove them.
- [ ] **Logs use Serilog** — All logging goes through `ILogger<T>` / Serilog, never direct console writes (except for CLI tools like LicenseGen).
- [ ] **Sensitive data in logs** — Does any new log message include credentials, tokens, or PII? If yes, **reject**.

---

## 12. AI Agent PRs (Phase 4.5+)

Applies only to PRs touching `ElpisEdgeConnect.AI`.

- [ ] **Tool-use pattern** — Does the agent interact via structured tool calls, not free-text code generation?
- [ ] **Tool permissions** — Does every new tool declare its permission level (read-only / draft-write / etc.)? Is the permission enforced?
- [ ] **Audit logging** — Is every prompt, tool call, and response logged to the AI audit log?
- [ ] **User confirmation** — For state-changing actions, does the agent propose a change and wait for explicit user confirmation?
- [ ] **Local LLM support** — Does the agent work against the configured local LLM provider (Ollama), not just cloud providers?
- [ ] **Data sovereignty** — Does the agent honor the `DataSovereignty` config (no telemetry export when disabled, PII redaction when enabled)?
- [ ] **Prompt injection defense** — Are tool outputs and log content wrapped in delimiters and clearly marked as untrusted data, not instructions?
- [ ] **Grounding and citations** — Does the agent cite evidence (diagnostic values, log lines, config fields, doc sections) in its responses?
- [ ] **Failure mode** — If the AI provider is unreachable, does the agent degrade gracefully (disabled) without affecting the gateway's data flow?

---

## 13. Milestone Exit Checks

When a PR claims to complete a Phase 1 milestone, verify:

- [ ] **Milestone Definition of Done** — Every item in the corresponding section of `docs/PHASE1_EXECUTION_PLAN.md` is satisfied, not just "mostly done."
- [ ] **Benchmark gate** — For milestones with benchmarks (A1, B2, B3, C1, C2a, C2b, C3, C4, D4), the benchmark exists, runs, and meets the target.
- [ ] **C2 sub-gates** — C2b cannot be merged until C2a is merged and its gate review has passed.
- [ ] **Section 10 checklist** — Every applicable item in `docs/PHASE1_EXECUTION_PLAN.md` Section 10 "Phase 1 Exit Criteria" that this milestone addresses is checked off.
- [ ] **Documentation deliverable** — Any docs from `docs/PHASE1_EXECUTION_PLAN.md` Section 8.5 that the milestone produces are written and reviewed.

---

## 14. Final Gate (Every PR)

- [ ] Blueprint not violated (Section 1 of this checklist).
- [ ] All tests pass locally.
- [ ] Build is clean with zero warnings.
- [ ] Commit messages are clear and explain *why*.
- [ ] No secrets, credentials, or internal URLs committed.
- [ ] User has been informed of any surfaced architectural conflicts.

---

## How to Use This Checklist

**Order of review:** Always start with **Section 0 (Scope and Size)**. If any Section 0 item fails, stop and return the PR to the author before spending time on deeper review. Then work through Section 1, then the remaining sections in order.

**For human reviewers:** Walk through the sections in order. Skip sections that obviously don't apply (e.g., "AI Agent PRs" for a buffer change). Don't skip items within a section you are walking through. Annotate each finding with a severity tag (🔴 Blocking / 🟡 Important / 🟢 Suggestion).

**For AI reviewers:** When asked to review a diff:
1. Begin with Section 0. If any item there fails, stop and return the PR immediately with the failing items cited — do not continue into Sections 1-14.
2. Explicitly cite which sections of this checklist you are applying and which you are skipping (with the reason).
3. Walk through each applicable item individually. Flag every failing item separately rather than grouping them into a vague "looks good / looks bad" summary.
4. **Annotate every finding with explicit severity** (🔴 / 🟡 / 🟢) per the severity rubric at the top of this document.
5. When in doubt, err on the side of flagging rather than approving, and err on the side of higher severity rather than lower.

**When a check fails:** Don't block silently. State:
1. The severity (🔴 / 🟡 / 🟢)
2. The exact checklist item that failed
3. The file and line where the failure occurs
4. A quote from the relevant blueprint or Phase 1 plan section
5. The minimal change that would make it pass

Example: *"🔴 Blocking — §1 Core purity. `src/ElpisEdgeConnect.Core/Adapters/AdapterRegistration.cs:42` references `ElpisEdgeConnect.Sources.Modbus`. Per ARCHITECTURE_BLUEPRINT.md §3 'Core never references any protocol module.' Move the Modbus-specific registration into `ElpisEdgeConnect.Host` where protocol-module references are allowed."*

**When a check is "not applicable":** State so explicitly. Don't just skip without acknowledgment. Example: *"§12 AI Agent PRs — not applicable; this PR does not touch `ElpisEdgeConnect.AI`."*

**Approval criteria:** A PR may be approved only when all 🔴 items pass, all applicable 🟡 items either pass or are explicitly deferred with user approval and a tracked follow-up, and 🟢 items have been considered (take or leave).
