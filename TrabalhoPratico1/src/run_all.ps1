Write-Host "=== [ ONE HEALTH - CENTRO DE CONTROLO ] ===" -ForegroundColor Cyan
Write-Host "1. Modo Automatico (Ler dos ficheiros CSV + Abrir Dashboard)"
Write-Host "2. Modo Manual (Inserir dados via teclado)"
$modo_opcao = Read-Host "Opcao (1 ou 2)"

$MODO = "auto"
if ($modo_opcao -eq "2") { $MODO = "manual" }

dotnet build OneHealth.sln
Start-Process "dotnet" -ArgumentList "run", "--project", "OneHealth.Server/OneHealth.Server.csproj"
Start-Sleep -Seconds 3

Start-Process "dotnet" -ArgumentList "run", "--project", "OneHealth.Gateway/OneHealth.Gateway.csproj", "--", "5001"
Start-Process "dotnet" -ArgumentList "run", "--project", "OneHealth.Gateway/OneHealth.Gateway.csproj", "--", "5002"
Start-Sleep -Seconds 2

Start-Process "dotnet" -ArgumentList "run", "--project", "OneHealth.Sensor/OneHealth.Sensor.csproj", "--", "101", $MODO, "5001"
Start-Process "dotnet" -ArgumentList "run", "--project", "OneHealth.Sensor/OneHealth.Sensor.csproj", "--", "102", $MODO, "5001"
Start-Process "dotnet" -ArgumentList "run", "--project", "OneHealth.Sensor/OneHealth.Sensor.csproj", "--", "103", $MODO, "5002"
Start-Process "dotnet" -ArgumentList "run", "--project", "OneHealth.Sensor/OneHealth.Sensor.csproj", "--", "104", $MODO, "5002"

if ($MODO -eq "auto") { Start-Process "dotnet" -ArgumentList "run", "--project", "OneHealth.Dashboard/OneHealth.Dashboard.csproj" }