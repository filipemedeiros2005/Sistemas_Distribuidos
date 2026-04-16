# ADR 006: Validação Perimetral e Base de Dados Central
**Estatuto**: Aceite

## Contexto
O protocolo permite (com valorização extra) que os dados sejam guardados em Base de Dados Relacional. Exige-se ainda controlo do início da comunicação (Handshake).

## Decisão
1. **Borda (Gateway)**: Executa Firewall Lógica. No `1:HELO`, o `SensorID` é verificado contra o ficheiro `sensors_config.csv` do próprio Gateway. Entidades desconhecidas recebem `5:NACK` e o socket é encerrado (evita DoS no Servidor).
2. **Núcleo (Servidor)**: A persistência ocorrerá em **PostgreSQL**, utilizando uma tabela otimizada para Séries Temporais (*Time-Series*).

## Consequências
* **Positivas**: Arquitetura profissional pronta para leitura via ferramentas como PowerBI.
* **Negativas**: Requer a instalação do SGBD PostgreSQL e o *driver* Npgsql no ecossistema do projeto.