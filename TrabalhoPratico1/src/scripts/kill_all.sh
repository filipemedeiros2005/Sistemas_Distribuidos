#!/bin/bash
echo "=== [ ONE HEALTH - CLEANUP MAC/LINUX ] ==="
echo "Encerrando todos os processos 'dotnet' relativos a OneHealth..."

# Mata as execuções de dotnet que correm no Terminal
pkill -f "dotnet run"
pkill -f "OneHealth"

echo "Limpando portas presas (5000, 5001, 5002, 5005, 6000, 6001, 7000)..."
lsof -ti :5000,5001,5002,5005,6000,6001,7000 | xargs kill -9 2>/dev/null

# Fecha as abas do Terminal que ficaram abertas com as execucoes
osascript -e 'tell application "Terminal" to close (every window whose name contains "dotnet")' 2>/dev/null

echo "Limpeza concluida com sucesso!"
