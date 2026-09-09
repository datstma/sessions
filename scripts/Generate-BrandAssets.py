"""Regenerate assets using fonttools 4.59.2 and Pillow 11.3.0 (not needed to build)."""
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'artifacts/branding-tools'))
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont
from PIL import Image, ImageDraw

output = ROOT / 'src/Sessions.App/Assets/Fonts'
output.mkdir(parents=True, exist_ok=True)
for weight, name in [(500, 'Medium'), (700, 'Bold'), (800, 'ExtraBold')]:
    font = instantiateVariableFont(TTFont(ROOT / 'branding/fonts/Manrope-variable.ttf'),
                                   {'wght': weight}, updateFontNames=True)
    font.recalcTimestamp = False
    font.save(output / f'Manrope-{name}.ttf')

scale = 8
image = Image.new('RGBA', (96 * scale, 96 * scale))
draw = ImageDraw.Draw(image)

def render(node, dx=0, dy=0):
    tag = node.tag.split('}')[-1]
    if tag == 'g':
        values = list(map(float, re.findall(r'[-\d.]+', node.attrib.get('transform', ''))))
        dx += values[0] if values else 0
        dy += values[1] if len(values) > 1 else 0
    if tag == 'rect':
        values = node.attrib
        x = (float(values.get('x', 0)) + dx) * scale
        y = (float(values.get('y', 0)) + dy) * scale
        draw.rounded_rectangle((x, y, x + float(values['width']) * scale,
                                y + float(values['height']) * scale),
            float(values.get('rx', 0)) * scale, fill=values.get('fill'),
            outline=values.get('stroke'), width=round(float(values.get('stroke-width', 0)) * scale))
    for child in node:
        render(child, dx, dy)

render(ET.parse(ROOT / 'branding/logo/sessions-tile.svg').getroot())
image.resize((256, 256), Image.Resampling.LANCZOS).save(
    ROOT / 'src/Sessions.App/Assets/sessions.ico',
    sizes=[(size, size) for size in (16, 24, 32, 48, 64, 128, 256)])
print('Generated Manrope static weights and Sessions icon.')
