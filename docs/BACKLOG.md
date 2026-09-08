# Sessions — Backlog

Last reviewed: **2026-09-08**. Scope comes from [PRODUCT.md](PRODUCT.md); technical constraints come from [ARCHITECTURE.md](ARCHITECTURE.md). [DEVELOPMENT_NOTES.md](DEVELOPMENT_NOTES.md) contains the current handoff and evidence behind this initial backlog.

This is a work record, not authorization to implement everything. The user explicitly requested safe Session deletion (SESS-009), a running-app picker (SESS-008), running indicators with window focus (SESS-012), and individual app launching (SESS-013), implemented below. Priorities remain an initial sequencing recommendation and can change with feedback.

## Working rules

- Keep IDs stable and never reuse them. Add new findings to the relevant item or assign the next ID.
- Status: **Open**, **In progress**, **Awaiting feedback**, **Proposed**, **Done**, or **Deferred**. In progress means someone is actually working on the item. Proposed means the solution or scope is not yet settled.
- Priority: **P0** must be resolved before real Session launching is enabled; **P1** is near-term validation or usability/reliability work; **P2** is a later improvement. Priority does not claim that the feature is already part of the first milestone.
- Record the source and whether a problem is reproduced. Add reproduction steps and expected/actual behaviour when user feedback reports a bug. Include relevant window size, theme, app, or lifetime mode when useful.
- Complete an item only when its acceptance criteria are met and validation evidence is recorded. Retain a short completed entry; update the product/architecture documents when decisions change.
- Do not add all speculative action types as committed work. Existing out-of-scope requirements remain in force.

## Suggested sequence

The user confirmed the default stopping flow (SESS-018) and Start menu picker (SESS-019), then requested a README as the first step toward GitHub and eventual public releases (SESS-020). No next app feature is selected. Suggested app follow-ups are draft-close protection (SESS-006), accessibility/scaling (SESS-010), and invalid-field guidance (SESS-011). Remaining native checks stay under SESS-001, including UAC cancellation and the narrower Playnite-specific confirmation in SESS-017; do not treat the overall stopping flow as still failing. SRS start/stop is confirmed under SESS-016. The first execution slice and its prerequisites are implemented under SESS-002/003/004/005/007.

## SESS-001 — Review the first native UI with the user

**P1 · Awaiting feedback · Current UI validation**  
Source: the user began a hands-on trial on 2026-09-08. Discord launch independence (SESS-014), SRS start/stop (SESS-016), default full stopping (SESS-018), and Start menu discovery (SESS-019) are now confirmed. Earlier Start/End feedback identified tray-only close behavior (SESS-015) and SRS self-elevation tracking (SESS-016). These confirmations do not complete all checks below.

Check whether first creation, choosing Sessions, editing, app ordering, and the end-condition choice are discoverable without explanation. Confirm the Windows file picker (including cancel and multiple files), persistence after a real restart, and behaviour at the user's normal display scale. Ask whether scrolling hides an important choice and whether disabling sidebar browsing while editing feels natural. Confirm that the green Running button brings a normal and a minimized app window forward, including an app with several windows; verify the background-only explanation. Observe feedback rather than assuming a specific answer.

Done when: feedback is recorded with enough context to act on; bugs/improvements are linked to stable IDs; unperformed checks remain explicitly listed. A passing headless test alone does not complete the native picker/restart checks.

## SESS-002 — Implement ordered execution and both lifetime modes

**P0 · Done · First execution slice · 2026-09-08**  
Source: first-milestone requirements and the user's explicit request to activate Start/End Session.

Implemented in Core: captured run definitions, ordered startup, one active run, user-bound/empty Sessions, tracked-main exit monitoring, End during startup, duplicate-operation guards, and abort/rollback on failure. Windows operations stay behind ISessionProcessHost/ITrackedProcess; UI only presents snapshots. Core tests exercise ordering, main/supporting exits, multiple existing main instances, empty Sessions, startup failure, and concurrent/repeated operations. Native main-exit cleanup passes. SESS-018 now gates main-exit cleanup and startup rollback on save-work confirmation.

Limits: null/unverifiable launch handoffs are explicitly untracked and require manual management, including manual End for an untracked main. No general child-process adoption or configurable action-policy subsystem is introduced.

## SESS-003 — Track process identity and ownership

**P0 · Done · Conservative process ownership · 2026-09-08**  
Source: mandatory ownership rules, implemented with SESS-002.

Full-path/current-login matching and retained process handles distinguish pre-existing processes from verified new shell-launch handles. Existing instances remain unowned. Unknown handoffs and inaccessible identity are never adopted; cleanup never resolves a replacement PID by executable name. Identity is now captured before the settling interval, and verified same-executable self-restarts are followed under SESS-016. Creation time prevents treating a returned older process as newly owned.

Evidence: Core owned/unowned tests and native tests prove an already-open app survives End while an owned app receives a normal close. The native fixture now verifies a refusing owned app is terminated after confirmed End while a pre-existing refusing app survives (SESS-018). General launchers/descendant trees remain unsupported and are explained in the UI.

## SESS-004 — End safely and define failure recovery

**P0 · Done · Graceful cleanup and recovery · 2026-09-08**  
Source: ownership/cleanup requirements, implemented alongside Start/End.

End requests normal WM_CLOSE in reverse order, waits three seconds per process, and leaves pre-existing/untracked apps untouched. Startup abort rolls back owned apps; End during startup waits for the in-flight acquisition. Close failures do not prevent cleanup of other apps. Remaining owned apps keep NeedsAttention, with Retry End or explicit Finish and leave apps open. SESS-018 supersedes the earlier per-app force-close choice: every confirmed End attempts normal close then terminates lingering owned apps. Main-app exit and startup rollback require the same save-work confirmation. Elevated cleanup follows SESS-016.

Normal window close during a run offers keep open, end-and-close, or leave-apps-open-and-close; incomplete cleanup keeps Sessions open. Starting/stopping cannot be abandoned by the normal leave-open command. Tests cover refusal/retry/release, errors, rollback, ordering, overlapping End, and active-window close choices. Hard termination/crash loses in-memory ownership; surviving apps are pre-existing on restart.

## SESS-005 — Connect the UI to real runtime state

**P0 · Done · Real Session runtime UI · 2026-09-08**  
Source: user explicitly requested enabling Start and adding End after confirming the Discord fix.

Start/End now drive the Core runner. A persistent runtime panel, active sidebar marker, and per-app ownership/outcome messages survive browsing to another Session. Starting another Session is blocked; the active definition cannot be edited/deleted. Individual launches are disabled during any active run, and an in-flight manual launch must settle before Start. Apps manually opened before a run remain pre-existing.

Headless keyboard/theme tests verify Start/End, navigation, active edit/delete guards, cleanup recovery, individual-launch guards, and window close/end choices. Native tests cover owned-only cleanup, refusal, main lifetime, and single-instance coordination. Preview-only launch-disabled copy has been removed. See SESS-001/010 for continuing hands-on and accessibility review.

## SESS-006 — Protect drafts when closing the window

**P1 · Open · UI reliability follow-up**  
Source: initial code inspection of MainWindow on 2026-09-08 found no draft protection. The runtime implementation now guards closing during an active run, including active-run editing; general draft protection outside a run remains missing. Loss through native close has not yet been manually reproduced.

Reproduce closing the window with a new or edited unsaved Session and during a save. Choose a simple changed-draft policy: Save / Discard / Keep editing, or another documented recovery behaviour. Do not prompt for unchanged drafts or turn ordinary browsing into a confirmation flow.

Done when: actual changes survive or are knowingly discarded, a failed save keeps editing available, and in-flight saves are handled predictably. Add focused interaction tests. Coordinate active-run window-close behaviour with SESS-004.

## SESS-007 — Prevent conflicting application instances

**P0 · Done · Single application writer/run owner · 2026-09-08**  
Source: prerequisite for safe runtime execution and the existing single-writer JSON design.

Program acquires a Global named mutex keyed by the local profile before Avalonia/library initialization. A second instance signals an activation event and exits; the first requests restoring/activating its window. Abandoned mutexes can be acquired after a crash. The scope is one writer/run owner per profile, including separate Windows login sessions using that profile.

Native helper processes exercise ownership exclusion, activation signalling, owner exit, and later acquisition. This tests the production guard in separate processes; existing-app foreground permission remains subject to Windows. No support for multiple independent writable Sessions instances or external JSON-editor conflict detection is implied.

## SESS-008 — Make adding installed apps easier

**P1 · Done (running-app picker) · Usability improvement · 2026-09-08**  
Source: initially a UX proposal; the user explicitly requested picking from running applications so users do not need to know executable locations.

Implemented: Add app opens a searchable, multi-select picker of apps with visible windows, showing executable display names and icons with an initial fallback. Entries are grouped by executable path; existing Session apps are marked, and inaccessible/unsupported apps are disabled with explanations. Refresh retains surviving selections; Browse files merges executable choices. Cancel changes nothing; Add appends fresh action definitions without overwriting existing settings or taking ownership of running processes.

Evidence: clean build; 9 Core + 25 regular App tests pass, plus the opt-in read-only native check. Picker integration, cancellation, grouping, selection/filter/refresh, browse merging and failures are covered. Light/dark layouts were reviewed at default/minimum picker sizes. The native Windows check found 10 choices with icons, of which 6 were supported selections; no processes or saved user configuration were changed.

Follow-up: Start menu desktop-shortcut discovery is now implemented in SESS-019 after the user's explicit request. Background-only apps remain absent from Running apps but may appear through their Start menu shortcuts. Packaged-app activation and shared app hosts remain unsupported. Native mouse/file-picker and accessibility review remain SESS-001/010.

## SESS-009 — Add Session deletion and consider duplication

**P1 · Done (deletion) · Library management · 2026-09-08**  
Source: initially a code-inspection finding; the user explicitly requested safe deletion after trying the preview.

Implemented: Delete Session in the detail view, named confirmation with Cancel initially focused, Escape cancellation and keyboard containment, save-before-removal, retry without losing the Session on failure, and deterministic next/previous selection or first-run state. Only the saved definition is removed; installed apps, files, and processes are unaffected. Confirmation warns that successful deletion cannot be undone.

Evidence: solution build has zero warnings/errors; all 9 Core and 17 App cases pass. Added tests verify real JSON persistence/reload, referenced-file preservation, cancellation, failure/retry, same-name Session identity, in-flight duplicate requests, neighbour selection, and the final deletion. Light/dark confirmation/error layouts were reviewed at the minimum window size with a long name. The future active-run policy is defined in PRODUCT.md; integration is tracked under SESS-005.

Duplication remains an uncommitted optional follow-up and was not part of this request. Create a separate item if selected later; copied app identities/main-app references must then remain internally consistent.

## SESS-010 — Verify keyboard access, scaling, and longer content

**P1 · Open · UI validation**  
Source: current tests cover keyboard activation in selected flows and two rendered window sizes, not a complete accessibility/native scaling review.

Exercise the complete creator/editor using only the keyboard, including app reordering, expanded options, validation, scrolling, and focus after saving/cancelling. Review accessible names and state announcements with a screen reader. Check Windows display scaling, long names/descriptions, larger libraries, and both themes.

Done when: primary actions remain reachable, labels/content do not obstruct controls, focus returns predictably, and state/errors are understandable without colour alone. Record tested configurations and any remaining limits; add regression tests for discovered failures.

## SESS-011 — Make invalid editor fields actionable

**P1 · Open · UI feedback follow-up**  
Source: code inspection. Save is disabled for blank names/app paths, but the explicit validation message currently focuses on a missing main app. Executable existence is not checked by the editor.

Verify the invalid-field experience with the user. Provide nearby explanations for fields preventing a save, including fields inside collapsed app options. Distinguish valid saved configuration from launch-time availability: portable/offline paths should not be silently rewritten or discarded. Avoid treating successful configuration saving as proof an app can launch.

Done when: users can locate and fix the reason Save is disabled; whitespace-only inputs and missing main-app choices have understandable feedback; launch-time checks remain consistent with SESS-002.

## SESS-012 — Show running apps and bring their windows forward

**P1 · Done · Session detail usability · 2026-09-08**  
Source: explicit user request for a green running indicator that focuses the app when clicked.

Implemented: live presence beside each configured app, a keyboard-accessible green Running button, passive background/no-window and not-running labels, automatic refresh, and inline recovery feedback. Focus resolves the current window, restores it if minimized, and requests foreground activation. Full executable paths distinguish apps with the same filename. Presence never changes configuration, process ownership, or Session lifecycle.

Evidence: full solution build has zero warnings/errors; 9 Core, 32 regular App tests, and the read-only native discovery/presence check pass; headless keyboard activation, stale scans, window-close cancellation, read failure/recovery, app disappearance, and denied-focus retry are covered. Light/dark minimum-size layouts were reviewed. The native check found 11 windowed apps and verified background status, case-insensitive matching, and rejection of a different location with the same filename.

Validation limit: actual foreground switching/minimized-window restoration needs hands-on confirmation in the Rider-launched build (SESS-001); headless focus tests use the service boundary. The native test only reads desktop metadata. Tray-only activation, document/profile selection, and packaged-app activation remain uncommitted. Session runtime ownership/outcomes remain SESS-005.

## SESS-013 — Open an individual app from its Not running indicator

**P1 · Done · Explicit manual app action · 2026-09-08**  
Source: user requested making the Not running indicator clickable to start that app.

Implemented: a neutral play button, saved arguments/working folder, Starting feedback, fresh presence checks, duplicate-click and concurrent-request suppression, startup grace period, and inline launch errors with retry. A stale click focuses an already-windowed app or reports a background app. Unknown status does not launch. The real Windows adapter validates paths and launches through Windows desktop Open (SESS-014). This creates no Session run, persistence change, or cleanup ownership; apps stay independently open.

Evidence: clean full build with zero warnings/errors; 53 tests/checks pass (9 Core, 42 regular App, 2 native). Headless keyboard and light/dark rendering checks; service tests cover existing/unknown presence, concurrency, grace expiry, settings, failure/retry, and invalid paths. A native background Windows Script Host test passed, verifying actual launch with the specified arguments and working folder before exiting naturally. Full Session execution/ownership remain SESS-002/003/004/005; active-run integration is explicitly tracked under SESS-005.

Limit: the ten-second guard is local to this app instance; external launches are not atomically coordinated. Native installed GUI-app startup remains part of the Rider trial. Packaged activation remains unsupported; Windows may request consent for executables that require elevation. The original direct-launch lifetime defect is corrected under SESS-014.

## SESS-014 — Isolate launched apps from Sessions output handles

**P1 · Done · Discord fix confirmed by user · 2026-09-08**  
Source: user reports Discord errors shortly after killing Sessions. Exact Discord error/termination method not yet supplied.

Reproduced: the direct launch adapter allowed a GUI child to inherit redirected stdout/stderr. Native tests terminated or normally exited their own launcher, closed pipe readers, and observed Windows error 232 on subsequent child writes. The previous launch-receipt test had not exercised launcher shutdown.

Fixed: use Windows desktop Open activation, keep saved arguments/path validation/working folder, and allow successful shell handoff without a process handle. Both formerly failing tests pass; the child has no inherited output/error pipes and continues working after normal/forced parent exit. The native arguments/working-folder test still passes. Final validation: zero build warnings/errors; 55 tests/checks pass (9 Core, 42 regular App, 4 native); 43 local documentation links resolve.

User subsequently confirmed the Discord fix: “works like a charm!” The controlled pipe failure remains the automated evidence; the user confirmation supplies application-specific validation. Do not claim Discord itself was reproduced or that this protects against a tool explicitly killing the whole process tree/job. That fix itself did not change Session lifecycle or saved configuration; the subsequent runtime work is tracked above.

## SESS-015 — Fully exit apps that minimize on close

**P1 · Done · Full stopping now the confirmed default under SESS-018 · 2026-09-08**  
Source: user explicitly requests properly closing Tobii Game Hub even when its close behavior minimizes it.

Initially implemented a per-app Force quit if still running checkbox with an unsaved-work warning. This choice has since been superseded by confirmed full stopping for every owned app in SESS-018; the checkbox and saved flag are removed. End/rollback first sends normal close to eligible owned application windows, including hidden main windows; after the grace period, a lingering owned app is terminated through a verified exact handle after the user confirms stopping. No process tree or name-based kill is used. Pre-existing apps remain untouched. The previous no-force-close decision is superseded by this explicit user request and per-app choice.

Evidence: native refusing-app tests prove forced owned-process exit and survival of an already-open refusing app. Persistence and keyboard/draft/reopening tests cover the choice. Full build is clean; 85 tests/checks pass. The real Tobii trial remains part of SESS-001; no claim is made about stopping unrelated Tobii services.

## SESS-016 — Retain ownership across elevation and close elevated apps

**P1 · Done · SRS start/stop confirmed by user · 2026-09-08**  
Source: user reports SR-ClientRadio exits its original tracked process during UAC, remains green through independent presence detection, and survives End. Upstream SRS source confirms the same-executable runas restart; the installed version was not inspected.

Implemented: minimal query/synchronize retained handles; verified unique direct-child self-restarts matched by path, login session, parentage and creation/exit times; updated tracking messages; optional direct Run as administrator launch; and a one-shot administrator cleanup helper for access denial. The helper validates PID + creation time + path + login session, is routed before app/library initialization, never recursively elevates, and does not run the UI as administrator. Cancelled UAC leaves cleanup retryable. Unverifiable/ambiguous/broker handoffs remain manual; no name-only ownership recovery is attempted.

Evidence: native tests cover early/delayed restarts without premature main-session ending, unrelated same-path survival, and separate-helper exact-identity acceptance/rejection. These do not trigger real UAC. Clean build and 85 checks pass (19 Core, 51 regular App, 15 native).

User subsequently confirmed: “SRS start and stop, check!” This confirms the application-specific start/stop result. The exact settings and whether each helper path prompted were not supplied; UAC cancellation/retry remains an unperformed follow-up in SESS-001, not a claimed test result. No user processes or saved configuration were changed by the agent.

## SESS-017 — Avoid partial shutdown from closing internal app windows

**P1 · Awaiting feedback · Cleanup fix implemented; Playnite trial pending · 2026-09-08**  
Source: user reports Playnite Desktop's tray icon disappears but the process remains in a broken state after End, with and without Force quit; Task Manager's End process finishes it.

Code inspection found that the previous hidden-window support broadcast WM_CLOSE to every top-level window, including internal helper windows. A native fixture reproduced the resulting damaged-but-running state and failed against the previous implementation. The fix limits normal-close targets to titled application windows with a system menu, excluding tool/internal windows, while allowing eligible hidden main windows. Process exit is still determined using the retained handle. Force-close fallback still terminates only the verified owned process; no new process-tree or name-based cleanup was added.

Regression evidence covers the internal-window failure, normal close of a hidden main window, and an app whose UI disappears while its process remains alive (NeedsAttention without force; actual exit with force). The agent did not launch/stop installed Playnite. A read-only snapshot of the current saved Playnite entry showed ForceClose=false; this does not establish the setting used in the user's earlier force-enabled trial. The runtime error/outcome was requested to distinguish that report if it persists. SRS successful start/stop is recorded under SESS-016.

Validation: zero build warnings/errors and 89 checks pass (19 Core, 51 regular App, 19 native); all 41 local documentation links resolve. Finish this item when the user confirms the rebuilt version closes Playnite properly, or record any remaining per-app runtime error for further diagnosis.


## SESS-018 — Make full stopping the default with save-work confirmation

**P1 · Done · User confirmed the new stopping flow · 2026-09-08**  
Source: user reports that three of four apps need Force quit enabled and asks for full stopping by default with a save-work confirmation.

Implemented: named End confirmation with owned-app list, Cancel initially focused, Enter/Escape cancellation, and explicit Stop apps and end Session. It warns about losing unsaved work and includes in-flight apps. Confirmed cleanup first requests normal close then terminates a lingering verified owned process. Pre-existing/untracked apps remain untouched. The per-app ForceClose checkbox/model field is removed; old JSON flags are ignored until dropped on the next normal save. Run as administrator remains available. There is no additional stronger termination mode or process-tree kill.

Automatic main-app exit and startup failure with owned launches wait in AwaitingEndConfirmation. Cancelling leaves apps running, suppresses repeated main-exit prompts, and retains cleanup ownership for later End. Dialogs defer during editing/saving/other confirmations. Window-close End-and-close supplies the equivalent explicit warning. A pending End is bound to the active run ID, independent of sidebar selection.

Evidence: clean full build and **95 checks pass (23 Core, 53 regular App, 19 native)**. Native tests exercise default forced termination, pre-existing preservation and existing process-identity protections. Core/UI tests cover automatic cleanup gating, cancellation/retry, startup/failure behavior, backward-compatible storage, keyboard defaults, and confirmation for the named active Session. Light/dark dialogs reviewed at 860×620; all 41 local documentation links resolve. Real installed-app trials continue under SESS-001/017; no user apps/configuration were changed by validation.

User confirmation after trying this change: “worked like a charm!” This validates the overall stopping flow in their setup; it does not enumerate every app or establish that UAC cancellation was tested.

## SESS-019 — Find apps through the Start menu

**P1 · Done · Start menu picker confirmed by user · 2026-09-08**  
Source: user requests searching and scrolling installed apps via the Start menu when adding apps.

Implemented: Start menu is the default picker source; Running apps remains beside it. Current-user and shared desktop shortcuts supply names, folder context, executable icons, arguments, working directory, and administrator launch choice. Search, scrolling, batch selection, source switching, Refresh, and Browse files share the existing draft-only Add flow. Selecting another entry for the same executable replaces the previous choice; already-configured apps remain blocked. Failed sources preserve their prior snapshot while the other source remains usable. Broken, missing, website, and unsupported shortcuts show disabled explanations.

Discovery reads `.lnk` metadata on a background STA thread without activating, repairing, or saving shortcuts. No ownership or runtime changes are introduced. This is desktop-shortcut discovery, not complete virtual Start menu/Microsoft Store coverage; packaged activation, custom shortcut icon locations, and show-window preferences remain unsupported. Native mouse/accessibility review continues under SESS-001/010.

Evidence: zero build warnings/errors; **101 checks pass (23 Core, 58 regular App, 20 opt-in native)**. Isolated native shortcut fixtures verify metadata, duplicate roots, invalid entries, cancellation, and unchanged shortcut bytes. A read-only scan of the actual Start menu passes. UI tests exercise 80-entry scrolling, searching, keyboard switching, retained choices, duplicates, draft settings, and source failure/retry. Light/dark picker layouts were reviewed at default/minimum sizes. The user's apps and saved library were not changed by validation.

User confirmation at the end of the day: “it's working!” The Start menu picker is confirmed in their setup; this does not imply complete Microsoft Store coverage or completion of the broader accessibility review.

## SESS-020 — Prepare the public GitHub project and downloadable releases

**P1 · Open · README and GitHub setup complete; license and releases pending · 2026-09-08**  
Source: user wants a proper GitHub repository, starting with a clever public-facing README, and is considering public distribution with downloadable releases eventually.

Implemented: root README explains the app, examples, setup flow, ownership and full-stop behaviour, preview limitations, local storage, source build/test commands, and links to project documentation. Downloads are explicitly future work; no invented repository URL, release badge, license grant, or supported installer is advertised.

README validation: full solution build has zero warnings/errors, all 23 Core tests pass, and all 52 local documentation links and anchors resolve. App code is unchanged; the earlier 78 App checks remain the latest App test result.

Repository: the user created public `datstma/sessions` and supplied its URL to connect this project. The remote was verified empty before the initial push; `origin` uses HTTPS and the branch is `main`. README includes the real clone command and issue tracker. The original source checkpoint and subsequent README/handoff changes are retained in Git history.

Remaining: select a project license with the user and decide the first release version and packaging approach. Before offering binaries, verify installation/launch on a clean Windows environment, runtime requirements, upgrade behaviour and library preservation, and document signing status and release notes. These are release-preparation items, not permission to publish releases automatically or commitments to an installer, CI provider, or release date.

Done when: the chosen repository and license are in place and a tested Windows release has accurate download/setup instructions. Keep completed README work distinct from the still-unavailable downloads. Screenshots can be added from representative app states when preparing the public listing.

## Completed baseline

| Date | Work | Evidence |
| --- | --- | --- |
| 2026-09-08 | Choose sidebar/detail interaction direction | User explicitly selected option 2; recorded in PRODUCT.md. |
| 2026-09-08 | Native UI preview: first-run creator, sidebar/details, draft editing, ordered app configuration, both lifetime settings, local persistence, light/dark themes | Solution built with zero warnings/errors; 9 Core and 9 App test cases passed; rendered screens reviewed. Native picker/restart validation remains SESS-001. |
| 2026-09-08 | Recoverable invalid-library and save-error handling | Tests cover preservation/retry; InvalidDataException is explicitly handled by the UI. |
| 2026-09-08 | Safe Session deletion (SESS-009) | Named confirmation, keyboard cancellation, save-before-removal, error retry, identity/order protection, JSON reload and referenced-file preservation; 26 total tests pass. |
| 2026-09-08 | Running-app picker (SESS-008) | Search, multi-selection, icons, duplicate/unavailable states, refresh and browse fallback; 34 regular tests and 1 native discovery check pass. |
| 2026-09-08 | Start/End Session and single-instance protection (SESS-002/003/004/005/007) | Ordered startup, owned-only graceful cleanup, main lifetime, rollback/retry/leave-open, active navigation and close choices; zero build warnings/errors and 74 tests/checks pass (18 Core, 48 regular App, 8 native). |

Import/export, compact mode, custom icons, website/settings actions, and other future possibilities remain uncommitted directions in PRODUCT.md and ARCHITECTURE.md. Add specific backlog items when a real need emerges, rather than treating that catalogue as the next implementation queue.
