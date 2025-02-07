# CD to the directory of this PS script
Push-Location $PSScriptRoot

# Create a publish directory
$publishDir = Join-Path $PSScriptRoot "publish"
if (Test-Path $publishDir) {
    # prompt the user to confirm the deletion
    $confirm = Read-Host "The directory $publishDir already exists. Do you want to delete it? (y/n)"
    if ($confirm -eq "y") {
        Remove-Item -Recurse -Force $publishDir
    } else {
        Write-Host "Aborting..."
        exit
    }
}
New-Item -ItemType Directory -Path $publishDir

# Build in release mode
dotnet.exe clean
dotnet.exe publish .\Vpn.Service\Vpn.Service.csproj -c Release -a x64 -o $publishDir\service
$msbuildBinary = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe
& $msbuildBinary .\App\App.csproj /p:Configuration=Release /p:Platform=x64 /p:OutputPath=..\publish\app /p:GenerateAppxPackageOnBuild=true

$scriptsDir = Join-Path $publishDir "scripts"
New-Item -ItemType Directory -Path $scriptsDir

# Download the 1.6.250108002 redistributable zip from here and drop the x64
# version in the root of the repo:
# https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads
$windowsAppSdkInstaller = Join-Path $PSScriptRoot "WindowsAppRuntimeInstall-x64.exe"
Copy-Item $windowsAppSdkInstaller $scriptsDir

# Acquire wintun.dll and put it in the root of the repo.
$wintunDll = Join-Path $PSScriptRoot "wintun.dll"
Copy-Item $wintunDll $scriptsDir

# Add a PS1 script for installing the service
$installScript = Join-Path $scriptsDir "Install.ps1"
$installScriptContent = @"
try {
    # Install Windows App SDK
    `$installerPath = Join-Path `$PSScriptRoot "WindowsAppRuntimeInstall-x64.exe"
    Start-Process `$installerPath -ArgumentList "/silent" -Wait

    # Install wintun.dll
    `$wintunPath = Join-Path `$PSScriptRoot "wintun.dll"
    Copy-Item `$wintunPath "C:\wintun.dll"

    # Install and start the service
    `$name = "Coder Desktop (Debug)"
    `$binaryPath = Join-Path `$PSScriptRoot "..\service\Vpn.Service.exe" | Resolve-Path
    New-Service -Name `$name -BinaryPathName `$binaryPath -StartupType Automatic
    Start-Service -Name `$name
} catch {
    Write-Host ""
    Write-Host -Foreground Red "Error: $_"
} finally {
    Write-Host ""
    Write-Host "Press Return to exit..."
    Read-Host
}
"@
Set-Content -Path $installScript -Value $installScriptContent

# Add a batch script for running the install script
$installBatch = Join-Path $publishDir "Install.bat"
$installBatchContent = @"
@echo off
powershell -Command "Start-Process powershell -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%~dp0scripts\Install.ps1\"' -Verb RunAs"
"@
Set-Content -Path $installBatch -Value $installBatchContent

# Add a PS1 script for uninstalling the service
$uninstallScript = Join-Path $scriptsDir "Uninstall.ps1"
$uninstallScriptContent = @"
try {
    # Uninstall the service
    `$name = "Coder Desktop (Debug)"
    Stop-Service -Name `$name
    sc.exe delete `$name

    # Delete wintun.dll
    Remove-Item "C:\wintun.dll"

    # Maybe delete C:\coder-vpn.exe and C:\CoderDesktop.log
    Remove-Item "C:\coder-vpn.exe" -ErrorAction SilentlyContinue
    Remove-Item "C:\CoderDesktop.log" -ErrorAction SilentlyContinue
} catch {
    Write-Host ""
    Write-Host -Foreground Red "Error: $_"
} finally {
    Write-Host ""
    Write-Host "Press Return to exit..."
    Read-Host
}
"@
Set-Content -Path $uninstallScript -Value $uninstallScriptContent

# Add a batch script for running the uninstall script
$uninstallBatch = Join-Path $publishDir "Uninstall.bat"
$uninstallBatchContent = @"
@echo off
powershell -Command "Start-Process powershell -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%~dp0scripts\Uninstall.ps1\"' -Verb RunAs"
"@
Set-Content -Path $uninstallBatch -Value $uninstallBatchContent

# Add a PS1 script for starting the app
$startAppScript = Join-Path $publishDir "StartTrayApp.bat"
$startAppScriptContent = @"
@echo off
start /B app\App.exe
"@
Set-Content -Path $startAppScript -Value $startAppScriptContent

# Write README.md
$readme = Join-Path $publishDir "README.md"
$readmeContent = @"
# Coder Desktop for Windows

## Install
1. Install the service by double clicking `Install.bat`.
2. Start the app by double clicking `StartTrayApp.bat`.
3. The tray app should be available in the system tray.

## Uninstall
1. Close the tray app by right clicking the icon in the system tray and
   selecting "Exit".
2. Uninstall the service by double clicking `Uninstall.bat`.

## Notes
- During install and uninstall a User Account Control popup will appear asking
  for admin permissions. This is normal.
- During install and uninstall a bunch of console windows will appear and
  disappear. You will be asked to click "Return" to close the last one once
  it's finished doing its thing.
- The system service will start automatically when the system starts.
- The tray app will not start automatically on startup. You can start it again
  by double clicking `StartTrayApp.bat`.
"@
Set-Content -Path $readme -Value $readmeContent
