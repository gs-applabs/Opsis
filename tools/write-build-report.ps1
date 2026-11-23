# Capture exact failed CI gate and compiler toolchain for the 2.8 release.
# This runs on failures too; it never declares the installer ready or executes tests.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutFile)
$ErrorActionPreference = 'Continue'
function CommandVersion([string]$name, [string[]]$args) {
  $command = Get-Command $name -ErrorAction SilentlyContinue
  if (-not $command) { return 'NOT_INSTALLED' }
  try { return (($(& $name @args 2>&1 | Select-Object -First 1) | Out-String).Trim()) }
  catch { return 'VERSION_UNAVAILABLE' }
}
$stages = [ordered]@{
  installer = $env:OP_INSTALLER
  selftests = $env:OP_SELFTESTS
  manifest = $env:OP_MANIFEST
  audit = $env:OP_AUDIT
}
$firstFailure = ($stages.GetEnumerator() | Where-Object { $_.Value -eq 'failure' } | Select-Object -First 1).Key
$report = [ordered]@{
  schemaVersion = 1
  releaseTarget = '2.8.0.2500'
  reportType = 'CI_DIAGNOSTICS_NOT_RELEASE_CERTIFICATION'
  createdUtc = [DateTime]::UtcNow.ToString('o')
  runner = $env:RUNNER_OS
  workflow = $env:GITHUB_WORKFLOW
  gitCommit = $env:GITHUB_SHA
  stageOutcomes = $stages
  failedAt = if ($firstFailure) { $firstFailure } else { $null }
  toolchain = [ordered]@{
    dotnetSdk = CommandVersion 'dotnet' @('--version')
    node = CommandVersion 'node' @('--version')
    pwsh = $PSVersionTable.PSVersion.ToString()
    inno7 = if (Test-Path "${env:ProgramFiles}\Inno Setup 7\ISCC.exe") { 'installed' } else { 'not detected in default Program Files' }
  }
}
$folder = Split-Path -Parent $OutFile
if ($folder) { New-Item -ItemType Directory -Force $folder | Out-Null }
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutFile -Encoding utf8
Write-Host "Build diagnostics stored: $OutFile; first failed gate: $firstFailure"
