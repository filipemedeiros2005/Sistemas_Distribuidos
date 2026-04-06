# ADR 006: Validação Perimetral e Resposta NACK
**Estatuto**: Aceite
**Data**: 2026-04-05

## Contexto
Sensores físicos são o elo mais vulnerável da rede a nível de intrusão, interceção ou configuração errónea. Submeter o núcleo do sistema a autenticações contínuas destrói a capacidade de processamento útil do Servidor Central.

## Decisão
Implementar o isolamento e validação no Gateway (Edge Firewalling).
1. O Gateway valida o pacote de Handshake (MsgType 1: HELO) contra um documento local sensors_config.csv.
2. Identidades ausentes ou corrompidas recebem imediatamente uma rejeição formal via pacote MsgType 5: NACK, originando o fecho sumário da socket TCP.

## Consequências
* **Positivas**: Purga do tráfego não autorizado fora da rede nuclear, blindando o servidor contra injeção de dados piratas.
* **Negativas**: Introduz a necessidade de espelhar atualizações dos ficheiros de configuração CSV pelos múltiplos Gateways sempre que a infraestrutura física dos sensores é expandida.