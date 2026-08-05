# Elpis EdgeConnect — Developer Onboarding

Welcome. This page is the entry point for a new contributor on a fresh machine.

If you only have 60 seconds, do this:

```pwsh
cd C:\dev\EdgeConnect
.\scripts\dev\bootstrap.ps1
```

The bootstrap script checks your prerequisites, restores packages, runs a full build, and runs the unit test suite. If everything passes, you're ready to read the full guide below.

---

## Full guide — `docs/onboarding/`

| Step | Topic | When to read |
|---|---|---|
| 00 | [Prerequisites](docs/onboarding/00-prerequisites.md) | Before anything else |
| 01 | [Clone, build, test](docs/onboarding/01-clone-build-test.md) | First hour |
| 02 | [Dev license](docs/onboarding/02-dev-license.md) | Before running the Studio |
| 03 | [Dev gateway config](docs/onboarding/03-dev-config.md) | Before running the Studio |
| 04 | [Running the Studio](docs/onboarding/04-running-studio.md) | First Studio session |
| 05 | [MQTT integration tests](docs/onboarding/05-mqtt-integration-tests.md) | When touching sinks / EREMOS contract |
| 06 | [Codebase tour](docs/onboarding/06-codebase-tour.md) | Read while waiting on builds |
| 07 | [Conventions](docs/onboarding/07-conventions.md) | Before your first PR |
| 08 | [Troubleshooting](docs/onboarding/08-troubleshooting.md) | When something breaks |

---

## Background you should also read

These are project-wide documents the rest of the team treats as ground truth:

- `CLAUDE.md` — repo-level conventions, locked architectural decisions, anti-patterns. **Read first.**
- `docs/ARCHITECTURE_BLUEPRINT.md` — master architecture. Appendix A lists every locked decision.
- `docs/PHASE1_EXECUTION_PLAN.md` — closed; useful as a map of how Phase 1 was built.
- `docs/platform-principles.md` — six commitments shaping every design call.
- `docs/decisions/` — Architecture Decision Records. Locked unless explicitly revisited.
- `docs/sessions/` — per-session handoff notes capturing in-flight context.

---

## First-day expectation

You should be able to:

1. Build the solution (0 warnings, 0 errors).
2. Run the unit test suite (~2,500 tests pass).
3. Open the Studio at `http://127.0.0.1:5080/` and navigate the configured demo gateway.
4. Find the issue tracker / ADR / blueprint section relevant to your assigned task.

If anything blocks you for more than 20 minutes, ping the repo owner. Onboarding friction is a bug; we want the report so we can fix the docs.
