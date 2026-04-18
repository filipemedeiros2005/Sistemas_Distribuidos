# ADR 005: Arquitetura de Fila de Prioridade no Gateway (QoS)
**Estatuto**: Aceite

## Contexto
Na função de agregação (Fase 3), o Gateway atua como multiplexador. Em picos de receção de rotina, um alerta não deve esperar numa fila FIFO tradicional.

## Decisão
Utilizar a estrutura `PriorityQueue` no Gateway.
Pacotes de rotina assumem prioridade secundária, enquanto pacotes marcados como `6:ALERT` adquirem prioridade máxima na *heap* da estrutura, passando para a frente da fila de envio, independentemente de terem chegado depois dos dados de rotina. O Gateway nunca destrói o contexto de rotina no seu próprio buffer.

## Consequências
* **Positivas**: Implementação de Qualidade de Serviço (QoS), priorizando a segurança pública.
* **Negativas**: O envio cronológico rede-abaixo perde-se ligeiramente. É por este motivo que o Timestamp gerado no ADR 001 é crucial para a reordenação no lado do Servidor.