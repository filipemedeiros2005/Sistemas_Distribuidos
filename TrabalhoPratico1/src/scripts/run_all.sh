#!/bin/bash
echo "=== [ ONE HEALTH - ECOSSISTEMA AUTOMATICO ] ==="

echo "A verificar dependencias necessarias..."
if ! command -v dotnet &> /dev/null; then
    echo "[ERRO FATAL] .NET SDK (v9.0) não foi detectado no sistema."
    echo "         Instale o dotnet primeiro antes de rodar os scripts."
    exit 1
fi

if ! command -v docker &> /dev/null; then
    echo "[AVISO] Docker nao detectado. O broker RabbitMQ (Fase 1 TP2) nao podera arrancar."
fi

if ! nc -z localhost 5432 &>/dev/null; then
    echo "[AVISO] Nao foi possivel confirmar PostgreSQL na porta 5432."
    echo "        Se nao utilizar a configuracao padrao do PG, podem ocorrer erros de BD."
fi

DIR="$(cd "$(dirname "$0")/.." && pwd)"
cd "$DIR"

# Sobe o broker RabbitMQ (TP2). O healthcheck dentro do compose garante
# que o port 5672 está aceitar AMQP antes de avancarmos.
if command -v docker &> /dev/null; then
    echo "[INFRA] A arrancar RabbitMQ via docker compose..."
    docker compose -f infra/docker-compose.yml up -d
    echo "[INFRA] A aguardar healthcheck do broker..."
    for i in $(seq 1 24); do
        status=$(docker inspect --format='{{.State.Health.Status}}' onehealth-rabbitmq 2>/dev/null || echo "missing")
        if [ "$status" = "healthy" ]; then
            echo "[INFRA] RabbitMQ pronto (admin em http://localhost:15672 -- guest/guest)."
            break
        fi
        sleep 2
    done
fi


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

# --- COMANDOS UTEIS ---
# Para abrir o sensor 999 NOVA JANELA (a partir da pasta 'src'):
# osascript -e "tell application \"Terminal\" to do script \"cd '$(pwd)' && dotnet run --project OneHealth.Sensor/OneHealth.Sensor.csproj -- 999 manual 5001\""
#
# Para executar no terminal atual (a partir de 'src/scripts'):
# ./teste_manual.sh
#
# Para aceder a BD PostgreSQL no terminal:
# psql -h localhost -U postgres -d onehealth
# (A password: postgres)
#
# Exemplo de comando SQL (ultimas 30 entradas):
# SELECT * FROM telemetry ORDER BY timestamp DESC LIMIT 30;
# ----------------------
