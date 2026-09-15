# Sessions — Intended Architecture

This document is the source of truth for architectural decisions and implementation direction. [PRODUCT.md](PRODUCT.md) defines product behaviour, UX, examples, and milestone scope. [AGENTS.md](../AGENTS.md) provides operational guidance and validation commands.

[DEVELOPMENT_NOTES.md](DEVELOPMENT_NOTES.md) preserves implementation findings and verification limits. [BACKLOG.md](BACKLOG.md) tracks remaining work and unresolved decisions.

## Implementation status

The repository contains a .NET 10 Avalonia desktop application, Core definitions/JSON persistence and Session execution, Core behaviour tests, headless App interaction/rendering tests, and opt-in native regression helpers.

Implemented: a Session sidebar and detail view, a first-run empty state, creation/editing of named Sessions, ordered Start Process configuration, optional main-app selection, and local saving/loading. `SessionDefinition` and `StartProcessAction` live in Core. `ISessionStore` is implemented by `JsonSessionStore`; App composition supplies the path `%LOCALAPPDATA%/Sessions/sessions.json`. App ViewModels contain presentation and draft-editing state only. The Avalonia storage picker stays in the View layer.

Start/End Session are implemented through `SessionRunner` in Core and `WindowsSessionProcessHost` in App/Services. The UI presents real run state, ownership outcomes, cleanup recovery and close-window choices. Individual app launch/presence remain separate manual desktop actions. A single-instance guard is acquired before loading the library. The sections below distinguish implemented behaviour from first-milestone design direction and possible extensions.

## Decisions already made

### Project licensing

Sessions uses `GPL-3.0-only`; see [LICENSE](../LICENSE). App and Core declare the SPDX expression in project metadata. The App project copies the full license into build and publish output. Third-party license notices and corresponding source for each distributed build must be addressed during release preparation (SESS-020); a license file alone does not complete binary-release preparation.

### Release packaging

Windows releases use a self-contained `win-x64` publish and a separate WiX MSI
project under `installer/`. `Directory.Build.props` supplies one numeric application
and MSI version; `global.json` pins the build SDK. Packaging stays outside
`Sessions.slnx` so normal application builds do not require installer tooling.
The local script and manually dispatched GitHub workflow share the same build path.
The workflow builds an existing matching version tag and creates an unpublished
draft containing an MSI, matching Sessions and WiX source archives, and checksums. There is no
automatic publication or in-app updater.

Authored installation scope is per-user, with binaries in
`%LOCALAPPDATA%\Programs\Sessions`, a Start menu shortcut, and a stable MSI upgrade
identity. The library in `%LOCALAPPDATA%\Sessions` is outside installer ownership
and must survive upgrades and uninstall. Running `Sessions.App.exe` blocks installer
changes through a detection-only action; installer-driven close/termination and
Restart Manager shutdown are disabled to preserve the application's confirmation
and ownership behavior. Generated payload components use stable relative-path IDs
and HKCU key paths for per-user MSI validation. File components have explicit
deterministic GUIDs derived from a stable product/platform/scope/path namespace;
WiX cannot automatically generate GUIDs for registry-keyed components containing
files. Each packaging run uses independent intermediate outputs. ICE91 is excluded
because it warns about user folders in per-machine packages; this MSI rejects
`ALLUSERS`. All other ICE validation remains enabled.

The user authorized WiX 7 EULA acceptance on 2026-09-09, recorded by the project's
`AcceptEula=wix7` property. MSI compilation and the configured ICE checks pass.
The tagged 0.1.0 GitHub workflow also passes full solution build, regular tests,
MSI validation, source/checksum generation, and draft creation. Installed-release
verification limits and Actions runtime maintenance are tracked in SESS-021/022.
See [RELEASING.md](RELEASING.md) for operational instructions
and [BACKLOG.md](BACKLOG.md#sess-020--prepare-the-public-github-project-and-downloadable-releases)
for release history and follow-ups.

### Technology and platform

- C# and .NET 10.
- Avalonia for the desktop UI.
- CommunityToolkit.Mvvm for MVVM support.
- xUnit for tests.
- Simple local JSON persistence initially.

The application is Windows-first. Avoid unnecessary Windows-specific dependencies in Core and isolate platform-specific behaviour enough to permit other platforms later. Do not prematurely implement macOS or Linux support.

### Project responsibilities

| Project | Responsibility |
| --- | --- |
| `src/Sessions.App` | Avalonia Views, ViewModels, navigation, user interaction, application composition, and presentation of Session state. |
| `src/Sessions.Core` | Platform-neutral Session and action models, execution engine, action ordering, runtime state, process ownership tracking, error-handling policies, and persistence abstractions as these are implemented. |
| `tests/Sessions.Core.Tests` | Meaningful Core behaviour tests covering execution semantics, process ownership, action ordering, state transitions, cleanup, and failure policies. |
| `tests/Sessions.App.Tests` | Headless Avalonia interaction and rendering tests covering creation, selection, draft cancellation, app ordering/main-app selection, and recoverable persistence errors. |

`Sessions.Core` must not depend on Avalonia. Domain and execution logic belongs outside Views and ViewModels. The UI presents execution state and mediates user interaction; it does not own execution semantics.

Tests should exercise meaningful behaviour rather than trivial properties.

### Architectural principles

- Prefer simple, readable solutions over elaborate architecture.
- Introduce abstractions when they solve a real application boundary, not solely for hypothetical future uses.
- Use async APIs for work that may block.
- Use dependency injection where it improves separation and testing, without excessive service or interface proliferation.
- Keep UI logic separate from Session execution logic.
- Keep extension points understandable and avoid unnecessary commercial-service dependencies, consistent with the [product philosophy](PRODUCT.md#local-first-and-open-source-philosophy).

## Current first-milestone design

This section describes first-milestone behaviour and boundaries, explicitly identifying the configuration and persistence pieces already implemented. Session runtime execution and individual app launch are implemented with the conservative boundaries described below. The authoritative acceptance scope is the [first usable milestone](PRODUCT.md#first-usable-milestone).

### Session domain model

The central domain object is a `Session`. Its conceptual structure follows the product's [Before / Main / While Running / After model](PRODUCT.md#what-a-session-contains): ordered preparation actions, an optional main activity, runtime observation and state, and cleanup actions.

The implemented saved model is `SessionDefinition`: identity, name, description, an ordered list of `StartProcessAction` values, and an optional `MainAppId` referencing an app in that list. Each app has an identity, display name, executable path, arguments, working directory, and an optional RunAsAdministrator flag defaulting to false. AllowForceQuit defaults to false and permits automatic force quit only when explicitly enabled. Legacy forceClose fields are ignored, and v1/v2 libraries always load with AllowForceQuit false; loading never rewrites them. Library v3 introduced this policy boundary so older builds reject it instead of silently forcing every app; current source writes v5, described under Persistence below. Core validation rejects missing required values, duplicate app identities, and dangling main-app references. Optional apps are handled in `SessionRunner`: plugin preflight skips an optional app whose plugin cannot be resolved; an optional entry's launch or readiness exception is caught without setting the run's failure flag or cancelling other launches. It becomes `SessionAppState.Skipped` when its launch produced no live tracked process (the monitor then ignores it, and a skipped main app sets the manual-end flag), otherwise keeps its tracked or untracked state with an explanation and remains owned for cleanup. Cancellation during End is never treated as a skip. The startup-complete message lists optional apps with problems; completion focus is not requested for a skipped target. `SessionDefinition.Duplicate` creates an independent copy with new Session and app identities, remaps main-app and completion-focus references to the copied apps and clones plugin settings; the editor opens it as a new draft, and saving inserts it after the original. A null main-app identity means a user-bound Session, including a Session with no configured apps. Runtime snapshots and retained process references live in the runner and are never persisted in saved definitions.

The editor copies a definition into a draft and replaces the saved definition only after persistence succeeds. App reordering preserves identities, so moving the main app does not change the lifetime setting. Removing the main app requires an explicit replacement or switching back to user-bound lifetime before saving. Executable and working-directory validation occurs in the Windows adapter when an app must be launched.

Editor field errors are presentation state. `SessionEditorViewModel` derives its
first `EditorValidationIssue` and `CanSave` from the same field checks, including
nested app errors and retained numeric values. Each issue identifies a field and,
when applicable, the draft app instance; reordering updates its displayed position.
The view routes Review fields to that control, opens its section, and scrolls both
the input and nearby explanation into view. Accessible help accompanies each field;
the summary remains a polite live notification. No filesystem inspection, execution,
configuration schema change or additional persistence occurs during validation.

`SessionEditorViewModel.HasChanges` compares the current raw editable values with
immutable initial Session/app snapshots, including ordered app identities and
nullable numeric inputs. It does not call `BuildDefinition`, which requires valid
inputs and normalizes text. Selection, focus, expansion and computed display state
are excluded; restoring editable values clears the comparison. The snapshot is
presentation state and is never persisted.

`MainViewModel.RequestWindowClose` checks pending edits and writes before allowing
exit, even with no active run. A changed draft opens a modal with Keep editing as
the safe default, Discard, and Save. Modal Save and ordinary Save share a guarded
`SaveEditorAsync` path. Closing during a save records one continuation; only a
successful write may proceed to closing, while errors retain the editor and clear
the continuation. Modal failures remain visible for retry or Keep editing/Discard;
normal-save failures return to the editor. Choices are disabled during a write.

After draft resolution, an active run receives its separate close confirmation.
This dialog is established before removing the editor/draft modal so a pending
automatic End cannot interleave or implicitly authorize process cleanup. Views only
route the window Closing event and manage focus: Keep editing/Escape restores the
previous editor control, and a failed modal save restores the safe default. This
adds no Core runtime policy, disk autosave, crash recovery or library schema change.

### App discovery

`IAppSource`, `DiscoveredApp`, `WindowsRunningAppSource`, and `WindowsStartMenuAppSource` live in `Sessions.App/Services`, outside the platform-neutral Core. `MainWindow` supplies both sources to the picker and permits injection for tests. Discovery is a configuration aid, separate from the Core execution/ownership boundary.

The running-app implementation enumerates visible, titled top-level windows, excluding tool/cloaked windows and Sessions itself. It checks title length without reading title content. It queries executable paths with `PROCESS_QUERY_LIMITED_INFORMATION`, checks package identity, and reads friendly names from executable version information. Multiple windows/processes are grouped by executable path. Apps that exit during inspection are skipped; unreadable paths or unsupported activation methods are not selectable. Windows-packaged apps and `ApplicationFrameHost` are conservatively unsupported by this `.exe`-only configuration model. No commands are run and no processes are adopted or terminated.

The Start menu adapter scans `SpecialFolder.Programs` and `CommonPrograms` on a background STA thread, skipping reparse-point children. `WindowsShellShortcut` uses `IPersistFile.Load` in read mode and `IShellLinkW` to read `.lnk` target, arguments, and working directory; `IShellLinkDataList` supplies the administrator flag. Environment variables in target/working paths are expanded; arguments are retained verbatim. It does not call Resolve, Save, or activate a shortcut. See Microsoft's [IShellLinkW contract](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishelllinkw). Invalid/missing/non-executable targets, unavailable working folders, unreadable entries, and `.url` links produce disabled choices. Identical shortcuts across roots merge; distinct launch settings remain choices. Names come from shortcut filenames; icons come from target executables. Virtual AppsFolder/packaged activation and shortcut show-window preferences are not implemented.

Enumeration, file metadata, and icon extraction run off the UI thread. `System.Drawing.Common` is used only by the Windows adapters to extract executable icons into PNG bytes; Avalonia displays those bytes, falling back to an initial when an icon is unavailable. The picker disposes its bitmaps and cancels pending discovery on close. `AppPickerViewModel` handles source switching, search, multi-selection, refresh, and browse-result merging. Both sources load independently; a failure preserves that source's previous snapshot and allows the other source to remain usable. `AppPickerWindow` owns the modal and native file picker; only explicit Add returns choices to the editor. Cancel returns no choices. Checked selections survive filtering and source switches; refresh retains choices still present and separately browsed entries. Choice identity includes source, executable path, arguments, working directory, and elevation; selection permits only one choice per executable across sources.

The editor suppresses picker duplicates by normalized, case-insensitive executable path and appends fresh action identities, preserving existing arguments, working directories, main-app choice, and action order. It copies the discovered name/path plus shortcut arguments, working directory, and administrator flag when supplied; running-app discovery supplies no such launch metadata. These fit the existing `StartProcessAction` schema. No process identity, runtime ownership, icon, window title, inspected process command line, or open-document information is persisted. Runtime launch validation remains necessary even after successful discovery.

### Saved-app icons

`AppIcon` is a presentation-only control in the Session detail list. `IAppIconSource`
keeps executable inspection behind an App/Services boundary. `WindowsAppIconSource`
reuses the picker's Windows icon extraction on a worker task and caches PNG bytes
by normalized, case-insensitive executable path, sharing pending requests. The cache
is limited to 128 entries, includes unavailable results, and lasts for this app
process; restarting Sessions refreshes cached artwork after an app update. Core and
JSON contain no artwork. No app is activated or launched to obtain an icon.

Each attached control decodes and owns its Avalonia bitmap, retaining the name
initial until loading succeeds. A path-generation check rejects late results after
rebinding or detachment; detachment/path replacement clears and disposes the bitmap.
Missing executables, unavailable extraction and invalid PNG data retain the fallback
without affecting app status, launch commands or configuration. Icons keep their
original colors within the existing theme-aware app tile and do not replace app names.

### App presence and window activation

`IAppPresenceService` and `WindowsAppPresenceService` provide passive desktop observation and explicit window activation in App/Services. App composition injects the Windows adapter into `MainViewModel`; `SessionAppRow` holds transient presence and focus feedback. This presentation feature is independent of the Core execution lifecycle and does not establish process ownership.

A window-owned two-second dispatcher timer schedules asynchronous checks of the selected Session. Selection, return from editing, and window activation request fresh checks. Checks pause while minimized, editing, or confirming deletion. The ViewModel prevents overlapping scans and rejects results from an earlier selection/edit generation; closing the window cancels discovery. No icons, titles, command lines, arguments, working directories, or process/window identifiers are added to saved configuration.

The adapter runs native inspection off the UI thread. It restricts observations to the current Windows login session, narrows processes by executable filename, then requires a normalized, case-insensitive full-path match using limited process-query access. An inaccessible same-name process produces Unknown unless a matching process was found. A matching process without an eligible window is Background, and one with a window is Window. Multiple matches prefer the windowed state. `WindowsAppWindows` shares visible/titled, non-tool, non-cloaked window enumeration with the running-app picker, excluding Sessions itself.

Each focus request resolves a fresh matching window and rechecks its process identity/path before activation; it never trusts identifiers from a prior poll. A minimized window receives `ShowWindowAsync(SW_RESTORE)` and a brief bounded asynchronous wait, followed by `SetForegroundWindow`. Foreground restrictions are respected: failure is returned to the row for inline feedback, with no forced input, elevation, or process launch. See Microsoft's [SetForegroundWindow contract](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow) and [ShowWindowAsync contract](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindowasync). Background/tray-only apps and per-document/profile activation require different mechanisms and are not implemented here.

### Individual app launching

`IIndividualAppLauncher` is the presentation boundary for a manual app launch. `IndividualAppLauncher` performs a fresh presence check, serializes requests, and suppresses repeat launches of a normalized executable path for a ten-second startup grace period. It uses the shared presence adapter and an `IProcessStarter` boundary. Both are injected at App composition and shared across all Session rows in the window. These manual desktop actions live in App/Services and do not implement the Core Session engine, ordered actions, main-app lifetime, or cleanup.

`WindowsProcessStarter` validates an absolute `.exe` path and existing working directory off the UI thread, then calls `Process.Start` with `UseShellExecute = true` and `Verb = "open"`. Windows desktop activation avoids coupling GUI apps to Sessions/Rider's standard handles. The earlier direct CreateProcess path inherited output pipes; a native probe reproduced error 232 when those pipes lost their reader after parent exit. Arguments are passed as saved and blank working directories default to the executable's folder. No command interpreter is used. An explicit per-app RunAsAdministrator flag selects the `runas` verb instead; otherwise Windows can still prompt for consent if the executable manifest requires elevation. Successful shell activation may return no process handle, so null is not treated as failure. OS errors throw; actual running state comes from presence checks. Disposing a returned process handle releases resources without terminating the app. See the .NET [UseShellExecute documentation](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.useshellexecute?view=net-10.0) and Windows [shell activation contract](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-shellexecuteinfow).

`SessionAppRow` presents pending launch state and inline errors. It shows Starting until presence is observed or the grace period expires, never treating `Process.Start` success as continuing runtime presence. On a stale Not running click, an already-windowed app is focused; a background app is reported without a second launch. Failed/unknown presence blocks launching. Duplicate suppression is local to the current app instance, not a cross-process atomic check against every external launch.

Manual launches create no Session run or ownership records. Apps opened this way remain independent when editing/deleting a definition or closing Sessions. A later Session run regards them as pre-existing. Individual launch controls are disabled throughout the library while any Session is active/starting/stopping or awaiting cleanup; focus remains available. Starting a Session waits for an in-flight individual app launch to settle. No cleanup or lifetime semantics may be inferred from this manual launcher. The opt-in native launch test runs Windows Script Host in background mode with a temporary receipt script, verifies arguments/working directory, and lets it exit naturally; ordinary tests use injected starters. `tests/Sessions.LaunchProbe` compiles the production launch adapter into a windowless helper without Avalonia. Two additional native tests launch it with IDE-like output pipes, close or terminate only that parent, then verify the child has no inherited output/error pipes and can continue working. The original adapter failed both tests with error 232; shell activation passes. This does not promise survival when an external tool explicitly terminates the entire process tree or a containing job.

### Individual app closing

`IIndividualAppCloser` prepares an `IPreparedAppClose` before the named-app
confirmation. Preparation retains a fixed set of identities without closing them;
cancellation disposes the handles. Core's `PreparedAppClose` is a single-use normal
close request over that snapshot, with a three-second timeout per process and
`allowForceQuit: false`. It attempts the remaining targets after a failure. The
ViewModel runs confirmed closing off the UI thread, reports refusals/errors inline
and refreshes the runner and presence afterward. `SessionAppRow` keeps close feedback
separate from launch acknowledgements and focus errors. Successful requests show a
neutral waiting message; refusals/errors retain error styling. A fresh NotRunning
observation clears only close feedback, even after delayed exit; unknown presence
cannot resolve it. Views only manage commands/focus.

`WindowsIndividualAppCloser` matches ordinary apps by normalized full executable
path and current Windows login session, retaining verified `WindowsProcessIdentity`
handles. It excludes Sessions itself. `WindowsTrackedApp` instances disable restart
adoption; apps opened later cannot enter the request. Explicit manual confirmation
allows existing apps to be targeted without assigning automatic Session ownership.
The runner keeps its existing supporting/main-app exit semantics; this action does
not call End or change saved cleanup preferences.

Plugin `PrepareCloseAsync` captures current preferences and delegates preparation,
independently of CloseOnEnd. Its default is unsupported. Steam first requires known
running state, then `WindowsSteamManualClose` reads at most the final 1 MiB of
`gameprocess_log.txt`. The parser reconciles app/PID additions and removals. Live
handles must match the current login, manifest installation directory and creation
time relative to the log event. Captured targets close in reverse addition order.
The shared client, paths outside that directory and later copies are excluded.
Missing, stale, truncated or unverifiable evidence refuses preparation; no fallback
scans and closes all processes in a directory. This manual path does not require the
pre-launch baseline used to establish automatic Session ownership.

### Accessibility and available space

The presentation theme is generated from `branding/tokens/tokens.json` by
`scripts/Generate-BrandTheme.py`; the checked-in `Styles/BrandTheme.axaml` and
`branding/tokens/Theme.axaml` match. `App.axaml` imports the resource dictionary
and retains `RequestedThemeVariant="Default"`. Dynamic colour resources follow
OS theme changes; `SessionStyles.axaml` supplies shared branded control states and
compact layout rules. Existing Session resource aliases keep view bindings simple.
The CSS export is a reference asset, not a desktop runtime dependency.

Manrope 500/700/800 static weights are bundled as Avalonia resources. The upstream
source, OFL notice and regeneration instructions live in `branding/fonts`; the
project copies the notice into build/publish output. The native vector BrandMark
adapts the supplied logo to theme colours; a generated multi-size ICO brands the
Windows executable and windows. The MSI reuses that executable as the icon source for its
Installed apps registration and Start menu shortcut. Manrope uses Segoe UI fallback;
the previous Inter package/registration and Avalonia template icon are removed.
Python/fontTools/Pillow are only regeneration tools,
not required by normal builds or application startup. No Core dependency changes.
The End dialog's two ownership lists are projections of existing run snapshots;
this visual presentation does not change acquisition or cleanup rules.

Accessibility metadata belongs to the presentation layer: item-container styles bind
Session names/counts/active state, editor app names/order, and main-app choice names.
Picker choices expose their unavailable/source explanation through HelpText. Text
errors, runtime updates, and the selected-app summary use Avalonia's
[polite live regions](https://docs.avaloniaui.net/api/avalonia/automation/automationproperties).
Periodic presence labels are not live regions, avoiding repeated announcements for
background scans. Native screen-reader delivery is not established by peer tests.

Views manage keyboard focus after presentation transitions. They defer focus until
bindings/layout settle, capture the close-prompt return target before disabling the
background, and ignore duplicate close-state notifications. App-list actions restore
focus if a command hides/disables the focused button. No execution logic moves to Views.
MainWindow switches a compact style below 900 logical pixels and allows 640×480;
both windows bound their initial size using the screen working area and scaling.
The main runtime/error region has a shared 200-pixel scroll limit so it cannot consume
the editor's whole viewport. Headless tests exercise keyboard navigation, automation
peers, large lists/long text, and render scaling; they do not simulate Windows UIA
clients, OS text-size preferences, or physical monitor transitions.

### Session deletion

Deletion is a presentation workflow in `MainViewModel`: capture the confirmed Session identity, save the remaining ordered definitions through `ISessionStore`, then remove the item from the observable library and select its neighbour. The confirmation uses the captured identity rather than looking up a potentially different selection when saving completes. Requests are guarded during editing and in-flight deletion; errors leave both the displayed library and pending confirmation available for retry/cancel. `MainWindow` handles focus, keyboard containment and restoration. No process APIs or executable-file deletion are involved, and the JSON schema is unchanged. Editing/deleting the active definition is blocked, including while starting, stopping, or awaiting cleanup. Other definitions can still be browsed and edited; persistence remains independent of the captured run definition.

### Action model and ordering

A Session consists of an ordered list of Actions. Core applies the configured startup mode; cleanup always uses reverse definition order, independent of concurrent completion order.

Start Process is the action required by the first milestone. The action system should be extensible without requiring the entire application to know every concrete action type. This does not require a complex plugin architecture or speculative action implementations.

### Lifecycle and main activity

`SessionRunner` owns one run at a time. Its explicit states are Starting, Running, AwaitingEndConfirmation, Stopping, NeedsAttention, Completed, and Failed; no current snapshot corresponds to idle. It publishes immutable snapshots and per-app outcomes through a Changed event. `MainViewModel` marshals notifications to the Avalonia UI thread; Views never execute or own processes.

Start captures a copy of the saved definition and app list, applies ordered or concurrent startup, and blocks overlapping runs. Each run has a fresh RunId and a StartupSucceeded flag separate from its Session definition identity. The App start command requires at least one configured app, and the detail view hides the start action for empty Sessions while keeping editing available. Empty definitions remain valid for saving; Core retains its empty-run semantics for direct callers. A user-bound run remains active until End. A one-second Core monitor checks retained process lifetimes independently of selected Session, editing, or window minimization. For a tracked main app, exit of all its captured processes transitions to AwaitingEndConfirmation without closing any supporting apps. DismissEndRequest returns to Running and suppresses repeat main-exit prompts for that run; explicit End remains available. Supporting-app exits only update that app's outcome. An untracked main launch clearly requires manual End instead of guessing a new identity.

SessionDefinition adds LaunchMode (InOrder/Together), PauseBetweenAppsSeconds,
FocusAfterStartup (Unchanged/Sessions/App), and FocusAppId. StartProcessAction adds
Readiness (LaunchCompleted/ProcessRunning/WindowAppeared), ReadinessTimeoutSeconds,
and nullable PauseAfterSeconds (null inherits the Session pause; zero explicitly
disables it). Core validates enum values, integer ranges, and focus-target references.
PRODUCT.md defines timing semantics and defaults. ViewModels retain hidden timing
values when modes change and validate numeric input before constructing definitions.

Together groups entries by normalized full executable path with case-insensitive
comparison. Entries within one group remain sequential; independent groups use
Task.WhenAll. An untracked preceding launch blocks a repeat at that path. Any failure
cancels pending launch work/readiness/delays, but successful acquisitions are stored
before inspecting cancellation. StartFinished is signalled only after every group
settles; confirmed End drains this barrier before reverse-definition-order cleanup.
Readiness never broadens process ownership: ITrackedProcess.HasWindow checks only a
retained live identity (including verified self-restarts). WindowsTrackedApp uses
eligible-window enumeration without reading window titles. Core polls on a worker
thread at up to four checks per second, with a monotonic timeout and cancellation.
ProcessRunning needs any retained live process; WindowAppeared also needs one such
process to expose an eligible window. No untracked-process adoption or input-idle
heuristic is used. The readiness timeout begins after acquisition, not around a
shell launch/UAC operation whose late result still needs ownership capture.

MainViewModel performs completion focus once from the captured definition after
StartAsync returns successfully for the same RunId. Editing/modals suppress it, and
run transitions/editing/modals cancel pending focus. IStartupFocusService is the
presentation/platform boundary: WindowStartupFocusService restores/activates Sessions
on the UI thread or delegates chosen-app focus to IAppPresenceService. Failed focus
only changes presentation feedback; it cannot affect execution or ownership.

Confirmed End during Starting cancels waits/pauses and pending launches, waits for every current acquisition to finish, then cleans up any process actually returned as owned. The Windows adapter checks cancellation before launching; after Windows creates a process it returns an owned/untracked result even if End was pressed, avoiding a cancellation leak. Concurrent/repeated End shares the in-flight cleanup task. New Start is blocked until cleanup completes or the user explicitly relinquishes remaining apps.

MainViewModel's End command only captures the active Session ID and opens a save-work confirmation. ConfirmEndSession checks that the same run is still active before calling SessionRunner.EndAsync; the latter is the platform-neutral execution entry point for an already-confirmed operation. Cancel performs no process operation. Automatic main-exit and startup-failure requests are represented in Core state, then shown by the UI when editing/saving/other modals permit. End-and-close requires the close dialog to be open and uses its explicit save-work warning as approval. No automatic runner path invokes destructive cleanup without a request already confirmed by the caller. Default Cancel focus, Escape, modal keyboard containment and named active-run selection are covered by headless interaction tests.

### Session audio lifecycle

`SessionDefinition` stores optional `AudioDeviceChoice` values for output and input,
containing endpoint identity and a display name. `IAudioDeviceService` is the Core
boundary for listing endpoints and reading/writing a default for a direction/role.
The Windows adapter uses documented Core Audio endpoint enumeration and property
stores; endpoint IDs, not friendly names, determine device identity. Enumeration
and policy calls run on workers with scoped COM lifetimes. No third-party package
or external utility is required.

Default switching uses Windows' undocumented `IPolicyConfig::SetDefaultEndpoint`
interface, isolated in `WindowsAudioDeviceService`. This is a compatibility limit,
not a publicly supported Microsoft setter API. Reference sources:
[Microsoft endpoint enumeration](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-immdeviceenumerator-enumaudioendpoints),
[default role lookup](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-immdeviceenumerator-getdefaultaudioendpoint),
and [EarTrumpet's policy interface declarations](https://github.com/File-New-Project/EarTrumpet/blob/master/EarTrumpet/Interop/MMDeviceAPI/IPolicyConfig.cs).
The adapter validates active state/direction before writes; Core reads back defaults
after each write. Access or interface failures become normal recoverable run errors.

A run-local `SessionAudio` records original/applied IDs separately for console,
multimedia and communication roles. Apply validates both selections and captures all
original defaults before writes; it rechecks the baseline immediately before each
write, accepting the target if an earlier setter already applied it. All planned
changes are retained before the first setter because Windows can switch console
and multimedia roles together, including when a later setter fails. End waits for
startup/rollback to settle before cleanup, retaining late
writes during cancellation. Startup failure rolls audio back without implicitly
closing owned apps. Confirmed End restores audio after its process-close attempts,
including when apps remain open; Finish/Leave-and-close await restoration without
closing apps. `LeaveAppsOpenAsync` serializes this work in Stopping; close callers
check active-run state again before exiting.

Restoration runs in reverse order, checks current ID against the run's applied ID,
and relinquishes roles with a different current default. Failed restoration retains
only unresolved roles for explicit retry and prevents terminal completion, including
monitor-driven completion after an app closes. Console and multimedia form a
restoration group per direction: check both captured roles, even one already matching
the target at startup, and relinquish both if either now differs from its original
and applied IDs. If either lookup fails, retain both pending roles without writing.
This prevents a linked setter from overwriting a later ordinary-device selection;
communications restoration is independent. A successful restore is not repeated.
There is no crash recovery journal or audio mutation on editor save/refresh. Compare
and write are not atomic; later choices that return to the same ID cannot be detected.
The UI shows runtime audio errors through the existing bounded status/recovery area.

### Process ownership and cleanup

The authoritative rule is in [PRODUCT.md](PRODUCT.md#ownership-and-cleanup). `ISessionProcessHost` and `ITrackedProcess` form the Core/platform boundary. The Windows adapter restricts queries to the current Windows login session, matches full executable paths case-insensitively, and retains handles identifying exact process lifetimes. Existing matching instances are observed but never owned; when several main-app instances already exist, automatic ending waits for all captured instances to exit. An unreadable same-name candidate prevents a new launch when no existing match can be established.

New launches preserve desktop shell activation and the Discord output-pipe fix. Identity is captured immediately after Process.Start, before a 500 ms settling interval. WindowsProcessIdentity retains a handle with PROCESS_QUERY_LIMITED_INFORMATION and SYNCHRONIZE, allowing observation of elevated apps without requesting unnecessary full process access. Full path, Windows login session and creation time are verified; an older reused process remains unowned. Null/inaccessible/mismatched launch handles remain untracked. See Microsoft's [process access rights](https://learn.microsoft.com/en-us/windows/win32/procthread/process-security-and-access-rights).

WindowsTrackedApp follows only verified same-executable self-restarts of owned launches. When the current process exits, a Toolhelp snapshot supplies direct-child candidates. A replacement must have the same full path/login session and a creation time within the retained parent's creation/exit interval. Exactly one candidate can transfer tracking; ancestor handles remain retained. Multiple candidates or unverifiable same-name child identities produce a manual-management error rather than guessed ownership. Once final exit is confirmed, later independent launches are never adopted. The chain is bounded to 16 processes. This supports SRS-style restart/elevation, including a handoff before the first monitor tick; it is not general launcher, descendant-tree, browser-worker, service, or broker tracking. See [PROCESSENTRY32](https://learn.microsoft.com/en-us/windows/win32/api/tlhelp32/ns-tlhelp32-processentry32w) and [GetProcessTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getprocesstimes). RunAsAdministrator can avoid self-elevation by starting the configured app elevated directly.

End visits owned apps in reverse configuration order. WindowsProcessCleanup posts WM_CLOSE only to exact-process application windows: titled, carrying WS_SYSMENU, and not WS_EX_TOOLWINDOW. Eligible main windows may be hidden (tray apps). Untitled, system-menu-less and tool/helper windows are excluded; broadcasting WM_CLOSE to internal infrastructure can destroy it without shutting down its application. Window enumeration errors are surfaced. Cleanup then waits up to three seconds for the retained process handle to signal exit, not for a window/tray icon to disappear. A still-running app is preserved unless its captured AllowForceQuit setting is true or the user explicitly confirmed ForceQuitAppAsync for that app and RunId. ITrackedProcess.RequestCloseAsync receives this permission from Core; the Windows adapter and elevated helper propagate it unchanged. Termination opens a handle with explicit terminate/query/synchronize access, revalidates creation time, path and login session against the retained identity, and uses TerminateProcess on that handle. No process-name kill or entire-tree termination exists. Already-open apps never reach automatic Session cleanup, regardless of their settings. The separately confirmed individual Close action can target them explicitly. The UI obtains save-work confirmation before any cleanup. WM_CLOSE lets each app handle its own unsaved-work prompt. No temp-file, process-name or dialog heuristic authorizes termination. The End/close dialogs name automatic-force apps and warn about potential loss; one-off force quit has a separate named-app confirmation.

UIPI/access denial on an elevated app invokes a one-shot helper using the same Sessions.App executable with the runas verb. Program routes this mode before Avalonia, the single-instance mutex and library loading. Arguments carry only the exact PID, creation FILETIME, executable path, login session, captured close policy and bounded timeout. The helper requires the same Windows login session and revalidates identity before acting; it cannot recursively elevate. It closes just that target and exits with a result. Windows UAC remains in control; cancellation/error keeps ownership for Retry End. No persistent privileged service, elevation bypass, shell command string, or writable command file is used. The UI app remains asInvoker. See Microsoft's [PostMessage/UIPI restrictions](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-postmessagew) and [separate elevated operations guidance](https://learn.microsoft.com/en-us/windows/win32/secbp/running-with-administrator-privileges).

If an owned app stays open or close fails, the run remains NeedsAttention and blocks another Start. The monitor continues after a stop request, observing exact retained lifetimes without reissuing close; once all remaining owned apps exit it finishes the run. Cancelling an app save prompt keeps ownership and does not re-prompt. Dismissed startup-failure cleanup without a stop request is not auto-finalized. Retry End Session attempts cleanup again using per-app permissions; Finish and leave apps open explicitly releases ownership without closing anything. ForceQuitAppAsync validates RunId, app identity, LeftOpen state and ownership before using the serialized cleanup path for only that app. Stale, unowned and unrelated requests do nothing. MainViewModel maintains named cleanup rows independently of sidebar selection, dismisses stale force confirmations and routes Bring forward through the existing presence service; foreground activation does not confer cleanup ownership. A self-restart completing during cleanup is checked before declaring success; cleanup follows up to three replacements in one attempt. If external brokers hide parentage, the launch is too short-lived to capture, or access prevents identity verification, manual management remains necessary. Finishing/closing the old run cannot retroactively adopt apps discovered by green presence indicators.

Normal window close during an active run offers Keep Sessions open, End Session and close, or Leave apps open and close. The close dialog explains normal closing and warns about any explicitly configured automatic-force apps; End-and-close is the explicit approval and only exits when cleanup completes; otherwise the main window remains available for recovery. Leaving apps open is disabled during starting/stopping. An editor/save in progress must be resolved before closing an active run. Forced process termination/crashes cannot perform this UI flow: independent apps remain open, in-memory ownership is lost, and a new instance treats them as pre-existing. Runtime ownership is not restored from guessed process names or a stale file.

### Error handling and action policies

Core is responsible for error-handling policies and runtime results that let the UI report success, failure, running processes, ownership, and expected cleanup.

The policy aborts remaining startup on acquisition/readiness failure and cancels other readiness waits/pauses. It drains in-flight acquisitions before deciding whether cleanup is needed. If any acquisition was owned, it transitions to AwaitingEndConfirmation; rollback does not run until approval. Cancelling leaves NeedsAttention with retained ownership for later cleanup. Failure with no owned acquisitions can finish once all in-flight work settles. Confirmed rollback closes only owned apps already opened, in reverse definition order. Cleanup continues after individual close errors. Failed is terminal only once cleanup has completed; remaining owned apps keep NeedsAttention until observed exit, retry, targeted force quit or explicit leave-open. Untracked handoffs are an explained manual-management outcome, not inferred ownership. Normal close with explicit per-app force permission is the policy for every cleanup entry point; configurable retry/skip/optional-app policies remain future work.

### Application preferences and appearance

`AppPreferences` is App-layer presentation configuration, separate from Core Session
models and the library schema. `JsonPreferencesStore` implements `IPreferencesStore`
using a version-1 JSON envelope in `%LOCALAPPDATA%\Sessions\preferences.json`.
It validates supported theme/size choices, writes a unique adjacent temporary file,
then replaces the destination after a complete write. Missing files return defaults;
invalid/unsupported files are errors. Explicit recovery copies existing bytes to a
unique `.recovery-<id>.bak` before replacement. A failed backup prevents replacement.
The existing application-instance guard also protects preference writes.

`PreferencesService` serializes load/save/reset and exposes the last successfully
applied immutable preferences. Composition loads it asynchronously when the main
window opens. Load failure preserves usable appearance, reports a recovery warning,
and blocks ordinary Apply until retry or explicit reset succeeds. Save failure does
not change applied preferences. Settings has an independent `SettingsViewModel`
draft and preview; its owned, modeless window neither replaces the Session editor
nor invokes execution operations. Reopening activates the existing window. Closing
either window is blocked during a preferences operation; other main-window close,
draft and runtime rules remain in force. Preferences controls unsubscribe on close.

Main-window state is not a preference. `JsonMainWindowStateStore` keeps a version-1
envelope in `%LOCALAPPDATA%\Sessions\window.json` with the normal (restored) bounds,
maximized flag and last selected Session ID. It is read synchronously before the window
is shown, so the window opens in place, and written with temporary-file replacement when
the window closes. Missing, unreadable or unsupported files and failed writes are ignored
and never block starting or closing. Position is physical pixels; size is logical.
`WindowPlacementPolicy` fits saved bounds to current working areas: the most-overlapped
screen at its own scaling, otherwise centred on the primary screen, capped by the working
area minus the existing decoration allowances and never below the minimum window size.
`MainWindow` records normal bounds after resize/move events settle, because Windows
reports a maximized size before the state change. Selection is restored after the library
loads; an unloaded library keeps the earlier saved ID.

`AppInfo` supplies About's version (the informational version without build metadata),
project stage and repository from assembly metadata generated from `Directory.Build.props`
(`Version`, `ReleaseStage`, `RepositoryUrl`), plus the data folder
(`%LOCALAPPDATA%\Sessions`, also used by composition for every local file) and the
installation folder containing `LICENSE` and `licenses`. `AboutViewModel` opens web pages
and the data folder only through Avalonia's `ILauncher`, supplied by the Settings window and
replaceable in tests; a false result or exception becomes a nearby message with the
address or path. It performs no network requests itself. License texts are shown in-app:
`LicenseCatalog` lists `LICENSE`, the packaged `licenses/dependencies.json` inventory
(formatted as a package summary, accepting PowerShell's single-object form), the top-level
`THIRD-PARTY-NOTICES.txt` and notice files under `licenses` with .txt, .md or no extension
(package .nuspec metadata is skipped), and reads each on selection with a 2 MiB cap.
`LicensesViewModel` ignores stale reads; `LicensesWindow` is owned by Settings, reused while
open and applies the same appearance preferences.

`WindowAppearance` applies window-level theme variants and token-derived dynamic
font-size/line-height resources, keeping application theme inheritance at System.
Main and picker content use layout transforms, not Windows DPI changes. The effective
interface multiplier is capped by available width/height divided by the window's
established minimum viewport, with 100% as the floor. Compact styling uses the
resulting logical width. Settings keeps standard interface spacing and applies text
preferences; its isolated preview shows the requested multipliers. System preview
tracks the application's OS-following actual theme even under a window override.
No Windows text-size factor is manually reapplied. Headless rendering/scaling tests
are not evidence of physical-monitor, native screen-reader or OS text-size behavior.

### Bundled plugins

`Sessions.Plugins` defines `IApplicationPlugin`, metadata, string-valued local
settings and a catalog with explicit bundled registration. `Sessions.Plugins.Steam`
is the first separate implementation assembly. Both are independent of Avalonia;
App composition supplies the Windows Steam adapter and the local preference store.
Core contains only the portable `PluginAppReference` and captured launch boundary.

The existing `StartProcessAction` record gains an optional plugin target to preserve
ordinary executable callers and their stored shape without a broad action refactor.
A plugin entry has an empty executable path and no ordinary executable options.
Its reference carries plugin ID, target ID, configuration version and optional opaque
JSON, plus an explicit CloseOnEnd flag (default false). Structural validation and saving do not require the plugin to be installed;
unknown positive versions and opaque data survive unrelated edits. Legacy envelopes
cannot introduce plugin targets. Runtime resolution never treats one as an executable.

`ISessionPluginHost.Capture` captures configuration once per run.
`ISessionPluginLaunches` validates targets, reports optional provider presence and
opens plugin apps. Default implementations remain launch-only; a SupportsClose
capability permits ProcessAcquisition results with retained lifetimes. Catalog and
Core both restrict ownership to a captured CloseOnEnd opt-in. Capability checks run
in preflight. Presence alone can never grant ownership or window focus.
Before audio or executable startup, Core validates all plugin targets. Plugin calls
join the existing startup cancellation/drain/failure policy. Issued launches must
settle before End completes. Same-plugin launches serialize in All at once; a
duplicate plugin/target pair is rejected before startup. Only ordinary apps can
provide main lifetime, readiness or completion focus.

Catalog API version 1 accepts compatible explicitly registered bundles. No folder
scanning, assembly loading, plugin downloads, hot reload or sandbox is implemented.
Bundled code runs in process with Sessions' user privileges and is trusted like the
application; exceptions can be reported, but this is not isolation from crashes or
malicious code. Plugins are stateless service instances receiving captured settings,
without background initialization or cleanup hooks. Third-party loading/trust and
other action kinds require a future contract decision rather than bypassing the current contract.

`PluginService` owns version-1 `%LOCALAPPDATA%\Sessions\plugins.json`, independently
of the library and appearance store. Missing files use bundled defaults without
writing; failed initial reads block plugin operations. Apply/reset/load serialize;
reset cannot precede an attempted read, and failed-load recovery backs up original
bytes before atomic replacement. Unknown plugin IDs, settings versions and keys are
retained. Settings has an independent plugin draft and Apply/reset controls; closing
either window cannot abandon a write. Active captured configurations remain valid
after preferences change. No plugin-specific account or credential data is included
in the shareable library by the Steam slice.

Steam uses bounded text VDF/ACF parsing and stable numeric app IDs, with registry
installation discovery isolated in `WindowsSteamClient`. The adapter sends only a
numeric `steam://run/<appid>` argument to that installation's `steam.exe` using
desktop shell activation. Its returned client handle is disposed and never tracked
as a game. The separate presence path reads the local current-user
`Software\Valve\Steam\Apps\<appid>` Running flag. This undocumented client signal
is presentation only; missing/unreadable values give unknown status.

For opted-in Session launches, WindowsSteamLaunchObservation captures a process-ID
baseline and retains a cursor at the end of gameprocess_log.txt before activation.
A known-clear Running flag is required; an existing process inside the app directory
also blocks acquisition. SteamLaunchTracker reads only subsequent matching AppID
addition records, ignores command lines, excludes baseline PIDs and verifies full
process path inside the manifest install directory, login session, retained creation
time after the request and within the log timestamp's bounded tolerance. Handles
retain identity across PID reuse. Capture waits at least five seconds and for a
tracked window, with a 30-second maximum even for a cancelled issued launch. Up to
1 MiB of appended log text is accepted; truncation/read/verification failures keep
any already retained ownership and otherwise fall back to manual closing. Rotation
keeps the original file handle and may cause manual fallback.

The result is a fixed set of WindowsTrackedApp handles with later self-restart
adoption disabled. Cleanup uses existing exact-identity WM_CLOSE, timeout and explicit
force-quit handling; no Steam-wide command, process-name kill, arbitrary descendant
adoption or game install-directory-wide termination is used. Late-starting processes,
processes outside the game directory, unverified helpers and later launches remain
independent. This local Steam-log correlation is a bounded compatibility heuristic,
not a public Valve lifecycle API or proof of causality against simultaneous external
launches; uncertainty never expands cleanup targets.

Local discovery never activates Steam or writes its files. See
[PLUGINS.md](PLUGINS.md) for supported settings, source references and validation limits.

### Persistence

Use simple local JSON persistence initially. Prefer readable, portable configuration that supports the [future product uses](PRODUCT.md#local-first-and-open-source-philosophy) of export, import, sharing, and source control.

Do not introduce a database until there is a demonstrated need. The JSON envelope has a `version` and an ordered `sessions` array. The reader accepts versions 1–6; the writer uses version 6 and readable string enums for startup settings. Missing settings in version-1 definitions retain ordered launching, zero pause, launch-completed readiness, a 30-second readiness timeout, no per-app override, and unchanged focus. Loading never rewrites the file. Versions 1 and 2 load with AllowForceQuit false even if unknown fields request otherwise. Version 3 prevents published 0.2.1 and earlier builds from silently ignoring the safer cleanup policy; version 2 previously protected startup settings. Version 4 adds Session audio identities/names; formats 1–3 leave audio unchanged, and published 0.2.3 and earlier reject v4. Version 5 adds plugin references; older envelopes reject plugin entries, and published 0.4.0 and earlier reject v5 rather than silently dropping them. Version 6 adds `StartProcessAction.Optional`; formats 1–5 load it as false even if a stray field is present, and published 0.5.0 and earlier reject v6 rather than dropping it on save. Only configuration is persisted; runtime state, captured plugin preferences, audio restoration records and RunId are excluded. Saves validate first, write a uniquely named temporary file beside the destination, then replace the destination after the complete write. Before replacing a file whose `version` is not the current writer version (older, newer or unreadable), the store copies it byte-for-byte to `sessions.v<version>-backup-<local yyyyMMdd-HHmmss>.json` (or `.unreadable-backup-`), adding a numeric suffix rather than overwriting an earlier copy. A failed copy throws before replacement, so the save fails and the original stays. `ISessionStore.LastUpgradeBackupPath` reports the copy from the most recent successful save (a default interface member, null for other stores); the main view model shows a dismissible notice. Any future format change to preferences, plugin preferences or window state must follow the same rule, as those files are still version 1. Missing files start an empty library; malformed, invalid, or unsupported files produce errors and remain untouched. The UI blocks library writes after a failed initial load until a retry succeeds. The store has one application writer: `Program.Main` acquires `SingleInstanceGuard` before Avalonia composition/library access. A Global named mutex, keyed by a hash of the local app-data profile path, prevents competing instances (including the same profile in another Windows login session). A second launch signals a named event and exits without reading/writing the library; the owner requests restoration/activation of its window. The mutex is released on normal exit and recovered if abandoned after a crash. This guards application instances, not arbitrary external editors of the JSON file.

The App test project uses Avalonia's headless platform with Skia for rendering and xUnit v3, as required by Avalonia 12's headless runner. Core tests retain xUnit v2. Run `dotnet test tests/Sessions.App.Tests/Sessions.App.Tests.csproj` in addition to Core tests when changing UI behaviour. Setting `SESSIONS_SCREENSHOT_DIR` to an output directory saves review PNGs from the headless tests; they do not open desktop windows or use the real Session library.

## Possible future extensions

These are existing directions for evolution, not commitments to implement them all in the first milestone. Do not implement speculative action types until needed.

### Additional action types

Beyond Start Process, initially useful candidates are:

- Stop Process.
- Wait for Process.
- Run Command.
- Open URL.
- Delay.

Further possible actions include:

- PowerShell.
- Windows Service.
- Select audio device.
- Set Windows power plan.
- OBS control.
- Docker actions.
- SSH commands.
- HTTP requests.

### Action configuration

Actions should eventually support configuration such as:

- Start only if not already running.
- Remember whether the Session started the process.
- Stop when the Session ends.
- Wait until a tracked process is running or a window appears (implemented in SESS-023).
- Readiness timeout (implemented in SESS-023; launch/UAC timeouts remain unsupported).
- Continue the Session on failure.
- Abort the Session on failure.
- Optional administrator elevation (implemented per app).
- Arguments.
- Working directory.
- Environment variables.

The ownership and cleanup guarantees above remain mandatory for the first milestone even though broader configurability is listed here as an evolution path. Runtime event responses and additional system-setting restoration may be introduced when needed; avoid designing speculative subsystems for them now.
