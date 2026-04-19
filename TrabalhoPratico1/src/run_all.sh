#!/bin/bash
echo "🚀 A iniciar Topologia 4 Sensores -> 2 Gateways -> 1 Servidor..."
DIR="$(pwd)"

dotnet build OneHealth.sln

osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --project OneHealth.Server/OneHealth.Server.csproj\""
sleep 3 

osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --project OneHealth.Gateway/OneHealth.Gateway.csproj -- 5001\""
osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --project OneHealth.Gateway/OneHealth.Gateway.csproj -- 5002\""
sleep 2

# ATENÇÃO AO "--" AQUI! Ele garante que o código C# recebe os parâmetros.
osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --project OneHealth.Sensor/OneHealth.Sensor.csproj -- 101 auto 5001\""
osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --project OneHealth.Sensor/OneHealth.Sensor.csproj -- 102 auto 5001\""

osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --project OneHealth.Sensor/OneHealth.Sensor.csproj -- 103 auto 5002\""
osascript -e "tell application \"Terminal\" to do script \"cd '$DIR' && dotnet run --project OneHealth.Sensor/OneHealth.Sensor.csproj -- 104 auto 5002\""

echo "A abrir o Dashboard..."
dotnet run --project OneHealth.Dashboard/OneHealth.Dashboard.csproj