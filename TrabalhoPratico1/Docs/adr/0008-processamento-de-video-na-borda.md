# ADR 008: Processamento de Vídeo na Borda (Edge Storage/Proxy)
**Status**: Aceite
**Data**: 16 de abril de 2026

## Contexto
Transmitir vídeo 24/7 de todos os sensores para o Servidor Central saturaria a largura de banda da rede core e o processamento do Servidor.

## Decisão
O Gateway atuará como um Proxy e Gravador de Borda. O vídeo só é transmitido ao Servidor sob pedido (Live) ou após a consolidação de um incidente (Gravação ALERT). O Servidor armazena apenas metadados e referências (IDs) para os ficheiros.

## Consequências
* **Positivas**: Redução drástica do tráfego de rede.
* **Negativas**: Exige que o Gateway tenha armazenamento local (SSD/HDD).

