# ADR 0015: Automação Inteligente de Sensores (EMA & Eventos Críticos Anómalos)
**Status**: Proposed
**Data**: 18 de Abril de 2026

## Contexto
O protótipo de cliente CLI que envia input em String providencia as interações pedidas para os Docentes na "Fase 2", e o fecho natural `BYE`. O passo adiante requer simulações autônomas robustas no tempo para que o Gateway possa provar a Fila Circular Agregada em cenários de Stress-Test / Timeout do Watchdog (> 45s).

## Decisão
Implementar nos Clientes uma autogeração paralela usando uma Média Móvel Exponencial Ponderada (EMA) de Suavização:
`EMA = (Valor_Atual * Alpha) + (EMA_Anterior * (1 - Alpha))` com `α=0.2`.
O sensor gera valores sintéticos por "Thread.Timer" passivo (5 em 5 segundos), engatilhando ainda flutuações e anomalias de incêndio aleatórias com 5% de gravidade extrema ponderada e um respetivo switch do `MsgType.Alert`.

## Consequências
* **Positivas**: Simula o comportamento realista e aleatório de um Sensor ambiental ou biomédico sem manipulação programática, abrindo portas a um Stress Test end-to-end do Sistema Distribuído com o Watchdog.
* **Negativas**: A Anomalia aleatória exige um seguimento logístico assíncrono durante eventos de debug e impede o rastreio padronizado com valores controlados no tempo pelo Administrador.
