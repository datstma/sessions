[CmdletBinding()]
param([Parameter(Mandatory)][string] $PublishDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$assets = Get-Content (Join-Path $repo 'src/Sessions.App/obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
$deps = Get-Content (Join-Path $PublishDirectory 'Sessions.App.deps.json') -Raw | ConvertFrom-Json -AsHashtable
$noticeRoot = Join-Path $PublishDirectory 'licenses'
New-Item -ItemType Directory -Path $noticeRoot -Force | Out-Null
$inventory = @()
foreach ($entry in $deps.libraries.GetEnumerator() | Sort-Object Key) {
    if ($entry.Value.type -ne 'package') { continue }
    $packageDirectory = $null
    foreach ($folder in $assets.packageFolders.Keys) {
        $candidate = Join-Path $folder $entry.Value.path
        if (Test-Path -LiteralPath $candidate) { $packageDirectory = $candidate; break }
    }
    if (!$packageDirectory) { throw "Cannot locate package notices for $($entry.Key)." }
    $destination = Join-Path $noticeRoot ($entry.Key.Replace('/', '-'))
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $nuspec = Get-ChildItem -LiteralPath $packageDirectory -Filter '*.nuspec' | Select-Object -First 1
    [xml] $metadata = Get-Content -LiteralPath $nuspec.FullName -Raw
    $licenseNode = $metadata.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='license']")
    $urlNode = $metadata.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='projectUrl']")
    Copy-Item -LiteralPath $nuspec.FullName -Destination $destination
    $notices = @(Get-ChildItem -LiteralPath $packageDirectory -File -Recurse |
        Where-Object Name -Match '^(LICENSE|LICENCE|COPYING|COPYRIGHT|NOTICE|THIRD.PARTY.NOTICES|OFL)(\..*)?$')
    foreach ($notice in $notices) {
        $relative = [IO.Path]::GetRelativePath($packageDirectory, $notice.FullName)
        $target = Join-Path $destination $relative
        New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $notice.FullName -Destination $target
    }
    $inventory += [ordered]@{
        package = $entry.Key
        license = if ($licenseNode) { $licenseNode.InnerText } else { '' }
        projectUrl = if ($urlNode) { $urlNode.InnerText } else { '' }
        packageNotices = @($notices | ForEach-Object { [IO.Path]::GetRelativePath($packageDirectory, $_.FullName) })
    }
}
# Runtime notices are outside the NuGet dependency list in a self-contained build.
$runtime = (Get-Content (Join-Path $PublishDirectory 'Sessions.App.runtimeconfig.json') -Raw | ConvertFrom-Json).runtimeOptions.includedFrameworks |
    Where-Object name -EQ 'Microsoft.NETCore.App'
if (!$runtime) { throw 'The payload is not self-contained.' }
$runtimePath = $null
foreach ($folder in $assets.packageFolders.Keys) {
    $candidate = Join-Path $folder "microsoft.netcore.app.runtime.win-x64/$($runtime.version)"
    if (Test-Path -LiteralPath $candidate) { $runtimePath = $candidate; break }
}
if (!$runtimePath) { throw 'Cannot locate bundled .NET runtime license notices.' }
$runtimeDestination = Join-Path $noticeRoot "Microsoft.NETCore.App-$($runtime.version)"
New-Item -ItemType Directory -Path $runtimeDestination -Force | Out-Null
foreach ($name in 'LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT') {
    Copy-Item -LiteralPath (Join-Path $runtimePath $name) -Destination $runtimeDestination
}
$supplemental = Join-Path $repo 'installer/licenses'
if (Test-Path $supplemental) {
    $supplementalDestination = Join-Path $noticeRoot 'supplemental'
    New-Item -ItemType Directory -Path $supplementalDestination -Force | Out-Null
    Get-ChildItem -LiteralPath $supplemental -File | Copy-Item -Destination $supplementalDestination -Force
}
$inventory | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $noticeRoot 'dependencies.json') -Encoding utf8
@'
Sessions is licensed under GPL-3.0-only; see LICENSE in the installation folder.
Dependencies retain their own licenses. This directory includes package license
metadata, available upstream notices, the bundled .NET runtime notices, and
supplemental notices. See dependencies.json for exact dependency versions.
Release maintainers must review this inventory when dependencies change.
'@ | Set-Content (Join-Path $PublishDirectory 'THIRD-PARTY-NOTICES.txt') -Encoding utf8
