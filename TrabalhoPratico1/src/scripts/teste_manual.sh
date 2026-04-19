#!/bin/bash
echo "=== [ ONE HEALTH - INJETOR DE DADOS MANUAL ] ==="
read -p "ID do Sensor (Ex: 999): " id_manual
read -p "Porta do Gateway (5001 ou 5002): " porta_gw

DIR="$(cd "$(dirname "$0")/.." && pwd)"
cd "$DIR"

dotnet run --no-build --project OneHealth.Sensor/OneHealth.Sensor.csproj -- $id_manual manual $porta_gw