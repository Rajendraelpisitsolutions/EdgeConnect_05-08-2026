# M.2b.5 + M.2b.6 — UX mockups

**Status:** v1 — DRAFT, for review alongside the v1 plan
**Date:** 2026-05-18
**Companion to:** [`2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan.md`](2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan.md)
**Format:** ASCII mockups + UX rationale. NOT HTML prototypes — these are layout specifications the Razor implementer reads to compose `MudBlazor` widgets correctly.

---

## 0. Design principles

The Studio already commits to a **Linear / Stripe / Vercel / Notion enterprise-SaaS aesthetic** (per [`MainLayout.razor` lines 7-11](../../src/ElpisEdgeConnect.Management/Components/Layout/MainLayout.razor)):

- **White chrome.** AppBar, cards, surfaces all `#FFFFFF`. Page background `#F8FAFC` (soft neutral). Subtle 1px borders in `#E5E7EB` instead of shadows or coloured fills.
- **Blue (#1976D2) is reserved for accents** — primary actions, focus rings, selected state, links. Never used as a chrome fill.
- **Semantic colors only for state**: success `#16A34A` green, warning `#F59E0B` amber, error `#DC2626` red, info `#2563EB` blue. Same vocabulary across the whole Studio.
- **Typography:** Inter, then system-ui fallbacks. Headers use medium weight (500), not bold. Body 14-15px. Captions 12px in `#6B7280` secondary.

We compete with **KepServerEX, MatrikonOPC, Cogent DataHub** — industrial gateway UIs known for power but not for UX polish. Our advantage: feel like Notion configured an OPC UA stack.

### Five UX commitments

1. **Smart defaults, no surprises.** Every field has a sensible default. Wizards never demand input the operator can't reasonably guess.
2. **Progressive disclosure.** Advanced options (retry policies, TLS, transforms) live behind expansion panels. The default view is the 80% path.
3. **Live validation.** Inline error/warning chips next to the offending field; no "click Save to find out it's broken".
4. **Auto-suggest, never overwrite.** Route ID auto-derived from source ID, but a dirty-bit stops auto-suggest the moment the operator types.
5. **Visible draft semantics.** Every wizard ends with a Draft Summary panel that lists exactly what will change. Operators commit by clicking Save-as-draft → Validate → Apply, not by mystery.

---

## 1. M.2b.5 — Route Wizard

**Path:** `/routes/new`
**Reachable from:** `Routes` page → "Add Route" button (currently hardcoded disabled per `Routes.razor:33-42`; this milestone flips it).

### 1.1 Page layout

```
┌────────────────────────────────────────────────────────────────────────────┐
│ [←] [🔗] Add a route                                                       │
│         Wire a source's data into one or more destinations.                │
├────────────────────────────────────────────────────────────────────────────┤
│                                                                            │
│ ┌─ 1. Identity ────────────────────────────────────────────────────────┐   │
│ │  Route id *           [ route-focas-cell-A_____________________ ]    │   │
│ │  Name (optional)      [ Cell A → MQTT____________________________ ]  │   │
│ │  ☑ Enabled            (uncheck to create the route stopped)          │   │
│ └──────────────────────────────────────────────────────────────────────┘   │
│                                                                            │
│ ┌─ 2. Source ──────────────────────────────────────────────────────────┐   │
│ │  Pick the source whose data this route delivers.                     │   │
│ │                                                                      │   │
│ │  ┌─────────────────────────────────────────────────────────────┐    │   │
│ │  │ ◉ focas-cell-A          [focas2]  Running  192.168.1.10:8193│    │   │
│ │  │   Cell A · cnc · 3 axes                                     │    │   │
│ │  ├─────────────────────────────────────────────────────────────┤    │   │
│ │  │ ○ modbus-line-7         [modbustcp]  Running  10.0.5.42:502 │    │   │
│ │  │   Siemens S7 Line 7 · plc                                   │    │   │
│ │  └─────────────────────────────────────────────────────────────┘    │   │
│ └──────────────────────────────────────────────────────────────────────┘   │
│                                                                            │
│ ┌─ 3. Destinations ────────────────────────────────────────────────────┐   │
│ │  Pick one or more sinks. Routes fan out — every selected sink        │   │
│ │  receives a copy of the source's data.                               │   │
│ │                                                                      │   │
│ │  ☑ mqtt-primary          [mqtt]      Running   eremos/+/cnc/+/+      │   │
│ │  ☐ opcua-server-1        [opcua]     Stopped   :4840/EdgeConnect     │   │
│ │  ☐ mqtt-eremos-staging   [mqtt]      Running   staging.eremos.io     │   │
│ │                                                                      │   │
│ │  Selected: 1 destination                                             │   │
│ └──────────────────────────────────────────────────────────────────────┘   │
│                                                                            │
│ ┌─ 4. Buffer ──────────────────────────────────────────────────────────┐   │
│ │  How the route holds data when a destination is unreachable.         │   │
│ │                                                                      │   │
│ │  Mode      ◉ Store and forward (durable, recommended)                │   │
│ │            ○ In memory (lossy on restart, lower latency)             │   │
│ │                                                                      │   │
│ │  Max depth [ 10000______ ] points                                    │   │
│ │            ▒ ~ 2.4 MB at 256 bytes/point                             │   │
│ └──────────────────────────────────────────────────────────────────────┘   │
│                                                                            │
│ ┌─ 5. Filter ──────────────────────────── ▾ Filter tags by name ──────┐   │
│ │  ╭─ Include ─────────────────╮  ╭─ Exclude (optional) ────────────╮ │   │
│ │  │  *                       × │  │  diagnostics/*_internal     × │  │   │
│ │  │  axes/*                  × │  │  (none)                       │  │   │
│ │  │  spindle/speed           × │  │                               │  │   │
│ │  │  [ Add pattern...   + ]    │  │  [ Add pattern...   + ]       │  │   │
│ │  ╰────────────────────────────╯  ╰─────────────────────────────────╯ │   │
│ │  ⓘ Glob: * matches any sequence, ? matches one character.            │   │
│ └──────────────────────────────────────────────────────────────────────┘   │
│                                                                            │
│ ┌─ 6. Transforms ────────────────────── ▾ Reshape values per tag ─────┐   │
│ │  ▾ Tag mapping (rename canonical tags)                          (0) │   │
│ │  ▾ Deadband (suppress small changes)                            (2) │   │
│ │    Tag                          Threshold (abs)   Threshold (%)     │   │
│ │    [ axes/x/absolute_______ ]  [ 0.5_____ ]      [        ]   ×    │   │
│ │    [ spindle/speed_________ ]  [        ]       [ 0.05___ ]   ×    │   │
│ │    [ Add tag...           + ]                                       │   │
│ │  ▾ Rate limit (cap publish frequency)                           (1) │   │
│ │    Tag                          Min interval (ms)                   │   │
│ │    [ alarms/active________ ]   [ 5000____ ]                  ×     │   │
│ │    [ Add tag...           + ]                                       │   │
│ └──────────────────────────────────────────────────────────────────────┘   │
│                                                                            │
│ ┌─ 7. Delivery ────────────────────────── ▾ Advanced ─────────────────┐   │
│ │  Mode      ◉ At least once   ○ At most once                          │   │
│ │  Retries   [ 3___ ]    Backoff initial [ 500 ms ]  max [ 30s ]       │   │
│ └──────────────────────────────────────────────────────────────────────┘   │
│                                                                            │
│ ┌─ Draft summary ──────────────────────────────────────────────────────┐   │
│ │  This draft will:                                                    │   │
│ │  • Add route route-focas-cell-A — focas-cell-A → mqtt-primary        │   │
│ │  • Filter:    3 include patterns, 1 exclude pattern                  │   │
│ │  • Transforms: 2 deadband tags, 1 rate-limit tag                     │   │
│ │  • Buffer:    Store-and-forward, 10000 points                        │   │
│ │  • Delivery:  At least once, 3 retries                               │   │
│ └──────────────────────────────────────────────────────────────────────┘   │
│                                                                            │
│                                              [ Cancel ]  [ Save as draft ] │
└────────────────────────────────────────────────────────────────────────────┘
```

### 1.2 UX rationale per section

**Section 1 — Identity**
- Route id auto-suggests `route-{sourceId}` once the operator picks a source in §2. A dirty-bit stops auto-suggest the moment the operator hand-edits.
- Live regex validation: `^[A-Za-z0-9][A-Za-z0-9._-]*$`. Inline red error if violated.
- Dup-id check against existing routes; inline error if collision.

**Section 2 — Source**
- Renders as a list of **selectable cards**, NOT a flat dropdown. Each card shows: instance id (bold), protocol chip, runtime status badge, device name + class, endpoint.
- Why cards: industrial operators have ~3-20 sources, all relevant. Cards help them disambiguate by status + endpoint at a glance.
- Selected card has a 2px primary-blue left border + a radio dot.
- Empty state: amber `MudAlert` "No sources are configured. [Add a source →](/sources/new) before creating a route."

**Section 3 — Destinations**
- Multi-checkbox table-style list (more compact than cards since fanout selection is often 1-3 destinations).
- Each row: checkbox, sink id (bold), protocol chip, runtime status, "preview" of where data will land (e.g. MQTT topic template).
- Live count chip: "Selected: N destinations".
- ≥1 required; inline error if zero selected.

**Section 4 — Buffer**
- Radio for mode, numeric for max depth.
- Smart hint: estimates memory cost from max depth × 256 bytes/point. Operators understand "2.4 MB" better than "10000 points".
- Anti-footgun: warning chip if max depth × 256 > 100 MB.

**Section 5 — Filter (collapsed by default)**
- Side-by-side Include + Exclude column layout on wide screens; stacks vertically on narrow.
- Default Include shows `*` as a chip the operator can remove (revealing it's the wildcard).
- Add-pattern row at the bottom of each list; inline glob-syntax validation.
- Help tooltip at the bottom explaining `*` / `?` semantics.

**Section 6 — Transforms (collapsed by default)**
- Three sub-sections in expansion panels: Tag Mapping, Deadband, Rate limit.
- **Deadband is ONE table with two threshold columns** (abs + %) per R-Q2 of the v1 plan. Operators see at a glance that a tag can have one or the other, not both. Inline error if both columns populated for the same tag.
- Tag input is a **typeahead** populated from the source's `BrowseTagsAsync` result (FOCAS2 source provides this; Modbus uses the configured tag list). Drastically reduces typo risk.
- Counter chips in each panel header: "(2)" tags configured under that step.
- EnrichmentTags deliberately omitted (DORMANT in Core; surfacing it would mislead).

**Section 7 — Delivery (collapsed by default)**
- Mode radio + retry/backoff numerics in advanced expansion. 90% of routes use defaults; the panel stays closed.

**Draft summary**
- Mirrors the source-wizard summary panel. Always-visible recap of what Save will do. Bulleted, plain English.

### 1.3 Validation states

| State | Visual |
|---|---|
| Empty required field | Red 1px border on the input + small red caption below |
| Valid optional field | Default — no special treatment |
| Warning (e.g. dup IP for source wizard — not used here) | Amber `MudAlert` between sections |
| Field passes regex but fails business rule (e.g. dup route id) | Red `MudAlert` directly below the field |
| Section incomplete | Section header chip "Incomplete" in amber |
| All sections complete | Save-as-draft button enabled; otherwise disabled |

### 1.4 Empty / edge states

- **No sources configured** — disable §2 entirely; show CTA: "Add a source to wire a route." with link to `/sources/new`.
- **No sinks configured** — disable §3; show CTA: "Add a destination first." with link to `/destinations/new`.
- **Browse-typeahead unavailable** (e.g. source's protocol doesn't support browse) — fall back to free-text input with a caption "Tag name (free-text — source does not expose discovery)".

---

## 2. M.2b.6 — Destination Wizard

### 2.1 Protocol picker page

**Path:** `/destinations/new`
**Reachable from:** `Sinks` page → "Add Destination" button (currently disabled per `Sinks.razor:35-44`).

```
┌────────────────────────────────────────────────────────────────────────────┐
│ [←] [+] Add a destination                                                  │
│         Choose where data leaves the gateway.                              │
├────────────────────────────────────────────────────────────────────────────┤
│                                                                            │
│   ┌─────────────────────┐  ┌─────────────────────┐                         │
│   │ [📡]                │  │ [🔌]                │                         │
│   │ MQTT                │  │ OPC UA Server       │                         │
│   │                     │  │                     │                         │
│   │ Publish to a broker │  │ Expose tags as an   │                         │
│   │ (Mosquitto, HiveMQ, │  │ OPC UA server other │                         │
│   │ AWS IoT, …)         │  │ tools subscribe to. │                         │
│   │                     │  │                     │                         │
│   │ [Available]         │  │ [Available]         │                         │
│   └─────────────────────┘  └─────────────────────┘                         │
│                                                                            │
│   ┌─────────────────────┐  ┌─────────────────────┐                         │
│   │ [🌐]                │  │ [📞]                │                         │
│   │ HTTP webhook        │  │ TCP socket          │                         │
│   │                     │  │                     │                         │
│   │ POST events to a    │  │ Stream bytes to a   │                         │
│   │ REST endpoint.      │  │ raw TCP listener.   │                         │
│   │                     │  │                     │                         │
│   │ [Coming in M.2b.7]  │  │ [Coming in M.2b.8]  │                         │
│   └─────────────────────┘  └─────────────────────┘                         │
└────────────────────────────────────────────────────────────────────────────┘
```

**Notes:** Identical visual treatment to `ChooseSourceProtocol.razor`. Available tiles get filled-success chips; pending tiles get outlined-default chips + reduced opacity + dashed border. Click anywhere on an available tile navigates to the per-protocol wizard.

### 2.2 MQTT Destination wizard

**Path:** `/destinations/new/mqtt`

```
┌────────────────────────────────────────────────────────────────────────────┐
│ [←] [📡] Add MQTT destination                                              │
│         Publish gateway data to an MQTT broker.                            │
├────────────────────────────────────────────────────────────────────────────┤
│                                                                            │
│ ┌─ 1. Identity ────────────────────────────────────────────────────────┐   │
│ │  Instance id *  [ mqtt-eremos-prod______________________________ ]   │   │
│ │  Display name   [ EREMOS Production______________________________ ]  │   │
│ │  ☑ Enabled                                                           │   │
│ └──────────────────────────────────────────────────────────────────────┘   │
│                                                                            │
│ ┌─ 2. Broker ──────────────────────────────────────────────────────────┐   │
│ │  Host *         [ mqtt.eremos.io_________________________________ ]  │   │
│ │  Port           [ 1883_ ]    ☐ Use TLS (port becomes 8883)           │   │
│ │                                                                      │   │
│ │  Authentication ◉ None (anonymous)                                   │   │
│ │                 ○ Username / password                                │   │
│ │                 ○ TLS client certificate                             │   │
│ │                                                                      │   │
│ │  Client id      [ edgeconnect-{gateway-id}_______________________ ]  │   │
│ │                 ⓘ {gateway-id} is templated at runtime.              │   │
│ └──────────────────────────────────────────────────────────────────────┘   │
│                                                                            │
│ ┌─ 3. Topic policy ────────────────────────────────────────────────────┐   │
│ │  Mode  ◉ Per-tag (EREMOS V2 compatible)                              │   │
│ │        ○ Batch (single JSON array per poll)                          │   │
│ │                                                                      │   │
│ │  Topic template                                                      │   │
│ │  [ eremos/{gatewayId}/cnc/{sourceId}/{tagName}_____________________]  │   │
│ │  ⓘ Tokens: {gatewayId}, {sourceId}, {tagName}, {deviceClass}         │   │
│ │                                                                      │   │
│ │  QoS   ◉ 0 (at most once, lowest overhead)                           │   │
│ │        ○ 1 (at least once, retransmit on no-ack)                     │   │
│ │        ○ 2 (exactly once — not recommended for telemetry)            │   │
│ │  ☑ Retained messages                                                 │   │
│ └──────────────────────────────────────────────────────────────────────┘   │
│                                                                            │
│ ┌─ 4. Test connection ─────────────────────────────────────────────────┐   │
│ │  Verify the broker is reachable before saving.                       │   │
│ │                                                                      │   │
│ │  [ 🔍 Test Connection ]   ━━━━━━━━━━━━━ Connecting…                  │   │
│ │                                                                      │   │
│ │  ⓘ Test Connection performs a one-shot CONNECT + DISCONNECT against   │   │
│ │     the configured broker. No data is published. No configuration is │   │
│ │     saved until you click Save as draft.                             │   │
│ └──────────────────────────────────────────────────────────────────────┘   │
│                                                                            │
│ ┌─ 5. Routing ─────────────────────────────────────────────────────────┐   │
│ │  ◉ Do not wire yet                                                   │   │
│ │      Destination saved as DISABLED. Add a route later to activate.   │   │
│ │  ○ Create a new route now                                            │   │
│ │      Wire one or more sources to this destination.                   │   │
│ └──────────────────────────────────────────────────────────────────────┘   │
│                                                                            │
│ ┌─ Draft summary ──────────────────────────────────────────────────────┐   │
│ │  • Add destination mqtt-eremos-prod (MQTT, mqtt.eremos.io:1883)      │   │
│ │  • Topic: eremos/{gatewayId}/cnc/{sourceId}/{tagName}                │   │
│ │  • Routing: leave for later — destination DISABLED until wired       │   │
│ └──────────────────────────────────────────────────────────────────────┘   │
│                                                                            │
│                                              [ Cancel ]  [ Save as draft ] │
└────────────────────────────────────────────────────────────────────────────┘
```

### 2.3 Test Connection success state

When the operator clicks "Test Connection" and the probe succeeds:

```
┌─ 4. Test connection ─────────────────────────────────────────────────┐
│                                                                      │
│  ✓ Connected to mqtt.eremos.io:1883 in 184 ms                        │
│    Broker version: Mosquitto 2.0.18                                  │
│    ProbeId: a7b2c1d3                                                 │
│                                                                      │
│  [ 🔍 Test Connection ]                                               │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

Visual: green-bordered `MudAlert Severity.Success`. ProbeId in monospace for log correlation. Button stays available for re-test.

### 2.4 Test Connection failure state

```
┌─ 4. Test connection ─────────────────────────────────────────────────┐
│                                                                      │
│  ✗ Could not connect to mqtt.eremos.io:1883                          │
│    Connection refused after 5.0s                                     │
│    ErrorCode: MQTT.CONNECT_REFUSED                                   │
│    ProbeId: f1e2d3c4                                                 │
│                                                                      │
│    Common causes:                                                    │
│    • Broker is not running                                           │
│    • Firewall blocking the port                                      │
│    • Wrong port (try 8883 with TLS)                                  │
│                                                                      │
│  [ 🔍 Test Connection ]                                               │
└──────────────────────────────────────────────────────────────────────┘
```

Visual: red-bordered `MudAlert Severity.Error`. Error code in monospace. Plain-English remediation hints below the structured error (operator-friendly).

### 2.5 OPC UA Server Destination wizard

**Path:** `/destinations/new/opcua`

```
┌────────────────────────────────────────────────────────────────────────────┐
│ [←] [🔌] Add OPC UA Server destination                                     │
│         Expose gateway data as an OPC UA server other tools subscribe to. │
├────────────────────────────────────────────────────────────────────────────┤
│                                                                            │
│ ┌─ 1. Identity ────────────────────────────────────────────────────────┐   │
│ │  Instance id *  [ opcua-server-1_______________________________ ]    │   │
│ │  Display name   [ Plant OPC UA Server___________________________ ]   │   │
│ │  ☑ Enabled                                                           │   │
│ └──────────────────────────────────────────────────────────────────────┘   │
│                                                                            │
│ ┌─ 2. Server endpoint ─────────────────────────────────────────────────┐   │
│ │  Bind address  [ 0.0.0.0_____ ]  Port [ 4840_ ]                      │   │
│ │  Server name   [ EdgeConnect___________________________________ ]    │   │
│ │  Application URI [ urn:edgeconnect:server:{gateway-id}_________ ]    │   │
│ └──────────────────────────────────────────────────────────────────────┘   │
│                                                                            │
│ ┌─ 3. Security ────────────────────────────────────────────────────────┐   │
│ │  Allowed security policies                                           │   │
│ │  ☑ None (insecure — dev only)                                        │   │
│ │  ☑ Basic256Sha256                                                    │   │
│ │  ☐ Aes128_Sha256_RsaOaep                                             │   │
│ │  ☐ Aes256_Sha256_RsaPss                                              │   │
│ │                                                                      │   │
│ │  Server certificate  ◉ Auto-generate on first start (recommended)    │   │
│ │                      ○ Use existing certificate at custom path       │   │
│ │                                                                      │   │
│ │  ☑ Allow anonymous access (no operator credentials)                  │   │
│ │  ☐ Require username / password                                       │   │
│ └──────────────────────────────────────────────────────────────────────┘   │
│                                                                            │
│ ┌─ 4. Namespace ───────────────────────────────────────────────────────┐   │
│ │  Namespace URI [ urn:edgeconnect:ns:plant1____________________ ]     │   │
│ │  Browse template  ◉ Site/Area/Source/Tag                             │   │
│ │                   ○ Source/Tag (flat)                                │   │
│ │  ⓘ Browse paths are populated from gateway settings at runtime.      │   │
│ └──────────────────────────────────────────────────────────────────────┘   │
│                                                                            │
│ ┌─ 5. Routing ─────────────────────────────────────────────────────────┐   │
│ │  ◉ Do not wire yet                                                   │   │
│ │  ○ Create a new route now                                            │   │
│ └──────────────────────────────────────────────────────────────────────┘   │
│                                                                            │
│ ┌─ Draft summary ──────────────────────────────────────────────────────┐   │
│ │  • Add destination opcua-server-1 (OPC UA Server, :4840)             │   │
│ │  • Security: None + Basic256Sha256                                   │   │
│ │  • Auto-generate certificate on first start                          │   │
│ │  • Anonymous access enabled                                          │   │
│ └──────────────────────────────────────────────────────────────────────┘   │
│                                                                            │
│  ⓘ No Test Connection for OPC UA Server: it's an acceptor, not a          │
│    connector. The first client that subscribes verifies reachability.     │
│                                                                            │
│                                              [ Cancel ]  [ Save as draft ] │
└────────────────────────────────────────────────────────────────────────────┘
```

### 2.6 UX rationale (Destination wizards)

**Test Connection placement (MQTT only)**
- Section 4, between configuration and routing. Operator typically: fill broker → test → if success, fill routing → save. Linear flow.
- Button is `MudButton.Outlined Color.Primary` with search icon — visually distinct from the primary "Save as draft" so operators don't confuse the two.
- Re-runnable: the operator can adjust connection settings and re-test as many times as they want.

**Topic template UI (MQTT)**
- Monospace input box with token highlighting (we render `{gatewayId}`, `{sourceId}`, etc. in primary-blue inline). Operators see "this part templates at runtime" without reading docs.
- Below the input: a live preview row that resolves the template against the gateway's actual `gatewayId` (read from `/api/v1/config`).

**No Test Connection (OPC UA Server) caption**
- Plain language explanation at the bottom of the form. Operator might expect a Test button because MQTT has one; the explicit caption pre-empts the "where's the test button?" support ticket.

**Security panel (OPC UA Server)**
- Default selections: `None` + `Basic256Sha256`, auto-generate cert, allow anonymous. Matches "minimum-viable dev" baseline.
- Warning chip if `None` is selected: "Insecure transport — dev/lab only. Disable before production."

**Routing section (both wizards)**
- Identical to source-wizard routing pattern.
- "Do not wire yet" forces `Enabled=false` to satisfy Core's `CONFIG.SINK_WITHOUT_ROUTE` validator. Same defence-in-depth pattern as M.2b.1/M.2b.3.

---

## 3. Cross-cutting UX patterns

### 3.1 Wizard chrome

All three wizards (route + MQTT + OPC UA Server) share the existing chrome from M.2b.1/M.2b.3:

- White-card sections with subtle 1px borders.
- Numbered section headers (`1. Identity`, `2. Broker`, …) for ordered progress without a progress bar.
- Required fields marked `*` in the label; tooltip explains "required for save".
- Cancel + Save-as-draft buttons right-aligned at the bottom.
- Sticky behaviour: as the operator scrolls down past a section, the section header subtly fades (this is Razor sticky-position styling, not separate widgets).

### 3.2 Save-as-draft button states

| State | Style | Trigger |
|---|---|---|
| Disabled | Greyed-out, no hover | Required fields missing or any validation error |
| Enabled | `MudButton.Filled Primary` blue | All required fields valid |
| Busy | Disabled + spinner in button | Save POST in flight |
| Success | (button hidden, snackbar fires) | Server accepts; redirect to `/config?new={draftId}` |

### 3.3 Snackbar messaging

After Save: same pattern as M.2b.1/M.2b.3 — `Snackbar.Add("Draft created. Next: click Validate, then Apply.", Severity.Success)` with 10-second `VisibleStateDuration`. Operator reads it, navigates to `/config`, completes the flow.

### 3.4 Demo mode banner (from M.2b.3.1) co-exists

When `EDGECONNECT_FOCAS2_FAKE_MODE=true`, the sticky amber banner from M.2b.3.1 still renders across every page including these new wizards. No conflict — the banner sits above all section panels.

### 3.5 Mobile / narrow viewport

- Side-by-side two-column layouts (Filter Include/Exclude) stack vertically below 1024px width.
- Section panel padding reduces from 24px → 16px.
- Expansion-panel content takes full width on mobile.
- Touch targets ≥44×44px on every interactive element.

---

## 4. Aesthetic + accessibility notes

| Concern | Treatment |
|---|---|
| **Colour contrast** | Text ≥ AA contrast (`#1F2937` on white = 12.6:1). Captions ≥ AA (`#6B7280` = 4.7:1). All semantic colours pass AA on white backgrounds. |
| **Keyboard navigation** | Every interactive element reachable via Tab. Section expansion panels respond to Enter/Space. Save-as-draft is the last Tab stop (after Cancel). |
| **Screen readers** | All form fields have explicit `<label>` association via `MudTextField Label="..."`. Validation messages tied via `aria-describedby`. Expansion panel state announced via `aria-expanded`. |
| **Reduced motion** | Respect `prefers-reduced-motion`. Section expansion collapses without animation; progress spinners reduced to static "Loading…" text. |
| **Locale** | English-only at v1. All copy strings are POCO constants (`LayoutChromeModel`-pattern) so a future i18n milestone can resource-bundle them. |

---

## 5. Comparable competitors — what we improve on

| Product | Strength | Weakness we exploit |
|---|---|---|
| **KepServerEX** | Comprehensive protocol coverage | Win32 MFC chrome; nested modal dialogs; no in-line validation; no draft semantics (changes apply immediately on click) |
| **MatrikonOPC** | Industrial deployment maturity | Forms feel like 1998; cluttered tool palettes; protocol-specific UIs diverge wildly |
| **Cogent DataHub** | Strong scripting & rule engine | Heavy admin tools; configuration buried 4 menus deep; no wizards |
| **Inductive Ignition** | Modern look | Mostly a SCADA layer above the gateway; gateway config UX is dense and engineer-only |

Our wizard sections share consistent chrome → an operator who can configure Modbus can configure FOCAS2 because they can configure MQTT because they can configure OPC UA Server. **One Studio, one design system, one pattern.** That's the differentiator.

---

## 6. Open UX questions for review

| # | Question |
|---|---|
| UX-Q1 | **Card vs dropdown for source picker** — cards are richer but take more vertical space. With ≥10 sources, the section becomes long. Should the source picker switch to a virtualised dropdown above N=10? |
| UX-Q2 | **Deadband combined-table** — one table with two columns (abs + %) per tag, or three sub-sections (Deadband / Deadband %)? Plan v1 §R-Q2 already flags this; mockup leans toward the combined table. |
| UX-Q3 | **Token preview** for MQTT topic template — fetch `gatewayId` from `/api/v1/config` on init and render the resolved preview live? Or static documentation of the tokens? |
| UX-Q4 | **OPC UA Server "no Test Connection" caption placement** — bottom of the form (as drawn) or right next to the connection section? Top is more visible; bottom matches the "draft summary always last" pattern. |
| UX-Q5 | **Tile order on destination picker** — MQTT first (most-used) or alphabetical? Mockup picks MQTT first. |
| UX-Q6 | **Filter + Transforms section split** — keep separate (as drawn) or merge into a "Tags" mega-section with tabs? Plan v1 §R-Q4 flags this. Mockup keeps separate for now (matches the underlying schema's split). |
| UX-Q7 | **Browse-typeahead for transforms tag-input** — should the source wizard's known tags be available as a typeahead in the route wizard's transforms editor? Requires loading `BrowseTagsAsync` results during route-wizard init. Tradeoff: friction-free for FOCAS2/Modbus sources, but slow on first load. |

---

**End of UX mockup v1. Ready for review alongside the v1 plan. After review, mockups + plan v2 land together.**
