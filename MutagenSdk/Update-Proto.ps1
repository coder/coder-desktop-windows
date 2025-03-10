# Usage: Update-Proto.ps1 -mutagenTag <version>
param (
    [Parameter(Mandatory = $true)]
    [string] $mutagenTag
)

$ErrorActionPreference = "Stop"

$protoPrefix = "pkg"
$entryFile = "service\synchronization\synchronization.proto"

$outputNamespace = "Coder.Desktop.MutagenSdk.Proto"
$outputDir = "MutagenSdk\Proto"

$cloneDir = Join-Path $env:TEMP "coder-desktop-mutagen-proto"
if (Test-Path $cloneDir) {
    Write-Host "Found existing mutagen repo at $cloneDir, checking out $mutagenTag..."
    # Checkout tag and clean
    Push-Location $cloneDir
    try {
        & git.exe clean -fdx
        if ($LASTEXITCODE -ne 0) { throw "Failed to clean $mutagenTag" }
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
        "https://github.com/mutagen-io/mutagen.git" `
        $cloneDir
}

# Read and format the license header for the copied files.
$licenseContent = Get-Content (Join-Path $cloneDir "LICENSE")
# Find the index where MIT License starts so we don't include the preamble.
$mitStartIndex = $licenseContent.IndexOf("MIT License")
$licenseHeader = ($licenseContent[$mitStartIndex..($licenseContent.Length - 1)] | ForEach-Object { (" * " + $_).TrimEnd() }) -join "`n"

$entryFilePath = Join-Path $cloneDir (Join-Path $protoPrefix $entryFile)
if (-not (Test-Path $entryFilePath)) {
    throw "Failed to find $entryFilePath in mutagen repo"
}

# Map of src (in the mutagen repo) to dst (within the $outputDir).
$filesToCopy = @{
    $entryFilePath = $entryFile
}

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

Add-ImportedFiles $entryFilePath

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot
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
        Copy-Item -Force $filePath $dstPath

        # Determine the license header.
        $fileHeader = "/**`n" +
        " * This file was taken from `n" +
        " * https://github.com/mutagen-io/mutagen/tree/$mutagenTag/$protoPrefix/$protoPath`n" +
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

        # Add the csharp_namespace declaration.
        $content = Get-Content $dstPath -Raw
        $content = $fileHeader + $content
        $content = $content -replace '(?m)^(package .*?;)', "`$1`noption csharp_namespace = `"$csharpNamespace`";"
        Set-Content -Path $dstPath -Value $content
    }
}
finally {
    Pop-Location
}
