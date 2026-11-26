param(
    [Parameter(Mandatory=$true)][string]$SignatureUiRoot,
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$ui = (Resolve-Path -LiteralPath $SignatureUiRoot).Path
$expected = '4b168dadf3eada5ec6883883ba90c52d4336da74'
$actual = & git -C $ui rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $actual -ne $expected) {
    throw "Use the reviewed Opsis.UI commit $expected in a separate source checkout."
}
$dirty = & git -C $ui status --porcelain --untracked-files=no
if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'Opsis.UI tracked sources must be clean.' }
$vswhere = Join-Path ([Environment]::GetEnvironmentVariable('ProgramFiles(x86)')) 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'Install the x64 Visual C++ tools and Windows SDK.' }
$devcmd = Join-Path $vs 'Common7\Tools\VsDevCmd.bat'
Push-Location $ui
try {
    New-Item -ItemType Directory -Force artifacts | Out-Null
    $build = 'call "' + $devcmd + '" -arch=x64 && cl /nologo /std:c++17 /EHsc /O2 /MT /LD src\Opsis.UI.Composition.Native\CompositionGlass.cpp /Fo:artifacts\CompositionGlass.obj /link /OUT:artifacts\Opsis.UI.Composition.Native.dll d3d11.lib dcomp.lib d2d1.lib dxgi.lib dxguid.lib user32.lib'
    cmd.exe /d /c $build
    if ($LASTEXITCODE -ne 0) { throw 'Native compilation failed.' }
    dotnet test -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Opsis.UI regression tests failed.' }
    dotnet run --project tests/Opsis.UI.Composition.Smoke -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Composition smoke failed.' }
} finally { Pop-Location }
Push-Location $root
try {
    dotnet build Opsis.csproj -c $Configuration -p:EnableSignatureComposition=true "-p:SignatureUiRoot=$ui" -o artifacts/SignaturePreview
    if ($LASTEXITCODE -ne 0) { throw 'Opsis preview build failed.' }
} finally { Pop-Location }
Write-Host 'Preview build: artifacts/SignaturePreview (not the release installer).'
