# ADR 0014: Agregação Móvel (Averaging) em Edge Computing
**Status**: Proposed
**Data**: 18 de Abril de 2026

## Contexto
Durante o encaminhamento puro, os sensores libertam valores crús constantes. Para o ambiente global do "One Health", esse ruído cria poluição temporal nos históricos em Base de Dados.

## Decisão
Capacitar o Gateway com o princípio de Edge Computing (computação de borda pesada). O Gateway adquire e mantém individualmente uma `ConcurrentQueue<float>` dos últimos _10 pacotes_ de cada Dispositivo e substitui, no interior do _buffer TCP_, o valor transmitido individual original do byte array pela _Média Agregada_ recalcutalando assim implicitamente o "Checksum" de Integridade para salvarguardar as regras base.

## Consequências
* **Positivas**: Carga transferível altamente filtrada, minimiza processamentos estáticos pesados na Cloud, validável diretamente no PowerBI ou Base de Dados como Sinal Vital Consolidado.
* **Negativas**: Introduzimos Compressão de Dados com Margem de Perda (os registos diretos de milisegundos individuais desaparecem permanentemente da rota do servidor). O `Server` apenas regista agregações representativas.
