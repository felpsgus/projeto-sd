# TodoList — Backend

Solution .NET 10 com **dois microsserviços independentes** — **Identity Service** e **Tasks Service** —
que se comunicam por gRPC (BE-25/BE-26). O Identity já expõe um servidor gRPC real (`ValidateUser`);
o Tasks ainda não chama nada (cliente/wiring ficam para BE-27/BE-28). Cada serviço
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
│   │   ├── TodoList.Identity.Infrastructure ← UserRepository, PersistedUserLookup, InMemoryUserLookup (Users/, BE-04/BE-26); IdentityDbContext + migrations (Persistence/, BE-02)
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
| Identity | gRPC (`IdentityService`) | `5081` | HTTP/2 (h2c) |
| Tasks | HTTP (só `/health`) | `5100` | HTTP/1.1 |
| Tasks | gRPC (`CreateTask`) | `5101` | HTTP/2 (h2c) |

> **Antes do primeiro `dotnet run` do Identity:** gere o par de chaves RSA (BE-40, RS256) com
> `./scripts/new-jwt-keys.ps1` e aponte `Jwt:PrivateKeyPath` para o `private.pem` gerado — sem
> ela a inicialização falha de propósito (CA-03/CA-04/CA-05/CA-06). Veja a seção
> ["Chaves JWT RS256: `private.pem`/`public.pem` nunca versionados"](#chaves-jwt-rs256-privatepempublicpem-nunca-versionados-be-40)
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

> Para o roteiro completo — Postgres, migrations na ordem certa, os três serviços e a criação de tarefa
> ponta a ponta através do Gateway — veja ["Rodando o T2"](#rodando-o-t2) mais abaixo. Os comandos
> acima sobem os serviços, mas sem banco não dá para criar tarefa.

Em ambiente de desenvolvimento, cada serviço também expõe a documentação OpenAPI/Scalar:

- Identity: <http://localhost:5080/scalar/v1> (JSON cru em `/openapi/v1.json`)
- Tasks: <http://localhost:5100/scalar/v1> (JSON cru em `/openapi/v1.json`)

## gRPC do Identity Service (BE-25/BE-26)

O contrato compartilhado vive em [`contracts/identity/v1/identity.proto`](contracts/identity/v1/identity.proto) —
único `.proto` do repositório, referenciado por caminho relativo pelos dois serviços (decisão D-29):
`TodoList.Identity.Api` gera o lado `Server`, `TodoList.Tasks.Infrastructure` gera o lado `Client`
(cujo wiring/uso real fica para BE-27/BE-28). O serviço `IdentityService` expõe, entre outros, estes RPCs:

- **`ValidateUser`** — consulta o usuário pelo id; nunca lança nem devolve erro gRPC para id
  malformado ou usuário inexistente (resposta negativa, status `OK`).
- **`Login`** (BE-33, recorte de BE-09 — D-36) — troca e-mail e senha por um access token. Exige
  `UserStore:Provider=Persisted` (ver seção seguinte). Nunca revela, por resposta ou por tempo, qual
  das causas de falha ocorreu (e-mail inexistente ou senha errada) — sempre
  `succeeded=false` sem detalhe adicional.

### Store de usuários: em memória ou persistido (BE-04)

`ValidateUser` lê de uma de duas implementações de `IUserLookup`, escolhida por
`UserStore:Provider` em configuração — trocar de store é mudar configuração, não código:

- **`InMemory`** (só em teste; o padrão é `Persisted`, D-39) — seed fixo em memória (`InMemoryUserLookup`). Roda no processo real — não
  é mock de teste — e emite um **log de aviso** na inicialização deixando explícito que o store
  persistido não está em uso. Exatamente um usuário, com id fixo:
  `10000000-0000-0000-0000-000000000001` (Ada Lovelace).

- **`Persisted`** — `PersistedUserLookup`, sobre `IUserRepository`/`IdentityDbContext` (BE-04). Cada
  chamada é uma consulta nova ao banco — sem cache — então o resultado reflete o estado real do banco
  **sem reiniciar o serviço** (CA-13 de BE-26). Como
  `PersistedUserLookup` depende do `DbContext` (`Scoped`), `IUserLookup` também é registrado como
  `Scoped` no `Program.cs` (nunca `Singleton` — criaria uma dependência cativa sobre um `DbContext`
  descartado); `InMemoryUserLookup` continua efetivamente único no processo porque ele mesmo é
  registrado como `Singleton` por baixo do wrapper `Scoped`.

### Cadastro real, não seed (Onda E, T2)

Não há usuários pré-cadastrados em nenhum ambiente. A FK cruzada `tasks.tasks.owner_id →
identity.users(id)` (nota mais abaixo) exige que o dono exista de verdade no banco: cadastre a conta
que for usar pela tela, por `curl`/Scalar (`POST /api/auth/register`, ver "gRPC do Identity Service"
acima), ou deixe que `deploy/smoke.sh` cadastre a sua própria a cada execução.


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

### Chaves JWT RS256: `private.pem`/`public.pem` nunca versionados (BE-40)

Desde BE-40 (D-38, emenda a D-31), o JWT é **RS256**, não HS256: o Identity assina com uma chave
**privada** que nunca sai dele; o Gateway valida localmente (`AddJwtBearer`) com a **pública**
correspondente. Nenhuma das duas é uma string curta em `user-secrets`/variável de ambiente — são
arquivos PEM:

| Chave | Serviço | O que é | Obrigatória |
|---|---|---|---|
| `Jwt:PrivateKeyPath` | Identity | Caminho de um PEM PKCS8, RSA ≥ 2048 bits | sim — falha a inicialização sem ela |
| `Jwt:PublicKeyPath` | Gateway | Caminho de um PEM SubjectPublicKeyInfo (a pública correspondente) | sim — falha a inicialização sem ela |

`appsettings.json` de cada serviço só declara `Jwt:Issuer`/`Jwt:Audience` (e, no Gateway, também não
declara caminho nenhum) — o caminho da chave é sempre configuração externa. **O Tasks Service não
recebe, e não deve receber, nenhuma chave `Jwt:*`** (D-38): ele não sabe nada sobre tokens; a
identidade do dono chega pela metadata gRPC `x-user-id` (D-34).

**Gerar o par localmente:**

```powershell
./scripts/new-jwt-keys.ps1
```

Grava `private.pem` e `public.pem` em `.secrets/jwt/` na raiz do repositório (pasta ignorada pelo
git — `.gitignore` tem `.secrets/` e `*.pem`), usando `System.Security.Cryptography.RSA` puro, sem
depender de `openssl` estar instalado. Não sobrescreve um par existente sem `-Force` — trocar a
chave invalida todo token já emitido (sem rotação nesta etapa, D-38).

Aponte os dois serviços para os arquivos gerados:

```powershell
dotnet user-secrets set "Jwt:PrivateKeyPath" "$PWD/.secrets/jwt/private.pem" --project src/Identity/TodoList.Identity.Api
```

```powershell
$env:Jwt__PublicKeyPath = "$PWD/.secrets/jwt/public.pem"
```

(`appsettings.Development.json` de cada serviço já aponta, por padrão, para
`../../../.secrets/jwt/{private,public}.pem` — caminho relativo resolvido contra o content root de
cada projeto — então normalmente nem é preciso configurar nada além de gerar o par uma vez.)

`./scripts/new-jwt-keys.ps1` gera o par uma vez (pula se já existir em `.secrets/jwt/`); passe
`Jwt__PrivateKeyPath`/`Jwt__PublicKeyPath` (caminhos absolutos) só aos processos de Identity e Gateway. O **Tasks Service não recebe nenhuma variável
`Jwt__*`** — uma varredura de arquitetura (CA-18 de BE-40) falha o build se `Jwt:`/`Jwt__` aparecer
em qualquer código ou `appsettings*.json` de `src/Tasks`, ou nos serviços `tasks` de
`docker-compose.yml` e `deploy/docker-compose.prod.yml`. A mesma varredura cobre o Gateway do lado oposto (CA-17): ele só
pode ter `Jwt:Issuer`, `Jwt:Audience` e `Jwt:PublicKeyPath` — nunca `Jwt:PrivateKeyPath` nem
`Jwt:SigningKey`.

> **Implantação (VM):** a chave não viaja do seu ambiente de desenvolvimento para a VM — ela é
> gerada direto lá, uma vez, por `deploy/install-docker-on-vm.sh` (`openssl genpkey`/`openssl pkey
> -pubout`, em `/etc/todolist/jwt/`), com `private.pem` `1654:1654 0400` (uid do usuário `app` da
> imagem `aspnet`) montada como `secrets:` do compose só no container do Identity, e `public.pem`
> `0444`, montada só no Gateway. Ver [`deploy/README.md`](deploy/README.md).

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
| `GET /health` e `GET /health/live` | Só que o processo está de pé — nenhuma dependência externa (`/health/live` é o alias de `/health`; os dois ficam) | Sempre `200 healthy` |
| `GET /health/ready` | Conectividade com o Postgres (`AddDbContextCheck`) | `503` (degradado) — **o processo não cai** |

```powershell
curl http://localhost:5080/health/ready   # Identity
curl http://localhost:5100/health/ready   # Tasks
```

O `/health/ready` do **Tasks** também verifica o alcance do Identity por gRPC (`ValidateUser` com um id que não
existe): Identity fora do ar é **`Degraded`** (`200` com corpo `Degraded`), não `503` — o Tasks está de pé, só não
cria tarefas (fail-closed, ADR-0006). Esse check **não** entra no probe gRPC `grpc.health.v1` usado pelo Cloud Run
(D-37), que continua olhando só o banco. O Gateway só tem liveness (`/health` e `/health/live`): ele não tem banco.

### Observabilidade: logs, `traceId` e expurgo (BE-24)

**Formato.** Os três serviços escrevem **JSON, uma linha por evento, no console** (Serilog, formato compacto com a
mensagem renderizada) — nos containers é só `docker compose logs`. Campos em toda linha:

| Campo | Conteúdo |
|---|---|
| `@t`, `@l`, `@m`, `@x` | instante, nível (ausente = `Information`), mensagem renderizada, exceção com stack trace |
| `service` | `gateway`, `identity` ou `tasks` |
| `environment`, `version` | ambiente do host e versão do assembly |
| `traceId`, `spanId` | do `Activity` corrente (W3C), mesmo valor de `@tr`/`@sp` |
| `userId` | **só** em requisição autenticada: no Gateway, o `sub` do token; no Tasks, a metadata `x-user-id`. Anônimo não tem o campo |

Cada requisição gera uma entrada (`SourceContext` `Serilog.AspNetCore.RequestLoggingMiddleware`) com
`RequestMethod`, `RequestPath`, `StatusCode` e `Elapsed` — **nunca corpo nem headers**. Chamadas gRPC entre serviços
aparecem como requisições `POST /tasks.v1.TasksService/CreateTask` etc. no servidor e como `Chamada gRPC de saída`
(RPC, `StatusCode`, duração) no chamador; o Identity registra `ValidateUser` com `exists`/`active`.
Senha, hash, access token, refresh token e `Authorization` nunca vão para o log, em nenhum nível — o teste
`LogLeakageTests` sobe Gateway e Identity reais com o log em `Verbose` e prova isso (EF Core nunca com
`EnableSensitiveDataLogging`).

**Níveis** vêm da seção `Serilog:MinimumLevel` do `appsettings.json` e se sobrescrevem por variável de ambiente
(`:` vira `__`): `Serilog__MinimumLevel__Default=Debug`, `Serilog__MinimumLevel__Override__Microsoft.EntityFrameworkCore=Information`.
`Warning` marca bloqueio de login, refresh recusado (inclusive reuso) e logout com token de outro usuário; `Error`
leva a exceção com o stack trace **no log** — o cliente recebe só o `500` genérico com `traceId`.

**Correlação.** O `traceId` do `ProblemDetails` de um erro é o mesmo `traceId` das linhas de log daquela requisição
em **todos** os serviços (o `traceparent` W3C viaja na chamada gRPC). Para investigar um erro, pegue o `traceId` do
corpo da resposta e filtre:

```powershell
docker compose logs gateway identity tasks | Select-String '"traceId":"<traceId>"'
```

**Expurgo de retenção (BE-23, ADR-0003).** Cada serviço, ao subir e a cada `Retention:IntervalHours`, apaga o que
passou do prazo. Ligado por padrão; os prazos e o lote são configuração:

| Chave | Padrão | Serviço | Efeito |
|---|---|---|---|
| `Retention:Enabled` | `true` | Identity, Tasks | `false` desliga o worker (os testes de integração rodam assim) |
| `Retention:IntervalHours` | `24` | Identity, Tasks | intervalo entre ciclos |
| `Retention:BatchSize` | `500` | Identity, Tasks | linhas por `DELETE` |
| `Tasks:SoftDeleteRetentionDays` | `30` | Tasks | tarefas removidas (soft delete) somem depois deste prazo |
| `Auth:TokenRetentionDays` | `30` | Identity | refresh tokens expirados/revogados e tentativas de login antigas |

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

## Como testar

Na raiz do repositório:

```powershell
dotnet test
```

Isso executa os seis projetos de teste. Sem Docker, use
`dotnet test --filter "Category!=Docker"`, que deixa de fora os que sobem Postgres por
Testcontainers (ver seção de persistência acima). A contagem não está escrita aqui de
propósito: todo commit a muda, e número em documentação envelhece sem avisar — quem quer
saber roda o comando:

- `TodoList.Identity.UnitTests` — testes de arquitetura (dependências entre camadas e entre serviços,
  incluindo que Domain/Application não referenciam o `.proto`/tipos gerados — CA-08 de BE-25; e que
  Application não referencia EF Core/Npgsql — CA-12 de BE-02), inspeção do modelo do `IdentityDbContext`
  (CA-13 de BE-02), varredura contra connection string versionada (CA-11 de BE-02), validação de
  configuração na inicialização, `IdentityGrpcService`/`InMemoryUserLookup`/`PersistedUserLookup` com
  `IUserLookup`/`IUserRepository` substituídos, `Email`/`User` de domínio (CA-01 a CA-09 de BE-04),
  e reflection sobre `User` (nenhum setter público — CA-07/CA-08 de BE-04).
- `TodoList.Identity.IntegrationTests` — `GET /health` (liveness) e `GET /health/ready` (readiness,
  degradado sem derrubar o processo — CA-03/CA-04 de BE-02), o servidor gRPC real subido por
  `WebApplicationFactory` invocado por um cliente gRPC de teste (`ValidateUser`,
  id malformado, inclusive com `UserStore:Provider=Persisted` contra Postgres real — CA-13 de BE-26), o
  CRUD trivial sobre SQLite in-memory exercitando o interceptor de auditoria/soft delete real
  (CA-06/CA-07/CA-08 de BE-02), as convenções de modelo (CA-05, sem Docker) e os testes
  Testcontainers/Postgres — round-trip de `timestamptz`, idempotência de migration, readiness com o banco
  de pé (CA-05/CA-02b/CA-04 de BE-02), índice único de e-mail (CA-10/CA-11 de BE-04), serialização JSON
  sem `PasswordHash` (CA-12 de BE-04), `UserRepository.GetByEmailAsync`/`EmailExistsAsync` (comparação do
  value object `Email` traduzida pelo EF contra a coluna convertida) e `PersistedUserLookup` refletindo
  desativação sem reiniciar (CA-13 de BE-26).
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

O switch `AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true)`, do
lado **cliente**, também faria a chamada funcionar, e **não é** a solução adotada: ele resolve no
consumidor um problema que é do servidor — cada novo cliente do Identity/Tasks precisaria repetir o
remendo, e um deles vai esquecer. Declarar o endpoint como `Http2` conserta para todos, de uma vez.

**Sintomas de transporte comuns:** **503** `identity.unavailable` em toda requisição (o Identity não
está no ar, ou `Identity:GrpcAddress` aponta para o lugar errado — suba o Identity primeiro e confira a
chave); erro de **protocolo** no canal gRPC (`HTTP/1.1` onde se esperava HTTP/2 — o endpoint gRPC não
está declarado `Protocols: Http2`, corrija no servidor, não no cliente); **500** no `INSERT` depois de o
Identity aprovar o dono (FK cruzada: o dono existe no store do Identity mas não em `identity.users` —
use `UserStore__Provider=Persisted` com o usuário realmente cadastrado e a migration do Identity
aplicada). O `statusCode` do log do Tasks varia entre `Unavailable` (conexão recusada de imediato) e
`DeadlineExceeded` (o prazo de `Identity:GrpcTimeoutSeconds` estourou antes); para quem chamou é o mesmo
desfecho, e nenhum detalhe de transporte (endereço, `RpcException`) aparece no corpo da resposta, só no
log do servidor.

**Outras chaves de endereço/porta/ambiente já existentes na base:**

| Chave | Serviço | Padrão em dev | Obrigatória | Variável de ambiente equivalente |
|---|---|---|---|---|
| `Kestrel:Endpoints:Http:Url` | Tasks | `http://localhost:5100` | não¹ | `Kestrel__Endpoints__Http__Url` |
| `Kestrel:Endpoints:Grpc:Url` | Tasks | `http://localhost:5101` | não¹ | `Kestrel__Endpoints__Grpc__Url` |
| `ConnectionStrings:IdentityDb` | Identity | — (nunca versionada, CA-11 de BE-02) | sim para operar o banco² | `ConnectionStrings__IdentityDb` |
| `ConnectionStrings:TasksDb` | Tasks | — (nunca versionada, CA-11 de BE-02) | sim para operar o banco² | `ConnectionStrings__TasksDb` |
| `ASPNETCORE_URLS` | ambos | — | não | já é variável de ambiente — alternativa/complemento a `Kestrel:Endpoints:*`, padrão do ASP.NET Core |
| `Tasks:MaxActivePerUser` | Tasks | `500` | não (`null` desativa o limite) | `Tasks__MaxActivePerUser` |
| `UserStore:Provider` | Identity | `Persisted` (D-39, desde BE-40) | não (tem padrão) | `UserStore__Provider` |
| `Jwt:Issuer` | Identity, Gateway | `todolist-identity` | sim | `Jwt__Issuer` |
| `Jwt:Audience` | Identity, Gateway | `todolist` | sim | `Jwt__Audience` |
| `Jwt:PrivateKeyPath` | Identity | — (nunca versionada, PEM PKCS8 RSA ≥ 2048 bits, CA-03 a CA-06 de BE-40, D-38) | sim | `Jwt__PrivateKeyPath` — ver seção acima |
| `Jwt:PublicKeyPath` | Gateway | — (nunca versionada, PEM SubjectPublicKeyInfo, CA-16 de BE-40, D-38) | sim | `Jwt__PublicKeyPath` — ver seção acima |
| `Jwt:AccessTokenMinutes` | Identity | `15` (D-02) | não (tem padrão, faixa 1–60) | `Jwt__AccessTokenMinutes` |
| `Jwt:RefreshTokenDays` | Identity | `7` (D-10) | não (tem padrão, faixa 1–90; usado só pela futura BE-10) | `Jwt__RefreshTokenDays` |

¹ Sem essa chave o Kestrel cai no próprio padrão (não escuta em `0.0.0.0`) — por isso o `appsettings.json`
base do Tasks já a declara explicitamente (BE-30), com `appsettings.Development.json` sobrepondo para
`localhost` em desenvolvimento — mesmo padrão já usado pelo Identity.
² Sem a connection string configurada o serviço **sobe normalmente**; só o readiness (`/health/ready`)
fica degradado e qualquer operação de banco falha ao ser tentada (seção de persistência acima).

### Sobrescrita por variável de ambiente: o que está automatizado e o que é roteiro manual (BE-30)

`Identity__GrpcAddress` e `Identity__GrpcTimeoutSeconds` como variável de ambiente sobrescrevendo o
`appsettings`, com efeito observável (a chamada muda de destino; o deadline muda de verdade) e a criação
de tarefa completando com o Identity num endereço não padrão, têm teste automatizado —
`GrpcIdentityGatewayIntegrationTests` e `CreateTaskCustomAddressEndToEndTests`, em
`tests/TodoList.Tasks.IntegrationTests`. O que fica como **roteiro manual** (processos `dotnet run`
reais, em portas TCP diferentes — os testes automatizados usam `WebApplicationFactory`/`TestServer`, que
não abre porta real) é o mesmo da seção "Rodando o T2", só que deslocando as portas por variável de
ambiente, sem editar nenhum `appsettings*.json` (Postgres, migrations e chaves JWT como lá):

```powershell
# terminal 1 — Identity em portas não padrão
$env:Kestrel__Endpoints__Http__Url = "http://0.0.0.0:6080"
$env:Kestrel__Endpoints__Grpc__Url = "http://0.0.0.0:6081"
dotnet run --project src/Identity/TodoList.Identity.Api

# terminal 2 — Tasks em portas não padrão, apontando para o Identity acima
$env:Kestrel__Endpoints__Http__Url = "http://0.0.0.0:6100"
$env:Kestrel__Endpoints__Grpc__Url = "http://0.0.0.0:6101"
$env:Identity__GrpcAddress = "http://localhost:6081"
dotnet run --project src/Tasks/TodoList.Tasks.Api

# terminal 3 — Gateway apontando para os dois
$env:Backends__IdentityGrpcAddress = "http://localhost:6081"
$env:Backends__TasksGrpcAddress = "http://localhost:6101"
dotnet run --project src/Gateway/TodoList.Gateway.Api
```

Depois, `DEMO_PASSWORD=... ./deploy/smoke.sh http://localhost:8080`: os passos 3 a 6 (cadastro, login,
400, 201) só passam se o Gateway alcançou o Identity e o Tasks nos endereços novos, e o 201 só se o
Tasks alcançou o Identity em `6081`.

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
  inexistente ou senha errada devolvem sempre o mesmo **401** `auth.invalid_credentials`
  (RN-AUTH-09) — o Gateway só vê `succeeded=false`, nunca a causa. Sucesso: `{ accessToken, expiresAt }`
  no corpo e o **refresh token só no cookie** `refreshToken` (`HttpOnly; Secure; SameSite=Strict;
  Path=/api/auth`, D-20) — nunca no corpo; falha nunca emite cookie. **Bloqueio (BE-12, RN-AUTH-13):** após
  5 falhas para o mesmo e-mail (normalizado, existente ou não — ADR-0002) o login responde **429**
  `auth.too_many_attempts` com `Retry-After` em segundos, mesmo com a senha correta, por 15 minutos; sem
  cookie e com `Cache-Control: no-store`. Configuração na seção `Lockout` do Identity (`Lockout__MaxAttempts`
  etc.; `Lockout__Enabled=false` desliga).
- `POST /api/auth/refresh` (anônimo, corpo vazio) — renova a sessão com o token **do cookie** e rotaciona
  o cookie (uso único, RN-AUTH-16). Qualquer falha — sem cookie, expirado, revogado, reuso — é o mesmo
  **401** `auth.invalid_refresh_token` e apaga o cookie. Reusar um token já consumido revoga a sessão inteira.
- `POST /api/auth/logout` e `POST /api/auth/logout-all` (autenticados, corpo vazio) — **204**, idempotentes,
  apagam o cookie; o primeiro encerra a sessão do cookie, o segundo todas as do usuário. O access token já
  emitido continua válido até expirar (D-41).
- `RefreshCookie:Secure` (padrão `true`; `RefreshCookie__Secure=false` em deploy HTTP puro por IP, D-42).
- `POST /api/tasks` (autenticado) — valida o payload na borda (título, descrição, prioridade, data de
  vencimento — os mesmos limites de RN-TASK-02/03/04, duplicados de propósito como defesa em profundidade)
  e traduz para `CreateTask` (Tasks); sucesso devolve **201** com `Location: /api/tasks/{id}`.
- `GET /health` (anônimo) — liveness simples, não depende de Identity/Tasks estarem de pé.
- Autenticação via `AddJwtBearer` (BE-40, D-38 — substituiu o `IdentityTokenAuthenticationHandler`
  original de BE-36): todo endpoint exige token por padrão (fallback policy); só `/health`,
  `POST /api/auth/login`, `POST /api/auth/refresh` (o cookie é a credencial; o access token já pode ter
  expirado), `POST /api/auth/register` e a documentação OpenAPI/Scalar (Development) são anônimos. O Bearer é
  validado **localmente**, com a chave pública (`Jwt:PublicKeyPath`) — o Gateway não pergunta mais
  ao Identity a cada requisição. A chave de
  assinatura continua nunca saindo do Identity (D-31/D-38); o Gateway só tem a metade que verifica,
  nunca a que assina.
- Indisponibilidade do Identity ou do Tasks nunca vira 401/400 "normal" — vira **503** com `Retry-After`
  (D-28): "não consegui perguntar" é uma causa diferente de "credencial inválida" ou "payload inválido".
- Erros de negócio do Tasks (`RpcException`) são traduzidos por `GrpcErrorMapping` (D-35): `NotFound` →
  404, `FailedPrecondition` → 409, `Unavailable`/`DeadlineExceeded` → 503, o resto → 500 genérico — sempre
  preservando o `errorCode` do trailer gRPC em `extensions.errorCode` do `ProblemDetails`, o mesmo formato
  que o front já consumia de Identity/Tasks.

### Portas e configuração

O Gateway escuta em **uma única porta HTTP/1**, `8080` (`http://0.0.0.0:8080` em
desenvolvimento/containers, `http://localhost:8080` em Development) — D-37, a mesma porta que
vira `$PORT` no Cloud Run (T3).

> **Na VM do T2 (BE-42)** o Gateway não publica porta nenhuma: ele escuta em `0.0.0.0:8080` só
> dentro da rede do compose, e quem o alcança é o container `frontend` (nginx), a única origem
> HTTP pública (porta 80). Ver [`deploy/README.md`](deploy/README.md) para a topologia completa.

Chaves de configuração (`appsettings.json`/`appsettings.Development.json`, validadas com `ValidateOnStart`
— endereço ausente ou que não é URI absoluta derruba a inicialização, nunca a primeira requisição):

| Chave | Padrão | O que é |
|---|---|---|
| `Backends:IdentityGrpcAddress` | `http://localhost:5081` | Endereço gRPC (h2c local) do Identity Service |
| `Backends:TasksGrpcAddress` | `http://localhost:5101` | Endereço gRPC (h2c local) do Tasks Service |
| `Backends:IdentityGrpcTimeoutSeconds` | `2` | Deadline de `Login` |
| `Backends:TasksGrpcTimeoutSeconds` | `5` | Deadline de `CreateTask` — maior que o do Identity porque o Tasks faz, dentro dele, uma chamada aninhada ao Identity com deadline próprio de 2s |
| `Jwt:Issuer` / `Jwt:Audience` | `todolist-identity` / `todolist` | Já vêm em `appsettings.json` — mesmo valor do Identity |
| `Jwt:PublicKeyPath` | — (obrigatória, sem padrão, BE-40/D-38) | Caminho do PEM SubjectPublicKeyInfo usado por `AddJwtBearer` para validar a assinatura RS256 localmente — ver seção de chaves acima |

### Como subir localmente (Identity + Tasks + Gateway)

Pré-requisitos (Postgres, migrations, chaves JWT e as variáveis de ambiente de cada processo) estão em
["Rodando o T2"](#rodando-o-t2) — sem eles o Identity e o Gateway não sobem. Com isso feito:

```powershell
# terminal 1 — Identity (gRPC em 5081)
dotnet run --project src/Identity/TodoList.Identity.Api

# terminal 2 — Tasks (gRPC em 5101)
dotnet run --project src/Tasks/TodoList.Tasks.Api

# terminal 3 — Gateway (REST em 8080), apontando para os dois acima (valores padrão de appsettings.Development.json)
dotnet run --project src/Gateway/TodoList.Gateway.Api
```

Não há usuário pré-cadastrado: cadastre, faça login e crie uma tarefa via `curl`:

```bash
# cadastro — não existe seed (ver "Cadastro real, não seed")
curl -s -X POST http://localhost:8080/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"email":"voce@todolist.example","password":"<senha, 8+ caracteres, letra e número>","displayName":"Voce"}'
# => 201

# login — troca e-mail/senha pelo access token; -c guarda o cookie refreshToken (HttpOnly) no cookiejar.
# Se o seu curl não reenviar o cookie Secure por http://, suba o Gateway com RefreshCookie__Secure=false.
curl -s -c cookiejar -X POST http://localhost:8080/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"voce@todolist.example","password":"<a mesma senha>"}'
# => 200 { "accessToken": "...", "expiresAt": "..." }   + Set-Cookie: refreshToken=...; HttpOnly; Secure; SameSite=Strict; Path=/api/auth

# refresh — corpo vazio, o token vem só do cookie; -b envia o jar e -c grava o cookie rotacionado
curl -s -b cookiejar -c cookiejar -X POST http://localhost:8080/api/auth/refresh
# => 200 { "accessToken": "...", "expiresAt": "..." }   (repetir com o cookie antigo => 401 auth.invalid_refresh_token)

# logout — autenticado, encerra a sessão do cookie e apaga o cookie (204)
curl -s -b cookiejar -c cookiejar -X POST http://localhost:8080/api/auth/logout \
  -H "Authorization: Bearer <accessToken>"
# => 204. Depois: o refresh com o mesmo cookiejar => 401

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
    -ContentType "application/json" -Body '{"email":"voce@todolist.example","password":"<a mesma senha>"}'

Invoke-RestMethod -Method Post -Uri http://localhost:8080/api/tasks `
    -ContentType "application/json" -Headers @{ Authorization = "Bearer $($login.accessToken)" } `
    -Body '{"title":"Comprar leite","priority":"Medium","dueDate":"2026-12-31"}'
```

O roteiro completo de demonstração (cenários de sucesso e falha — 400/401/503) é o de BE-39, em
"Rodando o T2"; esta seção cobre só como subir e um exemplo mínimo de fumaça. Ainda não existe pipeline
de CI (BE-24), nem a varredura por `JOIN`/nome de schema entre serviços (CA-14 de BE-02) que entra com
ele.

## Rodando o T2

Roteiro de verificação de ponta a ponta do T2 (BE-39): subir Postgres + os **três** serviços — Identity,
Tasks, API Gateway — e ver os quatro desfechos exigidos pelo enunciado do T2 (`t2.md`: 401, 400, 201, e
REST → gRPC) acontecendo contra o Gateway, com o mesmo `traceId` correlacionando os logs dos três
serviços. Os trechos de log abaixo são saída literal de uma execução real, não exemplo escrito à mão.

### 0. O atalho: a stack em container e um script

```bash
docker compose --profile full up -d     # Postgres + migrate + os quatro serviços (pré-requisitos: seção "Rodando em containers")
DEMO_PASSWORD="<qualquer senha de desenvolvimento, 8+ caracteres, letra e número>" ./deploy/smoke.sh http://localhost
```

Onda E (T2): não existe mais seed de demonstração — `deploy/smoke.sh` cadastra sua própria conta a cada
execução (`POST /api/auth/register`), então a senha acima não precisa ser a mesma entre execuções.

`deploy/smoke.sh` aceita a URL base como argumento — é o mesmo script que roda no dia da apresentação,
apontado para o IP externo da VM (BE-39 CA-04, verificação **de fora**, não de `127.0.0.1` dentro da
própria VM; do notebook, por Git Bash). Ver [`deploy/README.md`](deploy/README.md) para o runbook
completo de implantação e o roteiro cronometrado de apresentação.

### 1. Pré-requisitos, chaves JWT e Postgres

Postgres de desenvolvimento por `docker compose`, migrations do Identity **antes** das do Tasks (a FK
cruzada `tasks.tasks.owner_id → identity.users(id)` exige isso) e, desde BE-40, o par de chaves RS256
(seção "Chaves JWT RS256" acima):

```powershell
./scripts/new-jwt-keys.ps1    # gera .secrets/jwt/{private,public}.pem — pula se já existir

docker compose up -d

$env:ConnectionStrings__IdentityDb = "Host=localhost;Port=5432;Database=todolist;Username=postgres;Password=postgres"
$env:ConnectionStrings__TasksDb    = "Host=localhost;Port=5432;Database=todolist;Username=postgres;Password=postgres"

dotnet ef database update --project src/Identity/TodoList.Identity.Infrastructure --startup-project src/Identity/TodoList.Identity.Api
dotnet ef database update --project src/Tasks/TodoList.Tasks.Infrastructure --startup-project src/Tasks/TodoList.Tasks.Api
```

### 2. Subir os três processos, na ordem

Identity primeiro (emite o token e valida o dono), Tasks depois (só alcançável por gRPC — BE-35
removeu o gatilho REST provisório), Gateway por último (é quem chama os outros dois).

```powershell
# terminal 1 — Identity: gRPC em 5081, REST (só /health) em 5080
$env:ConnectionStrings__IdentityDb = "Host=localhost;Port=5432;Database=todolist;Username=postgres;Password=postgres"
$env:UserStore__Provider = "Persisted"
$env:Jwt__PrivateKeyPath = "$PWD/.secrets/jwt/private.pem"
dotnet run --project src/Identity/TodoList.Identity.Api
```

```powershell
# terminal 2 — Tasks: gRPC em 5101, REST (só /health) em 5100 — nenhuma variável Jwt:* (D-38)
$env:ConnectionStrings__TasksDb = "Host=localhost;Port=5432;Database=todolist;Username=postgres;Password=postgres"
dotnet run --project src/Tasks/TodoList.Tasks.Api
```

```powershell
# terminal 3 — Gateway: REST em 8080 — a única borda pública (D-32)
$env:Jwt__PublicKeyPath = "$PWD/.secrets/jwt/public.pem"
dotnet run --project src/Gateway/TodoList.Gateway.Api
```

Sessão (Fase 4): o login também emite o cookie `refreshToken` (`HttpOnly`, `Path=/api/auth`) e existem
`POST /api/auth/refresh`, `/logout` e `/logout-all` — roteiro com `curl -c/-b cookiejar` em "Como subir
localmente" (seção do Gateway). A migration `AddRefreshTokens` cria `identity.refresh_tokens`: aplique-a
(`dotnet ef database update`, Identity antes do Tasks) ou gere o SQL idempotente com
`scripts/new-migrations-sql.ps1`. Numa VM em HTTP puro, o Gateway precisa de `RefreshCookie__Secure=false`.

Nenhum usuário pronto para logar — cadastre um pelo Scalar/`curl` (`POST /api/auth/register`) ou deixe
que o próprio `deploy/smoke.sh` cadastre o dele (próxima seção).

### 3. `deploy/smoke.sh`

```bash
# Gateway sozinho, sem nginx: o passo 0 (rota do SPA) é esperado falhar — quem serve o Angular é o nginx
DEMO_PASSWORD="<qualquer senha de desenvolvimento>" ./deploy/smoke.sh http://localhost:8080
```

O script imprime uma linha `OK`/`ERRO` por passo e, no fim, o comando de log para achar o `traceId` do
passo 6. Os passos: 0 rota profunda do SPA; 1 e 2 sem token e com token lixo (401); 3 e 4 cadastro e login
da conta nova; 2b token adulterado (401); 5 título vazio (400); 6 criação válida (201 + `Location`).
Qualquer passo fora do esperado faz o script sair com `1` (BE-39 CA-02).

### 4. Evidência de log: o mesmo `traceId` nos serviços envolvidos (passo 6)

> **Nota (BE-40).** O trecho de log abaixo é da execução do T2 **antes** de BE-40 — capturado
> quando o Gateway ainda perguntava `ValidateToken` ao Identity por gRPC a cada requisição (D-31
> original). Desde BE-40 (D-38), essa chamada **não existe mais** e o RPC `ValidateToken` foi
> **removido do contrato** do Identity (não só deixou de ser chamado): o Gateway valida o JWT
> localmente com `AddJwtBearer` e a chave pública, sem round-trip de rede — é exatamente o que o
> requisito de middleware do T2 exige. A cadeia de chamadas gRPC de um `POST /api/tasks`
> autenticado passa a ser só **Gateway → Tasks (`CreateTask`)** e, dentro dela, **Tasks →
> Identity (`ValidateUser`)** — duas paradas de log, não três. Esta seção fica pendente de
> recaptura com uma execução real pós-BE-40 (mesmo espírito do "[PENDENTE — medir...]" já registrado
> em `deploy/README.md`); a evidência abaixo continua válida para entender o formato do `traceId`
> correlacionado, só a contagem de saltos que mudou.

O par sucesso é o par login (passo 4) + criação (passo 6). Abaixo, os logs reais desse passo, de uma
execução anterior a BE-40 — a mesma requisição atravessava Gateway → Identity (`ValidateToken`),
Gateway → Tasks (`CreateTask`), e Tasks → Identity (`ValidateUser`), e as **três** paradas
carregavam o mesmo `traceId` (`00-2d5590b724dcaf24a7e19108dbc1d95c-...`):

```text
# terminal 3 (Gateway) — ATÉ BE-40: chamava ValidateToken; hoje: valida local, sem esta linha
info: TodoList.Gateway.Api.Backends.IdentityBackend[432667118]
      Chamada gRPC de saída: backend=Identity, rpc=ValidateToken, statusCode=OK, durationMs=52.2405,
      traceId=00-2d5590b724dcaf24a7e19108dbc1d95c-5b80ebee58185f18-00
info: TodoList.Gateway.Api.Backends.TasksBackend[432667118]
      Chamada gRPC de saída: backend=Tasks, rpc=CreateTask, statusCode=OK, durationMs=820.0457,
      traceId=00-2d5590b724dcaf24a7e19108dbc1d95c-5b80ebee58185f18-00

# terminal 1 (Identity) — ATÉ BE-40: respondia ValidateToken (Gateway) e ValidateUser (Tasks);
# hoje: só ValidateUser — ValidateToken foi removido do contrato
info: TodoList.Identity.Api.Grpc.IdentityGrpcService[1049219497]
      ValidateToken: valid=True, durationMs=33.7856,
      traceId=00-2d5590b724dcaf24a7e19108dbc1d95c-5b80ebee58185f18-00
info: TodoList.Identity.Api.Grpc.IdentityGrpcService[1561142135]
      ValidateUser: userId=10000000-0000-0000-0000-000000000001, exists=True, durationMs=15.7272,
      traceId=00-2d5590b724dcaf24a7e19108dbc1d95c-ed399fb3ceb26372-00

# terminal 2 (Tasks) — chamou o Identity de novo (ValidateUser) antes de gravar — não muda com BE-40
info: TodoList.Tasks.Infrastructure.Identity.GrpcIdentityGateway[1561142135]
      ValidateUser (Identity gRPC): userId=10000000-0000-0000-0000-000000000001, statusCode=OK, durationMs=126.395,
      traceId=00-2d5590b724dcaf24a7e19108dbc1d95c-ed399fb3ceb26372-00
info: TodoList.Tasks.Api.Grpc.TasksGrpcService[1138808421]
      CreateTask: ownerId=10000000-0000-0000-0000-000000000001, statusCode=OK, durationMs=698.8348,
      traceId=00-2d5590b724dcaf24a7e19108dbc1d95c-ed399fb3ceb26372-00
```

Note os **dois** pares de `traceId` (mesmo prefixo de 32 caracteres — o trace W3C — com dois sufixos de
span diferentes): um para o salto Gateway→Tasks, outro para o salto interno Tasks→Identity, que o Tasks
abre como filho da chamada que recebeu. Até BE-40 havia um terceiro salto (Gateway→Identity,
`ValidateToken`) compartilhando o primeiro `traceId`; desde BE-40 esse salto não existe mais — a
validação de assinatura é local, sem chamada de rede —, então a cadeia observável em produção passa a
ter dois saltos gRPC, não três. É essa correlação de `traceId` entre Gateway e Tasks/Identity, qualquer
que seja o número de saltos, que a comunicação REST→gRPC do T2 precisa deixar auditável (nota técnica
de BE-39).

Contra a VM, o comando equivalente para achar essas linhas é:

```bash
sudo docker compose -f /opt/todolist/docker/docker-compose.prod.yml --env-file /opt/todolist/docker/.env \
  logs --since 2m | grep -E 'CreateTask|ValidateUser'
```

### 5. Caminho de falha controlada: Identity fora do ar → 503, nunca 401

Encerre o Identity (`Ctrl+C` no terminal 1, ou `docker compose ... stop identity` na VM) e repita a
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

Verificado com o Identity de fato encerrado (BE-39 CA-07, CA-23 de BE-40): nunca `401` nem `500`.
**A causa mudou com BE-40 (D-38), o status observado não**: até BE-40, o 503 vinha do Gateway não
conseguir perguntar `ValidateToken` ao Identity ("não consegui validar o token"); desde BE-40, o
Gateway valida o token sozinho — com o Identity fora do ar, um token válido emitido antes da queda
ainda passa pela validação local e a chamada chega ao Tasks, que devolve 503 ao tentar `ValidateUser`
contra um Identity que não responde ("não consegui confirmar o dono"). Para verificá-la localmente, pare o Identity (`docker compose stop identity`) e repita um `POST /api/tasks`
com token válido.

### 6. Requisito do `t2.md` → passo do roteiro → evidência (BE-39 CA-01)

| Requisito de `t2.md` | Passo do roteiro | Evidência |
|---|---|---|
| **1. REST público** — pelo menos um endpoint REST adequado ao tema | Passos 3, 4 e 6 (`POST /api/auth/register`, `POST /api/auth/login`, `POST /api/tasks`) contra `http://<host>:8080` | Seção 3 acima — respostas HTTP reais do Gateway, porta única 8080 |
| **2. Validação na borda** — 400 em payload inválido, 201 em sucesso | Passo 5 (título vazio → 400) e passo 6 (título válido → 201 + `Location`) | Seção 3 acima — `CreateTaskHttpRequestValidator` rejeita antes de qualquer chamada gRPC (seção "O que ele faz" da API Gateway acima) |
| **3. Segurança** — 401 com token ausente ou inválido | Passos 1 (sem token) e 2 (token lixo) — dois caminhos de código distintos no middleware (CA-05) | Seção 3 acima — `AddJwtBearer` (BE-40, D-38, substitui `IdentityTokenAuthenticationHandler`), mesmo corpo `auth.unauthorized` nos dois — validado localmente com a chave pública, sem round-trip ao Identity, atendendo ao requisito 6 do enunciado ("middleware de autenticação configurado no próprio Gateway") |
| **4. Tradução e delegação de protocolo** — JSON → gRPC binário para o backend | Passo 6, evidência de log | Seção 4 acima — `traceId` correlacionado em Gateway (`CreateTask`), Tasks (`CreateTask`/`ValidateUser (Identity gRPC)`) e Identity (`ValidateUser`); a validação do JWT deixou de ser uma chamada gRPC do Gateway ao Identity desde BE-40 |

O caminho de indisponibilidade
(seção 5) não mapeia para um requisito numerado do enunciado, mas é exigido pelo BE-39 (CA-07; o
indisponível se reproduz à mão, o `smoke.sh` não o derruba) — a diferença entre "credencial errada" e "não consigo checar" é o tipo de bug que só aparece
na primeira demonstração real, não em revisão de código.

## Frontend (Angular) em desenvolvimento local (BE-42)

O recorte do T2 exige frontend obrigatório, falando só com o Gateway — código em
[`frontend/`](frontend/README.md) (Angular, standalone, zoneless). Ver
[`frontend/README.md`](frontend/README.md) para o roteiro completo (instalar, testar, lint,
build); aqui vai só o essencial para rodar junto do backend local:

```bash
cd frontend
npm ci
npm start          # abre em http://localhost:4200
```

`frontend/proxy.conf.json` encaminha `/api/*` para `http://localhost:8080` (o Gateway) —
**é o `ng serve` que faz, em dev, o papel que o nginx faz na VM (BE-42, D-40): a mesma
origem, sem CORS.** É por isso que o código do frontend só usa caminhos relativos
(`/api/...`), nunca uma URL absoluta do Gateway — o mesmo bundle funciona sem alteração
atrás do nginx (VM) ou atrás do `ng serve` (dev local).

Com o Gateway já no ar (seção "Rodando o T2" acima) e o `ng serve` rodando, a tela de login
em `http://localhost:4200` já fala de ponta a ponta com Identity/Tasks. O build de produção
(`npm run build`, gera `dist/frontend/browser/`) é compilado dentro da imagem `todolist-frontend`
(`frontend/Dockerfile`, nginx), publicada por `scripts/publish-images.ps1` — ver
[`deploy/README.md`](deploy/README.md) para o runbook de
implantação e a topologia completa (nginx na porta 80, demais serviços só na rede do compose).

## Rodando em containers (BE-38)

Preparo do T3 (Artifact Registry + Cloud Run) feito ainda no T2: cada serviço ganhou um `Dockerfile`
(`src/Identity/TodoList.Identity.Api/Dockerfile`, `src/Tasks/TodoList.Tasks.Api/Dockerfile`,
`src/Gateway/TodoList.Gateway.Api/Dockerfile`), e a stack inteira sobe com `docker compose` na máquina de
desenvolvimento. Isso é **adicional** ao fluxo de `dotnet run` da seção "Rodando o T2" acima — não o
substitui. A mesma stack, com imagens do Artifact Registry e o banco no Cloud SQL
(`deploy/docker-compose.prod.yml`), é a que a VM do GCP roda na apresentação do T2.

### Pré-requisito: os SQL de migration

O serviço `migrate` do compose (abaixo) aplica os mesmos scripts SQL idempotentes que a VM usa, gerados por
`scripts/new-migrations-sql.ps1`:

```powershell
./scripts/new-migrations-sql.ps1
```

Isso produz `artifacts/sql/01-identity.sql` e `artifacts/sql/02-tasks.sql` — sem eles, o serviço `migrate`
falha explicitamente com uma mensagem apontando para este comando (em vez de subir "vazio" e mascarar o
problema).

### Segredos: chaves JWT

O Identity não tem nenhum segredo de texto puro próprio nesta stack. A connection string inline no
`docker-compose.yml` é a credencial de desenvolvimento local de sempre (`postgres`/`postgres`, CA-11 de
BE-02), não um segredo real. Cadastre contas pela API/tela ou por `deploy/smoke.sh`.

**Chaves JWT RS256 (BE-40, D-38) — pré-requisito do perfil `full`.** O `docker-compose.yml` as monta via
`secrets:` de nível superior, a partir de `.secrets/jwt/{private,public}.pem` (mesma pasta usada
pelo fluxo `dotnet run`, ignorada pelo git). Gere o par antes de subir a stack — se você já rodou
`scripts/new-jwt-keys.ps1` antes, ele já existe e este passo é um no-op:

```powershell
./scripts/new-jwt-keys.ps1
```

`identity` recebe só `jwt_private` (`Jwt__PrivateKeyPath=/run/secrets/jwt_private`); `gateway`
recebe só `jwt_public` (`Jwt__PublicKeyPath=/run/secrets/jwt_public`); `tasks` não recebe nenhuma.
Sem Swarm, um secret de arquivo do compose é um bind mount comum — o arquivo aparece em
`/run/secrets/<nome>` dentro do container com a permissão do arquivo no host, e os três serviços já
rodam como usuário não-root (`$APP_UID`, ver `Dockerfile` de cada um).

### Subir a stack

```powershell
docker compose --profile full up --build -d
```

Isso constrói as imagens e sobe, na ordem exigida pelas dependências, `postgres` → `migrate`
(aplica os dois SQL e sai) → `identity`/`tasks` → `gateway` → `frontend`. Só o `frontend` (nginx)
publica porta no host (`80:8080`, a origem única — D-32/D-40): `gateway`, `identity` e `tasks` só
existem na rede interna do compose, sem `ports:`, e o nginx repassa `/api/*` ao Gateway.

Sem o perfil `full` (`docker compose up -d postgres` ou apenas `docker compose up -d`), o comportamento
**não muda em nada** em relação a antes desta task — sobe só o Postgres, sem exigir o perfil (CA-06).

Verifique com o mesmo roteiro de sempre, agora contra os containers (pela porta 80, a única exposta):

```bash
DEMO_PASSWORD="<qualquer senha de desenvolvimento>" ./deploy/smoke.sh http://localhost
```

Os passos respondem exatamente como na seção "Rodando o T2" — mesmo
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

## CI e gates

`.github/workflows/ci.yml` roda em todo PR e em push na `main` (cancela execuções antigas do mesmo ref). Jobs:

| Job | O que roda |
|---|---|
| `backend` | restore, build (warnings = erro), `dotnet format --verify-no-changes`, testes com cobertura + gate, `check-vulnerable.ps1` |
| `frontend` | `npm ci`, lint, `format:check`, `test:coverage` (pisos no `vitest.config.ts`), build de produção, sem `*.map` em `dist/`, `npm audit --audit-level=high` |
| `e2e` | `playwright install chromium`, `npm run e2e:stack`; em falha publica report/trace/vídeo e `docker compose logs` |
| `secrets` | gitleaks (imagem oficial) sobre o histórico inteiro; config em `.gitleaks.toml` |

Firefox e WebKit não rodam no CI (hoje falham: 1 teste no Firefox, 12 no WebKit, não investigados); seguem disponíveis localmente por `npm run e2e:all`.

O resumo de cobertura aparece no *Job summary* do PR e o relatório completo vai como artefato.

Cada gate roda localmente:

```powershell
./scripts/coverage.ps1                 # backend: testes + cobertura -> coverage-report/index.html -> gate
./scripts/coverage.ps1 -Filter "Category!=Docker"   # sem Docker
./scripts/check-vulnerable.ps1         # NuGet High/Critical (direto e transitivo)
cd frontend; npm run test:coverage     # cobertura do frontend (falha abaixo dos pisos)
cd frontend; npm audit --audit-level=high
cd frontend; npm run e2e:stack         # sobe o compose (perfil full) e roda o Playwright
docker run --rm -v "${PWD}:/repo" ghcr.io/gitleaks/gitleaks:v8.30.1 git /repo --no-banner --redact
```

**Pisos de cobertura.** Backend: linhas globais >= 75% e `*.Domain` + `*.Application` agregados >= 85%
(parâmetros `-MinLine`/`-MinDomainApplication` de `scripts/check-coverage.ps1`). Frontend: linhas >= 75% global e
>= 80% em `core/`, stores, guards e interceptors. **Exclusões do backend** (`coverlet.runsettings`): `Program.cs`,
`Migrations/`, `obj/**`, namespaces `TodoList.Contracts.*` (gerado pelo Grpc.Tools), código marcado com
`GeneratedCode`/`CompilerGenerated`/`ExcludeFromCodeCoverage` e os assemblies de teste. Não amplie a lista para
passar no gate: escreva o teste. O relatório separa os assemblies (Identity.*, Tasks.*, Gateway, SharedKernel.*).

Pendente: sinalizar queda de cobertura em relação à `main` (exige guardar o resumo da `main` como artefato).
Requer `dotnet tool restore` (ReportGenerator e `dotnet-ef` ficam em `.config/dotnet-tools.json`).
