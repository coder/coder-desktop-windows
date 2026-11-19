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
$mutagen = Get-Content -Raw (Join-Path $PSScriptRoot "mutagen.json") | ConvertFrom-Json
$mutagenVersion = $mutagen.version
$mutagenPath = Join-Path $PSScriptRoot "files\mutagen-windows-$($arch).exe"
$mutagenUrl = "https://storage.googleapis.com/coder-desktop/mutagen/$($mutagenVersion)/mutagen-windows-$($goArch).exe"
$mutagenEtagFile = $mutagenPath + ".etag"
$mutagenChecksum = $mutagen.sha256."mutagen-windows-$($goArch).exe"
Download-File $mutagenUrl $mutagenPath $mutagenEtagFile $mutagenChecksum

# Download mutagen agents tarball.
$mutagenAgentsPath = Join-Path $PSScriptRoot "files\mutagen-agents.tar.gz"
$mutagenAgentsUrl = "https://storage.googleapis.com/coder-desktop/mutagen/$($mutagenVersion)/mutagen-agents.tar.gz"
$mutagenAgentsEtagFile = $mutagenAgentsPath + ".etag"
$mutagenAgentsChecksum = $mutagen.sha256."mutagen-agents.tar.gz"
Download-File $mutagenAgentsUrl $mutagenAgentsPath $mutagenAgentsEtagFile $mutagenAgentsChecksum
