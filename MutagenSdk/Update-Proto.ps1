# Usage: Update-Proto.ps1 -mutagenTag <version>
param (
    [Parameter(Mandatory = $true)]
    [string] $mutagenTag
)

$ErrorActionPreference = "Stop"

$repo = "coder/mutagen"
$protoPrefix = "pkg"
$entryFiles = @(
    "service/daemon/daemon.proto",
    "service/prompting/prompting.proto",
    "service/synchronization/synchronization.proto"
)

$outputNamespace = "Coder.Desktop.MutagenSdk.Proto"
$outputDir = "MutagenSdk\Proto"

$cloneDir = Join-Path $env:TEMP "coder-desktop-mutagen-proto"
if (Test-Path $cloneDir) {
    Write-Host "Found existing mutagen repo at $cloneDir, checking out $mutagenTag..."
    # Checkout tag and clean
    Push-Location $cloneDir
    try {
        & git.exe clean -fdx
        if ($LASTEXITCODE -ne 0) { throw "Failed to clean $cloneDir" }
        # If we're already on the tag, we don't need to fetch or checkout.
        if ((& git.exe name-rev --name-only HEAD) -eq "tags/$mutagenTag") {
            Write-Host "Already on $mutagenTag"
        }
        else {
            & git.exe fetch --all
            if ($LASTEXITCODE -ne 0) { throw "Failed to fetch all tags" }
            & git.exe checkout $mutagenTag
            if ($LASTEXITCODE -ne 0) { throw "Failed to checkout $mutagenTag" }
        }
    }
    finally {
        Pop-Location
    }
}
else {
    New-Item -ItemType Directory -Path $cloneDir -Force

    Write-Host "Cloning mutagen repo to $cloneDir..."
    & git.exe clone `
        --depth 1 `
        --branch $mutagenTag `
        "https://github.com/$repo.git" `
        $cloneDir
}

# Read and format the license header for the copied files.
$licenseContent = Get-Content (Join-Path $cloneDir "LICENSE")
# Find the index where MIT License starts so we don't include the preamble.
$mitStartIndex = $licenseContent.IndexOf("MIT License")
$licenseHeader = ($licenseContent[$mitStartIndex..($licenseContent.Length - 1)] | ForEach-Object { (" * " + $_).TrimEnd() }) -join "`n"

# Map of src (in the mutagen repo) to dst (within the $outputDir).
$filesToCopy = @{}

function Add-ImportedFiles([string] $path) {
    $content = Get-Content $path
    foreach ($line in $content) {
        if ($line -match '^import "(.+)"') {
            $importPath = $matches[1]

            # If the import path starts with google, it doesn't exist in the
            # mutagen repo, so we need to skip it.
            if ($importPath -match '^google/') {
                Write-Host "Skipping $importPath"
                continue
            }

            # Mutagen generates from within the pkg directory, so we need to add
            # the prefix.
            $filePath = Join-Path $cloneDir (Join-Path $protoPrefix $importPath)
            if (-not $filesToCopy.ContainsKey($filePath)) {
                Write-Host "Adding $filePath $importPath"
                $filesToCopy[$filePath] = $importPath
                Add-ImportedFiles $filePath
            }
        }
    }
}

foreach ($entryFile in $entryFiles) {
    $entryFilePath = Join-Path $cloneDir (Join-Path $protoPrefix $entryFile)
    if (-not (Test-Path $entryFilePath)) {
        throw "Failed to find $entryFilePath in mutagen repo"
    }
    $filesToCopy[$entryFilePath] = $entryFile
    Add-ImportedFiles $entryFilePath
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot
$outputDir = Resolve-Path $outputDir
if (Test-Path $outputDir) {
    Remove-Item -Recurse -Force $outputDir
}
New-Item -ItemType Directory -Path $outputDir -Force

try {
    foreach ($filePath in $filesToCopy.Keys) {
        $protoPath = $filesToCopy[$filePath]
        $dstPath = Join-Path $outputDir $protoPath
        $destDir = Split-Path -Path $dstPath -Parent
        if (-not (Test-Path $destDir)) {
            New-Item -ItemType Directory -Path $destDir -Force
        }

        # Determine the license header.
        $fileHeader = "/*`n" +
        " * This file was taken from`n" +
        " * https://github.com/$repo/tree/$mutagenTag/$protoPrefix/$protoPath`n" +
        " *`n" +
        $licenseHeader +
        "`n */`n`n"

        # Determine the csharp_namespace for the file.
        # Remove the filename and capitalize the first letter of each component
        # of the path, then join with dots.
        $protoDir = Split-Path -Path $protoPath -Parent
        $csharpNamespaceSuffix = ($protoDir -split '[/\\]' | ForEach-Object { $_.Substring(0, 1).ToUpper() + $_.Substring(1) }) -join '.'
        $csharpNamespace = "$outputNamespace"
        if ($csharpNamespaceSuffix) {
            $csharpNamespace += ".$csharpNamespaceSuffix"
        }

        # Add the license header and csharp_namespace declaration.
        $content = Get-Content $filePath -Raw
        $content = $fileHeader + $content
        $content = $content -replace '(?m)^(package .*?;)', "`$1`noption csharp_namespace = `"$csharpNamespace`";"

        # Replace all LF with CRLF to avoid spurious diffs in git.
        $content = $content -replace "(?<!`r)`n", "`r`n"

        # Instead of using Set-Content, we use System.IO.File.WriteAllText
        # instead to avoid a byte order mark at the beginning of the file, as
        # well as an extra newline at the end of the file.
        [System.IO.File]::WriteAllText($dstPath, $content, [System.Text.UTF8Encoding]::new($false))
    }
}
finally {
    Pop-Location
}
