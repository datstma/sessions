# Branding package review — 2026-09-09

Historical review of the original user-provided package against released 0.2.0.
The findings and contrast table below describe that original package, not the current
tokens. The subsequent implementation resolves the applicable findings; see the
updated [design guide](../branding/DESIGN_GUIDE.md) and SESS-024 in
[BACKLOG.md](BACKLOG.md). This review is not a competing specification.

## Resolution — 2026-09-09

The user confirmed OS-following light/dark startup, delegated missing token/asset
decisions, prioritised readability and explicitly excluded mockup-only features.
The 640×480 compact layout is retained. Canonical JSON now generates both theme
exports; Manrope fonts/license and the Windows icon are bundled. New semantic
text/fill/hover colours correct the identified contrast failures. Existing screens,
advanced options and confirmation/recovery states use the shared style vocabulary.
Ambient animation and custom shadow effects are not introduced. Original SVG/PNG
references remain unchanged; the desktop uses native title bars and responsive layouts.

The following sections preserve the **pre-implementation audit** for context.

## Setup and visual direction

The folder is correctly placed at the repository root, and AGENTS.md contains the
provided UI instructions. Its branding references now use explicit repository-relative
links. All referenced package files exist. The theme is not yet imported into the app;
adding the branding folder alone does not change the running application.

All seven PNG references are 1440×900 and were visually reviewed. The five SVGs and
Theme.axaml parse; tokens.json parses, and all 28 shared theme color values match
the AXAML brushes. This is structural validation, not an Avalonia integration test.

The indigo action color separates actions from green running status more clearly
than the current green primary buttons. Rounded app tiles, a stronger Session header,
individual app cards, and the two ownership lists in End confirmation fit Sessions
well. Keep the restrained surfaces, visible local-storage reassurance, and clear
ownership explanation. The cascading-square logo also fits the idea of grouping apps.

## Resolve before implementation

| Finding | Proposed resolution |
| --- | --- |
| AGENTS.md, README, token metadata, and the guide opening say dark by default; the guide's accessibility section and Theme.axaml comment say follow the OS. | Choose one startup rule, then make all documents agree. A theme toggle is new UI behavior and should be specified in PRODUCT.md. |
| The guide raises the minimum to 1024×700 and fixes the sidebar width; PRODUCT.md and the released accessibility work support 640×480 with a compact sidebar. | Keep 1440×900 as the reference size and retain the compact adaptation unless the user explicitly chooses the larger minimum. |
| The guide says Save is disabled only for an empty name. Existing validation also rejects invalid app fields, missing lifetime/focus targets, and invalid timeouts/pauses. | Preserve validation and document it in the guide. Visual changes must not bypass it. |
| The editor reference shows the active Session being edited, which the product blocks. | Render an inactive definition when capturing the editor reference; retain active-run editing protection. |
| References include last-run history, an elapsed timer, global shortcut chips, Recently added, and a theme toggle. These are not current features. | Separate approved visual treatment from feature scope. Do not show invented history, inactive tabs, or shortcuts that do nothing. Define any selected additions in PRODUCT.md. |
| Advanced startup, per-app options, description, invalid input, cleanup recovery, and several confirmation states are absent from the references. | Extend the visual vocabulary to these existing features. Preserve contextual headings, empty-Session start rules, and all current recovery/confirmation flows. |
| The first-run screenshot contains saved Sessions and a running banner. The light detail screenshot shows a running Session, not the same idle state as the dark detail. | Treat these as prototype compositions; capture actual empty-library and matching light/dark states for comparison. Remove the prototype's Show first-run state/Back to library controls. |
| Exact screenshot matching would reproduce wrapped Not running, Add apps, and Keep running labels, browser-style scrollbars, and a mocked Windows title bar. | Match hierarchy, palette, spacing, and shapes. Fix accidental wrapping and use native title-bar behavior; allow platform/font rendering differences. |

## Token and asset gaps

- Make tokens.json the explicit canonical source and provide a repeatable generator.
  CSS is labelled generated, but no generator is included. AXAML currently omits
  font weights, line heights, tracking, overlay, shadow, and motion values; CSS also
  lacks parts of the typography/motion definitions. They are partial exports.
- Add missing layout/control tokens before implementing the guide: for example
  18/22/26 spacing, 26px mark, 40px sidebar action, modal widths, focus border/offset,
  disabled opacity, and compact-layout measurements. Use appropriate Avalonia resource
  types for Thickness, CornerRadius, FontWeight, and durations.
- Reconcile the guide's 9px chip radius with the 7px token, and its minimum 12px type
  with the 11px label token. The guide's 62% overlay also differs from the light
  token's 45%; document whether this is intentionally theme-specific.
- Manrope is named but no font files or font license are supplied. The app currently
  bundles Inter. Bundle the chosen Manrope weights and license for reproducible
  offline rendering; font fallback alone cannot reproduce the references.
- SVG logo assets are present; a Windows ICO is not. Generate the documented icon
  sizes from the supplied tile. The dark mark uses brandHover while the light mark
  uses the dark theme's brand value; document logo-specific color roles rather than
  accidentally recoloring a fixed brand asset through action-state tokens.
- The HTML prototype mentioned by screens/README.md is not included. It would help
  inspect layout and hover behavior but is not required to implement the stills.
- Define reduced-motion behavior before adding the proposed continuously pulsing
  status dot. The guide's opening says animation only follows an event, while its
  motion section explicitly adds ambient animation.

## Contrast measurements

Ratios calculated directly from the supplied opaque sRGB tokens using relative
luminance. These are token-pair checks, not a complete accessibility audit.
Ordinary button and chip text needs 4.5:1; the 3:1 exception applies to large text,
not to all chips. Ratios below 4.5 must not be rounded up to a pass. See
[W3C's contrast guidance](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html).

| Text / background | Dark | Light |
| --- | ---: | ---: |
| Primary text / panel | 12.86 | 16.66 |
| Muted text / panel | 5.96 | 4.83 |
| Muted text / app background | 6.61 | 4.36 |
| Running text / runningSoft | 6.97 | 3.00 |
| White action text / brand | 4.49 | 5.99 |
| White action text / brandHover | 3.21 | 4.49 |
| White action text / danger | 3.49 | 4.38 |

Keep the indigo/green/red identity, but introduce suitable semantic text/fill/hover
tokens where one color cannot serve every role. In particular, light-mode running
text should be darker, and white-on-fill button states need adjustment. Recheck
the resulting controls in both themes rather than copying inaccessible token pairs.

## Suggested implementation sequence

1. Reconcile the decisions above and complete the canonical tokens, font, and icon.
2. Apply the shell, typography, Session header, app cards, and running/ownership state.
3. Restyle editor, app picker, confirmations, and existing advanced/error states.
4. Compare rendered views at 1440×900 in both themes, then run keyboard, compact-size,
   scaling, and existing behavior tests. Retain native verification limits honestly.

Start with these visual changes. Treat history, Recently added, and any other new
subsystems as separate decisions instead of silently introducing them via mockups.

Repository validation after the review documentation: clean Release solution build,
45 Core tests passing, 71 local documentation links/anchors checked, and clean diff
whitespace checks. App/native tests were not rerun because application code is unchanged.
