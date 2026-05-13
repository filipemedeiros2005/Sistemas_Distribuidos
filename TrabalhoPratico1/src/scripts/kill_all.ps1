Write-Host "=== [ ONE HEALTH - CLEANUP WINDOWS ] ===" -ForegroundColor Cyan
Write-Host "Encerrando todos os processos 'dotnet'..."

# Forcar o encerramento dos processos dotnet para libertar as portas e a bd
Stop-Process -Name "dotnet" -Force -ErrorAction SilentlyContinue
Stop-Process -Name "OneHealth*" -Force -ErrorAction SilentlyContinue

# Pre-processor Go (TP2).
if (Test-Path "$env:TEMP\oh_preproc.pid") {
    $pid_preproc = Get-Content "$env:TEMP\oh_preproc.pid"
    Stop-Process -Id $pid_preproc -Force -ErrorAction SilentlyContinue
    Remove-Item "$env:TEMP\oh_preproc.pid" -ErrorAction SilentlyContinue
}
Stop-Process -Name "preprocessor" -Force -ErrorAction SilentlyContinue

# Analysis-py service (TP2 - Fase 3).
if (Test-Path "$env:TEMP\oh_analysis.pid") {
    $pid_analysis = Get-Content "$env:TEMP\oh_analysis.pid"
    Stop-Process -Id $pid_analysis -Force -ErrorAction SilentlyContinue
    Remove-Item "$env:TEMP\oh_analysis.pid" -ErrorAction SilentlyContinue
}

Write-Host "Libertando portas especificas relativas a gateways e sensores..."
$ports = @(5000, 5001, 5002, 5005, 5006, 6000, 6001, 7000, 50051, 50052)
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
