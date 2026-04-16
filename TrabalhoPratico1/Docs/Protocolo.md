# Especificação Técnica do Protocolo - One Health (v2.0)

Este documento define a comunicação binária otimizada para monitorização urbana, garantindo integridade temporal e priorização de alertas.

## 1. Estrutura do Pacote (20 Bytes - Alinhamento Atómico)

| Offset | Campo | Tamanho | Tipo | Descrição |
| :--- | :--- | :---: | :--- | :--- |
| 0 | `MsgType` | 1 Byte | uint8 | 1:HELO, 2:DATA, 3:ACK, 4:BYE, 5:NACK, 6:ALERT |
| 1 | `DataType` | 1 Byte | uint8 | 1:PM10, 2:PPM, 3:Temp, 4:Hum |
| 2 | `Reserved` | 2 Bytes | - | Padding para alinhamento (Reserved) |
| 4 | `SensorID` | 4 Bytes | uint32 | Identificador único do Sensor |
| 8 | `Timestamp`| 4 Bytes | uint32 | Tempo Unix (segundos desde 1970) |
| 12 | `Value` | 4 Bytes | float32 | Valor da medição (IEEE 754) |
| 16 | `CheckSum` | 4 Bytes | uint32 | CRC32 ou Soma de verificação dos 16B anteriores |

## 2. Ciclo de Vida da Ligação (Máquina de Estados)

O fluxo segue três fases distintas para garantir segurança e eficiência:

### Fase A: Handshake e Firewall de Borda
1. **HELO**: O Sensor identifica-se.
2. **Validação**: O Gateway consulta a Whitelist local (`sensors_config.csv`).
3. **Resposta**: `ACK` (Autorizado) ou `NACK` (Rejeitado + Fecho de Socket).

### Fase B: Fluxo de Dados e Priorização (QoS)
- **Caminho de Rotina**: Acumulação de 10 pacotes no Sensor -> Envio para Buffer Global do Gateway -> Persistência no Servidor.
- **Caminho de Emergência**: Deteção de anomalia (EMA + 3σ) -> Flush imediato do Buffer de Rotina + Alerta -> Prioridade máxima na fila do Gateway -> Alerta imediato no Servidor.

### Fase C: Encerramento e Resiliência
- **Saída Graciosa**: Envio de `BYE` pelo Sensor.
- **Saída por Exceção**: Detetada queda de socket (falha de energia/rede). O Gateway executa o flush dos dados residuais para o Servidor antes de libertar recursos.