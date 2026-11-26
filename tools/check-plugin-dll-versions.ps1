# PowerShell 7: validate the published .NET plugin DLL identities (not product versions).
# Usage: pwsh tools/check-plugin-dll-versions.ps1 -CrawlerDir <publish> -ChronikologioDir <publish> -HostDir <publish>
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$CrawlerDir,
  [Parameter(Mandatory)][string]$ChronikologioDir,
  [Parameter(Mandatory)][string]$HostDir
)
$ErrorActionPreference = 'Stop'
$specs = @(
  @{ Name='Crawler'; Directory=$CrawlerDir; File='OpsisNewsCrawler.OpsisPlugin.dll'; Assembly='OpsisNewsCrawler.OpsisPlugin'; Version='1.0.0.0' },
  @{ Name='Chronikologio'; Directory=$ChronikologioDir; File='Chronikologio.OpsisPlugin.dll'; Assembly='Chronikologio.OpsisPlugin'; Version='1.0.0.0' },
  @{ Name='Shared contract'; Directory=$HostDir; File='OpsisNewsCrawler.Plugin.Abstractions.dll'; Assembly='OpsisNewsCrawler.Plugin.Abstractions'; Version='1.0.0.0' },
  @{ Name='Shared Opsis.UI'; Directory=$HostDir; File='Opsis.UI.dll'; Assembly='Opsis.UI'; Version='0.6.0.0' }
)
foreach ($spec in $specs) {
  $file = Join-Path $spec.Directory $spec.File
  if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "$($spec.Name): missing $file" }
  $identity = [Reflection.AssemblyName]::GetAssemblyName((Resolve-Path -LiteralPath $file).Path)
  if ($identity.Name -ne $spec.Assembly -or $identity.Version.ToString() -ne $spec.Version) {
    throw "$($spec.Name): incompatible assembly identity $($identity.FullName), expected $($spec.Assembly), Version=$($spec.Version)"
  }
  $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path -LiteralPath $file).Path).FileVersion
  if ($spec.Name -in @('Crawler','Chronikologio') -and $fileVersion -ne '1.0.0.0') {
    throw "$($spec.Name): FileVersion=$fileVersion, expected 1.0.0.0"
  }
  Write-Host "PASS $($spec.Name): $($identity.FullName); FileVersion=$fileVersion"
}
