[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$SourcePackageDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\StremioXboxPrototype'
$source = Get-Item -LiteralPath $SourcePackageDirectory
$packages = @(Get-ChildItem -LiteralPath $source.FullName -Filter '*.msix' -File)
if ($packages.Count -ne 1) { throw 'Specify a test package directory containing exactly one validated Release MSIX.' }
[xml]$settings = Get-Content -LiteralPath (Join-Path $project 'StremioXboxPrototype.csproj.user')
$thumbprint = [string](($settings.Project.PropertyGroup | Where-Object PackageCertificateThumbprint | Select-Object -First 1).PackageCertificateThumbprint)
if (!$thumbprint) { throw 'No development certificate thumbprint is configured.' }
$cert = Get-Item -LiteralPath "Cert:\CurrentUser\My\$thumbprint"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
try {
    $reader = [IO.StreamReader]::new($archive.GetEntry('AppxManifest.xml').Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($manifest.Package.Identity.Publisher -ne $cert.Subject) { throw 'Certificate publisher mismatch.' }
    if ($manifest.Package.Identity.ProcessorArchitecture -ne 'x64') { throw 'Xbox requires the x64 package.' }
    if ($archive.Entries.FullName -match '(^|/)(coreclr|hostfxr)\.dll$') { throw 'Expected a Native AOT Release package.' }
} finally { $archive.Dispose() }
if (!$cert.HasPrivateKey -or $cert.NotAfter -le (Get-Date) -or $cert.NotBefore -gt (Get-Date)) { throw 'Development certificate is not valid for signing.' }
$signTool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter signtool.exe -Recurse |
    Where-Object FullName -match '\\x64\\' | Sort-Object FullName | Select-Object -Last 1
if (!$signTool) { throw 'Windows SDK SignTool not found.' }
$output = Join-Path $project ('AppPackages\XboxSideload_{0}_{1}' -f $manifest.Package.Identity.Version, (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output | Out-Null
$target = Join-Path $output ('watchstream_{0}_x64.msix' -f $manifest.Package.Identity.Version)
Copy-Item -LiteralPath $packages[0].FullName -Destination $target
& $signTool.FullName sign /fd SHA256 /s My /sha1 $thumbprint $target
if ($LASTEXITCODE -ne 0) { throw 'Package signing failed.' }
[IO.File]::WriteAllBytes((Join-Path $output 'watchstream-development.cer'), $cert.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
$dependencies = Join-Path $source.FullName 'Dependencies\x64'
if (Test-Path -LiteralPath $dependencies) { Copy-Item -LiteralPath $dependencies -Destination (Join-Path $output 'Dependencies-x64') -Recurse }
Add-Type -AssemblyName System.Security
$signedArchive = [IO.Compression.ZipFile]::OpenRead($target)
try {
    $signatureStream = $signedArchive.GetEntry('AppxSignature.p7x').Open()
    $buffer = [IO.MemoryStream]::new()
    try { $signatureStream.CopyTo($buffer); $signatureBytes = $buffer.ToArray() }
    finally { $signatureStream.Dispose(); $buffer.Dispose() }
    $cms = [Security.Cryptography.Pkcs.SignedCms]::new()
    $cms.Decode([byte[]]$signatureBytes[4..($signatureBytes.Length - 1)])
    $cms.CheckSignature($true)
    if ($cms.SignerInfos[0].Certificate.Thumbprint -ne $cert.Thumbprint) { throw 'Unexpected package signer.' }
} finally { $signedArchive.Dispose() }
$cert.Dispose()
Write-Host "Signature verified cryptographically: $target"
Write-Host 'Uses a self-signed development certificate. Device trust and installation must be checked on the Xbox in Dev Mode.'
