# Sessions

**Get your apps together. Get on with your day.**

Work, fly, stream, build. Each activity comes with its own collection of apps—and the same routine of opening them all again.

Sessions is a local-first Windows desktop app that lets you save that collection as a named **Session**. Choose it, start it, and see what's running. When you're finished, Sessions stops the apps it opened and leaves apps that were already running alone.

No account. No cloud sync. Your setup stays on your computer.

[How it works](#how-it-works) · [Downloads](#downloads) · [Build from source](#build-from-source) · [Project direction](#project-direction)

## A setup for whatever you're doing

| Session | Apps you might bring together |
| --- | --- |
| Work | Your browser, chat app, and notes |
| Flight sim | Your simulator, radio client, and cockpit utilities |
| Streaming | OBS, chat, and the app you're sharing |
| Development | Your editor, terminal, and browser |

Give each Session its own app order and launch options. You can end it manually, or choose an app whose exit prompts you to end the Session. A main app is optional.

## How it works

1. **Create a Session.** Give it a name you'll recognise in the sidebar.
2. **Add your apps.** Search or scroll through Start menu apps, choose from running apps, or browse for an executable. Select several at once.
3. **Make it yours.** Arrange the opening order and choose when the Session ends. Arguments, working folders, and administrator launch settings are available when needed.
4. **Start the Session.** Apps open in order. Matching apps that are already running stay open without becoming part of its cleanup.
5. **End when you're ready.** Review which apps will stop, save your work, and confirm.

Selecting a Session only shows its details; starting it is a separate action. Creating or editing one never launches its apps.

### Small details that make daily use easier

- **Find apps by name.** Start menu discovery keeps supported shortcuts' arguments, working folders, and administrator settings.
- **See what's running.** A green Running button brings an app's window forward. Outside an active Session, click Not running to open an individual app.
- **Keep your place.** Search and switch between app sources without losing checked selections.
- **Stay oriented.** An active Session remains visible while you browse your other saved setups.
- **Use either theme.** The interface follows your system's light or dark appearance.
- **Edit deliberately.** Save or cancel a draft; confirm before deleting a Session. Deleting its setup does not uninstall its apps.

## What happens when a Session ends?

Sessions remembers which processes it started for that run. An app that was already open stays yours to close.

For example, if your chat app is already running when you start a Streaming Session, ending that Session leaves chat open and stops the apps Sessions opened for it.

**Save your work before confirming End Session.** After confirmation, Sessions requests a normal close, then force quits an owned app if it remains running. This also handles apps that normally minimise to the tray when closed; unsaved changes can be lost.

If an app cannot be stopped or safely tracked, Sessions explains the problem and offers recovery where available. Closing Sessions during an active run lets you end the run or leave its apps open. Apps opened individually remain independent of a later Session's cleanup.

## Downloads

**Sessions 0.1.0 is a development preview.** Download the
[Windows x64 MSI](https://github.com/datstma/sessions/releases/download/v0.1.0/Sessions-0.1.0-win-x64.msi)
from [GitHub Releases](https://github.com/datstma/sessions/releases/tag/v0.1.0).
The installer includes .NET and installs for your Windows user, with a Start menu
shortcut. Installer and application binaries are unsigned.

Close Sessions before installing, updating, or uninstalling. Updates and uninstall
preserve your saved Sessions. To update, download and run a newer MSI; there is no
automatic updater. The release page includes checksums, source, and validation limits.

See [Building and releasing Sessions](docs/RELEASING.md) for versioning and packaging.

### Current boundaries

- Windows desktop apps are the supported target. macOS and Linux are not implemented.
- One Session can run at a time.
- Start menu discovery supports desktop shortcuts; it is not a complete catalogue of Microsoft Store apps. Packaged-app activation is not supported.
- General launcher handoffs, background services, and entire process trees are not automatically managed. Some apps may need manual handling.
- Running apps without an accessible window can show their status but cannot be brought forward by the Running control.
- Sessions opens apps with saved launch settings; it does not capture your current windows, documents, browser tabs, or desktop layout.
- Save or cancel edits before closing the app. General protection for unsaved drafts outside an active run is still on the backlog.

## Build from source

Use Windows with **.NET SDK 10.0.400**, pinned in [global.json](global.json).
Rider is optional; the command line is enough.

Clone the repository and run these commands:

```powershell
git clone https://github.com/datstma/sessions.git
cd sessions
dotnet build Sessions.slnx
dotnet run --project src/Sessions.App/Sessions.App.csproj
```

If you downloaded the source instead, open a terminal in the extracted repository and run the two `dotnet` commands.

In Rider, open `Sessions.slnx` and run `Sessions.App`.

The main app runs without administrator privileges. Windows may ask for approval when you explicitly launch an app as administrator or when cleanup needs permission to stop an elevated app.

### Run the tests

```powershell
dotnet test tests/Sessions.Core.Tests/Sessions.Core.Tests.csproj
dotnet test tests/Sessions.App.Tests/Sessions.App.Tests.csproj
```

Core tests cover execution, ownership, ordering, persistence, and failure handling. App tests cover interaction and rendered UI. Native Windows checks are opt-in; see the [verification instructions](docs/DEVELOPMENT_NOTES.md#previous-stopping-point--end-of-day-2026-09-08) for enabling them.

### Where your Sessions live

Your saved library is readable JSON at:

```text
%LOCALAPPDATA%\Sessions\sessions.json
```

It stores Session definitions and launch settings. Live process ownership is kept only for the current run. Built-in import/export and cloud synchronisation are not implemented.

## Project direction

The immediate focus is reliable app startup and cleanup, clear feedback, and a UI that makes the next step obvious. Upcoming usability work includes draft protection, accessibility and display-scaling checks, and better guidance for invalid fields.

Broader ideas—such as audio-device changes, power settings, and additional action types—are future possibilities, not features in the current app or promised release dates.

- [Product definition](docs/PRODUCT.md): intent, behaviour, and scope.
- [Architecture](docs/ARCHITECTURE.md): how the app is structured and why.
- [Backlog](docs/BACKLOG.md): completed work, open issues, and evidence.
- [Development notes](docs/DEVELOPMENT_NOTES.md): decisions, findings, and the latest handoff.

## Help shape Sessions

Useful feedback starts with the activity you're trying to set up. [Open an issue](https://github.com/datstma/sessions/issues) with the app involved, reproduction steps, expected behaviour, actual behaviour, and any message Sessions displays. Mention whether the app was already running or was started by the Session—this matters for cleanup.

Before proposing a substantial change, read the product scope and architecture. Keep execution logic in Core, Windows integration in App services, and UI behaviour in the presentation layer. [AGENTS.md](AGENTS.md) records the repository's development and validation conventions.

## License

Copyright (c) 2026 datstma and contributors.

Sessions is licensed under the **GNU General Public License, version 3 only** (`GPL-3.0-only`). See [LICENSE](LICENSE) for the full terms.

You may use, modify, and redistribute Sessions, including commercially, under those terms. If you distribute modified versions, they must remain GPLv3-licensed and recipients must have access to the corresponding source code. Sessions is provided without warranty; see the license for details.

Third-party dependencies and assets retain their own licenses.
