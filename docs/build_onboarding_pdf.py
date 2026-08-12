"""
Convert docs/onboarding.md to docs/onboarding.pdf.

Pipeline:
  markdown (with tables + fenced_code + nl2br extensions) -> HTML
  embedded CSS for tables / code / headings + Consolas font registration
  xhtml2pdf -> PDF

xhtml2pdf is pure-Python (no GTK / wkhtmltopdf binary required), which
matters on the user's Windows dev machine.

Iteration history:
  v1 — default Courier rendered Unicode box-drawing chars as squares.
       Tables overflowed when code spans contained long file paths.
  v2 (this) — registers Consolas for the box-drawing glyphs, adds
       table-layout:fixed + word-break for breakable code spans in tables,
       widens cell padding tolerance, white-space:pre-wrap for fenced blocks.
"""

import os
import re
import sys
from pathlib import Path

import markdown
from bs4 import BeautifulSoup, NavigableString
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from xhtml2pdf import pisa

ZWSP = "​"  # zero-width space — invisible, but a break opportunity

SOURCE = Path(r"C:\dev\EdgeConnect\docs\onboarding.md")
OUTPUT = Path(r"C:\dev\EdgeConnect\docs\onboarding.pdf")

# ─── Font registration ──────────────────────────────────────────────────────
# Consolas ships with Windows and has the box-drawing glyphs the default
# Courier lacks. Falls back to Courier silently if Consolas isn't found
# (e.g. running this on Linux). The tree art may render as squares then,
# but everything else is fine.
WIN_FONT_DIR = Path(r"C:\Windows\Fonts")
CONSOLAS_AVAILABLE = False
if (WIN_FONT_DIR / "consola.ttf").exists():
    pdfmetrics.registerFont(TTFont("Consolas", str(WIN_FONT_DIR / "consola.ttf")))
    if (WIN_FONT_DIR / "consolab.ttf").exists():
        pdfmetrics.registerFont(TTFont("Consolas-Bold", str(WIN_FONT_DIR / "consolab.ttf")))
        pdfmetrics.registerFontFamily(
            "Consolas",
            normal="Consolas",
            bold="Consolas-Bold",
        )
    CONSOLAS_AVAILABLE = True

MONO_FAMILY = "Consolas, 'Courier New', Courier, monospace" if CONSOLAS_AVAILABLE else "'Courier New', Courier, monospace"

CSS = """
@page {
    size: letter;
    margin: 0.75in 0.65in 0.7in 0.65in;
}

body {
    font-family: 'Helvetica', 'Arial', sans-serif;
    font-size: 9.5pt;
    line-height: 1.35;
    color: #1f1f1f;
}

h1 {
    font-size: 20pt;
    font-weight: bold;
    color: #1F4E78;
    margin-top: 0;
    margin-bottom: 8pt;
    padding-bottom: 3pt;
    border-bottom: 2pt solid #1F4E78;
}

h2 {
    font-size: 14pt;
    font-weight: bold;
    color: #1F4E78;
    margin-top: 16pt;
    margin-bottom: 6pt;
    padding-bottom: 1pt;
    border-bottom: 0.5pt solid #B4C7E7;
}

h3 {
    font-size: 11pt;
    font-weight: bold;
    color: #2E5C8A;
    margin-top: 10pt;
    margin-bottom: 4pt;
}

p {
    margin-top: 3pt;
    margin-bottom: 5pt;
    text-align: left;
}

ul, ol {
    margin-top: 3pt;
    margin-bottom: 6pt;
    padding-left: 18pt;
}

li {
    margin-bottom: 2pt;
}

/* Inline code (default — outside tables) */
code {
    font-family: __MONO__;
    font-size: 9pt;
    background-color: #F2F2F2;
    padding: 1pt 3pt;
    color: #C7254E;
}

/* Fenced code blocks: white-space:pre-wrap lets long lines (e.g. the
   defect template's Environment: line) wrap inside the pre instead of
   running off the page. */
pre {
    background-color: #F8F8F8;
    border: 0.5pt solid #DDDDDD;
    padding: 6pt;
    font-family: __MONO__;
    font-size: 8.5pt;
    line-height: 1.35;
    margin-top: 4pt;
    margin-bottom: 6pt;
    white-space: pre-wrap;
    word-wrap: break-word;
}

pre code {
    background-color: transparent;
    color: #1f1f1f;
    padding: 0;
    font-size: 8.5pt;
}

/* Tables: table-layout:fixed forces predictable column widths so a long
   code span doesn't push everything sideways. Cells that contain a code
   span need word-break to break the otherwise-unbreakable monospace
   strings (file paths, URLs). */
table {
    border-collapse: collapse;
    margin-top: 4pt;
    margin-bottom: 8pt;
    width: 100%;
    font-size: 8.75pt;
    table-layout: fixed;
}

th {
    background-color: #1F4E78;
    color: #FFFFFF;
    font-weight: bold;
    text-align: left;
    padding: 4pt 6pt;
    border: 0.5pt solid #1F4E78;
    word-wrap: break-word;
}

td {
    padding: 3pt 6pt;
    border: 0.5pt solid #BFBFBF;
    vertical-align: top;
    word-wrap: break-word;
    overflow-wrap: anywhere;
}

/* Code spans INSIDE table cells need aggressive breaking — file paths
   like docs/qa/2026-05-27-modbus-to-opcua-pipeline-qa-tracker.xlsx
   have no natural break points. */
td code {
    font-size: 8.25pt;
    word-break: break-all;
    overflow-wrap: anywhere;
    background-color: #F2F2F2;
    padding: 0 2pt;
}

th code {
    font-size: 8.25pt;
    background-color: transparent;
    color: #FFFFFF;
    padding: 0 1pt;
}

tr:nth-child(even) td {
    background-color: #F7F9FC;
}

a {
    color: #1F4E78;
    text-decoration: underline;
    word-break: break-all;
}

strong, b {
    font-weight: bold;
}

em, i {
    font-style: italic;
}

hr {
    border: 0;
    border-top: 0.5pt solid #BFBFBF;
    margin-top: 8pt;
    margin-bottom: 8pt;
}

blockquote {
    border-left: 3pt solid #1F4E78;
    padding-left: 8pt;
    margin-left: 0;
    color: #595959;
    font-style: italic;
}
""".replace("__MONO__", MONO_FAMILY)

HEADER = """<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<title>Elpis EdgeConnect - Onboarding</title>
<style>%s</style>
</head>
<body>
""" % CSS

FOOTER = """
</body>
</html>
"""


def asciify_box_drawing(text: str) -> str:
    """Replace Unicode box-drawing chars in the repo tree with ASCII.

    Even with Consolas registered, xhtml2pdf's TTF subset embedding
    doesn't reliably include the box-drawing glyphs (U+2500..U+257F).
    ASCII fallback is the robust path. The source markdown keeps the
    nicer Unicode tree art for screen readers (GitHub, IDE, etc.) —
    only the PDF build path swaps to ASCII.
    """
    return (text
            .replace("├──", "+--")   # ├──
            .replace("└──", "\\--")  # └──
            .replace("│", "|")                  # │
            .replace("─", "-"))                 # ─


def fix_table_layouts(html: str) -> str:
    """Force tables to lay out with explicit column widths + inject break
    points only at boundaries where they will actually render as a
    line-break (not as a visible glyph).

    Approach:
    1. For each <table>, count columns from the <thead> row.
    2. Inject a <colgroup> with widths that bias toward path/URL columns:
       - 2-col tables: 45% / 55%  (path column gets a little less than half,
                                   description column gets a little more)
    3. Insert ACTUAL spaces (' ') after every '/' in code/link text inside
       <td>s — this gives reportlab honest break opportunities. Visible
       gaps are acceptable for breakable paths in a table cell (better
       than overflow into the neighbour column).

    Tables that already have <colgroup> or column widths set are left alone.
    """
    soup = BeautifulSoup(html, "html.parser")

    for table in soup.find_all("table"):
        if table.find("colgroup") is not None:
            continue
        # Count columns via first <tr>
        first_row = table.find("tr")
        if first_row is None:
            continue
        cells = first_row.find_all(["th", "td"])
        n = len(cells)
        if n == 0:
            continue

        # Width recipes per column count
        if n == 2:
            widths = [45, 55]
        elif n == 3:
            widths = [30, 50, 20]
        elif n == 4:
            widths = [22, 30, 26, 22]
        else:
            widths = [100 // n] * n

        colgroup = soup.new_tag("colgroup")
        for w in widths:
            col = soup.new_tag("col")
            col["style"] = f"width: {w}%;"
            colgroup.append(col)
        # Insert as first child of <table>
        table.insert(0, colgroup)

        # Inside <td> cells, give code/link text REAL break opportunities.
        # ZWSP (U+200B) renders as visible black squares in xhtml2pdf
        # because the embedded font subset lacks the glyph for it. Use
        # actual whitespace instead: invisible-when-no-break (already a
        # space) and visible-when-breaking (the line just wraps after it).
        #
        # We split at '/' boundaries — path separator is the natural
        # break point. URLs and file paths gain `docs/ qa/ 2026-05-27`
        # appearance when forced to wrap; otherwise look normal.
        for td in table.find_all("td"):
            for el in td.find_all(["code", "a"]):
                for child in list(el.children):
                    if isinstance(child, NavigableString):
                        s = str(child)
                        new_s = ""
                        for i, ch in enumerate(s):
                            new_s += ch
                            # Inject space after '/' EXCEPT:
                            # - if next char is already a space (no-op)
                            # - if next char is another '/' (would split "://" — preserve URL protocol)
                            # - if previous char is '/' (we're the second '/' in "://" — preserve it)
                            if (ch == "/"
                                    and (i + 1 < len(s))
                                    and s[i + 1] != " "
                                    and s[i + 1] != "/"
                                    and (i == 0 or s[i - 1] != "/")):
                                new_s += " "
                        child.replace_with(new_s)

    return str(soup)


def preserve_pre_newlines(html: str) -> str:
    """Replace `\\n` inside <pre><code>...</code></pre> with <br/>.

    xhtml2pdf does not preserve `\\n` inside <pre><code>` reliably even
    with `white-space: pre-wrap` — the defect template (Section 6.4)
    collapses into a single paragraph without this fix.
    """
    def _replace(match: re.Match) -> str:
        content = match.group(1)
        # Strip the surrounding newlines markdown adds, then convert
        # the inner newlines to <br/>
        content = content.strip("\n").replace("\n", "<br/>")
        return f"<pre><code>{content}</code></pre>"

    return re.sub(
        r"<pre><code>(.*?)</code></pre>",
        _replace,
        html,
        flags=re.DOTALL,
    )


def main():
    md_text = SOURCE.read_text(encoding="utf-8")
    md_text = asciify_box_drawing(md_text)

    html_body = markdown.markdown(
        md_text,
        extensions=[
            "tables",
            "fenced_code",
            "sane_lists",
        ],
    )
    html_body = fix_table_layouts(html_body)
    html_body = preserve_pre_newlines(html_body)

    full_html = HEADER + html_body + FOOTER

    with open(OUTPUT, "wb") as out:
        result = pisa.CreatePDF(
            src=full_html,
            dest=out,
            encoding="utf-8",
        )

    if result.err:
        print(f"FAILED: {result.err} errors", file=sys.stderr)
        sys.exit(1)
    else:
        size_kb = OUTPUT.stat().st_size / 1024
        print(f"OK: {OUTPUT} ({size_kb:.1f} KB)")
        print(f"Consolas registered: {CONSOLAS_AVAILABLE}")


if __name__ == "__main__":
    main()
