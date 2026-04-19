Write-Host "=== [ ONE HEALTH - INJETOR DE DADOS MANUAL ] ==="
$id_manual = Read-Host "ID do Sensor (Ex: 999)"
$porta_gw = Read-Host "Porta do Gateway (5001 ou 5002)"

$PSScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$DIR = Resolve-Path "$PSScriptRoot\.."
cd $DIR

dotnet run --no-build --project OneHealth.Sensor/OneHealth.Sensor.csproj -- $id_manual manual $porta_gw