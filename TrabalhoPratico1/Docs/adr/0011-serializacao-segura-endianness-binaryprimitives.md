# ADR 0011: Serialização Segura com BinaryPrimitives
**Status**: Accepted
**Data**: 18 de Abril de 2026

## Contexto
O protocolo binário de comunicação (ADR-0001) define o uso de Big-Endian para os pacotes `TelemetryPacket` e `VideoPacketHeader`. A submissão inicial utilizava `Marshal.AllocHGlobal`, resultando em serializações dependentes da arquitetura do processador hospedeiro (Little-Endian nativo no macOS ARM64 do ambiente de desenvolvimento), o que criava falhas de comunicação em ambientes cross-platform.

## Decisão
Substituir as operações nativas não geridas do pacote de interoperabilidade por `System.Buffers.Binary.BinaryPrimitives` com controlo rigoroso através de `Span<byte>`.

## Consequências
* **Positivas**: Conversão rigorosa para Big-Endian; previne *memory-leaks* na omissão de remoção não-gerida do Marshal; melhoria crítica de performance (Zero Allocation).
* **Negativas**: Maior verbosidade da serialização, exigindo cálculo estrito e manual perante os offsets dos arrays.
