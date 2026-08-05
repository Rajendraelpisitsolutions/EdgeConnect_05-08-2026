# Pitch deck — follow-ups after v3

**Date:** 2026-05-24
**Source:** Two ChatGPT review passes on the executive pitch deck (PR #32)
**Status:** v3 of `pitch-deck` is the canonical version. The items below were
flagged during review but deferred — they are improvements for future
sessions, not gaps in v3.

This doc captures **work the v3 deck does not yet do**, alongside one
operational reminder and one design-philosophy guardrail. Per
`feedback_chip_markdown_redundancy.md`, the work lives here in markdown rather
than as floating chips that the user could lose.

---

## 1. Slide 6 — visual maturity ladder (partially addressed in v4)

**Strategic verdict from review (v1–v3):** the slide's *idea* is one of the
strongest in the deck — it reframes the platform from "monitoring tool" to
"operational system replacement". The *visual execution* was the weakest in
the deck through v3.

### What v4 did (closes ~70% of the gap)

v4 replaced the em-dash placeholder mock with a **stylized spreadsheet
artifact**: a filename header (`shift-handover-w17.xlsx`), a 5-column table
(Time / Op / Mc / OEE / Notes) with 7 rows of synthesized shift-handover
data, and two "broken" rows (`0635 ?? M-15 — OFFLINE` and `0830 TLN M-08 —
down 47min`) subtly emphasized via brighter text + a small teal "audit hash"
marker on the row's left edge. Monospaced font on cell values for that
spreadsheet feel. All in palette — no red/amber state colors. The slide now
reads as a believable operational artifact.

### What still remains (the last 30%)

A future visual-maturity pass can push the slide further if and when real
stylized artifacts become available:

- Blurred Excel screenshots (recognizable, not legible — no real customer
  data)
- Pseudo-table screenshots with intentional formatting degradation
- Operator hand-written shift notes (scanned, slightly skewed)
- The same downtime event represented across three different spreadsheets
  with different numbers — *the exact operational pain the platform fixes*
- A scanned, faded, off-axis "Daily report" PDF fragment

### Discipline (carries forward)

- Never literal customer data — only generic or synthesized artifacts
- Maintain the calm dark palette — no bright "data chaos" colors
- One or two artifacts max, generously sized — not a collage
- The right column ("WITH THE PLATFORM") stays exactly as v4

**When to do it:** when Elpis has access to stylized operational artifacts.
v4 is fully suitable for distribution as-is.

**Where to edit:** `docs/marketing/assets/build-pitch-deck-vN.py`, the slide 6
LEFT panel block. Replace the synthesized table with `add_picture` calls
referencing the new artifact files placed under `docs/marketing/assets/`.

---

## 2. Presenter placeholders — fill before any real distribution

The deck currently uses explicit placeholder slots:

| Placeholder | Where it appears |
|---|---|
| `[ Presenter Name ]` | Slide 1 (presenter block), slide 12 (contact line) |
| `[ Role ]` | Slide 1 (presenter block) |
| `[ Date ]` | Slide 1 (presenter block) |
| `[ presenter@elpisitsolutions.com ]` | Slide 12 (contact line) |
| `[ website ]` | Slide 12 (contact line) |
| `[ phone ]` | Slide 12 (contact line) |

These are intentional — the deck is a *master template*, not a per-deal copy.

**Recommended workflow per real meeting:**

1. Copy `build-pitch-deck-v3.py` to a per-deal name (or just edit a working
   copy).
2. Replace the placeholders with the real presenter and contact details.
3. Re-run the script to regenerate the deal-specific `.pptx`.
4. Save the deal-specific deck *outside* the repo (or under a private
   `docs/decks/` path that is gitignored).

**Do not commit deal-specific decks to this repo.** Master template only.

---

## 3. Design-philosophy guardrail — restraint is the asset

The two review passes both ended with the same warning: the deck works because
it is *calm, deliberate, infrastructural, operational*. The biggest risk
going forward is not messaging — the messaging is locked. The biggest risk
is **visual over-design during future polish**.

If a future designer session opens against this deck, these are the things
**not** to add:

- Animations (hover effects, slide transitions beyond simple fades, parallax)
- Glow effects, neon, "cyberpunk" tonal moves
- Dashboard clutter (mini-charts, gauges, busy data widgets)
- AI-themed visuals (brain icons, neural networks, "intelligent" badges)
- Stock photography (handshakes, smiling operators, aerial factory shots)
- Multiple accent colors (the teal-only discipline holds, except in the
  separately-authorized `architecture-diagram-v1-poster.svg`)
- More than one subtle gradient per hero block
- **Background textures** (subtle telemetry patterns, hex grids, circuit traces,
  data-pulse motifs) on any content slide. Considered and explicitly rejected
  during the v4 → v5 review pass: even when called "subtle, not decorative,"
  texture introduces ambient industrial-themed visuals that conflict with the
  deck's premium-industrial-infrastructure restraint. Dark voids on the
  content slides are deliberate negative space, not gaps. If a slide later
  feels too sparse in real presentation use, fix it with type/layout
  rebalancing, not with background pattern

The deck is *premium-industrial infrastructure presentation*. The visual
language must match. If a future change feels exciting, it is probably wrong
for this deck. If it feels calm, it is probably right.

---

## 4. Optional future variants

These were flagged in the original `pitch-deck-outline-v1.md` "Out of scope
for v1" but remain relevant once the master template lands externally:

- **Investor variant** — adds market-size, revenue-model, traction, ask
  slides. Different audience, different ordering. Derive from v3 master
  rather than rebuild from scratch.
- **Vertical decks** — CNC-only, brownfield-only, OEM-only, multi-site-only.
  Each is a thin overlay on v3: swap slide 3 to lead with the relevant
  vertical, swap slide 5 outcomes to vertical-specific phrasing, swap slide 8
  diagram to the matching `architecture-diagram-v1-<vertical>.svg` (which
  doesn't exist yet — would need to be produced from the master SVG).
- **Localized variants** — Japanese (Fanuc-heavy markets), German
  (Siemens-heavy markets), Mandarin (Brother + China). Translation of
  speaker notes is at least as important as slide text — the speaker notes
  carry the pitch.

None of these are urgent. They become real work when:
- An investor meeting requires the variant
- A vertical-specific deal accumulates enough volume to justify the deck
- A localized market opens

---

## 5. Files to touch when items above become active

```
docs/marketing/assets/
├── build-pitch-deck-vN.py        ← bump N for any new version
├── pitch-deck-vN.pptx            ← generated output
├── architecture-diagram-v1-*.svg ← may need vertical variants (item 4)
└── brand/BRAND_TOKENS.md         ← only touch if accent-color rules evolve
```

The build script is the durable source of truth. The `.pptx` is generated
output and should never be hand-edited (any edit would be lost on the next
regeneration).

---

*Follow-up notes — 2026-05-24. Pitch deck v3 is the current canonical version
and is suitable for executive meetings as-is, with the caveat that placeholder
slots in slides 1 and 12 must be filled before any real distribution.*
