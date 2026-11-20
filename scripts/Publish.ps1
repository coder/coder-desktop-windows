# Usage: Publish.ps1 -arch <x64|arm64> -version <version> [-msiOutputPath <path>] [-outputPath <path>] [-mutagenAgentsPath <path>] [-sign]
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
    [string] $mutagenAgentsPath = "", # defaults to "scripts\files\mutagen-agents.tar.gz"

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
    if ($mutagenAgentsPath -eq "") {
        throw "Missing parameter: mutagenAgentsPath"
    }
}

function Add-CoderSignature([string] $path, [switch] $replaceExisting) {
    if (!$sign) {
        Write-Host "Skipping signing $path"
        return
    }

    Write-Host "Signing $path"
    $extraArgs = @()
    if ($replaceExisting) { $extraArgs += "--replace" }
    & java.exe -jar $env:JSIGN_PATH `
        --storetype GOOGLECLOUD `
        --storepass $env:GCLOUD_ACCESS_TOKEN `
        --keystore $env:EV_KEYSTORE `
        --alias $env:EV_KEY `
        --certfile $env:EV_CERTIFICATE_PATH `
        --tsmode RFC3161 `
        --tsaurl $env:EV_TSA_URL `
        @extraArgs `
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
& dotnet.exe restore /p:BuildWithNetFrameworkHostedCompiler=true
if ($LASTEXITCODE -ne 0) { throw "Failed to dotnet restore" }
$servicePublishDir = Join-Path $buildPath "service"
& dotnet.exe publish .\Vpn.Service\Vpn.Service.csproj -c Release -a $arch -o $servicePublishDir /p:Version=$version
if ($LASTEXITCODE -ne 0) { throw "Failed to build Vpn.Service" }
$appPublishDir = Join-Path $buildPath "app\"
dotnet build .\App\App.csproj -c Release -o $appPublishDir /p:Platform=$arch /p:OutputPath=$appPublishDir /p:Version=$version
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
Add-CoderSignature $mutagenDestPath -replaceExisting

$mutagenAgentsSrcPath = Join-Path $scriptRoot "files\mutagen-agents.tar.gz"
$tar = Join-Path ([Environment]::SystemDirectory) "tar.exe"
if ($mutagenAgentsPath -ne "") {
    # Compare the supplied mutagen agents with the pinned ones. The macOS
    # agents are not compared.
    $pinnedAgentsDir = Join-Path $publishDir "mutagen-agents-pinned-$($version)-$($arch)"
    $suppliedAgentsDir = Join-Path $publishDir "mutagen-agents-supplied-$($version)-$($arch)"
    foreach ($dir in @($pinnedAgentsDir, $suppliedAgentsDir)) {
        if (Test-Path $dir) {
            Remove-Item -Recurse -Force $dir
        }
        New-Item -ItemType Directory -Path $dir -Force
    }
    $pinnedAgents = @(& $tar -tf $mutagenAgentsSrcPath)
    if ($LASTEXITCODE -ne 0) { throw "Failed to list pinned mutagen agents" }
    $suppliedAgents = @(& $tar -tf $mutagenAgentsPath)
    if ($LASTEXITCODE -ne 0) { throw "Failed to list supplied mutagen agents" }
    if (($suppliedAgents -join "`n") -cne ($pinnedAgents -join "`n")) {
        throw "Supplied mutagen agents '$suppliedAgents' do not match pinned mutagen agents '$pinnedAgents'"
    }
    & $tar -xf $mutagenAgentsSrcPath -C $pinnedAgentsDir
    if ($LASTEXITCODE -ne 0) { throw "Failed to unpack pinned mutagen agents" }
    & $tar -xf $mutagenAgentsPath -C $suppliedAgentsDir
    if ($LASTEXITCODE -ne 0) { throw "Failed to unpack supplied mutagen agents" }
    foreach ($agent in $pinnedAgents) {
        $suppliedAgentPath = Join-Path $suppliedAgentsDir $agent
        if ((Get-Item -LiteralPath $suppliedAgentPath -Force).LinkType) {
            throw "Supplied mutagen agent '$agent' is not a regular file"
        }
        if ($agent -cin @("darwin_amd64", "darwin_arm64")) { continue }
        $pinnedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $pinnedAgentsDir $agent) -ErrorAction Stop).Hash
        $suppliedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $suppliedAgentPath -ErrorAction Stop).Hash
        if ($suppliedHash -ne $pinnedHash) {
            throw "Supplied mutagen agent '$agent' does not match the pinned mutagen agent"
        }
    }
    Remove-Item -Recurse -Force $pinnedAgentsDir
    Remove-Item -Recurse -Force $suppliedAgentsDir
    $mutagenAgentsSrcPath = $mutagenAgentsPath
}
$mutagenAgentsDestPath = Join-Path $vpnFilesPath "mutagen-agents.tar.gz"
if ($sign) {
    # Unpack the mutagen agents, sign the Windows agents and repack them in the
    # same order.
    $mutagenAgentsDir = Join-Path $publishDir "mutagen-agents-$($version)-$($arch)"
    if (Test-Path $mutagenAgentsDir) {
        Remove-Item -Recurse -Force $mutagenAgentsDir
    }
    New-Item -ItemType Directory -Path $mutagenAgentsDir -Force
    $mutagenAgents = @(& $tar -tf $mutagenAgentsSrcPath)
    if ($LASTEXITCODE -ne 0) { throw "Failed to list mutagen agents" }
    & $tar -xf $mutagenAgentsSrcPath -C $mutagenAgentsDir
    if ($LASTEXITCODE -ne 0) { throw "Failed to unpack mutagen agents" }
    foreach ($agent in @("windows_amd64", "windows_arm64")) {
        $agentPath = Join-Path $mutagenAgentsDir $agent
        $agentExePath = $agentPath + ".exe"
        Move-Item $agentPath $agentExePath
        Add-CoderSignature $agentExePath -replaceExisting
        Move-Item $agentExePath $agentPath
    }
    & $tar -czf $mutagenAgentsDestPath -C $mutagenAgentsDir @mutagenAgents
    if ($LASTEXITCODE -ne 0) { throw "Failed to repack mutagen agents" }
    $repackedAgents = @(& $tar -tf $mutagenAgentsDestPath)
    if ($LASTEXITCODE -ne 0) { throw "Failed to list repacked mutagen agents" }
    if (($repackedAgents -join " ") -cne ($mutagenAgents -join " ")) {
        throw "Repacked mutagen agents '$repackedAgents' do not match '$mutagenAgents'"
    }
    Remove-Item -Recurse -Force $mutagenAgentsDir
}
else {
    Copy-Item $mutagenAgentsSrcPath $mutagenAgentsDestPath
}

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
    --theme-xml-path "scripts\files\RtfThemeLarge.xml" `
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
