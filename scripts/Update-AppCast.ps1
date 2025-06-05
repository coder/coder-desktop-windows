# Updates appcast.xml and appcast.xml.signature for a given release.
#
# Requires openssl.exe. You can install it via winget:
#   winget install ShiningLight.OpenSSL.Light
#
# Usage: Update-AppCast.ps1
#          -tag <tag>
#          -version <version>
#          -channel <stable|preview>
#          -x64Path <path>
#          -arm64Path <path>
#          -keyPath <path>
#          -inputAppCastPath <path>
#          -outputAppCastPath <path>
#          -outputAppCastSignaturePath <path>
param (
    [Parameter(Mandatory = $true)]
    [ValidatePattern("^v\d+\.\d+\.\d+$")]
    [string] $tag,

    [Parameter(Mandatory = $true)]
    [ValidatePattern("^\d+\.\d+\.\d+$")]
    [string] $version,

    [Parameter(Mandatory = $true)]
    [ValidateSet('stable', 'preview')]
    [string] $channel,

    [Parameter(Mandatory = $false)]
    [string] $pubDate = (Get-Date).ToUniversalTime().ToString("ddd, dd MMM yyyy HH:mm:ss +0000"),

    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path $_ })]
    [string] $x64Path,

    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path $_ })]
    [string] $arm64Path,

    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path $_ })]
    [string] $keyPath,

    [Parameter(Mandatory = $false)]
    [ValidateScript({ Test-Path $_ })]
    [string] $inputAppCastPath = "appcast.xml",

    [Parameter(Mandatory = $false)]
    [string] $outputAppCastPath = "appcast.xml",

    [Parameter(Mandatory = $false)]
    [string] $outputAppCastSignaturePath = "appcast.xml.signature"
)

$ErrorActionPreference = "Stop"

$repo = "coder/coder-desktop-windows"

function Get-Ed25519Signature {
    param (
        [Parameter(Mandatory = $true)]
        [ValidateScript({ Test-Path $_ })]
        [string] $path
    )

    # Use a temporary file. We can't just pipe directly because PowerShell
    # operates with strings for third party commands.
    $tempPath = Join-Path $env:TEMP "coder-desktop-temp.bin"
    & openssl.exe pkeyutl -sign -inkey $keyPath -rawin -in $path -out $tempPath
    if ($LASTEXITCODE -ne 0) { throw "Failed to sign file: $path" }
    $signature = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($tempPath))
    Remove-Item -Force $tempPath
    return $signature
}

# Retrieve the release notes from the GitHub releases API
$releaseNotesMarkdown = & gh.exe release view $tag `
    --json body `
    --jq ".body"
if ($LASTEXITCODE -ne 0) { throw "Failed to retrieve release notes markdown" }
$releaseNotesMarkdown = $releaseNotesMarkdown -replace "`r`n", "`n"
$releaseNotesMarkdownPath = Join-Path $env:TEMP "coder-desktop-release-notes.md"
Set-Content -Path $releaseNotesMarkdownPath -Value $releaseNotesMarkdown -Encoding UTF8

Write-Output "---- Release Notes Markdown -----"
Get-Content $releaseNotesMarkdownPath
Write-Output "---- End of Release Notes Markdown ----"
Write-Output ""

# Convert the release notes markdown to HTML using the GitHub API to match
# GitHub's formatting
$releaseNotesHtmlPath = Join-Path $env:TEMP "coder-desktop-release-notes.html"
& gh.exe api `
    --method POST `
    -H "Accept: application/vnd.github+json" `
    -H "X-GitHub-Api-Version: 2022-11-28" `
    /markdown `
    -F "text=@$releaseNotesMarkdownPath" `
    -F "mode=gfm" `
    -F "context=$repo" `
    > $releaseNotesHtmlPath
if ($LASTEXITCODE -ne 0) { throw "Failed to convert release notes markdown to HTML" }

Write-Output "---- Release Notes HTML -----"
Get-Content $releaseNotesHtmlPath
Write-Output "---- End of Release Notes HTML ----"
Write-Output ""

[xml] $appCast = Get-Content $inputAppCastPath

# Set up namespace manager for sparkle: prefix
$nsManager = New-Object System.Xml.XmlNamespaceManager($appCast.NameTable)
$nsManager.AddNamespace("sparkle", "http://www.andymatuschak.org/xml-namespaces/sparkle")

# Find the matching channel item
$channelItem = $appCast.SelectSingleNode("//item[sparkle:channel='$channel']", $nsManager)
if ($null -eq $channelItem) {
    throw "Could not find channel item for channel: $channel"
}

# Update the item properties
$channelItem.title = $tag
$channelItem.pubDate = $pubDate
$channelItem.SelectSingleNode("sparkle:version", $nsManager).InnerText = $version
$channelItem.SelectSingleNode("sparkle:shortVersionString", $nsManager).InnerText = $version
$channelItem.SelectSingleNode("sparkle:fullReleaseNotesLink", $nsManager).InnerText = "https://github.com/$repo/releases"

# Set description with proper line breaks
$descriptionNode = $channelItem.SelectSingleNode("description")
$descriptionNode.InnerXml = "" # Clear existing content
$cdata = $appCast.CreateCDataSection([System.IO.File]::ReadAllText($releaseNotesHtmlPath))
$descriptionNode.AppendChild($cdata) | Out-Null

# Remove existing enclosures
$existingEnclosures = $channelItem.SelectNodes("enclosure")
foreach ($enclosure in $existingEnclosures) {
    $channelItem.RemoveChild($enclosure) | Out-Null
}

# Add new enclosures
$enclosures = @(
    @{
        path = $x64Path
        os   = "win-x64"
    },
    @{
        path = $arm64Path
        os   = "win-arm64"
    }
)
foreach ($enclosure in $enclosures) {
    $fileName = Split-Path $enclosure.path -Leaf
    $url = "https://github.com/$repo/releases/download/$tag/$fileName"
    $fileSize = (Get-Item $enclosure.path).Length
    $signature = Get-Ed25519Signature $enclosure.path

    $newEnclosure = $appCast.CreateElement("enclosure")
    $newEnclosure.SetAttribute("url", $url)
    $newEnclosure.SetAttribute("type", "application/x-msdos-program")
    $newEnclosure.SetAttribute("length", $fileSize)

    # Set namespaced attributes
    $sparkleNs = $nsManager.LookupNamespace("sparkle")
    $attrs = @{
        "os"                 = $enclosure.os
        "version"            = $version
        "shortVersionString" = $version
        "criticalUpdate"     = "false"
        "edSignature"        = $signature # NetSparkle prefers edSignature over signature
    }
    foreach ($key in $attrs.Keys) {
        $attr = $appCast.CreateAttribute("sparkle", $key, $sparkleNs)
        $attr.Value = $attrs[$key]
        $newEnclosure.Attributes.Append($attr) | Out-Null
    }

    $channelItem.AppendChild($newEnclosure) | Out-Null
}

# Save the updated XML. Convert CRLF to LF since CRLF seems to break NetSparkle
$appCast.Save($outputAppCastPath)
$content = [System.IO.File]::ReadAllText($outputAppCastPath)
$content = $content -replace "`r`n", "`n"
[System.IO.File]::WriteAllText($outputAppCastPath, $content)

Write-Output "---- Updated appcast -----"
Get-Content $outputAppCastPath
Write-Output "---- End of updated appcast ----"
Write-Output ""

# Generate the signature for the appcast itself
$appCastSignature = Get-Ed25519Signature $outputAppCastPath
[System.IO.File]::WriteAllText($outputAppCastSignaturePath, $appCastSignature)
Write-Output "---- Updated appcast signature -----"
Get-Content $outputAppCastSignaturePath
Write-Output "---- End of updated appcast signature ----"
