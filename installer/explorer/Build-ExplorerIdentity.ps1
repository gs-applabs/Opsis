[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PublishedDirectory,
    [Parameter(Mandatory)] [string] $OutputPackage,
    [Parameter(Mandatory)] [string] $CertificateThumbprint
)
$ErrorActionPreference = 'Stop'
$published = (Resolve-Path -LiteralPath $PublishedDirectory).Path
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPackage)
$certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint"
if (!$certificate.HasPrivateKey) { throw 'The signing certificate must have a private key.' }

$sdk = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$toolsDirectory = Get-ChildItem -LiteralPath $sdk -Directory |
    Where-Object { $_.Name -match '^\d+(\.\d+){3}$' -and (Test-Path (Join-Path $_.FullName 'x64\makeappx.exe')) } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$toolsDirectory -or [version]$toolsDirectory.Name -lt [version]'10.0.26100.0') {
    throw 'Install Windows SDK 10.0.26100 or later.'
}
$makeAppx = Join-Path $toolsDirectory.FullName 'x64\makeappx.exe'
$signTool = Join-Path $toolsDirectory.FullName 'x64\signtool.exe'
$manifestTool = Join-Path $toolsDirectory.FullName 'x64\mt.exe'
$exe = Join-Path $published 'Opsis.exe'
$projectVersion = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
$extension = Join-Path $published 'OpsisExplorer.dll'
if (!(Test-Path -LiteralPath $extension -PathType Leaf)) { throw 'Build the Explorer DLL first.' }

$staging = Join-Path ([IO.Path]::GetTempPath()) ('OpsisExplorer-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    [xml]$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw
    $manifest.Package.Identity.Publisher = $certificate.Subject
    $manifest.Package.Identity.Version = $projectVersion
    $manifest.Save((Join-Path $staging 'AppxManifest.xml'))
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Assets') -Destination $staging -Recurse
    Copy-Item -LiteralPath $extension -Destination $staging

    $appManifest = Join-Path $staging 'apphost.manifest'
    & $manifestTool -nologo "-inputresource:$exe;#1" "-out:$appManifest"
    if ($LASTEXITCODE -ne 0) { throw 'Could not read the application manifest.' }
    [xml]$application = Get-Content -LiteralPath $appManifest -Raw
    $identity = $application.assembly.ChildNodes | Where-Object { $_.NamespaceURI -eq 'urn:schemas-microsoft-com:msix.v1' -and $_.LocalName -eq 'msix' }
    if (!$identity) {
        $identity = $application.CreateElement('msix', 'urn:schemas-microsoft-com:msix.v1')
        [void]$application.assembly.AppendChild($identity)
    }
    $identity.SetAttribute('publisher', $certificate.Subject)
    $identity.SetAttribute('packageName', $manifest.Package.Identity.Name)
    $identity.SetAttribute('applicationId', $manifest.Package.Applications.Application.Id)
    $application.Save($appManifest)
    & $manifestTool -nologo -manifest $appManifest "-outputresource:$exe;#1"
    if ($LASTEXITCODE -ne 0) { throw 'Could not connect the executable with the identity package.' }
    Remove-Item -LiteralPath $appManifest

    New-Item -ItemType Directory -Path (Split-Path $output) -Force | Out-Null
    # The Win32 executable is installed by Inno at the package's external location.
    & $makeAppx pack /o /nv /d $staging /p $output
    if ($LASTEXITCODE -ne 0) { throw 'Explorer identity packaging failed.' }
    & $signTool sign /fd SHA256 /sha1 $certificate.Thumbprint $output
    if ($LASTEXITCODE -ne 0) { throw 'Explorer identity signing failed.' }
} finally {
    Remove-Item -LiteralPath $staging -Recurse -Force
}
