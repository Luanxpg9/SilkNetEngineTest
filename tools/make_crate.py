"""Draws the placeholder crate sprite for step 3 Phase A: src/TopDownShooter/Assets/crate.png.

A test image for the sprite batcher, not final art (Phase B brings the SVG pipeline). It has
antialiased rounded corners and a soft, see-through shadow, so dark fringes around transparent
edges would show if blending were wrong.

Needs Pillow. Run from anywhere:  python tools/make_crate.py
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

SIZE = 256
SCALE = 4  # draw 4x larger, then shrink: that's the antialiasing
OUT = Path(__file__).resolve().parent.parent / "src" / "TopDownShooter" / "Assets" / "crate.png"


def s(*values):
    """Scales coordinates from the 256 px design to the big canvas."""
    return [v * SCALE for v in values]


big = SIZE * SCALE

# Soft shadow on its own layer: a dark rounded square, offset down-right and blurred.
shadow = Image.new("RGBA", (big, big), (0, 0, 0, 0))
ImageDraw.Draw(shadow).rounded_rectangle(s(30, 36, 230, 236), radius=22 * SCALE, fill=(0, 0, 0, 110))
shadow = shadow.filter(ImageFilter.GaussianBlur(8 * SCALE))

box = Image.new("RGBA", (big, big), (0, 0, 0, 0))
d = ImageDraw.Draw(box)
d.rounded_rectangle(s(24, 24, 224, 224), radius=22 * SCALE, fill=(176, 120, 62, 255), outline=(60, 36, 16, 255), width=3 * SCALE)
for y in (70, 116, 162):  # plank seams
    d.line(s(44, y, 204, y), fill=(150, 98, 48, 255), width=3 * SCALE)
d.line(s(52, 52, 196, 196), fill=(120, 76, 36, 255), width=22 * SCALE)  # braces
d.line(s(196, 52, 52, 196), fill=(120, 76, 36, 255), width=22 * SCALE)
d.rounded_rectangle(s(33, 33, 215, 215), radius=14 * SCALE, outline=(96, 58, 26, 255), width=18 * SCALE)  # frame
for x, y in ((30, 30), (190, 30), (30, 190), (190, 190)):  # metal corner plates with a bolt
    d.rounded_rectangle(s(x, y, x + 28, y + 28), radius=6 * SCALE, fill=(150, 156, 166, 255))
    d.ellipse(s(x + 10, y + 10, x + 18, y + 18), fill=(70, 74, 82, 255))

image = Image.alpha_composite(shadow, box).resize((SIZE, SIZE), Image.LANCZOS)
OUT.parent.mkdir(parents=True, exist_ok=True)
image.save(OUT)
print(f"Wrote {OUT}")
