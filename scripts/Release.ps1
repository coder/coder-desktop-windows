# Usage: Release.ps1 -version <version>
param (
  [Parameter(Mandatory = $true)]
  [ValidatePattern("^\d+\.\d+\.\d+\.\d+$")]
  [string] $version,

  [Parameter(Mandatory = $true)]
  [ValidatePattern("^\d+\.\d+\.\d+\.\d+$")]
  [string] $assemblyVersion
)

$ErrorActionPreference = "Stop"

foreach ($arch in @("x64", "arm64")) {
  Write-Host "::group::Publishing $arch"
  try {
    $archUpper = $arch.ToUpper()

    $msiOutputPath = "publish/CoderDesktopCore-$version-$arch.msi"
    Add-Content -Path $env:GITHUB_OUTPUT -Value "$($archUpper)_MSI_OUTPUT_PATH=$msiOutputPath"
    Write-Host "MSI_OUTPUT_PATH=$msiOutputPath"

    $outputPath = "publish/CoderDesktop-$version-$arch.exe"
    Add-Content -Path $env:GITHUB_OUTPUT -Value "$($archUpper)_OUTPUT_PATH=$outputPath"
    Write-Host "OUTPUT_PATH=$outputPath"

    $publishScript = Join-Path $PSScriptRoot "Publish.ps1"
    & $publishScript `
      -version $assemblyVersion `
      -arch $arch `
      -msiOutputPath $msiOutputPath `
      -outputPath $outputPath `
      -sign
    if ($LASTEXITCODE -ne 0) { throw "Failed to publish" }

    # Verify that the output exe is authenticode signed
    $sig = Get-AuthenticodeSignature $outputPath
    if ($sig.Status -ne "Valid") {
      throw "Output file is not authenticode signed"
    }
    else {
      Write-Host "Output file is authenticode signed"
    }
  }
  finally {
    Write-Host "::endgroup::"
  }
}
