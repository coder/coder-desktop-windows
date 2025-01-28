# Elevate to administrator
if (-not ([Security.Principal.WindowsPrincipal]([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Elevating script to run as administrator..."
    Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$($MyInvocation.MyCommand.Path)`"" -Verb RunAs
    exit
}

$name = "Coder Desktop (Debug)"

try {
    Stop-Service -Name $name -Force -ErrorAction SilentlyContinue
    sc.exe delete $name
    Write-Host "Service '$name' deleted"
} catch {
    Write-Host $_ -ForegroundColor Red
    Write-Host "Press Return to exit..."
    Read-Host
}
