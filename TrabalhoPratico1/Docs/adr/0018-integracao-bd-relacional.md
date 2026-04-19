# ADR 0018: Integração de Base de Dados Relacional para Armazenamento Persistente
**Status**: Proposed
**Data**: 19 de Abril de 2026

## Contexto
O protocolo da unidade curricular estipula na Fase 3 que a funcionalidade extra, altamente valorizada pela documentação, envolve a capacidade do Servidor descarregar os dados agregados dos ficheiros (que servem como base intermédia ou pré-processamento) numa `Base de Dados Relacional` de longo prazo. A presente solução depende apenas de repositório em texto limpo / logs, perdidos ou fragmentados nas limitações do disco.

## Decisão
Migrar ou duplicar a lógica de persitência final para uma arquitetura com uma BD Relacional (previsto o uso de PostgreSQL através de ORM como o *Entity Framework Core* ou *Dapper*). O Servidor (após receber as strings/bytes do Gateway e confirmar o *CheckSum*) atualizará de forma síncrona/assíncrona os controlos ou tabelas normalizadas. As tabelas incluirão `Sensores`, `Gates`, `Medicoes`, entre outras entidades, correspondendo aos DataTypes e TimeStamps provenientes do paradigma "One Health".

## Consequências
* **Positivas**: Possibilita queries complexas e cruzamentos para relatórios demográficos, essenciais no sistema One Health urbano (ex: "Qual a correlação entre poluição e temperatura na Zona Norte, durante o mês passado?"). Aumenta largamente as capacidades transacionais (ACID).
* **Negativas**: Introduz a gestão de estado complexa (*Migrations*, *Connection Strings*, Infraestrutura da base de dados localmente ligada mediante Docker/SGBD externo), requerendo conhecimento profundo das dependências do `.NET`. O `Bulk Insert` poderá necessitar de otimização extra nas concorrências, de forma a não afunilar.
