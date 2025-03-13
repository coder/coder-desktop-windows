# Usage: Publish.ps1 -arch <x64|arm64> -version <version> [-msiOutputPath <path>] [-outputPath <path>] [-sign]
param (
    [ValidateSet("x64", "arm64")]
    [Parameter(Mandatory = $true)]
    [string] $arch,

    [Parameter(Mandatory = $true)]
    [ValidatePattern("^\d+\.\d+\.\d+\.\d+$")]
    [string] $version,

    [Parameter(Mandatory = $false)]
    [string] $msiOutputPath = "", # defaults to "publish\CoderDesktopCore-$version-$arch.msi"

    [Parameter(Mandatory = $false)]
    [string] $outputPath = "", # defaults to "publish\CoderDesktop-$version-$arch.exe"

    [Parameter(Mandatory = $false)]
    [switch] $keepBuildTemp = $false,

    [Parameter(Mandatory = $false)]
    [switch] $sign = $false
)

$ErrorActionPreference = "Stop"

$ourAssemblies = @(
    "Coder Desktop.exe",
    "Coder Desktop.dll",
    "CoderVpnService.exe",
    "CoderVpnService.dll",

    "Coder.Desktop.CoderSdk.dll",
    "Coder.Desktop.Vpn.dll",
    "Coder.Desktop.Vpn.Proto.dll"
)

function Find-Dependencies([string[]] $dependencies) {
    foreach ($dependency in $dependencies) {
        if (!(Get-Command $dependency -ErrorAction SilentlyContinue)) {
            throw "Missing dependency: $dependency"
        }
    }
}

function Find-EnvironmentVariables([string[]] $variables) {
    foreach ($variable in $variables) {
        if (!(Get-Item env:$variable -ErrorAction SilentlyContinue)) {
            throw "Missing environment variable: $variable"
        }
    }
}

Find-Dependencies @("dotnet.exe", "wix.exe")

if ($sign) {
    Write-Host "Signing is enabled"
    Find-Dependencies java
    Find-EnvironmentVariables @("JSIGN_PATH", "EV_KEYSTORE", "EV_KEY", "EV_CERTIFICATE_PATH", "EV_TSA_URL", "GCLOUD_ACCESS_TOKEN")
}

function Add-CoderSignature([string] $path) {
    if (!$sign) {
        Write-Host "Skipping signing $path"
        return
    }

    Write-Host "Signing $path"
    & java.exe -jar $env:JSIGN_PATH `
        --storetype GOOGLECLOUD `
        --storepass $env:GCLOUD_ACCESS_TOKEN `
        --keystore $env:EV_KEYSTORE `
        --alias $env:EV_KEY `
        --certfile $env:EV_CERTIFICATE_PATH `
        --tsmode RFC3161 `
        --tsaurl $env:EV_TSA_URL `
        $path
    if ($LASTEXITCODE -ne 0) { throw "Failed to sign $path" }

    # Verify that the output exe is authenticode signed
    $sig = Get-AuthenticodeSignature $path
    if ($sig.Status -ne "Valid") {
        throw "File $path is not authenticode signed"
    }
}

# CD to the root of the repo
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot

if ($msiOutputPath -eq "") {
    $msiOutputPath = Join-Path $repoRoot "publish\CoderDesktopCore-$($version)-$($arch).msi"
}
if (Test-Path $msiOutputPath) {
    Remove-Item -Recurse -Force $msiOutputPath
}

if ($outputPath -eq "") {
    $outputPath = Join-Path $repoRoot "publish\CoderDesktop-$($version)-$($arch).exe"
}
if (Test-Path $outputPath) {
    Remove-Item -Recurse -Force $outputPath
}
if (Test-Path $outputPath.Replace(".exe", ".wixpdb")) {
    Remove-Item -Recurse -Force $outputPath.Replace(".exe", ".wixpdb")
}

# Create a publish directory
$publishDir = Join-Path $repoRoot "publish"
$buildPath = Join-Path $publishDir "buildtemp-$($version)-$($arch)"
if (Test-Path $buildPath) {
    Remove-Item -Recurse -Force $buildPath
}
New-Item -ItemType Directory -Path $buildPath -Force

# Build in release mode
& dotnet.exe restore
if ($LASTEXITCODE -ne 0) { throw "Failed to dotnet restore" }
$servicePublishDir = Join-Path $buildPath "service"
& dotnet.exe publish .\Vpn.Service\Vpn.Service.csproj -c Release -a $arch -o $servicePublishDir
if ($LASTEXITCODE -ne 0) { throw "Failed to build Vpn.Service" }
# App needs to be built with msbuild
$appPublishDir = Join-Path $buildPath "app"
$msbuildBinary = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe
if ($LASTEXITCODE -ne 0) { throw "Failed to find MSBuild" }
if (-not (Test-Path $msbuildBinary)) { throw "Failed to find MSBuild at $msbuildBinary" }
& $msbuildBinary .\App\App.csproj /p:Configuration=Release /p:Platform=$arch /p:OutputPath=$appPublishDir
if ($LASTEXITCODE -ne 0) { throw "Failed to build App" }

# Find any files in the publish directory recursively that match any of our
# assemblies and sign them.
$toSign = Get-ChildItem -Path $buildPath -Recurse | Where-Object { $ourAssemblies -contains $_.Name }
foreach ($file in $toSign) {
    Add-CoderSignature $file.FullName
}

# Copy any additional files into the install directory
Copy-Item "scripts\files\License.txt" $buildPath
$vpnFilesPath = Join-Path $buildPath "vpn"
New-Item -ItemType Directory -Path $vpnFilesPath -Force
Copy-Item "scripts\files\LICENSE.WINTUN.txt" $vpnFilesPath
$wintunDllSrc = Get-Item "scripts\files\wintun-*-$($arch).dll"
if ($null -eq $wintunDllSrc) {
    throw "Failed to find wintun DLL"
}
$wintunDllDest = Join-Path $vpnFilesPath "wintun.dll"
Copy-Item $wintunDllSrc $wintunDllDest

$scriptRoot = Join-Path $repoRoot "scripts"
$getMutagen = Join-Path $scriptRoot "Get-Mutagen.ps1"
& $getMutagen -arch $arch

$mutagenSrcPath = Join-Path $scriptRoot "files\mutagen-windows-$($arch).exe"
$mutagenDestPath = Join-Path $vpnFilesPath "mutagen.exe"
Copy-Item $mutagenSrcPath $mutagenDestPath

$mutagenAgentsSrcPath = Join-Path $scriptRoot "files\mutagen-agents.tar.gz"
$mutagenAgentsDestPath = Join-Path $vpnFilesPath "mutagen-agents.tar.gz"
Copy-Item $mutagenAgentsSrcPath $mutagenAgentsDestPath

# Build the MSI installer
& dotnet.exe run --project .\Installer\Installer.csproj -c Release -- `
    build-msi `
    --arch $arch `
    --version $version `
    --license-file "scripts\files\License.rtf" `
    --output-path $msiOutputPath `
    --icon-file "App\coder.ico" `
    --input-dir $buildPath `
    --service-exe "service\CoderVpnService.exe" `
    --service-name "Coder Desktop" `
    --app-exe "app\Coder Desktop.exe" `
    --vpn-dir "vpn" `
    --banner-bmp "scripts\files\WixUIBannerBmp.bmp" `
    --dialog-bmp "scripts\files\WixUIDialogBmp.bmp"
if ($LASTEXITCODE -ne 0) { throw "Failed to build MSI" }
Add-CoderSignature $msiOutputPath

# Build the bootstrapper
& dotnet.exe run --project .\Installer\Installer.csproj -c Release -- `
    build-bootstrapper `
    --arch $arch `
    --version $version `
    --license-file "scripts\files\License.rtf" `
    --output-path $outputPath `
    --icon-file "App\coder.ico" `
    --msi-path $msiOutputPath `
    --logo-png "scripts\files\logo.png"
if ($LASTEXITCODE -ne 0) { throw "Failed to build bootstrapper" }

# Sign the bootstrapper, which is not as simple as just signing the exe.
if ($sign) {
    $burnIntermediate = Join-Path $publishDir "burn-intermediate-$($version)-$($arch)"
    New-Item -ItemType Directory -Path $burnIntermediate -Force
    $burnEngine = Join-Path $publishDir "burn-engine-$($version)-$($arch).exe"

    # Move the current output path
    $unsignedOutputPath = Join-Path (Split-Path $outputPath -Parent) ("UNSIGNED-" + (Split-Path $outputPath -Leaf))
    Move-Item $outputPath $unsignedOutputPath

    # Extract the engine from the bootstrapper
    & wix.exe burn detach $unsignedOutputPath -intermediateFolder $burnIntermediate -engine $burnEngine
    if ($LASTEXITCODE -ne 0) { throw "Failed to extract engine from bootstrapper" }

    # Sign the engine
    Add-CoderSignature $burnEngine

    # Re-attach the signed engine to the bootstrapper
    & wix.exe burn reattach $unsignedOutputPath -intermediateFolder $burnIntermediate -engine $burnEngine -out $outputPath
    if ($LASTEXITCODE -ne 0) { throw "Failed to re-attach signed engine to bootstrapper" }
    if (!(Test-Path $outputPath)) { throw "Failed to create reattached bootstrapper at $outputPath" }

    # Now sign the output path
    Add-CoderSignature $outputPath

    # Clean up the intermediate files
    if (!$keepBuildTemp) {
        Remove-Item -Force $unsignedOutputPath
        Remove-Item -Recurse -Force $burnIntermediate
        Remove-Item -Force $burnEngine
    }
}

if (!$keepBuildTemp) {
    Remove-Item -Recurse -Force $buildPath
}
