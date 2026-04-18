# Sistemas Distribuídos - Infraestrutura One Health

Este repositório contém o ecossistema de monitorização ambiental urbana desenvolvido para a unidade curricular de Sistemas Distribuídos. O sistema foca-se na recolha de indicadores de saúde pública através de uma arquitetura escalável e resiliente.

## Estrutura da Solução

O projeto está organizado em múltiplos módulos em C# para garantir a separação de responsabilidades:
- **OneHealth.Common**: Biblioteca de classes contendo as estruturas de dados do protocolo (20B TCP / 16B+ UDP) e lógica de verificação de integridade (CRC32).
- **OneHealth.Sensor**: Emulador de dispositivo IoT com lógica de Média Móvel Exponencial (EMA) para deteção de anomalias e buffering circular de vídeo em RAM.
- **OneHealth.Gateway**: Agregador inteligente que implementa firewall por whitelist, filas de prioridade para alertas e proxy de vídeo UDP.
- **OneHealth.Server**: Núcleo central de processamento e persistência. Inclui o atendimento concorrente multithreaded e interface de monitorização.

## Stack Tecnológica

- **Runtime**: .NET 9.0 (C#)
- **Persistência**: PostgreSQL com integração via Npgsql.
- **Interface**: Interface gráfica funcional desenvolvida em Avalonia UI (multiplataforma).
- **Protocolos**: Comunicação híbrida TCP (Controlo) e UDP (Multimédia).
- **Gestão de Concorrência**: Utilização de `Mutex` para proteção de recursos partilhados e `PriorityQueue` para QoS.

## Compatibilidade e Suporte

O projeto foi verificado e suporta as seguintes plataformas:
- **macOS**: Arquitetura ARM (Apple Silicon).
- **Windows**: Arquiteturas x64 e ARM (via virtualização/nativo).
- **IDEs Recomendados**: JetBrains Rider (preferencial para Mac ARM) e Visual Studio.

## Requisitos de Operação

- **Modo Telemetria**: Os sensores podem operar em regime de baixo consumo (bateria).
- **Modo Multimédia**: A transmissão de vídeo e o processamento de borda exigem que os dispositivos estejam ligados à corrente elétrica devido ao custo computacional do buffering de RAM e codificação UDP.

## Como Executar

1. Certifique-se de que a instância do PostgreSQL está ativa.
2. Compile a solução `OneHealth.sln`.
3. Execute as aplicações na seguinte ordem:
   - Servidor (Aguardando conexões de Gateways).
   - Gateway (Carregando a whitelist de sensores).
   - Sensor (Iniciando a simulação de medições).

---
*Desenvolvido no âmbito da Licenciatura em Engenharia Informática.*