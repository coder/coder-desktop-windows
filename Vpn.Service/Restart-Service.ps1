$name = "Coder Desktop (Debug)"

try {
    Restart-Service -Name $name -Force
    if ((Get-Service -Name $name -ErrorAction Stop).Status -ne "Running") {
        throw "Service '$name' is not running"
    }
    Write-Host "Service '$name' restarted successfully"
} catch {
    Write-Host $_ -ForegroundColor Red
    Write-Host "Press Return to exit..."
    Read-Host
}
