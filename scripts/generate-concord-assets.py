"""
generate-concord-assets.py
Concord product mark + Mendix Marketplace thumbnail generator.

Outputs (into marketing/ relative to script's parent dir):
  concord-icon-128.png   -- 128x128, transparent bg
  concord-icon-256.png   -- 256x256, transparent bg
  concord-icon-512.png   -- 512x512, transparent bg
  concord-icon-1024.png  -- 1024x1024, transparent bg
  concord-thumbnail-600x420.png -- marketing tile

Regen command (from project root):
  python scripts/generate-concord-assets.py

Dependencies: Pillow (pip install Pillow)
Fonts: CascadiaMono.ttf (C:\\Windows\\Fonts) -- falls back to Consolas if absent.
"""

import os
import math
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, ImageFilter

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------
SCRIPT_DIR = Path(__file__).parent
PROJECT_ROOT = SCRIPT_DIR.parent
MARKETING = PROJECT_ROOT / "marketing"
MARKETING.mkdir(parents=True, exist_ok=True)

FONT_BOLD_PATH = r"C:\Windows\Fonts\CascadiaMono.ttf"
FONT_REG_PATH = r"C:\Windows\Fonts\CascadiaMono.ttf"
FALLBACK_BOLD = r"C:\Windows\Fonts\consolab.ttf"
FALLBACK_REG = r"C:\Windows\Fonts\consola.ttf"

def get_font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    path = (FONT_BOLD_PATH if bold else FONT_REG_PATH)
    if not os.path.exists(path):
        path = FALLBACK_BOLD if bold else FALLBACK_REG
    try:
        return ImageFont.truetype(path, size)
    except Exception:
        return ImageFont.load_default()


# ---------------------------------------------------------------------------
# Color palette
# ---------------------------------------------------------------------------
BLUE_LIGHT   = (68, 118, 183)     # #4476B7  Mendix dark-mode accent
BLUE_DARK    = (31,  90, 159)     # #1F5A9F  Mendix light-mode accent
BLUE_MID     = (44, 110, 196)     # #2C6EC4  gradient center
BLUE_DEEP    = (26,  77, 138)     # #1A4D8A  gradient bottom-right
DARK_BG      = (49,  49,  49)     # #313131  in-product dark theme
TERM_SURFACE = (30,  30,  30)     # #1E1E1E  terminal panel
WHITE        = (255, 255, 255)
WHITE_DIM    = (255, 255, 255, 220)
CURSOR_BLUE  = (68, 118, 183, 230)
SEPARATOR    = (255, 255, 255, 30)
DOT_RED      = (255,  95,  87)
DOT_YELLOW   = (254, 188,  46)
DOT_GREEN    = ( 39, 200,  64)

ALPHA_FULL   = 255
ALPHA_0      = 0


# ---------------------------------------------------------------------------
# Geometry helpers
# ---------------------------------------------------------------------------
def lerp_color(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def rounded_rect_mask(draw: ImageDraw.ImageDraw, xy, radii, fill):
    """Draw a rounded rectangle using Pillow's built-in rounded_rectangle."""
    draw.rounded_rectangle(xy, radius=radii, fill=fill)


def gradient_rounded_rect(img: Image.Image, xy, radius: int, color_tl, color_br):
    """
    Fill a rounded rectangle with a linear gradient from top-left to bottom-right.
    Works by painting a scanline gradient on a full-size RGBA layer, then
    masking it to the rounded rect shape.
    """
    x0, y0, x1, y1 = xy
    w, h = img.size

    # 1. Build gradient layer (full image size, RGBA)
    grad = Image.new("RGBA", img.size, (0, 0, 0, 0))
    grad_draw = ImageDraw.Draw(grad)

    diag = math.sqrt((x1 - x0) ** 2 + (y1 - y0) ** 2)
    for py in range(y0, y1 + 1):
        for px_chunk_start in range(x0, x1, 4):
            t = ((px_chunk_start - x0) / (x1 - x0) + (py - y0) / (y1 - y0)) / 2
            t = max(0.0, min(1.0, t))
            c = lerp_color(color_tl, color_br, t) + (255,)
            chunk_end = min(px_chunk_start + 4, x1)
            grad_draw.line([(px_chunk_start, py), (chunk_end, py)], fill=c)

    # 2. Build mask for rounded rect
    mask = Image.new("L", img.size, 0)
    mask_draw = ImageDraw.Draw(mask)
    mask_draw.rounded_rectangle([x0, y0, x1, y1], radius=radius, fill=255)

    # 3. Composite
    img.paste(grad, mask=mask)


def draw_line_thick(draw: ImageDraw.ImageDraw, x0, y0, x1, y1, width, color, cap_round=True):
    """Draw a thick anti-aliased line by drawing a polygon (trapezoid)."""
    # Vector perpendicular to line direction, scaled to half-width
    dx, dy = x1 - x0, y1 - y0
    length = math.sqrt(dx * dx + dy * dy)
    if length == 0:
        return
    nx, ny = -dy / length * width / 2, dx / length * width / 2

    poly = [
        (x0 + nx, y0 + ny),
        (x1 + nx, y1 + ny),
        (x1 - nx, y1 - ny),
        (x0 - nx, y0 - ny),
    ]
    draw.polygon(poly, fill=color)

    if cap_round:
        r = width // 2
        draw.ellipse([x0 - r, y0 - r, x0 + r, y0 + r], fill=color)
        draw.ellipse([x1 - r, y1 - r, x1 + r, y1 + r], fill=color)


# ---------------------------------------------------------------------------
# Icon renderer
# ---------------------------------------------------------------------------
def render_icon(size: int) -> Image.Image:
    """
    Render the Concord product mark at `size` x `size` pixels.
    All measurements are expressed as fractions of `size` so the icon
    is crisp at every target resolution.
    """
    S = size
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))  # transparent

    # ── 1. Rounded-square container ──────────────────────────────────────
    corner_r = round(S * 96 / 512)
    gradient_rounded_rect(img, [0, 0, S - 1, S - 1], corner_r, BLUE_MID, BLUE_DEEP)

    draw = ImageDraw.Draw(img, "RGBA")

    # ── 2. Dark terminal panel (inset) ───────────────────────────────────
    panel_margin = round(S * 48 / 512)
    panel_h = round(S * 320 / 512)
    panel_r = round(S * 20 / 512)
    px0, py0 = panel_margin, panel_margin
    px1, py1 = S - panel_margin, panel_margin + panel_h
    draw.rounded_rectangle(
        [px0, py0, px1, py1],
        radius=panel_r,
        fill=(*TERM_SURFACE, 140)
    )

    # ── 3. Traffic-light dots ────────────────────────────────────────────
    dot_y = round(S * 82 / 512)
    dot_r = round(S * 10 / 512)
    dot_x0 = round(S * 90 / 512)
    dot_gap = round(S * 32 / 512)
    for color, offset in [(DOT_RED, 0), (DOT_YELLOW, dot_gap), (DOT_GREEN, dot_gap * 2)]:
        cx = dot_x0 + offset
        draw.ellipse([cx - dot_r, dot_y - dot_r, cx + dot_r, dot_y + dot_r], fill=(*color, 215))

    # Thin separator
    sep_y = round(S * 105 / 512)
    draw.line([(px0, sep_y), (px1, sep_y)], fill=(*WHITE, 30), width=max(1, round(S / 512)))

    # ── 4. Prompt glyphs: >_ ─────────────────────────────────────────────
    # Chevron ">" — two arms meeting at apex
    #   Top arm:    (96,148) → (224,232)   [normalized to 512-grid]
    #   Bottom arm: (224,232) → (96,316)
    stroke_w = round(S * 36 / 512)
    apex_x  = round(S * 224 / 512)
    apex_y  = round(S * 232 / 512)
    top_x   = round(S *  96 / 512)
    top_y   = round(S * 148 / 512)
    bot_y   = round(S * 316 / 512)

    draw_line_thick(draw, top_x, top_y, apex_x, apex_y, stroke_w, (*WHITE, 245))
    draw_line_thick(draw, apex_x, apex_y, top_x, bot_y, stroke_w, (*WHITE, 245))

    # Cursor bar "_" — wide underline
    cur_x0 = round(S * 264 / 512)
    cur_y0 = round(S * 282 / 512)
    cur_w  = round(S * 160 / 512)
    cur_h  = round(S *  36 / 512)
    cur_r  = round(S *   8 / 512)
    draw.rounded_rectangle(
        [cur_x0, cur_y0, cur_x0 + cur_w, cur_y0 + cur_h],
        radius=cur_r,
        fill=(*WHITE, 245)
    )

    # Vertical cursor bar (blue, active-input indicator)
    vcur_x = round(S * 264 / 512)
    vcur_y = round(S * 148 / 512)
    vcur_w = round(S *  20 / 512)
    vcur_h = round(S * 120 / 512)
    vcur_r = round(S *   4 / 512)
    draw.rounded_rectangle(
        [vcur_x, vcur_y, vcur_x + vcur_w, vcur_y + vcur_h],
        radius=vcur_r,
        fill=(*BLUE_LIGHT, 230)
    )

    # ── 5. Wordmark "CONCORD" below panel ─────────────────────────────────
    wm_y = round(S * 400 / 512)
    wm_font_size = round(S * 52 / 512)
    # Only show wordmark at >= 128px; at 16px it's moot
    if S >= 64 and wm_font_size >= 6:
        try:
            font = get_font(wm_font_size, bold=True)
            draw.text(
                (S // 2, wm_y),
                "CONCORD",
                font=font,
                fill=(*WHITE, 235),
                anchor="mt"
            )
        except Exception:
            pass  # graceful skip if font fails

    # Underline accent below wordmark
    accent_y = round(S * 452 / 512)
    accent_x0 = round(S * 128 / 512)
    accent_x1 = round(S * 384 / 512)
    accent_h  = max(2, round(S * 3 / 512))
    draw.rounded_rectangle(
        [accent_x0, accent_y, accent_x1, accent_y + accent_h],
        radius=max(1, accent_h // 2),
        fill=(*BLUE_LIGHT, 180)
    )

    return img


# ---------------------------------------------------------------------------
# Thumbnail renderer
# ---------------------------------------------------------------------------
def measure_text_width(draw: ImageDraw.ImageDraw, text: str, font) -> int:
    """Return pixel width of text string using the given font."""
    bbox = draw.textbbox((0, 0), text, font=font)
    return bbox[2] - bbox[0]


def render_thumbnail() -> Image.Image:
    """
    600x420 Mendix Marketplace marketing tile.
    Layout (pixel-exact):
      Left zone  [0..199]   : icon centered vertically (180px icon, 10px margin each side)
      Divider    [200..215]  : 1px subtle separator, padded
      Right zone [216..583]  : text panel (367px wide, 16px right margin -> 583)
      Bottom stripe [415..420]: 5px Mendix-blue gradient

    All font sizes are chosen so that no text line exceeds the right boundary.
    """
    W, H = 600, 420
    MARGIN_R = 16          # right margin from canvas edge
    TEXT_X = 216           # left edge of text panel
    TEXT_MAX_W = W - TEXT_X - MARGIN_R   # 368px max text width

    img = Image.new("RGBA", (W, H), (*DARK_BG, 255))
    draw = ImageDraw.Draw(img, "RGBA")

    # ── Background: very subtle top-to-bottom darkening ───────────────────
    for y in range(H):
        t = y / H
        c = lerp_color(DARK_BG, (38, 38, 42), t)
        draw.line([(0, y), (W, y)], fill=(*c, 255))

    # ── Thin vertical separator between icon and text zones ───────────────
    sep_x = 204
    for y in range(24, H - 24):
        alpha = int(60 * math.sin(math.pi * (y - 24) / (H - 48)))
        draw.point((sep_x, y), fill=(*BLUE_LIGHT, alpha))

    # ── Icon: 180px, left zone centered ──────────────────────────────────
    ICON_SIZE = 180
    icon = render_icon(ICON_SIZE)
    icon_x = (200 - ICON_SIZE) // 2      # centers in left zone [0..199]
    icon_y = (H - ICON_SIZE) // 2
    img.paste(icon, (icon_x, icon_y), icon)

    # ── Right panel: wordmark + tagline ──────────────────────────────────
    # We size the wordmark font so "Concord" fits within TEXT_MAX_W.
    # Cascadia Mono is wide; iterate down from 56 to find the right size.
    wm_font_size = 56
    font_wm = get_font(wm_font_size, bold=True)
    while measure_text_width(draw, "Concord", font_wm) > TEXT_MAX_W and wm_font_size > 24:
        wm_font_size -= 2
        font_wm = get_font(wm_font_size, bold=True)

    # Category label — auto-size so it fits
    cat_text = "MENDIX STUDIO PRO  /  Studio Pro 11.10+"
    cat_font_size = 12
    font_cat = get_font(cat_font_size, bold=False)
    while measure_text_width(draw, cat_text, font_cat) > TEXT_MAX_W and cat_font_size > 8:
        cat_font_size -= 1
        font_cat = get_font(cat_font_size, bold=False)

    # Tagline — auto-size so it fits on one line
    tag_text = "The terminal Studio Pro was missing."
    tag_font_size = 17
    font_tag = get_font(tag_font_size, bold=False)
    while measure_text_width(draw, tag_text, font_tag) > TEXT_MAX_W and tag_font_size > 10:
        tag_font_size -= 1
        font_tag = get_font(tag_font_size, bold=False)

    # Bullet font — fixed small
    bul_font_size = 13
    font_bul = get_font(bul_font_size, bold=False)

    # ── Layout calculation ─────────────────────────────────────────────────
    # Total text block height: cat + wm + underline gap + tag + 3 bullets
    # We anchor the block so it's vertically centered in [24..H-29] (excluding stripes)
    block_h = (
        cat_font_size + 10          # category label + gap
        + wm_font_size + 4          # wordmark
        + 3                         # underline
        + 14                        # gap below underline
        + tag_font_size + 6         # tagline
        + 3 * (bul_font_size + 8)   # 3 bullets
    )
    content_top = (H - 5 - block_h) // 2   # vertically center above stripe
    content_top = max(content_top, 24)

    cur_y = content_top

    # Category label
    draw.text((TEXT_X, cur_y), cat_text, font=font_cat, fill=(*BLUE_LIGHT, 170))
    cur_y += cat_font_size + 10

    # Wordmark
    draw.text((TEXT_X, cur_y), "Concord", font=font_wm, fill=(*WHITE, 245))
    wm_w = measure_text_width(draw, "Concord", font_wm)
    wm_bottom = cur_y + wm_font_size
    cur_y = wm_bottom + 4

    # Blue underline
    draw.rounded_rectangle(
        [TEXT_X, cur_y, TEXT_X + wm_w, cur_y + 3],
        radius=1,
        fill=(*BLUE_LIGHT, 200)
    )
    cur_y += 3 + 14

    # Tagline
    draw.text((TEXT_X, cur_y), tag_text, font=font_tag, fill=(*WHITE, 190))
    cur_y += tag_font_size + 6

    # Feature bullets — simple dot prefix, no unicode arrow that might not render
    bullets = [
        "Claude Code  *  Codex  *  GitHub Copilot CLI",
        "Dockable pane  *  Dark & light themes",
        "MCP Action Bridge  *  Maia-aware",
    ]
    for line in bullets:
        # Trim line if still too wide after font fit (safety net)
        while measure_text_width(draw, "-  " + line, font_bul) > TEXT_MAX_W and len(line) > 8:
            line = line[:-4] + "..."
        draw.text((TEXT_X, cur_y), "-  " + line, font=font_bul, fill=(*WHITE, 130))
        cur_y += bul_font_size + 8

    # ── Bottom accent stripe ──────────────────────────────────────────────
    stripe_h = 5
    stripe_y = H - stripe_h
    for x in range(W):
        t = x / W
        c = lerp_color(BLUE_DARK, BLUE_LIGHT, t)
        draw.line([(x, stripe_y), (x, H)], fill=(*c, 255))

    return img


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
def main():
    icon_sizes = [128, 256, 512, 1024]

    print("Generating Concord icons...")
    for size in icon_sizes:
        icon = render_icon(size)
        out_path = MARKETING / f"concord-icon-{size}.png"
        icon.save(out_path, "PNG")
        file_bytes = out_path.stat().st_size
        print(f"  {out_path.name}  {size}x{size}  {file_bytes:,} bytes  alpha=RGBA")

    print("Generating Marketplace thumbnail...")
    thumb = render_thumbnail()
    thumb_path = MARKETING / "concord-thumbnail-600x420.png"
    thumb.save(thumb_path, "PNG")
    file_bytes = thumb_path.stat().st_size
    print(f"  {thumb_path.name}  600x420  {file_bytes:,} bytes")

    print(f"\nAll assets written to: {MARKETING}")


if __name__ == "__main__":
    main()
