# Opsis 2.8.0.2500 R9 Windows release gate. Run ONLY on disposable Windows VM / CI runner.
# Requires: .NET 11 SDK, Node 22+, Inno Setup 7, an interactive desktop for --ui-audit.
# Optionally set -PreviousInstaller to a SHA-verified older Opsis.Studio installer for upgrade coverage.
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$Compiler,
  [string]$PreviousInstaller,
  [string]$PreviousSha256,
  [string]$LegacyInstaller,
  [string]$LegacySha256,
  [switch]$SkipUiAudit,
  [switch]$AllowLocalDisposableVm
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Windows-only release QA: use a disposable Windows runner.' }
if ($env:GITHUB_ACTIONS -ne 'true' -and -not $AllowLocalDisposableVm) {
  throw 'This script alters Windows installation state. Run on ephemeral CI or explicitly pass -AllowLocalDisposableVm.'
}
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [System.IO.Path]::GetTempPath() }
$qa = Join-Path $repo 'qa-results'
New-Item -ItemType Directory -Force $qa | Out-Null
$results=[ordered]@{version='2.8.0.2500'; runner=$env:RUNNER_OS; startedUtc=[DateTime]::UtcNow.ToString('o'); stages=[ordered]@{}; notes=@()}
function Step([string]$name,[scriptblock]$block) {
  try { & $block; $results.stages[$name]='PASS'; Write-Host "PASS $name" }
  catch { $results.stages[$name]='FAIL'; $results.notes+= "$name`: $($_.Exception.Message)"; throw }
  finally { $results | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $qa 'release-gate.json') -Encoding utf8 }
}
function CheckExit([string]$what) { if ($LASTEXITCODE -ne 0) { throw "$what failed ($LASTEXITCODE)" } }
function Installer([string]$file,[string[]]$params,[string]$stage) {
  $proc=Start-Process -FilePath $file -ArgumentList $params -Wait -PassThru
  if ($proc.ExitCode -ne 0) { throw "$stage exited with $($proc.ExitCode)" }
}
$compiler=(Resolve-Path -LiteralPath $Compiler).Path
$env:OPSIS_APPDATA=Join-Path $tempRoot 'opsis-r9-qa-appdata'
$env:OPSIS_E2E='1'
$env:OPSIS_FORCE_TIER='Low'
$env:OPSIS_AUDIT_RESOURCE_CYCLES='3'
New-Item -ItemType Directory -Force $env:OPSIS_APPDATA | Out-Null
try {
  Step 'preflight' {
    if (!(Test-Path $compiler)) { throw 'Inno Setup 7 compiler unavailable' }
    $sdk=(& dotnet --version); CheckExit 'dotnet --version'
    if ($sdk -notmatch '^11\.') { throw "Expected .NET 11 SDK, got $sdk" }
    node --version | Write-Host; CheckExit 'node --version'
    $results.dotnetSdk=$sdk
    $results.dotnetRuntimes=(& dotnet --list-runtimes) -join ' | '
    $results.webView2Machine=(Test-Path 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F1E7DD42-BA0A-4C84-9FB7-0AB451719730}')
    $results.notes+='Existing .NET 10/11 and WebView2 states are recorded; missing-runtime scenarios require a separate clean VM.'
  }
  Step 'source-regressions' { Push-Location $repo; try { node --test; CheckExit 'Node regression tests' } finally { Pop-Location } }
  Step 'dotnet-publish-and-installer' {
    Push-Location $repo
    try { & (Join-Path $repo 'installer/build.ps1') -Compiler $compiler; if ($LASTEXITCODE -ne 0) { throw 'Build failed' } }
    finally { Pop-Location }
    $app=Join-Path $repo 'publish/Opsis.Studio.exe'
    if (!(Test-Path $app)) { throw 'Published exe is missing' }
    if ((Get-Item $app).VersionInfo.FileVersion -ne '2.8.0.2500') { throw 'Published exe has wrong version' }
    $setup=Get-ChildItem (Join-Path $repo 'Output') -Filter 'OpsisSetup-2.8.0.2500.exe' | Select-Object -First 1
    if (!$setup) { throw 'Installer was not generated' }
    $results.installerHash=(Get-FileHash $setup.FullName -Algorithm SHA256).Hash
    $results.installer=$setup.FullName
  }
  Step 'dotnet-selftest' {
    & (Join-Path $repo 'publish/Opsis.Studio.exe') --run-selftest
    CheckExit 'Opsis .NET 11 self-tests'
  }
  Step 'classic-explorer-fallback' {
    $classic = Join-Path $tempRoot 'opsis-r9-classic'
    New-Item -ItemType Directory -Force $classic | Out-Null
    & (Join-Path $repo 'installer/explorer/Build-Explorer.ps1') -OutputDirectory $classic -ForceClassicMenu
    if (Test-Path (Join-Path $classic 'OpsisExplorer.dll')) { throw 'Classic fallback incorrectly shipped native extension' }
    $menu=Get-Content (Join-Path $repo 'src/Services/DesktopMenu.cs') -Raw
    foreach ($verb in @('new-container','prev-wallpaper','next-wallpaper','toggle-wallpaper','stop-wallpaper')) {
      if (!$menu.Contains('("' + $verb + '"')) { throw "Missing Explorer command $verb" }
    }
    # This is a static fallback construction check; register/test all five commands below.
  }
  Step 'classic-explorer-five-commands' {
    $app=Join-Path $repo 'publish/Opsis.Studio.exe'
    # The standalone registration API selects classic mode only if OpsisExplorer.dll is absent.
    $sandbox=Join-Path $tempRoot 'opsis-r9-classic-exe'
    New-Item -ItemType Directory -Force $sandbox | Out-Null
    Copy-Item $app (Join-Path $sandbox 'Opsis.Studio.exe') -Force
    # .NET apphost also needs its deps/runtime files; use full published directory without native extension.
    Copy-Item (Join-Path $repo 'publish/*') $sandbox -Recurse -Force
    Remove-Item (Join-Path $sandbox 'OpsisExplorer.dll') -Force -ErrorAction SilentlyContinue
    & (Join-Path $sandbox 'Opsis.Studio.exe') --setup-desktop-menu on el
    CheckExit 'Register classic commands'
    try {
      foreach ($name in @('1-new-container','2-prev-wallpaper','3-next-wallpaper','4-toggle-wallpaper','5-stop-wallpaper')) {
        $key = 'HKCU:\Software\Classes\DesktopBackground\Shell\OpsisStudio\shell' + $name + '\command'
        $val = (Get-Item -LiteralPath $key -ErrorAction Stop).GetValue('')
        if ($val -notmatch '--desktop-action') { throw "Missing registered Explorer verb: $name" }
      }
    } finally {
      & (Join-Path $sandbox 'Opsis.Studio.exe') --setup-desktop-menu off el
      CheckExit 'Unregister classic menu'
    }
    if (Test-Path 'HKCU:\Software\Classes\DesktopBackground\Shell\OpsisStudio') { throw 'Classic menu keys left behind' }
    $results.notes+='Native C++ Explorer path is separate: test on a runner with Visual C++ x64 Build Tools.'
    $results.stages['native-explorer-menu']='NOT_VERIFIED'
  }
  if (-not $SkipUiAudit) {
    Step 'ui-audit' {
      $out=Join-Path $qa 'ui-audit'
      New-Item -ItemType Directory -Force $out | Out-Null
      $p=Start-Process (Join-Path $repo 'publish/Opsis.Studio.exe') -ArgumentList @('--ui-audit', '"' + $out + '"') -PassThru
      if (-not $p.WaitForExit(1500000)) { Stop-Process -Id $p.Id -Force; throw 'UI audit timed out' }
      if ($p.ExitCode -ne 0) { throw "UI audit exit $($p.ExitCode)" }
      if (!(Test-Path (Join-Path $out 'ui-audit.md'))) { throw 'UI audit report missing' }
    }
  } else { $results.stages['ui-audit']='NOT_VERIFIED' }
  if ($PreviousInstaller) {
    Step 'verify-previous-installer' {
      $prior=(Resolve-Path $PreviousInstaller).Path
      if (-not $PreviousSha256 -or (Get-FileHash $prior -Algorithm SHA256).Hash.ToLowerInvariant() -ne $PreviousSha256.ToLowerInvariant()) {
        throw 'Older installer must be SHA-256 verified before upgrade test'
      }
    }
  }
  Step 'installer-install-upgrade-uninstall' {
    $dest=Join-Path $env:ProgramFiles 'Opsis Desktop Customization'
    $setup=$results.installer
    try {
      if ($PreviousInstaller) {
        Installer (Resolve-Path $PreviousInstaller).Path @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/DEPS=none') 'previous install'
      } else {
        $results.notes+='Upgrade-from-previous-release NOT VERIFIED (no verified previous installer supplied).'
        $results.stages['upgrade-from-previous']='NOT_VERIFIED'
      }
      Installer $setup @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/DEPS=none',"/LOG=`"$(Join-Path $qa 'install.log')`"") 'Opsis 2.8 install/upgrade'
      $app=Join-Path $dest 'Opsis.Studio.exe'
      if (!(Test-Path $app)) { throw "Opsis installer did not produce $app" }
      if ((Get-Item $app).VersionInfo.FileVersion -ne '2.8.0.2500') { throw 'Installed version mismatch' }
      $uninstaller=Join-Path $dest 'unins000.exe'
      if (!(Test-Path $uninstaller)) { throw 'Uninstaller missing' }
      $reg=Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{0F9363FA-64CB-45CF-8D70-E748B9C5BDA8}_is1' -ErrorAction SilentlyContinue
      if (!$reg) { throw 'Registry uninstall entry missing' }
      Installer $uninstaller @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') 'Opsis uninstall'
      if (Test-Path $app) { throw 'Opsis executable remains after uninstall' }
    } finally {
      Get-Process Opsis.Studio -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
  }
  Step 'side-by-side-legacy-folder-guard' {
    # The old branch's Opsis.exe must never be silently overwritten by Opsis.Studio.exe.
    $iss=(Get-Content (Join-Path $repo 'installer.iss') -Raw)
    if (!$iss.Contains('FileExists(ExpandConstant(' + "'{app}\Opsis.exe'" + '))')) {
      throw 'Installer legacy-folder guard is absent'
    }
  }
  if ($LegacyInstaller) {
    Step 'legacy-2.4.6.1-side-by-side' {
      $legacy=(Resolve-Path $LegacyInstaller).Path
      if (!$LegacySha256 -or (Get-FileHash $legacy -Algorithm SHA256).Hash.ToLowerInvariant() -ne $LegacySha256.ToLowerInvariant()) {
        throw 'Legacy 2.4.6.1 installer must be SHA-256 verified'
      }
      $legacyDir=Join-Path $env:ProgramFiles 'Opsis Legacy 2.4.6.1 QA'
      try {
        Installer $legacy @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/DEPS=none',"/DIR=`"$legacyDir`"") 'legacy side-by-side install'
        if (!(Test-Path (Join-Path $legacyDir 'Opsis.exe'))) { throw 'Legacy Opsis.exe missing' }
        $modernDir=Join-Path $env:ProgramFiles 'Opsis Desktop Customization'
        Installer $results.installer @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/DEPS=none') 'modern side-by-side install'
        if (!(Test-Path (Join-Path $modernDir 'Opsis.Studio.exe'))) { throw 'Modern Opsis.Studio.exe missing' }
        if (!(Test-Path (Join-Path $legacyDir 'Opsis.exe'))) { throw 'Modern setup destroyed legacy executable' }
        # Concurrent UI launch is permitted only if each instance stays isolated.
        $old = Start-Process (Join-Path $legacyDir 'Opsis.exe') -PassThru
        $new = Start-Process (Join-Path $modernDir 'Opsis.Studio.exe') -PassThru
        Start-Sleep -Seconds 8
        if ($old.HasExited -or $new.HasExited) { throw 'Side-by-side app unexpectedly exited' }
        Stop-Process $old.Id,$new.Id -Force
      } finally {
        Get-Process Opsis,Opsis.Studio -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        foreach ($dir in @($legacyDir,(Join-Path $env:ProgramFiles 'Opsis Desktop Customization'))) {
          $un=Join-Path $dir 'unins000.exe'
          if (Test-Path $un) { Installer $un @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') 'side-by-side cleanup' }
        }
      }
    }
  } else {
    $results.stages['legacy-2.4.6.1-side-by-side']='NOT_VERIFIED'
    $results.notes+='Side-by-side runtime against 2.4.6.1 NOT VERIFIED without an approved, SHA-256 verified old installer.'
  }

} finally {
  $results.completedUtc=[DateTime]::UtcNow.ToString('o')
  $results | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $qa 'release-gate.json') -Encoding utf8
}
