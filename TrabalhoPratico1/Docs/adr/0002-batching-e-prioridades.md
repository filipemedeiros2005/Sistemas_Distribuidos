# ADR 002: Sincronização e Persistência Concorrente
**Estatuto**: Aceite
**Data**: 2026-04-05

## Contexto
O Servidor Central receberá pacotes provenientes de múltiplos Gateways simultaneamente. A gravação assíncrona concorrente no mesmo ficheiro CSV resultará em corrupção do documento, escrita sobreposta de bytes ou exceções do sistema operativo (File In Use).

## Decisão
Implementar mecanismos de exclusão mútua através de objetos System.Threading.Mutex. As worker threads do servidor devem adquirir o bloqueio global antes de realizarem operações de I/O em disco e garantir a sua libertação em blocos de tratamento de exceções (finally).

## Consequências
* **Positivas**: Prevenção absoluta contra race conditions na camada de persistência física.
* **Negativas**: Causa constrangimento de performance (thread contention) em momentos de grande afluência de dados, criando um funil obrigatório na escrita.