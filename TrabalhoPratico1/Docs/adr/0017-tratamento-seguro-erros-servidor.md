# ADR 0017: Captura Ativa de Exceções nas Tarefas do Servidor (Task.Run)
**Status**: Accepted
**Data**: 19 de Abril de 2026

## Contexto
Na arquitetura do Servidor, cada ligação de um Gateway estava a ser lançada num `Task.Run` com *discard* (ex: `_ = Task.Run(...)`), de modo a processar os dados paralelamente e escalar o `AcceptTcpClientAsync`. O desafio é que o decorrer normal da rede está sempre sujeito a erros intermitentes (desconexões forçadas e *SocketExceptions*). O descartar da execução *Awaitable* no C# silencia eventuais quedas dos *Handlers*; se um Gateway falhasse abruptamente, o Servidor não registaria nada no *SystemEvents.log*.

## Decisão
Implementar um bloco `try-catch` envolvente dentro do método de processamento do Gateway (`HandleGatewayAsync` ou homólogo). Qualquer exceção que cause a interrupção da manipulação de sockets do lado do Servidor deve ser explicitamente capturada e registada com o máximo detalhe (ID do Gateway, estado da conexão, timestamp e StackTrace ou mensagem).

## Consequências
* **Positivas**: Evita que o programa principal continue a operar "às escuras" em caso de problemas recorrentes com componentes. Aumenta massivamente a visibilidade *debuggable* perante quebras da infraestrutura sem colapsar a *main thread* do processo Servidor.
* **Negativas**: Aumenta marginalmente a poluição do código com gestão puramente defensiva, forçando uma forte formatação nos logs para não poluir os registos por mero SPAM, se o Gateway se ligar e desligar freneticamente.
