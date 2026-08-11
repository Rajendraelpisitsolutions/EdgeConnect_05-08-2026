<!--
File:    docs/sessions/2026-06-06-go-live-plan-v1.md
Purpose: Pre-launch / go-live plan (plan-trail v1) for taking the static
         marketing mockups public at www.elpisitsolutions.com, replacing the
         existing site. For user + ChatGPT review before the hardening waves.
Status:  v1 DRAFT — awaiting review + the open inputs in §3.
Date:    2026-06-06
-->

# Go-live plan v1 — ship the static marketing site to www.elpisitsolutions.com

**Goal:** replace the existing site with the 38-page static set we built, hardened to production. This plan sequences the work into waves and lists the inputs still needed.

---

## 1. Decisions locked (user, 2026-06-06)

1. **Launch path:** ship the **static pages as live v1 now**; build the Angular SSR app (roadmap intent) as a later modernization. *(Divergence from web-platform-roadmap §2's Angular-first plan is deliberate and recorded here.)*
2. **Download gate at launch:** **simplify for v1** — drop the non-functional capture form; collateral downloads directly (or routes to a contact/mailto). Functional lead-capture/CRM stays Phase 4.
3. **Hosting:** **existing infrastructure** — deploy to wherever elpisitsolutions.com is hosted today (stack TBD — §3).

---

## 2. What "production v1" means (scope)

The static corpus, hardened: mockup scaffolding removed, every link real or removed, legal pages present, SEO/analytics wired, pretty URLs + redirects, a final honesty/legal sign-off — deployed to the live domain with 301s from old URLs. No backend beyond a contact path. No CMS. No Angular (later).

---

## 3. Open inputs needed from the user (block specific waves)

| # | Need | Blocks |
|---|---|---|
| I1 | **Hosting stack** for elpisitsolutions.com today (static host? CMS/WordPress? server? who controls DNS?) | Wave D (deploy/cutover) |
| I2 | **Privacy + Terms** content — provide existing legal copy, or approve placeholder legal text to start | Wave A (legal pages) |
| I3 | **Footer links** About / Partners / Careers — build minimal pages, or **drop from footer for v1**? (recommend: drop for v1; add when content exists) | Wave A |
| I4 | **Real company/contact details** — legal entity, address, phone, support email, real LinkedIn/social URLs | Wave A |
| I5 | **Public-claims sign-off** — confirm the trust anchors (incl. "Deployed in defense and space-agency programs.") are authorized for the *public* site | Final gate |
| I6 | **Overview deck asset** — supply the real `pitch-deck-v7.pptx` (or a PDF), or drop the deck download | Wave A (broken link) |
| I7 | **Old-site URL list** — to build the 301 redirect map | Wave C/D |
| I8 | **Analytics choice** — Plausible vs GA4 (+ account/property) | Wave B |

---

## 4. Work plan — hardening waves

### Wave A — Strip the mockup + fix links (host-agnostic; can start now)
- Remove the **37 "STATIC MOCKUP" banners** + the `.mock-banner` blocks/CSS, "filters visual-only" notes, and any dev-only labels.
- **Simplify the gate** (decision #2): remove `gate.js` interception (or repoint to a contact path); collateral becomes a direct download; "Request access" stays a `mailto`/contact link. Update the resources spec §2/§10 amendment to record "gate deferred to Phase 4."
- **Resolve the 193 `href="#"`**: build **Privacy** + **Terms** (I2); drop About/Partners/Careers from the footer (I3) or stub them; wire real LinkedIn/social (I4).
- **Fix the broken overview-deck link** (I6).
- Real **company/legal footer** (I4): entity, address, contact.
- Add a **404 page**.

### Wave B — SEO + quality (host-agnostic)
- `favicon`, `sitemap.xml`, `robots.txt`, canonical `<link>` per page (from the specs' §1.4), OG + Twitter share image(s).
- Wire **analytics** (I8) — page views + CTA clicks.
- **Lighthouse ≥90** (Perf/A11y/Best-practices/SEO) + **WCAG AA** pass; fix what it flags.
- Confirm referenced assets resolve (logos, datasheet PDF, the 9 collateral PDFs).

### Wave C — URL structure + redirects
- Mockups are `platform.html`, `solutions-cnc-machining.html`; specs' canonical URLs are pretty (`/platform`, `/solutions/cnc-machining`). Choose ONE:
  - **(a) Host rewrites** — keep flat files, configure clean-URL rewrites at the host (simplest if the host supports it), **OR**
  - **(b) Folderize** — restructure to `/<path>/index.html` so paths are pretty without rewrites.
  - *Recommend (a) if the host supports clean URLs; else (b).* Then update internal links to match.
- Build the **301 redirect map** from old-site URLs (I7) → new URLs.

### Wave D — Deploy + cutover (needs I1)
- Stand up the deploy to the existing infra (I1): TLS, CDN/caching, the rewrite/redirect rules.
- Stage on a preview URL → smoke-test every page + every download + analytics firing.
- **DNS cutover** to the new site; verify 301s; monitor.

---

## 5. Final pre-launch gate (sign-off before DNS cutover)
- [ ] No "STATIC MOCKUP"/placeholder artifacts anywhere; 0 dead links (incl. assets).
- [ ] Privacy + Terms live; footer legal/company details real.
- [ ] Honesty pass: no fabricated metrics, no named customers, no competitors, no cert claims; trust anchors verbatim **and** authorized for public (I5); protocol status verbatim.
- [ ] SEO basics present (sitemap/robots/canonicals/favicon/OG); analytics firing.
- [ ] Lighthouse ≥90 + WCAG AA.
- [ ] 301s from all old URLs; nav/footer identical across pages; mobile + desktop verified.

---

## 6. Recommended immediate next step
Start **Wave A** (host-agnostic, reversible) as soon as I2/I3/I4/I6 are answered — it's the bulk of the visible hardening and needs no hosting decision. Waves B–C can largely proceed in parallel; Wave D waits on the hosting stack (I1) + old-URL list (I7).

*Go-live plan **v1 DRAFT** — decisions #1–#3 locked; eight open inputs (§3) gate specific waves. Ship-static-v1 deliberately diverges from the roadmap's Angular-first plan (recorded). Awaiting review, then Wave A.*
