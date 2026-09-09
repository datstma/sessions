# Sessions — Brand & UI package

The supplied mockups guide the current desktop UI. Read [DESIGN_GUIDE.md](DESIGN_GUIDE.md)
for the agreed adaptations: follow the OS theme, preserve compact layouts, prioritise
readability and use existing features rather than prototype-only additions.

| Path | Purpose |
| --- | --- |
| [tokens/tokens.json](tokens/tokens.json) | Canonical colours, typography and layout tokens |
| [tokens/Theme.axaml](tokens/Theme.axaml) | Generated Avalonia resources; identical app copy under src/Sessions.App/Styles |
| [tokens/tokens.css](tokens/tokens.css) | Generated CSS reference export for both themes |
| [fonts/](fonts/) | Pinned Manrope source, OFL license and regeneration instructions |
| [logo/](logo/) | Supplied SVGs and icon guidance |
| [screens/](screens/) | Original 1440×900 visual references; not feature specifications |
| [AGENTS_SNIPPET.md](AGENTS_SNIPPET.md) | Current repository UI instructions |

Run `python scripts/Generate-BrandTheme.py` from the repository root after editing
tokens. Check in both generated AXAML copies and CSS. The Avalonia export contains
the resources used by the desktop app; CSS also retains the prototype's optional
tracking, motion and shadow values. These are not all implemented desktop effects.

The app bundles generated static Manrope weights and a multi-size Windows icon;
normal .NET builds need neither Python nor network access for those assets. See
[font regeneration](fonts/README.md) for the optional asset toolchain.

Indigo denotes actions, green running state, red destructive actions/errors. Use
the distinct text/fill/hover tokens so small labels remain readable in both themes.
