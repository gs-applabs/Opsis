# Manual-only future verification, never run against user profile files.
# Crash/FailsFast is confined to a disposable child process and a temporary dir.
param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$proj = Join-Path $repo 'tests/AtomicWriteCrashHarness/AtomicWriteCrashHarness.csproj'
$dir = Join-Path $env:TEMP ("opsis-atomic-qa-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dir | Out-Null
try {
  dotnet build $proj -c $Configuration --nologo
  if ($LASTEXITCODE -ne 0) { throw 'harness build failed' }
  $dll = Join-Path $repo "tests/AtomicWriteCrashHarness/bin/$Configuration/net11.0/AtomicWriteCrashHarness.dll"
  foreach ($stage in @('after-temp-flush','after-backup')) {
    $path = Join-Path $dir ($stage + '.json')
    & dotnet $dll seed $path
    if ($LASTEXITCODE -ne 0) { throw 'seed failed' }
    $env:OPSIS_E2E = '1'; $env:OPSIS_ATOMIC_CRASH_STAGE = $stage
    & dotnet $dll write $path 2>$null
    $crashCode = $LASTEXITCODE
    Remove-Item Env:OPSIS_ATOMIC_CRASH_STAGE; Remove-Item Env:OPSIS_E2E
    if ($crashCode -eq 0) { throw "Crash injection was not activated: $stage" }
    $json = (& dotnet $dll read $path) | ConvertFrom-Json
    if ($json.value -ne 'old') { throw "Recovery lost the last valid committed settings at $stage" }
  }
  $first = Join-Path $dir 'first.json'
  $env:OPSIS_E2E = '1'; $env:OPSIS_ATOMIC_CRASH_STAGE = 'after-temp-flush'
  & dotnet $dll write $first 2>$null
  if ($LASTEXITCODE -eq 0) { throw 'First-save crash injection was not activated' }
  Remove-Item Env:OPSIS_ATOMIC_CRASH_STAGE; Remove-Item Env:OPSIS_E2E
  $result = (& dotnet $dll read $first) | ConvertFrom-Json
  if ($result.value -ne 'new') { throw 'Could not recover a completed first-write temp file' }
  'Atomic-write process-crash recovery: PASS (NOT a physical power-loss guarantee)'
}
finally {
  Remove-Item Env:OPSIS_ATOMIC_CRASH_STAGE -ErrorAction SilentlyContinue
  Remove-Item Env:OPSIS_E2E -ErrorAction SilentlyContinue
  Remove-Item $dir -Force -Recurse -ErrorAction SilentlyContinue
}
