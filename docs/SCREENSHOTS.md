# Sessions in pictures

These screenshots show **Sessions 0.6.0**, including executable icons in the app
list, matching status chips and the safer-closing confirmation. You can
[download the Windows alpha](https://github.com/datstma/sessions/releases/tag/v0.6.1).
The screenshots show the system-following themes; Settings also offers appearance
preferences.

## Your apps, ready together

A Session keeps its apps and launch settings in one place. Selecting it shows
the setup; **Start Session** opens it. Apps already running stay open when you end.

![Light theme: a Gaming Session with Discord, Playnite, SR-ClientRadio and TobiiGameHub icons, and Discord already running.](images/session-light.png)

![The same app icons in the dark theme, with indigo actions and a green Running indicator.](images/session-dark.png)

## Make the setup yours

Choose the apps, their order and when the Session ends. Expand app options or
advanced startup settings when you need more control.

![Session editor showing the name, ordered app list, expandable options, and Save and Cancel actions.](images/editor-dark.png)

## Know what will close

Before ending, review the apps Sessions will close and those that will stay open.
Apps that remain open after a normal close are preserved so you can handle save
prompts. Force quit is an explicit choice unless enabled in an app's options.

![End confirmation separates Playnite, SR-ClientRadio and TobiiGameHub under Asked to close from Discord under Stays open, explaining that apps waiting for you remain open; Cancel has focus.](images/end-dark.png)

These are unmodified renders of the application UI at 1440×900 with isolated
sample data and simulated process status. No installed apps are launched and no
personal Session library is used. Native Windows title bars are outside the capture.
App artwork is read from installed executables through the app's icon loader;
the capture fixture keeps displayed executable paths as sample data. Third-party
icons identify their respective apps and are not bundled into Sessions.
The original design references remain in [branding/screens](../branding/screens/README.md).

To refresh these images from the repository root with the pinned .NET SDK:

```powershell
./scripts/Update-PublicScreenshots.ps1 -AppExecutables @{
    Discord = 'C:\path\to\Discord.exe'
    Playnite = 'C:\path\to\Playnite.DesktopApp.exe'
    'SR-ClientRadio' = 'C:\path\to\SR-ClientRadio.exe'
    TobiiGameHub = 'C:\path\to\TobiiGameHub.exe'
}
```

Replace these paths with the installed executables on your computer. The script
reads their icons without launching them and fails if any required artwork cannot
load, so an accidental fallback-only refresh cannot replace the public images.
Regular tests do not require these apps. The script also renders compact 640×480
views in both themes under ignored `artifacts/public-screenshots` for review.

Review the resulting images in both themes before committing them. Keep the
release wording here and in the [README](../README.md) aligned with the version shown.
