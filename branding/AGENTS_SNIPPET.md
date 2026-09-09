# Repository instructions snippet

Paste the following into the repository-root AGENTS.md. Links in this example
are relative to the repository root. The current [AGENTS.md](../AGENTS.md) already includes it.

```markdown
## UI look and feel

The visual design of Sessions is specified in [branding/](branding/), relative to the repository root. Treat it as binding.

- Read [branding/DESIGN_GUIDE.md](branding/DESIGN_GUIDE.md) before changing any view, style, or control template.
- Take every color, spacing value, corner radius, font size and font weight from [branding/tokens/](branding/tokens/).
  Do not introduce new values; if something is missing, add it to the tokens first and say so in the PR.
- [branding/screens/](branding/screens/) holds the approved visual references at 1440x900.
  Match their hierarchy, palette, typography and shapes while preserving existing product behaviour,
  readability and the 640x480 compact layout. Mockup-only features are not implementation scope.
- Follow the OS light/dark theme by default. Check every change in both themes.
- [branding/tokens/tokens.json](branding/tokens/tokens.json) is canonical; regenerate exports with
  `python scripts/Generate-BrandTheme.py`. Structural grid indices, vector geometry and behaviour
  limits are not visual tokens. Bundled font/icon sources and regeneration are documented in branding/.
- Semantic colors are fixed: indigo = primary action, green = running/healthy, red = destructive.
- Copy follows the voice rules in the design guide: plain, calm, second person, no exclamation marks.
```
