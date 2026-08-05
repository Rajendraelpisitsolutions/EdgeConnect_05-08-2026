"""
File:     docs/marketing/assets/build-datasheet-v1.py
Purpose:  Generate the print-ready branded datasheet PDF (A4 + US Letter
          variants) for the Elpis Industrial Intelligence Platform.
Source:   docs/marketing/elpis-industrial-intelligence-platform-v4.md
Tokens:   docs/marketing/assets/brand/BRAND_TOKENS.md v1 (locked palette)
Spec:     docs/marketing/architecture-diagram-spec-v2.md §3.4
Fonts:    Inter (bundled under docs/marketing/assets/fonts/), embedded in
          the PDF so the document renders identically on any machine.
Outputs:
          docs/marketing/assets/datasheet-v1-a4.pdf       (210x297 mm)
          docs/marketing/assets/datasheet-v1-letter.pdf   (8.5x11 in)

Layout — 4 pages, same content on both A4 and Letter:
  Page 1   Cover hero — title, subtitle, intro, 3 highlight cards
  Page 2   Platform + Architecture — EdgeConnect + EREMOS V2 + diagram
  Page 3   Outcomes + Connectivity — 6 outcome cards + 2x2 protocols
  Page 4   Designed for + Why Elpis + Editions + Next step

Visual motif (matches pitch deck): dark navy bg, single teal accent rule
down the left edge of every page, Inter typography, premium-industrial
restraint. No icons that aren't functional, no decorative textures, no
gradients outside hero blocks.

Run:      python docs/marketing/assets/build-datasheet-v1.py
"""
from pathlib import Path
from reportlab.pdfgen import canvas
from reportlab.lib.pagesizes import A4, LETTER, portrait
from reportlab.lib.colors import HexColor
from reportlab.lib.utils import simpleSplit
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.lib.units import mm

# ---------------------------------------------------------------------------
# Brand tokens (mirrors BRAND_TOKENS.md v1)
# ---------------------------------------------------------------------------

BG           = HexColor("#1A1F26")
BG_DEEP      = HexColor("#0F1419")
HERO         = HexColor("#2A2F36")
HERO_RAISED  = HexColor("#2E343B")
SECONDARY    = HexColor("#3A4049")
BORDER_SUB   = HexColor("#4A5560")
BORDER_STR   = HexColor("#5E6B78")
TEXT_BODY    = HexColor("#E8ECF1")
TEXT_MUTED   = HexColor("#A8B3BD")
TEXT_CAP     = HexColor("#C8D0D8")
TEXT_HEAD    = HexColor("#FFFFFF")
TEAL         = HexColor("#00A0E0")

# ---------------------------------------------------------------------------
# Font registration — embed Inter into the PDF
# ---------------------------------------------------------------------------

FONTS_DIR = Path(__file__).parent / "fonts"
_font_map = {
    "Inter":          FONTS_DIR / "Inter-Regular.ttf",
    "Inter-Italic":   FONTS_DIR / "Inter-Italic.ttf",
    "Inter-SemiBold": FONTS_DIR / "Inter-SemiBold.ttf",
    "Inter-Bold":     FONTS_DIR / "Inter-Bold.ttf",
}
for name, path in _font_map.items():
    if path.exists():
        pdfmetrics.registerFont(TTFont(name, str(path)))
# Register a font family for italic/bold combinations
from reportlab.pdfbase.pdfmetrics import registerFontFamily
registerFontFamily(
    "Inter",
    normal="Inter",
    bold="Inter-Bold",
    italic="Inter-Italic",
    boldItalic="Inter-Bold",  # no separate bold-italic file bundled
)

FONT_REG  = "Inter"
FONT_IT   = "Inter-Italic"
FONT_SB   = "Inter-SemiBold"
FONT_BOLD = "Inter-Bold"

# ---------------------------------------------------------------------------
# Layout constants
# ---------------------------------------------------------------------------

M_L = 18 * mm   # left margin
M_R = 18 * mm   # right margin
M_T = 20 * mm   # top margin
M_B = 18 * mm   # bottom margin
ACCENT_W = 4    # teal accent rule width in pt

ASSETS = Path(__file__).parent
OUT_A4     = ASSETS / "datasheet-v1-a4.pdf"
OUT_LETTER = ASSETS / "datasheet-v1-letter.pdf"
# Use the SLIDE variant of the architecture diagram (caption-less per spec).
# The dark master variant has the caption embedded in the artwork, which
# would duplicate the caption we render as PDF text below the image.
ARCH_IMG   = ASSETS / "architecture-diagram-v1-slide@2x.png"
ARCH_RATIO = 3200 / 1800  # 16:9 aspect of the slide variant (= 1.778)

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def ty(ph, offset):
    """Convert a top-down offset (pts from top of page) to reportlab's
    bottom-up y coordinate."""
    return ph - offset


def draw_rect(c, x, y, w, h, *, fill=None, stroke=None, stroke_w=None):
    """Filled and/or outlined rectangle. y is reportlab's bottom-left y."""
    if fill is not None:
        c.setFillColor(fill)
    if stroke is not None:
        c.setStrokeColor(stroke)
    if stroke_w is not None:
        c.setLineWidth(stroke_w)
    c.rect(x, y, w, h, fill=1 if fill is not None else 0,
           stroke=1 if stroke is not None else 0)


def draw_text(c, text, x, y, *, font=FONT_REG, size=10, color=TEXT_BODY,
              align="left"):
    """Single-line text. y is reportlab's bottom-up baseline y."""
    c.setFont(font, size)
    c.setFillColor(color)
    if align == "right":
        c.drawRightString(x, y, text)
    elif align == "center":
        c.drawCentredString(x, y, text)
    else:
        c.drawString(x, y, text)


def draw_spaced(c, text, x, y, *, font=FONT_SB, size=9, color=TEXT_MUTED,
                spacing=1.5, align="left"):
    """Letter-spaced single-line text (for SECTION LABELS in small caps).
    Wrapped in saveState/restoreState because the PDF Tc operator (character
    spacing) is part of the graphics state and persists across BT/ET blocks —
    without isolation, subsequent draw_text / draw_paragraph calls inherit
    the spacing and run wide / overflow the layout."""
    c.saveState()
    c.setFont(font, size)
    c.setFillColor(color)
    if align == "center":
        w = pdfmetrics.stringWidth(text, font, size) + spacing * (len(text) - 1)
        sx = x - w / 2
    elif align == "right":
        w = pdfmetrics.stringWidth(text, font, size) + spacing * (len(text) - 1)
        sx = x - w
    else:
        sx = x
    to = c.beginText(sx, y)
    to.setFont(font, size)
    to.setFillColor(color)
    to.setCharSpace(spacing)
    to.textOut(text)
    c.drawText(to)
    c.restoreState()


def draw_paragraph(c, text, x, y, max_w, *, font=FONT_REG, size=10,
                   color=TEXT_BODY, leading=None):
    """Word-wrap text into lines fitting max_w, draw downward.
    y is the TOP of the first line; returns y AFTER (below) the last line.
    Internally converts to baseline coordinates."""
    leading = leading or size * 1.45
    c.setFont(font, size)
    c.setFillColor(color)
    lines = simpleSplit(text, font, size, max_w)
    cur_y = y - size  # first baseline
    for line in lines:
        c.drawString(x, cur_y, line)
        cur_y -= leading
    return cur_y + leading - size  # bottom of last line in top-down terms


# ---------------------------------------------------------------------------
# Page chrome
# ---------------------------------------------------------------------------

def page_chrome(c, pw, ph, page_num, total=4):
    """Dark bg + left accent rule + footer wordmark + page number."""
    # Background
    draw_rect(c, 0, 0, pw, ph, fill=BG)
    # Left accent rule
    draw_rect(c, 0, 0, ACCENT_W, ph, fill=TEAL)
    # Footer wordmark (left) + page number (right)
    foot_y = M_B - 24
    draw_spaced(c, "ELPIS  ·  INDUSTRIAL INTELLIGENCE PLATFORM",
                M_L, foot_y, font=FONT_SB, size=7, color=BORDER_STR,
                spacing=1.8)
    draw_text(c, f"{page_num:02d} / {total:02d}",
              pw - M_R, foot_y, font=FONT_SB, size=7,
              color=BORDER_STR, align="right")
    # Footer hairline
    draw_rect(c, M_L, foot_y + 12, pw - M_L - M_R, 0.4, fill=BORDER_SUB)


# ===========================================================================
# Page 1 — Cover
# ===========================================================================

def page1_cover(c, pw, ph):
    page_chrome(c, pw, ph, 1)

    cw = pw - M_L - M_R

    # Pre-title label
    draw_spaced(c, "DATASHEET  ·  v4",
                M_L, ty(ph, M_T + 18),
                font=FONT_SB, size=9, color=TEAL, spacing=3.0)

    # Big title (line 1 + line 2)
    title_y = ty(ph, M_T + 60)
    draw_text(c, "Industrial Intelligence",
              M_L, title_y, font=FONT_BOLD, size=34, color=TEXT_HEAD)
    draw_text(c, "Platform.",
              M_L, title_y - 40, font=FONT_BOLD, size=34, color=TEAL)

    # Subtitle — italic muted
    sub_top = ty(ph, M_T + 140)
    end_y = draw_paragraph(
        c,
        "Unified industrial connectivity and operational intelligence "
        "for modern manufacturing.",
        M_L, sub_top, cw,
        font=FONT_IT, size=15, color=TEXT_CAP, leading=21)

    # Hairline divider
    div_y = ty(ph, M_T + 195)
    draw_rect(c, M_L, div_y, cw, 0.5, fill=BORDER_SUB)

    # Intro paragraph
    intro_top = div_y - 18
    intro_text = (
        "Connect CNCs, Modbus PLCs, and instrumentation into one real-time "
        "operational platform. Measure OEE on signals collected directly "
        "from the controller. Reduce downtime with persistent alarms and "
        "incident workflows. From the spindle to the dashboard, on one "
        "foundation."
    )
    end_y = draw_paragraph(
        c, intro_text, M_L, intro_top, cw,
        font=FONT_REG, size=11, color=TEXT_BODY, leading=17)

    # Three highlight cards (stacked) — title + body per card
    cards = [
        ("MULTI-PROTOCOL EDGE",
         "FOCAS2  ·  MT-LINKi  ·  MTConnect  ·  "
         "Brother HTTP  ·  Modbus TCP",
         "One service speaks every controller on your floor."),
        ("REAL-TIME INTELLIGENCE",
         "OEE  ·  Persistent alarms  ·  Incident workflows  "
         "·  PDF and Excel reports",
         "Operational decisions from machine data — multi-tenant."),
        ("EDGE-FIRST ARCHITECTURE",
         "Offline-capable  ·  Air-gap friendly  ·  "
         "Per-route store-and-forward",
         "Years of operation on a small box in the control cabinet."),
    ]
    card_top = ty(ph, M_T + 295)
    card_h = 64
    card_gap = 12
    for i, (label, line, sub) in enumerate(cards):
        cy = card_top - i * (card_h + card_gap)
        # Card box
        draw_rect(c, M_L, cy - card_h, cw, card_h,
                  fill=HERO, stroke=BORDER_SUB, stroke_w=0.5)
        # Teal indicator
        draw_rect(c, M_L, cy - card_h, 3, card_h, fill=TEAL)
        # Label
        draw_spaced(c, label, M_L + 16, cy - 16,
                    font=FONT_SB, size=9, color=TEAL, spacing=2.5)
        # Line
        draw_text(c, line, M_L + 16, cy - 32,
                  font=FONT_SB, size=10, color=TEXT_HEAD)
        # Sub
        draw_text(c, sub, M_L + 16, cy - 48,
                  font=FONT_IT, size=9, color=TEXT_MUTED)


# ===========================================================================
# Page 2 — Platform + Architecture
# ===========================================================================

def page2_platform(c, pw, ph):
    page_chrome(c, pw, ph, 2)
    cw = pw - M_L - M_R

    # Section label
    draw_spaced(c, "THE PLATFORM", M_L, ty(ph, M_T + 18),
                font=FONT_SB, size=9, color=TEXT_MUTED, spacing=3.0)

    # Headline
    draw_text(c, "Two products. One foundation.",
              M_L, ty(ph, M_T + 50), font=FONT_BOLD, size=22, color=TEXT_HEAD)

    # Two product cards
    pcard_top = ty(ph, M_T + 80)
    pcard_h = 165
    gap = 16
    pcard_w = (cw - gap) / 2

    # ----- EdgeConnect card (left) -----
    ex = M_L
    ey = pcard_top - pcard_h
    draw_rect(c, ex, ey, pcard_w, pcard_h, fill=HERO,
              stroke=BORDER_STR, stroke_w=0.7)
    # Teal corner accent
    draw_rect(c, ex + 16, pcard_top - 18, 18, 2.5, fill=TEAL)
    draw_text(c, "EdgeConnect", ex + 16, pcard_top - 38,
              font=FONT_BOLD, size=18, color=TEXT_HEAD)
    draw_text(c, "Edge runtime",
              ex + 16, pcard_top - 52, font=FONT_IT, size=10, color=TEXT_MUTED)
    draw_rect(c, ex + 16, pcard_top - 62, pcard_w - 32, 0.5, fill=BORDER_SUB)
    ec_points = [
        "Protocol-agnostic edge service",
        "Canonical CNC vocabulary",
        "Per-route store-and-forward",
        "Three-way diagnostics",
        "Hash-chained config audit",
        "RSA-signed offline licensing",
    ]
    for i, p in enumerate(ec_points):
        py = pcard_top - 80 - i * 13
        draw_rect(c, ex + 16, py + 3, 3, 3, fill=TEAL)
        draw_text(c, p, ex + 24, py, font=FONT_REG, size=9, color=TEXT_BODY)

    # ----- EREMOS V2 card (right) -----
    erx = M_L + pcard_w + gap
    ery = ey
    draw_rect(c, erx, ery, pcard_w, pcard_h, fill=HERO,
              stroke=BORDER_STR, stroke_w=0.7)
    draw_rect(c, erx + 16, pcard_top - 18, 18, 2.5, fill=TEAL)
    draw_text(c, "EREMOS V2", erx + 16, pcard_top - 38,
              font=FONT_BOLD, size=18, color=TEXT_HEAD)
    draw_text(c, "Industrial intelligence",
              erx + 16, pcard_top - 52, font=FONT_IT, size=10, color=TEXT_MUTED)
    draw_rect(c, erx + 16, pcard_top - 62, pcard_w - 32, 0.5, fill=BORDER_SUB)
    er_points = [
        "Multi-tenant analytics",
        "OEE via Segments (auditable)",
        "Persistent alarms + incidents",
        "Configurable alerting channels",
        "PDF and Excel reports",
        "Tool-life and tag mapping",
    ]
    for i, p in enumerate(er_points):
        py = pcard_top - 80 - i * 13
        draw_rect(c, erx + 16, py + 3, 3, 3, fill=TEAL)
        draw_text(c, p, erx + 24, py, font=FONT_REG, size=9, color=TEXT_BODY)

    # MQTT · OPC UA connector label between cards
    conn_y = pcard_top - 85
    conn_cx = M_L + pcard_w + gap / 2
    draw_text(c, "MQTT", conn_cx, conn_y + 2,
              font=FONT_BOLD, size=8, color=TEAL, align="center")
    draw_text(c, "OPC UA", conn_cx, conn_y - 10,
              font=FONT_BOLD, size=8, color=TEAL, align="center")

    # Architecture diagram
    arch_top = ey - 22
    arch_label_y = arch_top
    draw_spaced(c, "ARCHITECTURE AT A GLANCE",
                M_L, arch_label_y, font=FONT_SB, size=9,
                color=TEXT_MUTED, spacing=3.0)
    # Image — 16:9 aspect (slide variant); size to fit content width
    img_w = cw
    img_h = img_w / ARCH_RATIO  # 16:9 = 1.778
    img_y = arch_label_y - 18 - img_h
    if ARCH_IMG.exists():
        c.drawImage(str(ARCH_IMG), M_L, img_y, width=img_w, height=img_h,
                    preserveAspectRatio=True, mask='auto')
    else:
        # Fallback box
        draw_rect(c, M_L, img_y, img_w, img_h, fill=HERO,
                  stroke=BORDER_SUB, stroke_w=0.5)
        draw_text(c, "[ architecture-diagram-v1-dark@2x.png missing ]",
                  M_L + img_w / 2, img_y + img_h / 2,
                  font=FONT_IT, size=12, color=TEAL, align="center")

    # Caption
    cap_y = img_y - 8
    end_y = draw_paragraph(
        c,
        "One EdgeConnect deploys at each plant. One EREMOS V2 tenant "
        "aggregates many sites. Standard MQTT and OPC UA make the "
        "integration interoperable with whatever else you run.",
        M_L, cap_y, cw, font=FONT_IT, size=9, color=TEXT_CAP, leading=13)


# ===========================================================================
# Page 3 — Outcomes + Connectivity
# ===========================================================================

def page3_outcomes_connectivity(c, pw, ph):
    page_chrome(c, pw, ph, 3)
    cw = pw - M_L - M_R

    # Section 1 — Outcomes
    draw_spaced(c, "OUTCOMES YOU CAN HOLD US TO",
                M_L, ty(ph, M_T + 18),
                font=FONT_SB, size=9, color=TEXT_MUTED, spacing=3.0)
    draw_text(c, "Operational deliverables, not promises.",
              M_L, ty(ph, M_T + 48),
              font=FONT_BOLD, size=18, color=TEXT_HEAD)

    outcomes = [
        ("Cut unplanned downtime",
         "Persistent alarm tracking and incident workflows."),
        ("Trust your OEE number",
         "Every input collected at the controller."),
        ("Modernize legacy controllers",
         "FOCAS2, Brother HTTP, Modbus — no replacements."),
        ("See your whole fleet",
         "Multiple plants, multiple shifts, one view."),
        ("Keep sensitive data on premise",
         "Fully offline-capable at the edge."),
        ("Pass your audit",
         "Hash-chained config, signed offline licensing."),
    ]

    grid_top = ty(ph, M_T + 80)
    card_h = 50
    gap_x = 12
    gap_y = 10
    card_w = (cw - gap_x) / 2  # 2 columns
    for i, (lead, sub) in enumerate(outcomes):
        col = i % 2
        row = i // 2
        x = M_L + col * (card_w + gap_x)
        y = grid_top - row * (card_h + gap_y) - card_h
        # Card
        draw_rect(c, x, y, card_w, card_h, fill=HERO,
                  stroke=BORDER_SUB, stroke_w=0.4)
        # Teal indicator
        draw_rect(c, x, y, 3, card_h, fill=TEAL)
        # Lead text
        draw_text(c, lead, x + 14, y + card_h - 16,
                  font=FONT_SB, size=11, color=TEXT_HEAD)
        # Sub
        draw_text(c, sub, x + 14, y + card_h - 32,
                  font=FONT_REG, size=9, color=TEXT_MUTED)

    # Section 2 — Connectivity coverage
    conn_top = grid_top - 3 * card_h - 2 * gap_y - 36
    draw_spaced(c, "CONNECTIVITY COVERAGE",
                M_L, conn_top,
                font=FONT_SB, size=9, color=TEXT_MUTED, spacing=3.0)
    draw_text(c, "Native to the protocols your controllers already speak.",
              M_L, conn_top - 28, font=FONT_BOLD, size=16, color=TEXT_HEAD)

    # 2x2 protocol cards
    pquad = [
        ("SOUTHBOUND", "CNC controllers",
         "FOCAS2  ·  MT-LINKi  ·  MTConnect  ·  Brother HTTP"),
        ("SOUTHBOUND", "PLC + instrumentation",
         "Modbus TCP"),
        ("NORTHBOUND", "Messaging",
         "MQTT (any compliant broker)"),
        ("NORTHBOUND", "Enterprise integration",
         "OPC UA Server"),
    ]
    quad_top = conn_top - 52
    quad_h = 56
    quad_w = (cw - gap_x) / 2
    for i, (direction, title, protocols) in enumerate(pquad):
        col = i % 2
        row = i // 2
        x = M_L + col * (quad_w + gap_x)
        y = quad_top - row * (quad_h + gap_y) - quad_h
        draw_rect(c, x, y, quad_w, quad_h, fill=HERO,
                  stroke=BORDER_SUB, stroke_w=0.4)
        draw_spaced(c, direction, x + 14, y + quad_h - 14,
                    font=FONT_SB, size=8, color=TEAL, spacing=2.5)
        draw_text(c, title, x + 14, y + quad_h - 30,
                  font=FONT_BOLD, size=12, color=TEXT_HEAD)
        draw_text(c, protocols, x + 14, y + quad_h - 44,
                  font=FONT_REG, size=9, color=TEXT_BODY)

    # Canonical vocabulary footer line
    voc_y = quad_top - 2 * quad_h - gap_y - 18
    voc_text = (
        "Every source delivers tags using a shared canonical CNC vocabulary. "
        "The same dashboard layout works across Fanuc, Brother, and "
        "Modbus-fronted machines."
    )
    draw_paragraph(c, voc_text, M_L, voc_y, cw,
                   font=FONT_IT, size=9.5, color=TEXT_CAP, leading=13)


# ===========================================================================
# Page 4 — Designed for + Why Elpis + Editions + Next step
# ===========================================================================

def page4_close(c, pw, ph):
    page_chrome(c, pw, ph, 4)
    cw = pw - M_L - M_R

    # ----- DESIGNED FOR -----
    draw_spaced(c, "DESIGNED FOR", M_L, ty(ph, M_T + 18),
                font=FONT_SB, size=9, color=TEXT_MUTED, spacing=3.0)

    audiences = [
        ("01", "Multi-vendor CNC manufacturing plants"),
        ("02", "Brownfield modernization projects"),
        ("03", "Multi-site industrial operations teams"),
        ("04", "OEM machine monitoring deployments"),
        ("05", "Precision manufacturing operations"),
    ]
    aud_top = ty(ph, M_T + 42)
    for i, (num, label) in enumerate(audiences):
        y = aud_top - i * 18
        col_strong = (i < 3)
        num_color  = TEAL if col_strong else BORDER_STR
        lead_color = TEXT_BODY if col_strong else TEXT_MUTED
        draw_text(c, num, M_L, y, font=FONT_BOLD, size=12, color=num_color)
        draw_text(c, label, M_L + 24, y, font=FONT_SB, size=11, color=lead_color)

    # ----- WHY ELPIS -----
    why_top = aud_top - 5 * 18 - 24
    draw_spaced(c, "WHY ELPIS", M_L, why_top,
                font=FONT_SB, size=9, color=TEXT_MUTED, spacing=3.0)

    differentiators = [
        ("Air-gapped factories are first-class",
         "RSA-signed offline license. No phone-home.", True),
        ("A lapsed license never stops production",
         "Expiration blocks configuration changes only.", True),
        ("AI proposes — humans decide",
         "Decision-support only. Local-LLM-capable.", True),
        ("Three-way diagnostics by design",
         "Source, pipeline, sink. No silent failures.", False),
        ("Per-protocol modular activation",
         "Pay for the connectivity you actually use.", False),
    ]
    diff_top = why_top - 22
    for i, (lead, sub, highlight) in enumerate(differentiators):
        y = diff_top - i * 30
        # Card bg
        if highlight:
            draw_rect(c, M_L, y - 22, cw, 24, fill=HERO,
                      stroke=TEAL, stroke_w=0.7)
            draw_rect(c, M_L, y - 22, 3, 24, fill=TEAL)
            lead_color = TEXT_HEAD
        else:
            draw_rect(c, M_L, y - 22, 3, 24, fill=BORDER_STR)
            lead_color = TEXT_BODY
        draw_text(c, lead, M_L + 14, y - 8,
                  font=FONT_BOLD, size=10, color=lead_color)
        draw_text(c, sub, M_L + 14, y - 20,
                  font=FONT_REG, size=8.5, color=TEXT_MUTED)

    # ----- EDITIONS & ROADMAP -----
    ed_top = diff_top - 5 * 30 - 22
    draw_spaced(c, "EDITIONS · ROADMAP", M_L, ed_top,
                font=FONT_SB, size=9, color=TEXT_MUTED, spacing=3.0)

    ed_text = (
        "Available in Starter, Professional, and Enterprise editions with "
        "optional connectivity modules: FOCAS2, MT-LINKi, MTConnect, "
        "Brother HTTP, Modbus TCP, MQTT, OPC UA Server. "
    )
    rm_text = (
        "On the roadmap: OPC UA Client (southbound), Siemens S7, "
        "HTTP / TCP sinks (northbound), Linux host, AI-assisted operations agents."
    )
    ed_y_after = draw_paragraph(c, ed_text, M_L, ed_top - 14, cw,
                                font=FONT_REG, size=10, color=TEXT_BODY,
                                leading=14)
    draw_paragraph(c, rm_text, M_L, ed_y_after - 6, cw,
                   font=FONT_IT, size=9.5, color=TEXT_CAP, leading=13)

    # ----- NEXT STEP -----
    next_top = M_B + 100
    # Box around the closing CTA
    cta_y = M_B + 40
    cta_h = 78
    draw_rect(c, M_L, cta_y, cw, cta_h, fill=HERO,
              stroke=TEAL, stroke_w=1.0)
    draw_rect(c, M_L, cta_y, 3, cta_h, fill=TEAL)
    draw_spaced(c, "NEXT STEP", M_L + 16, cta_y + cta_h - 16,
                font=FONT_SB, size=9, color=TEAL, spacing=3.0)
    draw_text(c, "Bring us a real plant. We'll scope a real proof.",
              M_L + 16, cta_y + cta_h - 36,
              font=FONT_BOLD, size=14, color=TEXT_HEAD)
    draw_text(c,
              "Demos run on real protocols against your real signals — not canned data.",
              M_L + 16, cta_y + cta_h - 54,
              font=FONT_IT, size=9.5, color=TEXT_MUTED)
    draw_text(c, "Elpis IT Solutions  ·  [ website ]  ·  [ phone ]",
              M_L + 16, cta_y + 12,
              font=FONT_REG, size=9, color=TEXT_BODY)


# ---------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------

def build(page_size, output_path):
    c = canvas.Canvas(str(output_path), pagesize=page_size)
    pw, ph = page_size
    c.setTitle("Elpis Industrial Intelligence Platform — Datasheet v1")
    c.setAuthor("Elpis IT Solutions")
    c.setSubject("Industrial Intelligence Platform datasheet")
    c.setCreator("build-datasheet-v1.py")

    page1_cover(c, pw, ph)
    c.showPage()
    page2_platform(c, pw, ph)
    c.showPage()
    page3_outcomes_connectivity(c, pw, ph)
    c.showPage()
    page4_close(c, pw, ph)
    c.showPage()

    c.save()
    print(f"wrote  {output_path.name}  ({output_path.stat().st_size:,} bytes, "
          f"{int(page_size[0])}x{int(page_size[1])} pt)")


if __name__ == "__main__":
    build(A4, OUT_A4)
    build(portrait(LETTER), OUT_LETTER)
