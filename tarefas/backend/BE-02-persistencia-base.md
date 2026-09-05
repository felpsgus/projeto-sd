# BE-02 — Persistência base: EF Core, PostgreSQL, migrations e Testcontainers

| | |
|---|---|
| **Domínio** | Infraestrutura |
| **Serviço** | ambos (Identity e Tasks) |
| **Depende de** | [BE-01](BE-01-fundacao-solution.md) |
| **Bloqueia** | BE-04, BE-05 e tudo que persiste |
| **Regras cobertas** | habilita RN-USER-01, RN-TASK-01, RN-TASK-13, RN-TASK-14 |
| **Estimativa** | M |

## Objetivo

Os dois serviços conversam com o **mesmo** banco PostgreSQL, cada um dentro do **seu schema**: existe um `DbContext` por serviço, migrations versionadas por serviço, e os testes de integração sobem um banco em container.

## Escopo

### Inclui

- **Um banco `todolist`, dois schemas** (decisão **D-27**):

  | Schema | Serviço dono | Tabelas |
  |---|---|---|
  | `identity` | Identity | `users` (BE-04), `refresh_tokens` (BE-10), `login_attempts` (BE-12) |
  | `tasks` | Tasks | `tasks` (BE-05) |

- **Um `DbContext` por serviço**, cada um na `Infrastructure` do seu serviço, registrado por DI, com connection string vinda de configuração:
  - `IdentityDbContext` — `HasDefaultSchema("identity")`. Mapeia **apenas** as tabelas do schema `identity`.
  - `TasksDbContext` — `HasDefaultSchema("tasks")`. Mapeia **apenas** `tasks`.
- **Cada contexto mapeia somente o que o seu serviço possui.** `TasksDbContext` **NÃO DEVE** declarar `DbSet<User>` nem qualquer configuração para `identity.users`; `IdentityDbContext` **NÃO DEVE** conhecer `tasks.tasks`. O banco é compartilhado; o modelo, não.
- **Uma chave estrangeira cruzando os schemas:** `tasks.tasks.owner_id → identity.users(id)`, com **`ON DELETE CASCADE`**. Ela é declarada por SQL explícito em uma migration do **Tasks** — o `TasksDbContext` não mapeia a entidade do outro lado, então não há como o EF gerá-la a partir do modelo.
- `__EFMigrationsHistory` **por schema**, configurado com `MigrationsHistoryTable("__EFMigrationsHistory", "<schema>")`. Sem isso os dois serviços disputam a mesma tabela de histórico e um apaga a migration do outro.
- As convenções abaixo valem para **os dois** contextos, sem duplicação de decisão.
- Convenções globais de mapeamento:
  - Todo `DateTime` persistido em **UTC** (`timestamptz`).
  - `string` sem `MaxLength` explícito é proibido — cada propriedade declara o seu limite.
  - Configurações via `IEntityTypeConfiguration<T>`, uma classe por entidade, aplicadas por `ApplyConfigurationsFromAssembly`.
- Interface `IUnitOfWork` (ou `IAppDbContext`) em `Application`; implementação em `Infrastructure`. **A camada Application não referencia `DbContext` concreto.**
- Abstração de tempo: `TimeProvider` injetado (nada de `DateTime.UtcNow` espalhado) — necessário para testar expiração de token e "tarefa atrasada" de forma determinística.
- Suporte a **soft delete** no nível do contexto: interface `ISoftDeletable` (`DeletedAt`), `HasQueryFilter` global excluindo removidos, e um caminho explícito para ignorar o filtro (`IgnoreQueryFilters`) usado só pelo expurgo (BE-23).
- Preenchimento automático de `CreatedAt` / `UpdatedAt` no `SaveChangesAsync` para entidades auditáveis (base para RN-TASK-14).
- Migrations em `Infrastructure/Migrations` **de cada serviço**, com histórico próprio, aplicadas por comando explícito — **nunca** `EnsureCreated`, **nunca** `Migrate()` automático em produção.
- `docker-compose.yml` de desenvolvimento com uma instância PostgreSQL e o banco `todolist`. Os schemas são criados pelas migrations, não por script à parte.
- Base de testes de integração: `WebApplicationFactory` customizada + **Testcontainers** (Postgres), com `ICollectionFixture` compartilhando o container e limpeza de dados entre testes — **uma base por serviço**, em `TodoList.Identity.IntegrationTests` e `TodoList.Tasks.IntegrationTests`.

### Não inclui

- Entidades de negócio (`User` em BE-04, `TodoTask` em BE-05).
- Repositórios específicos por agregado — entram junto com as respectivas entidades.
- **Qualquer leitura de dado do outro serviço pelo banco.** O banco é compartilhado, mas continua valendo: se o Tasks precisa saber algo sobre um usuário, ele pergunta por gRPC (BE-25 a BE-28). `JOIN` entre schemas é proibido.

## Notas técnicas

- Banco alvo: **PostgreSQL** (decisão D-17). Provider: `Npgsql.EntityFrameworkCore.PostgreSQL` 10.x.
- **O schema é o que segura a fronteira entre os serviços.** Com banco único e schema único, nada impediria alguém de mapear `users` no `TasksDbContext` e trocar a chamada gRPC por um `JOIN` — e a separação entre os serviços viraria fachada. Com contextos limitados ao próprio schema, ler o usuário continua exigindo a chamada de rede. É uma barreira de convenção reforçada por teste (CA-14), não uma barreira física.
- **A FK cruzada é rede de segurança, não a regra.** Ela garante que o dono existe. Não diz se está ativo (RN-USER-04) nem devolve o nome de exibição — isso é `ValidateUser` ([BE-28](BE-28-validacao-dono-grpc.md)). Se a FK for o que barra uma criação, o resultado é uma violação de constraint (`23503`), que é falha técnica; o caminho correto é a rejeição de negócio, que vem antes.
- **Ordem de aplicação das migrations importa:** o schema `identity` e a tabela `users` precisam existir antes da migration do Tasks que cria a FK. Documentar a ordem no README (Identity primeiro).
- Todo acesso a I/O é `async`. `.Result`/`.Wait()` são proibidos.
- Nomes de tabela e coluna em `snake_case` (convenção do Postgres); mapear explicitamente.
- Connection string **NÃO DEVE** aparecer versionada. Em dev, `dotnet user-secrets`; no CI, variável de ambiente.

## Critérios de aceite

- [ ] **CA-01** — `dotnet ef migrations add <Nome>` e `dotnet ef database update` funcionam a partir da raiz para **cada** serviço, com os dois comandos e a **ordem** (Identity primeiro) documentados no README.
- [ ] **CA-02** — Existe ao menos uma migration inicial versionada no repositório **por serviço**, com `__EFMigrationsHistory` no schema do próprio serviço.
- [ ] **CA-02b** — Rodar as migrations dos dois serviços em sequência, num banco vazio, produz os dois schemas completos — e rodar de novo é no-op, sem erro.
- [ ] **CA-02c** — A FK `tasks.tasks.owner_id → identity.users(id)` existe, com `ON DELETE CASCADE`, verificado por consulta ao catálogo do Postgres.
- [ ] **CA-03** — Subir a API com o banco indisponível **não** derruba o processo silenciosamente: o health check reporta o estado degradado.
- [ ] **CA-04** — `GET /health` distingue *liveness* (app viva) de *readiness* (banco alcançável).
- [ ] **CA-05** — Um `DateTime` gravado e lido de volta permanece em UTC, sem deslocamento (teste de integração).
- [ ] **CA-06** — Uma entidade marcada como removida **não** aparece em consultas normais e **aparece** com `IgnoreQueryFilters()`.
- [ ] **CA-07** — Ao salvar uma entidade auditável nova, `CreatedAt` e `UpdatedAt` são preenchidos; ao alterá-la, apenas `UpdatedAt` muda.
- [ ] **CA-08** — O tempo usado pelo contexto vem de `TimeProvider` injetado: um teste que avança o tempo fake vê o novo valor sem `Thread.Sleep`.
- [ ] **CA-09** — Os testes de integração sobem o Postgres via Testcontainers e passam em máquina limpa, sem banco pré-instalado.
- [ ] **CA-10** — Testes de integração são independentes: rodar a suíte em ordem aleatória duas vezes seguidas produz o mesmo resultado.
- [ ] **CA-11** — Nenhuma connection string real está versionada (verificável por varredura do repositório).
- [ ] **CA-12** — A camada `Application` de **cada** serviço compila sem referência a `Microsoft.EntityFrameworkCore` (verificado por teste de arquitetura).
- [ ] **CA-13** — `TasksDbContext.Model` não contém nenhuma entidade mapeada para o schema `identity`, e `IdentityDbContext.Model` nenhuma para o schema `tasks` — verificado inspecionando o modelo do EF, não por revisão.
- [ ] **CA-14** — Nenhuma consulta do Tasks referencia `identity.*` e nenhuma do Identity referencia `tasks.*` (varredura por `JOIN`/nome de schema no CI, junto das varreduras de [BE-24](BE-24-observabilidade-ci.md)).
- [ ] **CA-15** — Inserir em `tasks.tasks` um `owner_id` que não existe em `identity.users` é **rejeitado pelo banco** (`23503`) — comprovando que a FK está ativa. Este é o comportamento de rede de segurança; o caminho normal rejeita antes, em [BE-28](BE-28-validacao-dono-grpc.md).
- [ ] **CA-16** — Apagar uma linha de `identity.users` remove em cascata as tarefas daquele dono, **inclusive as soft-deleted** — verificado com `IgnoreQueryFilters()`. É o que sustenta [BE-16](BE-16-exclusao-conta.md).

## Testes obrigatórios

- Integração: CRUD trivial de uma entidade de teste cobrindo CA-05, CA-06, CA-07.
- Unidade: comportamento do interceptor de auditoria com `TimeProvider` fake (CA-08).
- Arquitetura: CA-12.

## Decisões em aberto

- **D-17** — Banco alvo. Padrão adotado: PostgreSQL.
- **D-27** — Banco único com um schema por serviço e FK cruzada com `ON DELETE CASCADE`. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
