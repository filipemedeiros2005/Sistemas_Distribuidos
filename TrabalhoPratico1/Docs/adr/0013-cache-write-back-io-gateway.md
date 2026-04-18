# ADR 0013: Modelagem Híbrida Write-Back de I/O no Gateway
**Status**: Accepted
**Data**: 18 de Abril de 2026

## Contexto
O Gateway deve assegurar uma *whitelist* e estados atualizados de todos os Sensores via ficheiro de configuração (`sensors_config.csv`). Guardar o registo e o valor *timestamp* no ficheiro sincronizadamente após cada comunicação TCP é avassalador operacionais I/O, impossibilitando a escala rápida de um Gateway.

## Decisão
Implementar um Cache em Memória com atualizações imediatas no `Dictionary` (`State=Write-Back`). Em paralelo, um daemon/task isolado (`SyncConfigToDiskAsync`) processa uma descarga global para o `.csv` a cada intervalo de **30 Segundos**.

## Consequências
* **Positivas**: Reduz 99% da pressão latente I/O do Edge, tornando as respostas e validações (`isValid/isAllowed`) quase instantâneas.
* **Negativas**: Em caso de falha bruta de energia na Borda do Gateway, o sistema perderá a precisão da sua própria integridade nas *timestamps* dos últimos _< 30s_, devendo reconstituí-las no novo *boot*.
