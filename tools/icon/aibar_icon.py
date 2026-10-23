"""Generate the AIBar liquid-gold brain icon sources.

Usage: python3 -I tools/icon/aibar_icon.py <outdir>
Writes aibar.svg (detailed, 24px and up) and aibar-small.svg (simplified untiled glyph, 16-48px)."""
import math
import pathlib
import sys

def hemisphere(cx, cy, rx, ry, r, n, a0=95, a1=265):
    """Scalloped left hemisphere: n bumps on a half ellipse plus filler circles."""
    pts = []
    for i in range(n):
        a = math.radians(a0 + (a1 - a0) * i / (n - 1))
        pts.append((round(cx + (rx - r) * math.cos(a)), round(cy - (ry - r) * math.sin(a)), r))
    pts += [(cx - rx // 3, cy, round(ry * .62)), (cx - 8, cy - ry // 3, round(ry * .55)), (cx - 8, cy + ry // 3, round(ry * .55))]
    return pts

# Left hemisphere lobes (cx, cy, r); lobes() mirrors them around x=256.
LOBES = hemisphere(256, 262, 172, 166, 52, 7)
SMALL_LOBES = hemisphere(256, 256, 236, 222, 78, 5)

def lobes(spec, gap):
    pts = []
    for cx, cy, r in spec:
        pts.append((cx - gap, cy, r))
        pts.append((512 - cx + gap, cy, r))
    return pts

def circles(pts, attrs):
    return "".join(f'<circle cx="{x}" cy="{y}" r="{r}" {attrs}/>' for x, y, r in pts)

TILE = '''<rect width="512" height="512" rx="112" fill="url(#bg)"/>
<rect x="3" y="3" width="506" height="506" rx="110" fill="none" stroke="#2C3A78" stroke-width="5"/>'''

def icon(spec, *, stroke, gap, level, glow, sulci, wave, tile=True):
    pts = lobes(spec, gap)
    sulci_svg = ""
    if sulci:
        sulci_svg = """
  <g fill="none" stroke="#5E3C06" stroke-width="10" stroke-linecap="round" opacity=".55" clip-path="url(#brain)">
    <path d="M104,296 c22,-14 40,14 62,0 s30,-12 50,2"/><path d="M408,296 c-22,-14 -40,14 -62,0 s-30,-12 -50,2"/>
    <path d="M128,350 c20,-12 36,12 56,0"/><path d="M384,350 c-20,-12 -36,12 -56,0"/>
  </g>
  <g fill="none" stroke="#2C3A78" stroke-width="10" stroke-linecap="round" clip-path="url(#brain)">
    <path d="M140,176 c18,10 30,-8 48,4"/><path d="M372,176 c-18,10 -30,-8 -48,4"/>
  </g>"""
    top = level
    surface = (f"M40,{top} C110,{top-wave} 170,{top+wave} 256,{top} S402,{top-wave} 472,{top}"
               if wave else f"M40,{top} L472,{top}")
    return f"""<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">
<defs>
  <radialGradient id="bg" cx="50%" cy="36%" r="78%">
    <stop offset="0" stop-color="#1D2A6B"/><stop offset=".6" stop-color="#0E1638"/><stop offset="1" stop-color="#060914"/>
  </radialGradient>
  <linearGradient id="gold" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0" stop-color="#F2CC6B"/><stop offset=".4" stop-color="#D4A12A"/><stop offset="1" stop-color="#94640F"/>
  </linearGradient>
  <radialGradient id="empty" cx="50%" cy="40%" r="70%">
    <stop offset="0" stop-color="#16204A"/><stop offset="1" stop-color="#0A1028"/>
  </radialGradient>
  <clipPath id="brain">{circles(pts, "")}</clipPath>
  <filter id="glow" x="-30%" y="-30%" width="160%" height="160%">
    <feGaussianBlur stdDeviation="{glow}" result="b"/>
    <feMerge><feMergeNode in="b"/><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge>
  </filter>
  <filter id="goldglow" x="-30%" y="-30%" width="160%" height="160%">
    <feGaussianBlur stdDeviation="{glow * 1.6}" result="b"/>
    <feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge>
  </filter>
</defs>
{TILE if tile else ""}
<g filter="url(#glow)">{circles(pts, f'fill="#6E9BFF" stroke="#6E9BFF" stroke-width="{stroke * 2}"')}</g>
{circles(pts, 'fill="url(#empty)"')}
<g clip-path="url(#brain)" filter="url(#goldglow)">
  <path d="{surface} L472,480 L40,480 Z" fill="url(#gold)"/>
  <path d="{surface}" fill="none" stroke="#FBE3A0" stroke-width="{max(6, stroke // 2)}" stroke-linecap="round"/>
</g>{sulci_svg}
<rect x="{256 - gap}" y="0" width="{2 * gap}" height="512" fill="#0A0F24" clip-path="url(#brain)"/>
</svg>"""

# level is the gold surface height in the 512 viewBox: lower values mean a fuller brain.
out = pathlib.Path(sys.argv[1]); out.mkdir(parents=True, exist_ok=True)
(out / "aibar.svg").write_text(icon(LOBES, stroke=9, gap=7, level=244, glow=4, sulci=True, wave=14))
(out / "aibar-small.svg").write_text(icon(SMALL_LOBES, stroke=26, gap=16, level=210, glow=0.01, sulci=False, wave=0, tile=False))
