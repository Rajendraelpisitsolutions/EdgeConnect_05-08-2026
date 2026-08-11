<!--
File:        docs/marketing/positioning-amendment-v4.md
Purpose:     Targeted amendment to the Industrial Intelligence Ecosystem
             positioning manifesto v3. Reverses the §4 "no customer names
             in Phase 1" lock and authorizes use of named customer logos
             in marketing surfaces, starting with the homepage trust band.
Audience:    Internal — Claude (governance enforcer), user (governance
             owner), engineering team (knows what is OK to publish),
             future writers / designers.
Format:      Markdown amendment memo. Sits alongside
             industrial-intelligence-ecosystem-positioning-v3.md as a
             scoped delta — the manifesto is NOT rewritten, this file
             carries the reversal.
Version:     v4 (amendment to v3)
Date:        2026-05-26
Status:      LOCKED — applied to homepage v3 + static reference 2026-05-26.

The full manifesto stays at v3. This file is the targeted §4 reversal.
Future amendments (v5, v6, …) follow the same pattern — scoped delta
files rather than full manifesto rewrites.
-->

# Industrial Intelligence Ecosystem — Positioning Amendment v4

**Scoped reversal of v3 §4 "no customer names in Phase 1" lock.**

This amendment authorizes the use of named customer logos and brand marks in marketing surfaces. The rest of positioning v3 remains in force unchanged.

---

## 1. What v3 §4 said (the lock being reversed)

Positioning v3 §4 stated, in summary:

> *"Phase 1 marketing surfaces do not name specific customers. Defense and space-agency deployments are referenced anonymously (e.g., 'deployed at defense and space-agency customers'). Customer stories arrive in Phase 3 with explicit named-customer sign-off."*

The intent was caution: protect customer relationships, avoid public claims that hadn't been confirmed, defer specifics until a Phase 3 customer-story program was in place.

## 2. What v4 reverses

**Named customer logos are now authorized for use in Phase 1 marketing surfaces** — homepage, datasheet, pitch deck, brochures, social cards.

**This is not a blanket unlock.** Customer NAMES (logos / wordmarks) are authorized; specific DEPLOYMENT STORIES remain anonymized until Phase 3 customer-story sign-off.

| Allowed under v4 | Still locked (until Phase 3) |
|---|---|
| Display "GE" / "Hitachi" / "Toyota" etc. as customer brand marks in a trust band, customer logo strip, or partner page | Naming a specific customer alongside a specific deployment story ("Toyota's plant in Bidadi deployed VAS to monitor…") |
| Caption: "Trusted by industrial leaders across automotive, energy, heavy manufacturing, and defense" | Caption: "Toyota uses Elpis VAS to monitor rotating machinery" — requires Phase 3 explicit sign-off |
| Use the logo at the size and format already public on `www.elpisitsolutions.com` | Reproduce confidential customer collateral, internal screenshots, or contract-specific imagery |

## 3. What customers are authorized for use

The amendment covers customers whose logos are **already publicly displayed** on `www.elpisitsolutions.com`. Those companies have implicitly accepted public association by appearing on the live site. Specifically:

**Industrial enterprises:**
- GE
- Hitachi
- Toyota
- Schneider Electric
- BHEL (Bharat Heavy Electricals)
- TVS
- Wipro

**E-IDOS sensor-ecosystem partners** (already cited in positioning v3 / hardware-ecosystem-map v3 §3.4 as supported sensor brands):
- HYDAC
- Filtrec

**Other:**
- Riverway
- University of Agricultural Sciences, Bangalore
- Software Toolbox (technology partner — note: this is a partner, not a customer)

**Phase 1 trust-band selection** (homepage v3 §1.5): 8 logos curated for maximum recognizability and ecosystem-fit — GE, Hitachi, Toyota, Schneider Electric, BHEL, TVS, HYDAC, Filtrec. The remaining 4 are retained as assets for future surfaces (Phase 3 customer stories, partners page, fuller customer index).

## 4. Why the reversal makes sense now

Three reasons the v3 caution no longer applies:

1. **The customers are already public.** Every logo authorized by §3 above already appears on `www.elpisitsolutions.com` as of the date of this amendment. The redesign roll-out doesn't introduce new public-facing claims — it carries forward existing claims into the new visual system. The "have we cleared this with the customer" question was answered when the logo first appeared on the live site.

2. **Trust bands are the standard premium-B2B pattern.** Stripe, Cognex, Schneider Electric, AVEVA, OSIsoft (PI), Rockwell — every premium industrial vendor displays customer logos on its homepage. Phase 1 omitting them looked anomalous against peer-vendor norms. The reversal restores parity.

3. **The HYDAC + Filtrec inclusion is ecosystem proof, not just trust signaling.** Positioning v3 §3.4 already publicly names HYDAC, Parker, MP Filter, and Argo-hytos as the sensor brands E-IDOS supports. Showing the HYDAC logo in a trust band is *ecosystem completeness*, not new disclosure.

## 5. Continuing locks (unchanged from v3)

The following v3 commitments remain in force:

- **§4 — Specific deployment stories stay anonymized.** "Deployed at defense and space-agency customers" remains the caption pattern for specific-story callouts (Section 7 proof band in homepage v3, datasheet credibility blurbs, pitch deck slide credits).
- **§4 — Defense / space-agency customer NAMES stay anonymized.** Even though customer-name unlock applies broadly, the specific defense-sector and space-agency customers remain off-the-record per the original v3 lock. (None of these appear in the §3 authorized list above.)
- **§5 — AMC partner channel remains anonymized at the partner level.** "Maintenance and AMC providers across India and the Middle East" stays the caption. Named partners arrive with Phase 4 partner portal.
- **All other v3 commitments** — five-pillar capability model, peer architecture, brownfield-direct emphasis, all unchanged.

## 6. How this amendment applies — surface by surface

| Surface | Treatment under v4 |
|---|---|
| Homepage (Section 1.5 trust band) | 8 logos (per §3) displayed in natural brand colors, ~64px tall, alternating left-right; subtle restraint via 92% opacity + scale hover |
| Datasheet | May add a customer-logo strip in v4 (deferred — datasheet v3 stays as-is until a refresh cycle) |
| Pitch deck (Slide 6 reference: customer-trust slide) | May add a customer-logo grid in deck v6 (deferred — deck v5 stays until refresh) |
| Brochures (hardware product brochures) | May add "Trusted by" footer with logo strip (deferred — current brochures stay until refresh) |
| Architecture diagram | Customer logos NOT added to the diagram. The architecture diagram remains protocol/product-focused only, per design-governance §2.3 ("no logos in the master diagram"). |
| Proof band (Section 7 homepage) | Continues anonymized per v3 §4. The proof band is for specific-deployment stories which remain locked. |

## 7. Sign-off

This amendment was approved by the user 2026-05-26 in response to the Phase 1.5 static-reference review pass.

User direction (verbatim): *"regarding the customer logo, as of now its not listing the logos. If thats expected behavior i am fine for now."* — followed by *"Yes but also flip to L2"* (where L2 = natural brand colors for customer logos).

The implicit positioning reversal is captured here so future writers and designers can reference a single document rather than chasing a chat-log decision.

---

## 8. Versioning

- **Current state:** positioning v3 (manifesto) + v4 (this amendment) — both in force.
- **Future amendments** (v5, v6, …) follow the same pattern: scoped delta files, not full manifesto rewrites.
- **Future canonical merge:** when accumulated amendments warrant a clean manifesto rewrite, a new v4 or v5 manifesto file will fold all amendments into one canonical doc. Until then, read v3 + v4 together.

---

*Positioning Amendment v4, 2026-05-26. LOCKED. Reverses v3 §4 "no customer names in Phase 1" lock with the scope and limits captured above. Future amendments to v3 follow this same delta-file pattern.*
