"""Regenerate the MSI dialog bitmaps with Pillow 11.3.0 (see branding/fonts/README.md; not needed to build)."""
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'artifacts/branding-tools'))
from PIL import Image, ImageDraw, ImageFont

# Colours match the fixed tile in branding/logo/sessions-tile.svg, which also supplies the app icon.
TILE = (0x5C, 0x66, 0xF5)
WHITE = (0xFF, 0xFF, 0xFF)
SCALE = 4  # Draw large, then downsample for smooth edges.
output = ROOT / 'installer/Assets'
output.mkdir(parents=True, exist_ok=True)


def draw_mark(image, left, top, unit):
    """Draw the tile's cascading squares (a 48-unit box) as the SVG does: element opacity, and a
    3-unit background-coloured stroke centred on the edge that separates overlapping squares."""
    for x, y, opacity, stroked in [(4, 4, 0.45, False), (14.5, 14.5, 0.72, True), (25, 25, 1.0, True)]:
        layer = Image.new('RGBA', image.size)
        draw = ImageDraw.Draw(layer)
        box = lambda inset: (left + (x + inset) * unit, top + (y + inset) * unit,
                             left + (x + 19 - inset) * unit, top + (y + 19 - inset) * unit)
        if stroked:
            draw.rounded_rectangle(box(-1.5), 6.5 * unit, fill=TILE)
            draw.rounded_rectangle(box(1.5), 3.5 * unit, fill=WHITE)
        else:
            draw.rounded_rectangle(box(0), 5 * unit, fill=WHITE)
        layer.putalpha(layer.getchannel('A').point(lambda value: round(value * opacity)))
        image.alpha_composite(layer)


def save(image, name, size):
    image.resize(size, Image.Resampling.LANCZOS).convert('RGB').save(output / name, format='BMP')


# Welcome and Finished dialogs: text starts about 180px across, so the art stays in the left strip.
width, height, strip = 493, 312, 164
dialog = Image.new('RGBA', (width * SCALE, height * SCALE), WHITE)
draw = ImageDraw.Draw(dialog)
draw.rectangle((0, 0, strip * SCALE, height * SCALE), fill=TILE)
unit = 2 * SCALE
draw_mark(dialog, (strip * SCALE - 48 * unit) / 2, 70 * SCALE, unit)
draw = ImageDraw.Draw(dialog)
font = ImageFont.truetype(str(ROOT / 'src/Sessions.App/Assets/Fonts/Manrope-ExtraBold.ttf'), 26 * SCALE)
draw.text((strip * SCALE / 2, 196 * SCALE), 'Sessions', font=font, fill=WHITE, anchor='mm')
save(dialog, 'InstallerDialog.bmp', (width, height))

# Other dialogs draw their title on the left of the banner, so the tile sits at the right edge.
width, height, tile = 493, 58, 40
banner = Image.new('RGBA', (width * SCALE, height * SCALE), WHITE)
draw = ImageDraw.Draw(banner)
left, top = (width - tile - 12) * SCALE, (height - tile) / 2 * SCALE
draw.rounded_rectangle((left, top, left + tile * SCALE, top + tile * SCALE), 20 / 96 * tile * SCALE, fill=TILE)
unit = tile / 96 * SCALE
draw_mark(banner, left + 24 * unit, top + 24 * unit, unit)
save(banner, 'InstallerBanner.bmp', (width, height))
print('Generated installer dialog and banner bitmaps.')
