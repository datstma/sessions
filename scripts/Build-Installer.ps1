#Requires -Version 7.0
[CmdletBinding()]
param([switch] $SkipTests)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (!$IsWindows) { throw 'Windows is required to build the MSI.' }
$repo = Split-Path $PSScriptRoot -Parent
$version = & (Join-Path $PSScriptRoot 'Get-ReleaseVersion.ps1')
# Each run gets a fresh payload so removed files can never leak into a release.
$work = Join-Path $repo "artifacts/installer/$([Guid]::NewGuid().ToString('N'))"
$publish = Join-Path $work 'publish'
$output = Join-Path $repo "artifacts/releases/$version"
New-Item -ItemType Directory -Path $work, $output -Force | Out-Null
function Invoke-DotNet([string[]] $Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE." }
}
Push-Location $repo
try {
    Invoke-DotNet @('build', 'Sessions.slnx', '-c', 'Release')
    if (!$SkipTests) {
        Invoke-DotNet @('test', 'tests/Sessions.Core.Tests/Sessions.Core.Tests.csproj', '-c', 'Release', '--no-build')
        Invoke-DotNet @('test', 'tests/Sessions.App.Tests/Sessions.App.Tests.csproj', '-c', 'Release', '--no-build')
    }
    Invoke-DotNet @('publish', 'src/Sessions.App/Sessions.App.csproj', '-c', 'Release', '-r', 'win-x64',
        '--self-contained', 'true', '-o', $publish, '-p:PublishTrimmed=false', '-p:PublishSingleFile=false',
        '-p:DebugType=None', '-p:DebugSymbols=false')
    & (Join-Path $PSScriptRoot 'Collect-ReleaseNotices.ps1') -PublishDirectory $publish
    $payload = Join-Path $work 'Payload.wxs'
    & (Join-Path $PSScriptRoot 'New-InstallerPayload.ps1') -PublishDirectory $publish -OutputPath $payload
    Invoke-DotNet @('build', 'installer/Sessions.Installer.wixproj', '-c', 'Release',
        "-p:PublishDirectory=$publish", "-p:PayloadSource=$payload", "-p:OutputPath=$work/msi/",
        "-p:IntermediateOutputPath=$work/obj/")
    $name = "Sessions-$version-win-x64.msi"
    $built = Join-Path $work "msi/$name"
    if (!(Test-Path -LiteralPath $built)) { throw "Expected installer not found: $built" }
    $destination = Join-Path $output $name
    Copy-Item -LiteralPath $built -Destination $destination -Force
    $hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name" | Set-Content (Join-Path $output "$name.sha256") -Encoding ascii
    Write-Host "Installer: $destination"
    Write-Host "Payload and build records: $work"
}
finally { Pop-Location }
