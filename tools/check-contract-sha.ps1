# Fails when the copies of the plugin contract DLL (OpsisNewsCrawler.Plugin.Abstractions.dll) do not all have the same SHA-256.
#   powershell -File tools\check-contract-sha.ps1 [-Extra path1,path2]
# Checked: libs\ against libs\contract.sha256, the copy in the Crawler repository (reference\ of its plugin tests; sibling folder crawler-p / OpsisNewsCrawler, or $env:OPSIS_CRAWLER_REPO),
# the contract next to a built Opsis (bin\...) and any -Extra path (for example the copy in the Chronikologio repository or in an installed plugin folder).
param([string[]]$Extra = @())
$ErrorActionPreference = "Stop"
$r = Split-Path -Parent $PSScriptRoot
$libs = Join-Path $r "libs\OpsisNewsCrawler.Plugin.Abstractions.dll"
$want = (Get-Content (Join-Path $r "libs\contract.sha256") -Raw).Trim().Split(' ')[0].ToLower()
$list = @($libs)
$parent = Split-Path -Parent $r
$cands = @($env:OPSIS_CRAWLER_REPO, (Join-Path $parent "crawler-p"), (Join-Path $parent "OpsisNewsCrawler")) | Where-Object { $_ }
foreach ($c in $cands) {
  $p = Join-Path $c "tests\OpsisNewsCrawler.Plugin.Tests\reference\OpsisNewsCrawler.Plugin.Abstractions.dll"
  if (Test-Path $p) { $list += $p; break }
}
$list += @(Get-ChildItem (Join-Path $r "bin") -Recurse -Filter OpsisNewsCrawler.Plugin.Abstractions.dll -ErrorAction SilentlyContinue | ForEach-Object FullName)
foreach ($e in $Extra) { if (Test-Path $e) { $list += (Resolve-Path $e).Path } else { throw "extra copy not found: $e" } }
$bad = 0
foreach ($p in $list) {
  $h = (Get-FileHash $p -Algorithm SHA256).Hash.ToLower()
  $ok = $h -eq $want
  "{0}  {1}  {2}" -f $(if ($ok) { "OK  " } else { "DIFF" }), $h.Substring(0, 16), $p
  if (-not $ok) { $bad++ }
}
$v = [Reflection.AssemblyName]::GetAssemblyName($libs).Version
if ($v -ne [version]"1.0.0.0") { throw "contract in libs is $v, expected 1.0.0.0" }
if ($bad -gt 0) { throw "$bad copy/copies of the contract DLL differ from the recorded SHA-256 $want" }
"all $($list.Count) copies have the SHA-256 $want (contract $v)"
