"""Shared Pillow drawing of branding/logo/sessions-tile.svg for the icon and installer generators."""
from PIL import Image, ImageDraw

# Colours of the fixed tile in branding/logo/sessions-tile.svg.
TILE = (0x5C, 0x66, 0xF5)
WHITE = (0xFF, 0xFF, 0xFF)


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


def draw_tile(image, left, top, size):
    """Draw the whole 96-unit tile: indigo with a 20-unit corner radius, mark offset by 24 units."""
    unit = size / 96
    ImageDraw.Draw(image).rounded_rectangle((left, top, left + size, top + size), 20 * unit, fill=TILE)
    draw_mark(image, left + 24 * unit, top + 24 * unit, unit)
