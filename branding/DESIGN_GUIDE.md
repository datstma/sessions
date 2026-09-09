# Sessions — design guide

Sessions is a calm, local desktop utility. Use the references' soft panels, rounded
cards, generous spacing, confident indigo actions, and readable status. This is a
desktop tool, not a dashboard or a marketing page.

## Implementation direction — agreed 2026-09-09

Follow the **OS light/dark setting** by default, including theme changes while the
app is open. The reference size is **1440×900**; retain the **640×480 logical-pixel**
minimum and compact layout below 900px. The user approved filling token/asset gaps
with consistent, readable adaptations of the mockups.

The screenshots establish visual direction, not additional functionality or exact
pixel matching. Ignore prototype history, Recently added, elapsed timers, shortcut
chips, theme toggle, overflow menu and demo-state controls. Preserve the existing
advanced options, validation, empty-Session start guard, active-run editing rules,
confirmation defaults and recovery flows. [PRODUCT.md](../docs/PRODUCT.md) defines
behaviour; [ARCHITECTURE.md](../docs/ARCHITECTURE.md) defines implementation boundaries.

## Foundations

Use [tokens/tokens.json](tokens/tokens.json) as the canonical source. Add missing
values there before using them, then run `python scripts/Generate-BrandTheme.py`
from the repository root. Avalonia resources use typed Thickness, CornerRadius,
FontWeight and measurement values. Grid indices, proportional layout definitions,
vector geometry, validation limits and runtime durations are not style tokens.

| Role | Meaning | Examples |
| --- | --- | --- |
| Indigo | Primary action or selection | Start, New, Save, Add; selected row and focus |
| Green | Running state or an app that stays open | Running control, active banner, ownership list |
| Red | Destructive action or error | End, Delete, cleanup warning |

Use `brand`/`brandHover` for filled actions and `onBrand` for their text.
`brandForeground` is the readable indigo for text and focus rings. Likewise,
`dangerFill`/`dangerHover` and `danger` separate destructive fills from warning text.
Do not use green for primary actions. Supporting text uses `textMuted`, never a
faint opacity applied to essential instructions.

**Type.** Bundle Manrope Medium (500), Bold (700) and ExtraBold (800), with Segoe UI
fallback. Use 800 for headings/buttons, 700 for row titles/status, 500 for body.
The scale is 12, 13, 14, 15, 16, 22, 30 and 40px. Never go below 12px. Large titles
wrap; file paths may ellipsize with full details exposed through tooltips/automation.
The prototype's letter-spacing hints are optional; readable native text takes priority.

**Space and shape.** Use roughly 20–24px card padding, 8px card gaps, 24px section
spacing; smaller compact margins preserve useful width. Cards use 12px radii,
buttons 11px, inputs 10px, dialogs 16px, status chips 9px. App/Session avatars are
rounded squares. Primary controls are at least 44px tall.

**Surfaces.** `appBackground` is the ground, `panel` the sidebar/cards, `panelAlt`
inputs/insets/secondary buttons, `line` the thin separating border. Cards need no
shadow. Dialogs use the theme's overlay (62% dark, 45% light); the stronger boundary
and overlay are sufficient without requiring a custom shadow.

**Motion.** No ambient pulsing or timer animation. Retain native control feedback;
the optional motion/shadow tokens remain reference values rather than promises of
custom animation. Future animation must respect reduced-motion preferences.

## Shell and screens

- **Shell:** native Windows title bar and branded application icon. The 288px sidebar
  contains the mark, Sessions heading, count, indigo New Session action, initial/name
  rows and local-storage reassurance. Selection uses `brandSoft` and an indigo avatar.
  Compact mode narrows it to 184px. Main content scrolls with a 1000px maximum width.
- **First run:** brand tile, “Get your apps together.”, brief supporting copy, Create
  your first Session, and three step cards. This is a real empty library, without a
  running banner or saved Sessions. Cards stack in compact mode.
- **Detail:** hero card with 56px initial, name, app count, description and Start/Edit
  actions. One card per app: 44px tile with a 30px executable icon (initial fallback), name, role, order/path and status.
  Full-width layouts place actions/status to the right; compact layouts move them
  below. Preserve status click behaviour and the distinction between already-open
  and Session-owned apps. End-condition and ownership explainer cards sit below.
- **Running:** green-tinted banner with real run status and a red End action; active
  avatar gets a green ring. Recovery and focus messages remain available. Do not add
  fake counters, history or timers. All state also has a text label.
- **Editor:** one main card, clear name field, ordered app list and existing move/remove
  controls. Description, selected-app options and Session startup options use inset
  expandable sections. Keep contextual headings and the end-condition choices.
  Save/Cancel stay in the footer while the form scrolls. Editing never launches apps.
- **Picker:** 820px default width, source pills for Start menu and Running apps, search,
  scrollable checkbox choices with icons/initials, selected-row tint and explicit Add.
  Preserve selections across filtering/source switches, browse, source errors and
  unavailable-entry explanations. Keep the existing 520×460 minimum.
- **End confirmation:** 640px dialog within the available window, clear save-work copy
  and a conditional warning naming apps opted into automatic force quit. Two inset lists: Asked to close (owned apps) / Stays open
  (already-running apps). Cancel retains initial focus and Escape behaviour. Red
  confirm is never the default keypress. Other confirmations use the same surfaces.
- **Apps still open:** named recovery rows in the active-run panel, with Bring forward
  and Force quit… actions. The force confirmation names its app and Session, warns
  about unsaved changes and uses the same modal surfaces with Cancel as the default.
  App options expose automatic force quit as an unchecked checkbox with a save-work warning.

## Interaction, copy and accessibility

Selection only changes what is shown; starting is a separate action. Save remains
subject to all existing name, app, lifetime/focus-target and startup-value validation.
Loading/errors use plain text and available recovery actions. Unavailable controls
retain native disabled feedback; do not weaken validation to match a prototype.

Use visible focus rings and preserve keyboard navigation, focus return, accessible
names and live error/status messages. Ordinary text—including buttons and status
chips—must reach **4.5:1** contrast in both themes; 3:1 applies only to genuinely
large text. Never rely on colour or a presence dot alone. Keep hit targets at least
32px, with primary controls at least 44px.

Copy is plain, calm, second person, with no exclamation marks. Say what happens
before it happens. Prefer verbs such as Start Session, End Session, Add apps and
Save changes. Respect existing product explanations rather than shortening away
ownership or unsaved-work consequences.

Render both themes at the reference size and compact minimum, and exercise 125%,
150% and 200% scaling. Headless rendering does not establish native screen-reader,
Windows text-size or physical-monitor behaviour; retain those verification limits.
