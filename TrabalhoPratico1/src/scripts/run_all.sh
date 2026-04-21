#!/bin/bash
echo "=== [ ONE HEALTH - ECOSSISTEMA AUTOMATICO ] ==="

echo "A verificar dependencias necessarias..."
if ! command -v dotnet &> /dev/null; then
    echo "[ERRO FATAL] .NET SDK (v9.0) não foi detectado no sistema."
    echo "         Instale o dotnet primeiro antes de rodar os scripts."
    exit 1
fi

if ! nc -z localhost 5432 &>/dev/null; then
    echo "[AVISO] Nao foi possivel confirmar PostgreSQL na porta 5432."
    echo "        Se nao utilizar a configuracao padrao do PG, podem ocorrer erros de BD."
fi

DIR="$(cd "$(dirname "$0")/.." && pwd)"
cd "$DIR"


dotnet build OneHealth.sln


osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --no-build --project OneHealth.Server/OneHealth.Server.csproj\""
sleep 3 


osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --no-build --project OneHealth.Gateway/OneHealth.Gateway.csproj -- 5001\""
osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --no-build --project OneHealth.Gateway/OneHealth.Gateway.csproj -- 5002\""
sleep 2


osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --no-build --project OneHealth.Sensor/OneHealth.Sensor.csproj -- 101 auto 5001\""
osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --no-build --project OneHealth.Sensor/OneHealth.Sensor.csproj -- 102 auto 5001\""
osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --no-build --project OneHealth.Sensor/OneHealth.Sensor.csproj -- 103 auto 5002\""
osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --no-build --project OneHealth.Sensor/OneHealth.Sensor.csproj -- 104 auto 5002\""


dotnet run --no-build --project OneHealth.Dashboard/OneHealth.Dashboard.csproj

# Para abrir o sensor 999 (sh):
# dotnet run --project OneHealth.Sensor/OneHealth.Sensor.csproj -- 999 manual 5001
