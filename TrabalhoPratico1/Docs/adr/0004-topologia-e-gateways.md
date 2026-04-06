# ADR 004: Topologia em Estrela Hierárquica e Múltiplos Gateways
**Estatuto**: Aceite
**Data**: 2026-04-05

## Contexto
O aumento do número de sensores acarreta esgotamento de portas de conexão num único ponto central. Um Gateway isolado configura um gargalo de processamento e um único ponto de falha global.

## Decisão
Adotar uma topologia em estrela hierárquica distribuída. O sistema suportará a instanciação de dezenas de Gateways paralelos de forma transparente. Os sensores conectam-se ao Gateway geograficamente mais próximo. O Gateway mantém conexões long-lived (persistentes) limitadas e multiplexadas para o Servidor Central.

## Consequências
* **Positivas**: Tolerância a falhas segmentada e distribuição da carga computacional.
* **Negativas**: Aumenta a dificuldade de rastreabilidade física dos pacotes, uma vez que o Servidor perde a visibilidade do IP original do sensor, confiando apenas no ID encapsulado no payload.