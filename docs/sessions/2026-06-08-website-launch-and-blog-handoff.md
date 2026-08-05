<!--
File:    docs/sessions/2026-06-08-website-launch-and-blog-handoff.md
Purpose: Session handoff — public website went live at www.elpisitsolutions.com
         (replacing the old Angular site), plus a 10-article Industrial
         Intelligence Blog. Captures what shipped, how it's deployed, the
         operational gotchas, and what's pending.
Status:  Handoff. Read this before continuing the marketing-web / blog track.
Date:    2026-06-08
-->

# Handoff — Public website launch + Industrial Intelligence Blog

## TL;DR
Two big things shipped this session:
1. **The static marketing site went LIVE at `www.elpisitsolutions.com`**, replacing the old Angular site. Hosting: **MochaHost "Swift Web Hosting" (Windows / IIS), cPanel**.
2. A **10-article Industrial Intelligence Blog** at `/blog/<slug>`, with a consistent designed image system, wired into nav + sitemap.

The site corpus lives at `docs/marketing/web/` (now a real production site, not just mockups). Deploy = upload that folder's contents to `public_html/` via cPanel File Manager.

---

## 1. Website launch (Waves A–D)

Plan: `docs/sessions/2026-06-06-go-live-plan-v1.md`. Decisions: ship the **static** set now (not the roadmap's Angular SSR app), **simplify the download gate**, deploy to **existing infra**.

| Wave | PR | What |
|---|---|---|
| Plan | #116 | Go-live plan |
| A | #117 | Stripped 37 mock banners; removed the non-functional `gate.js` (downloads are direct now); resolved 193 `href="#"`; built **privacy.html + terms.html**; production footer (3 real offices, LinkedIn `company/2769487`); added the real `pitch-deck-v7.pptx` |
| B | #118 | favicon set, OG image (`assets/og-default.png`), canonical + OG/Twitter on all pages, `sitemap.xml`, `robots.txt`, branded `404.html` |
| C | #119 | **`web.config`** (IIS): strip-`.html` pretty URLs, 11 old-site **301 redirects**, custom 404. Stripped `.html` from ~1,781 internal links + canonicals |
| D | #120 | **Plausible** analytics on all pages (cookieless, no consent banner) |

**Live status (confirmed by user):** homepage, pretty URLs, old-URL redirects, downloads, custom 404, Plausible (saw real visitors), Google Search Console sitemap accepted.

### web.config — important operational history
MochaHost terminates SSL at a proxy, so **server-side redirect rules loop**. Two rules were added then **removed** after causing `ERR_TOO_MANY_REDIRECTS`:
- **force-https** (#121 added → #122 removed): `{HTTPS}` is always OFF inside IIS → infinite loop.
- **non-www → www** (#121 added → #122 removed): proxy passes the bare host for all requests → loop.

➡️ **Do NOT re-add server-side HTTPS or non-www redirects to `web.config`.** HTTPS is handled by MochaHost infra. The **non-www → www redirect is still TODO** and must be done at the **cPanel/MochaHost level** (cPanel Redirects, or ask MochaHost support "how to redirect bare domain to www on Windows IIS"). Both `elpisitsolutions.com` and `www.` currently serve; canonical tags already point to `www`, so it's an SEO nicety, not a blocker.

Current `web.config` rules (safe): canonical strip-`.html`, **blog** pretty-URL rules, 11 old-site 301s, generic pretty-URL rewrite, custom 404. No HTTPS/non-www rules.

### Old-site 301 map (in web.config)
`/home`→`/`, `/Our-Software-Assistance/e-on`→`/edgeconnect`, `/e-remos`→`/eremos-v2`, the 5 `/IOT-Solutions/*` hardware → product pages, `/our-services/*`→`/`, `/about`→`/`, `/privacy-policy`→`/privacy`.

---

## 2. Operational gotchas (READ before "it's not updating")

- **CSS cache-busting:** the stylesheet link is `styles.css?v=N`. **Bump N on every `styles.css` change** (currently **`?v=4`**) so returning visitors get it without a hard refresh. (We hit this — a CSS change looked "not deployed" because the browser cached the old `styles.css`.)
- **Diagnosing "didn't update":** open `https://www.elpisitsolutions.com/styles.css` and Ctrl-F for the newest CSS comment (e.g. `BLOG v6`); if present, it's browser cache → hard refresh / it's the `?v` bump.
- **Images** are NOT version-querystringed. Overwriting an image with the same filename can show stale until hard-refresh. Acceptable for low traffic; rename or add `?v` if it matters.
- **Search Console:** submit the **`www`** sitemap: `https://www.elpisitsolutions.com/sitemap.xml`. The bare-domain version showed the OLD site.
- **Local preview is unreliable:** the `marketing-web` preview server (launch.json) serves `index.html` for ALL paths (SPA fallback), so screenshots show the homepage regardless of URL. **Verify pages via direct HTTP fetch** (`python -m http.server` on a temp port + urllib) or on the live site, not the preview screenshot tool.
- **Bash cwd** resets to repo root between some calls; use absolute paths or `cd` within the command.

---

## 3. The Industrial Intelligence Blog (10 articles)

**Name:** "Industrial Intelligence Blog", tagline "Insights from the shop floor". **URL:** `/blog/` (index) + `/blog/<slug>`.

### Structure
- Files in **`docs/marketing/web/blog/`**: `index.html` + 10 article pages. They use **root-relative links** (`/styles.css?v=4`, `/edgeconnect`, `/blog/...`, `/assets/...`) because they live one level deep.
- **`web.config`** has two blog rules: `blog-strip-html` (301 `/blog/x.html`→`/blog/x`) and `blog-pretty` (rewrite `/blog/x`→`blog/x.html`). `/blog/` serves `blog/index.html` via default document.
- "Blog" is in the **main nav** (after Security) and the footer EXPLORE column, on all pages.
- Each article has **Article JSON-LD**, canonical, OG/Twitter. All 10 are in `sitemap.xml` (**51 URLs total**).

### The 10 articles
1 EdgeConnect · 2 How to Calculate OEE · 3 EREMOS V2 · 4 Defensible OEE Reports · 5 Condition Monitoring vs PdM · 6 Industrial Protocols Explained · 7 Brownfield Industry 4.0 · 8 Canonical Data at the Edge · 9 Store-and-Forward in IIoT · 10 FANUC CNC Data Collection.

PRs: #124 (Batch 1: 4) · #128 (Batch 2: 3 + OPC UA fix on Article 1) · #131 (Batch 3: 3) · image/layout: #125, #126, #127, #129(closed), #130, #132.

### Content discipline (how articles are made)
Cadence: **draft text → ChatGPT review pass → apply corrections → build page**. Batches of ~3. Every article holds the site-wide honesty rules:
- No fabricated metrics / OEE %/$/benchmarks; no customer or competitor names.
- Protocol status verbatim — **collection:** FANUC FOCAS2, MTConnect, Brother HTTP, Modbus TCP, Siemens S7, and **OPC UA Client** (reads from external OPC UA Servers); **output:** MQTT publishing + **EdgeConnect's own OPC UA Server**. **MT-LINKi REST = roadmap.**
- Canonical mapping conditional ("maps supported readings where the required values are available").
- Store-and-forward wording verbatim: "designed to preserve data through supported outage scenarios where buffering is configured, and to make gaps visible instead of hiding them" — **never** "never loses data".
- Condition monitoring / PdM = **early-warning aid, not a guarantee**. VAS "captures vibration signatures, with measurement schedule and analytics configured per machine class". E-IDOS = hydraulic/lubrication.
- Beside-not-replacing SCADA / historian / MES / HMI / PLC / CMMS. AI = decision-support, never in the data path.

### Image system
- **Two images per article:** `<slug>-thumb.png` (1200×900, 4:3 — the index card) and `<slug>-hero.png` (1600×900, 16:9 — the article header).
- **Style:** clean schematic diagrams, deep-navy bg + cyan accents, no logos, no fake numbers/screenshots, EdgeConnect shown as **software** (not a hardware box). All 10 now share this one designed style.
- **How they're produced:** the user generates them from a corrected brief (a reusable **STYLE BLOCK + NEGATIVE BLOCK + per-image CONTENT**, one image per request). Claude QCs each at full resolution (label spelling, accuracy, governance) before wiring. The brief + per-image prompts are in this session's chat history; re-derive from an existing hero if needed.
- **Card CSS (BLOG v6):** horizontal rows; thumbnail column 320px, `object-fit: contain` on a navy panel (shows the whole diagram, no crop); excerpt clamped to 2 lines. Article header shows the full 16:9 hero.

---

## 4. Deploy checklist (to push the latest state live)
Upload to `public_html/` via cPanel File Manager (overwrite):
- **`blog/`** (index + 10 articles), **`assets/blog/`** (20 images), `web.config`, `styles.css`, `sitemap.xml`, `robots.txt`, `404.html`, and any changed root `*.html` (nav/footer touch every page).
- Then **re-submit** `https://www.elpisitsolutions.com/sitemap.xml` in Search Console.

---

## 5. Pending / follow-ups
- **non-www → www redirect** at cPanel/MochaHost level (NOT web.config — it loops). SEO nicety.
- **Tuesday LinkedIn drip** (1 article/week), suggested order: FANUC → EdgeConnect → OEE calc → Protocols → EREMOS → Condition Monitoring → Brownfield → Canonical → Defensible OEE → Store-and-Forward. Social person owns posting; Claude can draft captions.
- **More blog batches** when wanted — remaining topic bench: Siemens S7 how-to, Brother CNC (niche/easy-rank), Production Monitoring vs MES, Modbus→MQTT, OEE accuracy deep-dive, vibration-signatures (careful), oil-analysis/ISO-4406 (confirm E-IDOS capability first — flagged risky).
- **Download gate** is currently visual-only/removed (direct downloads). Functional lead-capture/CRM behind a gate = **Phase 4**.
- **Phase 4 backlog** (unchanged): functional lead capture, /about + Partners/Careers pages (currently dropped from footer), named case studies (need sign-off), /pricing, product-specific datasheets, design-finished collateral PDFs, A/B testing, i18n.
- **Collateral PDFs** (brochures/whitepapers/datasheet) are honest content renders, not design-finished.

---

## 6. Governance anchors (unchanged, still binding on every public page)
No fabricated metrics; no named customers/logos (defense/space-agency anchors are reproduced **verbatim**, unelaborated, and authorized for public use); no competitor names on public pages; no formal certification claims (IP65/67-**compatible** only); EdgeConnect = software, hardware = Edge Gateway/mDAQ/mTracker/VAS/E-IDOS; AI out of the data path; beside-not-replacing; per-plant identity. See `docs/platform-principles.md` + `docs/decisions/`.
