# Shared by local packaging and GitHub Actions. MSI only compares three fields.
[CmdletBinding()]
param([string] $Tag)
$ErrorActionPreference = 'Stop'
[xml] $props = Get-Content (Join-Path $PSScriptRoot '../Directory.Build.props') -Raw
$version = [string] $props.Project.PropertyGroup.Version
if ($version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw 'Version must contain three numeric components without leading zeros.'
}
$parts = $version.Split('.')
if ([long]$parts[0] -gt 255 -or [long]$parts[1] -gt 255 -or [long]$parts[2] -gt 65535) {
    throw 'Version exceeds Windows Installer limits (255.255.65535).'
}
if ($Tag -and $Tag -cne "v$version") {
    throw "Tag '$Tag' does not match the configured version v$version."
}
$version
