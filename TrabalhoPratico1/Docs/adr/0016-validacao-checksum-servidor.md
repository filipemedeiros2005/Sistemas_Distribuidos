# ADR 0016: Validação de CheckSum e Filtragem de Pacotes Corrompidos no Servidor
**Status**: Accepted
**Data**: 19 de Abril de 2026

## Contexto
Durante a auditoria de resiliência e a evolução para o tratamento rigoroso de dados ambientais, identificou-se a necessidade do Servidor não assumir que os dados que chegam estão sempre em perfeito estado. Transmissões de rede podem causar corrupção de bytes (apesar do TCP garantir a integridade da stream, bugs nos Gateways ou problemas de memória podem gerar *data corruption* antes do envio).

## Decisão
Implementar a validação obrigatória do campo `CheckSum` dos pacotes diretamente no Servidor, calculando o hash ou redundância esperada e comparando com o valor embutido no pacote `TelemetryPacket`. Caso os valores não correspondam, o pacote é descartado imediatamente (não sendo processado ou gravado).

## Consequências
* **Positivas**: Evita a poluição dos registos (logs) e da posterior base de dados relacional com dados inconsistentes ou incorretos (lixo binário). Aumenta fortemente a robustez contra potenciais ataques ou sensores defeituosos.
* **Negativas**: Representa um custo computacional (overhead) minúsculo para cada pacote que chega, visto que o Servidor terá de recalcular algoritmos de verificação em alta frequência consoante a escala do sistema.
