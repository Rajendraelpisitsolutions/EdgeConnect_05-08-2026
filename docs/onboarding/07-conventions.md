# 07 — Conventions

Read this before opening your first PR.

## Branching

```
master            Always green. Squash-merge target for every PR.
claude/<thing>    Working branches Claude Code creates. Same shape applies to human dev branches.
feature/<thing>   Optional human convention; either pattern is fine.
```

Never push to `master` directly. Open a PR even for trivial doc fixes — review is part of the audit trail.

## Commit cadence

- Commit at clean milestone boundaries (helper done, test suite passing, etc.) — not every save.
- Subject line: `type(scope): summary in under 70 chars`. Examples from recent history:
  - `feat(chip3): PR I-1 - BulkSourceMergeService + handlers + 117 tests`
  - `fix(chip3): generate.ps1 - resolve OutDir to absolute path`
  - `docs(sessions): Bulk-Provision UI Phase 1 v3.1 LOCKED`
- Body: 1-2 sentences on *why*, not *what*. The diff shows what.
- Co-Authored-By trailer for paired or assisted work.

## PR conventions

- Use `gh pr create` from the branch.
- Title mirrors the most recent commit (or summarizes the whole branch when squashing).
- Body has a `## Summary` section + a `## Test plan` checklist.
- Squash-merge on green. The repo's default merge style is squash; merge-commits aren't used.
- Never `--no-verify` on commit and never `--force` push to `master`.

## Code style

- Namespaces match folder structure: `ElpisEdgeConnect.Core.Model`, `ElpisEdgeConnect.Core.Adapters`, etc.
- Private fields use `_camelCase` prefix.
- Records are `sealed` unless inheritance is justified.
- Use `required` init properties for mandatory fields.
- No `async void` except event handlers.
- Library async calls use `ConfigureAwait(false)` where applicable.
- Error codes follow `MODULE.CATEGORY_SUBCATEGORY` (`CORE.CONFIG_INVALID`, `FOCAS2.HANDLE_EXHAUSTED`).

## File header

Every source file in `src/` starts with a header comment:

```csharp
// ============================================================================
// File: Path/Of/File.cs
// Purpose: One-sentence description.
// Reference: ARCHITECTURE_BLUEPRINT.md §X, sessions/YYYY-MM-DD-foo.md
// ============================================================================
```

Files implementing a LOCKED architectural decision say so explicitly in the header.

## Documentation

- Every public Core API has XML doc comments.
- New ADRs go in `docs/decisions/` with the next sequential number. Format: context → decision → reasoning → consequences.
- New session handoffs go in `docs/sessions/<YYYY-MM-DD>-<topic>-handoff.md` when a session locks decisions or leaves in-flight work.
- Platform principles in `docs/platform-principles.md` are amended rarely; require explicit owner sign-off.

## Testing

- Test class names: `{ClassUnderTest}Tests`.
- Test method names: `MethodName_Condition_ExpectedResult`.
- Arrange-Act-Assert with blank lines separating phases.
- Every locked architectural requirement has at least one named test that fails if violated.
- Tests are deterministic. No `Thread.Sleep`. Use `TaskCompletionSource` or time abstractions.

## What to refuse (do not negotiate without explicit owner override)

`CLAUDE.md` §9 lists the full set. Highlights:

1. Adding protocol-specific logic to `ElpisEdgeConnect.Core`.
2. Loading assemblies dynamically at runtime (v1 uses compile-time projects).
3. Putting AI agents in the data path. Agents propose; humans decide.
4. Implementing `ExactlyOnce` delivery mode (explicitly out of scope for v1).
5. Transactional fanout across sinks (independent per blueprint §19.2).
6. Phoning home for license validation.
7. Silent AI actions that change state.
8. Skipping the draft → validate → apply → rollback flow for config changes.

If a request seems to conflict with these, surface the conflict before proceeding.

## When you finish your first task

Open the PR. Squash-merge on green. Then write a short session handoff under `docs/sessions/` if your work locked any decisions or left context the next contributor will need.

## Done?

Continue to [08-troubleshooting.md](08-troubleshooting.md) if anything is currently broken.
