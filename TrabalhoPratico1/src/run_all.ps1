Write-Host "🚀 A iniciar Topologia 4 Sensores -> 2 Gateways -> 1 Servidor..." -ForegroundColor Cyan

dotnet build OneHealth.sln

Start-Process "dotnet" -ArgumentList "run", "--project", "OneHealth.Server/OneHealth.Server.csproj"
Start-Sleep -Seconds 3

Start-Process "dotnet" -ArgumentList "run", "--project", "OneHealth.Gateway/OneHealth.Gateway.csproj", "--", "5001"
Start-Process "dotnet" -ArgumentList "run", "--project", "OneHealth.Gateway/OneHealth.Gateway.csproj", "--", "5002"
Start-Sleep -Seconds 2

Start-Process "dotnet" -ArgumentList "run", "--project", "OneHealth.Sensor/OneHealth.Sensor.csproj", "--", "101", "auto", "5001"
Start-Process "dotnet" -ArgumentList "run", "--project", "OneHealth.Sensor/OneHealth.Sensor.csproj", "--", "102", "auto", "5001"
Start-Process "dotnet" -ArgumentList "run", "--project", "OneHealth.Sensor/OneHealth.Sensor.csproj", "--", "103", "auto", "5002"
Start-Process "dotnet" -ArgumentList "run", "--project", "OneHealth.Sensor/OneHealth.Sensor.csproj", "--", "104", "auto", "5002"

Start-Process "dotnet" -ArgumentList "run", "--project", "OneHealth.Dashboard/OneHealth.Dashboard.csproj"