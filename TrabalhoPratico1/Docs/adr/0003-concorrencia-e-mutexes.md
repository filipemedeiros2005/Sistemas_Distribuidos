# ADR 003: Avaliação Estatística Baseada em Média Móvel Exponencial (EMA)
**Estatuto**: Aceite

## Contexto
Os sensores devem avaliar anomalias em tempo real, mitigando o ruído de hardware. Guardar uma matriz com as últimas 50 amostras (*Janela Deslizante*) consome memória contínua nos nós e requer recalibrações pesadas no arranque.

## Decisão
Substituir a Janela Deslizante pela **Média Móvel Exponencial (EMA)**, atribuindo um fator de suavização $\alpha = 0.1$ (o que equivale matematicamente a uma perspetiva de 19 amostras passadas).
O Sensor apenas retém em memória 2 variáveis float (`Media` e `Variância`), que são pré-carregadas via um ficheiro simples de calibração de fábrica.

## Consequências
* **Positivas**: Memória consumida pelos cálculos estatísticos no sensor passa a ser $O(1)$.
* **Negativas**: Ajuste subtil à anomalia não é instantâneo (comportamento desejável para evitar falsos positivos).