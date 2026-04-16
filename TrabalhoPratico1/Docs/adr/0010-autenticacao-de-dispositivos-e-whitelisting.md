# ADR 010: Autenticação de Dispositivos e Whitelisting na Borda
**Status**: Aceite
**Data**: 16 de abril de 2026

## Contexto
Num cenário urbano (Smart City), o Gateway está exposto a tentativas de ligação de dispositivos alheios ao projeto. Permitir o processamento de qualquer ID de sensor sobrecarregaria o Servidor Central e a Base de Dados com lixo informacional.

## Decisão
Implementar uma firewall lógica no Gateway baseada numa lista de permissões (Whitelist) carregada a partir de um ficheiro sensors_config.csv. O sistema rejeita imediatamente qualquer ligação que não se identifique corretamente via pacote 1:HELO com um ID presente na lista.

## Consequências
* **Positivas**: Proteção contra ataques de negação de serviço (DoS) básicos e garantia de integridade da base de dados.
* **Negativas**: Requer a atualização manual ou sincronizada do ficheiro CSV no Gateway sempre que um novo sensor físico é instalado na rua.

