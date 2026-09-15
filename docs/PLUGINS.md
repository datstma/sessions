# Plugins

Sessions 0.5.0 includes Steam as its first bundled plugin. [PRODUCT.md](PRODUCT.md#plugins-and-steam)
defines behavior; [ARCHITECTURE.md](ARCHITECTURE.md#bundled-plugins) defines the
contract and trust boundary.

## Use Steam in a Session

1. Open **Settings → Plugins**. Steam is enabled by default. Leave its installation
   folder blank for automatic detection, or enter the folder containing `steam.exe`.
   Choose **Apply plugins** after changing plugin preferences.
2. Edit a Session, choose **Add app → Plugins**, select installed Steam apps and save.
   Other picker sources still add ordinary apps. Refresh after installing/moving a
   game or reconnecting a library drive.
3. If you want Sessions to close the app, expand its app options and enable
   **Close when this Session ends**, then save. This is off by default. Already-open
   apps and individual launches stay independent of Session cleanup.
4. Start the Session. Steam may ask you to sign in, update or choose launch options.
   The app card shows **Running** when Steam reports it active. **Launch via plugin**
   also sends an individual request, suppressing duplicates while Steam reports it
   running and briefly after a request.
5. End Session when finished. Opted-in, verified processes receive a normal close
   request. If they refuse, check for unsaved work and use Retry End, Leave apps open
   or the separately confirmed Force quit action. Untracked apps need manual closure.
   The shared Steam client and already-running apps stay open.

A successful request does not establish readiness or ownership. The running indicator
uses Steam's own local state and never authorizes cleanup. Steam apps cannot be the
Session's main app or completion focus, wait for a window, request elevation or enable
automatic force quit. Same-plugin entries launch serially even in All at once;
ordered startup keeps existing pauses. Each target can appear only once in a Session.

Opt-in tracking can keep startup (and an End requested during startup) waiting for
up to 30 seconds while Steam opens the app. Only newly observed processes associated
with that Steam app and verified inside its install directory can be retained. Late
launches beyond the capture window, external launchers, unknown running state,
unreadable logs or other unverifiable ownership fall back to manual closing. An app
can show **Running** while remaining unowned. Captured processes are fixed after
startup; later independent launches are never adopted. Steam prompts may delay a
launch beyond the capture window.

Missing clients, disabled plugins and unavailable games block startup before audio
or ordinary app launches. Saved entries remain editable/removable. Discovery reports
unavailable drives and malformed metadata while retaining healthy results. A game
with an incomplete install is visible but cannot be selected. Steam's launch options
remain controlled by Steam; Sessions does not supply custom game arguments.

## Close one running app

Choose **Close…** beside the app's green running status, then confirm **Close app**
after saving your work. This also works for apps opened outside Sessions and does
not require **Close when this Session ends**. Cancel leaves the app open. Other
apps and the shared Steam client stay open; your Session continues.

This requests normal closing and never automatically force quits. Check the app's
own window if it remains open. Sessions captures the currently verified processes
before confirmation, so later copies are unaffected. Steam identification uses
active app-specific records in the last 1 MiB of its process log and checks exact
Windows identities inside the app's installation directory. If a long-running app's
records have fallen outside that range, logs are unavailable, or its processes
cannot be verified, use the app's own window or Steam to close it.

## Preferences and compatibility

Plugin preferences live in `%LOCALAPPDATA%\Sessions\plugins.json`, separately from
appearance and the Session library. Apply and Reset affect plugins only. Closing
Settings discards unapplied edits. Read failures allow retry or explicit reset;
recovery backs up original bytes before replacing the file. Reset restores bundled
defaults and removes custom preferences, including settings for unknown plugins.
Normal saves preserve unknown plugin IDs, settings versions and keys.

Each run captures enabled state and settings at Start. Changes apply to later runs
and individual launches; no restart is needed. Existing runs retain their captured
configuration. Missing/incompatible plugin references are preserved, with launch
errors instead of executable fallback.

Plugin apps require library format v5 or later; published 0.4.0 and earlier cannot
read them. **Saving in 0.6.0 writes format v6**, which 0.5.0 and earlier cannot read.
Sessions reads v1–v6 without rewriting on load and keeps a copy of an older library
on its first save (see the [release notes](release-notes/0.6.0.md#compatibility)).
Steam entries store their name and stable app ID, not an installation path/account.

## Bundled extension boundary

`Sessions.Plugins.IApplicationPlugin` describes metadata, string-valued setting
fields, discovery, validation and launch requests. App composition explicitly
registers compatible implementations; API and settings schema versions are checked.
`Sessions.Plugins.Steam` consumes this boundary and a Windows adapter supplied by App.
Core sees only a captured launch interface and portable plugin reference containing
plugin ID, target ID, version, optional opaque JSON and a CloseOnEnd choice. Providers
may report presence independently. Optional SupportsClose implementations return
verified process lifetimes; both catalog and Core enforce the captured opt-in before
allowing ownership. A separate optional PrepareCloseAsync returns a fixed close request for explicit manual confirmation and does not require CloseOnEnd. Default implementations remain launch-only. Cancellation and
in-flight requests use the existing startup drain/failure rules.

Bundled code is trusted in-process application code with the user's privileges,
not sandboxed. Discovery exceptions are reported per plugin. There are no directory
scans for code, external DLL installation, background plugin services, hot reload,
marketplace or automatic updates. This contract supports app launches and optional verified cleanup;
Hue/Home Assistant actions and third-party loading require later design work.

## Steam implementation references

The Windows adapter finds Steam via `HKCU\Software\Valve\Steam` (`SteamPath`) or
the explicit installation folder. It passes one numeric `steam://run/<appid>`
argument to that installation's `steam.exe` through Windows shell activation and
disposes the returned client handle without tracking it. Valve documents the run
protocol in its [ISteamApps launch-query reference](https://partner.steamgames.com/doc/api/ISteamApps#NewLaunchQueryParameters_t).

Discovery reads `steamapps/libraryfolders.vdf` and `appmanifest_<appid>.acf` in each
local library. It supports current object and legacy path entries, checks matching
positive IDs, an existing install directory inside `steamapps/common`, and the
installed state flag. The bounded parser accepts text files up to 4 MiB and 32
nested objects; discovery examines at most 4,096 manifests per library. These local
metadata layouts were verified with fixtures and the installed client; they are not
treated as a stable public Valve API. Discovery never writes client files or starts
Steam, and uses existing initial-based icons without fetching artwork.

The running indicator reads `HKCU\Software\Valve\Steam\Apps\<appid>\Running`.
Absent/unreadable values are unknown, not evidence that an app stopped. Automatic Session cleanup uses
a pre-launch process baseline and an open cursor on `logs/gameprocess_log.txt`, then
matches new AppID/PID addition records to retained Windows handles. PIDs present in
the baseline are excluded. Full path, Windows login session and creation time must
match the permitted directory and launch/log time bounds. The adapter does not
execute or trust command lines from the log. It also declines ownership if app
processes were already present despite a clear Steam flag.

These registry/log formats were observed in Steam 10.96.30.42, including the user's
3DMark launch; they are undocumented client details, not public Valve lifecycle APIs.
Log rotation/truncation, delayed or simultaneous external launches and inaccessible
processes limit correlation. Tracking retains a fixed set of exact identities,
without process-name kills, client shutdown or later descendant adoption. Cleanup
reuses Sessions' existing normal-close and separately confirmed force-quit paths.

## Validation and remaining trial

Automated tests use isolated libraries and fake launch adapters. They cover saving
and reopening plugin entries, unknown configuration preservation, missing/disabled/
incompatible providers, preflight before side effects, captured settings, ordering,
cancellation, failure handling, owned-process cleanup, picker/editor interaction and
preference recovery. Both themes, compact layouts, enlarged text/interface settings
and 125/150/200% headless rendering are exercised.

An opt-in read-only Windows test (`SESSIONS_READ_STEAM=1`, filter
`SteamClientTests`) on 2026-09-10 detected Steam version **10.96.30.42**, discovered
**35 available apps**, reported one unavailable library and validated one available
target. Its adapter deliberately throws if asked to launch. No real Steam game,
user Session library or Steam settings were changed by these checks.

The user confirmed on 2026-09-10 that **3DMark (Steam app 223850) launches correctly**,
reported the missing running indicator and requested opt-in shutdown. Running state
and optional closing were implemented in response. The latest full validation and
captures are recorded in DEVELOPMENT_NOTES. An opt-in isolated hidden-helper test
(`SESSIONS_RUN_RUNTIME_SMOKE=1`, filter `SteamClientTests`) uses a synthetic Steam log:
it verifies baseline protection, captures only the new exact process, closes it
normally and leaves the pre-existing helper alive. It never launches Steam or games.

The user subsequently confirmed running detection and the corrected launch feedback.
Individual Close has two additional opt-in native helper checks (`NativeManualCloseTests`):
ordinary and synthetic Steam paths close the captured existing app, preserve later
copies and same-name apps in other folders, and leave everything open on Cancel.
No real Steam apps are launched or closed by these tests.

Next trial: reopen the updated Sessions build and try **Close…** on running 3DMark,
including an individually opened copy. Separately, enable its close-on-End option,
save and start a fresh Session with 3DMark initially closed. Confirm End closes the tracked app while Steam stays open. A second trial
with 3DMark already running should preserve it. These real-app cleanup checks,
login/update/launch-option prompts and physical desktop accessibility remain
unverified; SESS-029/030 stay awaiting feedback on the expanded behavior.
