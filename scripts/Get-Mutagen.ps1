# Usage: Get-Mutagen.ps1 -arch <x64|arm64>
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

$goArch = switch ($arch) {
    "x64" { "amd64" }
    "arm64" { "arm64" }
    default { throw "Unsupported architecture: $arch" }
}

# Download the mutagen binary from our bucket for this platform if we don't have
# it yet (or it's different).
$mutagenVersion = "v0.18.3"
$mutagenPath = Join-Path $PSScriptRoot "files\mutagen-windows-$($arch).exe"
$mutagenUrl = "https://storage.googleapis.com/coder-desktop/mutagen/$($mutagenVersion)/mutagen-windows-$($goArch).exe"
$mutagenEtagFile = $mutagenPath + ".etag"
Download-File $mutagenUrl $mutagenPath $mutagenEtagFile

# Download mutagen agents tarball.
$mutagenAgentsPath = Join-Path $PSScriptRoot "files\mutagen-agents.tar.gz"
$mutagenAgentsUrl = "https://storage.googleapis.com/coder-desktop/mutagen/$($mutagenVersion)/mutagen-agents.tar.gz"
$mutagenAgentsEtagFile = $mutagenAgentsPath + ".etag"
Download-File $mutagenAgentsUrl $mutagenAgentsPath $mutagenAgentsEtagFile
