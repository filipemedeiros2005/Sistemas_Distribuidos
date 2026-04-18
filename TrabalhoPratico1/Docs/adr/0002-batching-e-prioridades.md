# ADR 002: Sincronização e Persistência Concorrente (Mutex)
**Estatuto**: Aceite

## Contexto
O guião (Fase 4) estipula o atendimento concorrente. O Servidor Central receberá pacotes provenientes de múltiplos Gateways em simultâneo. A gravação assíncrona concorrente numa base de dados ou ficheiro resultará em falhas de I/O ou perda das propriedades ACID.

## Decisão
Implementar mecanismos de exclusão mútua através de objetos `System.Threading.Mutex`. As *worker threads* do servidor adquirem o bloqueio de forma sequencial antes das instruções de escrita na Base de Dados e libertam o bloqueio num bloco `finally`.

## Consequências
* **Positivas**: Prevenção absoluta contra *race conditions* e integridade total dos dados.
* **Negativas**: Criação de um ligeiro afunilamento (*thread contention*) durante a escrita.