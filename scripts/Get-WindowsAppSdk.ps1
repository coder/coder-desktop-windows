# Usage: Get-WindowsAppSdk.ps1 -arch <x64|arm64>
param (
    [ValidateSet("x64", "arm64")]
    [Parameter(Mandatory = $true)]
    [string] $arch
)

$ErrorActionPreference = "Stop"

function Download-File([string] $url, [string] $outputPath, [string] $etagFile) {
    Write-Host "Downloading '$url' to '$outputPath'"
    # We use `curl.exe` here because `Invoke-WebRequest` is notoriously slow.
    & curl.exe `
        --progress-bar `
        --show-error `
        --fail `
        --location `
        --etag-compare $etagFile `
        --etag-save $etagFile `
        --output $outputPath `
        $url
    if ($LASTEXITCODE -ne 0) { throw "Failed to download $url" }
    if (!(Test-Path $outputPath) -or (Get-Item $outputPath).Length -eq 0) {
        throw "Failed to download '$url', output file '$outputPath' is missing or empty"
    }
}

# Download the Windows App Sdk binary from Microsoft for this platform if we don't have
# it yet (or it's different).
$windowsAppSdkMajorVersion = "1.8"
$windowsAppSdkFullVersion = "1.8.250907003"
$windowsAppSdkPath = Join-Path $PSScriptRoot "files\windows-app-sdk-$($arch).exe"
$windowsAppSdkUri = "https://aka.ms/windowsappsdk/$($windowsAppSdkMajorVersion)/$($windowsAppSdkFullVersion)/windowsappruntimeinstall-$($arch).exe"
$windowsAppSdkEtagFile = $windowsAppSdkPath + ".etag"
Download-File $windowsAppSdkUri $windowsAppSdkPath $windowsAppSdkEtagFile
