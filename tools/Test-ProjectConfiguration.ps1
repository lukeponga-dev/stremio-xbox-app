$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $root 'src/StremioXboxPrototype/StremioXboxPrototype.csproj'
$manifestPath = Join-Path $root 'src/StremioXboxPrototype/Package.appxmanifest'
$appXamlPath = Join-Path $root 'src/StremioXboxPrototype/App.xaml'

[xml]$project = Get-Content -LiteralPath $projectPath -Raw
[xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
[xml]$appXaml = Get-Content -LiteralPath $appXamlPath -Raw

$targetFramework = [string]$project.Project.PropertyGroup.TargetFramework
if ($targetFramework -ne 'net10.0-windows10.0.26100.0') {
    throw "Unexpected TargetFramework '$targetFramework'. Expected net10.0-windows10.0.26100.0."
}

$identity = $manifest.Package.Identity
if ([string]::IsNullOrWhiteSpace([string]$identity.Name) -or
    [string]::IsNullOrWhiteSpace([string]$identity.Publisher)) {
    throw 'Package identity must include both Name and Publisher.'
}
if ([version]$identity.Version -lt [version]'1.0.0.0') {
    throw "Package version '$($identity.Version)' is not a valid release version."
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
Write-Host "Validated package identity: $($identity.Name) $($identity.Version)"
Write-Host "Validated entry point and $($assetPaths.Count) package asset paths."
