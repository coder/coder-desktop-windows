# Elevate to administrator
if (-not ([Security.Principal.WindowsPrincipal]([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Elevating script to run as administrator..."
    Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$($MyInvocation.MyCommand.Path)`"" -Verb RunAs
    exit
}

$name = "Coder Desktop (Debug)"
$binaryPath = Join-Path -Path $PSScriptRoot -ChildPath "bin/Debug/net8.0-windows/Vpn.Service.exe"

try {
    Write-Host "Creating service..."
    New-Service -Name $name -BinaryPathName "`"$binaryPath`"" -DisplayName $name -StartupType Automatic

    $sddl = & sc.exe sdshow $name
    if (-not $sddl) {
        throw "Failed to retrieve security descriptor for service '$name'"
    }
    Write-Host "Current security descriptor: '$sddl'"
    $sddl = $sddl.Trim() -replace "D:", "D:(A;;RPWP;;;WD)" # allow everyone to start, stop, pause, and query the service
    Write-Host "Setting security descriptor: '$sddl'"
    & sc.exe sdset $name $sddl

    Write-Host "Starting service..."
    Start-Service -Name $name

    if ((Get-Service -Name $name -ErrorAction Stop).Status -ne "Running") {
        throw "Service '$name' is not running"
    }
    Write-Host "Service '$name' created and started successfully"
} catch {
    Write-Host $_ -ForegroundColor Red
    Write-Host "Press Return to exit..."
    Read-Host
}
