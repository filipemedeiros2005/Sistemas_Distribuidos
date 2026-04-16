# ADR 007: Arquitetura de Protocolo Híbrido (TCP + UDP)
**Status**: Aceite (Substitui parte do ADR 001)
**Data**: 16 de abril de 2026

## Contexto
A telemetria exige fiabilidade absoluta (TCP), enquanto o vídeo em direto exige baixa latência. Usar TCP para vídeo causaria atrasos acumulados (lag) insuportáveis em caso de perda de pacotes.

## Decisão
Adotar uma stack de rede dupla. Canal de Controlo/Telemetria via TCP (Porta 5001) e Canal de Multimédia via UDP (Porta 6000).

## Consequências
* **Positivas**: Garante fluidez no vídeo e segurança nos alertas.
* **Negativas**: Maior complexidade no desenvolvimento do Sensor.

