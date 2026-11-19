# Usage: Get-Mutagen.ps1 -arch <x64|arm64>
param (
    [ValidateSet("x64", "arm64")]
    [Parameter(Mandatory = $true)]
    [string] $arch
)

$ErrorActionPreference = "Stop"

function Download-File([string] $url, [string] $outputPath, [string] $etagFile, [string] $checksum) {
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
    $actualChecksum = (Get-FileHash -Algorithm SHA256 -Path $outputPath).Hash
    if ($actualChecksum -ne $checksum) {
        if (Test-Path $etagFile) { Remove-Item -Force $etagFile }
        Remove-Item -Force $outputPath
        throw "Checksum mismatch for '$outputPath': expected '$checksum', got '$actualChecksum'"
    }
}

$goArch = switch ($arch) {
    "x64" { "amd64" }
    "arm64" { "arm64" }
    default { throw "Unsupported architecture: $arch" }
}

# Download the mutagen binary from our bucket for this platform if we don't have
# it yet (or it's different).
$mutagenVersion = "v0.18.5"
# SHA-256 checksums of the files for $mutagenVersion.
$mutagenChecksums = @{
    "mutagen-windows-amd64.exe" = "51415c027c3e63010fc1578fefd64998e92abbb8aa2cc40df36081261ef61c1d"
    "mutagen-windows-arm64.exe" = "bea242b472d1700b3d33dc07a187c615cc3cd13465fbddc397060e949d48afb9"
    "mutagen-agents.tar.gz"     = "02e7458b9c6d1436c1fda8151bac86bfb46783b75a10d0fd9202100eb2a16f1d"
}
$mutagenPath = Join-Path $PSScriptRoot "files\mutagen-windows-$($arch).exe"
$mutagenUrl = "https://storage.googleapis.com/coder-desktop/mutagen/$($mutagenVersion)/mutagen-windows-$($goArch).exe"
$mutagenEtagFile = $mutagenPath + ".etag"
$mutagenChecksum = $mutagenChecksums["mutagen-windows-$($goArch).exe"]
Download-File $mutagenUrl $mutagenPath $mutagenEtagFile $mutagenChecksum

# Download mutagen agents tarball.
$mutagenAgentsPath = Join-Path $PSScriptRoot "files\mutagen-agents.tar.gz"
$mutagenAgentsUrl = "https://storage.googleapis.com/coder-desktop/mutagen/$($mutagenVersion)/mutagen-agents.tar.gz"
$mutagenAgentsEtagFile = $mutagenAgentsPath + ".etag"
$mutagenAgentsChecksum = $mutagenChecksums["mutagen-agents.tar.gz"]
Download-File $mutagenAgentsUrl $mutagenAgentsPath $mutagenAgentsEtagFile $mutagenAgentsChecksum
