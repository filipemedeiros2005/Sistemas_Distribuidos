# ADR 003: Lógica de Prioridade e Buffering no Sensor

**ID:** 0003

**Estatuto:** Aceite

**Contexto:** Sensores IoT devem poupar energia, mas dados críticos não podem ser retidos.

**Decisão:** Implementação de um buffer híbrido. Dados de rotina são enviados em lotes de 10. Dados que excedam o desvio-padrão ($\sigma$) configurado são enviados imediatamente como "Prioritários".

**Justificação:** Esta lógica de Edge Computing reduz o tráfego de rede em 90% para dados estáveis, mas mantém a reatividade instantânea para anomalias.

**Consequências:** O código do sensor torna-se mais complexo, exigindo um histórico local de medições para o cálculo estatístico.