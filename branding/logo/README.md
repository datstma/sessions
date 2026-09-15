# Logo

The mark is three rounded squares cascading down-right: apps stacking into one set, the last one live in brand indigo.

- `sessions-mark-dark.svg` / `sessions-mark-light.svg` — the mark on the app background. The 3px stroke is the *background color*, not a border: it keeps the gaps between squares crisp. On another surface, change the stroke to that surface's color.
- `sessions-mark-mono.svg` — single-color version driven by currentColor. Use in the title bar and anywhere the mark must inherit text color.
- `sessions-tile.svg` — app icon / installer tile: white mark on brand indigo, 20/96 corner radius. Export to ICO at 16, 24, 32, 48, 64, 128, 256.
- `sessions-lockup.svg` — mark + wordmark for the sidebar header and About dialog. Wordmark is Manrope ExtraBold, tracking -0.02em.

Clear space: one square width on all sides. Minimum size 16px — the mark still reads because the squares keep a 3px gap. Never rotate it, never restack it, never recolor the live square anything but brand indigo (or white on an indigo fill).

## Desktop integration

The supplied SVGs remain unchanged reference artwork. `Views/BrandMark.axaml`
recreates the cascading squares as native vectors using the current theme's
muted, primary-text and readable-indigo colours. This keeps the mark legible on
both sidebar and card surfaces without introducing an SVG runtime dependency.

The fixed indigo tile is used for the Windows icon; its white mark is artwork,
not small action text. [Generate-BrandAssets.py](../../scripts/Generate-BrandAssets.py)
exports all seven ICO sizes above into `src/Sessions.App/Assets/sessions.ico`.
Both windows and the application executable use that icon. The MSI uses the
published executable's icon for Installed apps and the Start menu shortcut.
The repository README uses the supplied tile SVG directly.
[Generate-InstallerArt.py](../../scripts/Generate-InstallerArt.py) draws the installer's
493×312 dialog and 493×58 banner bitmaps (`installer/Assets`) from the tile's colour and
geometry, honouring the SVG's square opacity and centred background-coloured strokes, with
the Manrope ExtraBold wordmark. Installer dialogs are light-only.
