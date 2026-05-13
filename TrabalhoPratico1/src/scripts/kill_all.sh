#!/bin/bash
echo "=== [ ONE HEALTH - CLEANUP MAC/LINUX ] ==="
echo "Encerrando todos os processos 'dotnet' relativos a OneHealth..."

# Mata as execuções de dotnet que correm no Terminal
pkill -f "dotnet run"
pkill -f "OneHealth"

echo "Limpando portas presas (5000-5005, 6000-6001, 7000, 50051, 50052)..."
lsof -ti :5000,5001,5002,5005,6000,6001,7000,50051,50052 | xargs kill -9 2>/dev/null

# Pre-processor Go (TP2).
if [ -f /tmp/oh_preproc.pid ]; then
    kill -9 "$(cat /tmp/oh_preproc.pid)" 2>/dev/null
    rm -f /tmp/oh_preproc.pid
fi
pkill -9 -f "preprocessor-go/bin/preprocessor" 2>/dev/null

# Analysis-py service (TP2 - Fase 3).
if [ -f /tmp/oh_analysis.pid ]; then
    kill -9 "$(cat /tmp/oh_analysis.pid)" 2>/dev/null
    rm -f /tmp/oh_analysis.pid
fi
pkill -9 -f "analysis-py/server.py" 2>/dev/null

# Fecha as abas do Terminal que ficaram abertas com as execucoes
osascript -e 'tell application "Terminal" to close (every window whose name contains "dotnet")' 2>/dev/null

# Para o broker RabbitMQ (TP2 - Fase 1).
if command -v docker &> /dev/null; then
    DIR="$(cd "$(dirname "$0")/.." && pwd)"
    echo "[INFRA] A parar containers do docker compose..."
    docker compose -f "$DIR/infra/docker-compose.yml" down 2>/dev/null
fi

echo "Limpeza concluida com sucesso!"
