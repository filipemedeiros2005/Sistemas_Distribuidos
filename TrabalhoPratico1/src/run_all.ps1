Write-Host "🚀 A iniciar Ecossistema OneHealth (Windows)..." -ForegroundColor Cyan

# 1. Compilar a solução a partir da pasta src
dotnet build OneHealth.sln

# 2. Lançar o Servidor Central
Start-Process "dotnet" -ArgumentList "run", "--project", "OneHealth.Server/OneHealth.Server.csproj"

Start-Sleep -Seconds 2 # Aguardar que o servidor fique a ouvir

# 3. Lançar o Gateway
Start-Process "dotnet" -ArgumentList "run", "--project", "OneHealth.Gateway/OneHealth.Gateway.csproj"

Start-Sleep -Seconds 2

# 4. Lançar o Dashboard Visual
Start-Process "dotnet" -ArgumentList "run", "--project", "OneHealth.Dashboard/OneHealth.Dashboard.csproj"

# 5. Lançar 4 Sensores Automáticos
# Usamos os IDs e adicionamos a flag "auto" para eles não ficarem parados no menu
foreach ($id in 101, 102, 103, 104) {
    Start-Process "dotnet" -ArgumentList "run", "--project", "OneHealth.Sensor/OneHealth.Sensor.csproj", "$id", "auto"
}

Write-Host "✅ Sistema totalmente em execução!" -ForegroundColor Green

# Caso haja problemas por causa das permissões do powershell:
# Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass