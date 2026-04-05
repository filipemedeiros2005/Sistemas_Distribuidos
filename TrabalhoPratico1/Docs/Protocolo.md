# Especificação Técnica do Protocolo - Fase 1 (*OneHealth*)

Este documento define o protocolo de comunicação binária para o sistema de monitorização "One Health".
## 1. Estrutura do Pacote (16 Bytes)

Para otimizar o ciclo de leitura da CPU e evitar o custo de processamento de dados desalinhados, as mensagens seguem uma estrutura fixa de 16 bytes.

| Offset | Campo | Tamanho | Tipo | Descrição                                     |
| :--- | :--- | :---: | :--- |:----------------------------------------------|
| 0 | `MsgType` | 1 Byte | uint8 | Tipo de ação (1:HELO, 2:DATA, 3:ACK, 4:BYE, 5:NACK) |
| 1 | `DataType` | 1 Byte | uint8 | Tipo de sensor (1:PM10, 2:PPM, 3:Temp, 4:Hum) |
| 2 | `Reserved` | 2 Bytes | - | Padding para alinhamento a 4 bytes            |
| 4 | `SensorID` | 4 Bytes | uint32 | Identificador único do Sensor                 |
| 8 | `Value` | 4 Bytes | float32 | Valor da medição (formato IEEE 754)           |
| 12 | `CheckSum`| 4 Bytes | uint32 | Verificação de integridade ou uso futuro      | 

## 2. Fluxo de Comunicação

O protocolo é orientado à ligação (TCP) e segue um modelo de handshake simples para garantir a robustez necessária em sistemas distribuídos.

1. **Handshake (HELO)**: O Sensor liga-se e envia os seus dados de identificação.
2. **Confirmação (ACK)**: O Gateway confirma a prontidão para receber dados.
3. **Ciclo de Dados (DATA)**: Envio periódico das medições ambientais.
4. **Terminação (BYE)**: Encerramento da ligação para libertar recursos (threads/sockets).

## 3. Considerações Multi-plataforma

- **Line Endings**: Forçado o uso de CRLF via `.gitattributes` para compatibilidade entre sistemas operativos.
- **Byte Ordering**: O protocolo assume o uso de *Little-Endian* (padrão em arquiteturas modernas x86_64/x64 e ARM64).