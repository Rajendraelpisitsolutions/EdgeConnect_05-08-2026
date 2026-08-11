# ADR-0006: System-actor audit-chain entries for runtime-observed faults

**Status:** Accepted
**Date:** 2026-05-15
**Milestone:** M.P2.1

## Context

The audit chain (`ConfigurationAuditLog`) was designed to record
operator-driven configuration changes: drafts created, drafts
applied, rollbacks. Each entry carries an `Actor` field with the
operator's identity (or `"system"` as the unauthenticated default
when no actor is supplied).

ADR-0004 introduced runtime-observed faults that need a durable
record. The question: should these faults be written to the audit
chain, or to a separate log?

## Decision

Runtime faults DO get written to the audit chain, but as a
**clearly distinct entry kind**:

- New action: `ConfigurationAuditAction.RuntimeConfigurationFault = 4`
- Actor: literally `"system"` (do NOT pretend these are operator
  changes)
- New optional field on `ConfigurationAuditEntry`:
  `RuntimeFault: ConfigurationFault?` (the structured fault record)
- VersionId: the configuration version against which the fault was
  observed (so forensic queries can pin "this fault came from THIS
  version")

The pre-existing actions (`DraftCreated`, `DraftDiscarded`,
`Applied`, `RolledBack`) remain operator-driven. The chain remains
hash-chained end-to-end; the new optional field uses
`WhenWritingNull` serialization so legacy entries' JSON is
byte-identical and the hash chain verifies cleanly across the
schema extension.

## Reasoning

1. **One forensic source of truth.** Adding a separate log would
   force operators (and tooling) to cross-reference two streams to
   reconstruct gateway history. The chain absorbs both
   operator-driven changes and runtime observations against those
   changes — the natural shape.

2. **Distinction matters.** Per the ChatGPT review pass:
   "system-generated audit entries are valid, but separate the
   actor clearly — do not pretend these are operator changes." The
   explicit `actor="system"` + dedicated `RuntimeConfigurationFault`
   action satisfies this. Future audit-chain consumers can filter
   on either dimension.

3. **Hash chain stability is non-negotiable.** A schema extension
   that broke chain verification would require either a chain
   migration (painful) or a v2 chain file alongside v1 (worse).
   `WhenWritingNull` makes the extension byte-additive for new
   entries and byte-neutral for old ones; verifyChain stays clean.

## Consequences

- `IConfigurationManager.AppendRuntimeFaultAsync(fault, ct)` is
  the new write entry point. Acquires the same mutex as
  draft/apply/rollback so concurrent writes serialise correctly.

- `HostStartup` drains the in-memory `IConfigurationFaultRegistry`
  into the audit chain AFTER `InitializeAsync` completes (the
  chain isn't writable any earlier). Exceptions during the drain
  are caught and logged — never escalated to a gateway crash, or
  fail-soft itself would be defeated.

- `GET /api/v1/diagnostics/audit-chain` continues to verify the
  full chain. Pre-M.P2.1 hosts that boot a config containing only
  legacy entries verify unchanged. M.P2.1+ hosts can have mixed
  legacy + `RuntimeConfigurationFault` entries; chain verifies
  through both.

- Filtering by actor in chain-replay tools: `actor != "system"` =
  operator history; `actor == "system"` AND
  `action == RuntimeConfigurationFault` = system observations
  against config.

## References

- Implementation: M.P2.1 phase 1 (`6d85f38`) —
  `ConfigurationAuditEntry.cs`, `ConfigurationManager.cs`
- ADR-0005 (faults are runtime state) — paired with this ADR
- Pre-existing: `ConfigurationAuditLog.cs` (SHA-256 chain
  semantics)
