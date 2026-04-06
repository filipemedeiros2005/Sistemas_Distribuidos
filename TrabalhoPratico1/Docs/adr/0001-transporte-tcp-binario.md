# ADR 001: Protocolo Binário de Comunicação e Transporte TCP
**Estatuto**: Aceite
**Data**: 2026-04-05

## Contexto
A infraestrutura exige a recolha contínua de dados ambientais críticos. A perda de pacotes ou desordenação na rede invalida a análise epidemiológica. Sensores possuem restrições de processamento, tornando formatos baseados em texto (JSON/XML) ineficientes devido ao overhead de parsing.

## Decisão
1. Adotar Sockets TCP para garantir entrega e ordenação sem necessidade de controlo de fluxo na camada aplicacional.
2. Definir um protocolo proprietário binário de tamanho fixo de 16 bytes.
3. Incorporar 2 bytes de padding (Reserved) no Offset 2 para forçar o alinhamento de memória do campo SensorID a múltiplos de 4, otimizando ciclos de fetch na CPU.

## Consequências
* **Positivas**: Máxima eficiência de rede e alinhamento perfeito de memória nas estruturas C#.
* **Negativas**: O pacote estruturado rigidamente em 16 bytes requer a emissão de uma nova versão do protocolo caso a estrutura de dados precise de evoluir.