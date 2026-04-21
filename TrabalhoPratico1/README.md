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

## Como Executar (Ambiente de Defesa)

Para facilitar a simulação em ambiente distribuído e a avaliação, o projeto contém ferramentas de automação na diretoria `src/scripts/`. Encontram-se disponíveis versões para **macOS/Linux** (`.sh`) e **Windows** (`.ps1`).

1. **Arranque e Simulação (`run_all`)**
   Abra a pasta `src/scripts` num terminal e execute o equivalente `run_all`. O script fará uma validação prévia de dependências (*.NET 9.0 SDK* e *PostgreSQL* operacionais). Posteriormente compilará as aplicações (Server, Dashboard, Gateway, Sensores automáticos) despoletando as respetivas janelas/terminais concorrentes.

2. **Injeção de Anomalias (`teste_manual`)**
   A partir de outro terminal, execute o `teste_manual`. Este inicializará o **Sensor 999** num modo assistido, permitindo que escreva comandos manuais interativos (ex: `Lum 800`) para ver, ao vivo, a resposta da infraestrutura de Edge Computig a um alerta (*3-Sigma*) gerado espontaneamente.

3. **Encerramento Automático (`kill_all`)**
   Ao finalizar os testes e visualizações, ou em caso de paragem forçada, execute sempre o script `kill_all`. Este destrói todos os processos nativos escondidos, liberta e limpa forçosamente portas encravadas usadas (TCP 5000-5005, UDP 6000-6001, Server 7000) e minimiza a confusão nos terminais abertos.

**Nota para Execução Tradicional:**
Caso prefira simulação cirúrgica isolada, certifique-se de que a instância do PostgreSQL está ativa e corra as aplicações sequencialmente pelo CLI (`dotnet run`) respeitando a ordem: Servidor → Dashboard → Gateway → Carga de Sensores.

---
*Desenvolvido no âmbito da Licenciatura em Engenharia Informática.*