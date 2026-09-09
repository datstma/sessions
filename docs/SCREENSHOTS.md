# Sessions in pictures

These screenshots show the **Sessions 0.2.1** interface. The current **0.2.2** preview
adds app icons, safer closing and draft protection. You can
[download the Windows preview](https://github.com/datstma/sessions/releases/tag/v0.2.2).
Sessions follows your system's light or dark appearance.

## Your apps, ready together

A Session keeps its apps and launch settings in one place. Selecting it shows
the setup; **Start Session** opens it. Apps already running stay open when you end.

![Light theme: a Gaming Session with four configured apps and Discord already running.](images/session-light.png)

![The same Session in the dark theme, with indigo actions and a green Running indicator.](images/session-dark.png)

## Make the setup yours

Choose the apps, their order and when the Session ends. Expand app options or
advanced startup settings when you need more control.

![Session editor showing the name, ordered app list, expandable options, and Save and Cancel actions.](images/editor-dark.png)

## Know what will close

Before ending, review the apps Sessions will close and those that will stay open.
In 0.2.2, apps that remain open after a normal close are preserved so you can handle
save prompts. Force quit is an explicit choice unless enabled in an app's options.

![End confirmation separates Playnite, SR-ClientRadio and TobiiGameHub under Will be closed from Discord under Stays open; Cancel has focus.](images/end-dark.png)

These are unmodified renders of the application UI at 1440×900 with isolated
sample data and simulated process status. No installed apps are launched and no
personal Session library is used. Native Windows title bars are outside the capture.
The original design references remain in [branding/screens](../branding/screens/README.md).

To refresh these images from the repository root with the pinned .NET SDK:

```powershell
./scripts/Update-PublicScreenshots.ps1
```

Review the resulting images in both themes before committing them. Keep the
release wording here and in the [README](../README.md) aligned with the version shown.
