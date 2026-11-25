[CmdletBinding(DefaultParameterSetName = 'Register')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Register')] [string] $Package,
    [Parameter(Mandatory, ParameterSetName = 'Register')] [string] $InstallDirectory,
    [Parameter(Mandatory, ParameterSetName = 'Remove')] [switch] $Remove
)
$ErrorActionPreference = 'Stop'
if ($Remove) {
    Get-AppxPackage -Name 'gsapplabs.Opsis.Explorer' | Remove-AppxPackage
    return
}
if ([Environment]::OSVersion.Version.Build -lt 22621) { throw 'The compact desktop menu requires Windows 11 build 22621 or newer.' }
$packagePath = (Resolve-Path -LiteralPath $Package).Path
$app = (Resolve-Path -LiteralPath $InstallDirectory).Path
if (!(Test-Path -LiteralPath (Join-Path $app 'Opsis.exe') -PathType Leaf)) { throw 'The installed Opsis executable is missing.' }
Add-AppxPackage -Path $packagePath -ExternalLocation $app
