#!/usr/bin/env python3
"""Generate the 84 approved zodiac PNG assets (7 styles × 12 signs) from catalog.json."""
from pathlib import Path
import json
import cairosvg

ROOT = Path(__file__).resolve().parents[1]
CATALOG = ROOT / "picture" / "zodiac" / "catalog.json"
OUTPUT = ROOT / "picture" / "zodiac" / "png"
SIZE = 512

def svg_for(sign, style):
    bg = style["bg"]
    accent = style["accent"]
    glow = style["glow"]
    stroke = float(style.get("stroke", 3.5))
    # The transparent outer canvas lets the artwork sit cleanly on any Zurvan theme.
    # A softly tinted inner medallion and double ring provide style-specific framing.
    return f"""<svg xmlns="http://www.w3.org/2000/svg" width="{SIZE}" height="{SIZE}" viewBox="0 0 100 100">
      <defs>
        <radialGradient id="disc" cx="35%" cy="28%" r="80%">
          <stop offset="0" stop-color="{glow}" stop-opacity=".42"/>
          <stop offset="1" stop-color="{bg}" stop-opacity=".10"/>
        </radialGradient>
        <linearGradient id="line" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stop-color="{accent}"/>
          <stop offset=".55" stop-color="{glow}"/>
          <stop offset="1" stop-color="{accent}"/>
        </linearGradient>
      </defs>
      <circle cx="50" cy="50" r="47" fill="url(#disc)"/>
      <circle cx="50" cy="50" r="45" fill="none" stroke="{glow}" stroke-opacity=".48" stroke-width=".8"/>
      <circle cx="50" cy="50" r="41.5" fill="none" stroke="{accent}" stroke-opacity=".52" stroke-width=".45" stroke-dasharray="1.2 2.2"/>
      <g fill="{accent}" opacity=".9">
        <circle cx="50" cy="7" r="1.1"/><circle cx="50" cy="93" r="1.1"/>
        <circle cx="7" cy="50" r="1.1"/><circle cx="93" cy="50" r="1.1"/>
        <path d="M50 12 l1.2 2.4 2.4 1.2-2.4 1.2L50 19.2l-1.2-2.4-2.4-1.2 2.4-1.2z"/>
        <path d="M50 80 l1.2 2.4 2.4 1.2-2.4 1.2L50 87.2l-1.2-2.4-2.4-1.2 2.4-1.2z"/>
      </g>
      <path d="{sign["path"]}" fill="none" stroke="{glow}" stroke-opacity=".38" stroke-width="{stroke+2.1}" stroke-linecap="round" stroke-linejoin="round" transform="translate(3 3)"/>
      <path d="{sign["path"]}" fill="none" stroke="url(#line)" stroke-width="{stroke}" stroke-linecap="round" stroke-linejoin="round"/>
      <circle cx="50" cy="50" r="1.25" fill="{accent}" opacity=".8"/>
    </svg>"""

def main():
    data = json.loads(CATALOG.read_text(encoding="utf-8"))
    OUTPUT.mkdir(parents=True, exist_ok=True)
    count = 0
    for style in data["styles"]:
        for sign in data["signs"]:
            name = f'{style["id"]}_{sign["id"]}.png'
            target = OUTPUT / name
            cairosvg.svg2png(bytestring=svg_for(sign, style).encode("utf-8"),
                             write_to=str(target), output_width=SIZE, output_height=SIZE)
            count += 1
    print(f"Generated {count} zodiac PNG files in {OUTPUT}")

if __name__ == "__main__":
    main()
