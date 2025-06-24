# This is mostly just here for reference.
#
# Usage: Create-AppCastSigningKey.ps1 -outputKeyPath <path>
param (
    [Parameter(Mandatory = $true)]
    [string] $outputKeyPath
)

$ErrorActionPreference = "Stop"

& openssl.exe genpkey -algorithm ed25519 -out $outputKeyPath
if ($LASTEXITCODE -ne 0) { throw "Failed to generate ED25519 private key" }

# Export the public key in DER format
$pubKeyDerPath = "$outputKeyPath.pub.der"
& openssl.exe pkey -in $outputKeyPath -pubout -outform DER -out $pubKeyDerPath
if ($LASTEXITCODE -ne 0) { throw "Failed to export ED25519 public key" }

# Remove the DER header to get the actual key bytes
$pubBytes = [System.IO.File]::ReadAllBytes($pubKeyDerPath)[-32..-1]
Remove-Item $pubKeyDerPath

# Base64 encode and print
Write-Output "NetSparkle formatted public key:"
Write-Output ([Convert]::ToBase64String($pubBytes))
Write-Output ""
Write-Output "Private key written to $outputKeyPath"
