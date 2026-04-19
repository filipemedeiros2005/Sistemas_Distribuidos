#!/bin/bash
echo "=== [ ONE HEALTH - ECOSSISTEMA AUTOMATICO ] ==="
DIR="$(cd "$(dirname "$0")/.." && pwd)"
cd "$DIR"

# Compila a solucao 1 unica vez no inicio
dotnet build OneHealth.sln

# 1. Servidor
osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --no-build --project OneHealth.Server/OneHealth.Server.csproj\""
sleep 3 

# 2. Gateways
osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --no-build --project OneHealth.Gateway/OneHealth.Gateway.csproj -- 5001\""
osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --no-build --project OneHealth.Gateway/OneHealth.Gateway.csproj -- 5002\""
sleep 2

# 3. Sensores (Modo Auto)
osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --no-build --project OneHealth.Sensor/OneHealth.Sensor.csproj -- 101 auto 5001\""
osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --no-build --project OneHealth.Sensor/OneHealth.Sensor.csproj -- 102 auto 5001\""
osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --no-build --project OneHealth.Sensor/OneHealth.Sensor.csproj -- 103 auto 5002\""
osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --no-build --project OneHealth.Sensor/OneHealth.Sensor.csproj -- 104 auto 5002\""

# 4. Dashboard
dotnet run --no-build --project OneHealth.Dashboard/OneHealth.Dashboard.csproj