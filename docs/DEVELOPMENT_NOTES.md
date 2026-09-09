# Sessions — Development Notes

Project continuity and dated findings. [PRODUCT.md](PRODUCT.md) remains authoritative for product behaviour and scope; [ARCHITECTURE.md](ARCHITECTURE.md) remains authoritative for technical decisions. Track actionable follow-up in [BACKLOG.md](BACKLOG.md), rather than leaving tasks buried in these notes.

## Resume next session — branding approved 2026-09-09

The user approved adapting the supplied mockups to the current product, confirmed
OS-following light/dark as the default, delegated missing tokens/assets, prioritised
readability and excluded prototype-only features (history, Recently added, etc.).
The existing 640×480 minimum is retained with compact adaptation below 900px.
AGENTS.md and the branding guide now agree with these decisions.

SESS-024 is implemented: Manrope 500/700/800, indigo primary actions, green running
states, rounded sidebar/hero/app cards, shared editor/picker/confirmation styling,
and Will be closed / Stays open ownership lists. No startup/cleanup semantics or
saved schema changed. Existing advanced options, contextual headings, validation,
empty-Session start guard and recovery remain. No fake history/timer/shortcut UI.

`branding/tokens/tokens.json` is canonical. Run `python scripts/Generate-BrandTheme.py`
after token edits; both AXAML copies and CSS are generated. Missing readable
text/fill/hover colours and typed layout tokens were added. Manrope source/license
is pinned under branding/fonts; `scripts/Generate-BrandAssets.py` generates static
fonts and the ICO with optional fontTools/Pillow tools. Normal builds use the bundled
assets offline. Original SVG/PNG references are preserved. The pre-implementation
findings in [BRANDING_REVIEW.md](BRANDING_REVIEW.md) are labelled historical.

Validation using the pinned SDK in artifacts/dotnet: Release solution build has
zero warnings/errors; 45 Core tests and 94 App tests pass, 22 opt-in native tests
skipped. Four new theme/ownership rendering cases cover 1440×900 and 640×480 in
both themes, font loading, dynamic theme switching, contrast, focused field/search
appearance, compact title/actions, picker selection and safe confirmation focus.
Existing keyboard/automation and 125%/150%/200% render-scaling cases also pass.
All 100 local documentation links/anchors pass. Staged whitespace checks pass apart
from one upstream trailing space in Manrope's OFL notice, retained verbatim. Theme
and font/icon regeneration are byte-identical; all view resource keys resolve,
all three font weights and seven ICO sizes verify, and the font notice is present
in build output.
Review PNGs are in ignored artifacts/branding-review. Visual review found and fixed
Fluent focused-field colours, faded placeholders, checkbox shape and fixed local
layout values overriding compact styles. Native screen-reader/monitor/text-size
checks remain SESS-010; installed-app/UAC checks remain SESS-021. No new native trial
or installer smoke test was run for this presentation change.

Commands: `dotnet build Sessions.slnx -c Release`, then Core and App `dotnet test`
with `-c Release --no-build`. Set SESSIONS_SCREENSHOT_DIR to capture review PNGs.
Put artifacts/dotnet first on PATH and set DOTNET_ROOT to it for the pinned SDK.
The user reviewed and approved the visual refresh, confirmed the guidelines were
updated, and requested commit/push of this checkpoint. No new release was requested;
the current public release remains 0.2.0. Do not automatically start another backlog
item or publish a release.

## Previous checkpoint — 0.2.0 published 2026-09-09

The user requested a new release after checkpoint `851e476` was committed and pushed.
[Sessions 0.2.0 Preview](https://github.com/datstma/sessions/releases/tag/v0.2.0) is
public, published at 18:53 UTC on 2026-09-09. Annotated tag `v0.2.0` points to
`23f8ec7`; neither published tag nor its assets should be replaced. README links
now point to 0.2.0. No next feature slice is selected.

Includes advanced startup, accessibility/scaling, contextual options headings, and
the empty-Session start guard. Saving upgrades libraries to v2, which 0.1.0 cannot
read; loading v1 does not rewrite it. See [release-notes/0.2.0.md](release-notes/0.2.0.md).

[Workflow 34390315585](https://github.com/datstma/sessions/actions/runs/34390315585)
built the exact tag: solution and MSI builds have zero build warnings/errors;
45 Core and 90 App tests pass, with 22 opt-in native checks skipped. Prior source
validation passed all 17 native runtime fixtures. The Actions runtime notices remain
SESS-022; standard-user and installed real-app/UAC checks remain SESS-021, with native
screen-reader/monitor-scaling verification under SESS-010.

Downloaded MSI and both source ZIP checksums verify. All 114 Sessions source archive
entries match the tag. The MSI contains 274 payload files and reports
`0.2.0+23f8ec726d1a2481b45e6ea3fcf9725d79ba5d0f`; its 43 package metadata and notice
files match the reviewed 0.1.0 package. WiX source/license checks pass. MSI size:
59,418,429 bytes; SHA-256:
`bd89f83540957274d72c08a2a9d497242bf162b92fba3b7987bb6c340ffa9f3b`.

The exact downloaded installer passed all 26 Sandbox smoke assertions, plus the
initial 0.1.0 baseline install: upgrade to 0.2.0, installed launch without shared
.NET, one per-user registration, v1 load without rewriting, running-app refusal,
downgrade rejection, uninstall/reinstall, shortcut removal, and byte-for-byte library
preservation. Used the default Sandbox account and isolated fixtures; the host
library and installed apps were untouched. Evidence is in ignored
`artifacts/release-verification-0.2.0/`, including downloads, workflow log, asset
review, scripts, and `results/result.json` with status passed. The Sandbox is stopped.
Offline MSI operations again took about two minutes; no speed claim is made.

The public release notes include final downloaded-asset and installer verification.
Post-publication documentation records those results without changing the tagged
source archive. Reproduce source builds with the pinned SDK commands in the prior
checkpoint below.

## Previous checkpoint — advanced startup and UI feedback 2026-09-09

The user requested “update documentation and commit/push”. This source checkpoint
includes accessibility/scaling, advanced startup, contextual options headings, and
the empty-Session start guard. The README, product and architecture documents, and
backlog reflect these changes. The features are unreleased; the published 0.1.0
tag and installer remain the previous release. No next feature slice is selected.

Latest feedback: a Session with no apps should have no start option. Implemented:
empty Session details hide Start Session and startup hints, retain Edit Session and
the add-app guidance, and the start command refuses execution. Empty definitions
can still be saved. This supersedes the earlier product decision allowing users to
start empty Sessions; Core's direct-call empty-run behavior remains unchanged.
The existing light/dark runtime interaction checks now cover empty/populated
selection and blocked direct command execution. Accessibility runtime fixtures use
a simulated app acquisition, without launching real processes.
Validation: Release solution build has zero warnings/errors; 45 Core and 90 App
tests pass, with 22 opt-in native cases skipped. Documentation link targets,
main-window AXAML parsing, and diff whitespace checks pass.

Follow-up user feedback: name the editor sections after the selected app and current
Session. Implemented live draft headings: “[App name] options” and “[Session name]
advanced startup options”, including rename/selection updates, blank-name fallbacks,
and wrapping for long names. Existing keyboard checks now locate the contextual
headings. PRODUCT.md records the behavior.
Validation for this heading follow-up: Release solution build has zero warnings/errors;
45 Core and 90 App cases pass (22 opt-in native cases skipped). Local documentation
link targets, editor AXAML parsing, and diff whitespace checks pass.

The user approved the proposed first advanced-startup slice: “let's do it as you
have proposed.” SESS-023 is implemented in this source checkpoint together with
the accessibility changes. Launch stages, optional-app/retry policies and other
manager ideas remain proposals.

Implemented: In order/All at once; Session pauses (0–300 whole seconds, default 0)
and nullable per-app overrides; Launch request completed/Process is running/A window
appears; readiness timeout (1–600 seconds, default 30); countdown feedback; optional
one-time focus on Sessions or a selected app after successful startup. See PRODUCT.md
and ARCHITECTURE.md for exact behavior. Ordered mode applies waits and pauses;
Together overlaps independent paths, ignores pauses, and still awaits readiness.
Already-running apps remain unowned. Failed/untracked readiness cannot claim success.
All in-flight acquisitions are retained before failure cleanup or End; existing
confirmation and reverse-definition-order cleanup remain in force. Focus uses the
captured definition and RunId; cancellation/failure/end/editing/modals suppress or
cancel it. Windows focus failure is feedback, not Session failure.

Persistence: reads v1/v2, writes **v2** with readable startup enum names. Missing v1
settings preserve the previous defaults and loading does not rewrite files. The
published 0.1.0 app cannot open v2 libraries; this prevents it silently ignoring the
new execution settings. No real user library was changed during implementation/tests.

Validation: full Release solution build has zero warnings/errors; **45 Core + 90
regular App tests pass**. A separate enabled run passes **all 17 native runtime
fixtures**, including owned/pre-existing window readiness in both launch modes and
existing cleanup/self-restart protections: **152 passing tests/checks total**.
All 64 local documentation links/anchors, AXAML parsing, and diff whitespace checks pass.
The five unrelated native discovery/manual-launch checks were not rerun. Light/dark
640×480 startup settings were rendered and reviewed in ignored
`artifacts/startup-review/`; the full App suite also retains the prior accessibility
scaling coverage. These tests use isolated stores/processes. Native real-app/UAC
readiness and Windows foreground permission still need a hands-on trial; headless
tests verify restoring Sessions and focus request/cancellation behavior.

Use the isolated pinned SDK setup below. The user's Debug app was running at the
previous checkpoint; validation uses `-c Release` without stopping it. Commands:

```powershell
$env:PATH = "$PWD/artifacts/dotnet;$env:PATH"
$env:DOTNET_ROOT = "$PWD/artifacts/dotnet"
dotnet build Sessions.slnx -c Release
dotnet test tests/Sessions.Core.Tests/Sessions.Core.Tests.csproj -c Release
dotnet test tests/Sessions.App.Tests/Sessions.App.Tests.csproj -c Release
$env:SESSIONS_RUN_RUNTIME_SMOKE = '1'
dotnet test tests/Sessions.App.Tests/Sessions.App.Tests.csproj -c Release --no-build --filter FullyQualifiedName~NativeSessionRuntimeTests
Remove-Item Env:SESSIONS_RUN_RUNTIME_SMOKE
```

## Previous checkpoint — accessibility/scaling follow-up 2026-09-09

Latest user direction: explore advanced Session settings—wait for the previous app
to finish launching, pauses between apps, concurrent launching, and optionally
refocus Sessions after startup. They also requested ideas for the broader manager.
Recorded as proposed SESS-023; no execution/settings code changed for this discussion.
The proposed first slice is Advanced startup with explicit timing/readiness and
completion focus. Actual readiness conditions, parallel-mode interactions and
timeouts still need a settled design. Current sequential acquisition/500 ms handle
settling is not an application-ready check. Additional manager ideas remain proposals.
Documentation-only follow-up validation: 64 local links/anchors and diff whitespace
checks pass. Debug build was blocked by the user's running Sessions.App locking
its output DLL; the separate Release solution build passed with zero warnings/errors,
and all 23 Core tests passed. The running app was not closed or modified.

The user selected SESS-010: “Let's deal with the Accessibility and scaling.”
Implemented locally, not committed or released: meaningful custom-list accessibility
names, picker explanations, polite changed-status/error announcements, editor and
close-dialog focus restoration, app removal/reordering focus recovery, a compact
640×480 main window, working-area-aware initial sizing for main/picker, and bounded
scrolling for lengthy runtime/errors. PRODUCT.md and ARCHITECTURE.md describe the
implemented behavior. SESS-010 awaits native feedback rather than being marked Done.

Validation: full solution builds with zero warnings/errors; **23 Core + 78 regular
App cases pass**, including 20 new accessibility cases; 20 opt-in native cases skipped.
Rendered review covers both themes, 640×480 at 100%/150%/200% and 960×640 at 125%,
50 Sessions, 30 configured apps, 80 picker entries, long descriptions/names and
confirmation/error states. All 64 local documentation links/anchors resolve and
AXAML files parse. Tests use injected stores/sources and an empty runtime
fixture; no real apps are launched/stopped or user library changed. Screenshots:
ignored `artifacts/accessibility-review/`. Initial-focus and close-cancel restoration
regressions failed during development and passed after the fixes.

The computer-use skill loaded, but `sky.list_apps()` and `sky.list_windows()` both
failed: “Computer Use native pipe is unavailable ... (os error 2).” Native Narrator
delivery, actual Windows DPI/monitor transitions, and OS text-size preferences are
unverified. Headless render scaling and automation-peer metadata do not replace them.
Next review should exercise those native paths; SESS-006/011 remain separate work.

Environment: the system SDK is now 10.0.401 while global.json pins 10.0.400 with
roll-forward disabled. Installed the exact SDK locally under ignored `artifacts/dotnet/`
using Microsoft's dotnet-install script; the repository pin and system SDKs are
unchanged. To reproduce from the repository root in PowerShell:

```powershell
$env:PATH = "$PWD/artifacts/dotnet;$env:PATH"
$env:DOTNET_ROOT = "$PWD/artifacts/dotnet"
dotnet build Sessions.slnx
dotnet test tests/Sessions.Core.Tests/Sessions.Core.Tests.csproj
dotnet test tests/Sessions.App.Tests/Sessions.App.Tests.csproj
```

NuGet restore and Avalonia build-service log access required execution outside the
restricted sandbox. Native checks were not enabled. The published v0.1.0 assets/tag
remain unchanged; release evidence follows.

## Previous checkpoint — 0.1.0 published 2026-09-09

The user explicitly requested **“update the documentation, commit and publish the
release please.”** Completed: packaging and release documentation were committed
and pushed as `4ec492f`; annotated tag `v0.1.0` points to that commit. The
[0.1.0 preview](https://github.com/datstma/sessions/releases/tag/v0.1.0) is public
(published 2026-09-09 00:05 UTC). Do not move its tag or replace its binaries.
No next app feature has been selected. SESS-020 is complete; SESS-021/022 retain
installed-release validation and Actions maintenance work.

[GitHub workflow 34292417455](https://github.com/datstma/sessions/actions/runs/34292417455)
built the exact tag and created the draft successfully. Full solution/MSI builds
had zero build warnings/errors; **23 Core + 58 regular App tests passed**, with
20 opt-in native checks skipped. GitHub emitted Actions Node.js runtime deprecation
notices, recorded separately in SESS-022. The pre-commit local solution build and
documentation checks also passed.

Downloaded release assets: MSI, matching Sessions source ZIP, WiX source ZIP, and
SHA256SUMS.txt. All three hashes verify; all 108 entries of the Sessions archive
match a fresh `git archive v0.1.0`. The WiX archive has the pinned source commit,
matching license, and CloseApplications source. The MSI has 274 extracted payload
files, bundles .NET 10.0.11, and identifies the application as
`0.1.0+4ec492f8e9432b5880dcc69e51d54cf17f8e1aa7`. It is unsigned, 59,406,141 bytes;
SHA-256: `b70c026f427e880bae8701e80dcc70143e9fb0a25e7f91a159ccddeecbbd2a67`.

The exact downloaded MSI passed a fresh Windows Sandbox run: install/shortcut,
installed window without shared .NET, library preservation, running-app refusal
without terminating the app, normal close, upgrade to the test-only 0.1.1 fixture,
one per-user registration, downgrade rejection, uninstall/reinstall, and final
uninstall. Evidence is under ignored `artifacts/release-verification/`, including
the workflow log, downloaded assets, extracted MSI, source verification script,
and `results/result.json` with status passed. The disposable Sandbox was stopped.
The host's real Session library and installed apps were not used as test fixtures.

The dependency inventory and notice coverage were reviewed: published NuGet
metadata/notices, bundled .NET notices, and the pinned supplemental notices cover
the shipped dependencies and Inter font. The workflow also supplies the matching
WiX 7 source archive because its native utility action is embedded in the MSI.
The Sessions source archive comes from the same Git tag as the binary. This is a
packaging/source review, not a legal opinion. Standard-user and installed real-app/
UAC checks remain explicitly disclosed preview limits; they are not represented as
passing checks. SESS-021 tracks those installed-release checks; SESS-001 retains
the broader native UI review.

The previous local installer evidence below is historical and refers to the
pre-commit package. The public release's source, size, and hash are recorded above.

## Previous checkpoint — packaging follow-up 2026-09-09

The user accepted the proposed shared-version, self-contained MSI, and on-demand
GitHub draft-release workflow. Packaging files have been authored locally (not
committed/pushed). Start version is 0.1.0; SDK is pinned to the installed 10.0.400.
Read [RELEASING.md](RELEASING.md) and SESS-020 before continuing. The local unsigned
MSI is `artifacts/releases/0.1.0/Sessions-0.1.0-win-x64.msi`, with a SHA-256 sidecar.
No release has been published or remote workflow run.

The user explicitly authorized WiX terms acceptance with **“you may”** after the
terms question. `AcceptEula=wix7` is configured in the project; the previous WIX7015
blocker is resolved. Do not ask for this acceptance again for the current setup.

Compilation exposed two authoring issues, now fixed: file components with HKCU key
paths need explicit GUIDs, and WiX 7 requires `override` when rescheduling the
utility action. File component GUIDs are deterministic from a stable product,
architecture, scope, and relative path namespace. ICE91 is the sole exclusion:
Microsoft documents it as harmless for exclusively per-user installs, and this
MSI explicitly rejects ALLUSERS. All other ICE checks and warnings-as-errors remain.
Fresh per-build intermediate directories avoid a reproduced WiX stale-output issue
when building a higher-version fixture from the same payload.

Current validation: Debug and Release full-solution builds pass with zero
warnings/errors; **23 Core + 58 regular App tests pass**, 20 opt-in native checks
skipped. Release win-x64 self-contained publishing succeeds, as do notice
collection and per-user payload generation. Initial notice collection exposed a
missing optional NuGet projectUrl under strict mode; this is fixed. MSI compilation
and configured ICE validation pass with zero warnings/errors. GitHub execution
remains unverified. Reproduce packaging with
`./scripts/Build-Installer.ps1` in PowerShell 7; `-SkipTests` is for local iteration
after tests pass. Staging/output is ignored under `artifacts/`.

Additional checks: all four PowerShell scripts parse; matching version tags pass
and mismatched tags fail; the staged payload has unique definitions,
existing sources, and HKCU component key paths. Published executable metadata is
0.1.0.0 with the source commit in ProductVersion. Workflow YAML and manual-only,
draft, and permission checks pass; all 69 local documentation links/anchors resolve.
All 274 files extracted from the final MSI match their published sources by SHA-256;
the MSI checksum matches its sidecar. The 273 shared file-component GUIDs remain
identical across separate build directories. The final MSI is 59,389,757 bytes and
unsigned; SHA-256 is `788e0a8b4a35639855af0e596b64d620eab3516fe9677e6b0fdae5e7d63ff655`.
The real MSI sequence table confirms running-app detection/refusal at 1398/1399,
before InstallValidate (1400), and RemoveExistingProducts at 1501, inside the
transaction after InstallInitialize (1500). WiX's decompiler misleadingly rendered
the latter as afterInstallFinalize; raw MSI table values are authoritative.

Windows Sandbox verification now passes for the final MSI: fresh install, installed
app window without a shared .NET runtime, shortcut creation, refusal of upgrade
and uninstall while Sessions runs (1603 with the intended message, app remains
running), normal app close, upgrade to a test-only 0.1.1 MSI, one per-user product
registration, downgrade rejection, removed-file cleanup, uninstall/reinstall, and
byte-for-byte library preservation. The higher-version fixture reuses the app
payload from an earlier packaging build; it tests MSI replacement mechanics, not
a separately released app version. The default Sandbox account was used; an
ordinary nonadministrator account and installed real-app/UAC cleanup remain.

Evidence is under ignored `artifacts/sandbox-msi/` (MSI logs, scripts, and final
`results/result.json` with status passed). Harness corrections were needed for a
two-minute timeout, IntPtr.Zero window polling, Select-String parameter binding,
and using the Windows Installer API rather than assuming an HKCU Uninstall key.
Those were test-harness errors, not app defects. Fresh MSI operations in the
offline Sandbox took about 124 seconds; the cause is not established. The successful
continuation used five-minute timeouts. No changes were made to the host's real
Session library or installed apps. The disposable Sandbox was stopped after the
successful checks.

New evidence: published dependency metadata omits some upstream license files.
Supplemental unmodified licenses/notices are checked into `installer/licenses/`
with pinned provenance. The bundled Inter font identifies itself as
`3.019;git-0a5106e0b` under SIL OFL 1.1. The collector inventories the published
dependency manifest, copies available package notices, and includes .NET runtime
notices separately. This supports release review, not a claim of a completed legal
audit. The WiX 7 source license is also included because the MSI embeds its native
utility action. The draft release notes retain final notice/source review checks.

## Previous stopping point — end of day 2026-09-08

The user confirmed the Start menu picker: **“it's working!”** Today's app features and end-of-day documentation were committed as `85c1db5`, the repository's first source checkpoint; README/GitHub setup followed in `93ea4a8`. The public repository is `datstma/sessions`, `origin` is `https://github.com/datstma/sessions.git`, and `main` tracks `origin/main`. The user then selected GPLv3: the project uses **GPL-3.0-only**, without an optional later-version grant. LICENSE contains the full text from SPDX's official license list; README and App/Core metadata declare it, and App build/publish outputs include LICENSE. Third-party dependencies retain their own licenses. No binary release has been published. Remaining release preparation is tracked in SESS-020; no next app feature has been selected. Generated `output/ui-review/` PNGs are ignored and can be regenerated; `output/session-ux-options.html` preserves the original three-option design comparison.

- Read the current handoff below, then PRODUCT.md and ARCHITECTURE.md for the area being changed. Keep the selected sidebar/detail design; the user wants intuitive interaction, progressive disclosure, and Session-centric navigation.
- Confirmed by the user: Discord remains independent after Sessions closes, SRS starts/stops, the default full-stop confirmation flow works, and Start menu discovery works. These confirmations do not establish every installed app, UAC cancellation path, accessibility case, or Microsoft Store activation works.
- Likely follow-ups if the user wants a suggestion: unsaved-draft protection outside a run (SESS-006), keyboard/accessibility/display scaling (SESS-010), and actionable invalid-field feedback (SESS-011). SESS-001 tracks remaining native checks; SESS-017 retains its narrower Playnite-specific verification question. The overall stopping flow was subsequently confirmed under SESS-018; do not present that as an unresolved general failure.
- Preserve independent Windows shell launches, exact runtime ownership, pre-existing-app protection, save-work confirmation before termination, and the unelevated main UI. Do not restore the removed ForceClose option or infer ownership from saved executable paths.
- The real library is `%LOCALAPPDATA%/Sessions/sessions.json`. Tests use injected/temporary libraries. Do not replace the user's library with fixtures or launch/stop their installed apps for routine validation.

Licensing-change validation: build and local Debug publish succeed with zero build warnings/errors; the LICENSE files in build/publish output match the root file by SHA-256. All 23 Core and 58 regular App tests pass; the 20 opt-in native checks were not rerun for this metadata/documentation change. All 55 local documentation links and anchors resolve. The local publish under ignored `artifacts/license-check/` is a packaging check, not a tested end-user release.

Most recent full native verification, before the licensing change: **101 checks passed (23 Core, 58 regular App, 20 opt-in native)**. Native tests use isolated process fixtures; discovery scans are read-only. To reproduce from the repository root on Windows:

```powershell
dotnet build Sessions.slnx
dotnet test tests/Sessions.Core.Tests/Sessions.Core.Tests.csproj
$env:SESSIONS_RUN_DISCOVERY_SMOKE = '1'
$env:SESSIONS_RUN_LAUNCH_SMOKE = '1'
$env:SESSIONS_RUN_RUNTIME_SMOKE = '1'
dotnet test tests/Sessions.App.Tests/Sessions.App.Tests.csproj
Remove-Item Env:SESSIONS_RUN_DISCOVERY_SMOKE, Env:SESSIONS_RUN_LAUNCH_SMOKE, Env:SESSIONS_RUN_RUNTIME_SMOKE
```

Without the three flags, the 20 opt-in native checks are skipped. Set `SESSIONS_SCREENSHOT_DIR` to `output/ui-review` to regenerate review images. Native checks require an appropriate Windows desktop environment; they do not validate real UAC prompts. Earlier counts below are historical milestones, not the current total.

## Current handoff — 2026-09-08

The accepted UI is option 2: named Session sidebar plus details, with local creation/editing, app picker, safe deletion, live presence, individual launches, and now real Start/End Session. The user confirmed the Discord inherited-pipe fix (“works like a charm!”), then explicitly requested enabling Session execution. The local documentation/backlog must continue to be maintained after material changes.

Start/End use Core SessionRunner and WindowsSessionProcessHost. One active run, ordered startup, retained-handle ownership, main-app monitoring, reverse cleanup, startup rollback, and refusal recovery are implemented. Already-open and untracked apps are never cleanup-owned. A persistent runtime panel/active sidebar marker and per-app messages explain results. Active definitions cannot be edited/deleted; manual launches are disabled during a run. A Global per-profile single-instance guard prevents competing writers/runs and signals the first window on a second launch.

Latest request: search and scroll through installed apps via the Start menu. SESS-019 adds Start menu as the default Add app source, with Running apps alongside it. It reads current-user/shared desktop shortcuts and retains their arguments, working folder, and administrator setting. Selections survive search and source switching; duplicate executable selections replace one another. Unsupported/broken shortcuts explain why they cannot be selected. No shortcut is launched or modified during discovery; virtual/packaged app activation remains unsupported.

The user subsequently confirmed the Start menu addition works: “it's working!” This is hands-on confirmation of the overall picker in their setup; no complete installed-app inventory or packaged-app coverage was claimed.

Previous user feedback: after trying the new default-stop confirmation flow, the user reported “worked like a charm!” SESS-018 is confirmed in their real-app trial. End names the active Session and owned apps, defaults focus to Cancel, and warns that lingering apps will be terminated. Confirmed cleanup attempts normal close then force quits the exact owned process. Already-open/untracked apps remain protected. The redundant ForceClose setting has been removed from model/editor; old JSON flags are ignored and disappear on a normal save. RunAsAdministrator remains an optional per-app launch setting.

Main-app exit now enters AwaitingEndConfirmation, not immediate cleanup. Startup failure also waits before cleaning up owned launches. Cancelling an automatic end keeps apps running and suppresses repeated prompts for that main exit; cancelled failure cleanup retains ownership in NeedsAttention. The window-close dialog contains the same warning and its End-and-close choice is the approval. The Core EndAsync entry point executes an already-confirmed request; no automatic path silently terminates apps. Dialogs defer while unrelated editing/saving/confirmations are active.

Earlier feedback: SRS start/stop is confirmed (SESS-016). The Playnite partial-shutdown fix filters close requests to titled application windows with a system menu, excluding internal/tool windows while allowing eligible hidden main windows (SESS-017). A controlled test reproduced the original bug. The user has now confirmed the overall stopping flow works; the individual apps and UAC cancellation paths exercised were not enumerated.

WindowsProcessIdentity uses limited query/synchronize handles, including for elevated apps. WindowsTrackedApp follows a unique verified direct-child restart at the same full path, with parent lifetime and login-session checks; no name-only adoption or general tree cleanup. WindowsProcessCleanup handles eligible hidden app windows, confirmed default force quit, and an exact-identity one-shot elevated helper when Windows denies cleanup. The UI app remains unelevated and UAC is not bypassed. Unknown/ambiguous handoffs still require manual management. A current run cannot recover ownership already lost by an older build.

Current validation: clean solution build and **101 checks (23 Core, 58 regular App, 20 opt-in native)** pass. New coverage includes real shortcut metadata reading using isolated fixtures, missing/corrupt/unsupported links, duplicate selection, source failure/recovery, and search/scroll/keyboard source switching with 80 entries in both themes. A read-only scan of the actual Start menu passes. Picker layouts were reviewed at 650×620 and 520×460. Existing ownership, stopping, and Discord pipe-isolation checks still pass. Native helpers do not operate on the user's apps or configuration; actual UAC cancellation remains unverified.

Normal close during a run offers keep open, end-and-close, or leave-apps-open-and-close. Cleanup failure keeps Sessions open. Forced termination loses in-memory ownership and cannot clean up; surviving independent apps are pre-existing next time. Draft-close protection outside an active run remains SESS-006. Do not infer ownership from persisted paths or attempt cleanup based on process names after restart.

Start menu desktop-shortcut discovery, the running-app picker, presence/focus, and independent launching are implemented. The picker translates supported shortcuts into the existing executable action model; it does not activate `.lnk` files or packaged apps directly. The Windows desktop Open launch mechanism must be preserved: the previous direct launch reproduced pipe error 232 after the IDE/parent connection closed, and the user confirmed its fix in Discord.

## User preferences and design rationale

- Aim for the care and intuitiveness associated with Apple, without copying Apple's visual appearance. The user also referenced Discord's ease of use. Our interpretation is clear defaults, predictable navigation, and a stable layout, not a requirement to copy Discord's chrome.
- The primary task is choosing a saved Session and starting it. With no Sessions, creating the first one should be immediately discoverable.
- Three approaches were explored: a card library, a persistent sidebar, and a compact focus launcher. The user explicitly chose the sidebar on 2026-09-08. The library introduced navigation between collection and details; the compact launcher hid the collection and left less room for explaining state and failures. Neither alternative is a current implementation commitment.
- Selection and execution are separate actions. Users must be able to inspect another Session without changing their running environment.
- A Session represents a context, not necessarily a main executable. Work and Development are as central as gaming. Default to ending manually, with the option to end when a chosen app exits.
- Predictable cleanup is fundamental to trust. Explain it with concrete language such as "Apps already open will stay open" and, once execution exists, distinguish apps opened by Sessions from pre-existing apps.
- The current palette, initial badges, and dimensions are provisional implementation choices. System light/dark appearance is implemented. No usability study or accessibility audit has established that the current visual treatment is final.
- The one-active-Session policy is implemented and exercised by Core/UI/native tests. Current editing disables sidebar browsing; whether that feels too restrictive is a question for the hands-on trial.

## Implemented baseline

| Area | Current behaviour | Main locations |
| --- | --- | --- |
| First launch and navigation | Empty state with Create your first Session; sidebar appears after creation; stable saved order; selected details; no runtime status simulation | [MainWindow.axaml](../src/Sessions.App/Views/MainWindow.axaml), [MainViewModel.cs](../src/Sessions.App/ViewModels/MainViewModel.cs) |
| Creation and editing | Separate draft; name; optional description; pick Start menu/running apps or browse `.exe` files; remove/move apps; advanced app options; explicit Create/Save and Cancel | [SessionEditorView.axaml](../src/Sessions.App/Views/SessionEditorView.axaml), [SessionEditorViewModel.cs](../src/Sessions.App/ViewModels/SessionEditorViewModel.cs) |
| App picker | Start menu and Running apps sources; search, scroll, multi-select, names/icons, duplicate and unavailable states, Refresh, browse fallback; shortcut launch metadata retained; no ownership transfer | [AppPickerViewModel.cs](../src/Sessions.App/ViewModels/AppPickerViewModel.cs), [AppPickerWindow.axaml](../src/Sessions.App/Views/AppPickerWindow.axaml), [WindowsRunningAppSource.cs](../src/Sessions.App/Services/WindowsRunningAppSource.cs), [WindowsStartMenuAppSource.cs](../src/Sessions.App/Services/WindowsStartMenuAppSource.cs) |
| Lifetime configuration | Optional main-app identity; reordering preserves the reference; removing the main app leaves automatic ending invalid until a replacement is chosen or manual ending selected | [SessionDefinition.cs](../src/Sessions.Core/SessionDefinition.cs), [SessionEditorViewModel.cs](../src/Sessions.App/ViewModels/SessionEditorViewModel.cs) |
| Saving | `%LOCALAPPDATA%/Sessions/sessions.json`; version 1 envelope; readable JSON; validate, write temporary sibling file, replace destination | [JsonSessionStore.cs](../src/Sessions.Core/JsonSessionStore.cs), [App.axaml.cs](../src/Sessions.App/App.axaml.cs) |
| Error recovery | Failed saves retain the draft and original displayed definition; unreadable libraries show a retry and block creation/writes; missing library starts empty | [MainViewModel.cs](../src/Sessions.App/ViewModels/MainViewModel.cs) |
| Deletion | Named confirmation; Cancel/Enter/Escape; captured identity; persist before removing; failure retains the Session; next/previous selection or first-run state; no effect on apps/files/processes | [MainViewModel.cs](../src/Sessions.App/ViewModels/MainViewModel.cs), [MainWindow.axaml](../src/Sessions.App/Views/MainWindow.axaml) |
| Appearance | Shared theme resources, light/dark variants, scrollable content and fixed editor Save/Cancel footer; initial 1120×800 window, minimum 860×620 | [SessionStyles.axaml](../src/Sessions.App/Styles/SessionStyles.axaml), [MainWindow.axaml](../src/Sessions.App/Views/MainWindow.axaml) |

The real library is not seeded with examples. Work, Flight sim, and Development examples in review screenshots come from test fixtures. No app-launching or process-cleanup claims should be inferred from those fixtures.

## Verification and implementation findings — 2026-09-08

The implementation baseline was built with **zero warnings and errors**. **9 Core cases and 9 App cases passed**. These are a dated result, not a guarantee about future changes.

- Core tests cover missing libraries, ordered configuration round-trips with both lifetime modes, malformed/unsupported data, invalid main-app references, cancelled saves, and replacement without leftover temporary files.
- App tests cover the first-run create flow through keyboard activation and bindings, reloading via a test store, cancelling edits, main-app identity during reordering/removal, save-error retry, invalid-library handling, and sidebar/editor rendering at 1120×800 and 860×620 in light and dark variants.
- Rendered screenshots were visually inspected. Smaller windows need vertical scrolling; editor Save/Cancel remain visible. This establishes a layout baseline, not full accessibility or native desktop verification.
- The native Windows file picker was **not manually exercised** by the agent. Real installed-app selection and a real application restart still need hands-on confirmation. The automated reload test uses a test store; Core JSON round-trip tests independently cover storage.
- Avalonia 12.1.2 uses `PlaceholderText` rather than obsolete `TextBox.Watermark`. Its headless xUnit package requires xUnit v3; App tests use 3.2.2 while Core tests retain xUnit v2.9.3. Keep these intentional package choices in mind when extending tests.
- The UI storage-error filter explicitly handles `InvalidDataException`, alongside I/O and access failures. Do not remove this handling: invalid library contents must produce a recoverable message rather than an unhandled startup error.
- The initial JSON store assumes a single writer. Concurrent app instances are not coordinated; see SESS-007.
- At the initial preview baseline, code inspection showed no window-closing draft guard and no Session deletion command. Deletion has since been implemented (see below); window-close draft protection remains SESS-006.

Run the solution build and both test projects using the commands in [AGENTS.md](../AGENTS.md#validation). To regenerate local review PNGs without opening a window or using the real library:

```powershell
$env:SESSIONS_SCREENSHOT_DIR = Join-Path $PWD 'output/ui-review'
dotnet test tests/Sessions.App.Tests/Sessions.App.Tests.csproj
Remove-Item Env:SESSIONS_SCREENSHOT_DIR
```

Review images are generated under `output/ui-review/`. The earlier three-option interaction concept is in `output/session-ux-options.html`; it uses simulated actions and is not the implementation or specification. These generated artifacts are optional aids; the notes and source remain sufficient if they are unavailable.

## Safe deletion follow-up — 2026-09-08

Source: explicit user request, implementation, automated tests, and rendered layout review. SESS-009 is complete for the current preview. Duplication was not requested and remains uncommitted.

- Delete Session is a secondary action below the Session details. Confirmation names the target and explains that deletion cannot be undone. Cancel receives focus; Tab stays within the confirmation and Escape cancels. Focus returns to Delete Session afterward, or Create your first Session when the library becomes empty.
- Only the saved definition is removed. Existing apps, files and processes are untouched. No executable deletion or process operations were added.
- The captured Session identity is used even if selection changes programmatically. Repeated confirmation and cancellation are blocked while saving. A failed write keeps the original item and offers retry or cancel in the confirmation.
- Tests cover cancellation/default keyboard behaviour, confirmation, failures/retry, duplicate names and changed selection, in-flight repeated requests, next/previous selection, and deletion of the final Session. JSON-backed tests reload the reduced library and verify that a referenced app file remains untouched.
- Confirmation and failure screens were rendered and reviewed in light/dark themes at 860×620 with a 120-character name. Content can scroll while the confirmation buttons remain reachable.
- Validation: full solution build with zero warnings/errors; **9 Core + 17 App cases pass (26 total)**. Headless tests and isolated test libraries were used; the user's real library was not changed by verification. Native mouse/screen-reader verification remains part of SESS-001/010.
- Future execution integration must block deletion while a Session is starting, running, or stopping (SESS-005); no such runtime states exist in the preview yet.

## Running-app picker follow-up — 2026-09-08

Source: explicit user request for a more intuitive alternative to locating executable files. The running-app picker portion of SESS-008 is implemented. It presents apps with visible windows, rather than a process/task-manager list. Friendly names come from executable metadata; window titles are not read. Multiple windows of the same executable are grouped, and already-configured apps cannot be added again through the picker.

The picker supports search with retained selections, explicit batch Add, Refresh, and Browse files. Cancel leaves the draft untouched. Browse selections are merged and retained across refresh; running entries that disappear are removed. Existing configuration options and main-app identity are preserved when appending choices. Running-app discovery saves no arguments, document paths, browser tabs, process IDs, or icons. SESS-019 subsequently adds launch metadata read from Start menu shortcuts, without inspecting running-process command lines.

Discovery uses a Windows-only adapter and read-only limited process queries on a worker thread. Icons use `System.Drawing.Common` 10.0.11 and are converted to PNG; missing/invalid icons fall back to initials. Package-identity apps, shared app hosts and inaccessible executable paths are disabled with an explanation. Background-only/tray-only apps are absent from this first version. Discovery does not establish that the executable will still exist or launch successfully later.

Verification: clean full build; **9 Core cases and 25 regular App cases pass**, plus **1 separately enabled native discovery smoke test** (35 passing cases in total). Headless picker tests cover grouping, existing/unsupported choices, search/refresh selection, browse merging, read-error recovery, late results after close, draft-only addition, and the editor-to-picker modal flow. Light/dark picker screens were reviewed at 650×620 and 520×460.

The read-only native check found **10 app choices, 10 icons, and 6 selectable entries** on this desktop. Desktop enumeration was blocked inside the execution sandbox; the same test passed with approved desktop access outside it. This is a test-environment boundary, not a requirement to run Sessions as administrator. No applications were launched/closed and the real Session library was untouched. Native mouse and file-dialog operation still need hands-on review.

The native check is opt-in because ordinary CI/headless environments may not have an interactive Windows desktop:

```powershell
$env:SESSIONS_RUN_DISCOVERY_SMOKE = '1'
dotnet test tests/Sessions.App.Tests/Sessions.App.Tests.csproj --filter FullyQualifiedName~NativeRunningAppTests
Remove-Item Env:SESSIONS_RUN_DISCOVERY_SMOKE
```

Without that variable the native check is intentionally skipped; regular picker tests use injected snapshots and do not enumerate the desktop.

## Running indicator and focus follow-up — 2026-09-08

Source: explicit user request, implementation, automated tests, native read-only discovery, and headless rendered review.

- Details show a green Running button for apps with a window, green Running · no window for background apps, and neutral Not running. Checking and Status unavailable distinguish pending/failed observation from an app being closed. Only windowed apps offer focus; keyboard activation and descriptive accessible names are included.
- Refresh runs off the UI thread approximately every two seconds, with immediate requests on selection/return to details and activation. Scans do not overlap; stale selection/edit results and results arriving after window close are discarded. Editing, deletion confirmation, and minimization pause scheduled work.
- Observations are restricted to the current Windows login session. Full executable path matching avoids false positives between identically named executables in different folders. Shared window enumeration continues to avoid reading window-title content. Observation/focus does not save, launch, terminate, or take ownership of apps.
- Focus resolves a current matching window, rechecks process identity/path, queues restoration if minimized, and asks Windows for foreground focus. Missing windows and denied activation produce inline feedback. Multiple windows use the first eligible match; tray-only apps have no supported focus target.
- Final validation: full solution builds with zero warnings/errors; **9 Core + 32 regular App cases and 1 native check pass (42 total)**. All 43 local documentation links resolve. App coverage includes new keyboard/theme, status transition/recovery, stale-result, overlap, close-cancellation, app-disappearance, and focus-retry tests. Both themes were rendered and reviewed at 860×620, including a long app name and a background-only app.
- The opt-in read-only Windows test passed with **11 choices, 11 icons, 7 selectable picker entries, and 11 windowed presence matches**. It also verified this headless test process as running without a window, case-insensitive matching, and rejection of another executable location with the same filename. No real apps were focused, started, closed, or changed by verification.
- Actual foreground switching and restoring minimized native windows remain a hands-on check, tracked under SESS-001/012. The service uses Windows' documented foreground rules; headless keyboard tests verify command routing and feedback, not OS foreground permission.

## Individual app launch follow-up — 2026-09-08

Source: explicit user request, automated interaction/service tests, native process launch test, and rendered review. Not running is now a neutral button with a play icon; Running retains its existing focus behaviour. The saved executable, arguments, and working folder are used; no configured working folder means the executable's directory.

- Manual launches do not start a Session, save configuration, adopt existing processes, or establish cleanup ownership. Apps launched individually stay open when Sessions closes. Future Session ownership must regard them as pre-existing.
- Fresh full-path presence checks avoid launching a newly opened app. Windowed apps are focused, background apps are reported, and unknown state blocks launching. The shared launcher serializes requests and applies a ten-second per-executable grace period; the row displays Starting and waits for observed presence. Launch failures retain the configuration and explain retry/settings correction inline.
- The Windows adapter validates absolute `.exe` paths and working folders, then directly starts the executable without shell interpretation or automatic elevation. Missing/invalid locations and OS errors are surfaced. Cross-instance launch races, packaged-app activation, active-Session integration, and broader engine ownership remain outside this change.
- Automated coverage adds keyboard activation in both themes, saved settings, pending-state/duplicate-click behaviour, already-running and unknown states, concurrent requests across rows, grace-period expiry, failure/retry, stale-click focus, and invalid native paths. Light/dark launch and Starting layouts were reviewed at 860×620.
- Final validation: full solution build has zero warnings/errors; **9 Core + 42 regular App + 2 native checks pass (53 total)**. All 43 local documentation links resolve.
- The new opt-in native test passed: Windows Script Host executed a temporary background receipt script and exited. The receipt verified the configured working directory and an argument containing spaces. Test files were removed and user applications/library were untouched. Enable with `SESSIONS_RUN_LAUNCH_SMOKE=1` and filter `NativeAppLaunchTests`; regular test runs skip it. This checks the real process-start boundary, while GUI startup behaviour for individual installed apps still benefits from the user's Rider trial.

## Discord errors after closing Sessions — 2026-09-08

Source: user report followed by a controlled native reproduction. The user observed errors in Discord shortly after killing Sessions, after launching Discord through the Session detail list. The exact error text and whether “kill” meant window close, Rider Stop, or Task Manager remain unconfirmed; do not claim the Discord-specific cause is conclusively diagnosed.

- Discord was always a separate process, not a thread of Sessions. The implementation nevertheless used direct `Process.Start` (`UseShellExecute=false`), allowing inherited standard handles from Sessions or its IDE host.
- Added `Sessions.LaunchProbe`, a windowless test helper compiling the real starter source. A test parent inherits redirected output/error pipes; it launches a child via the adapter, then exits normally or is forcibly terminated. After the readers close, the child attempts output/error writes and records results locally.
- **Before the fix, both lifetime tests failed with Windows error 232 on output writes.** The child was alive but still coupled to a closed pipe. This demonstrates the launch defect independently of Discord.
- The starter now uses Windows desktop Open activation (`UseShellExecute=true`, `Verb=open`), retaining `.exe`/working-folder validation and saved arguments. Successful shell handoff with no returned handle is accepted; actual presence remains authoritative. No command interpreter or explicit elevation verb is added; Windows may prompt when an app's manifest requires it.
- **After the fix, both lifetime tests pass**, confirming no inherited stdout/stderr pipes and successful child work after normal/forced parent exit. The existing native argument/working-directory receipt test also passes. Tests operate only on their own temporary processes and files; no Discord/user process was started, killed, or inspected.
- Final validation: clean full solution build (zero warnings/errors), **55 passing tests/checks** (9 Core, 42 regular App, 4 native), and all 43 local documentation links resolve.
- This changes future launches only. Retesting requires rebuilding/restarting Sessions and fully quitting/relaunching Discord through it. Arbitrary debugger/process-tree or job termination is a separate boundary; the test deliberately kills only its own parent process.

## Start/End Session implementation — 2026-09-08

Source: user confirmed the Discord fix and explicitly requested activating Start/End. The implementation completes the conservative execution slice and prerequisite backlog items SESS-002/003/004/005/007.

- Core owns captured definitions, ordered startup, explicit lifecycle snapshots, exact-process observation, one active run, main/supporting app distinctions, End during startup, reverse cleanup, failure rollback, retry, and explicit leave-open. ViewModels only present results and mediate commands.
- WindowsSessionProcessHost shares validated desktop Open startup with independent launching. Existing full-path/current-login matches are retained but unowned. New returned handles are verified after 500 ms; null/short-lived/ambiguous handoffs are untracked. Main lifetime is monitored once per second, including while another Session is selected or the UI is minimized.
- Cleanup posts WM_CLOSE only to retained owned processes and waits three seconds each. There is no Kill in production cleanup. Save prompts, tray behaviour, hung apps, and access restrictions can leave a run needing attention; the UI exposes retry or explicit release. Other apps continue through cleanup even if one fails.
- Single-instance exclusion is acquired before library access; a second launch signals activation and exits. The named Global mutex is keyed by a profile-path hash and supports abandoned-owner recovery. Native helpers tested exclusion, activation signalling, owner release and reacquisition.
- Core tests cover ordered startup/reverse ownership-safe end, rollback/skipped remainder, End while a launch is in flight, repeated/competing operations, main/supporting exits, multiple existing main instances, refusal/retry/release, cleanup errors, untracked main, and empty Sessions. Headless tests cover keyboard Start/End, active navigation/edit/delete guards, manual-launch blocking, recovery and close choices. Native tests verify owned-only close, refusal survival, pre-existing survival, automatic main-exit cleanup, and instance exclusion with real Windows processes.
- Tests launch only isolated off-screen owned test windows and terminate only retained test-owned helpers during failure cleanup; no user apps or the real library are changed. Enable runtime native tests with `SESSIONS_RUN_RUNTIME_SMOKE=1`. Force-closing the Sessions process cannot execute cleanup or reconstruct ownership later; this remains a documented boundary, not a claim of crash recovery.
- Final validation: full solution build has zero warnings/errors; **74 tests/checks pass (18 Core, 48 regular App, 8 native)**. Native checks were enabled with `SESSIONS_RUN_RUNTIME_SMOKE`, `SESSIONS_RUN_LAUNCH_SMOKE`, and `SESSIONS_RUN_DISCOVERY_SMOKE` set to `1`. Active layouts were reviewed in light/dark themes at 860×620. A regression test verifies that choosing Keep Sessions open during end-and-close cancels window exit while cleanup continues. All 41 local documentation links resolve.

## Tray exit and elevation follow-up — 2026-09-08

Source: user reported Tobii Game Hub staying alive on close and SR-ClientRadio losing Session tracking through UAC, while green presence still showed it running. This is a distinction between the independent presence scan and ownership of an exact process, not a thread relationship.

[SRS startup source](https://raw.githubusercontent.com/ciribob/DCS-SimpleRadioStandalone/master/DCS-SR-Client/App.xaml.cs) confirms RequireAdmin launches a new SR-ClientRadio.exe with runas, then exits the original. That source supports the diagnosis; the installed SRS version and actual elevation transition were not manually exercised. The previous host also opened full-access Process.Handle for existing tracking; the new retained identity requests only query/synchronize rights.

Implemented settings and safety boundaries are specified in PRODUCT.md and ARCHITECTURE.md. Force-close is intentionally explicit per app, rather than silently turning every End into data-loss-capable termination. New runs capture that saved choice; already-open apps always stay independent. Suggested user setup: enable Force quit if still running for Tobii and Run as administrator for SRS. Finish the old run, quit leftover apps manually once, restart the build and start a fresh Session to establish ownership.

The self-restart regression simulates an approval delay, creates the same-executable child through desktop shell activation, and exits the original. Both early and later handoffs remain tracked without prematurely ending a main-app Session. An independently launched copy at the same path survives End. Force-quit tests preserve pre-existing refusing apps. The helper is run as a separate process without elevation for identity tests; incorrect creation time/path/login session are rejected before any close. Real UAC acceptance/cancellation remains unverified. Existing Discord parent-exit/pipe and discovery checks still pass.

Final validation: full solution build, zero warnings/errors; **85 checks pass (19 Core + 51 regular App + 15 native)**. App-option keyboard binding/draft isolation/reopening tests pass and light/dark captures were reviewed at 620×620. Old version-1 JSON without the new fields still loads with both flags false; the settings round-trip through persistence. Test code uses only isolated helper apps and temporary libraries; the user's running apps/configuration were not changed.

## Playnite partial-shutdown follow-up — 2026-09-08

The user confirmed SRS start/stop and reported Playnite Desktop staying alive in a broken state after its tray icon disappeared. A read-only saved-config check currently found ForceClose=false; this is not evidence about the user's earlier force-enabled trial. No Playnite process was running during the initial inspection. Installed Playnite was not launched or terminated for validation.

The previous WM_CLOSE broadcast included hidden internal windows. Windows' default close handler destroys a window; that does not ensure app shutdown ([WM_CLOSE contract](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-close)). A controlled fixture with a hidden internal window reproduced damage to infrastructure and a stranded process: the new regression failed against the old cleanup. After filtering to titled windows with a system menu and excluding tool windows, the same fixture closes normally. Another test preserves support for an eligible hidden main window. Two further cases verify a disappearing UI leaves a running process in NeedsAttention without force and that force really terminates the retained process. This establishes a generic shutdown fix, not a reproduction of the user's full Playnite setup or every force-enabled failure.

Normal close and force quit still operate only on owned identities. No speculative Playnite command adapter, global shutdown command, process-name kill or descendant-tree termination was introduced. SESS-017 awaits the user's application-specific confirmation. SRS confirmation completes SESS-016's reported start/stop issue; cancellation of UAC was not reported as tested.

Validation for this follow-up: full solution build with zero warnings/errors; **89 checks pass (19 Core, 51 regular App, 19 native)**, including the originally failing internal-window regression, hidden main-window close, and two lingering-process cases. All 41 local documentation links resolve. No user apps or saved configuration were changed.

## Confirmed full stopping as the default — 2026-09-08

Source: explicit user request after finding that three of four apps needed Force quit. The user asked whether the checkbox could represent an even stronger mode. The existing implementation already uses Windows process termination; no stronger per-process tier was added, and whole-tree cleanup would violate existing ownership boundaries. The redundant checkbox/model flag was removed.

End now shows save-work confirmation for the active Session, lists owned apps that may stop, mentions in-flight launches, and defaults focus to Cancel. Enter/Escape cancellation does not stop apps. Approval attempts normal close, waits three seconds, and terminates lingering owned processes. Window-close End-and-close uses its own equivalent warning as approval. Main-app exit and startup failure no longer bypass this confirmation. Startup cancellation/end already confirmed by the user still waits for the acquisition and cleans up only returned owned processes. A cancelled main-exit request is not repeatedly reopened.

Tests verify both legacy forceClose=true/false flags are ignored without modifying the file on load, administrator choice survives, and the next save omits the old field. Native default-stop tests terminate refusing owned processes while leaving pre-existing ones alive. Existing SRS-style handoff, helper identity, hidden-window, lingering-process and Discord independence tests pass. New UI tests cover automatic/failure prompts and deferral during a draft, and keyboard tests cover the explicit dialog in both themes. **Final build: zero warnings/errors; 95 checks pass (23 Core + 53 regular App + 19 native); 41 local links resolve.** No real user apps were stopped or saved definitions changed during validation. Restart the Rider-launched app to use the new runtime policy.

## Maintaining this record

Keep the current handoff concise and current. Add dated findings with their source: user feedback, reproducible manual behaviour, automated test, code inspection, or proposal. Preserve the original observation when a later fix changes the result, and link the backlog item that resolves it. Do not record an unperformed check as passing or imply that an idea has been accepted merely because it is in the backlog.
