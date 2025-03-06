$errorActionPreference = "Stop"

Set-Location $PSScriptRoot

# If hello.go does not exist, write it. We don't check it into the repo to avoid
# GitHub showing that the repo contains Go code.
if (-not (Test-Path "hello.go")) {
    $helloGo = @"
package main

func main() {
    println("Hello, World!")
}
"@
    Set-Content -Path "hello.go" -Value $helloGo
}

& go.exe build -ldflags '-w -s' -o hello.exe hello.go
if ($LASTEXITCODE -ne 0) { throw "Failed to build hello.exe" }

# hello-invalid-version.exe is used for testing versioned binaries with an
# invalid version.
Copy-Item hello.exe hello-invalid-version.exe
& go-winres.exe patch --in winres.json --delete --no-backup --product-version 1-2-3-4 --file-version 1-2-3-4 hello-invalid-version.exe
if ($LASTEXITCODE -ne 0) { throw "Failed to patch hello-invalid-version.exe with go-winres" }

# hello-self-signed.exe is used for testing untrusted binaries.
Copy-Item hello.exe hello-self-signed.exe
$helloSelfSignedPath = (Get-Item hello-self-signed.exe).FullName

# Create a self signed certificate for signing and then delete it.
$certStoreLocation = "Cert:\CurrentUser\My"
$password = "password"
$cert = New-SelfSignedCertificate `
    -CertStoreLocation $certStoreLocation `
    -DnsName coder.com `
    -Subject "CN=coder-desktop-windows-self-signed-cert" `
    -Type CodeSigningCert `
    -KeyUsage DigitalSignature `
    -NotAfter (Get-Date).AddDays(3650)
$pfxPath = Join-Path $PSScriptRoot "cert.pfx"
try {
    $securePassword = ConvertTo-SecureString -String $password -Force -AsPlainText
    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword

    # Sign hello-self-signed.exe with the self signed certificate
    & "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.19041.0\x64\signtool.exe" sign /debug /f $pfxPath /p $password /tr "http://timestamp.digicert.com" /td sha256 /fd sha256 $helloSelfSignedPath
    if ($LASTEXITCODE -ne 0) { throw "Failed to sign hello-self-signed.exe with signtool" }
} finally {
    if ($cert.Thumbprint) {
        Remove-Item -Path (Join-Path $certStoreLocation $cert.Thumbprint) -Force
    }
    if (Test-Path $pfxPath) {
        Remove-Item -Path $pfxPath -Force
    }
}

# hello-versioned-signed.exe is used for testing versioned binaries and
# binaries signed by a real EV certificate.
Copy-Item hello.exe hello-versioned-signed.exe

& go-winres.exe patch --in winres.json --delete --no-backup --product-version 1.2.3.4 --file-version 1.2.3.4 hello-versioned-signed.exe
if ($LASTEXITCODE -ne 0) { throw "Failed to patch hello-versioned-signed.exe with go-winres" }

# Then sign hello-versioned-signed.exe with the same EV cert as our real
# binaries. Since this is a bit more complicated and requires some extra
# permissions, we don't do this in the build script.
Write-Host "Don't forget to sign hello-versioned-signed.exe with the EV cert!"
