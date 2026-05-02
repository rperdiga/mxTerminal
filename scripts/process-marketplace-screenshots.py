#!/usr/bin/env python3
"""
Composite Concord screenshots onto a 600x420 marketing canvas.

Each output uses a blurred, darkened full Studio Pro screenshot as the
backdrop so every shot carries IDE context — the viewer never has to
guess where Concord lives. The detail screenshot (the modal or the
pane crop Neo captured) is layered on top with a drop shadow + rounded
corners + a side caption block carrying the Concord wordmark, version,
headline, and one-line subline.

Mendix Marketplace requires exactly 600x420.

Inputs:
  marketing/source/studio-pro-full.png  — full IDE screenshot (backdrop)
  ~/.claude/image-cache/<session>/<NN>.png — Neo's per-shot captures

Run from repo root:
    python scripts/process-marketplace-screenshots.py
"""

from __future__ import annotations

import sys
from pathlib import Path

try:
    from PIL import Image, ImageDraw, ImageFilter, ImageFont
except ImportError:
    print("Pillow required: pip install pillow", file=sys.stderr)
    sys.exit(1)

REPO_ROOT = Path(__file__).resolve().parent.parent
CACHE_DIR = Path.home() / ".claude" / "image-cache" / "7ccdf03a-54db-4ce4-9309-90ecf6f51567"
SOURCE_DIR = REPO_ROOT / "marketing" / "source"
OUT_DIR = REPO_ROOT / "marketing" / "screenshots"
BACKDROP_PATH = SOURCE_DIR / "studio-pro-full.png"

# Brand palette (matches in-product theme.ts dark surfaces).
ACCENT = (68, 118, 183, 255)        # #4476B7 — Mendix-blue dark
ACCENT_DIM = (68, 118, 183, 200)
TEXT_PRIMARY = (240, 240, 240, 255)
TEXT_SECONDARY = (180, 180, 180, 255)
SHADOW = (0, 0, 0, 130)
DARKEN_RGBA = (0, 0, 0, 105)        # veil — IDE chrome still readable as IDE, no detail
TERMINAL_BG = (49, 49, 49, 255)     # match Concord's dark theme — for redaction overlays

CANVAS = (600, 420)
BACKDROP_BLUR = 14                  # heavier — obscure project tree text + tab labels
SHADOW_OFFSET = (5, 7)
SHADOW_BLUR = 14
CORNER_RADIUS = 8
ACCENT_HEIGHT = 3

# Layout: detail screenshot on left, text panel on right (semi-translucent).
PANEL_W = 200                       # right-side text panel width
PANEL_X = CANVAS[0] - PANEL_W
SHOT_AREA = (PANEL_X, CANVAS[1])    # area available for the detail screenshot
SHOT_PAD = 18

# Per-source crop zones (applied FIRST, before redactions and scaling).
# Each crop: (left_frac, top_frac, right_frac, bottom_frac).
# Use to focus a tall source image on its most marketing-relevant portion
# so it scales up bigger inside the detail-screenshot area.
CROPS: dict[str, tuple[float, float, float, float]] = {
    # MCP output — drop the empty top portion above the /mcp output.
    "66.png": (0.0, 0.27, 1.0, 0.95),
    # General settings on top of pane — center the modal in the frame.
    "62.png": (0.0, 0.18, 1.0, 0.78),
}


# Per-source redaction zones (applied AFTER crop, BEFORE scaling).
# Each zone: (y1_frac, y2_frac, x1_frac, x2_frac, blur_radius)
# Coordinates are fractions of the source dimensions so they survive
# any source resizing. Blur radius is in pixels at source scale.
# Bands chosen to cover sensitive text (project name, username, full
# local paths) without requiring per-character precision.
REDACTIONS: dict[str, list[tuple[float, float, float, float, int]]] = {
    # Hero — Claude welcome screen has TestOSApp3 in the PS prompt + the
    # Claude header path.
    "60.png": [
        (0.31, 0.36, 0.0, 0.7, 8),    # PS prompt line
        (0.42, 0.46, 0.18, 0.65, 8),  # Claude welcome path
    ],
    # About panel — log + settings file paths show full local project paths.
    # Generous bands so the multi-line wrapped values are fully covered.
    "61.png": [
        (0.50, 0.71, 0.60, 1.0, 12),  # log file value
        (0.71, 0.86, 0.60, 1.0, 12),  # settings file value
    ],
    # General settings on top of pane — pane chrome visible above the modal
    # contains both the PS prompt edge and the Claude welcome path. Single
    # wide blur band over the top 16% of the cropped image catches both
    # without trying to be precise.
    "62.png": [
        (0.00, 0.16, 0.00, 1.0, 14),  # entire pane-context strip above modal
    ],
    # MCP output (fractions are POST-CROP — see CROPS above).
    "66.png": [
        (0.13, 0.25, 0.18, 1.0, 10),  # "Project MCPs (...TestOSApp3...)" + 2 lines below
        (0.27, 0.37, 0.18, 1.0, 10),  # "User MCPs (...z004fh9h...)" + lines below
    ],
}


def apply_redactions(img: Image.Image, source_name: str) -> Image.Image:
    zones = REDACTIONS.get(source_name)
    if not zones:
        return img
    w, h = img.size
    for y1f, y2f, x1f, x2f, blur in zones:
        x1, y1 = int(x1f * w), int(y1f * h)
        x2, y2 = int(x2f * w), int(y2f * h)
        if x2 <= x1 or y2 <= y1:
            continue
        region = img.crop((x1, y1, x2, y2))
        region = region.filter(ImageFilter.GaussianBlur(blur))
        img.paste(region, (x1, y1))
    return img


# Source -> output filename + headline + subline.
SCREENSHOTS: list[tuple[str, str, str, str]] = [
    ("60.png", "01-hero-claude-running.png",
        "Claude Code in Studio Pro",
        "Real PTY pane. Tabbed, persistent, theme-matched."),
    ("66.png", "02-mcp-integration-proven.png",
        "MCP integration, proven",
        "Studio Pro MCP + Action Bridge, both connected, one command."),
    ("64.png", "03-settings-mcp-wiring.png",
        "One toggle, three CLIs",
        "Wires Claude Code, Codex, and Copilot CLI to Studio Pro's MCP."),
    ("65.png", "04-settings-action-bridge.png",
        "Action Bridge",
        "Six MCP tools the CLI uses to drive Studio Pro itself."),
    ("62.png", "05-settings-general.png",
        "Persistent tabs",
        "Tabs survive Studio Pro restart. Per-project state."),
    ("63.png", "06-settings-shell.png",
        "Pick your shell",
        "PowerShell, bash, cmd — auto-detected on the system."),
    ("61.png", "07-settings-about.png",
        "Built by the Siemens CoE",
        "v1.1.0 — paste pipeline overhaul (ConPTY)."),
]


def load_font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    candidates = [
        ("CascadiaMono-Bold.ttf" if bold else "CascadiaMono.ttf"),
        ("CascadiaCode-Bold.ttf" if bold else "CascadiaCode.ttf"),
        ("segoeuib.ttf" if bold else "segoeui.ttf"),
        ("arial.ttf"),
    ]
    win_fonts = Path(r"C:\Windows\Fonts")
    for name in candidates:
        p = win_fonts / name
        if p.exists():
            try:
                return ImageFont.truetype(str(p), size)
            except OSError:
                continue
    return ImageFont.load_default()


def rounded_mask(size: tuple[int, int], radius: int) -> Image.Image:
    m = Image.new("L", size, 0)
    d = ImageDraw.Draw(m)
    d.rounded_rectangle([(0, 0), (size[0] - 1, size[1] - 1)], radius=radius, fill=255)
    return m


def make_backdrop() -> Image.Image:
    """Crop+scale the Studio Pro full screenshot to 600x420, then blur+darken."""
    src = Image.open(BACKDROP_PATH).convert("RGBA")
    src_w, src_h = src.size
    target_ar = CANVAS[0] / CANVAS[1]
    src_ar = src_w / src_h
    if src_ar > target_ar:
        # Source is wider — crop horizontally.
        new_w = int(src_h * target_ar)
        offset = (src_w - new_w) // 2
        src = src.crop((offset, 0, offset + new_w, src_h))
    else:
        # Source is taller — crop vertically.
        new_h = int(src_w / target_ar)
        offset = (src_h - new_h) // 2
        src = src.crop((0, offset, src_w, offset + new_h))
    src = src.resize(CANVAS, Image.LANCZOS)
    src = src.filter(ImageFilter.GaussianBlur(BACKDROP_BLUR))
    veil = Image.new("RGBA", CANVAS, DARKEN_RGBA)
    src.alpha_composite(veil)
    return src


def apply_crop(img: Image.Image, source_name: str) -> Image.Image:
    box = CROPS.get(source_name)
    if not box:
        return img
    w, h = img.size
    left = int(box[0] * w)
    top = int(box[1] * h)
    right = int(box[2] * w)
    bottom = int(box[3] * h)
    return img.crop((left, top, right, bottom))


def composite(source_path: Path, out_path: Path, headline: str, subline: str,
              backdrop: Image.Image) -> None:
    src = Image.open(source_path).convert("RGBA")
    src = apply_crop(src, source_path.name)
    src = apply_redactions(src, source_path.name)
    canvas = backdrop.copy()

    # 1. Right-side text panel — translucent panel for readable copy over the blur.
    panel = Image.new("RGBA", (PANEL_W, CANVAS[1]), (24, 24, 24, 200))
    canvas.alpha_composite(panel, (PANEL_X, 0))
    div = Image.new("RGBA", (1, CANVAS[1]), ACCENT_DIM)
    canvas.paste(div, (PANEL_X, 0), div)

    # 2. Fit the detail screenshot into the left area, preserving aspect.
    pad = SHOT_PAD
    max_w = SHOT_AREA[0] - 2 * pad - SHADOW_OFFSET[0] - SHADOW_BLUR
    max_h = SHOT_AREA[1] - 2 * pad - SHADOW_OFFSET[1] - SHADOW_BLUR - ACCENT_HEIGHT
    src_w, src_h = src.size
    scale = min(max_w / src_w, max_h / src_h, 1.0)
    new_w = max(1, int(src_w * scale))
    new_h = max(1, int(src_h * scale))
    if scale < 1.0:
        src = src.resize((new_w, new_h), Image.LANCZOS)

    # 3. Rounded corners.
    mask = rounded_mask((new_w, new_h), CORNER_RADIUS)
    rounded = Image.new("RGBA", (new_w, new_h), (0, 0, 0, 0))
    rounded.paste(src, (0, 0), mask)

    # 4. Drop shadow.
    shadow_buf = Image.new("RGBA", (new_w + SHADOW_BLUR * 2, new_h + SHADOW_BLUR * 2), (0, 0, 0, 0))
    shadow_layer = Image.new("RGBA", (new_w, new_h), (0, 0, 0, 0))
    shadow_layer.paste(SHADOW, (0, 0, new_w, new_h), mask)
    shadow_buf.paste(shadow_layer, (SHADOW_BLUR, SHADOW_BLUR), shadow_layer)
    shadow_buf = shadow_buf.filter(ImageFilter.GaussianBlur(SHADOW_BLUR / 2))

    sx = (SHOT_AREA[0] - new_w) // 2 - SHADOW_BLUR + SHADOW_OFFSET[0] // 2
    sy = (CANVAS[1] - new_h - ACCENT_HEIGHT) // 2 - SHADOW_BLUR + SHADOW_OFFSET[1] // 2
    canvas.alpha_composite(shadow_buf, (sx, sy))

    # 5. Paste rounded screenshot above the shadow.
    rx = (SHOT_AREA[0] - new_w) // 2
    ry = (CANVAS[1] - new_h - ACCENT_HEIGHT) // 2
    canvas.alpha_composite(rounded, (rx, ry))

    # 6. Caption text on the right panel.
    draw = ImageDraw.Draw(canvas)
    f_brand = load_font(22, bold=True)
    f_head = load_font(15, bold=True)
    f_sub = load_font(11, bold=False)
    f_meta = load_font(10, bold=False)

    cx = PANEL_X + 16
    cy = 30
    draw.text((cx, cy), "Concord", font=f_brand, fill=TEXT_PRIMARY)
    cy += 32
    draw.text((cx, cy), "v1.1.0", font=f_meta, fill=ACCENT)
    cy += 30
    draw.line([(cx, cy), (cx + 56, cy)], fill=ACCENT, width=2)
    cy += 22

    draw.text((cx, cy), wrap_text(headline, f_head, PANEL_W - 32),
              font=f_head, fill=TEXT_PRIMARY, spacing=4)
    cy += 60

    draw.text((cx, cy), wrap_text(subline, f_sub, PANEL_W - 32),
              font=f_sub, fill=TEXT_SECONDARY, spacing=4)

    draw.text((cx, CANVAS[1] - 30),
              "Siemens CoE Team", font=f_meta, fill=TEXT_SECONDARY)

    # 7. Bottom Mendix-blue accent.
    draw.rectangle([(0, CANVAS[1] - ACCENT_HEIGHT), (CANVAS[0], CANVAS[1])], fill=ACCENT)

    canvas.convert("RGB").save(out_path, "PNG", optimize=True)
    print(f"  {out_path.name}  ({source_path.name} -> 600x420)")


def wrap_text(text: str, font, max_px: int) -> str:
    words = text.split()
    lines: list[str] = []
    cur = ""
    for w in words:
        candidate = (cur + " " + w).strip()
        try:
            width = font.getlength(candidate)
        except AttributeError:
            width = font.getsize(candidate)[0]
        if width <= max_px:
            cur = candidate
        else:
            if cur:
                lines.append(cur)
            cur = w
    if cur:
        lines.append(cur)
    return "\n".join(lines)


def main() -> int:
    if not BACKDROP_PATH.exists():
        print(f"Backdrop not found: {BACKDROP_PATH}", file=sys.stderr)
        print("Save a full Studio Pro screenshot there first.", file=sys.stderr)
        return 1
    if not CACHE_DIR.exists():
        print(f"Cache dir not found: {CACHE_DIR}", file=sys.stderr)
        return 1
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    print("Building backdrop from", BACKDROP_PATH.name, "...")
    backdrop = make_backdrop()

    print(f"Compositing {len(SCREENSHOTS)} marketplace screenshots -> {OUT_DIR}")
    for src_name, out_name, headline, subline in SCREENSHOTS:
        src = CACHE_DIR / src_name
        if not src.exists():
            print(f"  SKIP missing: {src}", file=sys.stderr)
            continue
        composite(src, OUT_DIR / out_name, headline, subline, backdrop)

    print("Done.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
