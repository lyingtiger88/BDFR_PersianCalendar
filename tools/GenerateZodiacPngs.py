#!/usr/bin/env python3
"""Generate all 108 zodiac PNG assets (9 styles × 12 signs); preserve the 36 approved Cute crops from catalog.json."""
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
    sid = style["id"]
    if sid == "cosmic":
        frame = f'<circle cx="50" cy="50" r="47" fill="url(#disc)"/><circle cx="50" cy="50" r="45" fill="none" stroke="{glow}" stroke-width=".7"/><path d="M18 28 Q50 -3 82 28 M18 72 Q50 103 82 72" fill="none" stroke="{glow}" stroke-opacity=".55" stroke-width=".7"/>' + "".join(f'<circle cx="{x}" cy="{y}" r="{r}" fill="{accent}"/>' for x,y,r in [(13,18,1),(26,10,.7),(83,17,1),(90,36,.7),(12,70,.8),(24,89,1),(78,86,.8),(89,68,1)])
    elif sid == "neon":
        frame = f'<rect x="4" y="4" width="92" height="92" rx="19" fill="{bg}"/><rect x="9" y="9" width="82" height="82" rx="16" fill="none" stroke="{glow}" stroke-width="2"/><rect x="14" y="14" width="72" height="72" rx="12" fill="none" stroke="{accent}" stroke-width=".6"/><path d="M18 25 H28 M18 25 V35 M82 25 H72 M82 25 V35 M18 75 H28 M18 75 V65 M82 75 H72 M82 75 V65" stroke="{glow}" stroke-width="1.5" fill="none"/>'
    elif sid == "minimal":
        frame = f'<rect x="3" y="3" width="94" height="94" rx="2" fill="{bg}"/><circle cx="50" cy="50" r="43" fill="none" stroke="{accent}" stroke-width=".45"/><path d="M50 5 V14 M50 86 V95 M5 50 H14 M86 50 H95" stroke="{accent}" stroke-width=".8"/>'
    elif sid == "watercolor":
        frame = f'<rect width="100" height="100" fill="{bg}"/><path d="M9 28 C4 9 36 2 48 17 C65 1 95 18 85 40 C102 57 82 75 91 84 C67 103 49 84 34 94 C10 87 17 69 6 57 C-1 44 4 36 9 28Z" fill="{glow}" opacity=".34"/><path d="M17 19 C39 4 68 15 82 31 C93 51 78 76 61 85 C39 96 15 77 12 56 C8 39 10 28 17 19Z" fill="{accent}" opacity=".2"/><circle cx="50" cy="50" r="44" fill="none" stroke="{accent}" stroke-opacity=".7" stroke-width=".8" stroke-dasharray="2 2"/>'
    elif sid == "mythic":
        frame = f'<circle cx="50" cy="50" r="47" fill="url(#disc)" stroke="{accent}" stroke-width="1.2"/><circle cx="50" cy="50" r="39" fill="none" stroke="{accent}" stroke-width=".7" stroke-dasharray="1 2"/><path d="M50 4 L55 13 L50 22 L45 13Z M96 50 L87 55 L78 50 L87 45Z M50 96 L45 87 L50 78 L55 87Z M4 50 L13 45 L22 50 L13 55Z M18 18 L24 20 L20 24Z M82 18 L80 24 L76 20Z M18 82 L20 76 L24 80Z M82 82 L76 80 L80 76Z" fill="{accent}"/>'
    elif sid == "luxury":
        frame = f'<rect width="100" height="100" fill="{bg}"/><rect x="5" y="5" width="90" height="90" rx="2" fill="none" stroke="{accent}" stroke-width="1.2"/><rect x="10" y="10" width="80" height="80" fill="none" stroke="{glow}" stroke-width=".6"/><path d="M5 23 Q15 23 15 5 M95 23 Q85 23 85 5 M5 77 Q15 77 15 95 M95 77 Q85 77 85 95" fill="none" stroke="{accent}" stroke-width="1.3"/><path d="M50 6 l3 5 -3 5 -3 -5Z M94 50 l-5 3 -5 -3 5 -3Z M50 94 l-3 -5 3 -5 3 5Z M6 50 l5 -3 5 3 -5 3Z" fill="{accent}"/>'
    else:
        frame = f'<circle cx="50" cy="50" r="47" fill="url(#disc)"/>'
    return f"""<svg xmlns="http://www.w3.org/2000/svg" width="{SIZE}" height="{SIZE}" viewBox="0 0 100 100">
      <defs><radialGradient id="disc" cx="35%" cy="28%" r="80%">
        <stop offset="0" stop-color="{glow}" stop-opacity=".42"/>
        <stop offset="1" stop-color="{bg}" stop-opacity=".10"/>
      </radialGradient>
      <linearGradient id="line" x1="0" y1="0" x2="1" y2="1">
        <stop offset="0" stop-color="{accent}"/><stop offset=".55" stop-color="{glow}"/><stop offset="1" stop-color="{accent}"/>
      </linearGradient></defs>
      {frame}
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
            # Preserve the approved, manually cropped Cute artwork.
            # Always regenerate all six non-Cute styles to replace stale assets.
            if style["id"] in {"cute_1", "cute_2", "cute_3"}:
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
    print(f"Regenerated {count} non-Cute zodiac PNG files (approved Cute artwork preserved) in {OUTPUT}")

if __name__ == "__main__":
    main()
