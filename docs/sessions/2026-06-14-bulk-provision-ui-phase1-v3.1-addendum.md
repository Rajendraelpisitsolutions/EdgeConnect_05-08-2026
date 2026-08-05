# Bulk-Provision UI Phase 1 — v3.1 implementation addendum

**Date:** 2026-06-14
**Author:** Claude (post-v3-review)
**Status:** **LOCKED (patched)** — implementation refinements for `BulkSourceMergeService`. Does NOT reopen the v3 product design.
**Predecessor:** `docs/sessions/2026-06-14-bulk-provision-ui-phase1-v3-lock-final.md`
**Reviewer verdict:** "Approve v3 architecture amendment, request small v3.1 implementation addendum before PR I-1." Initial v3.1 (commit `5f045bf`) received a follow-up "approve v3.1 directionally, with a small implementation-contract patch" — patched in-place; see §13 for the change log.

---

## 0. Scope of this addendum

v3's product direction and architecture stand. v3.1 locks ten implementation-level details before `BulkSourceMergeService` coding begins. **Nothing here changes Phase 1 scope or the Phase 1 / 1.1 / 2 split.**

Per ChatGPT: **PR M and PR I-0 can proceed against v3 as-is. PR I-1 waits for this addendum.**

The ten locks below address specific concerns ChatGPT raised — each is a small, concrete decision that shapes how the merge service is built but not what it does.

---

## 1. API surface — resolve the v3 wording contradiction

**v3 contradiction:** §8 said "PR I-1 = BulkSourceMergeService + API endpoint + DTOs" while §10 said "No new API surface needed; reuse existing draft creation." Both can't be true.

### Locked surface

```text
BulkSourceMergeService.cs  (internal management-layer service)
    Preview inputs:
        protocolId
        csvBytes
        selectedSinkInstanceId  (required when 2+ enabled MQTT sinks exist; null when 1)
        importLabel             (optional)
        sourceNamePrefix        (optional)
    Preview returns: MergePreviewResult
        baseConfigHash
        validatedRows
        warnings, errors
        chosenSinkInstanceId (echo)

    Submit inputs:
        protocolId
        csvBytes               (RE-SENT by client, server re-parses)
        selectedSinkInstanceId
        importLabel, sourceNamePrefix
        baseConfigHash         (from preview)
    Submit returns: DraftId

IConfigurationManager.CreateDraftAsync(GatewayConfiguration draft)
    UNCHANGED. Existing public API. The merge service calls it
    after building the merged GatewayConfiguration in memory.

Razor-facing handler (NOT a public REST API for external clients):
    POST /management/sources/bulk-preview  -> returns MergePreviewResult
    POST /management/sources/bulk-submit   -> returns DraftId

These are Razor-driven management endpoints, NOT additions to the
public config-drafts API surface. They live alongside the existing
Razor-driven management handlers per the Studio's existing pattern.
```

**No new draft API. No new external integration surface.** The merge service is purely internal; draft creation reuses the existing pipe.

### Submit-replay safety (LOCKED)

**Submit MUST NOT trust client-supplied generated source / route objects from the preview response.** A hostile client could fake a preview and submit a crafted draft.

Locked behavior:

```text
Preview is stateless on the server. The MergePreviewResult is
informational only.

Submit re-sends:
    - the original csvBytes
    - protocolId, selectedSinkInstanceId, importLabel, sourceNamePrefix
    - baseConfigHash captured during preview

Submit server-side:
    1. re-read current applied GatewayConfiguration
    2. compute current baseConfigHash; reject if != submitted hash
    3. re-parse the resent csvBytes
    4. re-run the full merge + JSON-safe substitution
    5. re-run full schema validation on the merged GatewayConfiguration
    6. ONLY THEN invoke CreateDraftAsync
```

The client posts CSV + selections + the captured hash. The server generates the draft from scratch. The browser never gets to author source / route objects directly.

### Management handler authorization (LOCKED)

```text
Both /management/sources/bulk-preview and /management/sources/bulk-submit
use the same Studio authentication + role-based authorization + anti-
forgery protection as other config-changing management endpoints.

No new auth surface. No public-API anonymous endpoint.
```

---

## 2. Template substitution strategy (LOCKED)

**Reality-check on the existing templates:** verified `template-fanuc-v1.json` contains placeholders in BOTH string positions (`"InstanceId": "{{ instanceId }}"`) AND raw token positions (`"Enabled": {{ enabled }}`). The raw-token positions mean **the existing templates are NOT valid JSON before substitution.** `JsonNode.Parse(template)` fails on `{{ enabled }}`.

ChatGPT's preferred approach (parse first, walk JsonNode, substitute string values, deserialize) is **NOT possible** with the existing templates. Either change template format OR use the fallback.

**LOCKED choice: token-aware text substitution with JSON-safe escaping.**

### Substitution rules

```text
Service holds a STATIC PER-TEMPLATE registry of placeholders. Each
entry records:
    name                  e.g. "deviceId", "enabled"
    position kind         string | raw
    expectedOccurrences   int    (e.g. deviceId appears 3 times:
                                  once in Source.DeviceId,
                                  once derived in Source.InstanceId,
                                  once in RouteId reference)

Position kinds:
    string-position:  surrounded by " in the template
                      e.g. "{{ deviceId }}", "{{ deviceName }}", "{{ host }}", "{{ baseUrl }}"
    raw-position:     unquoted, must produce a valid JSON literal
                      e.g. "Enabled": {{ enabled }}

For each row's value:

    String-position values are encoded in TWO stages before insertion:

        Stage 1 - JSON-escape:
            "   -> \"
            \   -> \\
            control chars 0x00-0x1F -> \uXXXX

        Stage 2 - Brace-encode (prevents the post-substitution scan
                                from misreading operator-supplied {{ }}):
            {   -> {
            }   -> }

        Result: operator value `Mill "A" \ {{bad}}` becomes the JSON
        literal text:
            "Mill \"A\" \\ {{bad}}"

        After JsonSerializer.Deserialize, the .NET string property
        recovers the original literal:
            DeviceName == "Mill \"A\" \\ {{bad}}"   // C# string
                       == Mill "A" \ {{bad}}        // displayed

    Raw-position values are validated against a strict per-placeholder
    grammar BEFORE substitution:
        enabled  -> must match "true" or "false" exactly (case-sensitive)
                    after CSV parse normalization. Any other value -> block.

Substitution is a SINGLE PASS per placeholder:
    foreach (name, expectedOccurrences) in template registry:
        actual = replace ALL `{{ name }}` occurrences with prepared value
        if actual != expectedOccurrences:
            throw "BulkProvision.TemplateSubstitutionCountMismatch:
                   {name} expected {expectedOccurrences} occurrences,
                   found {actual}."

    after the pass, scan for any residual {{...}} markers:
        if any unconsumed marker remains -> throw
            "BulkProvision.TemplateResidualMarker: {marker} not in
             registry; template may be corrupt."

The residual-marker scan operates on the rendered TEMPLATE TEXT, NOT
on operator-supplied values (those got brace-encoded to { in
Stage 2 above). So `{{bad}}` from a CSV value never appears as raw
`{{` in the rendered text, and never trips the residual-marker scan.

After substitution, the resulting text is parsed via JsonSerializer:
    JsonSerializer.Deserialize<SourceInstanceConfig>(rendered, schemaOptions);
    JsonSerializer.Deserialize<RouteConfig>(...);

Any deserialization failure -> block with the row identified.
```

### Mandatory unit tests

```text
- deviceName = `Mill "A" \ {{bad}}` survives literally in the rendered
  SourceInstanceConfig.DeviceName property; no JSON corruption, no
  re-substitution of `{{bad}}`, no residual-marker throw.
- deviceName containing a JSON control char (0x09 tab) is preserved
  literally.
- deviceName containing a Unicode character (e.g. "Müller-CNC") is
  preserved literally.
- enabled = "TRUE" (uppercase) is rejected at preview as invalid raw-
  position value (strict casing).
- enabled = "" (empty) is rejected at preview.
- host = "192.168.10.21" passes.
- host = "1.2.3.4\";\"port\":99" doesn't break out of the JSON context.
- baseUrl = "http://example.com/x?y=1&z=2" survives escaping.
- A deliberately corrupted template (missing one `{{ deviceId }}`
  occurrence) throws BulkProvision.TemplateSubstitutionCountMismatch
  on first use.
- A deliberately corrupted template (adds unknown `{{ rogue }}` marker)
  throws BulkProvision.TemplateResidualMarker.
```

These tests gate PR I-1 merge.

### Forward path

If the template format gets reworked in Phase 2 (e.g. to support `tagProfile` columns), we can switch to ChatGPT's preferred approach (parseable templates with typed placeholders). For Phase 1, keep the existing templates and use this strategy.

---

## 3. Sink selection policy (LOCKED)

v3 said "first MQTT sink" implicitly. Replaced with explicit:

```text
Count of MQTT sinks (ProtocolName="mqtt", Enabled=true) on current gateway config:

    0 sinks
        Block at preview.
        Surface: "This gateway has no enabled MQTT sink. Add an MQTT
                  sink (Sinks page > Add destination) before running
                  bulk source import."

    1 sink
        Auto-select silently. Show selected sink in the Gateway
        context panel: "Routes will fan out to: <sinkInstanceId>".

    2+ sinks
        Require operator selection. Show a sink picker in the
        Gateway context panel before CSV upload step is enabled.
        Picker shows: sinkInstanceId, brokerHost, topic template,
        and any operator label.
```

Disabled sinks (`Enabled=false`) do NOT count toward the available set. If the gateway has only disabled MQTT sinks, treat as "0 sinks" and block.

---

## 4. Route collision policy + RouteId convention (LOCKED)

### RouteId generation

```text
For each imported source row:
    RouteId   = "route-" + deviceId             (e.g. "route-cnc-007")
    Name      = deviceName + " to " + sinkLabel  (e.g. "Mill-A1 to acme-mqtt")
    SourceInstanceId = "{deviceId}-source"      (matches the new source)
    SinkInstanceIds  = [ selectedSinkInstanceId ]
```

DeviceId-derived RouteId means a duplicate deviceId in the CSV (already blocked) also catches any route collision. No extra collision check needed at the CSV level.

### Collisions against the existing gateway config

```text
Block at preview if ANY of:
    new RouteId    already exists in current Routes[].RouteId
    new RouteId    matches existing Routes[].RouteId case-insensitive (defensive)
    new Source.InstanceId  already exists in current Sources[].InstanceId
    new Source.DeviceId    already exists in current Sources[].DeviceId

Warn (not block) if:
    new Route Name matches existing Route Name (operator can have
    intentional name reuse for grouping; the RouteId / SourceInstanceId
    checks already guarantee uniqueness)
```

### Test coverage

```text
- merge appends N RouteConfig records to current Routes[]
- each new route's SourceInstanceId == its new source's InstanceId
- each new route's SinkInstanceIds == [ selected sink ]
- existing routes untouched (identity preserved)
- existing routes' SourceInstanceId / SinkInstanceIds unchanged
- block when generated RouteId already exists on gateway
- warn when generated Route Name matches existing Route Name
```

---

## 5. deviceId / InstanceId relationship + format validation (LOCKED)

**Phase 1 convention:**

```text
CSV column        deviceId   -> SourceInstanceConfig.DeviceId
Service-derived              -> SourceInstanceConfig.InstanceId = deviceId + "-source"
                             -> RouteConfig.RouteId             = "route-" + deviceId
```

This matches the chip-3 offline generator's convention (per `generate.ps1`'s `$instanceId = "$($row.deviceId)-source"`), so the same deviceId produces the same InstanceId across both surfaces.

### Format validation (LOCKED — block invalid IDs at preview)

Because `deviceId` becomes Source.DeviceId, Source.InstanceId, and RouteId material, it must be a safe identifier:

```text
deviceId regex: ^[A-Za-z0-9_-]+$
    Length 1..64
    No spaces, no slashes, no dots, no Unicode.

If the operator uploads a CSV row with:
    cnc 007        -> block: contains space
    cnc/007        -> block: contains slash
    cnc.007        -> block: contains dot
    Mill@2         -> block: contains @
    (empty)        -> block: empty deviceId
    65+ chars      -> block: too long

The block fires per-row at preview with the offending value
identified. NO auto-normalization (e.g. spaces -> underscores).
Operators fix the CSV; the wizard never silently mutates IDs.
```

### Collision checks

Per §4, the merge service blocks against the existing gateway config on BOTH:
- `Source.InstanceId` collision → block
- `Source.DeviceId` collision → block

This prevents the case where the operator imports `cnc-007` twice under slightly different deviceNames — DeviceId is the operational identity, and importing the same one twice is always a mistake.

### Additional tests

```text
- blocks deviceId with spaces
- blocks deviceId with forward slash
- blocks deviceId with dot
- blocks deviceId with Unicode character
- blocks empty deviceId
- blocks deviceId longer than 64 chars
- passes deviceId matching ^[A-Za-z0-9_-]+$
```

---

## 6. Draft concurrency + unapplied-draft handling (LOCKED)

### Base-config staleness check

```text
On preview:
    BulkSourceMergeService computes baseConfigHash =
        SHA-256 of canonical-JSON serialization of the current
        applied GatewayConfiguration.

    Returns MergePreviewResult.BaseConfigHash to the Razor handler.

On submit:
    Razor handler sends the same baseConfigHash along with the
    user's confirmed merge intent.

    BulkSourceMergeService re-reads the current applied config and
    re-computes the hash.

    If newHash != submittedHash:
        BLOCK submit.
        Surface: "The current configuration changed since your
                  preview (someone else applied a draft). Please
                  refresh the preview and review the new state
                  before submitting."
        Operator must redo preview against the new state.
```

This avoids the "operator submits stale preview after someone else applied a different draft" race.

### Existing unapplied-draft handling

```text
Before preview:
    Service queries the existing draft store for any unapplied draft
    targeting THIS gateway.

If an unapplied draft exists:
    Warn (not block) in the Gateway context panel:
        "An unapplied draft exists for this gateway (Draft <id>,
         created <time>). Submitting will create another draft
         alongside it; you'll need to choose which one to apply."

    Operator can proceed with bulk import; the new draft becomes
    a parallel option in the existing draft list.

No block-on-existing-draft. The existing Studio draft system
already supports multiple coexisting drafts per gateway; the
warning just makes the situation visible.
```

---

## 7. "Tag availability not verified" warning surface (LOCKED — Preview AND post-apply)

v3 §5 put this warning only in post-apply guidance. ChatGPT correctly noted it should also appear in Preview so the operator sees the caveat **before** submitting.

### Preview surface

```text
At the bottom of the Preview state, in a soft-yellow warning box:

    "Tag availability is not verified in Phase 1.

     Some CNCs may not expose every tag in the selected protocol
     baseline (e.g., a lathe without a spindle won't publish
     Spindle/Speed). Missing tags will not publish; other tags
     continue normally.

     After applying the draft, check the Sources page for per-tag
     read errors via 3-way diagnostics."

For MTConnect submits where the operator pressed "Test connectivity",
the Preview also shows per-row observation coverage from /probe.
This is informational, not blocking.
```

### Post-apply confirmation

Same one-paragraph hint as v3 §5 stays in the confirmation step. Both surfaces matter — Preview warns BEFORE the operator commits; post-apply explains where to dig if something looks off later.

---

## 8. Service security guardrails (LOCKED — retained from v2 §7)

v3 dropped the pwsh shellout attack surface but ChatGPT correctly noted the upload-side guardrails still apply. Locked:

```text
- Template / protocol selected from a hardcoded allowlist:
      { focas2, brother-http, modbus-tcp, mtconnect }
  Anything else from the UI is a 400 Bad Request before service code runs.

- No arbitrary template path accepted from the UI. The service maps
  protocol IDs to known template file paths (compiled into the assembly
  or resolved from a known config directory).

- CSV file size cap: 1 MB max. Larger -> 400 with operator-readable
  message.

- CSV row count cap: 1,000 rows max. Larger -> 400 with operator-
  readable message.

- Strict required-column validation: CSV header must match the
  protocol's expected column shape exactly. Extra columns are
  rejected; missing columns are rejected. No partial-acceptance.

- HTML-encode all uploaded values in the Razor preview render to
  prevent XSS via deviceName / host strings.

- Never log raw CSV values without sanitization + truncation
  (no PII / no quote-escapes in logs).

- No draft created until the FULL merged GatewayConfiguration
  passes schema validation. The service calls
  IConfigurationSchemaValidator.ValidateAsync on the merged config
  before invoking CreateDraftAsync.
```

These are pure Studio-management concerns; nothing depends on pwsh, sidecar files, or temp workspaces.

---

## 9. Connectivity test UX + /probe safety (LOCKED — MTConnect only in Phase 1)

### UX

```text
MTConnect:
    Optional "Test connectivity" button appears in the Preview step
    after CSV upload. When pressed:
        - service probes each row's baseUrl
        - shows per-row reachable/unreachable + observation count
        - takes ~200-500ms per agent at low bounded parallelism
    Failure of any row does NOT block submit.
    Operator can submit blind, submit after partial test, or fix
    URLs and retest.

FOCAS2 / Brother / Modbus:
    Test connectivity button visible but DISABLED, with hover-text:
        "Connectivity test not available in Phase 1 for this protocol."
    Phase 1.1 may enable these once reusable probe helpers exist
    (FOCAS2 minimal handshake, Brother /version, Modbus TCP connect).
```

### MTConnect /probe safety (LOCKED — server-side hardening)

CSV values are operator-supplied and may be hostile. The probe request MUST be hardened:

```text
URL handling:
    baseUrl MUST parse as an absolute http or https Uri.
    Reject any other scheme (file://, ftp://, etc.) at preview.
    Probe URL is constructed via `new Uri(baseUri, "probe")`,
    NEVER by string concatenation.

    CSV may or may not include a trailing slash on baseUrl. Uri's
    resolver handles both. Examples that must work:
        http://192.168.10.51:5000        -> http://192.168.10.51:5000/probe
        http://192.168.10.51:5000/       -> http://192.168.10.51:5000/probe
        http://192.168.10.80/mtconnect/  -> http://192.168.10.80/mtconnect/probe
        https://example.local/mtconnect/ -> https://example.local/mtconnect/probe

HTTP client config (per probe):
    - Request timeout: 5 seconds (hard cap).
    - Max response size: 1 MB; aborted if exceeded.
    - Allow redirects: NONE (or limit to 1 redirect within same scheme).
    - Credentials: NEVER sent (no preemptive auth header,
                   no auto-credentials from the system).
    - Cookies: NONE (use an isolated HttpClient per import; do not
               share the Studio app's authenticated client).
    - User-Agent: a fixed Studio-bulk-provision identifier so
                  operators recognize Studio in agent logs.

XML parser config:
    - DTD processing: DISABLED.
    - External entity resolution: DISABLED.
    - XInclude: DISABLED.
    - Use XmlReaderSettings with DtdProcessing.Prohibit and
      XmlResolver = null.

Parallelism:
    - Max 5 concurrent probes (configurable, default 5).
    - Total wall-clock for 50 rows: ~5-10 seconds.
```

### Tests

```text
- probe URL is constructed correctly for baseUrl with no trailing slash
- probe URL is constructed correctly for baseUrl with trailing slash
- probe URL is constructed correctly for path-mounted agent
- probe URL is constructed correctly for HTTPS
- baseUrl with file:// scheme is rejected at preview
- baseUrl with javascript: scheme is rejected at preview
- probe request times out after 5 seconds for unresponsive agent
- response > 1 MB is aborted and row marked unreachable
- DTD-laden response does NOT cause external entity resolution
- redirect to a different scheme is NOT followed
- probe failures don't block submit
- 50-row probe completes within reasonable wall-clock budget
```

---

## 10. PR I-1 test list (LOCKED — explicit, 40 tests)

```text
BulkSourceMergeService + MTConnect probe + management handlers (~40 tests):

  Merge semantics:
    [T01] Appends N sources to current Sources[]
    [T02] Appends N routes to current Routes[] pointing at selected sink
    [T03] Preserves Gateway settings unchanged
    [T04] Preserves Sinks[] unchanged
    [T05] Preserves existing Sources[] unchanged (order + identity)
    [T06] Preserves existing Routes[] unchanged (order + identity)
    [T07] Preserves _provisioning + ExtensionData unchanged

  Sink selection:
    [T08] Blocks when zero enabled MQTT sinks exist
    [T09] Auto-selects when exactly one enabled MQTT sink exists
    [T10] Returns choice-required when 2+ enabled MQTT sinks exist
    [T11] Submit DTO must include selectedSinkInstanceId when 2+ sinks

  Source / DeviceId / Route collisions:
    [T12] Blocks duplicate deviceId within CSV
    [T13] Blocks new Source.InstanceId collision vs existing gateway
    [T14] Blocks new Source.DeviceId collision vs existing gateway
    [T15] Blocks new RouteId collision vs existing gateway
    [T16] Warns (does not block) duplicate deviceName within CSV
    [T17] Warns duplicate Route Name vs existing

  deviceId format validation:
    [T18] Blocks deviceId containing space
    [T19] Blocks deviceId containing forward slash
    [T20] Blocks deviceId containing dot
    [T21] Blocks deviceId containing Unicode character
    [T22] Blocks empty deviceId
    [T23] Blocks deviceId > 64 chars
    [T24] Passes deviceId matching ^[A-Za-z0-9_-]+$

  CSV / value safety:
    [T25] Blocks invalid MTConnect baseUrl format (must be http/https)
    [T26] Safely preserves quotes/backslashes in deviceName
    [T27] Safely preserves literal { } in deviceName (via brace-encoding)
    [T28] Preserves control chars via \uXXXX escape
    [T29] Preserves Unicode characters in deviceName
    [T30] Rejects enabled values other than exact "true"/"false"
    [T31] Template-substitution-count-mismatch fires for broken template
    [T32] Template-residual-marker fires for unknown {{ rogue }}

  Draft concurrency + submit-replay safety:
    [T33] Blocks submit when baseConfigHash mismatches (stale preview)
    [T34] Surfaces warning when unapplied draft exists for gateway
    [T35] Submit re-parses CSV from scratch (does not trust preview DTO)
    [T36] Submit re-runs full schema validation before CreateDraftAsync

  Schema validation:
    [T37] Blocks when generated merged config fails schema validation
    [T38] Submits when generated merged config passes schema validation

  MTConnect /probe safety:
    [T39] Probe URL constructed correctly (trailing-slash + path-mounted)
    [T40] file:// and javascript: baseUrl rejected at preview
    [T41] Probe timeout = 5s for unresponsive agent
    [T42] Probe response > 1 MB aborted
    [T43] DTD-laden response does NOT resolve external entities
    [T44] Probe redirect to different scheme NOT followed
    [T45] Probe failures don't block submit

  Management handler auth:
    [T46] Unauthenticated requests rejected with 401
    [T47] Missing anti-forgery token rejected with 403
```

That's the locked surface. PR I-1 acceptance requires all 47 green. (Numbering kept the original 1-38 stable from the pre-patch list; new tests appended.)

---

## 11. PR sequencing (LOCKED per ChatGPT)

```text
PR M (static HTML mockup):
    Can proceed against v3 as-is.
    Already includes the right states (gateway context, protocol
    picker, CSV download, upload + parse preview, optional
    connectivity, preview, submit, confirmation, error states).

PR I-0 (MTConnect template + 64-tag parity artifact + Pester):
    Can proceed against v3 as-is.
    Interim baseline ships first; customer-confirmed list lands later.
    Status header on the parity doc: "Interim baseline pending
    customer 64-tag enumeration."

PR I-1 (BulkSourceMergeService + management handlers + tests):
    Waits for THIS addendum (v3.1) to merge into master before
    coding begins. Then implements §1-§10 above as locked.

PR I-2 (Razor + Sources entry + UI/model tests):
    Waits for PR M operator sign-off AND PR I-1 service merge.
```

---

## 12. Cadence position

```text
1. v1 -> v2 -> v3 (LOCK with architecture amendment)
2. ChatGPT v3 review (approve direction; request v3.1 addendum)
3. v3.1 implementation addendum (THIS DOC)
4. User approval of v3.1
5. PR M and PR I-0 can start in parallel
6. PR I-1 starts after v3.1 merges to master
7. PR I-2 starts after PR M sign-off + PR I-1 merge
8. Phase 1.1 kickoff after Phase 1 merges (if cheap connectivity /
   coverage features are warranted)
9. Phase 2 kickoff for profile system + per-machine tags
```

User actions required:
- Approve v3.1 lock — or push back on any of the ten items.
- Customer 64-tag enumeration whenever ready (does not block PR I-0).
- PR M operator sign-off whenever the mockup PR lands.

---

## 13. v3.1 patch change-log (post-second-review)

ChatGPT's second review of v3.1 approved direction, requested seven implementation-contract fixes before PR I-1 starts. Folded in-place into the sections above:

| # | Section | What changed |
|---|---|---|
| 1 | §2 (substitution) | **Placeholder-literal contradiction fixed.** Operator-supplied `{` and `}` are now brace-encoded to `{` / `}` BEFORE insertion. The post-substitution residual-marker scan operates on TEMPLATE TEXT (no operator-supplied `{{` survives in raw form). Removes the contradiction between "literal `{{bad}}` survives" and "residual `{{...}}` throws". |
| 2 | §2 (substitution) | **"Replace one occurrence" → "replace all expected occurrences."** Per-placeholder `expectedOccurrences` count added to the static registry; mismatch throws `BulkProvision.TemplateSubstitutionCountMismatch`. Catches both broken templates and accidental partial substitution. |
| 3 | §1 (API surface) | **Submit-replay safety locked.** Submit must NOT trust client-supplied generated source/route objects. Submit re-sends CSV; server re-parses, re-merges, re-validates from scratch. Client's "preview" is informational only. |
| 4 | §1 (API surface) | **`selectedSinkInstanceId` added to the DTO contract** (preview + submit). Sink picker policy was locked in UI, but the field needed to exist on the wire too. |
| 5 | §5 (deviceId / InstanceId) | **deviceId format validation locked.** Regex `^[A-Za-z0-9_-]+$`, length 1..64. Spaces, slashes, dots, Unicode all rejected at preview. No auto-normalization. Seven new tests. |
| 6 | §9 (connectivity test) | **MTConnect /probe safety hardening.** Uri-based URL construction (not string concat), 5s timeout, 1 MB response cap, no redirects across schemes, no credentials, isolated HttpClient per import, fixed User-Agent. XML parser with DTD disabled, external entities disabled, XInclude disabled. Bounded parallelism (max 5 concurrent). Seven new tests. |
| 7 | §1 (API surface) | **Management handler auth locked.** Same Studio auth + role-based authz + anti-forgery as other config-changing endpoints. Two new tests. |

Test count grew from 25 → 47 (numbering remained stable for the original 25; 22 new tests appended).

No product-design changes. No scope changes. v3.1 patch is purely tightening the service contract before coding starts.
