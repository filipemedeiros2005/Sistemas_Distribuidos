#!/bin/bash
echo "=== [ ONE HEALTH - CENTRO DE CONTROLO ] ==="
echo "Escolha o modo de operacao dos Sensores:"
echo "1. Modo Automatico (Ler dos ficheiros CSV + Abrir Dashboard)"
echo "2. Modo Manual (Inserir dados via teclado)"
read -p "Opcao (1 ou 2): " modo_opcao

MODO="auto"
if [ "$modo_opcao" == "2" ]; then
    MODO="manual"
fi

DIR="$(pwd)"
dotnet build OneHealth.sln

osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --project OneHealth.Server/OneHealth.Server.csproj\""
sleep 3 

osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --project OneHealth.Gateway/OneHealth.Gateway.csproj -- 5001\""
osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --project OneHealth.Gateway/OneHealth.Gateway.csproj -- 5002\""
sleep 2

osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --project OneHealth.Sensor/OneHealth.Sensor.csproj -- 101 $MODO 5001\""
osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --project OneHealth.Sensor/OneHealth.Sensor.csproj -- 102 $MODO 5001\""
osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --project OneHealth.Sensor/OneHealth.Sensor.csproj -- 103 $MODO 5002\""
osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --project OneHealth.Sensor/OneHealth.Sensor.csproj -- 104 $MODO 5002\""

if [ "$MODO" == "auto" ]; then
    echo "A abrir o Dashboard..."
    dotnet run --project OneHealth.Dashboard/OneHealth.Dashboard.csproj
fi