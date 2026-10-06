[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot 'StremioXboxPrototype.sln'
$manifestPath = Join-Path $repositoryRoot 'src\StremioXboxPrototype\Package.appxmanifest'
$packageRoot = Join-Path $repositoryRoot 'src\StremioXboxPrototype\AppPackages'
$vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'

if (-not (Test-Path -LiteralPath $vswherePath)) {
    throw 'Visual Studio Installer was not found. Install Visual Studio with UWP and C++ development tools.'
}

[xml]$manifest = Get-Content -LiteralPath $manifestPath
$packageVersion = [version]$manifest.Package.Identity.Version

if ($packageVersion.Major -eq 0) {
    throw "Package version $packageVersion is not Store-ready. Use a version such as 1.0.0.0."
}

if ($packageVersion.Revision -ne 0) {
    throw "Package version $packageVersion is invalid for a Store build. The fourth number must remain 0."
}

$visualStudioPath = & $vswherePath -latest -products * -requires Microsoft.VisualStudio.Workload.Universal -property installationPath
if (-not $visualStudioPath) {
    throw 'Visual Studio with the Universal Windows Platform workload was not found.'
}

$msbuildPath = Join-Path $visualStudioPath 'MSBuild\Current\Bin\MSBuild.exe'
if (-not (Test-Path -LiteralPath $msbuildPath)) {
    throw "MSBuild was not found at $msbuildPath."
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outputDirectory = Join-Path $packageRoot "StoreUpload_${packageVersion}_$stamp"
New-Item -ItemType Directory -Path $outputDirectory | Out-Null

Write-Host "Building watchstream $packageVersion for Microsoft Store (Release, x64)..."
& $msbuildPath $solutionPath `
    '/t:Rebuild' `
    '/m' `
    '/p:Configuration=Release' `
    '/p:Platform=x64' `
    '/p:GenerateAppxPackageOnBuild=true' `
    '/p:UapAppxPackageBuildMode=StoreOnly' `
    '/p:AppxBundle=Never' `
    '/p:AppxPackageSigningEnabled=false' `
    "/p:AppxPackageDir=$outputDirectory\"

if ($LASTEXITCODE -ne 0) {
    throw "Store package build failed with exit code $LASTEXITCODE."
}

$upload = Get-ChildItem -LiteralPath $outputDirectory -Recurse -File -Filter '*.msixupload' |
    Select-Object -First 1
$msix = Get-ChildItem -LiteralPath $outputDirectory -Recurse -File -Filter '*.msix' |
    Select-Object -First 1
$result = if ($upload) { $upload } else { $msix }

if (-not $result) {
    throw "The build completed but no .msixupload or .msix was found in $outputDirectory."
}

Write-Host ''
Write-Host 'Store package created successfully:' -ForegroundColor Green
Write-Host $result.FullName
Write-Host ''
Write-Host 'Upload this file in Partner Center under Packages.'
