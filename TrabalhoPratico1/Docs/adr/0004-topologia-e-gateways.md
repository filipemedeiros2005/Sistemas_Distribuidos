# ADR 004: Buffering de Contexto em Eventos Anómalos (Caixa Negra)
**Estatuto**: Aceite

## Contexto
Para poupar energia, o sensor guarda dados num *buffer* rotineiro e envia em lotes de 10. Contudo, se ocorrer um pico crítico ambiental que destrua o hardware logo a seguir, o contexto histórico contido no buffer será perdido (Fase 3: Pré-processamento e encaminhamento).

## Decisão
Quando a matemática (EMA) deteta uma anomalia (MsgType 6: ALERT):
1. O valor crítico não é inserido na fórmula EMA (evitando *baseline poisoning*).
2. O sensor concatena o buffer de rotina pendente com a medição de urgência.
3. Este lote especial é enviado instantaneamente pelo Socket TCP (via *flush*), e o buffer local é reiniciado.

## Consequências
* **Positivas**: Garante o comportamento de "Caixa Negra", enviando sempre o histórico dos momentos antes do desastre.
* **Negativas**: Ligeira disrupção no tamanho fixo do lote (um envio pode conter 3, 5 ou 9 pacotes, em vez dos 10 padronizados), obrigando o Gateway a ser flexível na receção.