Write-Host "=== [ ONE HEALTH - ECOSSISTEMA AUTOMATICO ] ==="
$PSScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$DIR = Resolve-Path "$PSScriptRoot\.."
cd $DIR

dotnet build OneHealth.sln

Start-Process "dotnet" -ArgumentList "run", "--no-build", "--project", "OneHealth.Server/OneHealth.Server.csproj"
Start-Sleep -Seconds 3

Start-Process "dotnet" -ArgumentList "run", "--no-build", "--project", "OneHealth.Gateway/OneHealth.Gateway.csproj", "--", "5001"
Start-Process "dotnet" -ArgumentList "run", "--no-build", "--project", "OneHealth.Gateway/OneHealth.Gateway.csproj", "--", "5002"
Start-Sleep -Seconds 2

Start-Process "dotnet" -ArgumentList "run", "--no-build", "--project", "OneHealth.Sensor/OneHealth.Sensor.csproj", "--", "101", "auto", "5001"
Start-Process "dotnet" -ArgumentList "run", "--no-build", "--project", "OneHealth.Sensor/OneHealth.Sensor.csproj", "--", "102", "auto", "5001"
Start-Process "dotnet" -ArgumentList "run", "--no-build", "--project", "OneHealth.Sensor/OneHealth.Sensor.csproj", "--", "103", "auto", "5002"
Start-Process "dotnet" -ArgumentList "run", "--no-build", "--project", "OneHealth.Sensor/OneHealth.Sensor.csproj", "--", "104", "auto", "5002"

dotnet run --no-build --project OneHealth.Dashboard/OneHealth.Dashboard.csproj