# PROPOSED AMENDMENT — `shared-knowledge/contracts/eremos-per-tag-mqtt.md`

**Status:** PREPARED, NOT PUSHED. This is a cross-project edit per `CLAUDE.md` shared-knowledge rules: "Changes affecting both projects → edit in `C:\dev\shared-knowledge\`, commit, push."

The autonomous EREMOS V2 v2 implementation run did NOT push this amendment to the shared-knowledge repo. The user reviews, approves, and pushes manually on return.

**Source EREMOS V2 v2 plan:** [`docs/sessions/2026-05-21-eremos-v2-revalidation-plan-v2.md`](../sessions/2026-05-21-eremos-v2-revalidation-plan-v2.md) §5.4 — round-2 review item #17 requested explicit documentation of the sanitization rule once resolved.

---

## Suggested insertion location

Add the following subsection to `C:\dev\shared-knowledge\contracts\eremos-per-tag-mqtt.md`, after the existing "Topic shape" / "Payload format" subsections (or wherever the topic structure is currently documented).

## Suggested subsection content

```markdown
## Tag-path sanitization rule

EdgeConnect MQTT sink (`MqttTopicResolver.Sanitize`, source at
`src/ElpisEdgeConnect.Sinks.Mqtt/MqttTopicResolver.cs`) sanitizes each
placeholder value (`gatewayId`, `sourceId`, `routeId`, `tagName`,
`deviceClass`) before substitution into the topic template. The rule
is locked and stable within the v1 contract:

| Rule | Behaviour |
|---|---|
| `/` → `_` | Forward slash (MQTT topic separator) replaced with underscore |
| `+` → `_` | MQTT single-level wildcard replaced with underscore |
| `#` → `_` | MQTT multi-level wildcard replaced with underscore |
| Null byte | Stripped entirely |
| Leading/trailing whitespace | Trimmed |
| Null or empty value | Replaced with a per-placeholder fallback (`unknown` for gatewayId/sourceId/routeId, `_unknown_` for tagName, `cnc` for deviceClass) |
| Case | **Preserved** (uppercase and lowercase both pass through) |
| Unicode | **Preserved** (no ASCII-only restriction at the sanitizer level) |

### Effect on canonical tag paths

Canonical tag paths in EdgeConnect's runtime + catalogs (`BrotherTagMap`,
`Focas2TagMap`, etc.) are hierarchical with `/` separators. The
sanitizer flattens the `/` into `_` for the MQTT topic segment ONLY;
the canonical path remains hierarchical inside the gateway (Runtime
Tap, audit chain, canonical pipeline, etc.).

Examples:

| Canonical tag path | MQTT topic segment after sanitization |
|---|---|
| `Status/RunState` | `Status_RunState` |
| `MachineInfo/Hostname` | `MachineInfo_Hostname` |
| `Tools/Magazine/3/ToolNumber` | `Tools_Magazine_3_ToolNumber` |
| `Alarms/Active/0/Number` | `Alarms_Active_0_Number` |

### Phase 0 topic regex

The resulting MQTT topic for a sanitized PerTag publication matches:

```
^eremos/[A-Za-z0-9_-]+/[A-Za-z0-9_-]+/[A-Za-z0-9_-]+/[A-Za-z0-9_-]+$
```

EREMOS V2's Phase 0 subscription `eremos/+/cnc/+/+` matches this shape
when the deviceClass segment is `cnc`. Phase 1+ subscriptions
(`eremos/+/+/+/+`) match all deviceClass values.

### Collision-free invariant (operator-facing rule)

Because sanitization is non-injective (multiple distinct canonical
paths can collapse to the same MQTT segment), operators MUST ensure
no two canonical tag paths on the same source sanitize to the same
MQTT segment. Example collision:

| Canonical tag path | MQTT topic segment |
|---|---|
| `Status/Run/State` | `Status_Run_State` |
| `Status_Run/State` | `Status_Run_State` ← COLLISION |
| `Status/Run_State` | `Status_Run_State` ← COLLISION |

Future provisioning tooling (`tools/bulk-provision/` per the Chip 3
plan trail) is expected to detect collisions at config-generation
time. EdgeConnect's EREMOS V2 revalidation test
(`EremosV2ContractTests`) detects collisions at validation time as a
subgate of Gate 4 (Topic determinism).
```

---

## Where to insert

Per the existing `eremos-per-tag-mqtt.md` structure (Phase 0 contract document):

- Insert after the section that describes the topic structure (`eremos/{gw}/{deviceClass}/{src}/{tag}`).
- Before the "Migration plan v1 → v2" section, since the sanitization rule is part of the v1 contract and should be documented before the v2 migration narrative.

## Commit message suggestion (for the shared-knowledge repo)

```
docs(contracts/eremos-per-tag-mqtt): document tag-path sanitization rule

EdgeConnect's MQTT sink applies a locked sanitization rule to topic
placeholders (/+# replaced with _, case + Unicode preserved). The rule
has been stable since the M.P2.4 Brother HTTP migration, but it lived
implicitly in MqttTopicResolver.cs source code. This explicit contract
documentation:

* Locks operator-visible behaviour at the contract layer.
* Defines the collision-free invariant (no two canonical tag paths
  on the same source may sanitize to the same MQTT segment).
* Provides the Phase 0 topic regex
  (^eremos/[A-Za-z0-9_-]+/[A-Za-z0-9_-]+/[A-Za-z0-9_-]+/[A-Za-z0-9_-]+$).

Surfaced by ChatGPT round-2 review of the EREMOS V2 revalidation v2
plan (docs/sessions/2026-05-21-eremos-v2-revalidation-plan-v2.md §5.4
in the EdgeConnect repo).
```

---

**End of proposed amendment.** Apply manually to the shared-knowledge repo when ready.
