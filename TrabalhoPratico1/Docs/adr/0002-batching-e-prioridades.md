# ADR 002: Gestão de Concorrência e Persistência

**ID:** 0002

**Estatuto:** Aceite

**Contexto:** Na Fase 4, o Gateway e o Servidor processarão múltiplos sensores simultaneamente. Se duas threads tentarem escrever no ficheiro de log ou ler o sensors_config.csv ao mesmo tempo, ocorrerá uma exceção de I/O ou corrupção de dados.

**Decisão:** Utilização de Mutexes (`System.Threading.Mutex`) para garantir exclusão mútua no acesso aos ficheiros físicos.

**Justificação:** Ao contrário de um simples lock (que funciona apenas dentro do mesmo processo), o Mutex é um objeto do sistema operativo que permite maior robustez, sendo a solução clássica em Sistemas Distribuídos para proteger recursos partilhados.

**Consequências:** Pequeno custo de *performance* devido à espera de threads, garantindo em troca a integridade total dos dados.