#!/usr/bin/env python3
"""Generate the 84 approved zodiac PNG assets (7 styles × 12 signs) from catalog.json."""
from pathlib import Path
import json
from io import BytesIO

try:
    import cairosvg
except Exception:
    cairosvg = None

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
CATALOG = ROOT / "picture" / "zodiac" / "catalog.json"
OUTPUT = ROOT / "picture" / "zodiac" / "png"
SIZE = 512
GLYPHS = {
    "aries": "♈", "taurus": "♉", "gemini": "♊", "cancer": "♋",
    "leo": "♌", "virgo": "♍", "libra": "♎", "scorpio": "♏",
    "sagittarius": "♐", "capricorn": "♑", "aquarius": "♒", "pisces": "♓"
}

def svg_for(sign, style):
    bg, accent, glow = style["bg"], style["accent"], style["glow"]
    stroke = float(style.get("stroke", 3.5))
    return f"""<svg xmlns="http://www.w3.org/2000/svg" width="{SIZE}" height="{SIZE}" viewBox="0 0 100 100">
      <defs><radialGradient id="disc" cx="35%" cy="28%" r="80%">
        <stop offset="0" stop-color="{glow}" stop-opacity=".42"/>
        <stop offset="1" stop-color="{bg}" stop-opacity=".10"/>
      </radialGradient>
      <linearGradient id="line" x1="0" y1="0" x2="1" y2="1">
        <stop offset="0" stop-color="{accent}"/><stop offset=".55" stop-color="{glow}"/><stop offset="1" stop-color="{accent}"/>
      </linearGradient></defs>
      <circle cx="50" cy="50" r="47" fill="url(#disc)"/>
      <circle cx="50" cy="50" r="45" fill="none" stroke="{glow}" stroke-opacity=".48" stroke-width=".8"/>
      <circle cx="50" cy="50" r="41.5" fill="none" stroke="{accent}" stroke-opacity=".52" stroke-width=".45" stroke-dasharray="1.2 2.2"/>
      <g fill="{accent}" opacity=".9"><circle cx="50" cy="7" r="1.1"/><circle cx="50" cy="93" r="1.1"/>
        <circle cx="7" cy="50" r="1.1"/><circle cx="93" cy="50" r="1.1"/></g>
      <path d="{sign["path"]}" fill="none" stroke="{glow}" stroke-opacity=".38" stroke-width="{stroke+2.1}" stroke-linecap="round" stroke-linejoin="round" transform="translate(3 3)"/>
      <path d="{sign["path"]}" fill="none" stroke="url(#line)" stroke-width="{stroke}" stroke-linecap="round" stroke-linejoin="round"/>
    </svg>"""

def hex_rgba(value, alpha=255):
    value = value.lstrip("#")
    if len(value) == 8:
        return tuple(int(value[i:i+2], 16) for i in (2, 4, 6)) + (alpha,)
    return tuple(int(value[i:i+2], 16) for i in (0, 2, 4)) + (alpha,)

def pillow_fallback(sign, style, target):
    """Portable fallback if Cairo is unavailable on a Windows build runner."""
    bg, accent, glow = style["bg"], style["accent"], style["glow"]
    image = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.ellipse((16, 16, 496, 496), fill=hex_rgba(bg, 42), outline=hex_rgba(glow, 180), width=4)
    draw.ellipse((36, 36, 476, 476), outline=hex_rgba(accent, 150), width=2)
    draw.ellipse((58, 58, 454, 454), outline=hex_rgba(glow, 170), width=2)
    glyph = GLYPHS.get(sign["id"], "✦")
    font = None
    for candidate in ("seguisym.ttf", "Segoe UI Symbol.ttf", "DejaVuSans.ttf"):
        try:
            font = ImageFont.truetype(candidate, 260)
            break
        except OSError:
            pass
    if font is None:
        font = ImageFont.load_default()
    box = draw.textbbox((0, 0), glyph, font=font)
    x = (SIZE - (box[2] - box[0])) / 2 - box[0]
    y = (SIZE - (box[3] - box[1])) / 2 - box[1] - 10
    draw.text((x + 4, y + 4), glyph, font=font, fill=hex_rgba(glow, 150), stroke_width=3)
    draw.text((x, y), glyph, font=font, fill=hex_rgba(accent), stroke_width=1, stroke_fill=hex_rgba(glow))
    for px, py in ((256, 32), (256, 480), (32, 256), (480, 256)):
        draw.ellipse((px-5, py-5, px+5, py+5), fill=hex_rgba(accent))
    image.save(target, format="PNG", optimize=True)

def main():
    data = json.loads(CATALOG.read_text(encoding="utf-8"))
    OUTPUT.mkdir(parents=True, exist_ok=True)
    count = 0
    for style in data["styles"]:
        for sign in data["signs"]:
            target = OUTPUT / f'{style["id"]}_{sign["id"]}.png'
            # The user will provide independent artwork for Cute 2 and 3.
            # Do not synthesize lookalike files or overwrite manually cropped PNGs.
            if target.exists() or style["id"] in {"cute_1", "cute_2", "cute_3"}:
                continue
            if cairosvg is not None:
                try:
                    cairosvg.svg2png(bytestring=svg_for(sign, style).encode("utf-8"),
                                     write_to=str(target), output_width=SIZE, output_height=SIZE)
                except Exception:
                    pillow_fallback(sign, style, target)
            else:
                pillow_fallback(sign, style, target)
            count += 1
    print(f"Generated {count} missing base zodiac PNG files (existing custom art preserved) in {OUTPUT}")

if __name__ == "__main__":
    main()
