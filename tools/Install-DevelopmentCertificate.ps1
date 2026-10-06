[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectDirectory = Join-Path $repositoryRoot 'src\StremioXboxPrototype'
$projectFile = Join-Path $projectDirectory 'StremioXboxPrototype.csproj'
$userFile = "$projectFile.user"
$pfxPath = Join-Path $projectDirectory 'StremioXboxPrototype_TemporaryKey.pfx'
$signingDirectory = Join-Path $projectDirectory 'SigningTemp'
$cerPath = Join-Path $signingDirectory 'watchstream-development.cer'
$publisher = 'CN=72F532F7-EE7F-4B17-8DC0-38902DFF476C'
$friendlyName = 'watchstream local development'

New-Item -ItemType Directory -Path $signingDirectory -Force | Out-Null

# A fresh Windows profile may not have created the per-user RSA key container
# yet. CertEnroll reports ERROR_FILE_NOT_FOUND instead of creating it itself.
$currentUserSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$rsaKeyDirectory = Join-Path $env:APPDATA "Microsoft\Crypto\RSA\$currentUserSid"
New-Item -ItemType Directory -Path $rsaKeyDirectory -Force | Out-Null
$cngKeyDirectory = Join-Path $env:APPDATA 'Microsoft\Crypto\Keys'
New-Item -ItemType Directory -Path $cngKeyDirectory -Force | Out-Null

if (Test-Path -LiteralPath $pfxPath) {
    $backupName = 'StremioXboxPrototype_TemporaryKey-{0}.pfx.bak' -f (Get-Date -Format 'yyyyMMdd-HHmmss')
    Move-Item -LiteralPath $pfxPath -Destination (Join-Path $signingDirectory $backupName)
}

# Remove certificates left by an interrupted earlier run of this script.
foreach ($storePath in @('Cert:\CurrentUser\My', 'Cert:\CurrentUser\TrustedPeople')) {
    Get-ChildItem -Path $storePath -ErrorAction SilentlyContinue |
        Where-Object { $_.FriendlyName -eq $friendlyName -and $_.Subject -eq $publisher } |
        Remove-Item -Force
}

$certificate = New-SelfSignedCertificate `
    -Type Custom `
    -Subject $publisher `
    -FriendlyName $friendlyName `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -Provider 'Microsoft Software Key Storage Provider' `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -HashAlgorithm SHA256 `
    -KeyUsage DigitalSignature `
    -KeyExportPolicy Exportable `
    -NotAfter (Get-Date).AddYears(3) `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3')

$passwordBytes = New-Object byte[] 32
$random = [Security.Cryptography.RandomNumberGenerator]::Create()
$random.GetBytes($passwordBytes)
$random.Dispose()
$passwordText = [Convert]::ToBase64String($passwordBytes)
$password = ConvertTo-SecureString -String $passwordText -AsPlainText -Force
Export-PfxCertificate -Cert $certificate -FilePath $pfxPath -Password $password | Out-Null
Export-Certificate -Cert $certificate -FilePath $cerPath -Force | Out-Null
Import-Certificate -FilePath $cerPath -CertStoreLocation 'Cert:\CurrentUser\TrustedPeople' | Out-Null

if (Test-Path -LiteralPath $userFile) {
    [xml]$userProject = Get-Content -LiteralPath $userFile
} else {
    [xml]$userProject = '<Project><PropertyGroup /></Project>'
}

$propertyGroup = $userProject.Project.PropertyGroup | Select-Object -First 1
if (-not $propertyGroup) {
    $propertyGroup = $userProject.CreateElement('PropertyGroup')
    $userProject.Project.AppendChild($propertyGroup) | Out-Null
}

foreach ($setting in @{
    PackageCertificateKeyFile = 'StremioXboxPrototype_TemporaryKey.pfx'
    PackageCertificatePassword = $passwordText
    PackageCertificateThumbprint = $certificate.Thumbprint
}.GetEnumerator()) {
    $node = $propertyGroup.ChildNodes | Where-Object { $_.LocalName -eq $setting.Key } | Select-Object -First 1
    if (-not $node) {
        $node = $userProject.CreateElement($setting.Key, $userProject.Project.NamespaceURI)
        $propertyGroup.AppendChild($node) | Out-Null
    }
    $node.InnerText = $setting.Value
}

$userProject.Save($userFile)

Write-Host 'Development certificate installed for the current Windows user.' -ForegroundColor Green
Write-Host "Publisher: $publisher"
Write-Host "Expires: $($certificate.NotAfter.ToString('yyyy-MM-dd'))"
Write-Host 'The password is stored only in the ignored .csproj.user file.'
