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

The implemented saved model is `SessionDefinition`: identity, name, description, an ordered list of `StartProcessAction` values, and an optional `MainAppId` referencing an app in that list. Each app has an identity, display name, executable path, arguments, working directory, and an optional RunAsAdministrator flag defaulting to false. ForceClose was removed: old version-1 JSON forceClose fields are ignored on load and omitted on the next normal save; no eager rewrite/migration occurs. Confirmed full stopping is a runtime policy for every app. Core validation rejects missing required values, duplicate app identities, and dangling main-app references. A null main-app identity means a user-bound Session, including a Session with no configured apps. Runtime snapshots and retained process references live in the runner and are never persisted in saved definitions.

The editor copies a definition into a draft and replaces the saved definition only after persistence succeeds. App reordering preserves identities, so moving the main app does not change the lifetime setting. Removing the main app requires an explicit replacement or switching back to user-bound lifetime before saving. Executable and working-directory validation occurs in the Windows adapter when an app must be launched.

### App discovery

`IAppSource`, `DiscoveredApp`, `WindowsRunningAppSource`, and `WindowsStartMenuAppSource` live in `Sessions.App/Services`, outside the platform-neutral Core. `MainWindow` supplies both sources to the picker and permits injection for tests. Discovery is a configuration aid, separate from the Core execution/ownership boundary.

The running-app implementation enumerates visible, titled top-level windows, excluding tool/cloaked windows and Sessions itself. It checks title length without reading title content. It queries executable paths with `PROCESS_QUERY_LIMITED_INFORMATION`, checks package identity, and reads friendly names from executable version information. Multiple windows/processes are grouped by executable path. Apps that exit during inspection are skipped; unreadable paths or unsupported activation methods are not selectable. Windows-packaged apps and `ApplicationFrameHost` are conservatively unsupported by this `.exe`-only configuration model. No commands are run and no processes are adopted or terminated.

The Start menu adapter scans `SpecialFolder.Programs` and `CommonPrograms` on a background STA thread, skipping reparse-point children. `WindowsShellShortcut` uses `IPersistFile.Load` in read mode and `IShellLinkW` to read `.lnk` target, arguments, and working directory; `IShellLinkDataList` supplies the administrator flag. Environment variables in target/working paths are expanded; arguments are retained verbatim. It does not call Resolve, Save, or activate a shortcut. See Microsoft's [IShellLinkW contract](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ishelllinkw). Invalid/missing/non-executable targets, unavailable working folders, unreadable entries, and `.url` links produce disabled choices. Identical shortcuts across roots merge; distinct launch settings remain choices. Names come from shortcut filenames; icons come from target executables. Virtual AppsFolder/packaged activation and shortcut show-window preferences are not implemented.

Enumeration, file metadata, and icon extraction run off the UI thread. `System.Drawing.Common` is used only by the Windows adapters to extract executable icons into PNG bytes; Avalonia displays those bytes, falling back to an initial when an icon is unavailable. The picker disposes its bitmaps and cancels pending discovery on close. `AppPickerViewModel` handles source switching, search, multi-selection, refresh, and browse-result merging. Both sources load independently; a failure preserves that source's previous snapshot and allows the other source to remain usable. `AppPickerWindow` owns the modal and native file picker; only explicit Add returns choices to the editor. Cancel returns no choices. Checked selections survive filtering and source switches; refresh retains choices still present and separately browsed entries. Choice identity includes source, executable path, arguments, working directory, and elevation; selection permits only one choice per executable across sources.

The editor suppresses picker duplicates by normalized, case-insensitive executable path and appends fresh action identities, preserving existing arguments, working directories, main-app choice, and action order. It copies the discovered name/path plus shortcut arguments, working directory, and administrator flag when supplied; running-app discovery supplies no such launch metadata. These fit the existing `StartProcessAction` schema. No process identity, runtime ownership, icon, window title, inspected process command line, or open-document information is persisted. Runtime launch validation remains necessary even after successful discovery.

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

### Accessibility and available space

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

### Process ownership and cleanup

The authoritative rule is in [PRODUCT.md](PRODUCT.md#ownership-and-cleanup). `ISessionProcessHost` and `ITrackedProcess` form the Core/platform boundary. The Windows adapter restricts queries to the current Windows login session, matches full executable paths case-insensitively, and retains handles identifying exact process lifetimes. Existing matching instances are observed but never owned; when several main-app instances already exist, automatic ending waits for all captured instances to exit. An unreadable same-name candidate prevents a new launch when no existing match can be established.

New launches preserve desktop shell activation and the Discord output-pipe fix. Identity is captured immediately after Process.Start, before a 500 ms settling interval. WindowsProcessIdentity retains a handle with PROCESS_QUERY_LIMITED_INFORMATION and SYNCHRONIZE, allowing observation of elevated apps without requesting unnecessary full process access. Full path, Windows login session and creation time are verified; an older reused process remains unowned. Null/inaccessible/mismatched launch handles remain untracked. See Microsoft's [process access rights](https://learn.microsoft.com/en-us/windows/win32/procthread/process-security-and-access-rights).

WindowsTrackedApp follows only verified same-executable self-restarts of owned launches. When the current process exits, a Toolhelp snapshot supplies direct-child candidates. A replacement must have the same full path/login session and a creation time within the retained parent's creation/exit interval. Exactly one candidate can transfer tracking; ancestor handles remain retained. Multiple candidates or unverifiable same-name child identities produce a manual-management error rather than guessed ownership. Once final exit is confirmed, later independent launches are never adopted. The chain is bounded to 16 processes. This supports SRS-style restart/elevation, including a handoff before the first monitor tick; it is not general launcher, descendant-tree, browser-worker, service, or broker tracking. See [PROCESSENTRY32](https://learn.microsoft.com/en-us/windows/win32/api/tlhelp32/ns-tlhelp32-processentry32w) and [GetProcessTimes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getprocesstimes). RunAsAdministrator can avoid self-elevation by starting the configured app elevated directly.

End visits owned apps in reverse configuration order. WindowsProcessCleanup posts WM_CLOSE only to exact-process application windows: titled, carrying WS_SYSMENU, and not WS_EX_TOOLWINDOW. Eligible main windows may be hidden (tray apps). Untitled, system-menu-less and tool/helper windows are excluded; broadcasting WM_CLOSE to internal infrastructure can destroy it without shutting down its application. Window enumeration errors are surfaced. Cleanup then waits up to three seconds for the retained process handle to signal exit, not for a window/tray icon to disappear. After user confirmation, every owned app still running is terminated; no per-app ForceClose gate remains. Termination opens a handle with explicit terminate/query/synchronize access, revalidates creation time, path and login session against the retained identity, and uses TerminateProcess on that handle. No process-name kill or entire-tree termination exists. Already-open apps never reach cleanup, regardless of their settings. The UI obtains save-work confirmation before any cleanup. A save prompt does not prevent the subsequent termination once the user has approved stopping; the warning explicitly explains possible loss of unsaved changes.

UIPI/access denial on an elevated app invokes a one-shot helper using the same Sessions.App executable with the runas verb. Program routes this mode before Avalonia, the single-instance mutex and library loading. Arguments carry only the exact PID, creation FILETIME, executable path, login session, captured close policy and bounded timeout. The helper requires the same Windows login session and revalidates identity before acting; it cannot recursively elevate. It closes just that target and exits with a result. Windows UAC remains in control; cancellation/error keeps ownership for Retry End. No persistent privileged service, elevation bypass, shell command string, or writable command file is used. The UI app remains asInvoker. See Microsoft's [PostMessage/UIPI restrictions](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-postmessagew) and [separate elevated operations guidance](https://learn.microsoft.com/en-us/windows/win32/secbp/running-with-administrator-privileges).

If an owned app stays open or close fails, the run remains NeedsAttention and blocks another Start. Retry End Session attempts cleanup again; Finish and leave apps open explicitly releases ownership without closing anything. A self-restart completing during cleanup is checked before declaring success; cleanup follows up to three replacements in one attempt. If external brokers hide parentage, the launch is too short-lived to capture, or access prevents identity verification, manual management remains necessary. Finishing/closing the old run cannot retroactively adopt apps discovered by green presence indicators.

Normal window close during an active run offers Keep Sessions open, End Session and close, or Leave apps open and close. The close dialog warns about unsaved work and forced termination; End-and-close is the explicit approval and only exits when cleanup completes; otherwise the main window remains available for recovery. Leaving apps open is disabled during starting/stopping. An editor/save in progress must be resolved before closing an active run. Forced process termination/crashes cannot perform this UI flow: independent apps remain open, in-memory ownership is lost, and a new instance treats them as pre-existing. Runtime ownership is not restored from guessed process names or a stale file.

### Error handling and action policies

Core is responsible for error-handling policies and runtime results that let the UI report success, failure, running processes, ownership, and expected cleanup.

The policy aborts remaining startup on acquisition/readiness failure and cancels other readiness waits/pauses. It drains in-flight acquisitions before deciding whether cleanup is needed. If any acquisition was owned, it transitions to AwaitingEndConfirmation; rollback does not run until approval. Cancelling leaves NeedsAttention with retained ownership for later cleanup. Failure with no owned acquisitions can finish once all in-flight work settles. Confirmed rollback closes only owned apps already opened, in reverse definition order. Cleanup continues after individual close errors. Failed is terminal only once cleanup has completed; remaining owned apps keep NeedsAttention until retry or explicit leave-open. Untracked handoffs are an explained manual-management outcome, not inferred ownership. Full stopping after confirmation is the standard policy; configurable retry/skip/optional-app policies remain future work.

### Persistence

Use simple local JSON persistence initially. Prefer readable, portable configuration that supports the [future product uses](PRODUCT.md#local-first-and-open-source-philosophy) of export, import, sharing, and source control.

Do not introduce a database until there is a demonstrated need. The JSON envelope has a `version` and an ordered `sessions` array. The reader accepts versions 1 and 2; the writer uses version 2 and readable string enums for startup settings. Missing settings in version-1 definitions retain ordered launching, zero pause, launch-completed readiness, a 30-second readiness timeout, no per-app override, and unchanged focus. Loading never rewrites the file. Version 2 prevents older builds from silently ignoring execution settings. Only configuration is persisted; runtime state and RunId are excluded. Saves validate first, write a uniquely named temporary file beside the destination, then replace the destination after the complete write. Missing files start an empty library; malformed, invalid, or unsupported files produce errors and remain untouched. The UI blocks library writes after a failed initial load until a retry succeeds. The store has one application writer: `Program.Main` acquires `SingleInstanceGuard` before Avalonia composition/library access. A Global named mutex, keyed by a hash of the local app-data profile path, prevents competing instances (including the same profile in another Windows login session). A second launch signals a named event and exits without reading/writing the library; the owner requests restoration/activation of its window. The mutex is released on normal exit and recovered if abandoned after a crash. This guards application instances, not arbitrary external editors of the JSON file.

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
