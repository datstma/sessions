"""Regenerate assets using fonttools 4.59.2 and Pillow 11.3.0 (not needed to build)."""
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'artifacts/branding-tools'))
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont
from PIL import Image
from brand_art import draw_tile

output = ROOT / 'src/Sessions.App/Assets/Fonts'
output.mkdir(parents=True, exist_ok=True)
for weight, name in [(500, 'Medium'), (700, 'Bold'), (800, 'ExtraBold')]:
    font = instantiateVariableFont(TTFont(ROOT / 'branding/fonts/Manrope-variable.ttf'),
                                   {'wght': weight}, updateFontNames=True)
    font.recalcTimestamp = False
    font.save(output / f'Manrope-{name}.ttf')

# Draw the tile SVG-faithfully (square opacity, centred strokes) once at high resolution, then
# downsample straight to each ICO size so small frames are not resampled twice.
master = Image.new('RGBA', (1536, 1536))
draw_tile(master, 0, 0, master.width)
frames = [master.resize((size, size), Image.Resampling.LANCZOS) for size in (256, 128, 64, 48, 32, 24, 16)]
frames[0].save(ROOT / 'src/Sessions.App/Assets/sessions.ico',
               sizes=[frame.size for frame in frames], append_images=frames[1:])
print('Generated Manrope static weights and Sessions icon.')
