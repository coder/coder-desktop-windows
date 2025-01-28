& $PSScriptRoot/Stop-Service.ps1
dotnet build -c Debug ./Vpn.Service.csproj
& $PSScriptRoot/Restart-Service.ps1
