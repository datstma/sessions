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

The current application implements this navigation, local creation/editing, executable-file selection, app removal and reordering, and both lifetime configurations. It follows the system light/dark theme. Sessions are saved on this computer and loaded at startup. Save failures retain the draft; load failures show a retry action and prevent replacing an unreadable library. Start Session opens configured apps using the saved startup mode and changes to an active run with End Session available. Sample Sessions are used only in tests, not inserted into the user's library.

The visual treatment follows the supplied [branding guide](../branding/DESIGN_GUIDE.md):
Manrope typography, rounded Session/app cards, indigo primary actions, green running
states and red destructive actions. Both OS-selected themes use readable text/fill
variants. The sidebar includes the Sessions mark and local-storage reassurance;
the selected Session has a stronger header and individual app cards. End confirmation
separates apps this run owns from already-open apps that stay open. The mockups guide
appearance; history, Recently added, timers, shortcut chips and a theme toggle are
not part of this refresh. Existing advanced settings and recovery flows remain.

The main window supports a 640×480 logical-pixel minimum and uses a narrower sidebar
and smaller content margins below 900 logical pixels. Main and picker windows limit
their initial size to the display's working area, within their minimum sizes. Content
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

Session details show the current presence of each configured executable. A green **Running** button brings an existing window forward when clicked (or activated with the keyboard), restoring it if minimized. **Running · no window** is a passive green status for background apps; **Not running** is a neutral button with a play icon that opens that app. Status is checked when viewing a Session and about every two seconds while the window is visible and not minimized. Checks pause during editing/deletion confirmation and resume automatically. A failed check shows **Status unavailable**, rather than incorrectly claiming the app has stopped.

Presence matches the full executable path, not merely the app name. Apps with several windows use the first eligible matching window found on a fresh check; this does not select a specific document or browser profile. If an app closes or Windows declines focus, an inline message explains what happened or suggests selecting the app from the taskbar. Status checks never activate windows; manual window activation requires an explicit click. The separate optional completion-focus action is described below. Presence and window activation do not launch, adopt, stop, or save process state and do not mark a Session active. Tray-only apps without a visible window cannot be brought forward by this control.

Clicking **Not running** launches only that saved app, using its configured arguments and working directory (or its executable folder when no working directory is configured). The control shows **Starting…** and prevents repeated clicks while launch/presence catches up. A fresh check avoids launching an app that has already opened; if it now has a window, the click focuses it instead. Uncertain presence does not permit a new launch. Missing executables, invalid working folders, and Windows launch errors appear beside the app, with retry when its status can be checked.

A successful launch request does not prove the app stayed running. Presence checks determine the green status. Requests for the same executable are serialized within this Sessions instance, with a ten-second startup grace period to avoid duplicates during a delayed startup/handoff. If the app has not appeared after that period, the UI allows retry and explains that it has not been observed running. A manual launch neither starts a Session nor changes saved configuration: individually opened apps remain independent, stay open when Sessions closes, and must be treated as pre-existing by any later Session run. Individual launches are disabled during an active Session; apps already opened individually remain pre-existing for a later run. Executable launches use Windows desktop Open activation so they do not inherit Sessions’ console/output pipes. App options includes **Run as administrator**, off by default. When enabled, Windows requests approval before launching the app directly with elevation; this can avoid an app restarting itself to elevate. Windows can also request consent when an executable itself requires elevation. Sessions itself stays unelevated. Packaged-app activation remains unsupported.

Session details also offer **Delete Session…**. A confirmation names the Session, explains that its saved setup will be removed permanently, and makes clear that installed apps, files, and running processes are unaffected. Cancel receives initial focus; Enter on Cancel or Escape dismisses the confirmation. Deletion is unavailable while editing or saving, and the rest of the window is disabled during confirmation. Only an explicit Delete Session confirmation saves the reduced library. The displayed Session is removed only after that save succeeds; failure preserves it and offers retry or cancellation. Selection moves to the next Session, or the previous one when deleting the last item; deleting the only Session returns to the first-run screen. There is no undo after successful confirmation.

Start Session captures the saved setup, uses its configured launch mode (in order by default), and leaves matching already-open apps running without taking ownership. Only one Session can be active at a time. Its sidebar marker and a persistent status panel remain visible while browsing another Session; switching selection never starts/ends anything. Editing/deleting the active definition and individual launch controls are unavailable until the run ends. Other saved Sessions can still be edited. Starting waits for an individual launch already in progress to settle.

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

The updated app reads version-1 libraries with the old startup defaults. Saving writes
version 2 so older builds reject startup settings they cannot honor; loading alone
does not rewrite the library. The published 0.1.0 preview cannot read version-2 files.

For a user-bound Session, End is explicit. For a tracked main app, closing its captured process (all captured instances if already open in several processes) opens an end-confirmation request. Other apps keep running until the user confirms. Cancelling keeps the Session active and switches this run to manual ending, without repeatedly prompting about the already-exited main app. A supporting app closing does not end the run. Empty Sessions can be saved and edited, but cannot be started from the app. Their detail view hides Start Session and startup hints, keeps Edit Session available, and asks the user to add apps. The start command also rejects empty Sessions. A verified self-restart into one child with the same executable path stays part of the run, including an elevation restart. Exiting the original process does not end a main-app Session while that replacement remains alive. General launcher handoffs, ambiguous multiple children, and inaccessible identities still require manual management; Sessions never adopts a new app just because its name/path matches.

End Session first shows a confirmation naming the active Session and listing its owned apps that may be stopped, even when another Session is selected. It reminds the user to save work and explains that apps still running after a normal close will be force quit and may lose unsaved changes. **Cancel** receives initial focus; Enter on Cancel and Escape dismiss the prompt without stopping apps. **Stop apps and end Session** explicitly authorizes cleanup. Any app still being launched for this run is included in that cleanup, which the dialog also explains. No shutdown operations happen merely by opening the prompt.

After confirmation, End requests a normal close of only processes this run opened and owns, in reverse order, targeting application windows (including eligible hidden main windows) while leaving internal helper windows alone. It waits up to three seconds per process, then terminates a verified owned process that is still running. This is the standard stopping behavior for every app; there is no per-app force-quit switch or stronger termination mode. A disappearing window or tray icon does not count as process exit. Already-open and untracked apps stay untouched, and process trees are never terminated wholesale.

If Windows denies access to an owned elevated app, Sessions requests Windows administrator approval for a separate cleanup helper limited to that exact process. The main Sessions window remains unelevated. Cancelling approval, failed verification, or an app still running leaves the Session needing attention with **Retry End Session** (which shows the save-work confirmation again) and **Finish and leave apps open**. Run as administrator remains a saved per-app launch option. Old saved forceClose flags are ignored; opening an old library does not rewrite it, and the obsolete field is omitted on its next normal save. New and existing Session definitions use the same confirmed stopping behavior after restarting the updated app.

A startup failure stops further launches and cancels readiness waits/pauses. In-flight concurrent acquisitions still finish and are retained before cleanup is offered. If owned apps were opened, their cleanup waits for the same save-work confirmation; Cancel leaves those apps running with a retryable attention state. If no owned apps need cleanup, startup can fail without a prompt after in-flight acquisitions settle. Confirmed End during startup prevents further launches, waits for all acquisitions already in flight, and then cleans up owned apps in reverse configured order. Repeated Start/End/Confirm clicks cannot create overlapping runs or duplicate cleanup. An automatic prompt waits until an unrelated editor/save or another confirmation is resolved. A minimized window may retain a pending prompt until the user returns; it must not silently stop apps.

Closing Sessions during an active run offers **Keep Sessions open**, **End Session and close**, or **Leave apps open and close**. This close dialog includes the same save-work and force-quit warning, so End Session and close is the confirmation rather than another intervening prompt. End-and-close keeps the window open when cleanup fails. Leaving apps open is disabled while starting/stopping. Save or cancel edits before closing an active run. Forced process termination cannot show a prompt or perform cleanup; independent apps remain open, and a later run treats surviving apps as pre-existing.

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
