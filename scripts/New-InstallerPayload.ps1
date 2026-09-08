# WiX's automatic Files harvesting uses file key paths, which fail per-user ICE
# validation. Generate one HKCU-keyed component per file instead.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PublishDirectory,
    [Parameter(Mandatory)][string] $OutputPath
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PublishDirectory)
function Get-Id([string] $Value) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($Value.ToLowerInvariant())
    'P' + [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).Substring(0, 32)
}
function Escape-Xml([string] $Value) { [Security.SecurityElement]::Escape($Value) }
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"><Fragment>')
$directories = @{'INSTALLFOLDER' = ''; 'ProgramsFolder' = '..'}
foreach ($directory in Get-ChildItem -LiteralPath $root -Directory -Recurse | Sort-Object FullName) {
    $relative = [IO.Path]::GetRelativePath($root, $directory.FullName)
    $id = Get-Id "directory/$relative"
    $parentRelative = [IO.Path]::GetDirectoryName($relative)
    $parentId = if ($parentRelative) { Get-Id "directory/$parentRelative" } else { 'INSTALLFOLDER' }
    $lines.Add("<DirectoryRef Id=`"$parentId`"><Directory Id=`"$id`" Name=`"$(Escape-Xml $directory.Name)`" /></DirectoryRef>")
    $directories[$id] = $relative
}
$lines.Add('<ComponentGroup Id="PublishedFiles">')
foreach ($file in Get-ChildItem -LiteralPath $root -File -Recurse | Sort-Object FullName) {
    $relative = [IO.Path]::GetRelativePath($root, $file.FullName)
    $id = Get-Id "file/$relative"
    $parentRelative = [IO.Path]::GetDirectoryName($relative)
    $directoryId = if ($parentRelative) { Get-Id "directory/$parentRelative" } else { 'INSTALLFOLDER' }
    $source = Escape-Xml $file.FullName
    # WiX cannot auto-generate GUIDs for file components with registry key paths.
    # Keep this namespace and path identity stable across builds and upgrades.
    $componentGuid = [Guid]::ParseExact((Get-Id "Sessions/win-x64/per-user/file/$relative").Substring(1), 'N')
    $lines.Add("<Component Id=`"$id`" Directory=`"$directoryId`" Guid=`"$componentGuid`">")
    $lines.Add("<File Id=`"F$id`" Source=`"$source`" KeyPath=`"no`" />")
    $lines.Add("<RegistryValue Root=`"HKCU`" Key=`"Software\Sessions\Installer\Files`" Name=`"$id`" Type=`"integer`" Value=`"1`" KeyPath=`"yes`" />")
    $lines.Add('</Component>')
}
foreach ($id in $directories.Keys | Sort-Object) {
    $lines.Add("<Component Id=`"Cleanup$id`" Directory=`"$id`" Guid=`"*`">")
    $lines.Add("<RemoveFolder Id=`"Remove$id`" On=`"uninstall`" />")
    $lines.Add("<RegistryValue Root=`"HKCU`" Key=`"Software\Sessions\Installer\Folders`" Name=`"$id`" Type=`"integer`" Value=`"1`" KeyPath=`"yes`" />")
    $lines.Add('</Component>')
}
$lines.Add('</ComponentGroup></Fragment></Wix>')
[IO.File]::WriteAllLines($OutputPath, $lines)
