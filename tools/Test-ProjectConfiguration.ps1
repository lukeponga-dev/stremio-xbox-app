$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $root 'src/StremioXboxPrototype/StremioXboxPrototype.csproj'
$manifestPath = Join-Path $root 'src/StremioXboxPrototype/Package.appxmanifest'
$storeAssociationPath = Join-Path $root 'src/StremioXboxPrototype/Package.StoreAssociation.xml'
$appXamlPath = Join-Path $root 'src/StremioXboxPrototype/App.xaml'

[xml]$project = Get-Content -LiteralPath $projectPath -Raw
[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
[xml]$storeAssociation = Get-Content -LiteralPath $storeAssociationPath -Raw
[xml]$appXaml = Get-Content -LiteralPath $appXamlPath -Raw

$targetFramework = [string]$project.Project.PropertyGroup.TargetFramework
if ($targetFramework -ne 'net10.0-windows10.0.26100.0') {
    throw "Unexpected TargetFramework '$targetFramework'. Expected net10.0-windows10.0.26100.0."
}

# Validate the package identity against the checked-in Partner Center association.
# The association file is the source of truth; do not duplicate identity strings here.
$identity = $manifest.Package.Identity
$store = $storeAssociation.StoreAssociation
$mainPackageIdentityName = [string]$store.ProductReservedInfo.MainPackageIdentityName
$storePublisher = [string]$store.Publisher
$storePublisherDisplayName = [string]$store.PublisherDisplayName
$reservedNames = @($store.ProductReservedInfo.ReservedNames.ReservedName | ForEach-Object { [string]$_ })

if ([string]::IsNullOrWhiteSpace([string]$identity.Name) -or
    [string]::IsNullOrWhiteSpace([string]$identity.Publisher)) {
    throw 'Package identity must include both Name and Publisher.'
}
if ([string]::IsNullOrWhiteSpace($mainPackageIdentityName) -or
    [string]::IsNullOrWhiteSpace($storePublisher) -or
    [string]::IsNullOrWhiteSpace($storePublisherDisplayName)) {
    throw 'Package.StoreAssociation.xml is missing required Partner Center identity fields.'
}
if ([string]$identity.Name -cne $mainPackageIdentityName) {
    throw "Manifest package identity '$($identity.Name)' does not match Store association identity '$mainPackageIdentityName'."
}
if ([string]$identity.Publisher -cne $storePublisher) {
    throw "Manifest publisher '$($identity.Publisher)' does not match Store association publisher '$storePublisher'."
}
if ([string]$manifest.Package.Properties.PublisherDisplayName -cne $storePublisherDisplayName) {
    throw "Manifest PublisherDisplayName '$($manifest.Package.Properties.PublisherDisplayName)' does not match Store association publisher display name '$storePublisherDisplayName'."
}

$manifestDisplayName = [string]$manifest.Package.Properties.DisplayName
if ([string]::IsNullOrWhiteSpace($manifestDisplayName) -or
    $reservedNames -cnotcontains $manifestDisplayName) {
    throw "Manifest DisplayName '$manifestDisplayName' is not one of the names reserved in Package.StoreAssociation.xml."
}

$releaseVersion = [version]$identity.Version
$highestPublishedVersion = [version]'1.0.19.0'
if ($releaseVersion.Major -lt 1 -or $releaseVersion.Revision -ne 0) {
    throw "Package version '$($identity.Version)' is not Store-ready. Use a non-zero major version and revision 0."
}
if ($releaseVersion -le $highestPublishedVersion) {
    throw "Package version '$releaseVersion' must be higher than the highest published Partner Center version '$highestPublishedVersion'. Update the published-version baseline when a newer package is released."
}

$app = $manifest.Package.Applications.Application
$appClass = [string]$appXaml.Application.GetAttribute('x:Class')
if (-not $appClass) {
    $appClass = [string]$appXaml.Application.GetAttribute('Class', 'http://schemas.microsoft.com/winfx/2006/xaml')
}
if ($app.EntryPoint -ne $appClass) {
    throw "Manifest EntryPoint '$($app.EntryPoint)' does not match App.xaml class '$appClass'."
}

$visualElements = $app.SelectSingleNode("./*[local-name()='VisualElements']")
if (-not $visualElements) { throw 'Manifest is missing uap:VisualElements.' }
$visualDisplayName = [string]$visualElements.GetAttribute('DisplayName')
if ($visualDisplayName -cne $manifestDisplayName -or $reservedNames -cnotcontains $visualDisplayName) {
    throw "VisualElements DisplayName '$visualDisplayName' must match the manifest DisplayName and a name reserved in Package.StoreAssociation.xml."
}

$splash = $visualElements.SelectSingleNode("./*[local-name()='SplashScreen']")
$assetPaths = @(
    [string]$manifest.Package.Properties.Logo
    [string]$visualElements.GetAttribute('Square150x150Logo')
    [string]$visualElements.GetAttribute('Square44x44Logo')
    [string]$splash.GetAttribute('Image')
)
foreach ($asset in $assetPaths) {
    if ([string]::IsNullOrWhiteSpace($asset)) { throw 'A required package asset path is empty.' }
    $diskPath = Join-Path (Split-Path -Parent $manifestPath) $asset.Replace('\', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $diskPath -PathType Leaf)) {
        throw "Manifest references a missing package asset: $asset"
    }
}

Write-Host "Validated target framework: $targetFramework"
Write-Host "Validated Partner Center identity: $($identity.Name)"
Write-Host "Validated publisher: $($identity.Publisher)"
Write-Host "Validated package version: $($identity.Version) (higher than $highestPublishedVersion)"
Write-Host "Validated entry point and $($assetPaths.Count) package asset paths."
