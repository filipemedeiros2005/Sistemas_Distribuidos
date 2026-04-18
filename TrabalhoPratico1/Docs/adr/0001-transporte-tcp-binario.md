# ADR 001: Protocolo Binário de 20 Bytes e Transporte TCP
**Estatuto**: Aceite

## Contexto
A infraestrutura (Fase 1 e 2 do guião) exige comunicação fiável. Formatos de texto (JSON) são ineficientes para dispositivos IoT limitados. Adicionalmente, o momento exato da recolha de dados ambientais é crucial para a análise epidemiológica, independentemente da latência da rede, não podendo o Servidor Central confiar na sua própria data/hora de receção.

## Decisão
1. Adotar TCP para garantir entrega e ordenação (TCP stream).
2. Definir um protocolo binário estruturado de tamanho fixo (**20 bytes**).
3. Incluir o campo `Timestamp` (Unix Epoch, 4 bytes) na estrutura do pacote, gerado no momento da leitura pelo hardware do sensor.
4. Manter o alinhamento de memória usando 2 bytes de `Reserved` (Padding), garantindo que blocos de 4 bytes (SensorID, Timestamp, Value) iniciam em endereços otimizados para o CPU.

## Consequências
* **Positivas**: Alta eficiência de processamento atómico e preservação forense do momento da medição.
* **Negativas**: Aumento de 4 bytes por pacote em relação ao plano inicial (16 bytes), embora irrelevante face à MTU do TCP.