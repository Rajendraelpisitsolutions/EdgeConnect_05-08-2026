<!--
File:        docs/marketing/security-page-copy-v1.md
Purpose:     Full page copy for /security on the Elpis website. The
             enterprise-trust asset for industrial IT leads, CISOs, OEM
             procurement reviewers, and regulated-industry compliance
             officers.
Audience:    Web designer + developer building the page; the user signing off
             before publication.
Format:      Markdown page copy. Sections follow the same pattern as the
             homepage and CNC solution page: copy + visual notes + reader-
             effect notes.
Version:     v1 (first draft)
Date:        2026-05-24

Positioning: "Operational trust" as a category concept. NOT cybersecurity
theater. NOT compliance keyword stuffing. NOT "military-grade security"
language. NOT "zero trust" buzzword theater. Instead: engineering-led,
deployment-aware, production-continuity-first trust architecture that
very few OT vendors articulate clearly.

Source narratives:
  - docs/marketing/elpis-industrial-intelligence-platform-v4.md (canonical product)
  - docs/marketing/website-messaging-architecture-v2.md §11 (security page outline)
  - CLAUDE.md §1, §3 (locked architectural decisions — especially #5, #6,
    #7 on licensing and #14–#17 on AI)
  - docs/platform-principles.md P3 (Security spec-first)
  - shared-knowledge/contracts/opcua-namespace-policy.md (OPC UA security
    modes, X.509 cert management — technical reinforcement)
  - shared-knowledge/architecture-overview.md (offline operation, store-
    and-forward)

Hard rule for this page: never claim a certification we haven't earned.
Never imply compliance frameworks we don't actually meet. The honesty
is the trust signal.
-->

# Security and Operational Trust — Page Copy v1

**Page URL:** `/security`
**Primary audience:** Industrial IT lead, CISO, OEM procurement reviewer, regulated-industry compliance officer
**Secondary audience:** Enterprise architect evaluating the platform for fleet deployment

---

## §1 Hero

### Copy

> ### Built for the security posture your plant actually has.
>
> Offline operation. Air-gapped readiness. Signed offline licensing. An AI model that proposes — never alters. Trust architecture engineered for OT, not adapted from IT.
>
> [ Talk to our security lead ]   ·   Request a security review

### Visual notes

- **Full-bleed hero image:** a closed control cabinet, dim lighting, no people. Implies isolation, locked perimeter, calm reliability — not aggressive cybersecurity imagery (no padlocks, no shields, no glowing networks).
- **Avoid:** stock cybersecurity art, "hooded hacker" aesthetics, abstract "AI brain with circuits" imagery, military-grade visual language.
- **Headline at display weight.** Subhead at body size, slightly tighter than homepage to signal seriousness.
- **Primary CTA:** accent-color button. Secondary CTA as text link.

### Reader-effect notes

- The reader (security reviewer) should feel *"this page is going to answer my actual questions, not pitch at me."*
- The "trust architecture engineered for OT, not adapted from IT" line is the central frame — it signals that the platform was designed against OT constraints from day one, not retrofitted with security after a cloud-first product met a regulated-industry deployment.

---

## §2 The challenge

### Copy

> ### Why OT software fails security review
>
> Most industrial software was designed in an IT world and patched for an OT one. The architecture assumed: a live internet connection for licensing, a cloud back-end for telemetry, a third-party AI service for "intelligence," and a refresh cycle measured in days. Then the software met a real factory.
>
> Real factories don't have any of that. They have isolated control networks. They have plants that cannot be patched mid-shift. They have customers with strict no-cloud policies, export-control regimes, defense contracts, regulated data, or simply a CISO who reads vendor contracts carefully. The IT-shaped software starts requiring exceptions: a special license-server arrangement here, a cloud-bypass mode there, an air-gap workaround over there. Each exception is a compliance bill in waiting.
>
> The right answer isn't to ship the same IT-shaped software with a longer security questionnaire response. It's to design the trust architecture into the platform from the first commit. That's what Elpis did.

### Visual notes

- **Three narrative paragraphs.** No bullet lists in this section.
- **Optional pull-quote in the margin:** *"Each exception is a compliance bill in waiting."*
- **Subdued visual treatment.** This is the empathy section — quiet, deliberate.

### Reader-effect notes

- The reader (CISO, IT lead) should feel *"this person has actually sat through a vendor security review."*
- The "license-server arrangement / cloud-bypass mode / air-gap workaround" trio is doing real recognition work — every enterprise OT buyer has been through that exception-stacking exercise.

---

## §3 The Elpis approach

### Copy

> ### Operational trust, not cybersecurity theater
>
> **Production continuity is non-negotiable.** A platform that stops a production line because its license server is unreachable, its cloud back-end is down, or its AI service is rate-limited has failed its core operational responsibility. EdgeConnect runs offline by default, validates its license locally, and never depends on a remote service to keep machine data flowing.
>
> **Air-gapped operation is the default, not a fallback.** EdgeConnect does not require an internet connection at any point in its lifecycle — installation, licensing, configuration, telemetry, or update. Plants on isolated OT VLANs install and run the platform exactly the same way as plants with internet access. There is no "offline mode" because there is no "online mode" — there is just operation.
>
> **AI proposes; humans decide; the data path stays deterministic.** When AI features ship (Diagnostic, Configuration, Tag Mapping, and Intelligent Alerting agents), they generate proposals for operators to confirm. They do not silently alter routing, transform data, or change configuration. Local-LLM support is mandatory; cloud LLMs are optional. No plant data is sent to a third-party AI service by default.
>
> **Audit trail as system property, not feature.** Every configuration change to the gateway is recorded in a hash-chained log with actor identity and timestamp. The log is tamper-evident and replay-ready — not bolted on for regulated-industry sales, but built into the configuration plane itself.
>
> **Licensing that never cuts customer data.** A lapsed license blocks configuration changes; it never stops production data from flowing. Your machines keep talking even if your renewal email got stuck in someone's spam folder. The license enforces the commercial relationship — it does not hold operations hostage.

### Visual notes

- **Five bolded-lead paragraphs.** Each lead is a philosophical commitment; the body explains how it manifests in the platform.
- **Optional small inline icons** next to each bolded lead — a shield for continuity, a disconnected-cloud for air-gap, a balanced-scales for AI, a chain-link for audit, a key for licensing. Restrained iconography only.
- **Medium-high density section.** This is doing the heaviest philosophical work on the page.

### Reader-effect notes

- The reader should feel *"these are architectural decisions, not marketing positions."*
- The "no online mode" inversion in the air-gap paragraph is intentional — it reframes offline from a degraded state into the canonical state.
- The licensing paragraph is one of the strongest individual trust signals on the page. CISOs read vendor licensing terms carefully; "never holds operations hostage" earns trust immediately.

---

## §4 What sets Elpis apart on security

### Copy

> ### Specific properties to evaluate
>
> Use this section as a checklist against any other OT vendor you're considering. Each item is an architectural property of the platform, not an aspirational claim.
>
> - **Offline-first by design.** EdgeConnect does not require an internet connection to run, validate its license, or deliver data.
> - **Air-gapped factories are first-class.** RSA-signed JSON license files validate locally. No phone-home. No cloud licensing dependency. Updates ship on physical media or internal mirrors.
> - **No forced cloud dependency.** The platform can be entirely on-premise. Cloud destinations are optional, not required.
> - **AI never alters the data path.** AI agents propose; humans confirm. No silent state changes. Local-LLM support is mandatory; cloud LLMs are optional.
> - **Hash-chained configuration audit log.** Every change recorded with actor and timestamp. Tamper-evident.
> - **Per-tag quality codes.** Every data point carries a quality state (Good / Uncertain / Bad / Stale). Downstream systems can distinguish a real value from a stale or unreliable one.
> - **Role separation supported.** Configuration editing, audit review, and data consumption can be assigned to distinct roles. No single operator holds every privilege by default.
> - **Per-gateway identity.** Each EdgeConnect instance carries a stable UUID and customer/site binding established at first start. Fleet identity is clean and traceable.
> - **OPC UA Server security modes.** When the OPC UA Server is enabled, anonymous + SecurityMode=None is supported for commissioning on trusted OT VLANs; Sign / SignAndEncrypt with X.509 certificate-based authentication is available for production deployments.
> - **Application certificate management.** The OPC UA Server auto-generates a self-signed X.509 application certificate on first start; client trust is operator-controlled via standard trust folders.

### Visual notes

- **Single bulleted list, two-column on desktop, single-column on mobile.**
- **Bolded property name + plain-body description.** Each bullet is scannable in 5 seconds.
- **Optional:** small "checkbox" or "shield-check" icon next to each item to reinforce the "use this as a checklist" framing.
- **No claims of certifications.** Every bullet is an architectural property, not a compliance statement.

### Reader-effect notes

- The reader should be able to walk this list into a security review meeting and point at specific platform properties.
- The OPC UA Server security detail is unusually specific — it signals that the platform's enterprise-integration surface has been built with security primitives, not bolted on.

---

## §5 What the platform does NOT do

### Copy

> ### The negative space matters
>
> Honest framing of what the platform refuses to do is sometimes more useful than what it does:
>
> - **Does not phone home for telemetry or licensing.** Period.
> - **Does not require cloud connectivity.** Cloud is opt-in. The platform runs identically with no external network access.
> - **Does not send plant data to third-party AI services by default.** Local-LLM-capable is the default posture.
> - **Does not bypass operator confirmation for AI-proposed actions.** AI agents never silently alter state.
> - **Does not auto-update.** Updates are operator-controlled. Plants schedule updates against their own shift calendar.
> - **Does not allow a single shared credential.** Role separation is supported from day one.
> - **Does not depend on a vendor cloud service for any production-critical path.** Buffering, replay, alarm capture, and OEE inputs all work with the cloud entirely unreachable.

### Visual notes

- **Single bulleted list.** Same density as §4.
- **Visual contrast from §4 helpful** — consider a slight palette shift or visual rule between sections, so the reader registers "this is the inverse side of the trust story."
- **Each bullet leads with "Does not"** for rhythm consistency.

### Reader-effect notes

- The reader should feel *"these refusals are the trust signals — what the vendor refuses to do tells me more than what they claim."*
- This section is doing as much credibility work as §4. CISOs notice negative space.

---

## §6 Compliance and certification posture

### Copy

> ### Honest framing on certifications
>
> Elpis is building the operational primitives — audit trails, role separation, signed licensing, offline-first behavior, deterministic data path — that regulated-industry deployments require. Specific compliance certifications (ISO 27001, SOC 2, IEC 62443, 21 CFR Part 11) are evaluated on a per-deployment basis as our customer mix matures.
>
> If your industry requires a specific certification or audit posture, talk to us about your timeline and we will be transparent about where the platform stands today. We will not claim a certification we have not earned, and we will not stage one mid-sales-cycle to win a deal.
>
> **What you can verify today** — without us claiming any specific framework:
>
> - The configuration audit log produces a hash-chained, tamper-evident record suitable for regulated-industry change-control review.
> - The platform's offline-first architecture supports air-gapped deployments and export-control-sensitive environments.
> - The AI constraint architecture (proposals + human confirmation + local-LLM support) supports environments where data sovereignty is non-negotiable.
> - The per-tag quality codes and three-way diagnostics support investigations into data provenance and integrity.

### Visual notes

- **Three short prose paragraphs**, then a bulleted "what you can verify today" list.
- **No certification logos.** Do not display ISO, SOC 2, IEC, FDA, or similar logos until they are actually earned.
- **Visual treatment should feel calm and confident** — not defensive, not aspirational.

### Reader-effect notes

- The reader should feel *"these people are not going to oversell me on compliance theater."*
- The honesty is itself a competitive differentiator — vendors who name specific frameworks they haven't certified get burned in procurement; vendors who are honest get respected.
- The "we will not stage one mid-sales-cycle to win a deal" line is direct, deliberate, and earns trust the way nothing else on the page does. Worth keeping even if it feels blunt.

---

## §7 Common security-review questions answered

### Copy

> ### Questions your security team is about to ask
>
> If you're about to run this platform through your vendor security review, here are the answers to the questions that will come up:
>
> - **"Where does our plant data go?"** Wherever you route it. EdgeConnect publishes to MQTT brokers, OPC UA clients, or HTTP / TCP sinks of your choosing. Nothing is sent to Elpis. No telemetry is collected for our visibility.
> - **"What happens if the license expires?"** Configuration changes are blocked; data keeps flowing. Production continuity is not affected by a licensing event.
> - **"Can your software run completely offline?"** Yes. Installation, licensing, configuration, runtime, and updates can all happen with no internet access.
> - **"How do you handle credentials and secrets at the edge?"** Source credentials and sink credentials are stored encrypted at rest on the edge node. Operator credentials for the Connectivity Studio UI follow standard role-based access control.
> - **"What happens if a config change breaks the gateway?"** The draft → validate → apply → rollback flow makes every config change reversible. Untested config never reaches the data path.
> - **"Can a single operator do everything, or do you support role separation?"** Role separation is supported. Configuration editing, audit review, and data consumption can be assigned to distinct roles.
> - **"What's the audit trail for configuration changes?"** Hash-chained log with actor identity and timestamp for every change. Tamper-evident, exportable for review.
> - **"How does your AI work — does it need our data?"** AI agents propose actions for human confirmation. Local-LLM support is mandatory; cloud LLMs are optional. By default, no plant data leaves your environment for AI purposes.
> - **"Can we run a security review against the platform before purchase?"** Yes. We support pre-purchase security review against a representative deployment.

### Visual notes

- **Each question as a bold pull-quote**, answer in regular body weight underneath.
- **Generous line spacing.** These are scan-bait, not paragraphs.
- **Optional small icon** next to each question — a question-mark variant for visual cue.

### Reader-effect notes

- This section converts the security reviewer from "evaluating" to "ready to schedule the review call."
- Questions are written in the exact language a real security questionnaire uses — recognizable to anyone who has filled one out.

---

## §8 Architecture for trust

### Copy

> ### Where trust lives in the platform
>
> [ branded SVG diagram — variant of the master architecture diagram from `architecture-diagram-spec-v2.md`, annotated with trust properties: *"License validates locally"* at EdgeConnect; *"Hash-chained audit"* at the Configuration plane; *"AI proposals require human confirm"* at any AI agent boundary; *"Per-tag quality codes"* on every data arrow ]
>
> *Every layer of the platform has a trust property. Offline operation at the edge. Signed local licensing. Hash-chained configuration audit. AI proposals at the intelligence layer require human confirmation. Per-tag quality codes propagate end-to-end. There is no "trust feature" because trust is not a feature — it's the architecture.*

### Visual notes

- **Trust-annotated variant of the master architecture diagram.** Same structure as the master, but with small callout labels highlighting where each trust property lives.
- **If a trust-annotated diagram doesn't exist yet,** use the master architecture SVG with overlay callouts.
- **Caption in italic** beneath the diagram.

### Reader-effect notes

- The reader should feel *"trust is structural, not bolted-on."*
- The closing sentence — *"trust is not a feature — it's the architecture"* — is the central philosophical anchor of the page. Worth visual prominence.

---

## §9 Final CTA

### Copy

> ### Talk to our security lead.
>
> Bring us your security review questionnaire. Bring us your air-gap requirement. Bring us your regulated-industry constraint. We will tell you exactly what the platform does today, what it does not do, and what we are willing to commit to for your deployment.
>
> [ Request a security review ]   ·   [ Talk to our security lead ]

### Visual notes

- **Centered, generous whitespace.** Same pacing as the homepage and CNC solution page final CTAs.
- **Two primary CTAs** here (unusual — usually one primary, one secondary) — because the security audience splits cleanly between "I need to schedule a review" and "I have a specific question."
- **Headline at display weight**, slightly smaller than the page hero.

### Reader-effect notes

- The "Bring us your X" framing localizes the homepage CTA pattern to the security audience.
- *"We will tell you exactly what the platform does today, what it does not do, and what we are willing to commit to"* is doing real trust work. It promises honesty, not capability.

---

## Section-by-section word count summary

| Section | Words (approx) | Notes |
|---|---|---|
| §1 Hero | 50 | Headline + subhead + CTAs |
| §2 The challenge | 200 | Three narrative paragraphs |
| §3 Elpis approach | 290 | Five bolded-lead paragraphs |
| §4 What sets Elpis apart | 250 | Bulleted property list |
| §5 What the platform does NOT do | 130 | Bulleted "negative space" list |
| §6 Compliance posture | 180 | Three paragraphs + verification bullets |
| §7 Common review questions | 280 | Nine Q&A pairs |
| §8 Architecture for trust | 80 | Diagram + caption |
| §9 Final CTA | 70 | Headline + body + CTAs |
| **Total page copy** | **~1,530 words** | Plus diagram + visual elements |

Slightly less dense than the CNC solution page — security reviewers read carefully, so the page is sized to be skim-able by skim-readers and detail-able by detail-readers in one pass.

---

## Visual / pacing guidance summary

- **Pacing:** hero calm → challenge intimate → approach philosophical → property checklist dense → negative space dense → compliance honest → review questions scannable → architecture visual → CTA confident
- **Imagery:** restrained throughout. No cybersecurity clichés (no padlocks, hooded hackers, shields, glowing networks). Real control cabinets, clean architecture diagrams, calm typography.
- **Palette:** same dark premium industrial as homepage and CNC solution page. The Compliance section is a candidate for a subtle palette shift (lighter background, more whitespace) to signal calm honesty.
- **Mobile:** every section tested at 375px wide. The §7 questions stack vertically; the §4 / §5 lists collapse cleanly.

---

## What's out of scope for v1

- **Specific compliance framework claims** — never until certified
- **Cybersecurity-vendor language** ("military-grade," "zero trust," "next-gen security") — banned
- **Penetration test results** — until conducted and reportable
- **SOC 2 / ISO 27001 / IEC 62443 / 21 CFR Part 11 logos or claims** — until earned
- **Customer security testimonials** — until customers are ready to be quoted on security specifically
- **Detailed vulnerability disclosure policy** — separate document, linked from this page once written
- **Penetration test or red-team engagement program** — future operational program, not v1 copy

---

## Sign-off checklist

Before this page goes into production:

- [ ] Reviewed against datasheet v4, homepage copy v2, and CNC solution page v2 for voice consistency
- [ ] No compliance framework claims that haven't been formally earned
- [ ] No cybersecurity-vendor language ("military-grade," "zero trust," etc.)
- [ ] Every architectural property claim traces to CLAUDE.md §3 or shared-knowledge contracts
- [ ] Architecture diagram variant (trust-annotated) approved (designer brief from `architecture-diagram-spec-v2.md`)
- [ ] §7 security-review questions reviewed by Elpis sales lead and at least one customer-facing engineer — language must match real procurement questionnaires
- [ ] §6 compliance-posture text reviewed by user — the "we will not stage a certification mid-sales-cycle" line is intentionally blunt; confirm acceptable
- [ ] CTA destinations confirmed (security review request form + security lead contact)
- [ ] Page tested at 375px mobile width
- [ ] No certification logos on the page

---

*Security and Operational Trust — Page Copy v1, 2026-05-24. Derived from datasheet v4, website messaging architecture v2 §11, CLAUDE.md §3 locked decisions, and platform-principles.md P3. Establishes "operational trust" as a category concept for industrial software trust positioning.*
