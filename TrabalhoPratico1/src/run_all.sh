#!/bin/bash
echo "🚀 A iniciar Ecossistema OneHealth (macOS)..."

# Captura a pasta atual protegendo os espaços
DIR="$(pwd)"

# 1. Compilar tudo a partir da raiz da solution
dotnet build OneHealth.sln

# 2. Lançar Servidor
osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --project OneHealth.Server/OneHealth.Server.csproj\""

sleep 3 # Dá tempo para a BD ligar

# 3. Lançar Gateway
osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --project OneHealth.Gateway/OneHealth.Gateway.csproj\""

sleep 2

# 4. Lançar Sensores
for id in 101 102 103 104
do
  osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --project OneHealth.Sensor/OneHealth.Sensor.csproj $id auto\""
done

# 5. Lançar o Dashboard na janela atual!
echo "A abrir o Dashboard (Centro de Comando)..."
dotnet run --project OneHealth.Dashboard/OneHealth.Dashboard.csproj