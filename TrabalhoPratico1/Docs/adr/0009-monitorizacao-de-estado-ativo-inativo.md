# ADR 009: Monitorização de Estado Ativo/Inativo (Watchdog)
**Status**: Aceite
**Data**: 16 de abril de 2026

## Contexto
O Servidor precisa de distinguir entre "ausência de anomalias" e "sensor desligado". Como o sensor usa lotes para eficiência, a ligação TCP não é permanente.

## Decisão
Implementar um serviço de Watchdog no Gateway. Se um sensor não enviar dados ou frames dentro de uma janela de 5 minutos, o Gateway gera proativamente um pacote 7:STATUS para informar o Servidor da inatividade do nó.

## Consequências
* **Positivas**: Garante a veracidade dos dashboards no PowerBI/UX, evitando a exibição de dados obsoletos.
* **Negativas**: Aumenta a complexidade de implementação no Gateway devido à gestão de temporizadores por sensor.

