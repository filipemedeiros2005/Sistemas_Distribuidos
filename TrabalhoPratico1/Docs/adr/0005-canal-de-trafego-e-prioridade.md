# ADR 005: Canal Duplo de Tráfego e Multiplexagem de Prioridade
**Estatuto**: Aceite
**Data**: 2026-04-05

## Contexto
Durante períodos em que diversos sensores esvaziam os seus buffers de rotina em simultâneo para o Gateway, um alerta crítico (MsgType 6) não pode aguardar numa fila FIFO convencional.

## Decisão
O mecanismo de roteamento do Gateway será construído sobre uma PriorityQueue. 
1. Pacotes de MsgType 2 (DATA) são classificados com prioridade secundária.
2. Pacotes MsgType 6 (ALERT) recebem prioridade máxima. 
A thread despachante processa o topo da heap matemática da fila, garantindo que as emergências são drenadas para o Servidor Central antes da rotina estagnada.

## Consequências
* **Positivas**: Qualidade de Serviço (QoS) rígida que evita a estagnação de alertas vitais.
* **Negativas**: Inversão forçada da linha cronológica natural de eventos recebidos pela rede, favorecendo a tipologia em detrimento da ordem temporal estrita.