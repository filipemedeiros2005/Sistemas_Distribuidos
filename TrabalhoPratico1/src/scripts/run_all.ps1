Write-Host "=== [ ONE HEALTH - ECOSSISTEMA AUTOMATICO ] ===" -ForegroundColor Cyan

Write-Host "A verificar dependencias essenciais no sistema..." -ForegroundColor Yellow
if (-not (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
    Write-Host "[ERRO FATAL] O SDK do .NET (v9.0) nao esta instalado nas Variaveis de Ambiente." -ForegroundColor Red
    Write-Host "         O Avalonia UI e os nos distribuídos dependem do compilador C#." -ForegroundColor Red
    exit 1
}

$pgRunning = $false
# Verificar se porta do PostgreSQL (5432) esta em uso (ou seja, se a DB ta ligada)
try {
    $tcp = Get-NetTCPConnection -LocalPort 5432 -ErrorAction SilentlyContinue
    if ($tcp) { $pgRunning = $true }
} catch {}

if (-not $pgRunning) {
    Write-Host "[ERRO] Base de Dados PostgreSQL nao aparenta estar a correr na porta 5432." -ForegroundColor Red
    Write-Host "       A analise requer ligacao a uma instancia PostgreSQL com user/pass postgres." -ForegroundColor Yellow
    exit 1
}

$PSScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$DIR = Resolve-Path "$PSScriptRoot\.."
cd $DIR

# Sobe o broker RabbitMQ (TP2 - Fase 1). Aguarda o healthcheck antes de avancar.
if (Get-Command "docker" -ErrorAction SilentlyContinue) {
    Write-Host "[INFRA] A arrancar RabbitMQ via docker compose..." -ForegroundColor Cyan
    docker compose -f "$DIR\infra\docker-compose.yml" up -d
    for ($i = 0; $i -lt 24; $i++) {
        $status = (docker inspect --format='{{.State.Health.Status}}' onehealth-rabbitmq 2>$null)
        if ($status -eq "healthy") {
            Write-Host "[INFRA] RabbitMQ pronto (admin em http://localhost:15672 -- guest/guest)." -ForegroundColor Green
            break
        }
        Start-Sleep -Seconds 2
    }
} else {
    Write-Host "[AVISO] Docker nao detectado. RabbitMQ nao podera arrancar (Fase 1 TP2)." -ForegroundColor Yellow
}

dotnet build OneHealth.sln

# Pre-processor Go service (TP2 - Fase 2).
if (Get-Command "go" -ErrorAction SilentlyContinue) {
    Write-Host "[INFRA] A compilar e arrancar pre-processor (Go)..." -ForegroundColor Cyan
    Push-Location "$DIR\services\preprocessor-go"
    & make build *> $null
    Pop-Location
    $preproc = Start-Process -FilePath "$DIR\services\preprocessor-go\bin\preprocessor" -PassThru -WindowStyle Hidden -RedirectStandardOutput "$env:TEMP\oh_preproc.log"
    $preproc.Id | Out-File "$env:TEMP\oh_preproc.pid"
    Start-Sleep -Seconds 1
    Write-Host "[INFRA] Pre-processor PID=$($preproc.Id) em :50051." -ForegroundColor Green
} else {
    Write-Host "[AVISO] Go nao detectado - pre-processor desligado." -ForegroundColor Yellow
}

Start-Process "dotnet" -ArgumentList "run", "--no-build", "--project", "OneHealth.Server/OneHealth.Server.csproj"
Start-Sleep -Seconds 3

Start-Process "dotnet" -ArgumentList "run", "--no-build", "--project", "OneHealth.Gateway/OneHealth.Gateway.csproj", "--", "5001"
Start-Process "dotnet" -ArgumentList "run", "--no-build", "--project", "OneHealth.Gateway/OneHealth.Gateway.csproj", "--", "5002"
Start-Sleep -Seconds 2

Start-Process "dotnet" -ArgumentList "run", "--no-build", "--project", "OneHealth.Sensor/OneHealth.Sensor.csproj", "--", "101", "auto", "5001"
Start-Process "dotnet" -ArgumentList "run", "--no-build", "--project", "OneHealth.Sensor/OneHealth.Sensor.csproj", "--", "102", "auto", "5001"
Start-Process "dotnet" -ArgumentList "run", "--no-build", "--project", "OneHealth.Sensor/OneHealth.Sensor.csproj", "--", "103", "auto", "5002"
Start-Process "dotnet" -ArgumentList "run", "--no-build", "--project", "OneHealth.Sensor/OneHealth.Sensor.csproj", "--", "104", "auto", "5002"

dotnet run --no-build --project OneHealth.Dashboard/OneHealth.Dashboard.csproj

# --- COMANDOS UTEIS ---
# Para abrir o sensor 999 NOUA NOVA JANELA (a partir da pasta 'src'):
# Start-Process "dotnet" -ArgumentList "run", "--project", "OneHealth.Sensor/OneHealth.Sensor.csproj", "--", "999", "manual", "5001"
#
# Para executar no terminal atual (a partir de 'src/scripts'):
# ./teste_manual.ps1
#
# Para aceder a BD PostgreSQL no terminal:
# psql -h localhost -U postgres -d onehealth
# (A password: postgres)
#
# Exemplo de comando SQL (ultimas 30 entradas):
# SELECT * FROM telemetry ORDER BY timestamp DESC LIMIT 30;
# ----------------------