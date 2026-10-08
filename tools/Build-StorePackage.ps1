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

$msbuildPath = Join-Path $visualStudioPath 'MSBuild\Current\Bin\amd64\MSBuild.exe'
if (-not (Test-Path -LiteralPath $msbuildPath)) {
    $msbuildPath = Join-Path $visualStudioPath 'MSBuild\Current\Bin\MSBuild.exe'
}
if (-not (Test-Path -LiteralPath $msbuildPath)) {
    throw "MSBuild was not found at $msbuildPath."
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outputDirectory = Join-Path $packageRoot "StoreUpload_${packageVersion}_$stamp"
New-Item -ItemType Directory -Path $outputDirectory | Out-Null

Write-Host "Building watchstream $packageVersion for Microsoft Store (Release, x64)..."
# Restore Release-specific Native AOT targets even after a Debug build. Publish
# produces the executable before packaging and supplies the native payload.
& $msbuildPath $solutionPath `
    '/restore' `
    '/t:Publish' `
    '/m' `
    '/p:Configuration=Release' `
    '/p:Platform=x64' `
    '/p:GenerateAppxPackageOnBuild=false' `
    '/p:PublishAppxPackage=true' `
    '/p:IncludePublishItemsOutputGroup=true' `
    '/verbosity:minimal' `
    '/p:UapAppxPackageBuildMode=StoreOnly' `
    '/p:AppxBundle=Never' `
    '/p:AppxPackageSigningEnabled=false' `
    '/nr:false' `
    '/m:1' `
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

if (-not $msix) {
    throw 'No MSIX payload was found for validating the Store upload.'
}

# A successful archive build alone does not prove that Native AOT ran. Reject
# a desktop managed-runtime payload before presenting it as an Xbox release.
$packageArchive = [System.IO.Compression.ZipFile]::OpenRead($msix.FullName)
try {
    $manifestEntry = $packageArchive.GetEntry('AppxManifest.xml')
    if (-not $manifestEntry) { throw 'The MSIX payload has no app manifest.' }
    $manifestReader = [System.IO.StreamReader]::new($manifestEntry.Open())
    try { [xml]$builtManifest = $manifestReader.ReadToEnd() }
    finally { $manifestReader.Dispose() }

    if ($builtManifest.Package.Identity.Version -ne $packageVersion.ToString()) {
        throw 'The MSIX payload version does not match the source manifest.'
    }
    if ($builtManifest.Package.Identity.Name -ne $manifest.Package.Identity.Name -or
        $builtManifest.Package.Identity.Publisher -ne $manifest.Package.Identity.Publisher -or
        $builtManifest.Package.Identity.ProcessorArchitecture -ne 'x64') {
        throw 'The MSIX payload identity or architecture does not match this release.'
    }
    $executable = $builtManifest.Package.Applications.Application.Executable
    if (-not $packageArchive.GetEntry($executable)) {
        throw "The MSIX payload is missing its executable: $executable."
    }
    if ($packageArchive.Entries.FullName -match '(^|/)(coreclr|hostfxr)\.dll$') {
        throw 'The package contains a managed runtime instead of the expected Native AOT release.'
    }
}
finally { $packageArchive.Dispose() }

Write-Host ''
Write-Host 'Store package created successfully:' -ForegroundColor Green
Write-Host $result.FullName
Write-Host ''
Write-Host 'Upload this file in Partner Center under Packages.'
