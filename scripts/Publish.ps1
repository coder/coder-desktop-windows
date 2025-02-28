# Usage: Publish.ps1 -arch <x64|arm64> -version <version> [-buildPath <path>] [-outputPath <path>]
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
    [switch] $keepBuildTemp = $false
)

# CD to the root of the repo
$repoRoot = Join-Path $PSScriptRoot ".."
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
$buildPath = Join-Path $repoRoot "publish\buildtemp-$($version)-$($arch)"
if (Test-Path $buildPath) {
    Remove-Item -Recurse -Force $buildPath
}
New-Item -ItemType Directory -Path $buildPath -Force

# Build in release mode
$servicePublishDir = Join-Path $buildPath "service"
dotnet.exe publish .\Vpn.Service\Vpn.Service.csproj -c Release -a $arch -o $servicePublishDir
# App needs to be built with msbuild
$appPublishDir = Join-Path $buildPath "app"
$msbuildBinary = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe
& $msbuildBinary .\App\App.csproj /p:Configuration=Release /p:Platform=$arch /p:OutputPath=$appPublishDir

# Copy any additional files into the install directory
Copy-Item "scripts\files\License.txt" $buildPath
$vpnFilesPath = Join-Path $buildPath "vpn"
New-Item -ItemType Directory -Path $vpnFilesPath -Force
Copy-Item "scripts\files\LICENSE.WINTUN.txt" $vpnFilesPath
$wintunDllPath = Join-Path $vpnFilesPath "wintun.dll"
Copy-Item "scripts\files\wintun-*-$($arch).dll" $wintunDllPath

# Build the MSI installer
dotnet.exe run --project .\Installer\Installer.csproj -c Release -- `
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

# TODO: sign the installer

# Build the bootstrapper
dotnet.exe run --project .\Installer\Installer.csproj -c Release -- `
    build-bootstrapper `
    --arch $arch `
    --version $version `
    --license-file "scripts\files\License.rtf" `
    --output-path $outputPath `
    --icon-file "App\coder.ico" `
    --msi-path $msiOutputPath `
    --logo-png "scripts\files\logo.png"

# TODO: sign the bootstrapper

if (!$keepBuildTemp) {
    Remove-Item -Recurse -Force $buildPath
}
