#Requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][hashtable] $AppExecutables)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$capture = Join-Path $repo "artifacts/public-screenshots/$([Guid]::NewGuid().ToString('N'))"
$destination = Join-Path $repo 'docs/images'
$previousCapture = $env:SESSIONS_SCREENSHOT_DIR
$previousIcons = $env:SESSIONS_SCREENSHOT_APP_EXECUTABLES
foreach ($name in @('Discord', 'Playnite', 'SR-ClientRadio', 'TobiiGameHub')) {
    $path = $AppExecutables[$name]
    if (!$path -or [IO.Path]::GetExtension($path) -ne '.exe' -or !(Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Supply an installed executable for $name in -AppExecutables. Files are read for icons only."
    }
}
Push-Location $repo
try {
    $env:SESSIONS_SCREENSHOT_DIR = $capture
    $env:SESSIONS_SCREENSHOT_APP_EXECUTABLES = ConvertTo-Json $AppExecutables -Compress
    & dotnet test tests/Sessions.App.Tests/Sessions.App.Tests.csproj -c Release --filter FullyQualifiedName~BrandingInteractionTests
    if ($LASTEXITCODE -ne 0) { throw 'Screenshot rendering checks failed.' }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $images = @{
        'brand-detail-False-1440.png' = 'session-light.png'
        'brand-detail-True-1440.png' = 'session-dark.png'
        'brand-editor-True-1440.png' = 'editor-dark.png'
        'brand-end-True-1440.png' = 'end-dark.png'
    }
    foreach ($entry in $images.GetEnumerator()) {
        Copy-Item -LiteralPath (Join-Path $capture $entry.Key) -Destination (Join-Path $destination $entry.Value)
    }
    Write-Host "Public screenshots: $destination"
}
finally {
    $env:SESSIONS_SCREENSHOT_DIR = $previousCapture
    $env:SESSIONS_SCREENSHOT_APP_EXECUTABLES = $previousIcons
    Pop-Location
}
