# ADR 0022: Bypass da Pré-Processamento para Pacotes ALERT no Gateway
**Status**: Accepted
**Data**: 13 de Maio de 2026

## Contexto
A Fase 2 do TP2 introduziu um serviço de pré-processamento em Go (`services/preprocessor-go`, gRPC :50051), invocado pelo Gateway via `PreprocessorClient.NormalizeAsync` antes de empilhar cada pacote DATA ou ALERT na fila prioritária para o Servidor Central. O serviço aplica:
1. Conversão de unidades (TEMP: `F→C`, `K→C`);
2. Validação de limites por `data_type` (`TEMP ∈ [-40, 70]`, `HUM ∈ [0, 100]`, `RUIDO ∈ [0, 140]`, …).

A política `fail-closed` no Gateway descarta a leitura sempre que `Dropped=true` é devolvido. O problema é que um pacote `ALERT` — por definição — representa uma anomalia já classificada pelo Sensor (e.g. `RUIDO=145 dB`, `TEMP=82 °C`), pelo que excede frequentemente os limites canónicos. Aplicar a validação descarta exatamente os pacotes que deveriam chegar prioritários ao Servidor.

Foram consideradas três alternativas:
* **A.** Relaxar/anular os limites do serviço Go consoante o `msg_type` recebido — exigiria propagar `msg_type` pelo `RawMeasurement` (atualmente ausente) e bifurcar a tabela `dataTypeBounds`, aumentando a superfície de teste.
* **B.** Definir bounds separados para ALERT — mesma alteração no proto + duplicação de configuração.
* **C.** Saltar a chamada RPC no Gateway quando `msg_type == ALERT`.

## Decisão
Adotar a alternativa **C**: o Gateway invoca `PreprocessorClient.NormalizeAsync` apenas para pacotes `DATA`. Pacotes `ALERT` são enfileirados directamente em prioridade zero, sem normalização nem validação de limites. As mensagens `STATUS` e `BYE` continuam, como já estavam, fora do caminho da pré-processamento.

## Consequências
* **Positivas**:
  * Elimina o overhead de uma chamada gRPC por ALERT (latência ~ms + serialização) — caminho crítico mais rápido.
  * Resolve o conflito semântico sem mexer no proto nem no serviço Go: a lógica do `preprocessor-go` permanece simples, mono-purpose e *table-driven*.
  * Mantém a política `fail-closed` para DATA, que é o fluxo de volume.
* **Negativas**:
  * O Servidor passa a receber valores ALERT na unidade reportada pelo Sensor sem conversão de unidades pelo *edge*. Mitigação: os Sensores deste TP emitem sempre em unidades canónicas; o `unit_hint` é hoje informativo e a conversão F/K nunca foi exercida em produção.
  * Leituras `NaN/Inf` rotuladas como ALERT não serão filtradas no Gateway. Mitigação: o Sensor não gera `NaN/Inf` por construção; o Servidor pode opcionalmente rejeitar em segundo nível.