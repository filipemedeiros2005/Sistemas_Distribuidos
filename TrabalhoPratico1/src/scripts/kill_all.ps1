Write-Host "=== [ ONE HEALTH - CLEANUP WINDOWS ] ===" -ForegroundColor Cyan
Write-Host "Encerrando todos os processos 'dotnet'..."

# Forcar o encerramento dos processos dotnet para libertar as portas e a bd
Stop-Process -Name "dotnet" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "OneHealth*" -Force -ErrorAction SilentlyContinue

Write-Host "Libertando portas especificas relativas a gateways e sensores..."
$ports = @(5000, 5001, 5002, 5005, 6000, 6001, 7000)
foreach ($port in $ports) {
    try {
        $connections = Get-NetTCPConnection -LocalPort $port -ErrorAction SilentlyContinue
        foreach ($conn in $connections) {
            Stop-Process -Id $conn.OwningProcess -Force -ErrorAction SilentlyContinue
        }
    } catch {}
}

# Para o broker RabbitMQ (TP2 - Fase 1).
if (Get-Command "docker" -ErrorAction SilentlyContinue) {
    $PSScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
    $DIR = Resolve-Path "$PSScriptRoot\.."
    Write-Host "[INFRA] A parar containers do docker compose..." -ForegroundColor Cyan
    docker compose -f "$DIR\infra\docker-compose.yml" down 2>$null
}

Write-Host "Limpeza concluida com sucesso!" -ForegroundColor Green
