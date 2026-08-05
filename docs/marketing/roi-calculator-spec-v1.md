<!--
File:        docs/marketing/roi-calculator-spec-v1.md
Purpose:     Specification for the Elpis Industrial Intelligence Platform ROI
             calculator — the math, inputs, outputs, UX guidance, and discipline
             rules. This is a spec a developer + spreadsheet author can build
             the calculator from. NOT the calculator itself.
Audience:    Marketing team (lead the deliverable), web developer (builds the
             web variant), spreadsheet author (builds the Excel/Google Sheets
             template), sales team (uses it in conversations).
Format:      Markdown spec.
Version:     v1 (first draft)
Date:        2026-05-24

Source narratives:
  - docs/marketing/elpis-industrial-intelligence-platform-v4.md
    § "Typical value areas" (the credible value buckets)
  - docs/marketing/SESSION_HANDOFF.md
    §5 (the ROI quantification angles from the original handoff)

Locked-truth sources: see datasheet v4 header. Every benefit the
calculator computes must trace back to a capability the platform
actually ships.

Hard discipline rule: this calculator MUST NOT fabricate value. Every
output is a function of inputs the user supplies. The calculator
provides the math, the user provides the operating constants. No
"typical 30% downtime reduction" assumptions baked into outputs.
-->

# ROI Calculator — Specification v1

**A business-case worksheet a plant manager can fill in during a 20-minute conversation and walk away with a credible payback estimate.**

The ROI calculator exists to help prospects build their own internal business case for the Elpis Industrial Intelligence Platform — not to make Elpis's pitch louder. It anchors the conversation in the prospect's real production constants and produces a defensible number their CFO can take to a procurement review.

---

## 1. Goals

In priority order:

1. **Produce a defensible payback estimate** from inputs the customer supplies — never from numbers Elpis fabricates.
2. **Anchor every sales conversation in real production constants** — parts per hour, downtime hours, labor rates, current SCADA spend.
3. **Surface the value buckets explicitly** so the prospect understands *why* the platform pays back, not just *that* it does.
4. **Convert technically-interested visitors into qualified leads** by offering the calculator as a gated download (email + company) or a contact-form attachment.
5. **Arm the sales team** with a consistent business-case framework — every Elpis-led ROI conversation uses the same math.

---

## 2. Format and distribution

**Three deliverable formats, in priority order:**

| Format | Purpose | Where it lives |
|---|---|---|
| **Excel / Google Sheets template** (canonical) | Procurement-grade, CFO-credible. The math lives here. Single download, editable, shareable inside the prospect's organization. | `/resources` (gated download) |
| **Web calculator** (lead magnet) | Lower-friction; produces a directional number; entices the visitor to download the Excel for the full model. | `/roi-calculator` (subpage of `/resources`) |
| **PDF worksheet** (sales-meeting handout) | Printable, scribble-on-able. For in-person scoping meetings where the prospect doesn't want a screen between them and the salesperson. | `/resources` (open download) |

**The Excel template is the source of truth.** The web calculator and PDF worksheet derive from it; if the math changes, it changes there first, then propagates.

---

## 3. Inputs

The calculator asks for inputs in three groups: **operating constants**, **current-state pain**, and **assumed improvements**.

### 3.1 Operating constants (the prospect knows these)

| Input | Unit | Notes |
|---|---|---|
| Number of plants in scope | count | Multi-site customers enter total |
| Number of controllers per plant | count | CNCs + PLCs + meters combined |
| Plant operating hours per week | hours | E.g. 120 hours for a 3-shift, 5-day operation |
| Operating weeks per year | weeks | Default 50 (allow 2 weeks for holiday + planned shutdown) |
| Average parts produced per hour (per controller) | parts/hr | Aggregate; can be approximate |
| Contribution margin per part | currency | Marginal revenue minus marginal cost. If unknown, use revenue per part × estimated margin % (default 25%) |
| Fully loaded labor rate (operator) | currency/hour | Wages + benefits + overhead |
| Fully loaded labor rate (engineer) | currency/hour | For driver-build comparison |
| Currency | dropdown | USD / EUR / INR / JPY / GBP / other |

### 3.2 Current-state pain (the prospect estimates these)

| Input | Unit | Notes |
|---|---|---|
| Annual unplanned downtime hours per controller | hours/year | Typical range: 50–300; if unknown, use 150 default with a clear "default" flag in the output |
| Hours per shift spent on manual reporting (per supervisor) | hours/shift | Spreadsheet maintenance, shift handover prep, OEE stitching |
| Number of shift supervisors involved in reporting | count | Plant-wide |
| Number of southbound protocols currently uncovered | count | FOCAS2 / MT-LINKi / MTConnect / Brother HTTP / Modbus TCP / etc. that would otherwise need in-house drivers |
| Months of audit prep saved per major audit | months | Optional input for regulated-industry customers |

### 3.3 Assumed improvements (with caveats — the customer owns these)

The calculator does NOT default these to platform-favorable numbers. The user supplies them, and the worksheet shows the calculation with a sensitivity table.

| Input | Unit | Sensible range to display |
|---|---|---|
| Downtime reduction (fraction) | % | Show outputs at 5%, 10%, 15%, 20% — let the user pick |
| Reporting labor reduction (fraction) | % | Show outputs at 25%, 50%, 75% |
| Engineer-weeks saved per protocol (vs in-house build) | weeks | Default range 4–8 weeks per protocol; user adjusts |

**The web variant** can default to a conservative middle value (e.g. 10% downtime reduction, 50% reporting reduction) but must clearly label these as *"directional estimate based on conservative defaults — adjust for your operation"*.

**The Excel variant** leaves these blank and forces the user to enter their own — preserves credibility.

---

## 4. Outputs

The calculator produces a **value worksheet**, not a single payback number. The buyer sees the math.

### 4.1 Per-bucket annual savings

| Bucket | Formula | Output |
|---|---|---|
| **Downtime savings** | `Annual unplanned downtime hours × Number of controllers × Downtime reduction % × Parts per hour × Contribution margin` | Currency / year |
| **Reporting labor savings** | `Hours per shift × Shifts per day × Days per year × Number of supervisors × Reporting reduction % × Loaded operator labor rate` | Currency / year |
| **Engineering driver savings (Year 1 only)** | `Number of uncovered protocols × Engineer-weeks per protocol × 5 days/week × 8 hours/day × Loaded engineer labor rate` | Currency, one-time |
| **Audit prep savings (optional)** | `Months saved × Loaded audit team rate × Audit frequency per year` | Currency / year |

### 4.2 Aggregate outputs

- **Total annual recurring value** = Downtime + Reporting + Audit savings (excludes one-time engineering)
- **Total Year-1 value** = Annual recurring + Engineering driver savings
- **Payback period (months)** = Year-1 platform cost / Monthly recurring value
  - *Year-1 platform cost is supplied by Elpis sales — not the calculator. Calculator outputs the payback formula; the sales team enters the price during the conversation.*
- **3-year cumulative value** = (Annual recurring × 3) + Engineering driver savings
- **3-year ROI %** = (3-year cumulative value − 3-year platform cost) / 3-year platform cost × 100

### 4.3 Sensitivity table

A small grid showing payback period at three downtime-reduction assumptions × three reporting-reduction assumptions (3×3 = 9 cells). This lets the buyer see how sensitive the payback is to the assumptions they're least confident in.

### 4.4 What the calculator does NOT compute

Listed explicitly in the worksheet to set honest expectations:

- **OEE improvement value** — too dependent on the customer's current OEE baseline and bonus / commercial structure. Worth a discussion, not a calculation.
- **Tool-life optimization value** — varies wildly by industry; needs customer-specific data.
- **Cyber-insurance premium reduction from offline operation** — real but customer-specific; ask insurer.
- **Cost avoidance from not rebuilding a multi-vendor SCADA** — depends on what the customer would have done instead.
- **Soft benefits** — operator morale, recruiting, audit-confidence, regulatory readiness. Real, but uncalculated.

The "what it doesn't compute" section is doing as much credibility work as the "what it does compute" section. Honest calculators get trusted; opaque ones get ignored.

---

## 5. Worked example (placeholder numbers — to be replaced with real customer data once available)

**Scenario:** A 30-CNC precision-machining plant in a Tier-2 automotive supplier.

| Operating constant | Value |
|---|---|
| Plants | 1 |
| Controllers (CNCs) | 30 |
| Plant operating hours/week | 120 (3 shifts × 5 days × 8 hours) |
| Operating weeks/year | 50 |
| Average parts/hour (per CNC) | 6 |
| Contribution margin per part | $12 USD |
| Operator loaded rate | $45 USD/hr |
| Engineer loaded rate | $95 USD/hr |
| Annual downtime hours/CNC | 150 |
| Hours/shift on manual reporting (per supervisor) | 2 |
| Shift supervisors | 3 |
| Uncovered protocols | 3 (FOCAS2, Brother HTTP, Modbus TCP) |

**Assumed improvements (conservative middle values):**

- Downtime reduction: 10%
- Reporting reduction: 50%
- Engineer-weeks per protocol: 6

**Calculations:**

- **Downtime savings/year:** 150 hrs × 30 CNCs × 10% × 6 parts/hr × $12 = **$32,400/year**
- **Reporting savings/year:** 2 hrs/shift × 3 shifts × 250 days × 3 supervisors × 50% × $45 = **$50,625/year**
- **Engineering driver savings (Year 1):** 3 protocols × 6 weeks × 40 hrs × $95 = **$68,400 one-time**
- **Annual recurring value:** $32,400 + $50,625 = **$83,025/year**
- **Year-1 total value:** $83,025 + $68,400 = **$151,425**
- **3-year cumulative value:** ($83,025 × 3) + $68,400 = **$317,475**

**Payback at $40,000/year platform cost:** ~6 months on recurring value alone. With Year-1 engineering savings included, the platform is net-positive within Month 4.

> *This worked example uses placeholder operating constants. The Excel template requires the customer to enter their own numbers. No claim is made that any specific customer achieves these results — the calculator shows what the customer's own inputs imply.*

---

## 6. UX guidance

### 6.1 Excel template

- **Single workbook, multiple sheets:** *Inputs* (where the user fills in), *Outputs* (auto-calculated worksheet), *Sensitivity* (the 3×3 grid), *Notes* (the "does not compute" section + assumptions + caveats), *Worked example* (the §5 scenario as a populated sheet so the user sees what filled-in looks like).
- **All input cells highlighted** in a soft yellow or accent color. Output cells locked / read-only.
- **Every formula visible** — no hidden VBA, no proprietary functions. A CFO must be able to audit every cell.
- **Currency dropdown drives display formatting** across all sheets.
- **No branding overload.** Clean, professional layout. Elpis logo on the cover; that's enough.
- **"Reset to blank" button or instructions** so the user can start fresh after seeing the worked example.

### 6.2 Web calculator

- **Single-page form** with the three input groups (operating constants, current-state pain, assumed improvements) as collapsible sections.
- **Live recalculation** as the user types — every input change updates the outputs panel instantly.
- **Conservative defaults pre-filled** for the assumed-improvement inputs, with clear "edit me" affordance.
- **Output panel** shows the per-bucket savings, annual recurring, Year-1 total, payback formula (sales team supplies cost), and 3-year cumulative.
- **Inline sensitivity slider** for downtime-reduction assumption — drag to see payback shift.
- **Three CTAs on the output panel:**
  - *Download the full Excel model*
  - *Book a scoping call to review these numbers with our team*
  - *Email me this worksheet* (sends a PDF + Excel of the populated calculation)
- **No login required to use the calculator.** Email-gate only the download.

### 6.3 PDF worksheet

- **Two-page printable.** Page 1: blank input grid the prospect fills in by hand. Page 2: the formulas + worked example.
- **Designed to be folded into a sales packet** alongside the datasheet.
- **No interactivity** — it's a paper artifact.

---

## 7. Discipline rules — what the calculator must NOT do

The calculator earns credibility by what it refuses to claim.

- **Never fabricate inputs.** Conservative defaults are acceptable; manufactured "typical customer" outputs are not.
- **Never hide assumptions.** Every output must trace to inputs visible on the same page.
- **Never produce a single hero number.** The worksheet format prevents the "$5 million in year one!" overclaim that destroys trust on technical review.
- **Never use industry-average data the customer can't verify.** If a default is shown, it's labeled as a default and ranged honestly.
- **Never bundle soft benefits into hard numbers.** Operator morale, regulatory readiness, audit confidence — listed in the "does not compute" section, not added to the bottom line.
- **Never compare against unnamed competitors.** Build-vs-buy is fine (in-house driver development is a real alternative). Build-vs-Kepware-or-Ignition is not — that's an objection-handling conversation, not a calculator output.

---

## 8. Defaults and sensible ranges (for input sanity checks)

The web calculator can flag inputs outside these ranges as *"unusual — double-check?"*

| Input | Sensible range | Outside-range flag |
|---|---|---|
| Annual unplanned downtime hours/CNC | 50–400 | <50: probably underestimated; >400: probably overestimated or counting planned stops |
| Hours/shift on manual reporting | 0.5–4 | >4: probably double-counting; <0.5: probably already automated |
| Downtime reduction assumption | 5%–25% | >25%: aggressive; require explicit user override |
| Reporting reduction assumption | 25%–80% | >80%: probably overclaiming; the platform doesn't eliminate all reporting work |
| Engineer-weeks per protocol | 4–12 | <4: probably underestimating real production-readiness work; >12: probably overestimating |

---

## 9. Future extensions (out of scope for v1)

- **OEE-improvement value module** — once Elpis has 3–5 customer references with before/after OEE data
- **Energy-cost savings module** — for plants with Modbus-fronted energy meters
- **Multi-currency live FX** — v1 is fixed at user-selected currency
- **CRM integration** — calculator submissions flow into HubSpot / Salesforce / equivalent
- **A/B testing on default assumptions** — once the v1 calculator has been live long enough to see real input distributions
- **Industry-specific variants** — CNC machining / automotive parts / OEM / energy each with their own pre-populated worked examples

---

## 10. Sign-off checklist

Before the calculator is shipped:

- [ ] All formulas reviewed by Elpis sales lead and at least one customer-facing engineer for realism
- [ ] Worked example uses placeholder numbers, clearly labeled
- [ ] Discipline rules (§7) honored throughout the workbook and web variant
- [ ] "Does not compute" section visible and prominent (§4.4)
- [ ] Currency dropdown works correctly across all displayed values
- [ ] Excel workbook opens cleanly in both Excel and Google Sheets (test with a real account)
- [ ] Web calculator's output panel is screenshot-friendly (clean layout, no truncation)
- [ ] CTAs on the web calculator route to the right destinations
- [ ] Email-gate on Excel download is configured if Elpis wants lead capture
- [ ] Worked-example scenario reviewed by the user — adjusted to match Elpis's actual target customer profile

---

## 11. Out of scope for v1

- **Real customer numbers** — placeholder only until the user supplies them
- **Pricing inputs** — the calculator outputs the payback formula; the sales team enters platform cost during the conversation
- **Comparison against named competitors** — that's objection-handling, separate deliverable
- **Multi-currency live conversion** — pick one currency per session
- **Login / account creation** — calculator is anonymous; only the download is gated
- **Automated CRM integration** — manual lead capture in v1
- **Localized variants** — English-only in v1

---

*ROI Calculator Spec — v1, 2026-05-24. Derived from datasheet v4 §"Typical value areas" + the original handoff §5 quantification angles.*
