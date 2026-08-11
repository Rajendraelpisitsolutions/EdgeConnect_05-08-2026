# CLAUDE.md — Project Context for Elpis EdgeConnect


This file is read automatically by Claude Code sessions working in this repository. It provides the stable context every session needs before touching code.


## Shared knowledge
 
This project shares concepts and an MQTT contract with **EREMOS V2**.
Cross-project knowledge lives at `C:\dev\shared-knowledge\`.
 
Always check these files before working on anything that crosses the boundary
(use absolute paths — they work from worktrees too):
 
- `C:\dev\shared-knowledge\README.md`
- `C:\dev\shared-knowledge\common-modules.md`
- `C:\dev\shared-knowledge\architecture-overview.md`
- `C:\dev\shared-knowledge\glossary.md`
- `C:\dev\shared-knowledge\contracts\`
- `C:\dev\shared-knowledge\decisions\`
 
> Absolute paths are used because Claude may run from a git worktree
> (e.g. `C:\dev\EdgeConnect\.claude\worktrees\<name>\`) where relative
> paths like `..\shared-knowledge\` would not resolve.
 
Changes affecting **both** projects → edit in `C:\dev\shared-knowledge\`, commit, push.
Changes affecting **only EdgeConnect** → edit here.
Pull `shared-knowledge` before starting a session.

---

## 1. What This Project Is

**Elpis EdgeConnect** is a protocol-agnostic **Industrial Edge Integration Platform**. It runs as a Windows service on the factory floor, collects data from industrial devices via multiple southbound protocols (FOCAS2, MT-LINKi, MTConnect, Brother HTTP, Modbus TCP, and more), normalizes it through a canonical data pipeline, and delivers it to one or more northbound systems (MQTT, HTTP, TCP, OPC UA Server later).

It is **not** "a gateway with some protocols added over time." It is a modular platform with a locked core runtime, pluggable source and sink adapters, a canonical internal data model, route-based configuration, and three-layer licensing.

The project was originally migrated from `FanucCncDataBridge` (at `C:\dev\EREMOS_V2\FanucCncDataBridge`) and renamed. The existing migrated code lives at `src/ElpisEdgeConnect/` and will be refactored into the new modular structure during Phase 2. Phase 1 builds the new Core runtime alongside it.

---

## 2. The Documents That Govern Everything

**Before making any architectural or design decision, consult these documents in order.** They are the source of truth; anything that contradicts them is wrong.

| Document | Purpose |
|----------|---------|
| `docs/ARCHITECTURE_BLUEPRINT.md` | Master architecture reference. 19 sections covering design principles, contracts, licensing, store-and-forward, diagnostics, performance targets, route execution semantics, AI agents, and more. **Appendix A** lists which decisions are **LOCKED**, **FLEXIBLE**, and **OPEN**. |
| `docs/PHASE1_EXECUTION_PLAN.md` | Concrete engineering execution plan for Phase 1. Milestones A through D, file-by-file deliverables, definition of done per component, exit criteria in Section 10, benchmarks, risks. |
| `docs/core/architecture.md` | Core runtime internal architecture (Phase 1 deliverable, populated as milestones complete). |
| `docs/adapter-sdk/*.md` | Adapter SDK guides (Phase 1 deliverable, populated alongside A2). |
| `docs/benchmarks/phase1-baseline.md` | Captured benchmark results (Phase 1 deliverable, populated in Week 9). |

**Rule:** If you find yourself making a decision that isn't covered by the blueprint, stop and ask whether it should be added to the blueprint before implementing it.

### Decision records, platform principles, and session handoffs

Beyond the formal documents above, three lighter-weight mechanisms capture context between sessions:

**`docs/decisions/`** — Architecture Decision Records (ADRs). One short markdown file per locked architectural decision, numbered sequentially (e.g., `0003-fail-soft-startup-is-default.md`). Each contains: context, decision, reasoning, consequences. **Before making an architectural choice, scan `docs/decisions/` first — these are locked unless the user explicitly invokes a decision number to revisit. Do not relitigate them.**

**`docs/platform-principles.md`** — Six cross-milestone commitments shaping every architectural and UX decision (P1 Runtime Tap is observational; P2 Shared interaction primitives; P3 Security spec-first; P4 Preserve explainability data path; P5 EREMOS V2 is market identity; P6 Operational product, not developer tool). **Read this before any architectural or product-direction decision.** If a design choice would violate one of these principles, pause and surface the conflict — never silently work around it. The document is governed by an explicit "when to amend" clause; do not treat its principles as routine debate material.

**`docs/sessions/`** — Per-session handoff notes. Date-stamped markdown files (e.g., `2026-05-15-mp21-phase3a-handoff.md`) capturing transient context that didn't fit into commit messages: what's pending, in-flight state on disk, decisions locked in the session but not yet promoted to an ADR. **At the start of any new session, read the most recent file in `docs/sessions/` before diving in.**

When you make a decision that meets the bar for an ADR (architecturally locked, painful to reverse, generalises beyond one milestone), propose adding it to `docs/decisions/` with the next sequential number. When you end a session with locked decisions or pending work, propose adding a `docs/sessions/<date>-<milestone>-handoff.md` file before signing off. Platform-level commitments rarely emerge; they belong in `docs/platform-principles.md` only after a strategic review pass and with explicit user approval.

---

## 3. Architectural Locks (Do Not Negotiate These)

These are from `ARCHITECTURE_BLUEPRINT.md` Appendix A "Architecturally Locked Decisions." Any PR that violates these is wrong by definition.

1. **Protocol-agnostic core.** `ElpisEdgeConnect.Core` never references any protocol module. Adapters reference Core, not the other way around.
2. **Canonical data model.** All device data becomes `CanonicalDataPoint` before routing. No adapter formats payloads for specific sinks.
3. **Route-first design.** Routes are the primary product concept, not a config footnote. One source can fan out to many sinks.
4. **Modular assemblies, not dynamic plugins.** Protocols are compile-time assemblies (`ElpisEdgeConnect.Sources.Focas2`, etc.), activated by license at DI registration time.
5. **Three-layer licensing.** Packaging (per-edition installers) + runtime activation (signed license file) + UI/API enforcement. All three must be enforced.
6. **License signature.** RSA-signed JSON license, fully offline, no phone-home. Public key embedded in binary; private key held externally.
7. **License expiration behavior.** Continue data flow, block config changes. Never cut customer data to enforce licensing. **⚠️ Partially overridden by ADR-0035** (owner-approved): when the license status is not `Valid` for longer than a trial window (default 2h), the runtime now stops the application. See `docs/decisions/0035-unlicensed-runtime-cutoff.md`.
8. **Store-and-forward is mandatory.** Per-route SQLite storage with per-sink cursors. Non-negotiable for edge.
9. **Fanout semantics.** Independent per sink, not transactional. A failing sink never blocks a healthy sink.
10. **Per-adapter isolation.** One failing adapter never affects any other adapter, route, or sink.
11. **Replay ordering.** Sequential per sink; live messages wait for drain during recovery.
12. **Delivery modes.** `AtMostOnce` and `AtLeastOnce` only in v1. `ExactlyOnce` is rejected at config validation time. **Broker-acknowledged `AtLeastOnce` is available only where the destination protocol exposes a positive broker/application acknowledgment.** A sink advertises an acknowledgment boundary (`None | LocalTransport | Broker | Application`) and a route declares its `RequiredAcknowledgementBoundary`; **validation rejects a route whose required boundary exceeds the sink's advertised boundary**. A local-transport-only destination (e.g. Sparkplug B v1, QoS 0) supports durable store-and-forward but cannot satisfy a `Broker`/`Application` requirement. (Amended 2026-07-13 — see `ARCHITECTURE_BLUEPRINT.md` §19.7 and ADR-0036.)
13. **Sink capabilities.** Contracts support Push and Pull modes from day one (OPC UA Server forward-compatible).
14. **AI in decision-support only, never in data path.** The pipeline is deterministic, testable, replayable. AI agents propose; humans decide.
15. **AI tool-use pattern.** Agents interact via structured tool calls to the management API, not free-text code generation.
16. **AI state changes.** Always proposed, never autonomous. User confirmation required in the chat interface.
17. **AI local-LLM support.** Mandatory from day one. Cloud LLMs are optional, not required.
18. **Diagnostics.** Three-way (source / pipeline / sink), always.
19. **Gateway identity.** Per-gateway UUID + customer/site binding, established at first start.

See `ARCHITECTURE_BLUEPRINT.md` Sections 2-19 for full details and Appendix A for the complete locked/flexible/open decision table.

---

## 4. Repository Layout

```
C:\dev\EdgeConnect\
├── ElpisEdgeConnect.sln                     Solution file
├── CLAUDE.md                                This file
├── REVIEW.md                                Code review checklist
├── .editorconfig                            Style + nullable-as-error enforcement
├── .gitignore
│
├── docs/
│   ├── ARCHITECTURE_BLUEPRINT.md            ← Master architecture reference (locked)
│   ├── PHASE1_EXECUTION_PLAN.md             ← Phase 1 engineering plan (locked)
│   ├── core/                                Core runtime internal docs (populate as built)
│   ├── adapter-sdk/                         Adapter SDK guides (populate with A2)
│   ├── benchmarks/                          Benchmark baselines (Week 9+)
│   ├── config-schemas/                      Generated JSON schemas (B1+)
│   └── licensing/                           License format spec (B3)
│
├── src/
│   ├── ElpisEdgeConnect/                    Legacy migrated FanucCncDataBridge code.
│   │                                        Refactored into new modular structure
│   │                                        during Phase 2. Do not add new Phase 1
│   │                                        code here.
│   │
│   └── ElpisEdgeConnect.Core/               ← Phase 1 foundation. All new Phase 1
│                                              code lives here unless the plan
│                                              says otherwise.
│       ├── Adapters/                        ISourceAdapter, ISinkAdapter, state machine
│       ├── Buffer/                          IMessageBuffer, SQLite storage (C2a/C2b)
│       ├── Configuration/                   Config models, manager, draft/apply/rollback (B1/B2)
│       ├── Diagnostics/                     3-way diagnostics collector (C4)
│       ├── Errors/                          Error taxonomy (A3)
│       ├── Licensing/                       License manager, signature validation (B3)
│       ├── Model/                           CanonicalDataPoint and friends (A1)
│       ├── Pipeline/                        Transform pipeline + 4 steps (C1)
│       ├── Routing/                         Routing engine, fanout, replay (C3)
│       └── Security/                        Secrets handling
│
└── tests/
    ├── ElpisEdgeConnect.Core.Tests/         xUnit + FluentAssertions + NSubstitute
    └── ElpisEdgeConnect.Benchmarks/         BenchmarkDotNet
```

---

## 5. How to Build and Test

Always operate from the repository root (`C:\dev\EdgeConnect\`). Use absolute paths in Bash commands to avoid cwd drift.

```bash
# Full solution build (should be 0 warnings, 0 errors)
dotnet build ElpisEdgeConnect.sln

# Build Core only
dotnet build src/ElpisEdgeConnect.Core/ElpisEdgeConnect.Core.csproj

# Run Core unit tests
dotnet test tests/ElpisEdgeConnect.Core.Tests/ElpisEdgeConnect.Core.Tests.csproj --nologo

# Run benchmarks (Release config only)
dotnet run --project tests/ElpisEdgeConnect.Benchmarks --configuration Release -- --filter '*CanonicalDataPoint*'
```

**Build expectations (locked):**
- `TreatWarningsAsErrors=true` on `ElpisEdgeConnect.Core` — no exceptions
- Zero analyzer warnings at Error level
- Nullable reference types enabled project-wide
- All public APIs in Core have XML doc comments

**Test expectations:**
- Every unit test is deterministic. No `Thread.Sleep`. Use `TaskCompletionSource` or time abstractions when awaiting async signals.
- Integration tests under `tests/ElpisEdgeConnect.Integration.Tests/` use mock adapters, never real protocols.
- Code coverage on `ElpisEdgeConnect.Core` must reach ≥80% by end of Phase 1.

---

## 6. Decisions Locked in Week 1

These were confirmed at Phase 1 kickoff and should not be relitigated without user approval:

| Area | Choice |
|------|--------|
| SQLite library | `Microsoft.Data.Sqlite` |
| JSON Schema | `NJsonSchema` |
| Test framework | xUnit |
| Assertions | FluentAssertions |
| Mocking | NSubstitute |
| Benchmarks | BenchmarkDotNet |
| License signing keys (Phase 1) | RSA keypair held in password manager; public key embedded in binary. Migrate to HSM in Phase 4. |
| Metrics | `System.Diagnostics.Metrics` + Prometheus exporter |
| Target framework | .NET 8 |
| Host platform | Windows service (Linux later) |
| Project layout | Core and tests side-by-side under `src/` and `tests/`; legacy `ElpisEdgeConnect` project retained for Phase 2 migration reference |

---

## 7. Working Conventions

### Code style

- Namespaces match folder structure: `ElpisEdgeConnect.Core.Model`, `ElpisEdgeConnect.Core.Adapters`, etc.
- Private fields use `_camelCase` prefix.
- Records are `sealed` unless there's an explicit reason to allow inheritance.
- Use `required` init properties for mandatory fields, not constructor parameters.
- No `async void` except event handlers.
- Library async calls use `ConfigureAwait(false)` where applicable.
- Error codes follow `MODULE.CATEGORY_SUBCATEGORY` naming (`CORE.CONFIG_INVALID`, `FOCAS2.HANDLE_EXHAUSTED`).

### Documentation

- Every source file starts with a header comment identifying the file, its purpose, and the blueprint section it implements.
- Every public member in Core has XML doc comments.
- Any file that implements a LOCKED architectural decision must explicitly say so in its header comment.

### Testing

- Test class names: `{ClassUnderTest}Tests`.
- Test method names: `MethodName_Condition_ExpectedResult`.
- Arrange-Act-Assert with blank lines separating phases.
- One logical assertion per test where possible; use `Theory` + `InlineData` for combinatorial coverage.
- Every locked architectural requirement must have at least one named test that fails if the requirement is violated.

### Commits

- **Never commit without explicit user instruction.** The user controls commit cadence.
- When the user asks for a commit, follow the commit instructions in the default Claude Code instructions (no self-authored `--amend`, no hooks skipped, etc.).
- Commit messages describe *why*, not *what*, in 1-2 sentences.

### Session hygiene

- Check `docs/PHASE1_EXECUTION_PLAN.md` Section 10 before declaring any milestone complete. That is the authoritative exit checklist.
- Update the todo list as you go; mark items completed immediately, not in batches.
- If a decision you're about to make isn't covered by the blueprint or Phase 1 plan, **stop and surface it to the user** rather than silently choosing.

---

## 8. Phase Status

> **Refreshed 2026-05-31.** The original linear phase plan (below) no longer maps
> cleanly — adapters, the management Studio, and operability/explainability
> surfaces have advanced together. Read the table as "what each phase covered";
> the **Current state** section underneath is the authoritative snapshot.

> **"Done" means operator-shippable, not just "code exists."** An adapter is
> only complete when its add-source/-destination **wizard tile is Available** in
> the Studio (`SourceProtocolPickerModel` / `DestinationProtocolPickerModel`).
> Several adapters have backend code + tests + Host DI registration but no
> wizard yet — those are **backend-only**, NOT done.

| Phase | Status | What It Covers |
|-------|--------|----------------|
| **Phase 1** | **CLOSED** (`v0.1.0-phase1` tag, commit `342c6bb`) | Core foundation: canonical model, contracts, pipeline, routing, SQLite store-and-forward, licensing, diagnostics, host skeleton, mock adapters |
| **Phase 2** | **In progress** | Migrate existing adapters. **Operator-available:** FOCAS2, MTConnect, Brother HTTP, MQTT sink ✓. **MT-LINKi** has no adapter project at all |
| **Phase 3** | **In progress** | **Operator-available:** Modbus TCP, OPC UA Client, OPC UA Server ✓. Store-and-forward ✓ (Phase 1). License module catalog + DI enforcement ✓ (G.7). Siemens S7 operator-available ✓ (M.2b.2, 2026-06-04). Mitsubishi MELSEC operator-available ✓ (Slice 1, read-only, 2026-07-02; stacked PR chain). **Not started:** HTTP sink, TCP sink, EtherNet/IP (stub only), edition installers |
| **Phase 4** | **Substantially done** | Management REST API + Blazor admin Studio ✓: config draft/apply/rollback, 3-way diagnostics, backup, diagnostic bundle (ADR-0020), onboarding wizards, universal secret redaction. **Not done:** Documentation Copilot |
| **Phase 4.5** | **Not started** | Interactive AI agents (Diagnostic, Configuration, Tag Mapping, Intelligent Alerting) |
| **Phase 5** | **Partial** | OPC UA Client + Server operator-available ✓. **Not done:** advanced transforms, fleet management |

### Current state (2026-05-31)

- **Source protocols — operator-available** (add-source wizard tile = Available):
  **Modbus TCP**, **FANUC FOCAS2** (incl. demo mode), **MTConnect** (browse-driven
  semantic-onboarding wizard, M.2b.4), **Brother HTTP**, **OPC UA Client**,
  **Siemens S7** (manual tag-address editor, M.2b.2 — completed 2026-06-04; tile
  Available, `AddS7Source.razor` + edit routing, verified via live config-apply +
  adapter-Running. See `docs/sessions/2026-06-03-s7-source-wizard-handoff.md`),
  **Mitsubishi MELSEC** (hand-rolled SLMP / MC 3E binary over TCP, read-only
  Slice 1; manual tag-address editor `AddMelsecSource.razor` + edit routing,
  license-gated tile, planner-driven test-connection/test-read probes, and an
  observational SourceDetail diagnostics panel. Lands via the stacked PR chain
  #163 backend → UI PR. See `docs/sessions/2026-07-01-melsec-wizard-ui-plan-v2.md`.
  A-2O added a **PLC family profile selector** (Modern iQ-R/Q/L default, iQ-F/FX5)
  driven by the `MelsecProfiles` registry — iQ-F is operator-Supported but
  **Field-qualified: pending hardware**; claims stay profile-aware, never
  "all Mitsubishi PLCs". See `docs/decisions/0034-melsec-profile-matrix-strategy.md`).
- **Source protocols — backend-only, wizard Pending (NOT shippable yet):** none
  currently (S7 + MELSEC shipped).
- **Destination protocols — operator-available:** **MQTT** (Batch + PerTag,
  EREMOS V2 topic `eremos/{gatewayId}/cnc/{sourceId}/{tagName}`), **OPC UA Server**.
  Pending: HTTP webhook, TCP.
- **Stub / not started:** **EtherNet/IP** (`src/…Sources.EthernetIp/` is only
  `AssemblyMarker.cs`).
- **Near-term adapter roadmap:** EtherNet/IP (new). (S7 wizard shipped 2026-06-04;
  deferred S7 follow-ups: CSV tag import v1.1, demo mode, optimized-DB walk.
  MELSEC Slice 1 shipped 2026-07-02; deferred MELSEC follow-ups per plan-v2:
  writes, UDP/4E/1E, ASCII, browse, demo mode, CSV import.)
- **Management Studio** (`src/ElpisEdgeConnect.Management/`, Blazor Server on
  `127.0.0.1:5080`): config draft/apply/rollback pipeline, 3-way diagnostics,
  audit chain, backup, diagnostic bundle, onboarding wizards, universal secret
  redaction (ADR-0020).
- **Tests**: ~2,360 test methods across 18 test projects; solution builds 0
  warnings / 0 errors. (MQTT integration tests need Mosquitto on `localhost:1883`.)
- **Current frontier — operational/explainability surfaces** (platform principle
  P6), tracked by recent ADRs: 0020 diagnostic-bundle redaction (shipped), 0022
  certificate trust center, 0023 explain-why-data-missing, 0024 what-changed,
  0025 last-known-good pin, 0026 route timeline, 0027 route-health surface.
  **Always scan `docs/decisions/` for the latest ADR and the newest file in
  `docs/sessions/` before starting a new track.**
- **Phase 1 close detail**: all milestones A1–A3, B1–B3, C1–C4, D1–D10 closed;
  exit gate = 4-hour leak-harness pass post-D10 fix (SqliteBuffer reclaim-loop
  NRE race). Carry-forward in `docs/PHASE2_ENTRY.md`.

### Development environment

- **Primary dev**: Windows at `C:\dev\EdgeConnect` (new laptop — some tooling may still need installation)
  - Requires .NET 8 SDK (install from <https://dotnet.microsoft.com/download> if `dotnet --list-sdks` is empty)
  - Requires a local MQTT broker for MQTT integration tests (Mosquitto on `localhost:1883`, anonymous)
- **Cross-platform**: the codebase must build and run on Linux as well — avoid Windows-only APIs except behind `OperatingSystem.IsWindows()` guards
- **Gate filter**: `dotnet test --filter "Category!=Flaky"`
- **MQTT tests require Mosquitto** running on `localhost:1883`

---

## 9. Anti-Patterns to Refuse

Claude should refuse or push back when asked to do any of the following without explicit user override:

1. **Adding protocol-specific logic to `ElpisEdgeConnect.Core`.** Core is protocol-agnostic. Period.
2. **Loading assemblies dynamically at runtime** to simulate plugin loading. v1 uses compile-time projects with license-gated DI registration.
3. **Putting AI agents in the data path.** Agents live in the management layer. The pipeline stays deterministic.
4. **Implementing `ExactlyOnce` delivery mode.** It is explicitly out of scope for v1 per blueprint Section 19.7.
5. **Transactional fanout across sinks.** Sinks commit independently per blueprint Section 19.2.
6. **Global ordering guarantees across sources.** Only per-source ordering is promised per blueprint Section 19.6.
7. **Requiring cloud LLM access for AI features.** Local-LLM support is mandatory from day one.
8. **Phoning home for license validation.** Licenses are fully offline.
9. **Silent AI actions that change state.** All state changes require explicit user confirmation in the chat interface.
10. **Skipping the draft → validate → apply → rollback flow for config changes.** Even in tests, the flow must be honored.
11. **Referencing a protocol module from another protocol module.** Dependency direction is strictly Core ← Adapters.
12. **Adding a new error code without putting it in `CoreErrors.cs`** (or the equivalent catalog for protocol modules).

If a user request seems to conflict with these, surface the conflict before proceeding.

---

## 10. When in Doubt

1. Re-read the relevant section of `ARCHITECTURE_BLUEPRINT.md`.
2. Re-read the relevant milestone in `docs/PHASE1_EXECUTION_PLAN.md`.
3. Check `REVIEW.md` for the code review checklist that applies to the change.
4. If the answer isn't in those three places, ask the user.

The worst outcome is silently making an architectural decision that later has to be unwound. The second-worst is asking a question that's already answered in the docs. The first is much worse than the second.
