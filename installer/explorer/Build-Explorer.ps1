[CmdletBinding()]
param([Parameter(Mandatory)] [string] $OutputDirectory)
$ErrorActionPreference = 'Stop'
$out = (New-Item -ItemType Directory -Force $OutputDirectory).FullName
$compiler = Get-Command cl.exe -ErrorAction SilentlyContinue
if (!$compiler) { throw 'Run from the x64 Native Tools command prompt for Visual Studio.' }
$source = Join-Path $PSScriptRoot 'OpsisExplorer.cpp'
$dll = Join-Path $out 'OpsisExplorer.dll'
$obj = Join-Path $out 'OpsisExplorer.obj'
& $compiler.Source /nologo /utf-8 /std:c++17 /O2 /W4 /LD /MT "/Fo$obj" "/Fe$dll" $source /link "/IMPLIB:$out\OpsisExplorer.lib" shlwapi.lib shell32.lib ole32.lib user32.lib advapi32.lib uuid.lib
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $dll)) { throw 'Opsis Explorer extension compilation failed.' }
Remove-Item -LiteralPath @($obj, (Join-Path $out "OpsisExplorer.lib"), (Join-Path $out "OpsisExplorer.exp")) -ErrorAction SilentlyContinue
