[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Compiler,
    [string] $CrawlerSetup,
    [string] $CrawlerSha256 = '0971cc56e64f25fe608303144ac18f8854cc1c41f894d4989693ffbff8af0fc5'
)
$ErrorActionPreference = 'Stop'
$compilerPath = (Resolve-Path -LiteralPath $Compiler).Path
$repo = Split-Path $PSScriptRoot
$destination = Join-Path $repo 'publish'
$dotnetProject = Join-Path $repo 'Opsis.csproj'
$installer = Join-Path $repo 'installer.iss'
$arguments = @("/DPublishDir=$destination")
if ($CrawlerSetup) {
    $crawlerPath = (Resolve-Path -LiteralPath $CrawlerSetup).Path
    $hash = (Get-FileHash -LiteralPath $crawlerPath -Algorithm SHA256).Hash
    if ($hash -ne $CrawlerSha256) { throw 'The Crawler setup does not match the selected release SHA-256.' }
    $arguments += "/DCrawlerSetup=$crawlerPath"
}

dotnet publish $dotnetProject --configuration Release --runtime win-x64 --self-contained false -o $destination
if ($LASTEXITCODE -ne 0) { throw 'Opsis publish failed.' }
# Verify the approved intro (with audio) was included in the publish output.
$introFile = Join-Path $destination 'assets/intro/intro.webm'
if (-not (Test-Path -LiteralPath $introFile -PathType Leaf)) { throw 'Approved OPSIS intro is missing from publish.' }
if ((Get-FileHash -LiteralPath $introFile -Algorithm SHA256).Hash -ne '398e8564ff27401011067a1f29bffa9c7ce134e6d30caf4b62ff2fd5f970daa6') { throw 'Published OPSIS intro SHA-256 mismatch.' }
& (Join-Path $repo 'tools/emit-release-identity.ps1') -PublishDir $destination -OutFile (Join-Path $repo 'Output/release-identity.json')
& (Join-Path $PSScriptRoot 'explorer/Build-Explorer.ps1') -OutputDirectory $destination -AllowClassicMenu
& $compilerPath @arguments $installer
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
