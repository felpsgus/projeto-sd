# TodoList — Backend

Solution .NET 10 com **dois microsserviços independentes** — **Identity Service** e **Tasks Service** —
que se comunicam por gRPC (BE-25/BE-26). O Identity já expõe um servidor gRPC real (`ValidateUser`,
`ValidateToken` stub); o Tasks ainda não chama nada (cliente/wiring ficam para BE-27/BE-28). Cada serviço
sobe de forma independente, responde a health checks de liveness/readiness e expõe OpenAPI em
desenvolvimento. Os dois persistem no **mesmo** banco Postgres, cada um no seu schema (BE-02, D-27).

## Estrutura

```
TodoList.sln
├── docker-compose.yml                     ← Postgres de desenvolvimento (BE-02)
├── contracts/
│   └── identity/v1/identity.proto        ← contrato gRPC compartilhado (BE-25) — único .proto do repo
├── src/
│   ├── Shared/
│   │   └── TodoList.SharedKernel          ← código compartilhado entre os dois serviços (Result/Error em BE-03)
│   ├── Identity/                          ← Identity Service (servidor gRPC, BE-26)
│   │   ├── TodoList.Identity.Domain       ← User, Email (Users/, BE-04); IAuditable/ISoftDeletable (Common/, BE-02)
│   │   ├── TodoList.Identity.Application  ← IUserLookup, IUserRepository (Users/, BE-04); IUnitOfWork (Persistence/, BE-02)
│   │   ├── TodoList.Identity.Infrastructure ← UserRepository, PersistedUserLookup, InMemoryUserLookup, DemoUserSeeder (Users/, BE-04/BE-26); IdentityDbContext + migrations (Persistence/, BE-02)
│   │   └── TodoList.Identity.Api          ← IdentityGrpcService, gera o lado Server do .proto
│   └── Tasks/                             ← Tasks Service (cliente gRPC, a partir de BE-27)
│       ├── TodoList.Tasks.Domain          ← IAuditable/ISoftDeletable (Common/, BE-02)
│       ├── TodoList.Tasks.Application     ← IUnitOfWork (Persistence/, BE-02)
│       ├── TodoList.Tasks.Infrastructure  ← gera o lado Client do .proto (BE-27); TasksDbContext + migrations (Persistence/, BE-02)
│       └── TodoList.Tasks.Api
└── tests/
    ├── TodoList.Identity.UnitTests
    ├── TodoList.Identity.IntegrationTests ← inclui Persistence/ (SQLite sem Docker + Testcontainers/Postgres, BE-02)
    ├── TodoList.Tasks.UnitTests
    └── TodoList.Tasks.IntegrationTests    ← inclui Persistence/ (SQLite sem Docker + Testcontainers/Postgres, BE-02)
```

Dentro de cada serviço, as dependências apontam sempre para dentro: `Api → Infrastructure → Application → Domain → SharedKernel`.
Os dois serviços **não têm nenhuma referência de projeto entre si** — a única coisa compartilhada em código é o
`TodoList.SharedKernel` e, futuramente, o contrato `contracts/identity/v1/identity.proto`.

## Pré-requisitos

- **.NET SDK 10.0.202** ou compatível (fixado em [`global.json`](global.json)). Verifique com:

  ```powershell
  dotnet --version
  ```

## Como restaurar e compilar

Na raiz do repositório:

```powershell
dotnet restore
dotnet build
```

O build deve concluir **sem warnings** (analyzers do Roslyn tratados como erro).

## Como subir os dois serviços

Cada serviço tem sua própria porta de desenvolvimento (definida em `appsettings.Development.json`,
nunca hardcoded no código):

| Serviço | Endpoint | Porta (dev) | Protocolo |
|---|---|---|---|
| Identity | HTTP/REST | `5080` | HTTP/1.1 |
| Identity | gRPC (`IdentityService.ValidateUser`, `ValidateToken`) | `5081` | HTTP/2 (h2c) |
| Tasks | HTTP/REST | `5100` | HTTP/1.1 |

> **Antes do primeiro `dotnet run` do Identity:** configure `Jwt:SigningKey` via `dotnet user-secrets`
> (BE-08) — sem ela a inicialização falha de propósito (CA-02). Veja a seção
> ["Access token JWT: `Jwt:SigningKey` nunca versionada"](#access-token-jwt-jwtsigningkey-nunca-versionada-be-08)
> mais abaixo.

Em dois terminais separados, a partir da raiz do repositório:

```powershell
dotnet run --project src/Identity/TodoList.Identity.Api
```

```powershell
dotnet run --project src/Tasks/TodoList.Tasks.Api
```

Com os dois no ar, confirme o liveness check de cada um:

```powershell
curl http://localhost:5080/health
curl http://localhost:5100/health
```

Ambos devem responder `200 OK` com `{ "status": "healthy" }`.

> Para o roteiro completo — Postgres, migrations na ordem certa, criação de tarefa passando pelo gRPC e os
> três desfechos possíveis — veja ["Rodando os dois serviços"](#rodando-os-dois-serviços-roteiro-de-verificação-da-comunicação-grpc-be-31)
> mais abaixo. Os comandos acima sobem os serviços, mas sem banco não dá para criar tarefa.

Em ambiente de desenvolvimento, cada serviço também expõe a documentação OpenAPI/Scalar:

- Identity: <http://localhost:5080/scalar/v1> (JSON cru em `/openapi/v1.json`)
- Tasks: <http://localhost:5100/scalar/v1> (JSON cru em `/openapi/v1.json`)

## gRPC do Identity Service (BE-25/BE-26)

O contrato compartilhado vive em [`contracts/identity/v1/identity.proto`](contracts/identity/v1/identity.proto) —
único `.proto` do repositório, referenciado por caminho relativo pelos dois serviços (decisão D-29):
`TodoList.Identity.Api` gera o lado `Server`, `TodoList.Tasks.Infrastructure` gera o lado `Client`
(cujo wiring/uso real fica para BE-27/BE-28). O serviço `IdentityService` expõe dois RPCs:

- **`ValidateUser`** — consulta o usuário pelo id; nunca lança nem devolve erro gRPC para id
  malformado ou usuário inexistente (resposta negativa, status `OK`).
- **`ValidateToken`** (BE-34) — valida assinatura, issuer, audience e expiração de um access token
  JWT, reaproveitando os mesmos `TokenValidationParameters` de BE-08 (D-31). É o único caminho para
  validar um token fora do Identity — a chave de assinatura HS256 não sai daqui. Não consulta o
  store de usuários: não checa `IsActive` (ver nota técnica de BE-34 — decisão deliberada, não
  omissão). Nunca lança nem devolve erro gRPC para entrada malformada — sempre `OK` com
  `valid=false`.
- **`Login`** (BE-33, recorte de BE-09 — D-36) — troca e-mail e senha por um access token. Exige
  `UserStore:Provider=Persisted` (ver seção seguinte). Nunca revela, por resposta ou por tempo, qual
  das causas de falha ocorreu (e-mail inexistente, senha errada, usuário inativo) — sempre
  `succeeded=false` sem detalhe adicional.

### Store de usuários: em memória ou persistido (BE-04)

`ValidateUser` lê de uma de duas implementações de `IUserLookup`, escolhida por
`UserStore:Provider` em configuração — trocar de store é mudar configuração, não código:

- **`InMemory`** (padrão) — seed fixo em memória (`InMemoryUserLookup`). Roda no processo real — não
  é mock de teste — e emite um **log de aviso** na inicialização deixando explícito que o store
  persistido não está em uso. Exatamente dois usuários, com ids fixos:

  | Usuário | Id | Estado | Nome de exibição |
  |---|---|---|---|
  | Ativo | `10000000-0000-0000-0000-000000000001` | `active=true` | Ada Lovelace |
  | Inativo | `10000000-0000-0000-0000-000000000002` | `active=false` | Charles Babbage |

- **`Persisted`** — `PersistedUserLookup`, sobre `IUserRepository`/`IdentityDbContext` (BE-04). Cada
  chamada é uma consulta nova ao banco — sem cache — então desativar um usuário muda a resposta de
  `active=true` para `active=false` **sem reiniciar o serviço** (CA-13 de BE-26). Como
  `PersistedUserLookup` depende do `DbContext` (`Scoped`), `IUserLookup` também é registrado como
  `Scoped` no `Program.cs` (nunca `Singleton` — criaria uma dependência cativa sobre um `DbContext`
  descartado); `InMemoryUserLookup` continua efetivamente único no processo porque ele mesmo é
  registrado como `Singleton` por baixo do wrapper `Scoped`.

### Seed de usuários de demonstração no banco (`UserStore:SeedDemoUsers`)

Independente do `Provider` acima, `UserStore:SeedDemoUsers=true` liga o `DemoUserSeeder`, que
popula `identity.users` com os **mesmos dois ids fixos** da tabela acima — necessário porque a FK
cruzada `tasks.tasks.owner_id → identity.users(id)` (nota mais abaixo) exige que esses usuários
existam de verdade no banco, não só no seed em memória. **Desligado por padrão** (nunca ligue em
produção). Idempotente — rodar de novo não duplica nem falha — e emite **log de aviso** quando roda.
Nunca chama `EnsureCreated()`/`Migrate()`: pressupõe que a migration do BE-04 (`AddUsersTable`) já foi
aplicada (seção seguinte).

**Senha real (BE-33).** Com `SeedDemoUsers=true`, `UserStore:DemoUserPassword` passa a ser
**obrigatória** — sem ela, a inicialização **falha** (mesmo padrão de `Jwt:SigningKey`, BE-08). É a
senha em texto puro dos dois usuários de demonstração, lida só na inicialização e nunca versionada
(user-secrets/variável de ambiente):

```powershell
dotnet user-secrets set "UserStore:DemoUserPassword" "<senha de demonstração>" --project src/Identity/TodoList.Identity.Api
# ou, sem user-secrets:
$env:UserStore__DemoUserPassword = "<senha de demonstração>"
```

O seed sincroniza a senha a cada execução: usuário novo recebe `Hash(senha)`; usuário que já existe
só tem o hash regravado se a senha atual não bater mais com `DemoUserPassword` (idempotente) — é
assim que os dois usuários herdados de um ambiente antigo (ou uma troca de `DemoUserPassword` entre
implantações) ganham uma senha utilizável, sem duplicar nem falhar. `./scripts/demo-local.ps1` gera
essa senha aleatoriamente a cada execução e a imprime no console ao final.

**Login (BE-33) exige `UserStore:Provider=Persisted`.** Com `Provider=InMemory`, não existe
senha/hash associado aos usuários semeados em memória — `Login` sempre responde `succeeded=false`
para qualquer entrada, e o Identity emite um **aviso** uma única vez, na inicialização, avisando que
o modo não suporta autenticação (não a cada chamada, e não falha o start).

## Persistência: Postgres, EF Core e migrations (BE-02)

Os dois serviços conversam com o **mesmo** banco Postgres (`todolist`), cada um só no **seu** schema
(decisão D-27): `identity` (Identity Service) e `tasks` (Tasks Service). Nenhum dos dois mapeia nada do
schema do outro — `IdentityDbContext` não conhece `tasks.tasks`, `TasksDbContext` não conhece
`identity.users` — verificado por teste (CA-13 de BE-02), não por revisão.

### Subir o Postgres de desenvolvimento

Na raiz do repositório, com Docker disponível:

```powershell
docker compose up -d
```

Isso sobe uma única instância Postgres (`postgres:17-alpine`), banco `todolist`, porta `5432`, com a
credencial de desenvolvimento local `postgres`/`postgres` (comentada como tal em
[`docker-compose.yml`](docker-compose.yml) — não é um segredo real). **Os schemas `identity` e `tasks` não
vêm de nenhum script de inicialização**: eles são criados pelas migrations de cada serviço, na seção
seguinte.

### Connection string: nunca versionada (CA-11)

Nenhum `appsettings*.json` do repositório declara `ConnectionStrings` — isso é verificado por teste
(`ConnectionStringSecurityTests`, CA-11 de BE-02). Configure localmente com `dotnet user-secrets` (a partir
da raiz do repositório):

```powershell
dotnet user-secrets set "ConnectionStrings:IdentityDb" "Host=localhost;Port=5432;Database=todolist;Username=postgres;Password=postgres" --project src/Identity/TodoList.Identity.Api
dotnet user-secrets set "ConnectionStrings:TasksDb" "Host=localhost;Port=5432;Database=todolist;Username=postgres;Password=postgres" --project src/Tasks/TodoList.Tasks.Api
```

No CI (ou qualquer ambiente sem user-secrets), a mesma chave vem de variável de ambiente, com `:`
substituído por `__` (convenção do `Microsoft.Extensions.Configuration`):

```powershell
$env:ConnectionStrings__IdentityDb = "Host=...;Database=todolist;Username=...;Password=..."
$env:ConnectionStrings__TasksDb = "Host=...;Database=todolist;Username=...;Password=..."
```

> **Atenção — `dotnet ef` não enxerga o `user-secrets`.** Como cada serviço tem um
> `IDesignTimeDbContextFactory`, o `dotnet ef` usa essa fábrica e **ignora** a configuração do
> `--startup-project`. Por isso as fábricas leem a **variável de ambiente**
> `ConnectionStrings__IdentityDb` / `ConnectionStrings__TasksDb`, caindo na credencial local do
> `docker-compose.yml` quando ela não existe. Ou seja: `user-secrets` vale para **rodar** o serviço;
> para um `database update` apontar para um banco que não seja o local, exporte a variável de
> ambiente antes do comando.

Sem a connection string configurada, cada serviço ainda **sobe normalmente** — só o readiness check
(`/health/ready`) fica degradado (CA-03) e qualquer operação real de banco falha ao ser tentada. Nada
resolve o `DbContext` de forma antecipada no startup.

### Access token JWT: `Jwt:SigningKey` nunca versionada (BE-08)

Diferente da connection string, `Jwt:SigningKey` **não tem esse alívio**: sem ela o Identity **falha
ao iniciar** (CA-02) — é a chave HS256 que assina e valida o access token (D-31), e ela nunca sai do
Identity. `appsettings.json` só declara `Jwt:Issuer` e `Jwt:Audience`; a chave é sempre configuração
externa, do mesmo jeito que a connection string:

```powershell
dotnet user-secrets set "Jwt:SigningKey" "<chave gerada abaixo>" --project src/Identity/TodoList.Identity.Api
```

Gere uma chave aleatória de 48 bytes (bem acima do mínimo de 32 exigido por CA-03) com:

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
```

No CI (ou qualquer ambiente sem `user-secrets`), a mesma chave vem de variável de ambiente:

```powershell
$env:Jwt__SigningKey = "<chave gerada acima>"
```

`./scripts/demo-local.ps1` já faz isso sozinho — gera uma chave aleatória a cada execução e a passa por
`Jwt__SigningKey` só para o processo do Identity que ele sobe, nunca gravada em disco. O **Tasks
Service não recebe nem referencia** `Jwt:SigningKey` (D-31): uma varredura de arquitetura
(`ArchitectureTests.CodigoDoTasks_NaoReferenciaJwtSigningKey`, CA-14 de BE-08) falha o build se
`Jwt:`, `Jwt__` ou `SigningKey` aparecerem em qualquer código ou `appsettings*.json` de `src/Tasks` ou
em `deploy/tasks.env.example`. O API Gateway (T2, ainda não implementado) entra nessa mesma varredura
quando existir (BE-36).

> **Implantação (VM/nuvem):** o processo do Identity em produção também precisa de `Jwt__SigningKey`
> como variável de ambiente do serviço (systemd/unit file) — fora do escopo desta task (BE-37), mas é
> o mesmo mecanismo acima, só que a chave é gerada uma vez e guardada no secret manager do ambiente,
> não redistribuída a cada deploy.

### Migrations: `dotnet ef`, uma base por serviço

Cada serviço tem sua própria migration inicial, versionada em `Infrastructure/Migrations`, com sua própria
tabela de histórico (`__EFMigrationsHistory`) **dentro do próprio schema** — os dois serviços nunca disputam
a mesma tabela de histórico (`MigrationsHistoryTable("__EFMigrationsHistory", "<schema>")` em cada
`DbContext`). O ambiente já tem o `dotnet-ef` disponível como ferramenta global; se não tiver:

```powershell
dotnet tool install --global dotnet-ef
```

**Ordem obrigatória: Identity primeiro.** A FK cruzada que o Tasks declara (ver nota abaixo) referencia
`identity.users`, então o schema `identity` precisa existir antes da migration do Tasks que a cria — inverter
a ordem contra um banco vazio falha com `ERRO: esquema "identity" não existe` (SQLSTATE `3F000`).

```powershell
# 1) Identity — sempre primeiro
dotnet ef database update `
  --project src/Identity/TodoList.Identity.Infrastructure `
  --startup-project src/Identity/TodoList.Identity.Api `
  --context IdentityDbContext

# 2) Tasks — só depois do Identity
dotnet ef database update `
  --project src/Tasks/TodoList.Tasks.Infrastructure `
  --startup-project src/Tasks/TodoList.Tasks.Api `
  --context TasksDbContext
```

Rodar os dois comandos de novo num banco já migrado é *no-op*, sem erro (CA-02b) — os scripts gerados são
idempotentes (`IF NOT EXISTS`/`CREATE SCHEMA IF NOT EXISTS`, visível com `dotnet ef migrations script
--idempotent`).

Para criar uma nova migration (ex.: `AddUsersTable`, quando BE-04 adicionou `User`), o padrão é o mesmo
par `--project`/`--startup-project`, com `migrations add` no lugar de `database update`:

```powershell
dotnet ef migrations add NomeDaMigration `
  --project src/Identity/TodoList.Identity.Infrastructure `
  --startup-project src/Identity/TodoList.Identity.Api `
  --context IdentityDbContext `
  --output-dir Migrations
```

(troque `Identity` por `Tasks` e o `--context` para gerar uma migration do Tasks). **Nunca** `EnsureCreated()`
nem `Migrate()` automático na inicialização da aplicação — só os comandos explícitos acima.

### Convenções de mapeamento (valem para os dois contextos)

- Todo `DateTime` é persistido em UTC (`timestamptz` no Postgres) — conversão automática em
  `ConfigureConventions`, sem depender de `DateTime.UtcNow` espalhado pelo código (o tempo vem de
  `TimeProvider`, injetado e registrado em `Program.cs`).
- `string` sem `MaxLength` explícito é proibido — a construção do modelo falha citando a propriedade, em vez
  de truncar silenciosamente em runtime.
- `snake_case` automático em tabela/coluna (`EFCore.NamingConventions`); configuração fina continua por
  `IEntityTypeConfiguration<T>`, aplicada via `ApplyConfigurationsFromAssembly`.
- Soft delete: uma entidade que implementa `ISoftDeletable` nunca é apagada fisicamente — o interceptor
  converte `Remove()` num `UPDATE` que só marca `DeletedAt`; um `HasQueryFilter` global some com ela das
  consultas normais, e `IgnoreQueryFilters()` é o caminho explícito para revê-la (usado pelo expurgo, BE-23).
- Auditoria: `CreatedAt`/`UpdatedAt` de toda entidade `IAuditable` são preenchidos automaticamente por um
  `SaveChangesInterceptor` — nunca setados à mão pelo código de aplicação.

### FK cruzada `tasks.tasks.owner_id → identity.users(id)` (BE-02 CA-02c/CA-15/CA-16)

Existe desde a migration `AddOwnerForeignKeyToIdentityUsers` (Tasks), com **`ON DELETE CASCADE`**, declarada
por **SQL explícito** (`migrationBuilder.Sql(...)`, `Up`/`Down`) — o `TasksDbContext` nunca mapeia a entidade
do outro lado (D-27, CA-13), então o EF não tem como gerá-la a partir do modelo. A constraint chama-se
`fk_tasks_owner_id_identity_users`; comprovada por consulta ao catálogo do Postgres (`pg_constraint`,
`confdeltype = 'c'`), não só pelo código — é o que o teste de CA-02c faz. Essa FK é a rede de segurança que
sustenta a exclusão em cascata de [BE-16](tarefas/backend/BE-16-exclusao-conta.md) (apagar um usuário remove
as tarefas dele, inclusive as soft-deleted — CA-16, verificado com `IgnoreQueryFilters()`) e rejeita, com
`SQLSTATE 23503`, qualquer `owner_id` sem usuário correspondente (CA-15). O mesmo resumo está comentado em
`TasksDbContext` (`src/Tasks/TodoList.Tasks.Infrastructure/Persistence/TasksDbContext.cs`).

**Consequência prática, mais crítica agora do que antes:** a migration do Tasks que cria essa FK **exige**
que o schema `identity` já exista. Aplicar as migrations do Tasks contra um banco vazio **sem** ter migrado o
Identity antes falha com

```text
ERRO: esquema "identity" não existe
SQLSTATE: 3F000
```

(confirmado contra Postgres real — não é hipotético). É por isso que "Identity primeiro" (seção anterior)
deixou de ser só uma convenção de organização e passou a ser um requisito rígido de qualquer código — inclusive
testes — que aplica migrations do Tasks contra um banco vazio: `IdentityDbContext.Database.MigrateAsync()`
precisa rodar antes de `TasksDbContext.Database.MigrateAsync()`. Testes de integração do Tasks que migram
contra um Postgres real (Testcontainers) migram o Identity primeiro pelo mesmo motivo — ver
`tests/TodoList.Tasks.IntegrationTests/Persistence/CrossSchemaForeignKeyTests.cs`,
`PostgresPersistenceTests.cs` e `TodoTaskPostgresIndexTests.cs`.

### Health checks: liveness × readiness (CA-03, CA-04)

Cada serviço expõe dois checks, via `MapHealthChecks` (Minimal APIs):

| Endpoint | O que verifica | Comportamento com banco fora do ar |
|---|---|---|
| `GET /health` | Só que o processo está de pé — nenhuma dependência externa | Sempre `200 healthy` |
| `GET /health/ready` | Conectividade com o Postgres (`AddDbContextCheck`) | `503` (degradado) — **o processo não cai** |

```powershell
curl http://localhost:5080/health/ready   # Identity
curl http://localhost:5100/health/ready   # Tasks
```

### Testes de integração com Postgres real (Testcontainers)

Cada serviço tem uma base de testes de integração com Testcontainers
(`tests/TodoList.Identity.IntegrationTests/Persistence/PostgresPersistenceTests.cs` e o equivalente em
Tasks), cobrindo o round-trip real de `timestamptz` (CA-05), a idempotência de `Database.MigrateAsync()`
(CA-02b) e o readiness com o banco de pé (`HealthReadyComBancoRealTests`, a outra metade do CA-04) contra
um Postgres real, subido em container por `ICollectionFixture` — um container por coleção, não por teste
(CA-09). **Eles rodam por padrão**: são o que prova que a persistência funciona contra Postgres de verdade,
e não só sobre o SQLite in-memory.

O container só é iniciado (via `PostgresContainerFixture.EnsureStartedAsync()`) de dentro do corpo do
teste — nunca no `IAsyncLifetime` da fixture da coleção, que rodaria à toa em todo `dotnet test`, tentando
falar com o daemon Docker mesmo numa máquina que não tem Docker.

Numa máquina sem Docker, exclua a categoria explicitamente:

```powershell
dotnet test --filter "Category!=Docker"
```

Note a diferença: a exclusão é uma escolha de quem roda, não um `Skip` no código. Teste desativado no
código fica desativado para todo mundo, inclusive no CI, e ninguém percebe.

### Ordem aleatória entre testes (CA-10)

Os dois projetos de integração registram um `ITestCaseOrderer`/`ITestCollectionOrderer` que embaralha a
ordem a cada execução (`RandomOrder.cs`). O xUnit roda em ordem fixa por padrão — sem isso o CA-10 nunca
seria exercido, e uma dependência de ordem entre testes (fácil de criar quando eles compartilham um
container Postgres) só apareceria como falha intermitente no CI.

A semente é sorteada por execução e impressa no começo. Quando uma ordem específica quebrar, reproduza-a:

```powershell
$env:XUNIT_ORDER_SEED = "1163027058"; dotnet test
```

### O que puder ser verificado sem Postgres, é — sem Docker

Boa parte do mecanismo de persistência não precisa de Postgres, e o que não precisa é verificado por uma
base separada que roda **sempre**: `tests/TodoList.Identity.IntegrationTests/Persistence/AuditingAndSoftDeleteTests.cs`
e o equivalente em Tasks, sobre **SQLite in-memory**. Eles reaproveitam os tipos reais da Infrastructure
(`AuditingSaveChangesInterceptor`, `EfConventions`) contra uma entidade de teste só do projeto de teste
(`AuditableProbe` — nunca em produção), cobrindo CA-06 (soft delete), CA-07 (auditoria) e CA-08
(`TimeProvider` fake, sem `Thread.Sleep`).

`ConvencoesDeModeloTests` (também sem Docker) trava a convenção UTC no nível do **modelo** do EF, e não do
comportamento observado. A distinção importa: os testes de comportamento gravam valores que já nascem
`Kind=Utc` (vêm do `TimeProvider`) e o Npgsql devolve `Kind=Utc` por conta própria — passariam iguais com a
convenção desligada. O round-trip contra Postgres real (CA-05) usa de propósito um `DateTime` com
`Kind=Local`, que é o caso que a convenção existe para resolver.

## Rodando os dois serviços: roteiro de verificação da comunicação gRPC (BE-31)

> **⚠️ Roteiro histórico do T1.** Os passos abaixo disparam `curl -X POST /api/tasks` direto no Tasks
> Service — esse gatilho HTTP provisório (BE-29) foi **removido** por [BE-35](tarefas/backend/BE-35-tasks-servidor-grpc.md):
> o Tasks só é alcançável por gRPC agora. O roteiro equivalente do T2, através do API Gateway, chega com
> [BE-39](tarefas/backend/BE-39-verificacao-t2.md). Mantido aqui só como registro do que a demonstração do T1 provava.

Esta seção é o roteiro de demonstração da etapa: subir tudo do zero e ver uma tarefa ser criada **depois**
de o Identity confirmar o dono por gRPC, ver a criação ser recusada quando o Identity nega, e ver o que
acontece quando o Identity não responde. Os três caminhos abaixo foram executados contra os dois processos
reais — os trechos de log são saída literal, não exemplo escrito à mão.

### 0. O atalho: dois scripts

> **`scripts/demo-curl.ps1` não existe mais.** Foi removido junto da introdução do API Gateway — o
> Tasks não tem mais gatilho REST próprio (BE-35), então não há mais o que este script chamasse
> diretamente. O equivalente do T2, contra o Gateway, é [`scripts/demo-t2.ps1`](#rodando-o-t2)
> (ver a seção "Rodando o T2" abaixo). Os dois comandos originais ficam registrados aqui só como
> histórico do que a demonstração do T1 executava:

```powershell
./scripts/demo-local.ps1          # Postgres + migrations na ordem + os dois serviços, cada um numa janela
./scripts/demo-curl.ps1           # (removido) dispara os 4 desfechos e confere cada um
./scripts/demo-curl.ps1 -IncluindoIndisponibilidade   # (removido) o 503, com o Identity desligado
```

`demo-curl.ps1` aceitava `-BaseUrl` — o mesmo script serviria para verificar o ambiente do GCP através
de um túnel. As seções abaixo são o que os scripts faziam, passo a passo, para quando algo saísse do
esperado.

Para levar isto às VMs do GCP — empacotamento, systemd, firewall VPC e endurecimento do Postgres —
veja [`deploy/README.md`](deploy/README.md).

### 1. Subir, na ordem

A ordem importa e não é arbitrária: a FK cruzada `tasks.tasks.owner_id → identity.users(id)` faz a
migration do Tasks depender da tabela criada pela do Identity.

```powershell
# a) Postgres de desenvolvimento
docker compose up -d

# b) connection strings (esta janela; em dev, user-secrets também serve para rodar os serviços)
$env:ConnectionStrings__IdentityDb = "Host=localhost;Port=5432;Database=todolist;Username=postgres;Password=postgres"
$env:ConnectionStrings__TasksDb    = "Host=localhost;Port=5432;Database=todolist;Username=postgres;Password=postgres"

# c) migrations — Identity SEMPRE primeiro
dotnet ef database update --project src/Identity/TodoList.Identity.Infrastructure --startup-project src/Identity/TodoList.Identity.Api
dotnet ef database update --project src/Tasks/TodoList.Tasks.Infrastructure --startup-project src/Tasks/TodoList.Tasks.Api
```

Depois, **um terminal para cada serviço** — Identity primeiro, Tasks depois:

```powershell
# terminal 1 — Identity: lê os usuários do banco e popula identity.users com os dois ids de demonstração
$env:ConnectionStrings__IdentityDb = "Host=localhost;Port=5432;Database=todolist;Username=postgres;Password=postgres"
$env:UserStore__Provider = "Persisted"
$env:UserStore__SeedDemoUsers = "true"
dotnet run --project src/Identity/TodoList.Identity.Api
```

```powershell
# terminal 2 — Tasks: modo provisório de identidade por header (BE-29), sem autenticação ainda
$env:ConnectionStrings__TasksDb = "Host=localhost;Port=5432;Database=todolist;Username=postgres;Password=postgres"
$env:Tasks__AllowAnonymousCreate = "true"
dotnet run --project src/Tasks/TodoList.Tasks.Api
```

> **`UserStore__Provider=Persisted` não é detalhe de conforto.** Com o padrão `InMemory`, o `ValidateUser`
> responde a partir do seed em memória, mas a gravação da tarefa continua batendo na FK cruzada contra
> `identity.users` — o Identity aprovaria um dono que o banco não conhece, e a criação falharia no
> `INSERT`, não na validação. Para o roteiro, os dois lados precisam olhar para o mesmo banco.

Usuários de demonstração (ids fixos, criados pelo `DemoUserSeeder` — ver a seção de store de usuários):

| Papel no roteiro | Id | `active` |
|---|---|---|
| Dono válido | `10000000-0000-0000-0000-000000000001` | `true` |
| Dono inativo | `10000000-0000-0000-0000-000000000002` | `false` |

### 2. Caminho de sucesso — 201

```powershell
curl -X POST http://localhost:5100/api/tasks `
  -H "Content-Type: application/json" `
  -H "X-User-Id: 10000000-0000-0000-0000-000000000001" `
  -d '{"title":"Preparar a demonstracao do T1","priority":"High"}'
```

```json
{"id":"8e3e0bc4-dd97-4b1d-b8d4-eb89e216352e","title":"Preparar a demonstracao do T1","description":null,
 "status":"Pending","priority":"High","dueDate":null,"completedAt":null,"isOverdue":false,
 "createdAt":"2026-09-05T14:50:47.632211Z","updatedAt":"2026-09-05T14:50:47.632211Z"}
```

**Evidência nos dois lados — o mesmo `traceId`:**

```text
# terminal 2 (Tasks)
info: TodoList.Tasks.Infrastructure.Identity.GrpcIdentityGateway[1561142135]
      ValidateUser (Identity gRPC): userId=10000000-0000-0000-0000-000000000001, statusCode=OK,
      durationMs=23.0773, traceId=00-23666181a22c87f7bb74504949ed8d91-fdb5656773b1f372-00

# terminal 1 (Identity)
info: TodoList.Identity.Api.Grpc.IdentityGrpcService[1561142135]
      ValidateUser: userId=10000000-0000-0000-0000-000000000001, exists=True, active=True,
      durationMs=13.3075, traceId=00-23666181a22c87f7bb74504949ed8d91-fdb5656773b1f372-00
```

Esse par de linhas **é** o entregável desta etapa. A serialização gRPC é binária: sem os dois logs
correlacionados, não há nada observável entre "requisição entrou no Tasks" e "resposta saiu do Identity" —
e um Tasks que decidisse sozinho produziria exatamente o mesmo `201`. O `traceId` é o traceparent do W3C,
propagado pelo Tasks na metadata gRPC e registrado pelo Identity como recebido.

### 3. Caminho de rejeição — 404, decidido pelo Identity

A **mesma** requisição, mudando **apenas** o `X-User-Id`:

```powershell
curl -X POST http://localhost:5100/api/tasks `
  -H "Content-Type: application/json" `
  -H "X-User-Id: 99999999-9999-9999-9999-999999999999" `
  -d '{"title":"Tarefa de um dono que nao existe"}'
```

```json
{"type":"https://httpstatuses.io/404","title":"Not Found","status":404,
 "detail":"O usuário informado não foi encontrado.","errorCode":"task.owner_not_found",
 "traceId":"0HNOBBK8JELVK:00000001"}
```

```text
# terminal 1 (Identity) — quem disse "não" foi ele
info: TodoList.Identity.Api.Grpc.IdentityGrpcService[1561142135]
      ValidateUser: userId=99999999-9999-9999-9999-999999999999, exists=False, active=False,
      durationMs=2.0527, traceId=00-aee49582c3208ec84dbbab63f6e7763c-0d50a6b0f24d998f-00

# terminal 2 (Tasks) — a chamada foi OK; o "não" é conteúdo da resposta, não erro de transporte
info: TodoList.Tasks.Infrastructure.Identity.GrpcIdentityGateway[1561142135]
      ValidateUser (Identity gRPC): userId=99999999-9999-9999-9999-999999999999, statusCode=OK,
      durationMs=3.8989, traceId=00-aee49582c3208ec84dbbab63f6e7763c-0d50a6b0f24d998f-00
warn: TodoList.Tasks.Application.Tasks.CreateTaskHandler[2057755150]
      Criação de tarefa rejeitada: dono 99999999-9999-9999-9999-999999999999 inválido (not_found).
```

O par sucesso/rejeição, com a requisição mudando só no header, é o que demonstra que **quem decide é o
Identity**: um Tasks que aceitasse tudo passaria no caminho anterior e falharia aqui.

O dono **inativo** é o terceiro desfecho da mesma chamada — o Identity responde `exists=True, active=False`
e o Tasks devolve `409`, não `404` (a identidade existe; o que impede é o estado dela):

```powershell
curl -X POST http://localhost:5100/api/tasks `
  -H "Content-Type: application/json" `
  -H "X-User-Id: 10000000-0000-0000-0000-000000000002" `
  -d '{"title":"Tarefa de um dono inativo"}'
```

```json
{"type":"https://httpstatuses.io/409","title":"Conflict","status":409,
 "detail":"O usuário informado está inativo e não pode criar tarefas.","errorCode":"task.owner_inactive",
 "traceId":"0HNOBBK8JELVL:00000001"}
```

### 4. Caminho de indisponibilidade — 503, nada gravado

Encerre o Identity (`Ctrl+C` no terminal 1) e repita a requisição do caminho de sucesso:

```text
HTTP/1.1 503 Service Unavailable
Retry-After: 5
```

```json
{"type":"https://httpstatuses.io/503","title":"Service Unavailable","status":503,
 "detail":"Não foi possível validar o usuário no momento. Tente novamente em instantes.",
 "errorCode":"identity.unavailable","traceId":"0HNOBBK8JELVN:00000001"}
```

```text
# terminal 2 (Tasks)
warn: TodoList.Tasks.Infrastructure.Identity.GrpcIdentityGateway[1388007139]
      ValidateUser (Identity gRPC) falhou: userId=10000000-0000-0000-0000-000000000001,
      statusCode=DeadlineExceeded, durationMs=2033.7856,
      traceId=00-e527fe72f84ca167730bc902e56e5c72-1599d28edf384c74-00
fail: TodoList.Tasks.Application.Tasks.CreateTaskHandler[883122609]
      Criação de tarefa abortada: Identity indisponível ao validar o dono 10000000-0000-0000-0000-000000000001.
```

Este é o **fail-closed** da decisão D-28: a tarefa **não** é gravada. A FK garante que o dono existe, mas
só o Identity sabe se ele está **ativo** — na dúvida, o Tasks recusa. O `statusCode` desta linha varia
entre `Unavailable` (conexão recusada de imediato) e `DeadlineExceeded` (o prazo de
`Identity:GrpcTimeoutSeconds` estourou antes); os dois são o mesmo desfecho para quem chamou. Nenhum
detalhe de transporte — endereço, `RpcException`, status gRPC — aparece no corpo da resposta; ele fica só
no log do servidor.

### HTTP/2 sem TLS (h2c): por que o endpoint é declarado `Http2`

Com o Identity em `http://`, o endpoint gRPC do Kestrel **precisa** ser declarado `Protocols: Http2`
(é o que está em `appsettings.json`, ver a seção "Configuração"). Sem TLS não há ALPN para negociar o
protocolo, e o padrão `Http1AndHttp2` resolve para HTTP/1.1 — o canal gRPC então falha com um erro de
protocolo que não diz o que está errado.

O switch `AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true)`, do
lado **cliente**, também faz a chamada funcionar, e **não é** a solução adotada aqui. Fica registrado
apenas como último recurso, porque ele resolve no consumidor um problema que é do servidor: cada novo
cliente do Identity precisaria repetir o remendo, e um deles vai esquecer. Declarar o endpoint como
`Http2` conserta para todos, de uma vez.

### Sintomas e causas

| Sintoma | Causa provável | O que fazer |
|---|---|---|
| `curl` devolve "connection refused" em `5100` | o Tasks não subiu, ou está em outra porta | ver o terminal 2; conferir `Kestrel:Endpoints:Http:Url` |
| **503** `identity.unavailable` em toda requisição | o Identity não está no ar, ou `Identity:GrpcAddress` aponta para o lugar errado | subir o Identity primeiro; conferir a chave (seção "Configuração") |
| Erro de **protocolo** no canal gRPC (`HTTP/1.1` onde se esperava HTTP/2) | o endpoint gRPC não está declarado `Protocols: Http2` | ver a nota sobre h2c acima — corrigir no servidor, não no cliente |
| **404** `task.owner_not_found` com um id que você acredita existir | o id não existe **no Identity** — ou o Identity está lendo de outro store | conferir `UserStore:Provider` e se o `DemoUserSeeder` rodou |
| **400** apontando `X-User-Id` | header ausente, vazio ou não é um `Guid` | é o comportamento esperado do modo provisório (BE-29): sem dono não há o que validar |
| **500** no `INSERT`, depois de o Identity aprovar | FK cruzada: o dono existe no store do Identity mas não em `identity.users` | `UserStore__Provider=Persisted` + `SeedDemoUsers=true`, e migration do Identity aplicada |

> **Dois identificadores diferentes, de propósito — e é uma armadilha.** O `traceId` do **corpo de erro**
> (`0HNOBBK8JELVK:00000001`) é o `HttpContext.TraceIdentifier`, escolhido em BE-03 para correlacionar a
> resposta com o log de erro do próprio serviço. O `traceId` das linhas de **gRPC** é o traceparent do
> W3C, que é o que cruza a fronteira entre os serviços. Eles **não** são o mesmo valor: partindo de um
> `404` que o usuário reportou, localize a requisição pelo horário e pelo `userId`, não pelo `traceId` do
> corpo. Unificar os dois é trabalho de observabilidade (BE-24), não desta etapa.

## Como testar

Na raiz do repositório:

```powershell
dotnet test
```

Isso executa os quatro projetos de teste. Com Docker de pé são **296 testes**; sem Docker, use
`dotnet test --filter "Category!=Docker"` e são **271** (ver seção de persistência acima):

- `TodoList.Identity.UnitTests` — testes de arquitetura (dependências entre camadas e entre serviços,
  incluindo que Domain/Application não referenciam o `.proto`/tipos gerados — CA-08 de BE-25; e que
  Application não referencia EF Core/Npgsql — CA-12 de BE-02), inspeção do modelo do `IdentityDbContext`
  (CA-13 de BE-02), varredura contra connection string versionada (CA-11 de BE-02), validação de
  configuração na inicialização, `IdentityGrpcService`/`InMemoryUserLookup`/`PersistedUserLookup` com
  `IUserLookup`/`IUserRepository` substituídos, `Email`/`User` de domínio (CA-01 a CA-09 de BE-04),
  reflection sobre `User` (nenhum setter público — CA-07/CA-08 de BE-04) e `DemoUserSeeder` com
  `IUserRepository` substituído (idempotência).
- `TodoList.Identity.IntegrationTests` — `GET /health` (liveness) e `GET /health/ready` (readiness,
  degradado sem derrubar o processo — CA-03/CA-04 de BE-02), o servidor gRPC real subido por
  `WebApplicationFactory` invocado por um cliente gRPC de teste (`ValidateUser`, `ValidateToken`,
  id malformado, inclusive com `UserStore:Provider=Persisted` contra Postgres real — CA-13 de BE-26), o
  CRUD trivial sobre SQLite in-memory exercitando o interceptor de auditoria/soft delete real
  (CA-06/CA-07/CA-08 de BE-02), as convenções de modelo (CA-05, sem Docker) e os testes
  Testcontainers/Postgres — round-trip de `timestamptz`, idempotência de migration, readiness com o banco
  de pé (CA-05/CA-02b/CA-04 de BE-02), índice único de e-mail (CA-10/CA-11 de BE-04), serialização JSON
  sem `PasswordHash` (CA-12 de BE-04), `UserRepository.GetByEmailAsync`/`EmailExistsAsync` (comparação do
  value object `Email` traduzida pelo EF contra a coluna convertida), `PersistedUserLookup` refletindo
  desativação sem reiniciar (CA-13 de BE-26) e `DemoUserSeeder` rodado duas vezes contra o mesmo banco sem
  duplicar.
- `TodoList.Tasks.UnitTests` — mesma cobertura de arquitetura do Identity (incluindo CA-12/CA-13 de BE-02,
  e a varredura por endereço/porta literal fora de `appsettings*.json` — CA-01 de BE-30), para o Tasks
  Service, além da validação de inicialização de `IdentityGrpcOptions` (`Identity:GrpcAddress` ausente ou
  não parseável como URI absoluta, `Identity:GrpcTimeoutSeconds` zero/negativo — CA-02/CA-05 de BE-30).
- `TodoList.Tasks.IntegrationTests` — mesma cobertura de persistência do Identity (health readiness, SQLite,
  convenções de modelo, Testcontainers), `GET /health` via `WebApplicationFactory`, o cliente/servidor gRPC
  reais de BE-27/BE-28 (incluindo sobrescrita por variável de ambiente de `Identity:GrpcAddress`/
  `GrpcTimeoutSeconds` com efeito observável — CA-03/CA-04 de BE-30) e `POST /api/tasks` de ponta a ponta
  com o Identity em endereço não padrão (CA-06 de BE-30).

## Formatação e estilo

O estilo é automatizado por `.editorconfig` + `dotnet format`. Para verificar sem aplicar mudanças:

```powershell
dotnet format --verify-no-changes
```

## Configuração

Cada serviço usa configuração tipada (`IOptions<T>`) com validação na inicialização (`ValidateOnStart`).
Se uma seção obrigatória estiver ausente, o serviço **falha ao iniciar** com uma mensagem clara — não na
primeira requisição. Nenhum segredo fica em `appsettings.json`; valores sensíveis vêm de variável de
ambiente ou user-secrets em desenvolvimento.

### Onde cada serviço escuta e para onde o Tasks chama (BE-30)

Endereço, porta e protocolo são configuração, nunca literal em código — mover um serviço para outro
host/porta/ambiente é editar `appsettings` ou definir uma variável de ambiente, nunca recompilar (uma
varredura de arquitetura garante isso do lado do Tasks:
`ArchitectureTests.CodigoDoTasks_NaoContemEnderecoOuPortaLiteral_ForaDosAppsettings`). Qualquer chave de
`appsettings.json` pode ser sobrescrita por variável de ambiente trocando cada nível de `:` por `__` (dois
underscores) — comportamento padrão do provedor
`Microsoft.Extensions.Configuration.EnvironmentVariables`, sem nenhuma chave nova a inventar para isso
funcionar (é o mesmo mecanismo que, mais adiante, permite injetar os endereços pelo painel do provedor de
nuvem).

**Tasks Service — cliente gRPC do Identity (`IdentityGrpcOptions`):**

| Chave | Tipo | Padrão em dev | Obrigatória | Variável de ambiente equivalente |
|---|---|---|---|---|
| `Identity:GrpcAddress` | `string` (URI absoluta) | `http://localhost:5081` | **sim** | `Identity__GrpcAddress` |
| `Identity:GrpcTimeoutSeconds` | `int` (> 0) | `2` | não | `Identity__GrpcTimeoutSeconds` |

`Identity:GrpcAddress` ausente, vazio ou não parseável como URI absoluta, e `Identity:GrpcTimeoutSeconds`
igual a zero ou negativo, derrubam a inicialização do Tasks com uma mensagem que nomeia a chave — nunca
esperam até a primeira chamada gRPC (CA-02/CA-05).

**Identity Service — endpoints Kestrel:**

| Endpoint | Chave | Padrão em dev | Protocolo | Variável de ambiente equivalente |
|---|---|---|---|---|
| REST | `Kestrel:Endpoints:Http:Url` | `http://localhost:5080` | `Http1` | `Kestrel__Endpoints__Http__Url` |
| gRPC | `Kestrel:Endpoints:Grpc:Url` | `http://localhost:5081` | **`Http2`** | `Kestrel__Endpoints__Grpc__Url` |

**Tasks Service — endpoints Kestrel (BE-35, D-37):**

| Endpoint | Chave | Padrão em dev | Protocolo | Variável de ambiente equivalente |
|---|---|---|---|---|
| HTTP (só `/health`, `/health/ready`) | `Kestrel:Endpoints:Http:Url` | `http://localhost:5100` | `Http1` | `Kestrel__Endpoints__Http__Url` |
| gRPC (`CreateTask`, `grpc.health.v1.Health`) | `Kestrel:Endpoints:Grpc:Url` | `http://localhost:5101` | **`Http2`** | `Kestrel__Endpoints__Grpc__Url` |

O endpoint gRPC precisa do protocolo `Http2` declarado explicitamente (CA-07): sem TLS o Kestrel não
negocia o protocolo por ALPN e o padrão (`Http1AndHttp2`) resolveria para HTTP/1.1 — o cliente gRPC
falharia com um erro de protocolo pouco óbvio. A partir de BE-35, o Tasks não expõe mais nenhum endpoint
REST de negócio — `POST /api/tasks` (BE-29) foi removido, e `/api/tasks` deixou de existir. `CreateTask` só
é alcançável pela porta gRPC (`5101` em dev/VM, `8080` no container — D-37).

**Outras chaves de endereço/porta/ambiente já existentes na base:**

| Chave | Serviço | Padrão em dev | Obrigatória | Variável de ambiente equivalente |
|---|---|---|---|---|
| `Kestrel:Endpoints:Http:Url` | Tasks | `http://localhost:5100` | não¹ | `Kestrel__Endpoints__Http__Url` |
| `Kestrel:Endpoints:Grpc:Url` | Tasks | `http://localhost:5101` | não¹ | `Kestrel__Endpoints__Grpc__Url` |
| `ConnectionStrings:IdentityDb` | Identity | — (nunca versionada, CA-11 de BE-02) | sim para operar o banco² | `ConnectionStrings__IdentityDb` |
| `ConnectionStrings:TasksDb` | Tasks | — (nunca versionada, CA-11 de BE-02) | sim para operar o banco² | `ConnectionStrings__TasksDb` |
| `ASPNETCORE_URLS` | ambos | — | não | já é variável de ambiente — alternativa/complemento a `Kestrel:Endpoints:*`, padrão do ASP.NET Core |
| `Tasks:MaxActivePerUser` | Tasks | `500` | não (`null` desativa o limite) | `Tasks__MaxActivePerUser` |
| `UserStore:Provider` | Identity | `InMemory` | não (tem padrão) | `UserStore__Provider` |
| `UserStore:DemoUserPassword` | Identity | — (nunca versionada) | sim, se `SeedDemoUsers=true` (BE-33) | `UserStore__DemoUserPassword` |
| `Jwt:Issuer` | Identity | `todolist-identity` | sim | `Jwt__Issuer` |
| `Jwt:Audience` | Identity | `todolist` | sim | `Jwt__Audience` |
| `Jwt:SigningKey` | Identity | — (nunca versionada, CA-02/CA-03 de BE-08, D-31) | sim | `Jwt__SigningKey` — ver seção acima |
| `Jwt:AccessTokenMinutes` | Identity | `15` (D-02) | não (tem padrão, faixa 1–60) | `Jwt__AccessTokenMinutes` |
| `Jwt:RefreshTokenDays` | Identity | `7` (D-10) | não (tem padrão, faixa 1–90; usado só pela futura BE-10) | `Jwt__RefreshTokenDays` |

¹ Sem essa chave o Kestrel cai no próprio padrão (não escuta em `0.0.0.0`) — por isso o `appsettings.json`
base do Tasks já a declara explicitamente (BE-30), com `appsettings.Development.json` sobrepondo para
`localhost` em desenvolvimento — mesmo padrão já usado pelo Identity.
² Sem a connection string configurada o serviço **sobe normalmente**; só o readiness (`/health/ready`)
fica degradado e qualquer operação de banco falha ao ser tentada (seção de persistência acima).

### Sobrescrita por variável de ambiente: o que está automatizado e o que é roteiro manual (BE-30)

`Identity__GrpcAddress` e `Identity__GrpcTimeoutSeconds` como variável de ambiente sobrescrevendo o
`appsettings`, com efeito observável (a chamada muda de destino; o deadline muda de verdade) e
`POST /api/tasks` completando a criação com o Identity num endereço não padrão, têm teste automatizado —
`GrpcIdentityGatewayIntegrationTests` e `CreateTaskCustomAddressEndToEndTests`, em
`tests/TodoList.Tasks.IntegrationTests`. O que fica como **roteiro manual** (dois processos `dotnet run`
reais, em portas TCP diferentes — os testes automatizados usam `WebApplicationFactory`/`TestServer`, que
não abre porta real):

```powershell
# terminal 1 — Identity em portas não padrão, só por variável de ambiente
$env:Kestrel__Endpoints__Http__Url = "http://0.0.0.0:6080"
$env:Kestrel__Endpoints__Grpc__Url = "http://0.0.0.0:6081"
dotnet run --project src/Identity/TodoList.Identity.Api

# terminal 2 — Tasks em porta não padrão, apontando para o Identity acima
$env:Kestrel__Endpoints__Http__Url = "http://0.0.0.0:6100"
$env:Identity__GrpcAddress = "http://localhost:6081"
dotnet run --project src/Tasks/TodoList.Tasks.Api

# terminal 3 — usuário ativo do seed em memória (README, seção de store de usuários)
curl -X POST http://localhost:6100/api/tasks `
  -H "Content-Type: application/json" `
  -H "X-User-Id: 10000000-0000-0000-0000-000000000001" `
  -d '{"title":"teste manual de CA-06"}'
# esperado: 201 Created — nenhum appsettings*.json foi editado, só variável de ambiente.
```

## O que ainda não existe nesta etapa

BE-02 entregou a base de persistência (EF Core, Postgres, migrations, soft delete, auditoria, health
checks, Testcontainers); BE-04 entregou a primeira entidade de negócio do Identity — `User`/`Email`,
`IUserRepository`/`UserRepository`, a migration `AddUsersTable` (`identity.users`, índice único em
`email`) e `PersistedUserLookup` (BE-26 CA-13). A FK cruzada `tasks.tasks.owner_id → identity.users(id)`
com `ON DELETE CASCADE` (CA-02c/CA-15/CA-16 de BE-02) **existe** — ver a seção "FK cruzada" acima.

O cliente gRPC do Tasks e a validação de dono na criação de tarefa (BE-27/BE-28) **já existem** —
`GrpcIdentityGateway`/`IIdentityGateway` e a rejeição de dono inexistente/inativo em `POST /api/tasks` —,
assim como a configuração por ambiente dos endereços/portas dos dois serviços e do cliente gRPC (BE-30,
seção "Configuração" acima) e o roteiro de verificação de ponta a ponta dos três caminhos (BE-31, seção
"Rodando os dois serviços"). Segue fora do escopo desta etapa: validação real de JWT em `ValidateToken`
(fica para o API Gateway, D-31), autenticação/hash de senha real (BE-06/BE-08/BE-09 — `DemoUserSeeder` usa
um `PasswordHash` placeholder documentado, não um hash válido), endpoints de usuário (BE-07/BE-14), o
handler `DELETE /api/me` de exclusão de conta (BE-16 — a FK que sustenta a cascata das tarefas já existe,
mas o endpoint em si ainda não), qualquer regra de negócio de tarefa e o pipeline de CI (BE-24) — inclusive
a varredura por `JOIN`/nome de schema entre serviços (CA-14 de BE-02), que entra junto das varreduras de
BE-24. A varredura por endereço/porta literal fora de `appsettings*.json` (CA-01 de BE-30) já existe como
teste (`ArchitectureTests.CodigoDoTasks_NaoContemEnderecoOuPortaLiteral_ForaDosAppsettings`, do lado do
Tasks, rodado a cada `dotnet test`) — o que falta, e fica para BE-24, é integrá-la ao pipeline de CI.

## API Gateway (BE-36)

`src/Gateway/TodoList.Gateway.Api` é o **único ponto público** da aplicação (D-32): recebe REST/JSON do
navegador, autentica e valida na borda, e traduz cada chamada para gRPC contra o Identity e o Tasks. É um
projeto Web único, sem Domain/Application/Infrastructure (D-33) — organizado por pasta
(`Endpoints/`, `Authentication/`, `Validation/`, `Backends/`, `ErrorHandling/`, `Contracts/`,
`Configuration/`) — e **não referencia** `TodoList.Identity.*`/`TodoList.Tasks.*`/`TodoList.SharedKernel`:
os dois `.proto` (`contracts/identity/v1`, `contracts/tasks/v1`), como cliente gRPC, são a única fonte de
tipos compartilhados com os backends.

### O que ele faz

- `POST /api/auth/login` (anônimo) — troca e-mail/senha por um access token via `Login` (Identity); e-mail
  inexistente, senha errada ou usuário inativo devolvem sempre o mesmo **401** `auth.invalid_credentials`
  (RN-AUTH-09) — o Gateway só vê `succeeded=false`, nunca a causa.
- `POST /api/tasks` (autenticado) — valida o payload na borda (título, descrição, prioridade, data de
  vencimento — os mesmos limites de RN-TASK-02/03/04, duplicados de propósito como defesa em profundidade)
  e traduz para `CreateTask` (Tasks); sucesso devolve **201** com `Location: /api/tasks/{id}`.
- `GET /health` (anônimo) — liveness simples, não depende de Identity/Tasks estarem de pé.
- Autenticação via `IdentityTokenAuthenticationHandler`: todo endpoint exige token por padrão (fallback
  policy); só `/health`, `POST /api/auth/login` e a documentação OpenAPI/Scalar (Development) são
  anônimos. O Bearer é validado por `ValidateToken` (gRPC) — a chave de assinatura do JWT nunca sai do
  Identity (D-31); o Gateway não tem, e não deve ganhar, nenhuma chave `Jwt:*`.
- Indisponibilidade do Identity ou do Tasks nunca vira 401/400 "normal" — vira **503** com `Retry-After`
  (D-28): "não consegui perguntar" é uma causa diferente de "credencial inválida" ou "payload inválido".
- Erros de negócio do Tasks (`RpcException`) são traduzidos por `GrpcErrorMapping` (D-35): `NotFound` →
  404, `FailedPrecondition` → 409, `Unavailable`/`DeadlineExceeded` → 503, o resto → 500 genérico — sempre
  preservando o `errorCode` do trailer gRPC em `extensions.errorCode` do `ProblemDetails`, o mesmo formato
  que o front já consumia de Identity/Tasks.

### Portas e configuração

O Gateway escuta em **uma única porta HTTP/1**, `8080` (`http://0.0.0.0:8080` em produção,
`http://localhost:8080` em Development) — D-37, a mesma porta que vira `$PORT` no Cloud Run (T3).

Chaves de configuração (`appsettings.json`/`appsettings.Development.json`, validadas com `ValidateOnStart`
— endereço ausente ou que não é URI absoluta derruba a inicialização, nunca a primeira requisição):

| Chave | Padrão | O que é |
|---|---|---|
| `Backends:IdentityGrpcAddress` | `http://localhost:5081` | Endereço gRPC (h2c local) do Identity Service |
| `Backends:TasksGrpcAddress` | `http://localhost:5101` | Endereço gRPC (h2c local) do Tasks Service |
| `Backends:IdentityGrpcTimeoutSeconds` | `2` | Deadline de `ValidateToken`/`Login` |
| `Backends:TasksGrpcTimeoutSeconds` | `5` | Deadline de `CreateTask` — maior que o do Identity porque o Tasks faz, dentro dele, uma chamada aninhada ao Identity com deadline próprio de 2s |

### Como subir localmente (Identity + Tasks + Gateway)

```powershell
# terminal 1 — Identity (gRPC em 5081)
dotnet run --project src/Identity/TodoList.Identity.Api

# terminal 2 — Tasks (gRPC em 5101)
dotnet run --project src/Tasks/TodoList.Tasks.Api

# terminal 3 — Gateway (REST em 8080), apontando para os dois acima (valores padrão de appsettings.Development.json)
dotnet run --project src/Gateway/TodoList.Gateway.Api
```

Login e criação de tarefa via `curl`:

```bash
# login — troca e-mail/senha pelo access token
curl -s -X POST http://localhost:8080/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"ativo@example.com","password":"senha-do-seed"}'
# => 200 { "accessToken": "...", "expiresAt": "..." }

# criação de tarefa — usa o accessToken da resposta acima
curl -s -X POST http://localhost:8080/api/tasks \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer <accessToken>" \
  -d '{"title":"Comprar leite","priority":"Medium","dueDate":"2026-12-31"}'
# => 201 Created, Location: /api/tasks/{id}
```

Equivalente em PowerShell:

```powershell
$login = Invoke-RestMethod -Method Post -Uri http://localhost:8080/api/auth/login `
    -ContentType "application/json" -Body '{"email":"ativo@example.com","password":"senha-do-seed"}'

Invoke-RestMethod -Method Post -Uri http://localhost:8080/api/tasks `
    -ContentType "application/json" -Headers @{ Authorization = "Bearer $($login.accessToken)" } `
    -Body '{"title":"Comprar leite","priority":"Medium","dueDate":"2026-12-31"}'
```

O roteiro completo de demonstração (cenários de sucesso e falha — 400/401/503) é o de BE-39; este README
cobre só como subir e um exemplo mínimo de fumaça.

## Rodando o T2

Roteiro de verificação de ponta a ponta do T2 (BE-39): subir Postgres + os **três** serviços — Identity,
Tasks, API Gateway — e ver os quatro desfechos exigidos pelo enunciado do T2 (`t2.md`: 401, 400, 201, e
REST → gRPC) acontecendo contra o Gateway, com o mesmo `traceId` correlacionando os logs dos três
serviços. Os trechos de log abaixo são saída literal de uma execução real, não exemplo escrito à mão.

### 0. O atalho: dois scripts

```powershell
./scripts/demo-local.ps1                          # Postgres + migrations na ordem + os três serviços, cada um numa janela
./scripts/demo-t2.ps1 -DemoPassword <senha impressa por demo-local.ps1>
```

`demo-t2.ps1` aceita `-BaseUrl` — é o mesmo script que roda no dia da apresentação, apontado para o IP
externo da VM (BE-39 CA-04, verificação **de fora**, não de `127.0.0.1` dentro da própria VM). Dentro da
VM, o equivalente é [`deploy/smoke.sh`](deploy/smoke.sh) — ver [`deploy/README.md`](deploy/README.md) para
o runbook completo de implantação e o roteiro cronometrado de apresentação.

### 1. Pré-requisitos e Postgres

Os mesmos da seção anterior: Postgres de desenvolvimento por `docker compose`, migrations do Identity
**antes** das do Tasks (a FK cruzada `tasks.tasks.owner_id → identity.users(id)` exige isso).

```powershell
docker compose up -d

$env:ConnectionStrings__IdentityDb = "Host=localhost;Port=5432;Database=todolist;Username=postgres;Password=postgres"
$env:ConnectionStrings__TasksDb    = "Host=localhost;Port=5432;Database=todolist;Username=postgres;Password=postgres"

dotnet ef database update --project src/Identity/TodoList.Identity.Infrastructure --startup-project src/Identity/TodoList.Identity.Api
dotnet ef database update --project src/Tasks/TodoList.Tasks.Infrastructure --startup-project src/Tasks/TodoList.Tasks.Api
```

### 2. Subir os três processos, na ordem

Identity primeiro (emite e valida token, valida dono), Tasks depois (só alcançável por gRPC — BE-35
removeu o gatilho REST provisório), Gateway por último (é quem chama os outros dois).

```powershell
# terminal 1 — Identity: gRPC em 5081, REST (só /health) em 5080
$env:ConnectionStrings__IdentityDb = "Host=localhost;Port=5432;Database=todolist;Username=postgres;Password=postgres"
$env:UserStore__Provider = "Persisted"
$env:UserStore__SeedDemoUsers = "true"
$env:UserStore__DemoUserPassword = "<gerada por você, nunca versionada>"
$env:Jwt__SigningKey = "<gerada por você, nunca versionada>"
dotnet run --project src/Identity/TodoList.Identity.Api
```

```powershell
# terminal 2 — Tasks: gRPC em 5101, REST (só /health) em 5100
$env:ConnectionStrings__TasksDb = "Host=localhost;Port=5432;Database=todolist;Username=postgres;Password=postgres"
dotnet run --project src/Tasks/TodoList.Tasks.Api
```

```powershell
# terminal 3 — Gateway: REST em 8080 — a única borda pública (D-32)
dotnet run --project src/Gateway/TodoList.Gateway.Api
```

Usuários do seed (`DemoUserSeeder` — ver a seção de seed de usuários acima), com a senha de
`UserStore:DemoUserPassword`:

| Papel no roteiro | E-mail | `active` |
|---|---|---|
| Usuário ativo | `ada.lovelace@todolist.example` | `true` |
| Usuário inativo | `charles.babbage@todolist.example` | `false` |

### 3. `scripts/demo-t2.ps1`: os seis passos

```powershell
./scripts/demo-t2.ps1 -DemoPassword "<a senha de UserStore:DemoUserPassword>"
```

```text
API Gateway: http://localhost:8080

--- 1. POST /api/tasks sem token
    OK   HTTP 401 / auth.unauthorized

--- 2. POST /api/tasks com token lixo
    OK   HTTP 401 / auth.unauthorized

--- 3. POST /api/auth/login (usuário ativo)
    OK   HTTP 200

--- 4. POST /api/tasks com título vazio
    OK   HTTP 400

--- 5. POST /api/tasks válido
    OK   HTTP 201
    OK   5b. Location: /api/tasks/c5cdd314-5cec-4ca7-9a39-e18793f63524

--- 6. POST /api/auth/login (usuário inativo)
    OK   HTTP 401 / auth.invalid_credentials

Todos os passos responderam como esperado.
Agora procure, nos três painéis de log (Gateway, Tasks, Identity), o mesmo traceId do passo 5.
```

Saída literal de uma execução real contra os três serviços locais — exit code `0`. Qualquer status
divergente faz o script sair com `1` (BE-39 CA-02).

### 4. Evidência de log: o mesmo `traceId` nos três serviços (passo 5)

O par sucesso é o par login (passo 3) + criação (passo 5). Abaixo, os logs reais do passo 5 — a mesma
requisição atravessa Gateway → Identity (`ValidateToken`), Gateway → Tasks (`CreateTask`), e Tasks →
Identity (`ValidateUser`), e as **três** paradas carregam o mesmo `traceId`
(`00-2d5590b724dcaf24a7e19108dbc1d95c-...`):

```text
# terminal 3 (Gateway)
info: TodoList.Gateway.Api.Backends.IdentityBackend[432667118]
      Chamada gRPC de saída: backend=Identity, rpc=ValidateToken, statusCode=OK, durationMs=52.2405,
      traceId=00-2d5590b724dcaf24a7e19108dbc1d95c-5b80ebee58185f18-00
info: TodoList.Gateway.Api.Backends.TasksBackend[432667118]
      Chamada gRPC de saída: backend=Tasks, rpc=CreateTask, statusCode=OK, durationMs=820.0457,
      traceId=00-2d5590b724dcaf24a7e19108dbc1d95c-5b80ebee58185f18-00

# terminal 1 (Identity) — respondeu ValidateToken (chamado pelo Gateway) e ValidateUser (chamado pelo Tasks)
info: TodoList.Identity.Api.Grpc.IdentityGrpcService[1049219497]
      ValidateToken: valid=True, durationMs=33.7856,
      traceId=00-2d5590b724dcaf24a7e19108dbc1d95c-5b80ebee58185f18-00
info: TodoList.Identity.Api.Grpc.IdentityGrpcService[1561142135]
      ValidateUser: userId=10000000-0000-0000-0000-000000000001, exists=True, active=True, durationMs=15.7272,
      traceId=00-2d5590b724dcaf24a7e19108dbc1d95c-ed399fb3ceb26372-00

# terminal 2 (Tasks) — chamou o Identity de novo (ValidateUser) antes de gravar
info: TodoList.Tasks.Infrastructure.Identity.GrpcIdentityGateway[1561142135]
      ValidateUser (Identity gRPC): userId=10000000-0000-0000-0000-000000000001, statusCode=OK, durationMs=126.395,
      traceId=00-2d5590b724dcaf24a7e19108dbc1d95c-ed399fb3ceb26372-00
info: TodoList.Tasks.Api.Grpc.TasksGrpcService[1138808421]
      CreateTask: ownerId=10000000-0000-0000-0000-000000000001, statusCode=OK, durationMs=698.8348,
      traceId=00-2d5590b724dcaf24a7e19108dbc1d95c-ed399fb3ceb26372-00
```

Note os **dois** pares de `traceId` (mesmo prefixo de 32 caracteres — o trace W3C — com dois sufixos de
span diferentes): um para o salto Gateway→Identity/Gateway→Tasks, outro para o salto interno Tasks→Identity,
que o Tasks abre como filho da chamada que recebeu. É essa cadeia de três saltos, não apenas o par que já
existia no T1, que a comunicação REST→gRPC do T2 precisa deixar auditável (nota técnica de BE-39).

Contra a VM, o comando equivalente para achar essas linhas é:

```bash
sudo journalctl -u todolist-gateway -u todolist-tasks -u todolist-identity --since '2 min ago' \
  | grep -E 'ValidateToken|CreateTask|ValidateUser'
```

### 5. Caminho de falha controlada: Identity fora do ar → 503, nunca 401

Encerre o Identity (`Ctrl+C` no terminal 1, ou `sudo systemctl stop todolist-identity` na VM) e repita a
requisição de criação de tarefa (token válido emitido antes da queda, ou qualquer token):

```text
HTTP/1.1 503 Service Unavailable
Retry-After: 5
```

```json
{"type":"https://httpstatuses.io/503","title":"Serviço temporariamente indisponível.","status":503,
 "detail":"Não foi possível concluir a requisição no momento. Tente novamente em instantes.",
 "errorCode":"identity.unavailable","traceId":"0HNOG09J8NE3D:00000001"}
```

Verificado com o Identity de fato encerrado (BE-39 CA-07): nunca `401` (o Gateway não confunde "não
consegui perguntar ao Identity" com "a credencial é inválida" — D-28, agora aplicado à validação de
token) nem `500`. `./scripts/demo-t2.ps1 -IncluindoIndisponibilidade` automatiza esta verificação
localmente, com o Identity já parado antes de rodar.

### 6. Requisito do `t2.md` → passo do roteiro → evidência (BE-39 CA-01)

| Requisito de `t2.md` | Passo do roteiro | Evidência |
|---|---|---|
| **1. REST público** — pelo menos um endpoint REST adequado ao tema | Passos 3 e 5 (`POST /api/auth/login`, `POST /api/tasks`) contra `http://<host>:8080` | Seção 3 acima — respostas HTTP reais do Gateway, porta única 8080 |
| **2. Validação na borda** — 400 em payload inválido, 201 em sucesso | Passo 4 (título vazio → 400) e passo 5 (título válido → 201 + `Location`) | Seção 3 acima — `CreateTaskHttpRequestValidator` rejeita antes de qualquer chamada gRPC (seção "O que ele faz" da API Gateway acima) |
| **3. Segurança** — 401 com token ausente ou inválido | Passos 1 (sem token) e 2 (token lixo) — dois caminhos de código distintos no middleware (CA-05) | Seção 3 acima — `IdentityTokenAuthenticationHandler`, mesmo corpo `auth.unauthorized` nos dois |
| **4. Tradução e delegação de protocolo** — JSON → gRPC binário para o backend | Passo 5, evidência de log | Seção 4 acima — `traceId` correlacionado em Gateway (`ValidateToken`/`CreateTask`), Tasks (`CreateTask`/`ValidateUser (Identity gRPC)`) e Identity (`ValidateToken`/`ValidateUser`) |

O passo 6 (usuário inativo → 401 idêntico ao de senha errada, RN-AUTH-09) e o caminho de indisponibilidade
(seção 5) não mapeiam para um requisito numerado do enunciado, mas são obrigatórios no script (BE-39
CA-06/CA-07) — a diferença entre "credencial errada" e "não consigo checar" é o tipo de bug que só aparece
na primeira demonstração real, não em revisão de código.

## Rodando em containers (BE-38)

Preparo do T3 (Artifact Registry + Cloud Run) feito ainda no T2: cada serviço ganhou um `Dockerfile`
(`src/Identity/TodoList.Identity.Api/Dockerfile`, `src/Tasks/TodoList.Tasks.Api/Dockerfile`,
`src/Gateway/TodoList.Gateway.Api/Dockerfile`), e a stack inteira sobe com `docker compose` na máquina de
desenvolvimento. Isso é **adicional** ao fluxo de `dotnet run` da seção "Rodando o T2" acima — não o
substitui — e fica fora do caminho crítico da demonstração do T2, que continua sendo a VM (BE-37).

### Pré-requisito: os SQL de migration

O serviço `migrate` do compose (abaixo) aplica os mesmos scripts SQL idempotentes que a VM usa, gerados por
`scripts/publish.ps1`:

```powershell
./scripts/publish.ps1
```

Isso produz `artifacts/sql/01-identity.sql` e `artifacts/sql/02-tasks.sql` — sem eles, o serviço `migrate`
falha explicitamente com uma mensagem apontando para este comando (em vez de subir "vazio" e mascarar o
problema).

### Segredos: `.env`

Os segredos do Identity (`Jwt__SigningKey`, `UserStore__DemoUserPassword`) vêm de um `.env` na raiz,
**nunca versionado** — o `.gitignore` já ignora `*.env`/`.env.*` (com exceção explícita de `.env.example`,
que É versionado, só com placeholders):

```powershell
cp .env.example .env
```

Preencha `.env` com valores gerados por você (nunca reaproveitados de outro ambiente):

```powershell
# Jwt__SigningKey — mínimo 32 bytes em UTF-8 (JwtOptions.Validate)
openssl rand -base64 48

# UserStore__DemoUserPassword — qualquer senha de desenvolvimento; é a mesma
# que você passa depois para scripts/demo-t2.ps1 -DemoPassword
```

### Subir a stack

```powershell
docker compose --profile full up --build -d
```

Isso constrói as três imagens e sobe, na ordem exigida pelas dependências, `postgres` → `migrate`
(aplica os dois SQL e sai) → `identity`/`tasks` → `gateway`. Só o `gateway` publica porta no host
(`8080:8080`, D-32) — `identity` e `tasks` só existem na rede interna do compose, sem `ports:`.

Sem o perfil `full` (`docker compose up -d postgres` ou apenas `docker compose up -d`), o comportamento
**não muda em nada** em relação a antes desta task — sobe só o Postgres, sem exigir `.env` nem o perfil
(CA-06).

Verifique com o mesmo roteiro de sempre, agora contra os containers:

```powershell
./scripts/demo-t2.ps1 -BaseUrl http://localhost:8080 -DemoPassword "<a senha de UserStore__DemoUserPassword no seu .env>"
```

Os seis passos (401/401/200/400/201/401) respondem exatamente como na seção "Rodando o T2" — mesmo
`traceId` correlacionando os logs de `gateway`, `tasks` e `identity` (`docker compose logs <serviço>`) no
par login/criação. O cenário de indisponibilidade também se reproduz da mesma forma, agora derrubando o
container em vez do processo:

```powershell
docker compose stop identity
# uma chamada autenticada a POST /api/tasks agora devolve 503 + Retry-After (D-28)
docker compose start identity
```

### Derrubar

```powershell
docker compose --profile full down
```

Mantém o volume nomeado `todolist-postgres-data` — os dados do Postgres sobrevivem entre execuções, do
mesmo jeito que já acontecia antes desta task.

### Build isolado de cada imagem

O contexto de build é a **raiz** do repositório, não a pasta do serviço — `contracts/*.proto`,
`Directory.Build.props`, `global.json` e `.editorconfig` (raiz) precisam estar no contexto para o
`dotnet restore`/`dotnet publish` os enxergarem:

```bash
docker build -f src/Identity/TodoList.Identity.Api/Dockerfile -t todolist-identity .
docker build -f src/Tasks/TodoList.Tasks.Api/Dockerfile -t todolist-tasks .
docker build -f src/Gateway/TodoList.Gateway.Api/Dockerfile -t todolist-gateway .
```

### Tamanho das imagens (CA-07)

Medido com `docker image ls` em 11/09/2026:

| Imagem | Tamanho |
|---|---|
| `todolist-identity` | ~392 MB |
| `todolist-tasks` | ~388 MB |
| `todolist-gateway` | ~346 MB |

Framework-dependent sobre `mcr.microsoft.com/dotnet/aspnet:10.0` (não self-contained, não chiseled) — ver
a nota técnica de BE-38 sobre por quê. Registro para comparação futura, não critério de tamanho máximo.

### Como isso vira o T3

As mesmas imagens construídas aqui sobem, no T3, para o **Artifact Registry** — não da máquina do aluno
(sem `gcloud` local), mas do **Cloud Shell**: um `git clone` do repositório e
`gcloud builds submit --config cloudbuild.yaml .` a partir da raiz. Não `--tag`: esse atalho exige o
`Dockerfile` na raiz do contexto, e aqui ele mora em `src/*/` — o `cloudbuild.yaml` (T3, fora do escopo
desta task) declara um passo `docker build -f <caminho>/Dockerfile` por serviço, com a raiz como contexto,
igual aos três comandos acima. No Cloud Run, `$PORT` é sempre `8080` — o mesmo valor já fixado nos
`Kestrel__Endpoints__*__Url` desta task — e os dois backends gRPC (Identity, Tasks) precisam da flag
`--use-http2` na implantação; ambos ficam privados (`--no-allow-unauthenticated`), só o Gateway é público
(D-32).
