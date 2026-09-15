# Sessions — Backlog

Last reviewed: **2026-09-15**. Scope comes from [PRODUCT.md](PRODUCT.md); technical constraints come from [ARCHITECTURE.md](ARCHITECTURE.md). [DEVELOPMENT_NOTES.md](DEVELOPMENT_NOTES.md) contains the current handoff and validation evidence.

This is a work record, not authorization to implement everything. The user explicitly requested safe Session deletion (SESS-009), a running-app picker (SESS-008), running indicators with window focus (SESS-012), and individual app launching (SESS-013), implemented below. Priorities remain an initial sequencing recommendation and can change with feedback.

## Working rules

- Keep IDs stable and never reuse them. Add new findings to the relevant item or assign the next ID.
- Status: **Open**, **In progress**, **Awaiting feedback**, **Proposed**, **Done**, or **Deferred**. In progress means someone is actually working on the item. Proposed means the solution or scope is not yet settled.
- Priority: **P0** must be resolved before real Session launching is enabled; **P1** is near-term validation or usability/reliability work; **P2** is a later improvement. Priority does not claim that the feature is already part of the first milestone.
- Record the source and whether a problem is reproduced. Add reproduction steps and expected/actual behaviour when user feedback reports a bug. Include relevant window size, theme, app, or lifetime mode when useful.
- Complete an item only when its acceptance criteria are met and validation evidence is recorded. Retain a short completed entry; update the product/architecture documents when decisions change.
- Do not add all speculative action types as committed work. Existing out-of-scope requirements remain in force.

## Suggested sequence

The 0.2.1 branding preview is published (SESS-020/024/025); advanced startup shipped
in 0.2.0 (SESS-023). The user selected accessibility/scaling
(SESS-010) on 2026-09-09; the implementation and automated review below are complete,
with native screen-reader/scaling trials awaiting feedback. The user approved the
first advanced-startup slice; SESS-023 now implements timing, concurrent launching,
readiness checks, and completion focus. Launch stages and configurable failure
policies remain later ideas. The supplied branding is now implemented, validated
and approved by the user (SESS-024), with OS-following themes and compact support retained.
Public-facing branding polish and obsolete-asset cleanup shipped in 0.2.1 under
SESS-025. Safer closing (SESS-026), saved-app icons (SESS-027) and draft-close
protection (SESS-006) are user-confirmed and published in 0.2.2 Preview. Invalid-field guidance
(SESS-011) is user-confirmed and published in 0.2.3 Preview. Installed-release checks and Actions maintenance
remain SESS-021; Actions runtime maintenance is validated under SESS-022. Broader
native checks stay under SESS-001, including UAC
cancellation. The user confirmed Playnite force quit under SESS-017 on 2026-09-10;
this does not establish graceful-only closing or unsaved-document safety. Settings
appearance preferences (SESS-031) are also implemented, validated and user-confirmed.
On 2026-09-15 the user reported wasted space, inconsistent text centering and
mismatched running statuses; SESS-036 implements the correction, which the user confirmed.

The user agreed the 1.0 feature set on 2026-09-15 (SESS-037, recorded in PRODUCT.md).
Suggested order, open to change: small wins first (SESS-039 window memory, SESS-038
duplicate, SESS-042 About) and SESS-041's backup-on-upgrade before any format change;
then format-changing features (SESS-040 optional apps, SESS-044 websites) with SESS-046
packaged-app research alongside; then SESS-043 export/import once item kinds settle, and
SESS-045 shortcuts. Finish with the SESS-041 format freeze, SESS-047 and SESS-048.

## SESS-001 — Review the first native UI with the user

**P1 · Awaiting feedback · Current UI validation**  
Source: the user began a hands-on trial on 2026-09-08. Discord launch independence (SESS-014), SRS start/stop (SESS-016), default full stopping (SESS-018), and Start menu discovery (SESS-019) are now confirmed. Earlier Start/End feedback identified tray-only close behavior (SESS-015) and SRS self-elevation tracking (SESS-016). These confirmations do not complete all checks below.

Check whether first creation, choosing Sessions, editing, app ordering, and the end-condition choice are discoverable without explanation. Confirm the Windows file picker (including cancel and multiple files), persistence after a real restart, and behaviour at the user's normal display scale. Ask whether scrolling hides an important choice and whether disabling sidebar browsing while editing feels natural. Confirm that the green Running button brings a normal and a minimized app window forward, including an app with several windows; verify the background-only explanation. Observe feedback rather than assuming a specific answer.

Done when: feedback is recorded with enough context to act on; bugs/improvements are linked to stable IDs; unperformed checks remain explicitly listed. A passing headless test alone does not complete the native picker/restart checks.

## SESS-002 — Implement ordered execution and both lifetime modes

**P0 · Done · First execution slice · 2026-09-08**  
Source: first-milestone requirements and the user's explicit request to activate Start/End Session.

Implemented in Core: captured run definitions, ordered startup, one active run, user-bound/empty Sessions, tracked-main exit monitoring, End during startup, duplicate-operation guards, and abort/rollback on failure. Windows operations stay behind ISessionProcessHost/ITrackedProcess; UI only presents snapshots. Core tests exercise ordering, main/supporting exits, multiple existing main instances, empty Sessions, startup failure, and concurrent/repeated operations. Native main-exit cleanup passes. SESS-018 now gates main-exit cleanup and startup rollback on save-work confirmation.

Follow-up user feedback (2026-09-09): empty Sessions must not offer Start Session.
The App now hides the start action and startup hints for empty definitions, keeps
editing/add-app guidance available, and guards command execution. Saving empty
definitions and Core's direct-call empty-run behavior remain supported.
Validated by a clean Release build, 45 Core and 90 App tests (22 opt-in native cases
skipped), including light/dark empty-to-populated selection and command guards.
Documentation link targets, AXAML parsing, and diff whitespace checks pass.

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

**P1 · Done · Implemented, validated and user-confirmed · 2026-09-09**
Source: code inspection found window close bypassed draft/save protection without
an active run. The user selected this backlog item on 2026-09-09 and subsequently
reported that it "works great", requesting a version bump, commit and push.

Implemented: changed new/existing drafts open Keep editing / Discard / Save before
window exit. Initial/default focus and Escape keep editing and restore focus.
Raw invalid values and nested app/startup options count as changes; navigation and
restored values do not. Unchanged drafts need no prompt. Save must succeed before
closing; failure retains the draft, shows the error and allows retry. Closing while
an ordinary save is in flight queues one close continuation; failure cancels that
intent. No second write or discard can interrupt the save. Active-run close choices
follow draft resolution, without implicitly stopping apps or opening a competing
main-exit prompt. Existing editor Cancel still explicitly discards without a prompt.

Evidence: clean Release build, 54 Core + 125 App tests pass (25 unrelated native
opt-in cases skipped). Fifteen new regressions cover new/existing/invalid/unchanged
and reverted drafts, nested settings/order, keyboard defaults/focus, successful and
failed writes, repeated close during a save, retry and active-run/main-exit ordering.
Both themes at 1440×900 and 640×480 rendered at 100/125/150/200%; long names and
failed-save errors remain readable with reachable actions. Review captures live in
ignored artifacts/draft-close-review. No real apps/library were used as fixtures;
physical-monitor/Narrator checks remain SESS-010. No autosave/crash recovery added.

Released in 0.2.2 Preview from v0.2.2/57cfafd. Workflow 34407352459 passes clean
solution/MSI builds, 54 Core and 125 App tests (25 opt-in skips). Downloaded asset
checksums, all 167 source entries and MSI version verify. The user explicitly
waived repeated Sandbox lifecycle checks; release notes disclose this limit.

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

**P1 · Awaiting feedback · Accessibility fixes and automated review implemented · 2026-09-09**
Source: user explicitly selected accessibility/scaling. Code inspection found missing
custom-item accessibility names, incomplete focus restoration, and an 860×620
minimum that restricted usable space at high DPI. New tests also reproduced initial
focus timing and close-prompt return-focus failures; both now pass.

Implemented: full list-item names/order/active state, main-app option names, picker
explanations, polite error/runtime/selected-app announcements, predictable initial
and post-editor focus, focus recovery after removal/reordering and close cancellation,
640×480 minimum with compact sidebar/margins, working-area-aware initial sizing,
and bounded scrolling for long runtime/error messages.

Evidence: clean full build; 23 Core and 78 regular App cases pass, including 20 new
accessibility cases; 20 opt-in native checks skipped. Keyboard tests cover creation,
cancel/save, reordering/removal, expanded options, lifetime choice, missing-main
validation, picker selection/scrolling, modal containment and focus restoration.
Automation peers expose updated names and help/live metadata. Light/dark renders
were reviewed with 50 Sessions, 30 configured apps, 80 picker entries, 120-character
names and 500-character descriptions. Main viewports: 640×480 at 100%, 150%, 200%
and 960×640 at 125%; picker 520×460 and long dialogs 640×480. Screenshots are under
ignored `artifacts/accessibility-review/`.

Remaining: hands-on Narrator/screen-reader announcement and focus review, native
Windows DPI changes/moving between monitors, and OS text-size settings. Computer
Use initialization succeeded but both native inventory calls failed with
“Computer Use native pipe is unavailable”; no native UI or screen-reader result is
claimed. Invalid-field guidance remains SESS-011 rather than being marked complete
by the existing main-app validation test.

Done when: primary actions remain reachable, labels/content do not obstruct controls, focus returns predictably, and state/errors are understandable without colour alone. Record tested configurations and any remaining limits; add regression tests for discovered failures.

## SESS-011 — Make invalid editor fields actionable

**P1 · Done · Implemented, validated and user-confirmed · 2026-09-09**
Source: code inspection found that blank names/paths disabled Save without explaining
why; startup feedback did not locate the app or field. The user selected SESS-011.

Implemented nearby explanations for Session/app names, executable paths, main/focus
choices and each startup value. Invalid rows remain identifiable with options
collapsed; unnamed entries have a display fallback. Review fields beside Save opens
the relevant section, selects the app, and focuses/scrolls the input and explanation.
Corrections, reordering and removal update the reported blocker. Invalid retained
numeric values remain reachable after switching their option off, without resetting
values or changing the choice. Nonblank offline paths remain saveable; launch-time
availability checks are unchanged. No new visual tokens, Core or schema changes.

Evidence: full Release solution build has zero warnings/errors; 54 Core + 141 App
tests pass (25 unrelated opt-in native checks skipped). Sixteen new cases cover
whitespace/untouched drafts, unselected collapsed app errors, keyboard review and
numeric clearing, retained hidden timings, main/focus target removal, reordering,
correction and saving an offline path without modifying the original definition.
Both themes at 1440×900 and 640×480, with 100/125/150/200% headless scaling, pass.
Visual review caught and fixed explanations scrolling beneath the compact footer;
regressions check that both the focused field and its explanation remain visible.
Captures are under ignored artifacts/validation-review. Documentation links and
git diff --check pass. The user confirms it "works like a charm!" after their trial.
Native screen-reader and physical-monitor checks remain SESS-010.

On 2026-09-10 the user requested a version bump, release build, commit and push.
Version 0.2.3 builds a local MSI with clean solution/MSI builds, 54 Core + 141 App
tests passing (25 opt-in skips), verified MSI version/checksum and release notes.
Installer lifecycle checks were not repeated. The user then explicitly requested
publication: 0.2.3 Preview is public from v0.2.3/a2e9781. Workflow 34410480722 passes
clean solution/MSI builds and all 195 regular tests (25 native skips). Downloaded
hashes, all 169 source entries and MSI version verify. Release notes disclose the
unrepeated lifecycle checks; public download links now point to 0.2.3.


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

**P1 · Done · Cleanup fix validated; Playnite force quit user-confirmed · 2026-09-10**
Source: user reports Playnite Desktop's tray icon disappears but the process remains in a broken state after End, with and without Force quit; Task Manager's End process finishes it.

Code inspection found that the previous hidden-window support broadcast WM_CLOSE to every top-level window, including internal helper windows. A native fixture reproduced the resulting damaged-but-running state and failed against the previous implementation. The fix limits normal-close targets to titled application windows with a system menu, excluding tool/internal windows, while allowing eligible hidden main windows. Process exit is still determined using the retained handle. Force-close fallback still terminates only the verified owned process; no new process-tree or name-based cleanup was added.

Regression evidence covers the internal-window failure, normal close of a hidden main window, and an app whose UI disappears while its process remains alive (NeedsAttention without force; actual exit with force). The agent did not launch/stop installed Playnite. A read-only snapshot of the current saved Playnite entry showed ForceClose=false; this does not establish the setting used in the user's earlier force-enabled trial. The runtime error/outcome was requested to distinguish that report if it persists. SRS successful start/stop is recorded under SESS-016.

Validation: zero build warnings/errors and 89 checks pass (19 Core, 51 regular App, 19 native); all 41 local documentation links resolve.

User confirmation (2026-09-10): after trying Settings, the user also tested closing
Playnite and reported that force quit "works just great". This closes the pending
Playnite force-quit trial. The report does not identify automatic per-app force quit
versus the targeted Force quit action, or independently confirm graceful-only closing,
unsaved-document safety or UAC behavior. No remaining Playnite error was reported.


## SESS-018 — Make full stopping the default with save-work confirmation

Historical policy shipped through 0.2.1. The user-approved SESS-026 change supersedes automatic force quit for every app; save-work confirmation and ownership guards remain.

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

**P1 · Done · 0.1.0 preview published · 2026-09-09**
Source: user wants a proper GitHub repository, starting with a clever public-facing README, and is considering public distribution with downloadable releases eventually.

Implemented: root README explains the app, examples, setup flow, ownership and full-stop behaviour, preview limitations, local storage, source build/test commands, and links to project documentation. It now includes the 0.1.0 preview download and setup instructions.

README validation: full solution build has zero warnings/errors, all 23 Core tests pass, and all 52 local documentation links and anchors resolve. App code is unchanged; the earlier 78 App checks remain the latest App test result.

Repository: the user created public `datstma/sessions` and supplied its URL to connect this project. The remote was verified empty before the initial push; `origin` uses HTTPS and the branch is `main`. README includes the real clone command and issue tracker. The original source checkpoint and subsequent README/handoff changes are retained in Git history.

License: the user selected GPLv3. The project uses GPL-3.0-only; the unmodified full text is in LICENSE, the README and App/Core metadata declare it, and the App copies it into build/publish output. Dependencies and third-party assets retain their own licenses.

Licensing validation: clean build and local Debug publish; output LICENSE hashes match the root file. 23 Core and 58 regular App tests pass (20 opt-in native checks skipped); 55 local links/anchors resolve. This does not establish dependency-license compatibility or readiness of a public binary release; those checks remain below.

Accepted on 2026-09-09: start at 0.1.0 with a shared numeric version, per-user self-contained Windows x64 MSI, local packaging script, and manually triggered GitHub Actions workflow that creates a draft from a matching version tag. Normal pushes/tags do not publish releases. Source archive, checksums, release notes, and notice collection are included. Details and validation requirements are in [RELEASING.md](RELEASING.md).

Implemented configuration: shared version and pinned SDK, separate WiX 7 project, fresh publish/intermediate staging, per-user payload generation with HKCU key paths and deterministic component GUIDs, detection-only running-app refusal, and draft-release workflow. NuGet/runtime notices and pinned supplemental Avalonia/Inter/MicroCom/Tmds/WiX notices are collected. The user explicitly authorized WiX 7 terms acceptance; `AcceptEula=wix7` resolves WIX7015. Local builds produce the unsigned MSI and checksum under `artifacts/releases/0.1.0/`; the tagged GitHub build supplies the public release.

Validation on 2026-09-09: full Debug and Release solution builds succeed with zero warnings/errors; 23 Core and 58 regular App tests pass (20 opt-in native checks skipped). Self-contained Release publishing, notice collection, payload generation, MSI compilation, and configured ICE validation pass. ICE91 is the only exclusion, documented for exclusively per-user installs; ALLUSERS is rejected. Raw MSI tables confirm the running-app check precedes changes and old-version removal is inside the install transaction. The tagged GitHub workflow also passes build, tests, packaging, artifact transfer, and draft creation.

Installer evidence: the final MSI's 274 extracted files match the publish payload and its SHA-256 sidecar verifies. Windows Sandbox passes install and installed app launch without shared .NET, running-app refusal, per-user registration, upgrade to a test-only 0.1.1 fixture with one remaining product, removed-file cleanup, downgrade rejection, uninstall/reinstall, and byte-for-byte library preservation. All 69 local links/anchors and workflow YAML/permission checks pass. The default Sandbox account was used, and real-app/UAC cleanup was not tested. Offline fresh MSI operations took about two minutes; the cause is not established.

Publication authorized on 2026-09-09: the user explicitly requested documentation, commit, and release publication. Dependency/asset inventory and notice coverage have been reviewed; the workflow provides matching Sessions source/build materials and WiX utility-action source. Reader-facing release notes disclose unsigned binaries, the default Sandbox account, and the remaining ordinary nonadministrator and installed real-app/UAC checks. Keep automated fixture coverage distinct from hands-on real-app/UAC verification (SESS-001).

Published: [Sessions 0.1.0 preview](https://github.com/datstma/sessions/releases/tag/v0.1.0), from commit `4ec492f` and tag `v0.1.0`, with MSI, matching Sessions/WiX source ZIPs, checksums, setup instructions, and validation limits. All downloaded hashes verify; every Sessions source archive entry matches the tag. The exact downloaded MSI passed a fresh Sandbox install/launch, running-app refusal, upgrade/downgrade, uninstall/reinstall, and library-preservation run before publication. See the current DEVELOPMENT_NOTES handoff for its hash and evidence. Remaining installed-release validation and Actions runtime maintenance are separate SESS-021/022 items.

Follow-up release: [Sessions 0.2.0 Preview](https://github.com/datstma/sessions/releases/tag/v0.2.0)
is published from tag `v0.2.0`/commit `23f8ec7`. The exact-tag workflow passes clean
solution/MSI builds and all 135 regular tests (22 opt-in native cases skipped).
Downloaded hashes and all 114 source archive entries verify. The exact MSI passes
0.1.0-to-0.2.0 Sandbox upgrade, installed launch, running-app refusal, downgrade
rejection, uninstall/reinstall, and library preservation. README and release notes
include the v2-library compatibility change and remaining preview limits.

Done when: the chosen repository and license are in place and a tested Windows preview has accurate download/setup instructions and disclosed validation limits. Screenshots can be added from representative app states when preparing the public listing.

## SESS-021 — Extend installed-release validation

**P1 · Open · Verification limits, not reproduced failures · 2026-09-09**
Source: 0.1.0 packaging checks used the default Windows Sandbox account. They do
not establish installation under an ordinary nonadministrator account or installed
real-app/UAC cleanup behavior. The preview release notes disclose these limits.

User confirmation (2026-09-09): the published 0.2.1 installer "works like a charm,
as well as the uninstall." This confirms installation and uninstall in the user's
normal environment. Account privilege, fresh install versus upgrade, operation
timings and real-app/UAC checks were not specified. Those remaining checks keep
this item open; installation/uninstall should no longer be described as tested
only in Sandbox.

For 0.2.2, the user explicitly requested skipping repeated Sandbox lifecycle checks.
The tagged build/test/package workflow, asset checksums, source contents and MSI
version were verified. This waiver does not establish the remaining ordinary-user
or installed real-app/UAC outcomes and does not close this item.

Done when: record ordinary-user install/update/uninstall and installed launch/end
checks, including the elevated helper and cancellation paths where applicable.
Keep test libraries and apps isolated. Coordinate hands-on app findings with SESS-001.
Fresh offline Sandbox MSI operations also took about two minutes; record behavior
in a normal Windows environment before deciding whether a delay investigation is needed.

## SESS-022 — Refresh GitHub Actions runtimes

**P2 · Done · Node 24 actions validated locally and on GitHub · 2026-09-10**
Source: [the successful 0.1.0 workflow](https://github.com/datstma/sessions/actions/runs/34292417455)
reported that checkout/setup-dotnet/upload-artifact/download-artifact v4 target
deprecated Node.js 20 and were forced onto Node.js 24. Builds, tests, artifact
upload/download, and draft creation still passed.

Done when: review and update the action versions, then validate the manual release
workflow without replacing the published 0.1.0 assets or moving its tag.

The successful 0.2.1 workflow (34398030125) repeats the forced Node.js 24/runtime
deprecation notices; application and MSI builds still have zero warnings/errors.
This remains CI maintenance, not a release-build failure.

Selected by the user on 2026-09-10. Updated checkout to v7, setup-dotnet to v6,
upload-artifact to v7 and download-artifact to v8 after reviewing official releases
and verifying that each action manifest declares Node 24 and accepts the existing
inputs. No SDK or application version change. Added an optional `validation_only`
dispatch input (default false) to exercise the full build and artifact transfer
without creating a release. Both modes check transferred file checksums before
draft creation; the download action also fails on artifact digest mismatch.
See [workflow validation](RELEASING.md#validate-workflow-maintenance-without-creating-a-release).

Validation: actionlint 1.7.12, action-input/runtime checks, 37 local documentation
links/anchors and all 34 stable IDs pass. The exact transfer-check script accepts
valid isolated files and rejects corrupted payloads or empty release notes. Full
local packaging and [hosted run 34500827015](https://github.com/datstma/sessions/actions/runs/34500827015)
pass with zero solution/MSI warnings or errors, 74 Core and 177 App tests (27 native
skips). The hosted workflow at 45e28aa builds immutable v0.4.0, transfers the files,
verifies all three MSI/source checksums and skips draft creation. Published tag,
release metadata, asset IDs and digests match the pre-run snapshot.

The Node 20/forced-runtime notices are gone. Download-artifact v8 emits a separate
non-blocking Node DEP0005 `Buffer()` deprecation; no warning suppression was added.
New draft creation was deliberately not exercised; its existing command remains
unchanged behind the validation-only gate. No release, tag, application version or
SDK change. Evidence and exact resolved action SHAs are in the handoff's run log.

## SESS-023 — Advanced startup timing, readiness, and completion focus

**P1 · Done · Released in 0.2.0 Preview · 2026-09-09**

Source: user requested timing/readiness, concurrent launching and completion focus,
then approved the proposed first slice: “let's do it as you have proposed.”

Implemented: collapsed Advanced startup, In order/All at once, Session pauses with
per-app overrides, launch-completed/process-running/window-appeared conditions,
bounded readiness waits, countdown messages, and completion focus on Sessions or a
chosen app. PRODUCT.md defines the exact timing rules and ARCHITECTURE.md the engine
boundaries. Defaults preserve existing startup behavior; already-open apps remain
unowned. Concurrent work retains late acquisitions, cancels waits on failure/End,
and awaits all groups before confirmed reverse-order cleanup. Repeated executable
paths remain serialized, with ambiguous/untracked repeats blocked.

Follow-up user feedback implemented: app options uses the selected app's display
name; advanced startup options uses the current Session name. Both follow draft
renames and wrap long names, with generic headings when the name is blank.
Heading follow-up validation: clean Release build, 45 Core and 90 App cases pass;
22 opt-in native cases skipped. Documentation links and diff checks pass.

Validation: Release solution build has zero warnings/errors; **45 Core + 90 regular
App + 17 native runtime cases pass (152 total)**. New coverage includes concurrent
late-acquisition cleanup/failure, ordered readiness/pauses, ignored concurrent pauses,
timeout/cancellation, repeated executable paths, legacy defaults, v2 round-trip and
invalid-setting preservation, keyboard/draft settings, captured completion focus,
focus suppression/cancellation and denied-focus feedback. Native fixtures verify
owned/pre-existing window readiness in both launch modes and all existing runtime
ownership/cleanup/self-restart protections. Five unrelated native launch/discovery
checks were not rerun. Light/dark compact layouts reviewed in
`artifacts/startup-review/`; real user apps/library were not test fixtures.

Limits: readiness observes a tracked process/window, not completed loading/sign-in;
the timeout starts after Windows returns acquisition and cannot dismiss UAC. Native
foreground permission/refocusing the user's real apps remains a hands-on check.
Saved libraries now use version 2 (version 1 reads without rewriting); the public
0.1.0 build cannot read v2, preventing silent disregard of startup settings.

Candidate later additions for discussion: launch stages/dependencies, optional-app
failure policies, progress/history and preflight checks, window placement, reversible
audio/settings changes, local Session shortcuts, and import/export/duplication.
These are ideas rather than authorized implementation scope.

Completion criteria met by the tests and documentation above; published in 0.2.0.

## SESS-024 — Apply the supplied branding and visual direction

**P1 · Done · Released in 0.2.1 Preview · 2026-09-09**

Source: user supplied branding/ and AGENTS.md UI instructions, then approved adapting
the mockups to existing screens with OS-following themes, readable text and delegated
token/asset completion. Prototype-only history, Recently added and other new features
were explicitly excluded. The initial [review](BRANDING_REVIEW.md) remains historical.

Implemented canonical JSON/generated theme exports, bundled Manrope fonts/license,
Windows icon, branded shell/hero/app cards and shared editor/picker/confirmation styles.
End confirmation presents owned apps and already-running apps separately. Compact
640×480 support, existing validation/advanced settings, active-run editing protection,
cleanup/recovery, safe confirmation focus and empty-Session behaviour are preserved.

Evidence: Release solution build with zero warnings/errors; 45 Core and 94 App tests
pass, with 22 opt-in native tests skipped. Both themes rendered at 1440×900 and
640×480, plus existing 125%/150%/200% scaling/keyboard/automation cases. New assertions
cover real font loading, theme changes, contrast, focused input/placeholder appearance,
compact title/action geometry, picker selection and ownership confirmation/Escape.
Visual review corrected control-state and compact-layout issues. Screenshots are in
ignored artifacts/branding-review. Native screen-reader and monitor/text-scaling
limits remain SESS-010; this does not claim installed/native validation or a release.

## SESS-025 — Finish public-facing branding and remove obsolete assets

**P1 · Done · Released in 0.2.1 Preview · 2026-09-09**

Source: the user requested completion of public-facing polish and old-asset cleanup.
Code inspection found the Avalonia template icon, Inter package/registration, no
explicit MSI registration icon and no public UI screenshots. The existing Manrope
font resource already names Segoe UI as fallback.

Scope: README logo and actual UI screenshots with accurate source/release wording,
reproducible screenshot capture, installer/shortcut icon from the published app,
removal of unused assets and their obsolete notice, and packaging/documentation QA.
This does not publish a release or change startup/cleanup behavior.

Implemented: README tile and theme-aware screenshot, a four-image gallery of actual
UI renders, and `scripts/Update-PublicScreenshots.ps1` using isolated fixture data.
The screenshots explicitly preview source newer than the 0.2.0 download. MSI
Installed apps and Start menu icons reference the branded published executable.
Removed the template ICO, Inter package, production/test registration and obsolete
supplemental Inter notice; Manrope and its Segoe UI fallback remain.

Evidence: Release solution and MSI builds have zero warnings/errors; 45 Core and
94 App tests pass (22 opt-in native cases skipped). Four public 1440×900 images
were visually reviewed; the suite also covers both themes at 640×480 and scaling.
All 272 extracted MSI payload files match the fresh publish; MSI icon bytes match
the published executable, shortcut/Installed apps icon references verify, removed
package/notice files are absent, Manrope OFL and the checksum verify. Documentation
links, HTML image references, unchanged theme generation and whitespace checks pass.
Evidence is in ignored `artifacts/public-branding-qa/`. Native installed icon/UI,
accessibility and real-app checks remain SESS-010/021; no installation or release
was performed. The local MSI is a development rebuild, not an update to public 0.2.0.

Follow-up: the user selected 0.2.1 for the branding refresh and requested a stronger
app-like GitHub README. Shared application/MSI version is now 0.2.1, with draft
release notes. The README has a centered logo/tagline, theme-aware hero screenshot,
feature overview, inline editor/End screenshots and an expandable theme comparison.
Public download links still point to the published 0.2.0; 0.2.1 is labelled upcoming.
GitHub Markdown rendering preserves the layout elements, with all local anchors,
links and eight image/source references verified. The versioned 0.2.1 solution/MSI
builds have zero warnings/errors; 45 Core and 94 App tests pass, 22 native cases
skip. All 272 extracted files, icons, notices, checksum and version metadata verify
in ignored `artifacts/version-0.2.1-qa/`. The user requested commit/push of this
checkpoint; tagging and release publication remain separate.

Published on the user's request: [0.2.1 Preview](https://github.com/datstma/sessions/releases/tag/v0.2.1)
from tag v0.2.1/commit `863f75d`. Workflow 34398030125 has clean solution/MSI builds
and all 139 regular tests passing (22 native skipped). Downloaded checksums, all
160 source entries, 272 payload files and 42 notice/metadata files verify. Exact
MSI testing passes 34 assertions plus baseline installation, including 0.2.0
upgrade, installed launch, removed Inter files, icon registrations/decoding,
running-app refusal, downgrade rejection, uninstall/reinstall and library
preservation. The icon lookup harness was corrected and resumed; no product fix
was needed. The default Sandbox account was used and the Sandbox is stopped.
Native accessibility and ordinary-user/real-app/UAC limits remain SESS-010/021.
README and gallery now describe and link to the published refresh.

## SESS-026 — Preserve unsaved work when ending a Session

**P1 · Done · Implemented, validated and user-confirmed · 2026-09-09**

Source: after using 0.2.1, the user added Word to a Session, started it, typed text
in a document and ended the Session. They report that Sessions forcibly terminated
Word with the modified document open. Word version, save-prompt visibility, timing,
theme and window size were not supplied. Not independently reproduced with Word.

Code inspection confirms the mechanism: SessionRunner supplies a three-second
close timeout; WindowsTrackedApp enables force; WindowsProcessCleanup posts
WM_CLOSE, waits for process exit and terminates a lingering verified owned process.
No unsaved-document or save-dialog check gates termination. This follows the
published 0.2.1 SESS-018 policy, but exposes a safety gap not covered by its earlier
successful tray-app stopping trials.

Research: [WM_CLOSE](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-close)
allows an application to defer closing for user confirmation. Word exposes a
[Document.Saved property](https://learn.microsoft.com/en-us/office/vba/api/word.document.saved),
which could support app-specific detection. Open/temp files are not a reliable
general signal of unsaved work; unknown status must not imply safe termination.

User approved the general change on 2026-09-09. Implemented: normal close preserves
apps still running after the grace period; named recovery rows support bring-forward
and separately confirmed force quit for one owned app in the current run. Retry End
and explicit leave-open remain. Background observation finishes after remaining apps
exit; cancelling a save prompt does not automatically retry closing. Per-app
AllowForceQuit is an explicit opt-in. V1/v2 load with it disabled; saving writes v3
so older builds reject the policy rather than silently ignoring it. No name/temp-file
heuristics. PRODUCT and ARCHITECTURE hold the current specification.

Evidence: Release build clean; 54 Core and 101 headless app tests pass, covering
ownership, stale requests, targeted force, failure/main-exit/late-startup policy,
format migration, safe keyboard defaults and both themes at compact/full size.
The final combined app run passed 121 cases (101 headless + 20 isolated native),
with 5 unrelated opt-in checks skipped. This includes a save-dialog fixture surviving
the real three-second timeout, Cancel without re-prompting, and Save/Discard with
automatic completion. Exact-process helper normal/force checks and identity rejection pass;
commands and verification limits are recorded in DEVELOPMENT_NOTES. Published 0.2.1 remains unchanged.

User feedback after the implementation: "works great!" confirms the overall
safer-closing flow. Exact app, Save/Discard/Cancel steps and UAC outcomes were not
specified. The detailed Word/elevated-app trial matrix is not independently verified;
retain those limits under SESS-001 rather than claiming native helper tests exercised UAC.

Released in 0.2.2 Preview; shared release validation is recorded under SESS-006.

## SESS-027 — Show executable icons in saved app lists

**P2 · Done · Implemented, validated and user-confirmed · 2026-09-09**

Source: after confirming the closing change, the user asks to replace app-name
initials with the apps' icons. Code inspection found that the picker already extracts
icons, while saved app cards only show the initial.

Implemented: app cards load their executable artwork asynchronously through
a bounded, case-insensitive in-memory cache and retain the initial for missing,
unreadable or malformed icons. Late results cannot overwrite rebound/detached
controls; decoded bitmaps are released when replaced/detached. Names, Session
initials, running controls and execution ownership remain unchanged. No artwork is
persisted, and obtaining an icon never launches an app. Existing branding tokens
are reused; PRODUCT/ARCHITECTURE define the behavior and lifetime boundaries.

Evidence: clean Release build; 54 Core and 110 App tests pass (25 unrelated native
opt-in checks skipped). Nine icon cases cover actual executable artwork in a saved
card, unavailable/corrupt/denied fallback, shared background reads, stale results
and detach/reattach cleanup. All nine pass with 100/125/150/200% rendering coverage;
both themes at 1440×900 and 640×480 reviewed. No real apps were launched. Headless
rendering does not establish physical-monitor behavior. The user confirmed
"works great, looks great!" and requested commit/push with SESS-026. Released in
0.2.2 Preview; shared release validation is recorded under SESS-006.

Public screenshot follow-up requested after release: README/gallery now show the
0.2.2 app icons in both themes and current End confirmation. The capture script takes
explicit installed executable paths, reads icons only, retains isolated sample data,
and requires all four icons to load. Clean Release build; 54 Core + 125 App tests
pass (25 opt-in skips), plus four real-icon capture cases across both themes and
full/compact sizes. Public images and compact detail views were visually reviewed;
the editor render is unchanged. No production behavior or release asset changes.

## SESS-028 — Bespoke startup support for gaming and simulation utilities

**P2 · Proposed · Research first; implementation scope to follow · 2026-09-10**
Source: the user expects many Sessions users to be gamers with utility-heavy DCS,
Star Citizen and similar setups. They requested a backlog item for app-specific
startup support: determine how each utility can start with the settings wanted for
a particular Session. This audience expectation is a product hypothesis, not usage
data; Sessions retains its general Session-centric model and other use cases.

Initial research targets, preserving the user's list:

- TrackIR
- Tobii Experience
- Tobii Game Hub
- opentrack
- GameGlass
- VoiceAttack
- DCS-SRS (DCS SimpleRadio Standalone)
- MSI Afterburner
- MOZA Cockpit
- MOZA Pit House — likely the MOZA Racing application the user meant; the
  [official product page](https://mozaracing.com/pages/pit-house) confirms the name.

Launch flags and automation capabilities are **not yet researched or verified** for
these apps. Do not assume every app supports command-line profile selection, or
that similarly named products share an interface.

Research deliverable: a capability matrix for each exact product/version, with
primary-source links, date checked, supported executable/entry point and precise
argument syntax/quoting. Investigate Session-specific profile/preset selection,
starting minimized/in the tray, enabling tracking/listening, or choosing a connection
where relevant; these are questions to verify, not promised features. Record required
working directory, prerequisites/elevation, single-instance behavior, whether options
apply only at fresh launch or also to an already-running instance, and any persistent
or shared configuration effects. Distinguish documented support, independently tested
behavior, community reports, unsupported capabilities and unresolved questions.
Where no supported flags exist, record that outcome; a documented URI/API or other
alternative can be assessed separately rather than inventing switches or silently
editing global configuration. Do not contact vendors without user authorization.

Use isolated profiles/configuration for eventual hands-on trials and record the exact
version, command, expected result and observed result. Identify what can be expressed
through today's per-app Arguments and Working directory fields and what would require
additional behavior. Propose a small first implementation slice based on useful,
verified capabilities; the app order above does not establish implementation priority.

The intended follow-up is optional, understandable per-app settings in a Session
(e.g. choosing a verified supported profile), while retaining manual arguments for
other apps. Preserve user-entered arguments and define conflict handling before adding
helpers. Existing process ownership, already-running-app treatment, launch independence
and safer closing remain mandatory. App-specific support must not implicitly enable
force quit or mutate another running Session/app's settings. Keep integrations outside
Core's platform-neutral execution model; no plugin framework is authorized by this item.

Done when: every target has a sourced capability/limitation record, candidate options
have reproducible validation evidence, and an agreed first slice has been implemented,
documented and tested. Research completion alone does not mean bespoke support shipped;
split approved implementations into linked stable backlog IDs as scope becomes clear.
This request adds future work; it does not start research across all apps or authorize
implementation in the current turn. No product behavior or architecture has changed.

## SESS-029 — Add plugin support, with Steam as the first plugin

**P2 · Done (bundled slice) · Validated and accepted for 0.5.0 Preview · 2026-09-10**
Source: the user initially requested backlog-only plugin support, with Steam first,
then selected implementation after SESS-022 on 2026-09-10. Proceeded with a stated
bundled-first assumption after an optional third-party-loading question received
no answer. Steam is the first consumer; Hue (SESS-033) and Home Assistant (SESS-034)
remain later work. A marketplace and automatic plugin updates are outside scope.

Implemented: a portable plugin contract with launch-only defaults, optional presence
and verified opt-in cleanup, explicit bundled registration,
API/settings compatibility checks, per-run settings capture and isolated discovery
failures. Settings lists versions, enable/disable and supported fields, with separate
Apply/reset/retry and atomic local storage. Unknown plugin references and settings
survive saving. Missing/disabled/incompatible plugins fail preflight before audio
or ordinary app launches. Cleanup ownership requires the saved per-app opt-in and
verified lifetimes; provider presence alone grants none. Executable
Sessions retain their existing behavior. The library reads v1–v5 and now writes v5;
published 0.4.0 and earlier reject v5. See [product behavior](PRODUCT.md#plugins-and-steam),
[contract and trust boundary](ARCHITECTURE.md#bundled-plugins) and [plugin guide](PLUGINS.md).

The supported lifecycle is stateless, trusted, in-process bundled services. There
is no third-party loader, background initialization, hot reload or plugin sandbox.
Additional action kinds and external loading need a later contract decision.

Validation: the expanded implementation has a clean full Release build, 114 Core
passing tests, 200 App tests (29 opt-in native skips) and a native isolated cleanup
test. Visual evidence is in DEVELOPMENT_NOTES.
The user confirmed 3DMark launching; running detection and optional closure were
added from their feedback. The user subsequently tested the expanded behavior and
requested publication of 0.5.0; specific cleanup edge cases remain unverified.
Third-party loading remains future work.

## SESS-030 — Steam plugin: launch games as part of a Session

**P2 · Done (first Steam slice) · Validated and accepted for 0.5.0 Preview · 2026-09-10**
Source: the user requested Steam first, allowing Steam games alongside ordinary
Session apps, and selected plugin implementation on 2026-09-10.

Implemented: **Add app → Plugins** discovers local Steam app manifests across
libraries, retaining numeric IDs and display names. Steam's installation is found
from the current-user registry or an explicit Settings folder. Discovery reads
local files without login, downloads or modifying Steam. Missing drives, incomplete
installs and malformed metadata have recovery messages. Icons use the existing
initial fallback. No custom launch arguments or Steam preference changes are made.

The Windows adapter requests `steam://run/<appid>` through the installed client.
Startup ordering/pauses and failure policies apply; duplicate targets are rejected
and same-plugin launches serialize. Provider-reported running state updates the app
card and suppresses duplicate individual requests. The user confirmed 3DMark launches
on 2026-09-10, reported the missing running indicator and requested optional closure.

Implemented follow-up: per-app **Close when this Session ends**, default off, plus
Steam registry presence. Opted-in startup uses a baseline and bounded matching Steam
log events with exact Windows identities; already-open apps, the shared Steam client
and unknown ownership stay independent. Normal close and existing confirmed recovery/
force-quit flows apply to retained identities only. Late/external launchers can still
require manual management. Main-app lifetime, focus and readiness remain unavailable.

Validation: fixture tests cover local discovery and activation, log parsing,
pre-existing/old/outside-path/other-session/reused identities, cancellation and
observation failure, persisted consent and Core consent enforcement. Native tests
using synthetic logs/hidden helpers prove exact new-process closure and pre-existing
process survival. Read-only Steam 10.96.30.42 discovery found 35 available apps and
reported one unavailable library; the real 3DMark Running flag was observed. The agent
did not launch or close real Steam apps. See [limits and remaining real-app retry](PLUGINS.md#validation-and-remaining-trial).
The user reported that the expanded behavior works well overall, then identified a
stale red launch acknowledgement despite successful green running detection. Fixed:
manual launch acknowledgements use neutral styling and clear on detected running,
including after the grace period expires; actual failures remain distinct. Both-theme
compact/full-size regression coverage is recorded in DEVELOPMENT_NOTES. Specific
real-app cleanup edge cases remain unconfirmed. The user confirmed the launch-feedback
fix works on 2026-09-10. Individual app closing is tracked separately in SESS-035.

## SESS-031 — Settings entry point and application preferences

**P2 · Done (appearance slice) · Implemented, validated and user-confirmed · 2026-09-10**
Source: the user requested theme/plugin/scaling preference suggestions, then selected
SESS-031 for implementation on 2026-09-10.

Implemented slice: a Settings button in the sidebar and empty-library header opens
one separate, modeless window. It preserves Session drafts and active-run controls
with existing editor/close guards. System/Light/Dark, interface size 100/110/125/150%
and text size 100/110/125% have an isolated preview, explicit Apply, local persistence
and reset. Enlargement fits the available main/picker viewport; Settings keeps
standard interface spacing so reset remains reachable. All typography derives from
branding tokens. See [Settings behavior](PRODUCT.md#application-settings) and
[preferences architecture](ARCHITECTURE.md#application-preferences-and-appearance).

The separate preferences file uses validated, versioned JSON and temporary-file
replacement. Load errors leave the app usable with a recovery warning; retry leaves
the file untouched. Explicit reset backs up an existing unreadable/unsupported file
before replacing it. Failed saves retain applied appearance and draft choices;
reset never changes the Session library. In-flight operations prevent window close.

Plugin preferences were subsequently implemented under SESS-029/030; their validation
and remaining Steam trial are tracked there. Startup/tray/window memory, notifications, About/support,
arbitrary font-family selection and diagnostic export remain discussion candidates,
not implemented scope. Native Narrator, physical DPI/monitor changes and Windows
text-size trials remain SESS-010. The user confirmed Settings "works like a charm"
on 2026-09-10. This confirms the implemented appearance slice; it does not establish
specific native accessibility/monitor configurations or complete the future candidates.

Validation: clean full Release build with zero warnings/errors; 74 Core tests and
177 App tests pass (27 opt-in native cases skipped). Twenty-four new cases cover
persistence/reload, malformed/unsupported/denied reads, backup/reset, atomic-save
failure, serialization/retry, keyboard choices/focus, live System-theme preview,
unapplied-choice discard and editor/run/close guards. Both themes at 1440×900 and
640×480, plus 125/150/200% headless rendering, pass. Captures reviewed under ignored
artifacts/settings-review include enlarged text/interface, preview, editor footers
and compact active-run controls. Visual review found a clipped New Session label at
125% text; the button label now wraps and a regression checks it. Automated checks
used isolated fixtures without accessing the user library, launching real apps or
changing OS preferences.

Published in 0.4.0 Preview from v0.4.0/ea9f7b9. Workflow 34475353569 passed clean
solution/MSI builds and 74 Core + 177 App tests (27 opt-in native skips). Downloaded
checksums, all 184 tagged source entries, WiX source/license and MSI ProductVersion
0.4.0 verify. Repeated Sandbox lifecycle checks were omitted under the maintainer's
existing instruction and disclosed in the release notes. Publication was explicitly
requested on 2026-09-10; the release also records the Playnite confirmation in SESS-017.

Settings guidance retained from the original proposal (plugin controls now implemented under SESS-029/030):

- Plugins: show installed/bundled plugins, status, version, enable/disable and each
  plugin's own settings when supported. Depends on SESS-029/030; do not display
  nonfunctional plugin switches before the plugin lifecycle exists. Explain restart
  requirements and handle changes during active runs without losing ownership or
  invalidating the captured run. Game/profile choices for a particular Session
  stay with that Session rather than becoming global plugin defaults accidentally.

- General: remember the last selected Session and window size/position. Optionally
  start Sessions with Windows or start it minimized; launching Sessions must not
  automatically start a saved Session. Tray/minimize-on-close behavior needs its own
  explicit design and must preserve existing draft and active-run close protection.
- Notifications: choose whether startup completion or failures show notifications,
  if notifications are introduced. Keep errors and outcomes visible inside Sessions.
- About and support: version, license, release notes and an action to open the local
  data folder. Consider a user-reviewed diagnostic export separately if useful;
  avoid automatically sending paths, Session contents or logs anywhere.

## SESS-032 — Choose audio output and input devices per Session

**P2 · Done · Implemented, validated and user-confirmed · 2026-09-10**
Source: the user proposed Session-specific sound output/input, then selected SESS-032
for implementation. The editor now saves independent endpoint choices, each defaulting
to Leave unchanged, with discovery/refresh and retained unavailable selections.

Start applies Windows ordinary and communication defaults before launching apps.
End restores after process-close attempts, including when apps remain open; Finish
and Leave-and-close also restore. Partial/cancelled/failed startup rolls audio back.
Later differing choices are preserved, with console/multimedia treated as linked
roles. Unresolved restoration keeps the run active for explicit retry. See
[product behavior](PRODUCT.md#session-audio-devices) and
[audio lifecycle](ARCHITECTURE.md#session-audio-lifecycle) for the authoritative policy.

Validation: clean full Release build; Core lifecycle/persistence regressions and
headless editor tests cover both themes, compact/reference layouts, scaling,
keyboard selection, draft protection, unavailable devices, partial failures,
startup cancellation, later defaults, apps remaining open and restoration retry.
Native checks enumerated active Windows devices, reapplied current defaults and
briefly switched to an alternate output through the real runner, verifying all
three original output roles were restored. This exposed and verified the fix for
Windows coupling console/multimedia writes. Counts and commands are in the handoff.

The user confirmed the feature works on 2026-09-10 and requested release publication.
Broader validation limits: real gaming/voice apps, physical and virtual device playback
and microphone behavior, and physical disconnect/reconnect. No audio was played or
recorded during automated checks. The Windows default setter is undocumented;
crash restoration, per-app routing, volume and effects are outside this slice.
Library v4 preserves audio settings and is unreadable by published 0.2.3 and earlier.
Global appearance preferences are tracked separately in SESS-031. Published in 0.3.0 Preview
from v0.3.0/9bc12cb. Workflow 34414859067 passed clean solution/MSI builds and
74 Core + 153 App tests (27 opt-in native skips). Downloaded hashes, all 174 tagged
source entries, WiX source/license and MSI ProductVersion 0.3.0 verify. Repeated
Sandbox lifecycle tests were omitted under the user's instruction and disclosed.

## SESS-033 — Philips Hue plugin

**P2 · Proposed · Depends on SESS-029 · 2026-09-10**
Source: the user requested Philips Hue support in the plugin backlog. This is
future work; no integration research or implementation is authorized now.

Candidate outcome: choose lighting scenes or light settings for a Session, such as
a gaming or streaming setup. When selected, verify supported local connection,
pairing/authentication, discovery and device/scene identities before choosing an API
or dependency. Keep connection credentials separate from shareable Session definitions.
Coordinate connection/preferences UI with SESS-031.

Define when actions run, how unavailable bridges/devices and partial failures affect
startup, and what End, cancellation and app close do. Decide whether ending restores
captured state, applies an explicit end scene or leaves lighting unchanged; do not
assume ownership of shared lights or overwrite later user/automation changes.
Coordinate with SESS-034 if the same lights are reachable through Home Assistant.

Done when: an agreed first slice can save/reload Session lighting choices, execute
them through the plugin, handle missing/disabled plugins and connection failures,
and demonstrate documented start/end behavior with tests and device validation.
No bridge model, API version, cloud dependency or restoration policy is selected yet.

## SESS-034 — Home Assistant plugin

**P2 · Proposed · Depends on SESS-029 · 2026-09-10**
Source: the user requested Home Assistant support in the plugin backlog. This is
future work; no integration research or implementation is authorized now.

Candidate outcome: connect to the user's Home Assistant instance and choose explicit
Session actions, such as activating a scene or running a script. Verify supported
interfaces, authentication, discovery and stable action/entity identities when selected.
Prefer local operation and keep credentials outside shareable Session definitions;
coordinate connection/preferences UI with SESS-031.

Agree a small initial set of actions and explain their effects before execution.
Define ordering, timeouts, unavailable instances/entities, partial failures and retry
behavior without duplicating actions unintentionally. Specify optional end actions
separately: an arbitrary scene/script may have effects that cannot be inferred or
reversed. Respect changes made by users and other automations; do not claim automatic
rollback of arbitrary actions. Coordinate overlapping Hue control with SESS-033.

Done when: the agreed Session actions can be selected, saved/reloaded and executed
through the plugin, with clear missing-plugin/connection recovery and tested,
documented start/end semantics against a Home Assistant instance. No API, SDK,
entity scope or automatic restoration policy is selected yet.

## SESS-035 — Close an individual running app

**P2 · Done · Validated and accepted for 0.5.0 Preview · 2026-09-10**
Source: after confirming the plugin status-text fix, the user requested an intuitive
Close option beside each running app.

Implemented: **Close…** beside ordinary and Steam running statuses opens a named-app
save-work confirmation with Cancel initially focused and Escape cancellation.
Confirmed normal closing targets the captured existing copies, including apps opened
outside Sessions, independently of close-on-End preferences. Later copies and other
apps remain open. No automatic force quit is used. Startup/stopping, competing
confirmations and in-flight launches retain guards; the Session continues, with the
existing main-app exit prompt when applicable. Unverifiable Steam identities explain
that the app needs closing from its own window/Steam. See
[product behavior](PRODUCT.md#closing-an-individual-app) and
[identity boundaries](ARCHITECTURE.md#individual-app-closing).

Validation: clean full Release build; 119 Core and 216 App tests pass (31 opt-in native
skips). Five new Core cases cover fixed identities, cancellation, refusal/failure and
Steam active-log parsing. Twelve UI cases cover confirmation/keyboard/focus, guards,
active main/supporting apps and both themes at 1440×900/640×480 with 125/150/200%
headless scaling. Two separately enabled native helper cases verify actual normal
closure of existing apps while preserving later copies and other executable paths,
including synthetic Steam correlation. Cancel closes nothing. No real Steam app was
launched/closed by the agent. Real 3DMark closing, save prompts and physical desktop
accessibility remain for user trial. No release/commit/push requested for this slice.


Follow-up user trial: closing partly worked, but Close was taller than Running and
close feedback remained stale as app state changed. Fixed the row controls to share
the existing 44px minimum and centered alignment. Separate close feedback now clears
on observed NotRunning; successful requests wait in neutral text, refusals remain
red until resolved, and unknown status does not falsely count as exit. Unrelated
launch/focus errors remain separate. Eight new both-theme ordinary/plugin cases
exercise delayed exit and subsequent restart; existing layout cases now assert equal
control height/top at compact/reference sizes and 125/150/200% scaling. Full Release
build is clean; 119 Core and 224 App tests pass (31 opt-in native skips). No process
adapter or ownership changes; native close checks were not repeated. Awaiting the
user's retry of this feedback/alignment correction.


Release authorization: the user retried the correction, reported it is better, and
requested commit/push/publication on 2026-09-10. SESS-029/030/035 form the 0.5.0
Preview slice. This is positive overall feedback, with specific real-app cleanup
and native accessibility limits retained in the release notes.


Published SESS-029/030/035 in 0.5.0 Preview from v0.5.0/fecc5b0. Workflow 34516646243
passed clean tagged builds, 119 Core and 224 App tests (31 opt-in native skips).
Downloaded checksums, all 214 source archive entries, WiX source/license and MSI
ProductVersion 0.5.0 verify. Release notes retain the documented Steam/native limits
and omission of repeated Sandbox lifecycle checks. Publication confirmed 2026-09-10.


Project-stage follow-up, 2026-09-10: the user approved relabelling current 0.5.0 as
Alpha and documenting Alpha/Beta/1.0 criteria. This changes release metadata and
current documentation, preserving published tag/assets and historical Preview
releases. UI inspection found Preview only in the appearance-preview controls.

## SESS-036 — Use space efficiently and align text consistently

**P1 · Done · Implemented, validated and user-confirmed · 2026-09-15**
Source: user feedback with screenshots of 0.5.0 Alpha at 100% Sessions appearance on
their normal Windows scaling (about 1112×815 logical): opening the app "always" needs
scrolling, text in fields is not naturally or consistently centered, and Running and
Running · no window boxes look different. The editor screenshot also showed a separate
square behind the Session audio chevron on hover.

Reproduced headlessly at the user's logical size. Text boxes and number fields drew
their text 5.4px above center, Close… 4.8px and other buttons 1.4px: Sessions raises
controls to 44px but never set content alignment, so Fluent's stretched content
pinned text to the top. Combo boxes sat 1.4px high from Fluent's uneven padding. The
background-only status was a plain muted row rather than the green chip PRODUCT
describes. The chevron hover square came from a Fluent pointer-over style.
Measured scroll overflow at the 1120×800 default: 3-app detail 101px, 4 apps 197px,
new named draft 147px.

Implemented: centered button labels and single-line field/combo/number text (multi-line
description stays top-aligned); one status chip shape with green running and neutral
passive variants; transparent chevron hover. Detail view: description joins the hero
title column, the startup summary moves under the apps heading, the default
already-open start hint is dropped (the ownership card already says it) while the
active-run/opening blockers remain, the role line shows only for the app that ends the
Session, the end condition leads the first explainer paragraph, and container spacing
replaces trailing margins. Hero, detail and editor margins use 20px; the editor title
uses the 22px title size, audio/advanced sections sit together and the ending note joins
the end-condition group. Two layout tokens orphaned by this change were removed and exports
regenerated; no new tokens. No behaviour, persistence, ownership or Core changes.

After: at 1120×800 and the user's size, 2–4 app Sessions fit without scrolling; 6 apps
overflow 150px (was 389px). Every control of a new named draft is visible at 1120×800,
with only its closing note possibly a few pixels below; it fits fully at the user's size.
At 1440×900 up to 4 apps and a new draft fit.

Validation: clean full Release build with zero warnings/errors; 119 Core and 230 App
tests pass (31 opt-in native skips). Six new cases check vertical centering across
detail, expanded editor options and picker (light 1440×900 at 100% text, dark 640×480 at
125% text), shared chip appearance for window/background/unknown statuses in both themes
and default-window fit for a three-app Session and a new draft's last choice. Existing
layout, scaling, keyboard, close-control alignment and theme suites pass. Captures in
both themes at reference/compact sizes and 125/150/200% scaling were reviewed; public
README screenshots were not regenerated. Native monitor/text-size checks remain SESS-010.

User confirmation (2026-09-15): after running the source build with their library, the
user reported it "looks better". This confirms the overall layout/alignment correction in
their setup; specific window sizes, themes and scaling were not stated.

## SESS-037 — Agree the 1.0 feature set

**P1 · Done · Agreed with the user · 2026-09-15**
Source: PRODUCT.md defines Beta as "the agreed 1.0 feature set is complete", but no
set was agreed. After SESS-036 the user asked for a draft, which framed 1.0 as a
dependable everyday loop (build, start, see, end safely) rather than the whole backlog.

The agent proposed five must-haves (duplicate, window memory, optional apps, saved-data
commitment, About) and five undecided candidates. The user kept the must list as
drafted; added export/import, website items, starting from a shortcut or command line
and Microsoft Store/packaged apps to 1.0; chose to decide code signing near Beta; and
placed gaming utility integrations (SESS-028) after 1.0.

The agreed set, Beta gates and exclusions are recorded in
[PRODUCT.md](PRODUCT.md#10-feature-set). Features are split into SESS-038–046, the
signing decision into SESS-047 and Beta readiness checks into SESS-048. Size estimates
from the draft (S/M/L) are rough agent guesses, not commitments.

## SESS-038 — Duplicate a Session

**P1 · Open · 1.0 feature (S) · 2026-09-15**
Source: SESS-009 deferred duplication; the user included it in 1.0 via SESS-037.

The copy needs a new Session ID and new app IDs, with main-app and completion-focus
references remapped to the copied apps. Plugin references, audio choices and startup
settings copy as saved values; running state, ownership and any active run never copy.
Decide the copy's name, placement and whether it opens in the editor before saving.
Duplicating an active Session's saved definition must not affect its run.

Done when: a duplicated Session saves, reloads and starts independently, with tests for
identity remapping, plugin/audio settings and keyboard access in both themes.

## SESS-039 — Remember window size, position and last selected Session

**P1 · Open · 1.0 feature (S) · 2026-09-15**
Source: SESS-031 candidate; the user included it in 1.0 via SESS-037, following the
SESS-036 report that the app opens with too little usable space.

Restore the last normal size, position and maximized state, and reselect the last
Session. Never restore off-screen or below the 640×480 minimum after monitor, DPI or
resolution changes; fall back to the current working-area sizing. Selection never starts
anything, and a deleted Session falls back to the first. Store this with appearance
preferences (a preferences format change) and keep failures non-blocking.

Done when: restart restores these values, with tests for missing monitors, oversized
bounds, maximized state, deleted Sessions and unreadable preferences.

## SESS-040 — Optional apps: continue when an app fails to start

**P1 · Proposed · 1.0 feature (M) · 2026-09-15**
Source: agent proposal accepted in SESS-037. Today any launch failure stops startup and
waits for confirmation to clean up; utility-heavy Sessions need a tolerant choice.
Launch stages and broader failure policies (SESS-023 candidates) remain after 1.0.

Settle before implementation: per-app setting name and default (off); which failures
count (missing file, launch error, readiness timeout, UAC cancellation); whether the main
or completion-focus app may be optional; how skipped apps appear during and after
startup; and interaction with concurrent launching and pauses. Ownership, rollback of
required-app failures and save-work confirmation stay mandatory. Library format change;
coordinate with SESS-041.

Done when: agreed semantics are in PRODUCT/ARCHITECTURE, Core tests cover ordered and
concurrent failures with optional and required apps, and the UI explains skipped apps.

## SESS-041 — Saved-data compatibility commitment

**P1 · Open · 1.0 feature (S–M) · 2026-09-15**
Source: agent proposal accepted in SESS-037; 1.0 requires clear compatibility expectations.

Early slice: before the first save that upgrades a library (or preferences file) to a
newer format, write a timestamped backup beside it and tell the user where it is. Keep
reading every earlier format. Later slice, at Beta: declare the 1.0 library format frozen,
document the compatibility policy (newer minor releases read and write it; older builds
may reject newer data) and add tests loading real saved files from each released format.
Format changes from SESS-040/043/044/046 should land before the freeze.

Done when: backup-on-upgrade is tested (including write failure blocking the upgrade),
fixture libraries from v1 onward load, and PRODUCT/RELEASING state the policy.

## SESS-042 — About and support

**P1 · Open · 1.0 feature (S) · 2026-09-15**
Source: SESS-031 candidate; the user included it in 1.0 via SESS-037.

Show version, Alpha/Beta stage, license, links to release notes and the issue tracker,
and an action to open the local data folder. Nothing is sent automatically; diagnostic
export stays after 1.0. Reuse Settings surfaces and existing tokens.

Done when: the view is reachable by keyboard from Settings, opening the folder handles
failures, and both themes/compact sizes are checked.

## SESS-043 — Export and import a Session

**P1 · Proposed · 1.0 feature (M) · 2026-09-15**
Source: portability principle in PRODUCT.md; the user included it in 1.0 via SESS-037.

Settle: file format and extension (versioned, readable JSON for one Session); whether
import assigns fresh IDs; handling of name clashes, missing executables, unknown or
disabled plugins and unavailable audio devices. Import must open a draft for review
before saving, so arguments, Run as administrator and force-quit choices from a shared
file are visible first; importing never launches anything. Exported files contain paths
and arguments, so explain that before sharing. Depends on formats from SESS-040/044/046.

Done when: exported Sessions round-trip, foreign or partial files import safely with
clear explanations, and tests cover malformed, newer-format and hostile-looking files.

## SESS-044 — Open a website as part of a Session

**P1 · Proposed · 1.0 feature (M) · 2026-09-15**
Source: Work Session example in PRODUCT.md; the user included it in 1.0 via SESS-037.

Settle: the item type in the editor and picker, allowed schemes (http/https at least),
ordering and pauses with apps, and what the detail view shows. Websites open through the
default browser without process ownership: End never closes browser windows or tabs,
readiness and main-app lifetime are unavailable, and the UI says so. Browser profile
selection stays after 1.0. New item kind and library format change; coordinate with
SESS-041 and the plugin/action architecture.

Done when: website items save, reload, open in order during startup and individually,
with tests for validation, ordering, failure reporting and cleanup leaving them alone.

## SESS-045 — Start a Session from a shortcut or the command line

**P1 · Proposed · 1.0 feature (M) · 2026-09-15**
Source: SESS-023 candidate (local Session shortcuts); the user included it in 1.0 via SESS-037.

Settle: command-line syntax referencing a stable Session ID, a Create desktop shortcut
action, and behaviour when Sessions is closed, already open, editing, confirming or
running another Session. The single-instance guard currently signals activation without
data, so a newly started instance must pass its request to the running one. Existing
start rules, draft protection and confirmations stay in force; a shortcut never bypasses
them. Unknown or deleted Sessions show a clear message.

Done when: shortcuts and commands start the right Session from each app state, with
tests for forwarding, missing Sessions, an active run and an open editor.

## SESS-046 — Launch Microsoft Store and packaged apps

**P1 · Proposed · 1.0 feature (L, research first) · 2026-09-15**
Source: SESS-008/019 list packaged-app activation as unsupported; the user included it
in 1.0 via SESS-037.

Research before committing to a design: discovery of packaged apps in the Add app
picker, activation by application user model ID, whether activation yields a process
that can be tracked, running-status detection, bring forward, and closing behaviour for
apps with shared hosts, brokers or background tasks. Ownership rules stay mandatory:
when a launch cannot be verified, the app is untracked and needs manual closing, as today.
Record findings with primary sources and tested apps before choosing an approach.

Done when: a documented approach launches packaged apps from Sessions and the picker,
with verified ownership where possible, clear limits where not, and native tests using
installed packaged apps.

## SESS-047 — Decide code signing before Beta

**P2 · Open · Decision, not code · 2026-09-15**
Source: SESS-037; the user chose to decide near Beta. Releases are currently unsigned,
which can trigger Windows SmartScreen warnings.

Before Beta, compare available options (commercial certificates and signing services
for open-source projects), cost, identity requirements and release workflow changes,
then ask the user to decide. Do not contact vendors or buy anything without authorization.

Done when: the decision is recorded in RELEASING.md and, if signing is chosen, a signed
release passes verification.

## SESS-048 — Beta readiness: real-app matrix and user guide

**P1 · Open · Beta gate · 2026-09-15**
Source: Beta criteria agreed in SESS-037. Complements SESS-001, SESS-010 and SESS-021.

Record a real-app validation matrix with exact versions: for example Discord, SRS, Tobii,
Playnite, Word, a Steam game and the 1.0 additions (websites, packaged apps). Cover start,
running status, bring forward, End with save prompts, individual Close, force quit and UAC
where relevant, with expected and observed results. Update the README or a user guide with
setup, everyday use, data location, upgrades and known limitations.

Done when: the matrix is recorded with outcomes, failures link to backlog IDs, and the
guide matches the Beta build.
