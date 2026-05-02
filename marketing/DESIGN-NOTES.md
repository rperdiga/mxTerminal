# Concord Visual Identity — Design Notes

## Product mark concept

The icon is a terminal window chrome (rounded rectangle, dark inner surface, three macOS-style traffic-light dots) containing the canonical shell prompt glyphs `>` and `_`. The `>` is rendered as two thick line segments meeting at a right-pointing apex — pure geometry, no font dependency — so it scales deterministically from 1024px down to 16px. The `_` is a wide rounded underline representing the cursor rest position, and the adjacent vertical bar in Mendix blue (#4476B7) represents the active blinking cursor, the distinguishing visual that says "something is running here." Together, `>_` is the universal shorthand for "terminal" without being a copy of Windows Terminal (which uses a single `>` in a filled circle) or VS Code's terminal panel icon (flat monochrome prompt on a transparent background). The glyph pairing also subtly evokes two entities in dialogue — the developer and the AI agent — which is the product's core value proposition (harmony = Concord).

## Color choices

The outer container uses a top-left-to-bottom-right gradient from #2C6EC4 → #1A4D8A, bracketing the two in-product Mendix blue values (#4476B7 dark-mode, #1F5A9F light-mode). The dark terminal panel inside uses #1E1E1E, which is VS Code's default editor background — familiar and immediately legible as "code surface." The wordmark "CONCORD" is set in Cascadia Mono Bold (the same font used in the in-product terminal) so the mark and the product feel like one coherent object.

## Thumbnail layout

The 600×420 tile uses a strict two-zone split: the left 200px holds the icon (180px, centered), the right 368px holds the text hierarchy (category label → wordmark → tagline → feature bullets). All font sizes are computed at runtime with a fit-loop that measures actual rendered width and steps down until text stays within the right boundary (TEXT_MAX_W = 368px). This guarantees no text ever clips regardless of font hinting variation across machines. The bottom 5px is a left-to-right Mendix-blue gradient stripe as a brand anchor.

## What a designer should improve

The gradient on the icon container is computed as a simple diagonal lerp between two hardcoded stops — a real designer would use a radial highlight to give the icon more depth (the "bubble" look of good iOS/macOS icons). The traffic-light dots are sized proportionally but their shadows are omitted. The wordmark in the thumbnail uses Cascadia Mono Bold, which is a code font, not a display font — a designer might substitute a geometric sans for the wordmark while keeping mono for the tagline/bullets.

## Regeneration

From the project root (requires Pillow; no other dependencies):

    python scripts/generate-concord-assets.py

The script at `scripts/generate-concord-assets.py` is the source of truth. The PNG files in this directory are artifacts; delete and rerun to regenerate. The SVG at `concord-icon.svg` is a supplementary source for use in web/HTML contexts (uses the same geometry as the Pillow renderer but relies on font availability in the SVG renderer for the wordmark).
