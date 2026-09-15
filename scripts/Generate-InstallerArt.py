"""Regenerate the MSI dialog bitmaps with Pillow 11.3.0 (see branding/fonts/README.md; not needed to build)."""
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'artifacts/branding-tools'))
from PIL import Image, ImageDraw, ImageFont
from brand_art import TILE, WHITE, draw_mark, draw_tile

SCALE = 4  # Draw large, then downsample for smooth edges.
output = ROOT / 'installer/Assets'
output.mkdir(parents=True, exist_ok=True)


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
draw_tile(banner, (width - tile - 12) * SCALE, (height - tile) / 2 * SCALE, tile * SCALE)
save(banner, 'InstallerBanner.bmp', (width, height))
print('Generated installer dialog and banner bitmaps.')
