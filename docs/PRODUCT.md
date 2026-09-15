# Sessions — Product Definition

This document is the source of truth for product intent, user-visible behaviour, and scope. [ARCHITECTURE.md](ARCHITECTURE.md) describes the intended implementation. Capabilities described here express product direction; they are not a claim that they are already implemented.

See [DEVELOPMENT_NOTES.md](DEVELOPMENT_NOTES.md) for design rationale and findings, and [BACKLOG.md](BACKLOG.md) for outstanding work. Those records do not override the scope in this document.

## Distribution

The accepted Windows distribution direction is occasional self-contained x64 MSI
releases on GitHub. Installations are per-user, include the .NET runtime, and add a
Start menu shortcut. Upgrades and uninstall preserve saved Sessions. Users install
a newer MSI to update; an automatic updater is outside the implemented scope.
Installer changes require Sessions to be closed through its normal confirmation
flow. Version 0.1.0 is the first unsigned development preview, distributed through
[GitHub Releases](https://github.com/datstma/sessions/releases/tag/v0.1.0).
See [RELEASING.md](RELEASING.md) for the build and release process and validation limits.

## Project maturity

Sessions is in **Alpha**, beginning with the 0.5.0 release designation. The core
Session workflow is available for everyday testing. Features and saved-data formats
may change, and some apps require manual handling. Feedback and bug reports guide
further development; Alpha does not imply all real-app or native checks have passed.

The intended progression is:

- **Alpha:** usable core workflows with ongoing feature development and compatibility
  changes disclosed in each release.
- **Beta:** the agreed 1.0 feature set is complete, with broader real-app testing and
  reliable upgrades. The complete future backlog is not a Beta requirement.
- **1.0:** those workflows are dependable, compatibility expectations are clear, and
  remaining limitations are documented.

These are readiness criteria, not scheduled releases. Version numbering and release
metadata follow [RELEASING.md](RELEASING.md#versioning).

### 1.0 feature set

Agreed with the maintainer on 2026-09-15 (SESS-037). Everything already implemented
above remains part of 1.0. The additions below are planned unless marked implemented; each has
its own backlog item that settles detailed behaviour before implementation.

- Duplicate a Session (SESS-038, implemented).
- Remember the window size, position and last selected Session (SESS-039, implemented).
- Optional apps: let a Session continue when a chosen app fails to start (SESS-040).
- A saved-data compatibility commitment: all earlier library formats stay readable,
  a backup is written before a save upgrades the format, and the format is frozen at
  Beta (SESS-041).
- An About and support view with version, license, release notes and the local data
  folder, without sending information anywhere (SESS-042).
- Export and import a single Session as a portable file (SESS-043).
- Open a website as part of a Session (SESS-044).
- Start a Session from a desktop shortcut or the command line (SESS-045).
- Launch Microsoft Store and other packaged apps (SESS-046).

Beta additionally requires the hands-on checks in SESS-001, SESS-010 and SESS-021, a
recorded real-app validation matrix and an up-to-date user guide with known limitations
(SESS-048). Whether 1.0 binaries are code-signed is decided before Beta (SESS-047).

Not part of 1.0: gaming utility profile integrations (SESS-028), Philips Hue, Home
Assistant, third-party plugins, command/PowerShell items, starting with Windows or tray
behaviour, notifications, launch stages, arranging launched apps' windows, crash recovery of ownership
and audio, diagnostic export, localization, and the out-of-scope list below.

## Vision

**Sessions** is an open-source desktop application for creating, launching, managing, and ending reusable computer sessions.

A Session represents a context the user wants to enter, rather than merely an application to launch. Sessions should make it easy to define everything that happens when entering and leaving that context.

Examples include gaming, flight simulation, work, streaming, development, photography, and music production. Gaming is one use case among many; the product must not be designed primarily around games or game launchers.

## What a Session contains

1. **Before actions:** prepare the environment, start supporting applications, run commands, and change system settings.
2. **Main activity:** an optional application or process whose lifetime normally defines the Session.
3. **While-running behaviour:** observe process state and show Session state, with the possibility of responding to runtime events in the future.
4. **After actions:** clean up, stop applications started by the Session, and restore settings where appropriate.

Actions are ordered. Users should eventually be able to add, remove, configure, and reorder them visually.

### Session lifetime

- **Process-bound Session:** the main process determines the Session's lifetime. For example, DCS.exe exiting begins Session cleanup.
- **User-bound Session:** the Session remains active until the user presses End Session. A Work Session may use this model without a main application.

Do not assume every Session has a single executable determining its lifetime.

### Ownership and cleanup

Sessions must distinguish applications that were already running from applications it started for the Session. Cleanup should normally stop only applications owned by that Session. Applications that were already running should normally remain running.

For example, if Tobii Game Hub is already running and Sessions starts MOZA Cockpit and SRS, ending the Session leaves Tobii running and stops MOZA and SRS. Cleanup must never blindly stop every configured application.

Temporary settings should be restored where appropriate and configured. The execution responsibilities that support these requirements are described in [ARCHITECTURE.md](ARCHITECTURE.md#process-ownership-and-cleanup).

## Example Sessions

These examples illustrate product direction, including capabilities beyond the first milestone.

### DCS flight Session

**Before:** start Tobii Game Hub, MOZA Cockpit, and DCS SimpleRadio Standalone; configure audio devices if required.

**Main:** launch DCS World.

**After:** stop DCS SimpleRadio Standalone, MOZA Cockpit, and Tobii Game Hub only if this Session started them.

### Work Session

**Before:** start Slack and Outlook, open a specific browser profile and selected websites, start Teams, and select the preferred audio devices.

**Main:** use a designated main application or remain active until the user explicitly ends the Session.

**After:** close applications started by the Session and restore temporary settings if configured.

### Streaming Session

**Before:** start OBS, Discord, Stream Deck software, and audio utilities; set a Windows power profile; start a game or another main application.

**After:** stop applications owned by the Session and restore system state.

## Product and UX principles

Sessions should feel like a modern launcher, not an enterprise administration console. The primary experience should be:

1. See my Sessions.
2. Select a Session.
3. Start it.
4. Clearly see what Sessions is doing.
5. See that the Session is active.
6. End it when appropriate.

Creating and editing a Session should be easy. Use progressive disclosure: make common options obvious and advanced configuration available without overwhelming new users. Avoid exposing unnecessary technical complexity.

The chosen UI direction is a named Session sidebar beside a detail view. Session order stays stable; selecting a Session only changes the view and never starts or ends anything. The sidebar uses names and initial badges so navigation does not depend on recognising icons. The selected Session shows its description, ordered apps, end condition, and a primary Start Session action when it has at least one app. Editing is a separate mode, with an explicit save or cancel; browsing is temporarily disabled while editing to protect the draft.

When there are no Sessions, the main view offers **Create your first Session** with a short explanation. The sidebar appears once a Session has been created. Creation asks for a name, apps to open in order, and when the Session ends. Description, arguments, and working directory are optional and progressively disclosed. The default lifetime is user-bound; users may instead choose an app whose exit asks for confirmation to end the Session. A Session can be created without apps and configured later. Saving a Session never launches it.

The current application implements this navigation, local creation/editing, executable-file selection, app removal and reordering, and both lifetime configurations. It follows the system light/dark theme by default, with appearance preferences available in Settings. Sessions are saved on this computer and loaded at startup. Save failures retain the draft; load failures show a retry action and prevent replacing an unreadable library. Start Session opens configured apps using the saved startup mode and changes to an active run with End Session available. Sample Sessions are used only in tests, not inserted into the user's library.

The editor explains invalid names, executable paths, app choices and startup values
beside the affected fields. Whitespace-only required inputs are invalid. App rows
identify problems even when their options are collapsed, using “Unnamed app” when
needed. While Save is disabled by invalid input, **Review fields** remains available
in the footer. It selects the first affected app, opens the relevant options, and
scrolls the field and its explanation into view with keyboard focus. Corrections
update feedback immediately; repeated review proceeds to the next remaining problem.
Invalid retained timing values stay visible even when their option is inactive,
so switching launch/readiness modes or disabling an override cannot hide a blocker.
Reviewing a field does not change those choices or reset its value.

Saving validates configuration, not current file availability. Nonblank executable
paths on disconnected or portable drives remain saveable; Sessions checks executable
and working-directory availability when launching. Saving never proves an app can
launch and does not rewrite or discard offline paths.

Closing Sessions with a changed new or existing Session draft first offers **Keep
editing**, **Discard**, and **Save**. Keep editing receives initial focus; Enter
on that button and Escape return to the editor without losing changes. Discard
removes only the draft. Save persists successfully before closing; a failed save
keeps the draft and dialog available with the error and a retry. Invalid drafts
cannot be saved from the dialog; its explanation points back to editing. Existing
editor Cancel remains an explicit discard action without another prompt.

An untouched draft, or one whose editable values have been restored, needs no
draft-close prompt. Selection and expanded sections alone are not edits. Changes
include raw invalid inputs, description, app order and all app/startup options.
If closing is requested while a save is already underway, Sessions waits for that
save without starting another write. Failure leaves the editor open and clears
the pending close request; a later ordinary save will not unexpectedly close it.
The close dialog disables its choices while saving. Other in-flight library
operations must also finish before the window can close.

If a Session run is active, resolving the draft leads to the existing active-run
close choices; saving or discarding a draft never authorizes stopping apps. An
unchanged draft can proceed directly to those choices. Pending automatic End
requests wait until draft/close confirmation is resolved. Forced termination or
a crash cannot display this protection; draft recovery across crashes is not implemented.

The visual treatment follows the supplied [branding guide](../branding/DESIGN_GUIDE.md):
Manrope typography, rounded Session/app cards, indigo primary actions, green running
states and red destructive actions. Both light and dark themes use readable text/fill
variants. The sidebar includes the Sessions mark and local-storage reassurance;
the selected Session has a stronger header and individual app cards. End confirmation
separates apps this run owns from already-open apps that stay open. The mockups guide
appearance; history, Recently added, timers and shortcut chips are not part of the
branding refresh. Appearance preferences are implemented separately below. Existing
advanced settings and recovery flows remain.

The main window supports a 640×480 logical-pixel minimum and uses a narrower sidebar
and smaller content margins below 900 logical pixels. Main and picker windows limit
their initial size to the display's working area, within their minimum sizes. The main
window reopens with its last size, position and maximized state, and reselects the last
selected Session; selection never starts it. If that display has changed, the window
moves or shrinks to fit the display it mostly overlaps, or centres on the primary display
when that display is gone. A deleted Session falls back to the first. Minimized windows
reopen normally. Unreadable window state is ignored, and Reset preferences does not
change it. Content
scrolls while editor Save/Cancel stay available; long runtime/error messages share a
bounded scroll area. Keyboard focus starts at creation or the Session list, enters
the name field when editing, and returns to the initiating action after saving or
cancelling (with a visible fallback when that action is no longer available). Removing
the final app returns focus to Add app; a move button becoming unavailable returns
focus to the selected app. Cancelling the active-run close prompt restores focus.
Custom list entries expose full names, app order, and active state to accessibility
clients. Selected-app order and changed errors/runtime messages use polite live
notifications; status remains understandable through text as well as colour. Native
screen-reader output and Windows display/text-scaling trials remain tracked in SESS-010.

**Add app** opens a searchable, scrollable picker with **Start menu** selected by default and **Running apps** available beside it. Start menu lists desktop shortcuts from the user's and shared Windows Start menu folders, with shortcut names, folder context, and executable icons where available. Search matches names, folders, and executable paths. Running apps lists apps with visible windows, grouped by executable path. Users can check several apps and explicitly add them to the draft. Filtering and switching sources preserve choices. Apps already configured in the Session are marked and cannot be added again; selecting another shortcut or source for the same executable replaces that selection, preserving the latest choice's launch settings. Refresh retains surviving selections and removes entries no longer found. A failed source retains its previous results and does not prevent using the other source. **Browse files…** merges chosen `.exe` files with the selection. Cancelling makes no changes to the draft or saved library.

Start menu choices retain the shortcut's executable, arguments, working directory, and Run as administrator setting in the draft's app options. The picker reads shortcuts without opening, repairing, or changing them. Missing targets, unreadable shortcuts, website links, and unsupported launch methods are disabled with an explanation. This covers desktop shortcuts, not every virtual Start menu or Microsoft Store app; packaged-app activation remains unsupported.

Discovery does not take ownership of running processes. Running-app choices copy only names and executable paths, without reading process command lines, working directories, window titles, documents, or browser tabs. Existing processes remain pre-existing for later ownership checks. Background-only apps are absent from Running apps but may be found through Start menu shortcuts. Windows-packaged apps and shared app hosts requiring another activation method are unavailable in Running apps, as are apps whose locations cannot be read. Discovery does not require elevating Sessions; Browse files remains available. The picker never launches anything; individual launching and Start Session follow the execution rules below.

Session details show each app's executable icon where available, with its name initial as a fallback while loading or when the file/icon is missing or unreadable. Icons load in the background and are cached in memory; displaying them never starts apps or changes saved configuration. Session badges in the sidebar and header continue to use Session initials.

Session details show the current presence of each configured executable. A green **Running** button brings an existing window forward when clicked (or activated with the keyboard), restoring it if minimized. **Running · no window** is a passive green status for background apps; **Not running** is a neutral button with a play icon that opens that app. Status is checked when viewing a Session and about every two seconds while the window is visible and not minimized. Checks pause during editing/deletion confirmation and resume automatically. A failed check shows **Status unavailable**, rather than incorrectly claiming the app has stopped.

Presence matches the full executable path, not merely the app name. Apps with several windows use the first eligible matching window found on a fresh check; this does not select a specific document or browser profile. If an app closes or Windows declines focus, an inline message explains what happened or suggests selecting the app from the taskbar. Status checks never activate windows; manual window activation requires an explicit click. The separate optional completion-focus action is described below. Presence and window activation do not launch, adopt, stop, or save process state and do not mark a Session active. Tray-only apps without a visible window cannot be brought forward by this control.

Clicking **Not running** launches only that saved app, using its configured arguments and working directory (or its executable folder when no working directory is configured). The control shows **Starting…** and prevents repeated clicks while launch/presence catches up. A fresh check avoids launching an app that has already opened; if it now has a window, the click focuses it instead. Uncertain presence does not permit a new launch. Missing executables, invalid working folders, and Windows launch errors appear beside the app, with retry when its status can be checked.

A successful launch request does not prove the app stayed running. Presence checks determine the green status. Requests for the same executable are serialized within this Sessions instance, with a ten-second startup grace period to avoid duplicates during a delayed startup/handoff. If the app has not appeared after that period, the UI allows retry and explains that it has not been observed running. A manual launch neither starts a Session nor changes saved configuration: individually opened apps remain independent, stay open when Sessions closes, and must be treated as pre-existing by any later Session run. Individual launches are disabled during an active Session; apps already opened individually remain pre-existing for a later run. Executable launches use Windows desktop Open activation so they do not inherit Sessions’ console/output pipes. App options includes **Run as administrator**, off by default. When enabled, Windows requests approval before launching the app directly with elevation; this can avoid an app restarting itself to elevate. Windows can also request consent when an executable itself requires elevation. Sessions itself stays unelevated. Packaged-app activation remains unsupported.

Session details also offer **Duplicate Session** beside Delete. It opens a new draft
with the saved setup: apps, arguments, lifetime, startup, audio and plugin choices. The
name is selected and suggests "<name> copy", numbered when that name is taken. Nothing
is saved until **Create Session**. Cancel discards the copy; closing Sessions first asks
whether to keep editing, discard or save, even if the copy is untouched. The saved copy
appears directly after the original, becomes selected and is independent: it has its own
identity and app identities, so editing, deleting or starting it never affects the
original. Duplicating the active Session copies only its saved setup; the run continues
unchanged, and the copy cannot start until that run ends.

Session details also offer **Delete Session…**. A confirmation names the Session, explains that its saved setup will be removed permanently, and makes clear that installed apps, files, and running processes are unaffected. Cancel receives initial focus; Enter on Cancel or Escape dismisses the confirmation. Deletion is unavailable while editing or saving, and the rest of the window is disabled during confirmation. Only an explicit Delete Session confirmation saves the reduced library. The displayed Session is removed only after that save succeeds; failure preserves it and offers retry or cancellation. Selection moves to the next Session, or the previous one when deleting the last item; deleting the only Session returns to the first-run screen. There is no undo after successful confirmation.

Start Session captures the saved setup, uses its configured launch mode (in order by default), and leaves matching already-open apps running without taking ownership. Only one Session can be active at a time. Its sidebar marker and a persistent status panel remain visible while browsing another Session; switching selection never starts/ends anything. Editing/deleting the active definition and individual launch controls are unavailable until the run ends. Other saved Sessions can still be edited. Starting waits for an individual launch already in progress to settle.

### Closing an individual app

Each running app card offers **Close…** beside its running status, including Steam
apps. A confirmation names the app and asks you to save your work. It applies to
the currently identified copies, including apps opened outside Sessions or before
the current run. Cancel receives initial focus; Escape cancels and returns focus
to the originating control. Preparing or cancelling never closes an app.

Confirming asks only those captured copies to close normally. Later copies and other
apps stay open. This explicit action is independent of **Close when this Session
ends** and does not use automatic force quit, even when that option is enabled for
Session cleanup. Apps can show their own save prompts; if an app stays running,
inline feedback asks you to check its window. You can retry Close after handling it.
Close acknowledgements use neutral text while waiting for observed exit. Close
feedback, including a still-running warning, clears when a presence check confirms
the app stopped. An unknown status does not count as exit; unrelated launch/focus
errors retain their own feedback.

Closing one app does not end the Session. Closing its tracked main app uses the
existing end-confirmation flow; supporting apps stay open until you choose End.
Close is unavailable during startup, stopping, editing, other main-window
confirmations and an individual launch in progress. While preparing or confirming
a close, competing main-window actions and window closing are blocked.

Steam closing requires current app-specific process evidence, verified against its
installation directory and exact Windows identities. It leaves the shared Steam
client open. If those identities cannot be verified, Sessions explains that you
need to close the app from its own window or Steam. See [plugin limits](PLUGINS.md).

### Application Settings

**Settings** opens a separate window from the sidebar footer or the empty-library
header. Opening it again brings the same window forward. Session drafts, selection
and active runs remain intact. Main-window controls keep their existing guards;
in particular, finish editing a Session before using End. Closing Settings returns
focus to its entry point and discards choices that have not been applied.

Implemented appearance preferences:

- **Color theme:** System (default), Light or Dark. System follows Windows changes
  while the app is open. An explicit choice applies to Sessions and its app picker.
- **Interface size:** 100%, 110%, 125% or 150%, relative to Windows display scaling.
  Enlargement automatically fits the available window size to retain the established
  minimum logical viewport and reachable fixed controls. At the 640×480 main-window
  minimum, interface enlargement is limited to 100%; enlarging the window allows
  more of the chosen size. Text sizing remains independent. Settings itself keeps
  standard interface spacing so its reset controls stay reachable.
- **Text size:** 100%, 110% or 125%, multiplying the existing brand typography and
  line heights without changing the font family or independently enlarging icons
  and spacing. Interface enlargement also enlarges text. Sessions does not read or
  multiply Windows' text-size setting a second time; native behavior remains subject
  to SESS-010 validation.

Choices affect a labelled preview until **Apply preferences** saves successfully.
Successful application updates open windows immediately and survives restart.
**Reset preferences** immediately saves System theme and 100% sizing; its nearby
explanation makes clear that saved Sessions are kept. Apply, reset and retry are
serialized. Window close waits for an in-flight preferences operation to finish;
request close again afterwards. Saving failure retains the selected choices for retry
and leaves the current appearance unchanged.

Preferences are stored separately in `%LOCALAPPDATA%\Sessions\preferences.json`.
A missing file uses defaults without writing. Invalid, unsupported or unreadable
preferences leave the app usable and show a warning with access to Settings.
**Try loading again** retries without changing the file. Ordinary Apply is blocked
until recovery; an explicit reset preserves any existing file as a uniquely named
recovery backup before replacing it. If backup or saving fails, the original remains
and the error stays available. Reset never deletes or changes Session definitions.

Plugin management is described below. Startup/tray behavior, arbitrary font
selection, notifications and the other proposed preferences remain future work.

### Plugins and Steam

The first plugin slice is bundled with Sessions, with Steam enabled by default.
**Settings → Plugins** shows each bundled plugin's name, version and enabled state,
plus its supported settings. **Apply plugins** saves separately from appearance.
Unapplied choices are discarded when Settings closes. **Reset plugin preferences**
immediately restores bundled defaults without changing appearance or saved Sessions.
Failed reads block plugin discovery/launch and Apply until retry or explicit recovery;
recovery preserves the original file before replacement. Reset is unavailable before
the first read finishes. Failed writes retain applied settings and draft choices.
Both Settings and its owner remain open while plugin preferences are being written.

Each Session captures its enabled plugins and settings at Start. Later changes apply
to subsequent launches and do not invalidate the active run. Missing, disabled or
incompatible plugins preserve saved entries and explain recovery instead of falling
back to an executable. All plugin entries are checked before audio changes or app
launches. Third-party plugin installation, hot loading, a marketplace and automatic
plugin updates are outside this slice. Hue and Home Assistant remain future work.

In **Add app → Plugins**, Steam lists locally installed apps from its available
libraries, with names and initial artwork fallbacks. Steam is found automatically;
its installation folder can be overridden in Settings. Refresh never launches games
or changes Steam configuration. Unavailable installations cannot be added; already
saved selections remain editable and recover when their library becomes available.
Game identity is its Steam app ID, so a move between discovered libraries does not
require recreating the Session entry. Partial discovery errors keep healthy choices.

Steam entries send a launch request to the installed client without changing its
saved launch options. Steam handles login, updates and launch-choice dialogs. A
successful request does not establish readiness or ownership. The app card separately
shows green **Running** when Steam's local running flag reports the target active,
including apps opened outside Sessions. This flag does not grant cleanup permission.
Manual launch acknowledgements use neutral supporting text and disappear once running
is detected, even after the repeated-click guard expires. Actual launch/focus errors
remain distinct and use error styling.
Individual requests are suppressed while Steam reports the app running and otherwise
have a ten-second repeated-click guard. Each plugin target can appear only once in a
Session; same-plugin entries serialize in All at once, and In order honors pauses.

Each plugin app has **Close when this Session ends** in its app options, off by
default. Supporting plugins may ask verified processes newly opened for the Session
to close. Steam's implementation captures new app-associated processes within the
installed app directory during a bounded launch window (up to 30 seconds). It leaves
already-running apps, pre-existing processes, other Steam apps and the shared client
alone. Missing or ambiguous ownership falls back to manual closing. Capture drains
even if End is requested during startup. Later independent launches are never adopted.
Changing the checkbox affects future runs, not an active run's captured choice.

Normal End asks tracked apps to close and retains the existing save-work confirmation,
Needs attention, Retry End and Leave apps open flows. If a captured app refuses to
close, the existing separately confirmed Force quit action targets only its retained
processes. Automatic force quit is not exposed for plugin apps. Individual app
launches remain independent and never acquire Session cleanup ownership. Window
readiness, completion focus, administrator launch and main-app lifetime remain
unavailable for plugin apps. Owned ordinary-app cleanup and audio restoration are
unchanged. Steam/log failures and delayed starts outside the capture window can still
require manual management; a running indicator is not evidence that cleanup is available.

Saving in this source build writes library v5. Formats v1–v4 still load without
rewriting; published 0.4.0 and earlier cannot read v5. Preserve a library copy before
saving if rollback to a published build is needed. Plugin preferences stay separately
in `%LOCALAPPDATA%\Sessions\plugins.json`, outside shareable Session definitions.
See [plugin implementation and validation](PLUGINS.md).

### Session audio devices

The editor's collapsed **Session audio** section independently selects a sound output
and microphone input, each defaulting to **Leave unchanged**. Refresh lists active
Windows endpoints without changing audio. Saved selections use device identities and
friendly names; unavailable selections remain visible and saveable. Merely editing,
refreshing or saving never switches devices. Device changes count as unsaved edits.
The detail view summarizes configured audio choices. Sessions still requires at least
one app to offer Start; audio-only Sessions are not introduced in this slice.

Start captures the choices and applies Windows console, multimedia and communication
defaults for each selected direction before opening any app. This affects other apps
that follow Windows defaults; apps with their own explicit device choice may keep it.
This is not per-app routing, volume control, mixing or an audio-effects feature.
Validate both target devices and previous defaults before switching. Missing targets,
unrestorable previous defaults or a failed switch stop app startup with an explanation.
Defaults changed externally during preparation abort startup rather than replacing
that later choice. Partial changes and cancelled/failed startup attempt audio rollback;
opened apps still require the existing save-work confirmation before process cleanup.

On confirmed End, restore audio after asking apps to close, even if some are waiting
for a save prompt. Finish and leave apps open, and Leave apps open and close, restore
audio without stopping apps. For each changed direction/role, restore only if the
current default still equals what this run set; otherwise keep the later selection.
Windows can link console and multimedia defaults: if either ordinary role has a
later selection distinct from its original and applied devices, preserve both
ordinary roles for that direction. Communication roles remain independent.
Already-matching defaults are not owned changes. If restoration fails, retain the
pending changes, show **Needs attention**, block another Session and keep the window
open for retry. Users can reconnect the previous device and retry, or choose their
own defaults in Windows and retry. No automatic loop repeatedly changes audio.

Restoration state is in memory: a crash, forced termination or machine shutdown
cannot restore devices, and restarting Sessions does not guess prior defaults.
Comparison protects defaults that differ at cleanup, not an undetectable change away
and back to the same device; Windows offers no atomic compare-and-set here.

Audio choices were introduced in library v4. Formats v1–v3 load without rewriting
and leave audio unchanged; published 0.2.3 and earlier cannot read v4. Current source
saves use v5 for plugin compatibility, as described above.

The editor's options headings follow the current draft: **[App name] options** for
the selected app and **[Session name] advanced startup options** for the Session.
They update when selection or names change, with **App options** and **Advanced
startup options** as the respective blank-name fallbacks. Long headings wrap.
The advanced startup section is collapsed by default. **In order** opens each app,
checks its startup condition, then applies a pause before the next app. The Session
pause is 0–300 whole seconds, default 0, and applies only between apps. In **App
options**, a per-app override replaces that pause, including an explicit zero; an
override on the last app adds a final settling pause before startup completes.
Already-running apps follow the same conditions and pauses without becoming owned.
**All at once** overlaps launches for independent executable paths, waits for every
configured startup condition, and ignores all pauses while retaining their saved
values. Repeated entries for the same executable remain serialized. A repeated
entry after an untracked launch fails instead of risking another ambiguous launch.

Each app offers **Launch request completed** (default), **Process is running**, or
**A window appears**. The latter two require a live retained process; window readiness
requires an eligible visible window belonging to one of those tracked processes.
These conditions do not prove loading, sign-in, or a network connection has finished.
An untracked launch cannot satisfy a verified readiness condition. An exited process,
unverifiable readiness, or a timeout stops startup. Readiness timeout is 1–600 whole
seconds (default 30), starting after Windows returns the launch/acquisition result;
it does not time out Windows launch dialogs or UAC. Pending waits show their condition
and remaining seconds. End cancels waits/pauses promptly, while retaining in-flight
launches for the existing confirmed cleanup flow.

**When startup finishes** defaults to leaving focus unchanged. Users may request
Sessions or a chosen configured app be brought forward once after successful startup.
The chosen target is independent of the main-app lifetime choice. Failure, cancellation,
an ended/replaced run, or editing/a confirmation prevents the completion focus action.
A pending focus request is cancelled if the run ends or editing/a confirmation begins.
Changing sidebar selection does not change its target. Missing windows or Windows
declining foreground activation produce feedback without failing the Session. Manual
single-app launches do not apply Session timing or completion focus settings.

The app reads version-1 and version-2 libraries with automatic force quit disabled
for every app. Existing startup settings are preserved. Version 3 introduced the
compatibility boundary so older builds reject the library instead of silently
applying their force-quit policy; loading alone does not rewrite the library.
Published 0.2.1 and earlier builds cannot read version-3 files. Current saves use v5.

For a user-bound Session, End is explicit. For a tracked main app, closing its captured process (all captured instances if already open in several processes) opens an end-confirmation request. Other apps keep running until the user confirms. Cancelling keeps the Session active and switches this run to manual ending, without repeatedly prompting about the already-exited main app. A supporting app closing does not end the run. Empty Sessions can be saved and edited, but cannot be started from the app. Their detail view hides Start Session and startup hints, keeps Edit Session available, and asks the user to add apps. The start command also rejects empty Sessions. A verified self-restart into one child with the same executable path stays part of the run, including an elevation restart. Exiting the original process does not end a main-app Session while that replacement remains alive. General launcher handoffs, ambiguous multiple children, and inaccessible identities still require manual management; Sessions never adopts a new app just because its name/path matches.

End Session first shows a confirmation naming the active Session and listing its owned apps that will be asked to close, even when another Session is selected. It reminds the user to save work and explains that apps still running after a normal close wait for attention. Apps explicitly configured for automatic force quit are named with an unsaved-changes warning, including apps still waiting/opening. **Cancel** receives initial focus; Enter on Cancel and Escape dismiss the prompt without stopping apps. **Stop apps and end Session** authorizes cleanup. Any app still being launched for this run is included in that cleanup, which the dialog also explains. No shutdown operations happen merely by opening the prompt.

After confirmation, End requests a normal close of only processes this run opened and owns, in reverse order, targeting application windows (including eligible hidden main windows) while leaving internal helper windows alone. It waits up to three seconds per process, then preserves any app still running by default. Applications handle their own save prompts; Sessions does not infer unsaved work from file handles, temporary files, app names or dialog detection. A disappearing window or tray icon does not count as process exit. Already-open and untracked apps stay untouched, and process trees are never terminated wholesale.

An app's options include **Force quit if this app stays open**, off by default for both new and existing apps. Enabling it explicitly permits termination of that verified owned app after the normal-close grace period, with a warning that unsaved changes may be lost. The same policy applies to manual End, confirmed main-exit cleanup, startup failure cleanup, late startup acquisitions and End Session and close, including elevated cleanup.

Apps still open keep the run in **Apps still open**, blocking another Start. Named recovery rows remain available even while browsing another Session: **Bring forward** requests foreground activation, and **Force quit…** opens a separate confirmation for that app in that run. Cancel is the safe keyboard default and Escape dismisses it. Only explicit confirmation authorizes targeted force quit; other apps are not retried or forced. A stale confirmation is dismissed if its app closes or the run changes. **Retry End Session** requests normal cleanup again using each app's configured permission; **Finish and leave apps open** releases remaining ownership without closing apps. Sessions continues observing pending apps and finishes automatically when they exit after saving/discarding. Cancelling an app's save prompt leaves it open without automatically issuing another close request.

If Windows denies access to an owned elevated app, Sessions requests Windows administrator approval for a separate cleanup helper limited to that exact process and the same normal/force permission. The main Sessions window remains unelevated. Cancelling approval, failed verification, or an app still running leaves the Session needing attention. Run as administrator remains a saved per-app launch option. Old saved forceClose flags are ignored and cannot enable automatic force quit; opening an old library does not rewrite it, and the obsolete field is omitted on its next normal save.

A startup failure stops further launches and cancels readiness waits/pauses. In-flight concurrent acquisitions still finish and are retained before cleanup is offered. If owned apps were opened, their cleanup waits for the same save-work confirmation; Cancel leaves those apps running with a retryable attention state. If no owned apps need cleanup, startup can fail without a prompt after in-flight acquisitions settle. Confirmed End during startup prevents further launches, waits for all acquisitions already in flight, and then cleans up owned apps in reverse configured order. Repeated Start/End/Confirm clicks cannot create overlapping runs or duplicate cleanup. An automatic prompt waits until an unrelated editor/save or another confirmation is resolved. A minimized window may retain a pending prompt until the user returns; it must not silently stop apps.

Closing Sessions during an active run offers **Keep Sessions open**, **End Session and close**, or **Leave apps open and close**. This close dialog explains normal closing and names any apps with automatic force quit enabled, so End Session and close is the confirmation rather than another intervening prompt. End-and-close keeps the window open when any app remains running. A later app exit finishes the run but does not unexpectedly close Sessions after returning to recovery. Leaving apps open is disabled while starting/stopping. Resolve any changed draft before choosing how to close an active run. Forced process termination cannot show a prompt or perform cleanup; independent apps remain open, and a later run treats surviving apps as pre-existing.

Only one Sessions application instance may manage a given local profile. Starting a second instance requests activation of the first and exits before loading/saving the library. This prevents competing Session runs and stale library overwrites between application instances.

While a Session is running, the UI indicates:

- What started successfully.
- What failed.
- What is currently running.
- Which applications Sessions owns.
- What will be cleaned up when the Session ends.

Correct Session execution semantics take priority over visual polish. The initial UI may be simple.

## Local-first and open-source philosophy

The core application must require no account. Local-first is the default, and the product should avoid unnecessary dependence on commercial services.

Sessions is licensed under GNU GPL version 3 only (`GPL-3.0-only`), selected by the user on 2026-09-08. The repository's [LICENSE](../LICENSE) contains the full terms; third-party dependencies and assets retain their own licenses. Distributed builds must meet the license's corresponding-source requirements. The project does not include an “or any later version” grant.

Prefer understandable code, documented behaviour, portable configurations, minimal proprietary dependencies, and clear extension points.

Sessions should save configuration locally. Configuration should remain readable and portable so Sessions can eventually be exported, imported, shared, and stored in source control. These future uses do not require cloud sync or an online community.

## First usable milestone

The first milestone is a small but usable vertical slice. The user should be able to:

1. Launch Sessions.
2. See a list of Sessions.
3. Create a Session.
4. Give it a name.
5. Add ordered Start Process actions.
6. Select or configure a main process.
7. Start the Session.
8. Track which processes Sessions started.
9. Detect when the main process exits.
10. Stop only processes owned by that Session.
11. Save Sessions locally.
12. Load them again after restarting the application.

The broader distinction between process-bound and user-bound Sessions remains part of the product model. This milestone list does not make a main executable mandatory for every Session.

## Future possibilities

The examples above describe possible environment preparation, runtime reactions, and settings restoration beyond the first milestone. Additional actions may support commands, websites, audio devices, power plans, OBS, and other tools. The existing candidate action types and configuration options are recorded in [ARCHITECTURE.md](ARCHITECTURE.md#possible-future-extensions); they should be implemented only when needed.

## Explicitly out of scope for the first milestone

Do not implement these unless specifically requested:

- User accounts.
- Cloud sync.
- Plugin marketplace.
- Telemetry.
- Automatic updates.
- Mobile applications.
- Web application.
- Remote control.
- Scripting language.
- Online community.
- Complex plugin architecture.
- macOS implementation.
- Linux implementation.
- Database server.
- Authentication.
- Licensing system.

These may be considered later but must not distract from getting the basic Session model working correctly.
