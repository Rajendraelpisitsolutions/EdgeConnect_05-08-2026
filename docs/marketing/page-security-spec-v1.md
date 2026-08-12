<!--
File:        docs/marketing/page-security-spec-v1.md
Purpose:     Page spec for /security — the enterprise-trust / "operational
             trust" asset for the procurement / compliance reviewer (and the
             CISO / OT Architect behind them). v3 of the security page: migrates
             the security-page-copy-v2 prose into the canonical §9 per-page-spec
             format (matching every other Phase 2/E page), folds in the now-
             SHIPPED secret-redaction story, and refreshes stale cross-refs.
Audience:    Internal — Angular engineering (page implementers), copywriters
             (lifting verbatim), user + ChatGPT (reviewers), future page-spec
             authors.
Format:      Per §9 canonical per-page-spec template (page-capabilities-hub-spec
             -v1.md §9). Wraps the security page's own 9-section narrative.
Companion:   security-page-copy-v2.md (v2 — the prose this migrates + updates;
                voice + section narrative reused, now in §9 format)
             buyer-taxonomy-v1.md §2.7 (PRIMARY buyer = Procurement / compliance
                reviewer; §2.1 CTO/CIO + §2.3 OT Architect secondary; CTAs +
                vocabulary + proof-expectations locked)
             docs/platform-principles.md P3 (Security spec-first) + P1/P4
             design-system-v3.md §16 (trust-cue content pattern — /security is
                the AUTHORITATIVE target every other page cross-links INTO;
                this page does NOT itself use the §16 cue)
             proof-architecture-v1.md §3/§4/§8 (no fabricated claims, no
                customer/competitor names, no earned-cert claims)
             elpis-industrial-intelligence-platform-v5.md (datasheet — product
                facts; supersedes v4)
             page-platform-spec-v1.md + page-architecture-spec-v1.md v2.1
                (secondary surfaces; cross-link)
             architecture-diagram-spec-v3.md (trust-annotated diagram source;
                supersedes v2)
             docs/decisions/0020-diagnostic-bundle-redaction-spec.md (Accepted —
                four-tier redaction; the shipped Management/Backup redaction
                engine; fail-closed STRIP default; license file EXCLUDED)
             CLAUDE.md §3 (locked architectural decisions — #5/#6/#7 licensing,
                #8 store-and-forward, #10 per-adapter isolation, #14–#17 AI)
             shared-knowledge/contracts/opcua-namespace-policy.md (OPC UA
                security modes, X.509 cert management)
Version:     v1 (spec) — LOCKED 2026-06-05 (ChatGPT "Approve with changes"
                applied). v3 content generation of the security page (v1/v2 were
                copy docs).
Date:        2026-06-05
Status:      LOCKED 2026-06-05 — Track A collateral refresh (first item).
                ChatGPT "Approve with changes" verdict; all P0/P1/P2 applied.
                Next: static /security mockup.

ChatGPT review (2026-06-05) — "Approve with changes." Buyer fit strong; v2 trust
voice survived migration; redaction story net-positive. Applied before lock:
  - P0 roadmap-leak removal: §3.3 dropped "When AI features ship…" + named future
    agents → "AI-assisted workflows are constrained by design"; §3.7 Q1 dropped
    "HTTP / TCP sinks on the roadmap" → "destinations confirmed during the security
    review". (Cert Trust Center / last-known-good already excluded.)
  - P0 redaction-scope tightening: "every export" / "safe to email" / "nothing
    leaves by accident" → scoped to "configuration backups + diagnostic support
    bundles", "shareable in a support workflow after customer review", "exports
    redacted before they are written" (§3.4/§3.5/§3.7/§3.8).
  - P1 §6 rewritten to a cleaner no-cert sentence (removed "as our customer mix
    matures" future-cert flavor); kept "we will not stage one mid-sales-cycle".
  - P1 +7 questionnaire Q&A (network access/ports, update delivery, vendor remote
    access, cert/key lifecycle, audit retention/export, SBOM/dependency, public
    vuln-disclosure/IR) — all answered with honest "confirmed during the security
    review" / "no public program claimed", never fabricated specifics.
  - P1 OPC UA row reordered so anonymous/None doesn't read as a production posture.
  - Rollback reconciliation: "draft → validate → apply → rollback" KEPT — it is the
    SHIPPED config-apply pipeline (CLAUDE.md Management Studio + locked anti-pattern
    #10), distinct from the Proposed ADR-0025 last-known-good *pin*.

v2 (copy) → v3 (this spec) — what changed:
  - FORMAT: migrated from free-form page copy into the canonical §9 per-page-
    spec structure used by every other Phase 2/E page. /security is now a
    first-class spec, not an orphan copy doc.
  - NEW (shipped) CONTENT: the universal secret-redaction story — config
    backups + the diagnostic support bundle run through a field-level redaction
    engine (ADR-0020, Accepted; Management/Backup redactor shipped). Secrets are
    STRIPPED (adapter passwords, cert private keys, tokens), the license file is
    EXCLUDED, and any unclassified field FAILS CLOSED to stripped. Added as a §4
    property, a §5 "does not", and a §7 Q&A — it concretely strengthens the
    "how do you handle credentials / secrets" answer that v2 left thin.
  - STALE CROSS-REFS fixed: datasheet v4 → v5; homepage v2 → v3; CNC solution
    v2 → migrated spec; architecture-diagram v2 → v3; platform v4 → page-platform
    -spec.
  - P-G PROTOCOL ACCURACY: northbound is MQTT + OPC UA Server today (HTTP / TCP
    sinks = roadmap); corrected the v2 "OPC UA clients" sink wording.
  - PRESERVED (do not weaken): the "operational trust, not cybersecurity
    theater" frame; the five architectural commitments; the honest compliance
    posture incl. "we will not stage a certification mid-sales-cycle"; "trust is
    not a feature — it's the architecture"; "each exception is a compliance bill
    in waiting"; NO roadmap leakage; NO cyber-vendor language; NO earned-cert
    claims.

P3 HONESTY LOCK (security spec-first). Every property on this page is either a
LOCKED architectural decision (CLAUDE.md §3), a shipped capability, or an
Accepted spec — NEVER a Proposed-only feature. Specifically EXCLUDED from
"today" claims: Certificate Trust Center (ADR-0022, Proposed) and Last-Known-Good
pin / one-click rollback (ADR-0025, Proposed). Do not future-tense them onto this
page — the page's discipline is architectural confidence without roadmap leakage.
-->

# `/security` — Page Spec v1 (Security & Operational Trust, content v3)

**The enterprise-trust surface of the Elpis platform — the page a procurement / compliance reviewer walks into a vendor security review. It answers the questions a real security questionnaire asks, names what the platform does and refuses to do, and frames compliance honestly. It is the AUTHORITATIVE source of trust philosophy: every other page's §16 trust cue cross-links INTO this page.**

This is where a **procurement / compliance reviewer** (and the CISO / OT Architect behind them) lands to decide whether the platform clears vendor security review. They leave with *"I can document this posture, quote the no-overclaim language in my own internal review, and show the architecture to my CISO without flinching."* It is **not** a cybersecurity-vendor pitch and **not** a compliance-certification claim sheet — it is the operational-trust architecture, stated plainly.

Target length: **~1,700–2,000 words** page copy (the §4 checklist + §6 verification list + §7 Q&A are scannable lists, not prose-counted at full weight).

---

## 1. IA + buyer alignment

### 1.1 What this page IS / IS NOT

**IS:** The authoritative operational-trust page. Reader leaves with a documentable security posture: offline / air-gap behavior, signed offline licensing that never cuts data, hash-chained audit, AI-proposes-never-alters, role separation, per-tag quality, per-gateway identity, OPC UA security modes, and secret redaction by construction — each an architectural property they can verify, not an aspirational claim.

**IS NOT:**
- A cybersecurity-vendor pitch (no "military-grade", "zero trust", "next-gen", "AI-driven security" — §2.7 vocabulary-that-backfires)
- A compliance-certification claim sheet — **no ISO 27001 / SOC 2 / IEC 62443 / 21 CFR Part 11 claim or logo is made; the honesty is the trust signal** (§6)
- A roadmap page — only what the platform does **today** (no vulnerability-disclosure-policy / incident-response / cert-timeline leakage; those become their own pages once the programs exist, §8)
- A consumer of the §16 trust-cue pattern — this page is the **target** every other page cross-links into; it does not itself carry a trust cue (design-system v3 §16; memo v2 §4.0 authoritative-explanation invariant)
- The platform / architecture pages (`/platform`, `/architecture` v2.1 — secondary surfaces; cross-link)

### 1.2 Buyer alignment (per buyer-taxonomy v1 §2.7)

**Primary buyer:** Procurement / compliance reviewer (§2.7) — the risk / contract gatekeeper, always the last reviewer, owns the no-go decision when documentation is thin.
- Lands here from the §16 trust cue on any page, the footer, a vendor-questionnaire workflow, or a direct "send us your security posture" request
- Wants: a posture they can **document and circulate internally**; no-overclaim language they can quote; audit-defensible, verifiable architecture
- CTA preference: *"Request a security review"* > *"Download our security posture documentation"* > *"Talk to our security lead"* (peer-to-peer). **NOT** *"Book a demo"* / *"Talk to sales"* (wrong audience; they re-route internally)
- Vocabulary that lands: *audit trail / hash-chained / tamper-evident*, *offline-capable / no phone-home / air-gapped*, *role separation / least privilege*, *operational primitives*, *honest compliance posture*
- Vocabulary that backfires: *military-grade*, *zero trust*, *AI-driven security*, *bank-grade encryption*, and any specific framework claim (ISO / SOC / IEC / FDA) without the actual certification

**Secondary buyers:** CTO / CIO (§2.1 — honest-compliance framing) and OT Architect / SCADA engineer (§2.3 — validates the architectural primitives). Served by the same body copy; no separate section.

### 1.4 Page metadata (SEO + HTML head)

| Field | Value |
|---|---|
| **Meta title** (50–60 chars) | *Security & Operational Trust — Elpis Industrial Platform* |
| **Meta description** (140–160 chars) | *Offline-first, air-gap-ready OT platform: signed offline licensing that never cuts data, hash-chained audit, secrets redacted by construction, AI that proposes — never alters.* |
| **Canonical URL** | `https://www.elpisitsolutions.com/security` |
| **Schema intent** | `schema.org/WebPage` + `BreadcrumbList`. §7 security-review Q&A uses `FAQPage`. No `Product`/offer markup. No certification/`Certification` markup (none earned). |

---

## 2. Page structure — sections at a glance

9 content sections (the v2 narrative, preserved and updated). No new component.

| # | Section | Visual mode | Primary component(s) | Word target |
|---|---|---|---|---|
| **1** | Hero — "Built for the security posture your plant actually has." | `dark-deep` | `SectionShell` + `Button` ×2 | ~55 |
| **2** | The challenge — why OT software fails security review | `light` | narrative (3 ¶, no bullets) + margin pull-quote | ~200 |
| **3** | The Elpis approach — operational trust, not cybersecurity theater | `dark` | 5 bolded-lead commitments | ~300 |
| **4** | Properties to evaluate (the checklist) | `light` | bulleted property list (2-col desktop) | spec (scannable) |
| **5** | What the platform does NOT do (negative space) | `light-tinted` | bulleted "does not" list | ~150 |
| **6** | Compliance & certification posture (honest framing) | `light` | 3 ¶ + "verify today" bullets; NO logos | ~190 |
| **7** | Questions your security team is about to ask | `light` | inline FAQ (`FAQPage`), 17 Q&A | ~520 |
| **8** | Architecture for trust (trust-annotated diagram) | `dark` | trust-annotated architecture SVG + caption | ~85 |
| **9** | Final CTA — talk to our security lead | `dark-deep` | `CTASection` + `Button` ×2 (+ doc-download link) | ~75 |

---

## 3. Section-by-section detail

### 3.1 Section 1 — Hero

> EYEBROW: SECURITY & OPERATIONAL TRUST
> HEADLINE: Built for the security posture your plant actually has.
> SUBHEAD (≤64ch lines):
> Offline operation. Air-gapped readiness. Signed offline licensing that never cuts your data. Secrets redacted by construction. AI-assisted workflows that propose — never silently alter. Trust architecture engineered for OT, not adapted from IT.
>
> PRIMARY CTA (`Button.primary.lg`): Request a security review → HREF `/contact?intent=security-review`
> SECONDARY CTA (`Button.secondary.lg`): Talk to our security lead → HREF `/contact?intent=security-lead`

**Visual:** a closed control cabinet, dim, no people — isolation / locked perimeter / calm reliability. **Banned:** padlocks, shields, hooded-hacker / glowing-network stock art, "AI brain with circuits", military-grade visual language. Headline display weight; subhead body, slightly tighter than homepage.

**Reader effect:** *"this page will answer my actual questions, not pitch at me."* The "engineered for OT, not adapted from IT" line is the central frame — designed against OT constraints from day one, not retrofitted after a cloud-first product met a regulated deployment.

### 3.2 Section 2 — The challenge

> EYEBROW: THE CHALLENGE
> SECTION TITLE: Why OT software fails security review
>
> Most industrial software was designed in an IT world and patched for an OT one. The architecture assumed a live internet connection for licensing, a cloud back-end for telemetry, a third-party AI service for "intelligence," and a refresh cycle measured in days. Then the software met a real factory.
>
> Real factories don't have any of that. They have isolated control networks. They have plants that cannot be patched mid-shift. They have customers with strict no-cloud policies, export-control regimes, defense contracts, regulated data, or simply a CISO who reads vendor contracts carefully. The IT-shaped software starts requiring exceptions: a special license-server arrangement here, a cloud-bypass mode there, an air-gap workaround over there. Each exception is a compliance bill in waiting.
>
> The right answer isn't to ship the same IT-shaped software with a longer security-questionnaire response. It's to design the trust architecture into the platform from the first commit. That's what Elpis did.

**Visual:** three narrative paragraphs, no bullets; subdued (the empathy section). Margin pull-quote: *"Each exception is a compliance bill in waiting."*

**Reader effect:** *"this person has actually sat through a vendor security review."* The license-server / cloud-bypass / air-gap-workaround trio does real recognition work.

### 3.3 Section 3 — The Elpis approach

> EYEBROW: THE APPROACH
> SECTION TITLE: Operational trust, not cybersecurity theater
>
> **Production continuity is non-negotiable.** A platform that stops a production line because its license server is unreachable, its cloud back-end is down, or its AI service is externally unavailable has failed its core operational responsibility. EdgeConnect runs offline by default, validates its license locally, and never depends on a remote service to keep machine data flowing.
>
> **Air-gapped operation is the default, not a fallback.** EdgeConnect does not require an internet connection at any point in its lifecycle — installation, licensing, configuration, telemetry, or update. Plants on isolated OT VLANs install and run the platform exactly the same way as plants with internet access. There is no "offline mode" because there is no "online mode" — there is just operation.
>
> **AI-assisted workflows are constrained by design.** Where AI-assisted workflows are enabled, they generate proposals for an operator to confirm. They do not silently alter routing, transform data, or change configuration. Local-LLM support is mandatory for AI-assisted deployments; cloud LLMs are optional. No plant data is sent to a third-party AI service by default.
>
> **Audit trail as a system property, not a feature.** Every configuration change to the gateway is recorded in a hash-chained log with actor identity and timestamp. The log is tamper-evident and replay-ready — not bolted on for regulated-industry sales, but built into the configuration plane itself.
>
> **Licensing that never cuts customer data.** A lapsed license blocks configuration changes; it never stops production data from flowing. Your machines keep talking even if your renewal email got stuck in someone's spam folder. The license enforces the commercial relationship — it does not hold operations hostage.

**Visual:** five bolded-lead paragraphs; optional restrained inline icons (continuity / disconnected-cloud / balanced-scales / chain-link / key). Medium-high density — the heaviest philosophical work on the page.

**Reader effect:** *"these are architectural decisions, not marketing positions."* The "no online mode" inversion reframes offline from degraded state to canonical state. The licensing paragraph is one of the strongest individual trust signals — "never holds operations hostage" earns trust immediately.

### 3.4 Section 4 — Properties to evaluate (the checklist)

> EYEBROW: EVALUATE THE PLATFORM
> SECTION TITLE: Specific properties to evaluate
>
> Use this as a checklist against any other OT vendor you're considering. Each item is an architectural property of the platform, not an aspirational claim.
>
> - **Offline-first by design.** EdgeConnect does not require an internet connection to run, validate its license, or deliver data.
> - **Air-gapped factories are first-class.** RSA-signed JSON license files validate locally. No phone-home. No cloud licensing dependency. Updates ship on physical media or internal mirrors.
> - **No forced cloud dependency.** The platform can be entirely on-premise. Cloud destinations are optional, not required.
> - **Licensing never cuts data.** A lapsed license blocks configuration changes only; production data keeps flowing.
> - **AI never alters the data path.** AI agents propose; humans confirm. No silent state changes. Local-LLM support is mandatory; cloud LLMs are optional.
> - **Hash-chained configuration audit log.** Every change recorded with actor and timestamp. Tamper-evident, exportable for review.
> - **Reversible configuration changes.** A draft → validate → apply → rollback flow means untested configuration never reaches the data path, and any applied change can be rolled back.
> - **Secrets redacted by construction.** Configuration backups and the diagnostic support bundle run through a field-level redaction engine: adapter passwords, certificate private keys, and API tokens are stripped; the signed license file is excluded entirely; any field not explicitly classified fails closed to stripped. A support bundle is designed to be shareable in a support workflow after customer review.
> - **Per-tag quality codes.** Every data point carries a quality state (Good / Uncertain / Bad / Stale), so downstream systems can distinguish a real value from a stale or unreliable one.
> - **Fault isolation.** A failing adapter never affects another adapter, route, or sink; a misbehaving sink never blocks a healthy one. Store-and-forward preserves queued data across outages and replays in source order.
> - **Role separation supported.** Configuration editing, audit review, and data consumption can be assigned to distinct roles. No single operator holds every privilege by default.
> - **Per-gateway identity.** Each EdgeConnect instance carries a stable UUID and customer/site binding established at first start. Fleet identity is clean and traceable.
> - **OPC UA Server security modes.** Anonymous + SecurityMode=None is available for commissioning on trusted OT VLANs; production deployments can use Sign / SignAndEncrypt with X.509 certificate-based authentication.
> - **Application certificate management.** The OPC UA Server auto-generates a self-signed X.509 application certificate on first start; client trust is operator-controlled via standard trust folders.

**Visual:** single bulleted list, two-column desktop / single-column mobile; bolded property + plain-body description, each scannable in ~5s; optional "shield-check" checkbox icon to reinforce the checklist framing. **No certification claims** — every bullet is an architectural property.

**Reader effect:** the reviewer can walk this list into a security-review meeting and point at specific properties. The redaction + OPC UA detail signals security primitives built in, not bolted on.

### 3.5 Section 5 — What the platform does NOT do

> EYEBROW: THE NEGATIVE SPACE
> SECTION TITLE: What the platform refuses to do
>
> Honest framing of what the platform refuses to do is sometimes more useful than what it does:
>
> - **Does not phone home for telemetry or licensing.** Period.
> - **Does not require cloud connectivity.** Cloud is opt-in. The platform runs identically with no external network access.
> - **Does not send plant data to third-party AI services by default.** Local-LLM-capable is the default posture.
> - **Does not bypass operator confirmation for AI-proposed actions.** AI agents never silently alter state.
> - **Does not export secrets in plaintext in configuration backups or diagnostic bundles.** Both are redacted by construction; the license file is never bundled.
> - **Does not auto-update.** Updates are operator-controlled. Plants schedule them against their own shift calendar.
> - **Does not allow a single shared credential.** Role separation is supported from day one.
> - **Does not depend on a vendor cloud service for any production-critical path.** Buffering, replay, alarm capture, and data acquisition all work with the cloud entirely unreachable.

**Visual:** single bulleted list, same density as §4; a slight palette shift / visual rule from §4 so the reader registers "the inverse side of the trust story." Each bullet leads with "Does not" for rhythm.

**Reader effect:** *"what the vendor refuses to do tells me more than what they claim."* This does as much credibility work as §4.

### 3.6 Section 6 — Compliance & certification posture

> EYEBROW: COMPLIANCE POSTURE
> SECTION TITLE: Honest framing on certifications
>
> Elpis does not currently claim ISO 27001, SOC 2, IEC 62443, 21 CFR Part 11, or equivalent framework certification on this page. What we can provide today is evidence of the operational primitives those reviews often ask about: audit trails, role separation, signed local licensing, offline-first behavior, secret redaction, and a deterministic data path.
>
> If your industry requires a specific certification or audit posture, talk to us about your timeline and we will be transparent about where the platform stands today. We will not claim a certification we have not earned, and we will not stage one mid-sales-cycle to win a deal.
>
> **What you can verify today** — without us claiming any specific framework:
>
> - The configuration audit log produces a hash-chained, tamper-evident record suitable for regulated-industry change-control review.
> - The offline-first architecture supports air-gapped deployments and export-control-sensitive environments.
> - The AI constraint architecture (proposals + human confirmation + local-LLM support) supports environments where data sovereignty is non-negotiable.
> - The secret-redaction engine lets you produce a support bundle that is safe to share without leaking credentials, keys, or the license file.
> - The per-tag quality codes and three-way diagnostics support investigations into data provenance and integrity.

**Visual:** three short prose paragraphs, then a "verify today" bullet list. **No certification logos** — do not display ISO / SOC 2 / IEC / FDA marks until earned. Calm, confident treatment — not defensive, not aspirational.

**Reader effect:** *"these people will not oversell me on compliance theater."* The honesty is itself the differentiator. The *"we will not stage one mid-sales-cycle to win a deal"* line is direct, deliberate, and earns trust — keep it even though it is blunt (§2.7 explicitly calls it out as effective).

### 3.7 Section 7 — Questions your security team is about to ask

Per §9 (page-type FAQ governance — YES for trust pages). `FAQPage` schema. 10 Q&A in real-questionnaire language.

> EYEBROW: SECURITY REVIEW
> SECTION TITLE: Questions your security team is about to ask
>
> #### "Where does our plant data go?"
> Wherever you route it. EdgeConnect publishes to the destinations you configure — MQTT brokers and an OPC UA Server today. Required destinations and protocol scope are confirmed during the security review. Nothing is sent to Elpis by the platform automatically, and no telemetry is collected for our visibility.
>
> #### "What happens if the license expires?"
> Configuration changes are blocked; data keeps flowing. Production continuity is not affected by a licensing event.
>
> #### "Can your software run completely offline?"
> Yes. Installation, licensing, configuration, runtime, and updates can all happen with no internet access.
>
> #### "How do you handle credentials and secrets at the edge?"
> Source and sink credentials are stored encrypted at rest on the edge node. Operator credentials for the Connectivity Studio UI follow standard role-based access control. And exports are redacted before they are written: configuration backups and diagnostic bundles run through a field-level redaction engine that strips passwords, certificate private keys, and tokens, and excludes the license file entirely.
>
> #### "If we send you a diagnostic bundle for support, what's in it?"
> A redacted-by-construction snapshot: status codes, route/source/sink IDs, health metrics, and recent diagnostic events — with adapter passwords, certificate private keys, API tokens, and the license file removed before the bundle is written. Any field not explicitly classified as safe fails closed to stripped. The bundle is designed to be shareable in a support workflow after customer review.
>
> #### "What happens if a config change breaks the gateway?"
> The draft → validate → apply → rollback flow makes every config change reversible. Untested configuration never reaches the data path.
>
> #### "Can a single operator do everything, or do you support role separation?"
> Role separation is supported. Configuration editing, audit review, and data consumption can be assigned to distinct roles.
>
> #### "What's the audit trail for configuration changes?"
> A hash-chained log with actor identity and timestamp for every change. Tamper-evident, exportable for review.
>
> #### "How does your AI work — does it need our data?"
> AI-assisted workflows propose actions for human confirmation. Local-LLM support is mandatory; cloud LLMs are optional. By default, no plant data leaves your environment for AI purposes.
>
> #### "What network access does the platform require?"
> It can run with no internet access at all. Where you do connect it, the required inbound / outbound ports, egress endpoints, and DNS / NTP assumptions are documented and confirmed during the security review against your network policy.
>
> #### "How are updates delivered and controlled?"
> Updates are operator-controlled — never automatic. They can be applied from physical media or an internal mirror, on your own shift calendar. Package-integrity details are confirmed during the security review.
>
> #### "Does Elpis have remote access to our deployment?"
> No vendor remote access is required or enabled by default. Any support access is customer-approved and deployment-specific.
>
> #### "How are certificates and keys managed?"
> The OPC UA Server auto-generates a self-signed X.509 application certificate on first start; client trust is operator-controlled via standard trust folders. Generation, import, and rotation / expiry responsibility for the production trust model are confirmed during the security review.
>
> #### "What audit logs can we export, and what's the retention model?"
> The configuration audit log is a hash-chained, tamper-evident, exportable record with actor identity and timestamp. Retention window, export format, and time source are confirmed during the security review against your change-control requirements.
>
> #### "Do you provide an SBOM or third-party dependency information?"
> No public SBOM program is claimed on this page. Dependency and SBOM information for a specific release is provided during the security review.
>
> #### "Do you have a public vulnerability-disclosure or incident-response policy?"
> No public program is claimed on this page. Deployment-specific security contacts and escalation are established during the security review.
>
> #### "Can we run a security review against the platform before purchase?"
> Yes. We support a pre-purchase security review against a representative deployment.

**Visual:** each question a bold pull-quote, answer in body weight beneath; generous spacing (scan-bait, not paragraphs); optional question-mark icon.

**Reader effect:** converts the reviewer from "evaluating" to "ready to schedule the review call." Questions use the exact language a real security questionnaire uses.

### 3.8 Section 8 — Architecture for trust

> EYEBROW: WHERE TRUST LIVES
> SECTION TITLE: Where trust lives in the platform
>
> [ trust-annotated SVG — a variant of the master diagram (architecture-diagram-spec-v3.md) annotated with trust properties: *"License validates locally"* at EdgeConnect; *"Hash-chained audit"* at the configuration plane; *"Secrets redacted in backups + support bundles"* at the backup / bundle boundary; *"AI proposals require human confirm"* at any AI-agent boundary; *"Per-tag quality codes"* on every data arrow ]
>
> *Each critical layer of the platform has a named trust property. Offline operation at the edge. Signed local licensing. Hash-chained configuration audit. Secrets redacted in backups and support bundles. AI proposals at the intelligence layer require human confirmation. Per-tag quality codes propagate end-to-end. There is no "trust feature" because trust is not a feature — it's the architecture.*

**Visual:** trust-annotated variant of the master architecture diagram (callout labels at each trust property); if the annotated variant doesn't exist yet, use the master SVG with overlay callouts. Italic caption beneath.

**Reader effect:** *"trust is structural, not bolted-on."* The closing line — *"trust is not a feature — it's the architecture"* — is the page's central philosophical anchor; give it visual prominence.

### 3.9 Section 9 — Final CTA

> EYEBROW: NEXT STEP
> HEADLINE: Talk to our security lead.
> SUBHEAD: Bring us your security-review questionnaire. Bring us your air-gap requirement. Bring us your regulated-industry constraint. We will tell you exactly what the platform does today, what it does not do, and what we are willing to commit to for your deployment.
> PRIMARY CTA: Request a security review → `/contact?intent=security-review`
> SECONDARY CTA: Talk to our security lead → `/contact?intent=security-lead`
> TERTIARY (text link): Download our security posture documentation (PDF) — §2.7 wants something to circulate internally

**Visual:** centered, generous whitespace; same pacing as homepage / solution-page final CTAs; two primary-weight CTAs (the security audience splits between "schedule a review" and "specific question") plus the doc-download text link; headline display weight, slightly smaller than the hero.

**Reader effect:** the "Bring us your X" framing localizes the homepage CTA pattern to the security audience. *"We will tell you exactly what the platform does today, what it does not do, and what we are willing to commit to"* promises honesty, not capability.

---

## 4. Components used

All design-system v3 LOCKED. **No new visual primitive.**

| Component | Used in |
|---|---|
| `SectionShell` (mode variants) | every section |
| `Button` (primary + secondary, lg) | §3.1; §3.9 |
| Inline FAQ (`FAQPage` schema) | §3.7 |
| Trust-annotated architecture SVG (variant of architecture-diagram-spec-v3 master) | §3.8 |
| `CTASection` | §3.9 |
| **NOT used:** §16 trust-cue pattern | — this page is the authoritative target other pages cross-link into, never a consumer |

---

## 5. Verbatim copy summary

All page copy in §3.1–§3.9. **~1,900 words** page copy (within target, after the +7 questionnaire Q&A). The §3.4 checklist + §3.7 Q&A are scannable lists, not prose-counted at full weight. Source: security-page-copy-v2 voice preserved verbatim where unchanged; deltas are the redaction property/Q&A, the reversible-config + fault-isolation properties, the protocol-accuracy fix, the 7 added questionnaire questions, and the refreshed cross-refs.

---

## 6. Anti-patterns specific to this page

In addition to design-system v3 §21 + proof-architecture §3/§4/§8:

| Don't | Why |
|---|---|
| Claim or display ISO 27001 / SOC 2 / IEC 62443 / 21 CFR Part 11 (or any framework) before it is formally earned | The honesty is the trust signal (§6); §2.7 — framework claims without the cert are a procurement red flag. |
| Use cybersecurity-vendor language: "military-grade", "zero trust", "next-gen", "bank-grade", "AI-driven security" | §2.7 vocabulary-that-backfires — almost always reads as overclaim to a reviewer. |
| Leak roadmap / future-state onto the page (vulnerability-disclosure policy, incident-response process, cert timelines, **Certificate Trust Center, last-known-good rollback**) | The page's strength is architectural confidence without roadmap leakage; ADR-0022 + ADR-0025 are Proposed, not shipped (P3 — never future-tense them here). Those become their own pages once the programs exist (§8). |
| Claim the full EdgeConnect sink matrix as available today | Northbound today = MQTT + OPC UA Server; HTTP / TCP sinks are roadmap (§3.7 Q1, P-G). |
| Overstate redaction (e.g. "encrypts everything", "zero data ever leaves") | State it precisely: field-level engine, secrets STRIPPED, license EXCLUDED, unclassified fails closed (ADR-0020). Precision is the trust signal, not absolutes. |
| Cybersecurity-cliché imagery: padlocks, shields, hooded hackers, glowing networks, "AI brain" | §3.1 visual lock — restrained OT imagery only. |
| Add a §16 trust cue to this page | This page is the authoritative target, not a consumer (design-system v3 §16; memo v2 §4.0). |
| Customer / competitor names, fabricated metrics, security testimonials | proof-architecture §3/§4/§8; testimonials only once a customer agrees to be quoted on security specifically. |

---

## 7. Sign-off checklist (v1 lock)

- [x] Page copy ~1,500–1,700 words; §4 checklist + §7 Q&A scannable, not prose-bloated
- [x] All 9 sections present; voice consistent with the migrated solution specs + datasheet v5 + homepage v3
- [x] Primary buyer = Procurement / compliance reviewer (§2.7); CTAs "Request a security review" / "Talk to our security lead" (+ posture-doc download), NOT demo/sales
- [x] **NO compliance-framework claim or logo (ISO 27001 / SOC 2 / IEC 62443 / 21 CFR Part 11); honest posture §6 intact incl. "we will not stage a certification mid-sales-cycle"**
- [x] **NO roadmap leakage; Certificate Trust Center (ADR-0022) + last-known-good rollback (ADR-0025) NOT mentioned (Proposed-only; P3)**
- [x] **Every property is a LOCKED decision (CLAUDE.md §3), a shipped capability, or an Accepted spec — secret-redaction framed precisely per ADR-0020 (strip/exclude/fail-closed), not absolutes**
- [x] NO cyber-vendor language ("military-grade" / "zero trust" / "AI-driven security" / "bank-grade")
- [x] Protocol accuracy: northbound MQTT + OPC UA Server today; NO roadmap sinks disclosed on the public page (§3.7 Q1 — destinations "confirmed during the security review")
- [x] Cross-refs current: datasheet v5, homepage v3, architecture-diagram v3, `/platform` + `/architecture` specs; CNC = migrated solution spec
- [x] This page does NOT carry a §16 trust cue (authoritative target); §3.8 diagram cross-refs architecture-diagram-spec-v3
- [x] §3.1 + §8 imagery restrained (no padlocks/shields/hackers/glowing networks); no certification logos anywhere
- [x] §1.4 metadata (`WebPage` + `FAQPage`; no `Product`/cert markup)
- [x] ChatGPT review pass applied

---

## 8. Out of scope for v1

- **Specific compliance-framework claims / logos** — never until certified (§6 honest posture only).
- **Vulnerability-disclosure policy** — its own page once the program exists.
- **Incident-response process documentation** — its own page once the program exists.
- **Backup / recovery integrity validation documentation** — its own page once the program exists.
- **Supply-chain security guarantees** — its own statement once the program exists.
- **Certificate Trust Center UX + last-known-good rollback** — ADR-0022 / ADR-0025, Proposed; surface only once shipped (P3).
- **Penetration-test results** — until conducted and reportable.
- **Customer security testimonials** — until a customer agrees to be quoted on security specifically.
- **The trust-annotated diagram asset itself** — produced from architecture-diagram-spec-v3 by design; this spec briefs it (§3.8), does not draw it.

---

*`/security` Page Spec **v1 LOCKED 2026-06-05** (Security & Operational Trust — content v3; ChatGPT "Approve with changes" applied — P0 roadmap-leak removal, redaction scoped to backups+bundles, §6 no-cert rewrite, +7 questionnaire Q&A, OPC UA row reorder; rollback kept as the shipped config-apply pipeline). Migrates security-page-copy-v2 into the canonical §9 per-page-spec format; folds in the SHIPPED secret-redaction story (ADR-0020 — field-level engine, secrets stripped, license excluded, fail-closed); refreshes stale cross-refs (datasheet v5 / homepage v3 / architecture-diagram v3 / platform + architecture specs); P-G protocol accuracy (MQTT + OPC UA Server today, HTTP/TCP roadmap). PRESERVES the v2 strengths verbatim — "operational trust, not cybersecurity theater", the five commitments, honest compliance posture incl. "we will not stage a certification mid-sales-cycle", "trust is not a feature — it's the architecture". P3 honesty lock: every property is locked/shipped/Accepted; Proposed-only features (Cert Trust Center, last-known-good rollback) excluded; no roadmap leakage; no cyber-vendor language; no earned-cert claims. Primary buyer = Procurement / compliance reviewer (§2.7). /security is the AUTHORITATIVE trust source — does not itself use the §16 cue. Next: static /security mockup (Track A spec+mockup scope). Cites: security-page-copy-v2, buyer-taxonomy-v1 §2.7, platform-principles P3, design-system-v3 §16, proof-architecture-v1 §3/§4/§8, elpis-industrial-intelligence-platform-v5, page-platform-spec-v1, page-architecture-spec-v1 v2.1, architecture-diagram-spec-v3, ADR-0020, CLAUDE.md §3.*
