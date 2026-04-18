# ADR 0012: Multiplexação Simples de Conexões do Gateway
**Status**: Accepted
**Data**: 18 de Abril de 2026

## Contexto
O Gateway intermédio processa milhares de pedidos provenientes de diversos sensores e executa diretamente envios (Forwarding) para o Servidor Central. Abrir uma conexão TCP individual ao Servidor Central por cada pacote recebido satura a tabela do sistema operativo com sockets locais retidos no estado transitório `TIME_WAIT`.

## Decisão
Implementar uma Ligação Persistente (Cano Unidirecional Multiplexado). O Gateway abre um único `TcpClient` para o Server (`IP: 5000`) durante a inicialização (`Main`). Todas as *tasks* concorrentes dos respetivos sensores escrevem nesse mesmo `NetworkStream`, isoladas por um bloqueio mutuamente exclusivo (`lock`).

## Consequências
* **Positivas**: Evita-se exaustão de portas (*Socket Exhaustion*), suprime *overhead* constante dos *handshakes* TCP de 3-vias e melhora a densidade de *throughput*.
* **Negativas**: O `NetworkStream` compartilhado transforma-se num ponto único de estrangulamento / falha. Um estrangulamento induz bloqueio transacional nas rotinas concorrentes.
