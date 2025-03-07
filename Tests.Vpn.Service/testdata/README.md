# Tests.Vpn.Service testdata

### Executables

`Build-Assets.ps1` creates `hello.exe` and derivatives. You need `go`,
`go-winres` and Windows 10 SDK 10.0.19041.0 installed to run this.

You must sign `hello-versioned-signed.exe` yourself with the Coder EV cert after
the script completes.

These files are checked into the repo so they shouldn't need to be built again.

### Certificates

- `coder-ev.crt` is the Extended Validation Code Signing certificate used by
  Coder, extracted from a signed release binary on 2025-03-07
- `google-llc-ev.crt` is the Extended Validation Code Signing certificate used
  by Google Chrome, extracted from an official binary on 2025-03-07
- `mozilla-corporation.crt` is a regular Code Signing certificate used by
  Mozilla Firefox, extracted from an official binary on 2025-03-07
- `self-signed-ev.crt` was generated with `gen-certs.sh` using Linux OpenSSL
- `self-signed.crt` was generated with `gen-certs.sh` using Linux OpenSSL

You can extract a certificate from an executable with the following PowerShell
one-liner:

```powershell
Get-AuthenticodeSignature binary.exe | Select-Object -ExpandProperty SignerCertificate | Export-Certificate -Type CERT -FilePath output.crt
```
