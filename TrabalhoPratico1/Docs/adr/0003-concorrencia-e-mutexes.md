# ADR 003: Estratégia de Buffering Duplo e Análise Estatística na Borda
**Estatuto**: Aceite
**Data**: 2026-04-05

## Contexto
Dispositivos IoT esgotam energia ao manter a interface de rede constantemente ativa. É necessário reter dados não essenciais, mas sem comprometer a propagação em tempo real de anomalias que apresentem risco para a saúde pública.

## Decisão
A arquitetura interna do Sensor implementará um buffer duplo e cálculo em tempo real:
1. **Janela Deslizante**: O sensor retém as últimas 50 amostras em memória contínua para computar a média móvel e o desvio-padrão local de forma a isolar o ruído de hardware.
2. **Buffer de Rotina**: Leituras dentro do padrão são armazenadas num buffer e transmitidas em lotes de 10 pacotes.
3. **Buffer de Urgência**: Leituras fora da margem de erro disparam o envio de todos os dados residuais de rotina acrescidos do evento de alerta (MsgType 6), via transmissão imediata.

## Consequências
* **Positivas**: Reduz transações de rede e preserva o contexto temporal anterior à anomalia (análise forense).
* **Negativas**: Aumenta a alocação de RAM no nó local e exige um período cego (warm-up phase) até a janela deslizante estabilizar as métricas iniciais.