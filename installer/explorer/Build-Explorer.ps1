[CmdletBinding()]
param([Parameter(Mandatory)] [string] $OutputDirectory, [switch] $AllowClassicMenu)
$ErrorActionPreference = 'Stop'
$out = (New-Item -ItemType Directory -Force $OutputDirectory).FullName
$compiler = Get-Command cl.exe -ErrorAction SilentlyContinue
if ($env:VSCMD_ARG_TGT_ARCH -and $env:VSCMD_ARG_TGT_ARCH -ne 'x64') { $compiler = $null }
if (!$compiler) {
    $vswhere = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft Visual Studio\Installer\vswhere.exe')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1

    if ($vswhere) {
        $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        if ($LASTEXITCODE -eq 0 -and $installation) {
            $vcvars = Join-Path $installation 'VC\Auxiliary\Build\vcvarsall.bat'
            if (Test-Path -LiteralPath $vcvars) {
                $environment = & $env:ComSpec /d /c "`"$vcvars`" x64 >nul && set"
                if ($LASTEXITCODE -eq 0) {
                    foreach ($entry in $environment) {
                        if ($entry -match '^([^=]+)=(.*)$') {
                            [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2], 'Process')
                        }
                    }
                }
            }
        }
    }

    if ($env:VSCMD_ARG_TGT_ARCH -eq 'x64') { $compiler = Get-Command cl.exe -ErrorAction SilentlyContinue }
}
if (!$compiler) {
    if ($AllowClassicMenu) {
        $oldDll = Join-Path $out 'OpsisExplorer.dll'
        if (Test-Path -LiteralPath $oldDll) { Remove-Item -LiteralPath $oldDll }
        Write-Warning 'Using the classic Explorer menu. The native x64 C++ compiler is unavailable.'
        return
    }
    throw 'The Visual C++ x64 build tools were not found. Install the Visual Studio C++ build tools with the x64/x86 compiler component, then retry.'
}
$source = Join-Path $PSScriptRoot 'OpsisExplorer.cpp'
$dll = Join-Path $out 'OpsisExplorer.dll'
$obj = Join-Path $out 'OpsisExplorer.obj'
& $compiler.Source /nologo /utf-8 /std:c++17 /O2 /W4 /LD /MT "/Fo$obj" "/Fe$dll" $source /link "/IMPLIB:$out\OpsisExplorer.lib" shlwapi.lib shell32.lib ole32.lib user32.lib advapi32.lib uuid.lib
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $dll)) { throw 'Opsis Explorer extension compilation failed.' }
Remove-Item -LiteralPath @($obj, (Join-Path $out "OpsisExplorer.lib"), (Join-Path $out "OpsisExplorer.exp")) -ErrorAction SilentlyContinue
