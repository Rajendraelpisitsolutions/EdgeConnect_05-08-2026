# ADR-0018A: Tap Value Privacy Policy — explicit sensitive-tag allowlist, masked at capture

**Status:** Accepted (2026-06-01)
**Date:** 2026-06-01
**Framing:** Sub-decision of ADR-0018 (Live Data Tap). ADR-0018 Rule 6 requires
that sensitive tag VALUES are masked **at capture time**, but the policy that
decides *which* values are sensitive did not exist. This ADR defines it: an
explicit operator allowlist of tag-name patterns, no value heuristics. It is the
**prerequisite** that gates the tap capture hooks (Live Tap M2) — no point is
captured into a diagnostic ring until masking is in place.

## Context

The existing redaction machinery (`ConfigRedactionEngine`, `SecretShapeDetector`)
masks **configuration** secrets (passwords, keys) using typed-field rules and
shape heuristics. It cannot be reused for live data values:

- Config secrets are structural (a `Password` field); live values are domain
  data whose sensitivity is **operator knowledge**, not inferable from shape.
- A heuristic that guesses "this OT value looks sensitive" produces false
  positives (masking data the operator needs) and, worse, false *negatives* that
  build false trust ("the tap masks secrets" when it silently missed one).

ADR-0017 Rule 7 already locks **mask at capture, not at render** — a mistaken
render path must not be able to leak data the capture path never accepted.

## Decision

### Rule 1 — Explicit allowlist, no heuristics

Sensitivity is decided by an explicit operator-configured allowlist of tag-name
patterns: `gateway.sensitiveTags`. There is **no** entropy/shape/token heuristic
for live values. `SecretShapeDetector` stays a config-only tool.

### Rule 2 — Pattern forms

Each entry is matched **case-insensitively** against
`CanonicalDataPoint.TagName`:

- **Exact** — `recipe/secret_setpoint`
- **Glob** — `*` matches any run, `?` matches one char (`recipe/*`, `tag?`)

Source/route-scoped qualifiers (mask a tag only on a specific source) are a
deferred extension — v1 matches on tag name alone. (Tag names already contain
`/`, so a `sourceId/tag` scoping form would be ambiguous with tag paths; defer
until a real need with an unambiguous shape.)

### Rule 3 — Value-only mask, at capture

A point whose tag matches is captured with **only its value** replaced by
`***`. `valueType`, `quality`, `deviceTimestamp`, `gatewayTimestamp`,
`tagName`, identity, and metadata are preserved — the operator still sees that
the point flowed, when, and with what quality; just not the value. Masking
happens inside the capture path (`TapValueMasker.Mask`, wired into
`RouteTap`), before the point enters any ring. Cleartext never lives in a tap
buffer or a snapshot export.

### Rule 4 — Diagnostics-only, never the data path

This policy affects DIAGNOSTIC surfaces only. It never alters the runtime data
path — sinks still deliver the real value. Masking a value in the tap does not
mask it on the wire.

### Rule 5 — The patterns are not secret

The patterns themselves (tag names/globs) are configuration, not secrets, and
are `[BundleTier.Include]` — they appear in diagnostic bundles so support can
see what was masked and why.

## Consequences

**Positive:**
- Zero false positives/negatives from heuristics — the operator declares exactly
  what is sensitive.
- Cleartext sensitive values provably never enter a diagnostic buffer (capture-
  time masking) — a render bug cannot leak them.
- Tiny, testable surface: a policy matcher + a value-only masker.

**Negative:**
- The operator must know and configure their sensitive tags. Acceptable —
  sensitivity is domain knowledge the heuristic can't replace; the default
  (empty) masks nothing, which is the correct fail-*visible* default for a
  diagnostic the operator opted into.
- Source/route-scoped masking is deferred (Rule 2).

**Forbidden patterns:**
- Any value heuristic / entropy detector applied to live process data.
- Masking at render instead of capture.
- Letting `gateway.sensitiveTags` influence what a sink delivers.

## Reference

- ADR-0018 (Live Data Tap) Rule 6 — this resolves its open masking source-of-truth
- ADR-0017 Rule 7 — mask at capture, not render
- `SensitiveTagPolicy`, `TapValueMasker`, `GatewaySettings.SensitiveTags`
- `docs/sessions/2026-06-01-live-data-tap-plan-v2.md` — M1.5 (this is its blocker)
