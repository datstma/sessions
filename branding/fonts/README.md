# Manrope

`Manrope-variable.ttf` and [OFL.txt](OFL.txt) are unmodified upstream files from
[Google Fonts commit fb629caaa15ad25c051089c98f09cf6c8e30a86b](https://github.com/google/fonts/tree/fb629caaa15ad25c051089c98f09cf6c8e30a86b/ofl/manrope).
The upstream filename is `Manrope[wght].ttf`. Manrope is licensed under SIL OFL 1.1.
No reserved font name is specified in this license.

[Generate-BrandAssets.py](../../scripts/Generate-BrandAssets.py) instantiates static
Medium (500), Bold (700) and ExtraBold (800) fonts into the app's Assets/Fonts folder.
Static weights give predictable offline rendering. The script also generates the
Windows ICO from the supplied `logo/sessions-tile.svg`; its small SVG reader supports
that tile's rectangles/translations, not arbitrary SVG artwork.

To regenerate from the repository root (Python 3 required):

```powershell
python -m pip install --target artifacts/branding-tools fonttools==4.59.2 Pillow==11.3.0
python scripts/Generate-BrandAssets.py
```

Check in the generated fonts/icon. These tools are not runtime or .NET build
dependencies. The project copies OFL.txt into `licenses/Manrope-OFL.txt` in build
and publish output. Retain the copyright/license when redistributing the fonts.
