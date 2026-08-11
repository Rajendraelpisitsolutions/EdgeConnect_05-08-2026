# Post-M.2b.6 product roadmap — UX completeness + competitive positioning

**Status:** Strategic prioritization, v1 draft
**Date:** 2026-05-18
**Inputs:**
- Internal product-strategy assessment (Claude, 2026-05-18)
- ChatGPT product-strategy review (2026-05-18)
- M.2b.3 + M.2b.3.1 shipped; M.2b.5 + M.2b.6 in flight per [v3 plan](2026-05-18-mp2b5-mp2b6-route-destination-wizards-plan-v3.md)

---

## Strategic positioning (locked)

**Elpis EdgeConnect is positioned as:**
> "A modern industrial edge connectivity platform purpose-built for MQTT, CNC, industrial IoT, and unified OT-to-cloud architectures — with enterprise-grade architecture transitioning into a complete commercial operator product."

**Not** "another Kepware." That's a trap — Kepware spent decades on 200+ drivers; we'd lose feature-for-feature.

### Where we already win

- Architectural maturity (draft/validate/apply, audit chain, hot-reload, store-and-forward, fail-soft recovery)
- Visual language (Linear/Stripe/Notion aesthetic vs competitors' Win32-era UIs)
- Demo mode (Browse Controller + FOCAS2 fake mode) — rare differentiator
- **EREMOS V2 integration** — vertically integrated industrial stack vs competitors' "buy Kepware + MQTT broker + historian + dashboard + MES separately"
- Target markets where we're already credible: CNC machine builders, MQTT-first plants, smart manufacturing retrofits, India + Middle East manufacturing

### Where we lag

- Edit-via-wizard (every wizard creates only; edit = JSON hand-edit)
- Live tag watch (operators go external to MQTT Explorer)
- List-page ergonomics (Sources/Sinks/Routes are flat lists; no search/sort/group)
- Bulk operations (no multi-select tools)
- First-run onboarding (fresh gateway lands on empty Config page)
- Cloud sink breadth (MQTT only; competitors have Kafka, AWS, Azure, Snowflake)
- OPC UA Server security (schema-only until Milestone K — release prerequisite per v3 Locked O)
- Fleet management (every gateway standalone)

---

## Tier 0 — In flight (don't touch the order)

| # | Milestone | State |
|---|---|---|
| 1 | M.2b.5 — Route Wizard | v3 LOCKED; pre-implementation pause active |
| 2 | M.2b.6 — Destination Wizard | v3 LOCKED; stacks on M.2b.5 |

After these ship: operators can finally use Studio to CREATE sources + routes + destinations end-to-end without touching JSON.

---

## Tier 1 — UX completeness (the "polished commercial product" bar)

These four close the gap between "operators can create things" and "operators can run a plant with this." All four are table stakes; missing any one undermines the commercial-product claim.

### M.2c — Live Tag Watch ⭐ HIGHEST PRIORITY

**Why first:** This is **commissioning infrastructure**, not polish. Without it, every debugging session forces operators out to MQTT Explorer or Wireshark. Customers lose confidence in first contact. Sales loses demos. Kepware's QuickClient is the de facto standard for this; we have nothing equivalent.

**Scope:**
- New page `/sources/{id}/watch` (and `/routes/{id}/watch` for the post-pipeline view)
- Live-streams canonical points via SignalR (or SSE) as they arrive
- Virtualised table with tag-name search + value formatting (numeric, boolean, string)
- Pause / Resume control
- Per-tag time-since-last-update indicator
- Optional CSV export of a captured window

**Effort:** Medium. Needs a new diagnostics-streaming endpoint + Razor virtualised table. ~2 weeks engineer time.

**ROI:** Highest of any single UX milestone. Sales conversion, support cost reduction, daily operator workflow improvement.

### M.2d — Edit-via-Wizard

**Why second:** Currently every operator change to an existing source/route/sink requires JSON editing. This is the post-setup operator-experience tax. Adds up fast at scale (50 sources × N tweaks/month).

**Scope:**
- Each existing wizard (Modbus, FOCAS2, Route from M.2b.5, MQTT from M.2b.6, OPC UA Server from M.2b.6) gains an "Edit" entry point
- Load current entity state into the wizard model
- Show diff vs current in the Draft Summary panel
- Save produces a draft that REPLACES the existing entity, not appends
- Reuse `WizardConfigMerger` with new `ReplaceXxxInDraft` methods (or a unified `BuildDraftReplacingX` pattern)

**Effort:** Medium-high. Touches every wizard. ~3-4 weeks engineer time.

**ROI:** High. Eliminates the JSON-edit footgun that competitors don't have.

### M.2e — List-page chrome (Sources, Sinks, Routes)

**Why third:** Once a site has 30+ entities, the flat list pages become a scrolling problem. Search/sort/group are table stakes for modern enterprise tools.

**Scope:**
- Search input (debounced) above each list page
- Column-header sorting
- Group-by toggle (by protocol / device class / status)
- Column toggle (show/hide columns)
- Saved views (per-operator persisted view preferences)
- Apply uniformly across `/sources`, `/sinks`, `/routes`

**Effort:** Medium. Mostly Razor + state persistence. ~2 weeks.

**ROI:** Medium-high at small scale; high at production scale.

### M.2f — Bulk operations + multi-select toolbar

**Why fourth:** Common ops tasks (disable 30 sources for maintenance window, clone-and-rename 10 similar routes) are currently per-entity loops.

**Scope:**
- Multi-select checkboxes on list-page rows (built on M.2e's list chrome)
- Toolbar appearing when ≥1 row is selected: Enable, Disable, Delete, Clone, Export
- Confirm dialog for destructive actions
- Atomic batch operation against `/api/v1/config/drafts`

**Effort:** Low-medium (~1 week) **if M.2e lands first**. Higher if built standalone.

**ROI:** Medium. High among ops teams; doesn't affect first-contact UX.

---

## Tier 2 — Strategic differentiation

These differentiate us in target segments (modern MQTT/CNC/cloud OT) rather than chase Kepware breadth.

### M.2g — First-run onboarding wizard

**Why:** Fresh gateway lands on an empty Config page. Sales demos and new-customer onboarding both suffer. A guided "Add your first source + destination + route in 90 seconds" wizard turns an empty install into a working demo.

**Scope:**
- New page `/welcome` that fires when current.json has zero sources/routes
- Guided 3-step flow (pick protocol → fill connection → wire to MQTT/OPC UA / leave for later)
- Optional "Use demo data" toggle that enables `EDGECONNECT_FOCAS2_FAKE_MODE` + creates a Demo MQTT sink pointing at local Mosquitto
- One-click completion lands the operator on a populated Overview page

**Effort:** Medium. ~2 weeks. Reuses wizard primitives from M.2b.1/3/5/6.

**ROI:** High for sales/demo conversion; lower for ongoing-use experience.

### M.2h — Tag tree explorer

**Why:** Flat tag lists in the FOCAS2 wizard and the route's transforms typeahead don't scale. HighByte's tag tree (hierarchical browse + search + drag-to-route) is one of their strongest UX features. We have `BrowseTagsAsync` data; we just need the UX layer.

**Scope:**
- New `/tags` page (or right-pane in source detail)
- Tree view (collapsed by default) of discovered tags per source
- Search + filter (glob-aware)
- Drag-target integration with the route wizard's Filter / Transforms / Watch sections

**Effort:** Medium-high. ~3 weeks. Needs a tree component (custom or MudBlazor TreeView) + cross-component drag-drop.

**ROI:** High at production scale. Medium at demo / small deployment.

### M.2i — Kafka sink

**Why:** Cloud-native enterprise customers default to Kafka. Adding it to our sink protocol matrix is one of the highest-ROI additions to feature breadth.

**Scope:**
- New `ElpisEdgeConnect.Sinks.Kafka` project (parallel to `Sinks.Mqtt`)
- Confluent.Kafka client
- Producer-only (consumer is out of scope — we're a SINK)
- Configurable topic templating (like MQTT)
- Destination wizard tile + per-protocol wizard

**Effort:** Medium-high. ~2-3 weeks. Mostly mirrors MQTT sink shape.

**ROI:** High in enterprise/cloud segment. Low in pure-CNC/MQTT segment.

### M.2j — Dark mode

**Why:** Table stakes for modern enterprise tools in 2026. Loud signal of "we're a modern product."

**Scope:** MudBlazor theme toggle + per-operator preference. Small.

**Effort:** Low. ~1 week with theming + testing across all pages.

**ROI:** Low operationally; moderate for product perception. Cheap win.

---

## Tier 3 — Enterprise readiness (parallel work; release prerequisites)

### Milestone K — OPC UA Server security hardening 🚨 RELEASE BLOCKER

**Why:** v3 Locked O of M.2b.6 plan made this an explicit release prerequisite. Non-None security modes are schema-accepted in MVP but rejected at adapter Initialize with `OPCUA.SECURITY_NOT_YET_IMPLEMENTED`. Shipping with this gap means operators following the wizard's "Recommended Basic256Sha256" hit runtime errors.

**Scope:**
- Sign + Encrypt policies (Basic256Sha256, Aes128/Aes256 variants)
- Username/Password authentication
- Server certificate lifecycle (auto-generate + custom cert path)
- Trust list management
- Secure channel renewal
- User role mapping (operator vs admin permissions)

**Effort:** **HIGH. Multi-month for a single engineer.** OPC UA Foundation specs are extensive. Cert lifecycle alone is non-trivial.

**Risk:** Underestimating this is the biggest schedule risk in the roadmap.

**Recommendation:** Start K planning IMMEDIATELY in parallel with M.2c. Do NOT assume K is a small follow-up.

### M.2k — Fleet management foundations

**Why:** Every gateway is standalone today. HighByte Hub, Litmus Edge Manager, AVEVA Connect all offer centralised fleet UIs. For multi-site customers this becomes a buyer concern fast.

**Scope (foundations only, not the full fleet UI):**
- Gateway registers with a central "fleet console" service (separate product, not bundled with the gateway)
- Heartbeat + status reporting
- Config push from console → gateways (draft semantics preserved)
- Audit-chain replication (each gateway pushes its audit log to the console)

**Effort:** **HIGH.** This is essentially a separate product. ~6-8 weeks for v1 console + gateway integration.

**Recommendation:** Don't build until we have ≥5 multi-gateway customers asking for it. Premature for v1 release.

### M.2l — License management UI

**Why:** Today operators read license state (read-only); they can't rotate licenses, install a new license file, or check expiration via Studio.

**Scope:**
- `/license` page with current license info
- File-upload to install a new license
- Module status table
- Expiration warning banner (visible 30 days before expiry)

**Effort:** Low-medium. ~1 week.

**ROI:** Low until customer license churn becomes a thing; then high.

---

## Tier 4 — Future expansion (don't build yet)

These are credible future moves but should NOT compete for engineering time against Tier 1-3 work:

- **More cloud sinks:** Azure IoT, AWS IoT, Snowflake, S3, generic HTTP webhook
- **More source protocols:** EtherNet/IP (Allen-Bradley), BACnet, IEC 61850 (utilities), DNP3 (utilities), Siemens S7-1500 variants beyond the basic S7
- **OTA update mechanism:** Check-for-update + signed-update install
- **Backup/restore + provisioning:** Better than current export; full-fleet snapshot capability
- **Audit-chain compliance export:** SOC2 / IEC 62443 evidence packs

---

## Deferred indefinitely (don't build, ever, in current product scope)

Per both reviews — these are traps:

- ❌ **Historian** — sink to a real historian (InfluxDB, TimescaleDB, EREMOS V2). Don't build our own.
- ❌ **Scripting / rules engine** — Cogent QuickScript, Ignition Jython. Would balloon scope. Tag mapping + deadband + rate limit covers 80% of what scripting is used for in gateways.
- ❌ **SCADA / HMI builder** — Ignition's strongest play; not our category. Stay focused on gateway/connectivity layer.
- ❌ **Try to match Kepware's 200-driver count** — pick the 15-20 that cover 80% of the market.

---

## Recommended sequencing

```
Now ─────────────────────────────────────────────────────────────────────►
│
├─ M.2b.5 Route Wizard          [in flight, v3 locked]
├─ M.2b.6 Destination Wizard    [v3 locked, stacks on M.2b.5]
│
├─────────── Tier 1 (parallel-friendly; can interleave) ────────────────►
│
├─ M.2c  Live Tag Watch         ⭐ START NEXT
├─ M.2d  Edit-via-Wizard
├─ M.2e  List-page chrome
├─ M.2f  Bulk operations        (depends on M.2e)
│
├─────────── Tier 2 + Tier 3 (parallelizable) ─────────────────────────►
│
├─ M.2g  First-run onboarding   (quick win, ~2 weeks)
├─ M.2h  Tag tree explorer
├─ M.2i  Kafka sink             (enterprise differentiator)
├─ M.2j  Dark mode              (cheap polish)
│
├─ Milestone K  OPC UA security hardening    🚨 RELEASE BLOCKER
│              ↑ START PLANNING NOW; in parallel with Tier 1
│
├─ M.2l  License management UI
│
└─ M.2k  Fleet management       (defer until ≥5 multi-gateway customers)
```

### Critical-path observation

**Milestone K (OPC UA security) is the long pole.** Multi-month engineering. If we want to release in N months:
- N - 6 months ago should have been "start K planning"
- N - 4 months ago should have been "start K implementation"
- M.2c through M.2j are all "weeks not months" and shouldn't block release

**Practical recommendation:** Spin up K planning + investigation in parallel with M.2c (Live Tag Watch). Even if K's engineering work doesn't start for another month, the planning + spec work absorbs slack productively.

---

## What this roadmap commits to

If we ship Tier 0 + Tier 1 + M.2g (Tier 2's first-run wizard) + Milestone K, we can credibly claim:

> "Best-in-class architecture, modern operator UX, MQTT/CNC/cloud-native focus, vertically integrated with EREMOS V2."

**That's a product that wins in our target segments — not Kepware's segment.**

We do NOT need to ship every Tier 2 / Tier 3 / Tier 4 item to claim that. We need Tier 0, Tier 1, the first-run wizard, and OPC UA security. The rest is post-release expansion.

---

**End of post-M.2b.6 product roadmap. Next action: clear the pre-M.2b.5 implementation pause to start the Route wizard work. M.2c (Live Tag Watch) becomes the milestone planning target after M.2b.6 PRs land.**
