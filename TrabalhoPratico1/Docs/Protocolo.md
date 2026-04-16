# Especificação Técnica do Protocolo - One Health (v3.0)

Este documento define a comunicação híbrida otimizada para monitorização urbana, garantindo integridade temporal, priorização de alertas e suporte a fluxos multimédia.

## 1. Canais de Comunicação
A arquitetura utiliza dois canais lógicos distintos:
- **Canal de Controlo e Telemetria (TCP - Porta 5001/5000)**: Comunicação fiável para dados sensíveis, alertas e comandos.
- **Canal de Multimédia (UDP - Porta 6000)**: Transmissão de baixa latência para frames de vídeo em tempo real.

## 2. Estrutura do Pacote de Telemetria (TCP - 20 Bytes)

| Offset | Campo | Tamanho | Tipo | Descrição |
| :--- | :--- | :---: | :--- | :--- |
| 0 | `MsgType` | 1 Byte | uint8 | 1:HELO, 2:DATA, 3:ACK, 4:BYE, 5:NACK, 6:ALERT, 7:STATUS |
| 1 | `DataType` | 1 Byte | uint8 | 1:PM10, 2:PPM, 3:Temp, 4:Hum |
| 2 | `Reserved` | 2 Bytes | - | Padding para alinhamento (Reserved) |
| 4 | `SensorID` | 4 Bytes | uint32 | Identificador único do Sensor |
| 8 | `Timestamp`| 4 Bytes | uint32 | Tempo Unix (segundos desde 1970) |
| 12 | `Value` | 4 Bytes | float32 | Valor da medição (IEEE 754) |
| 16 | `CheckSum` | 4 Bytes | uint32 | CRC32 dos 16 bytes anteriores |

## 3. Estrutura do Pacote de Vídeo (UDP - Cabeçalho 16 Bytes + Payload)

| Offset | Campo | Tamanho | Tipo | Descrição |
| :--- | :--- | :---: | :--- | :--- |
| 0 | `SensorID` | 4 Bytes | uint32 | ID do Sensor de origem |
| 4 | `Timestamp`| 4 Bytes | uint32 | Momento da captura (Unix Epoch) |
| 8 | `SequenceNum`| 4 Bytes | uint32 | Contador de fragmentos para reordenação |
| 12 | `DataSize` | 4 Bytes | uint32 | Tamanho útil do Payload (bytes) |
| 16 | `Payload` | Variável | bytes | Fragmento binário do frame de vídeo |

## 4. Ciclo de Vida e Fluxos de Eventos

- **Fase A: Handshake e Segurança**: O Sensor envia `1:HELO`. O Gateway valida o SensorID via `sensors_config.csv` e responde `3:ACK` ou `5:NACK`.
- **Fase B: Telemetria e Alerta**: Dados de rotina são enviados em lotes de 10. Em caso de anomalia (3σ), o Sensor envia `6:ALERT` (TCP) e inicia o flush de vídeo (UDP): 30s de buffer em RAM + 120s de captura nova.
- **Fase C: Gestão de Estado (Watchdog)**: O Gateway monitoriza a atividade. Na ausência de dados por 5 minutos, o Gateway notifica o Servidor via `7:STATUS` (Inativo).
- **Fase D: Vídeo On-Demand**: O utilizador solicita acesso via Servidor. O comando viaja via TCP e o Sensor inicia a transmissão UDP imediata.
