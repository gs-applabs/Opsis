# Read the built binary's FileVersion, the one Inno Setup uses.
# Invoked only during the release/installer verification phase.
param([string]$PublishDir = 'publish', [string]$OutFile = 'Output/release-identity.json')
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PublishDir 'Opsis.Studio.exe'
if (-not (Test-Path $exe)) { throw 'Publish Opsis.Studio.exe before generating release identity' }
$version = (Get-Item $exe).VersionInfo.FileVersion
$props = [xml](Get-Content 'Version.props' -Raw)
$source = $props.Project.PropertyGroup.OpsisAppVersion
if ($version -ne $source) { throw "Version mismatch: binary=$version source=$source" }
New-Item -ItemType Directory -Force (Split-Path $OutFile) | Out-Null
[ordered]@{ app=$version; installer=$version; help=$version; update=$version; source='Version.props' } |
  ConvertTo-Json | Set-Content -LiteralPath $OutFile -Encoding utf8
