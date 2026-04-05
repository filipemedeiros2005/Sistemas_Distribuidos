# ADR 001: Seleção de Protocolo e Camada de Transporte

**ID:** 0001

**Estatuto:** Aceite

**Contexto:** O sistema necessita de uma comunicação entre Sensores, Gateway e Servidor. Os dados (saúde pública) são sensíveis à perda de pacotes e à corrupção de mensagens. A eficiência em dispositivos limitados é prioritária.

**Decisão:**
- Utilizar TCP (Sockets) em vez de UDP para garantir a entrega e ordenação.
- Implementar um Protocolo Binário de 16 Bytes com alinhamento forçado.
- Usar campos de *checksum* para verificar a integridade dos dados.
- Adicionar a possibilidade de retornar *NACK* no campo de *MsgType*.

**Justificação:**
- O TCP elimina a necessidade de lógica complexa de retransmissão na camada de aplicação.
- Os 16 bytes ($2^4$) permitem leituras atómicas em arquiteturas de 64 bits (como ARM64/x64), reduzindo ciclos de CPU no processamento de milhares de pacotes.
- O *NACK* permite diferenciar a possibilidade do gateway rejeitar o sensor pela Firewall da possibilidade de não existir conexão de rede.

**Consequências:**
- Necessidade de gerir o estado da conexão (Handshake).
- Ligeiro aumento de latência inicial devido ao three-way handshake do TCP, compensado pela fiabilidade.