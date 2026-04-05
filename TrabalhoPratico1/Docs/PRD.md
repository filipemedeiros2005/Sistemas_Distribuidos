# PRD: Sistema de Monitorização Urbana "One Health" 

## 1. Visão Geral
O sistema "One Health" é uma infraestrutura distribuída em três camadas para monitorização ambiental urbana. O foco reside na integridade dos dados, segurança na borda (edge) e eficiência na transmissão de telemetria sensível.

## 2. Entidades do Sistema
- **Sensor (Nó de Recolha):** Simula dispositivos IoT com lógica estatística local.
- **Gateway (Concentrador de Borda):** Atua como filtro de segurança e agregador de tráfego.
- **Servidor (Núcleo Central):** Responsável pela persistência final e análise consolidada.

## 3. Requisitos Funcionais (RF)

### 3.1. Comunicação e Protocolo
- **RF01 - Ciclo de Vida da Ligação:** O sistema deve implementar estritamente o fluxo: HELO (Sensor) $\rightarrow$ ACK/NACK (Gateway) $\rightarrow$ DATA (Sensor) $\rightarrow$ BYE (Sensor).
- **RF02 - Protocolo Binário:** Todas as mensagens devem seguir a estrutura fixa de 16 bytes para garantir alinhamento de memória e baixo overhead.

### 3.2. Inteligência e Lógica do Sensor
- **RF03 - Processamento Estatístico Local:** O Sensor deve calcular médias e desvios-padrão em tempo real para os parâmetros: PM10, PPM, Temperatura e Humidade.
- **RF04 - Envio por duas vias (Batch vs. Priority):**
  - **Dados de Rotina:** Devem ser acumulados num *buffer local* e enviados em lote (batch) para otimizar energia (conjuntos de 10 medições).
  - **Alertas Críticos:** Se uma medição ultrapassar a margem de erro do desvio-padrão, o sensor deve realizar um envio imediato (salto de fila), sinalizando uma anomalia ambiental.

### 3.3. Segurança e Gestão de Borda
- **RF05 - Firewall de Borda (Gateway):** O Gateway deve validar cada SensorID contra o ficheiro sensors_config.csv. Ligações não autorizadas são terminadas no estado de Handshake.
- **RF06 - Atendimento Concorrente:** O Gateway e o Servidor devem gerir múltiplas sockets simultâneas através de multithreading.

## 4. Requisitos Não Funcionais (RNF)
- **RNF01 - Integridade Concorrente:** O acesso aos ficheiros de log e de configuração deve ser mediado por Mutexes para evitar condições de corrida (race conditions).
- **RNF02 - Fiabilidade de Transporte:** Uso obrigatório de TCP para garantir que os dados de saúde não sofrem perdas ou desordenação.
- **RNF03 - Eficiência de Memória:** O Gateway deve utilizar uma fila de mensagens (FIFO) para processar os pacotes recebidos sem bloquear as threads de escuta.