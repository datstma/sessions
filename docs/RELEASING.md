# Building and releasing Sessions

Sessions uses a self-contained Windows x64 publish wrapped in a per-user WiX MSI.
Packaging is separate from `Sessions.slnx`: ordinary development builds do not
require WiX or generate an installer. The MSI contains the .NET runtime and needs
no separate .NET download on the target computer.

## Versioning

Set the version once in [Directory.Build.props](../Directory.Build.props).
Use `major.minor.patch`, starting at `0.1.0`. Bug fixes increment the last field;
feature releases increment the middle field. `1.0.0` is a product-readiness decision.
The installer and application metadata use this value. Do not change the Session
library's JSON schema version simply because the application version changes.

Every published build gets a new version and matching Git tag (`v0.1.0`). Never
move published tags or replace their binaries. MSI compares three numeric fields;
their maximums are `255.255.65535`. Keep preview status in GitHub's **pre-release**
flag instead of adding `-beta` suffixes to the MSI version.

## Build locally

Requirements: Windows x64, PowerShell 7, the exact .NET SDK in
[global.json](../global.json), and NuGet access on the first build. WiX SDK and
extension versions are pinned in the installer project.

WiX 7 requires acceptance of its [build-tool EULA](https://docs.firegiant.com/wix/osmf/).
The project owner authorized acceptance on 2026-09-09; `AcceptEula=wix7` is
configured in the installer project for local and CI builds.
The tool's maintenance-fee terms must be considered separately from Sessions' GPL.

From the repository root:

```powershell
./scripts/Build-Installer.ps1
```

This builds the solution in Release, runs Core and regular App tests, publishes
Windows x64 with the .NET runtime, collects dependency notices, generates the MSI
payload definition, and builds an embedded-cabinet MSI with WiX validation enabled.
Only [ICE91](https://learn.microsoft.com/en-us/windows/win32/msi/ice91) is excluded:
it warns about user folders in per-machine installs, whereas this MSI exclusively
supports per-user installation and rejects `ALLUSERS`. All other ICE checks remain
enabled and warnings are treated as errors.
Trimming and single-file publishing are disabled. Debug symbols are omitted.
The output is `artifacts/releases/0.1.0/Sessions-0.1.0-win-x64.msi` plus a SHA-256
checksum. Substitute the configured version in these paths.

Each build stages a fresh payload and separate WiX intermediate outputs in
`artifacts/installer/<unique-id>/`; old published files or cached installer builds
cannot leak into a new installer. Local outputs for the same
version can be overwritten while developing. `-SkipTests` is available for local
packaging iteration after tests have passed; the release workflow never uses it.
The script does not install Sessions, access your library, or upload anything.

## Installer behavior

- Program files: `%LOCALAPPDATA%\Programs\Sessions` for the installing user.
- Saved library: `%LOCALAPPDATA%\Sessions\sessions.json`, outside installer ownership.
- Start menu shortcut and Windows Installed apps registration.
- A stable upgrade identity; higher versions replace previous versions, and lower
  versions are rejected. Rebuilding a published version is not an update mechanism.
- Uninstall removes installed files and empty program directories, preserving the
  library. It does not delete arbitrary files added to the installation directory.
- A detection-only check blocks install, upgrade, and uninstall while an executable
  named `Sessions.App.exe` is running. This conservatively includes development
  copies. Close Sessions through its normal UI and retry. The installer sends no
  close messages and does not terminate Sessions or any Session apps. Restart
  Manager shutdown is disabled. Silent installations also fail when the check fires.

Installation, installed app launch without a shared .NET runtime, per-user
registration, upgrade, downgrade rejection, running-app refusal, uninstall, and reinstall
have been exercised in a disposable Windows Sandbox. The upgrade used a test-only
0.1.1 MSI over the 0.1.0 package. An ordinary nonadministrator account and installed
real-app/UAC cleanup remain unverified for the 0.1.0 preview and are disclosed in
its release notes. Build success alone does not establish those remaining checks.

The 2026-09-09 offline Sandbox checks preserved the library byte-for-byte, verified
one per-user registration after upgrading, and verified removal of a file absent
from the newer fixture. Fresh MSI operations took approximately two minutes before
proceeding; test timeouts were increased to five minutes. The cause of that delay
has not been established, and no installation-speed claim is made.

## Create a GitHub draft release

1. Update the version and add `docs/release-notes/<version>.md`. Review dependency
   changes and the resulting notices; include corresponding source/build materials.
2. Commit and push the release files. Create and push the matching tag:

   ```powershell
   git tag -a v0.1.0 -m "Sessions 0.1.0"
   git push origin v0.1.0
   ```

3. In GitHub, open **Actions → Build release → Run workflow**. Select the default
   branch for the workflow definition, enter the existing tag, and leave preview
   status enabled for early releases. The workflow must first exist on the default
   branch to appear as a manually runnable workflow.
4. GitHub checks out that exact tag and checks its version, builds and tests on
   Windows, packages the MSI, and archives source from the same commit. A separate
   job creates a draft with the MSI, Sessions source ZIP, matching WiX source ZIP,
   SHA256SUMS.txt, and your release notes. The separate WiX archive supplies source
   for the native utility action embedded in the MSI, under its own MS-RL license.
   Existing releases cause failure rather than replacement. Only this final job
   receives repository write permission; builds run with read permission.
5. Download and test those exact draft assets. Record the checks below, disclose
   any remaining preview validation limits in the notes, then click **Publish release**.

Normal pushes and tags do not automatically trigger releases. A draft remains
unpublished until a maintainer publishes it. Users upgrade by running a newer MSI;
there is no in-app updater.

## Release checks

Use a disposable Windows VM with no separately installed .NET runtime and a
standard user. Never replace the real development library with test fixtures.

- Install, use the Start menu shortcut, create/save a Session, close and reopen.
- Install a higher-version MSI over the previous release and verify the saved
  library and single Installed apps entry. Reject installation of the older MSI.
- While Sessions is running, attempt install/upgrade/uninstall (including silent
  mode) and verify it refuses without closing Sessions or its apps.
- Uninstall and reinstall; verify program/shortcut removal and library preservation.
- Exercise launch and confirmed cleanup in the installed build, including the
  elevated helper path where relevant. Regular CI skips the 20 opt-in native checks;
  a GitHub runner is not a substitute for desktop/UAC and installation testing.
- Compare checksums, inspect packaged license notices, and review matching source
  and build instructions. `Collect-ReleaseNotices.ps1` inventories packages from the
  published dependency manifest and copies available package/runtime notices plus
  checked-in supplemental notices. This assists, but does not replace, review when
  dependencies or embedded assets change.
- Record signing status. This initial configuration produces **unsigned** MSI/app
  binaries; signing and credentials are not configured. If signing is added, sign
  the app before packaging and the MSI before generating its checksum.

## References

- [.NET publishing](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-publish)
- [MSI version rules](https://learn.microsoft.com/en-us/windows/win32/msi/productversion)
- [WiX upgrades](https://docs.firegiant.com/wix/schema/wxs/majorupgrade/)
- [WiX running-app detection](https://docs.firegiant.com/wix/schema/util/closeapplication/)
- [GitHub manual workflows](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow)
- [GitHub releases](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository)
