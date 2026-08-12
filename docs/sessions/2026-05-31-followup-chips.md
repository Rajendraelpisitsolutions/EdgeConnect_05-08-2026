# Follow-up chips — 2026-05-31 (ADR-0020 bundle / G5)

Spawned as CCD task chips during the G5 session. Captured here because chips can
be dismissed and don't reappear.

---

## 1. Gateway data-dir must be writable by the runtime account (Windows ProgramData ACLs)

**Severity:** medium — surfaces as a hard failure on the first audit-writing
operation, but only under specific (common) Windows permission setups.

**What we hit (live, G5):**
Generating a diagnostic bundle threw
`UnauthorizedAccessException: Access to the path
'C:\ProgramData\EdgeConnect\config\history\audit.log' is denied.`

**Root cause (confirmed via Get-Acl):**
- `C:\ProgramData\EdgeConnect` and its sub-*directories* grant `Users` Write
  (so creating *new* files — history versions, drafts, temp files — works).
- Existing *files* (`audit.log`, `current.json`) were created under an
  elevated/admin context and grant `Users` only `ReadAndExecute` — no Write.
- The runtime appends `audit.log` **in place** (`FileMode.Append` on the
  existing file), which needs Write on the file → denied for a non-admin
  runtime user.

This is not bundle-specific: any audit-writing op (config apply / rollback /
draft) hits the same wall. It just hadn't been exercised in the non-elevated
session until the bundle tried to append.

**Immediate operator workaround (used):** elevated
`icacls "C:\ProgramData\EdgeConnect" /grant "<user>:(OI)(CI)M" /T`, or relocate
via `EDGECONNECT_DATA_ROOT` to a user-writable folder.

**What to actually fix (product):**
- First-run / auto-provision (and/or the Phase 3 Windows-service installer)
  should ensure the data root + the files it creates are writable by the
  runtime account — set a permissive ACL on the data dir at provision time, or
  have the runtime set it on files it creates under ProgramData.
- Add a startup pre-flight check: verify `audit.log` (and `current.json`) are
  append/writable by the current identity; if not, surface a clear
  `CORE.CONFIG_DATA_NOT_WRITABLE`-style fault at startup rather than failing
  deep inside the first write.
- Consider a one-line ops-runbook note (§ data dir permissions).

**Out of scope to change now (flagged, not decided):** bundle generation stays
fail-closed if the `BUNDLE.GENERATED` audit append fails (ADR-0020 Rule 5). An
unwritable audit log is a genuine gateway fault and should fail loudly, not be
silently swallowed.
