$name = "Coder Desktop (Debug)"

try {
    Stop-Service -Name $name -Force
    Write-Host "Service '$name' stopped successfully"
} catch {
    Write-Host $_ -ForegroundColor Red
    Write-Host "Press Return to exit..."
    Read-Host
}
