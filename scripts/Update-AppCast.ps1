# Generates appcast.xml and appcast.xml.signature for a given release.
#
# Requires openssl.exe. You can install it via winget:
#   winget install ShiningLight.OpenSSL.Light
#
# Usage: Update-AppCast.ps1
#          -tag <tag>
#          -channel <stable|preview>
#          -x64Path <path>
#          -arm64Path <path>
#          -keyPath <path>
#          -outputAppCastPath <path>
#          -outputAppCastSignaturePath <path>
param (
    [Parameter(Mandatory = $true)]
    [ValidatePattern("^v\d+\.\d+\.\d+$")]
    [string] $tag,

    [Parameter(Mandatory = $true)]
    [ValidateSet('stable', 'preview')]
    [string] $channel,

    [Parameter(Mandatory = $false)]
    [ValidatePattern("^\w{3}, \d{2} \w{3} \d{4} \d{2}:\d{2}:\d{2} \+00:00$")]
    [string] $pubDate = (Get-Date).ToUniversalTime().ToString("ddd, dd MMM yyyy HH:mm:ss +00:00"),

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
    [string] $outputAppCastPath = "appcast.xml",

    [Parameter(Mandatory = $false)]
    [string] $outputAppCastSignaturePath = "appcast.xml.signature"
)

$ErrorActionPreference = "Stop"

$repo = "coder/coder-desktop-windows"

$version = $tag.Substring(1) # remove the v prefix

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

$sparkleNs = "http://www.andymatuschak.org/xml-namespaces/sparkle"
$releaseNotesHtml = [System.IO.File]::ReadAllText($releaseNotesHtmlPath)

# NetSparkle only reads the first enclosure of each item, so every
# architecture gets its own item with a single enclosure.
$installers = @(
    @{ path = $x64Path; os = "win-x64" },
    @{ path = $arm64Path; os = "win-arm64" }
)

# The appcast only lists the latest release, so it is generated from scratch.
# NetSparkle fails to parse CRLF line endings, so use LF.
$settings = New-Object System.Xml.XmlWriterSettings
$settings.Indent = $true
$settings.IndentChars = "  "
$settings.NewLineChars = "`n"
$settings.Encoding = New-Object System.Text.UTF8Encoding($false)
$writer = [System.Xml.XmlWriter]::Create($outputAppCastPath, $settings)
try {
    $writer.WriteStartDocument($true)
    $writer.WriteStartElement("rss")
    $writer.WriteAttributeString("xmlns", "sparkle", $null, $sparkleNs)
    $writer.WriteAttributeString("version", "2.0")
    $writer.WriteStartElement("channel")
    $writer.WriteElementString("title", "Coder Desktop")
    $writer.WriteElementString("language", "en")

    foreach ($installer in $installers) {
        $fileName = Split-Path $installer.path -Leaf

        $writer.WriteStartElement("item")
        $writer.WriteElementString("title", $tag)
        $writer.WriteStartElement("description")
        $writer.WriteCData($releaseNotesHtml)
        $writer.WriteEndElement()
        $writer.WriteElementString("pubDate", $pubDate)
        $writer.WriteElementString("channel", $sparkleNs, $channel)
        $writer.WriteElementString("version", $sparkleNs, $version)
        $writer.WriteElementString("shortVersionString", $sparkleNs, $version)
        $writer.WriteElementString("fullReleaseNotesLink", $sparkleNs, "https://github.com/$repo/releases")

        $writer.WriteStartElement("enclosure")
        $writer.WriteAttributeString("url", "https://github.com/$repo/releases/download/$tag/$fileName")
        $writer.WriteAttributeString("type", "application/x-msdos-program")
        $writer.WriteAttributeString("length", (Get-Item $installer.path).Length)
        $writer.WriteAttributeString("os", $sparkleNs, $installer.os)
        $writer.WriteAttributeString("version", $sparkleNs, $version)
        $writer.WriteAttributeString("shortVersionString", $sparkleNs, $version)
        $writer.WriteAttributeString("criticalUpdate", $sparkleNs, "false")
        # NetSparkle prefers edSignature over signature
        $writer.WriteAttributeString("edSignature", $sparkleNs, (Get-Ed25519Signature $installer.path))
        $writer.WriteEndElement()

        $writer.WriteEndElement()
    }

    $writer.WriteEndElement()
    $writer.WriteEndElement()
    $writer.WriteEndDocument()
}
finally {
    $writer.Close()
}

Write-Output "---- Generated appcast -----"
Get-Content $outputAppCastPath
Write-Output "---- End of generated appcast ----"
Write-Output ""

# Generate the signature for the appcast itself
$appCastSignature = Get-Ed25519Signature $outputAppCastPath
[System.IO.File]::WriteAllText($outputAppCastSignaturePath, $appCastSignature)
Write-Output "---- Updated appcast signature -----"
Get-Content $outputAppCastSignaturePath
Write-Output "---- End of updated appcast signature ----"
