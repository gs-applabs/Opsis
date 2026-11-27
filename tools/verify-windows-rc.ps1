# Canonical Windows RC verification entry point for GitHub Actions.
[CmdletBinding()]
param([string]$ReportDirectory = 'ci-results')
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Windows is required for the native smoke suite.' }
Set-Location (Join-Path $PSScriptRoot '..')
$report = New-Item -ItemType Directory -Force -Path $ReportDirectory
$state = [ordered]@{ commit = $null; sdk = $null; node = $null; startedUtc = [DateTime]::UtcNow.ToString('o'); stages = [ordered]@{}; result = 'failed' }
$oldData = $env:OPSIS_APPDATA
$oldE2E = $env:OPSIS_E2E
$tempData = Join-Path ([IO.Path]::GetTempPath()) ('opsis-ci-' + [Guid]::NewGuid().ToString('N'))
function Invoke-Stage([string]$Name, [scriptblock]$Action) {
    $state.stages[$Name] = 'running'
    try {
        & $Action
        $state.stages[$Name] = 'passed'
    } catch {
        $state.stages[$Name] = 'failed'
        throw
    }
}
function Invoke-Checked([string]$Exe, [string[]]$Arguments) {
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Exe failed with exit code $LASTEXITCODE" }
}
try {
    $state.commit = (git rev-parse HEAD | Select-Object -First 1)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify checkout commit.' }
    $state.sdk = (dotnet --version | Select-Object -First 1)
    if ($LASTEXITCODE -ne 0 -or $state.sdk -notmatch '^11\.0\.') { throw 'Required .NET 11 SDK unavailable.' }
    $state.node = (node --version | Select-Object -First 1)
    if ($LASTEXITCODE -ne 0 -or $state.node -notmatch '^v(22|23|24|25|26)\.') { throw 'Node 22 or compatible newer supported major is required.' }
    $env:OPSIS_APPDATA = $tempData
    $env:OPSIS_E2E = '1'
    Invoke-Stage 'javascript' {
        Invoke-Checked node @('--check','src/Widgets/wallpaper/weather.js')
        Invoke-Checked node @('--check','src/Music/wallpaper-music.js')
        Invoke-Checked node @('--test','tests/WallpaperIdle.test.cjs','tests/WeatherInfoTone.test.cjs')
    }
    Invoke-Stage 'build' {
        Invoke-Checked dotnet @('build','Opsis.csproj','-c','Release','-p:Platform=x64','--nologo')
    }
    Invoke-Stage 'experience-smoke' {
        Invoke-Checked dotnet @('run','--project','tests/Opsis.ExperienceSmoke/Opsis.ExperienceSmoke.csproj','-c','Release')
    }
    $state.result = 'passed'
} finally {
    $state.finishedUtc = [DateTime]::UtcNow.ToString('o')
    $state | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $report.FullName 'verification.json') -Encoding UTF8
    $env:OPSIS_APPDATA = $oldData
    $env:OPSIS_E2E = $oldE2E
    if (Test-Path -LiteralPath $tempData) { Remove-Item -LiteralPath $tempData -Recurse -Force -ErrorAction SilentlyContinue }
}
