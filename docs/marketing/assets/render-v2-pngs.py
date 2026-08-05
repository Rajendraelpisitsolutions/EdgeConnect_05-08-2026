"""
render-v2-pngs.py — produce @2x PNG fallbacks for v2 architecture variants.

Uses PyMuPDF (fitz) to rasterize each SVG at 2x device pixel ratio. This
avoids Edge headless conflicts with the user's running browser instances
that were preventing reliable screenshot capture.

Inputs:  ./architecture-diagram-v2-{dark,light,slide,simple,poster}.svg
Outputs: ./architecture-diagram-v2-{...}@2x.png
"""

from pathlib import Path
import fitz

ASSETS = Path(__file__).parent.resolve()

# (svg_basename, expected_native_width, expected_native_height)
VARIANTS = [
    ("architecture-diagram-v2-dark",   2400, 1600),
    ("architecture-diagram-v2-light",  2400, 1600),
    ("architecture-diagram-v2-slide",  3200, 1800),
    ("architecture-diagram-v2-simple", 2400, 900),
    ("architecture-diagram-v2-poster", 2400, 1880),
]

SCALE = 2  # @2x


def render_one(stem: str, expected_w: int, expected_h: int) -> Path:
    svg_path = ASSETS / f"{stem}.svg"
    if not svg_path.exists():
        raise FileNotFoundError(svg_path)

    doc = fitz.open(str(svg_path))
    page = doc[0]
    rect = page.rect

    # Sanity: confirm viewBox matches expectation.
    if int(rect.width) != expected_w or int(rect.height) != expected_h:
        print(f"[warn] {stem} viewBox {rect.width:.0f}x{rect.height:.0f}, "
              f"expected {expected_w}x{expected_h}")

    matrix = fitz.Matrix(SCALE, SCALE)
    pix = page.get_pixmap(matrix=matrix, alpha=False)

    out_png = ASSETS / f"{stem}@2x.png"
    pix.save(str(out_png))
    doc.close()

    size = out_png.stat().st_size
    print(f"[ok] {out_png.name}  {pix.width}x{pix.height}  ({size:,} bytes)")
    return out_png


def main() -> None:
    for stem, w, h in VARIANTS:
        render_one(stem, w, h)


if __name__ == "__main__":
    main()
