# TodoList distribuído

Três serviços .NET 10 e um frontend Angular, atrás de nginx na mesma origem:

```
frontend (Angular + nginx, porta 80)  →  Gateway (REST, única borda pública)
                                           ├─ gRPC →  Identity  (usuários, login, JWT RS256)
                                           └─ gRPC →  Tasks     (CRUD de tarefas)
                                                        Postgres: schema identity + schema tasks
```

Por serviço: `Api → Infrastructure → Application → Domain → SharedKernel`. Identity e Tasks não se
referenciam; compartilham só `contracts/*/v1/*.proto` e o `SharedKernel`. O Gateway é um projeto web
único e conhece os backends só pelos `.proto`.

```
contracts/          identity/v1, tasks/v1 (.proto)
src/Shared/         TodoList.SharedKernel
src/Identity/       Domain, Application, Infrastructure, Api
src/Tasks/          Domain, Application, Infrastructure, Api
src/Gateway/        TodoList.Gateway.Api
frontend/           Angular (ver frontend/README.md)
tests/              unit + integration por serviço
deploy/             VM do GCP, smoke.sh, demo.sh (ver deploy/README.md)
scripts/            chaves JWT, SQL de migration, cobertura, publicação de imagens
```

## Pré-requisitos

- .NET SDK 10.0.202 ([global.json](global.json)) e `dotnet tool restore` (traz `dotnet-ef` e ReportGenerator)
- Docker
- Node (para o frontend)

## Início rápido: stack em containers

```powershell
./scripts/new-jwt-keys.ps1          # .secrets/jwt/{private,public}.pem (ignorado pelo git); pula se já existir
./scripts/new-migrations-sql.ps1    # artifacts/sql/01-identity.sql e 02-tasks.sql, usados pelo serviço migrate
docker compose --profile full up --build -d
```

Sobe `postgres` → `migrate` → `identity`/`tasks` → `gateway` → `frontend`. Só o `frontend` publica porta
(http://localhost); o resto vive na rede interna do compose. Sem `--profile full`, sobe só o Postgres.

Não há usuários pré-cadastrados: cadastre pela tela ou deixe o smoke test criar a conta dele:

```bash
DEMO_PASSWORD="<8+ caracteres, letra e número>" ./deploy/smoke.sh http://localhost
```

Uma linha `OK`/`ERRO` por passo (rota do SPA, 401 sem token e com token inválido, cadastro, login, 400,
201); sai com `1` se algum passo divergir. Derrubar: `docker compose --profile full down` (o volume
`todolist-postgres-data` fica).

## Desenvolvimento local (`dotnet run`)

**1. Chaves, Postgres e migrations — Identity sempre antes do Tasks** (a FK `tasks.tasks.owner_id →
identity.users(id)` exige o schema `identity`; inverter falha com `esquema "identity" não existe`).

```powershell
./scripts/new-jwt-keys.ps1
docker compose up -d

$env:ConnectionStrings__IdentityDb = "Host=localhost;Port=5432;Database=todolist;Username=postgres;Password=postgres"
$env:ConnectionStrings__TasksDb    = "Host=localhost;Port=5432;Database=todolist;Username=postgres;Password=postgres"

dotnet ef database update --project src/Identity/TodoList.Identity.Infrastructure --startup-project src/Identity/TodoList.Identity.Api
dotnet ef database update --project src/Tasks/TodoList.Tasks.Infrastructure --startup-project src/Tasks/TodoList.Tasks.Api
```

> O `dotnet ef` **não lê user-secrets**: as fábricas de design-time leem só a variável de ambiente e,
> sem ela, caem na credencial local do `docker-compose.yml`. Para migrar outro banco, exporte a variável.

Nova migration: `dotnet ef migrations add <Nome> --project src/<Svc>/TodoList.<Svc>.Infrastructure
--startup-project src/<Svc>/TodoList.<Svc>.Api --output-dir Migrations`.

**2. Os três processos, em terminais separados, nesta ordem.** Cada terminal precisa da sua variável de
connection string (ou de `dotnet user-secrets set "ConnectionStrings:IdentityDb" ...` uma vez por projeto).
Em `Development`, os caminhos das chaves JWT já apontam para `.secrets/jwt/`.

```powershell
dotnet run --project src/Identity/TodoList.Identity.Api   # REST 5080 (só /health), gRPC 5081
dotnet run --project src/Tasks/TodoList.Tasks.Api         # REST 5100 (só /health), gRPC 5101
dotnet run --project src/Gateway/TodoList.Gateway.Api     # REST 8080
```

**3. Frontend:**

```bash
cd frontend; npm ci; npm start      # http://localhost:4200
```

O `ng serve` encaminha `/api/*` para o Gateway (`frontend/proxy.conf.json`), fazendo o papel do nginx:
a mesma origem, sem CORS. Por isso o frontend só usa caminhos relativos.

Smoke test sem nginx: `DEMO_PASSWORD=... ./deploy/smoke.sh http://localhost:8080` (o passo da rota do
SPA falha, como esperado). Documentação da API em Development: `http://localhost:8080/scalar/v1`.

## Configuração

Configuração tipada (`IOptions<T>` + `ValidateOnStart`): chave obrigatória ausente ou inválida derruba a
inicialização com a chave no texto. Endereço, porta e protocolo nunca são literais em código. Qualquer
chave se sobrescreve por variável de ambiente trocando `:` por `__`.

| Chave | Serviço | Padrão (dev) | Observação |
|---|---|---|---|
| `ConnectionStrings:IdentityDb` / `TasksDb` | Identity / Tasks | — | nunca versionada; sem ela o serviço sobe, só `/health/ready` degrada |
| `Kestrel:Endpoints:Http:Url` | todos | 5080 / 5100 / 8080 | |
| `Kestrel:Endpoints:Grpc:Url` | Identity, Tasks | 5081 / 5101 | protocolo `Http2` obrigatório (h2c sem TLS) |
| `Identity:GrpcAddress`, `Identity:GrpcTimeoutSeconds` | Tasks | `http://localhost:5081`, `2` | |
| `Backends:IdentityGrpcAddress` / `TasksGrpcAddress` | Gateway | `:5081` / `:5101` | |
| `Backends:IdentityGrpcTimeoutSeconds` / `TasksGrpcTimeoutSeconds` | Gateway | `2` / `5` | o do Tasks é maior: ele chama o Identity dentro |
| `Jwt:PrivateKeyPath` | **só** Identity | `.secrets/jwt/private.pem` | PEM PKCS8, RSA ≥ 2048 |
| `Jwt:PublicKeyPath` | **só** Gateway | `.secrets/jwt/public.pem` | o Tasks não recebe nenhuma chave `Jwt:*` (teste de arquitetura) |
| `Jwt:Issuer`, `Jwt:Audience` | Identity, Gateway | `todolist-identity`, `todolist` | |
| `Jwt:AccessTokenMinutes`, `Jwt:RefreshTokenDays` | Identity | `15`, `7` | |
| `RefreshCookie:Secure` | Gateway | `true` | `false` em deploy HTTP puro por IP |
| `UserStore:Provider` | Identity | `Persisted` | `InMemory` só em teste; não suporta login |
| `Lockout:*` | Identity | 5 tentativas / 15 min | `Lockout__Enabled=false` desliga |
| `Tasks:MaxActivePerUser` | Tasks | `500` | `null` desativa |
| `Retention:Enabled` / `IntervalHours` / `BatchSize` | Identity, Tasks | `true` / `24` / `500` | expurgo de soft delete e tokens |
| `Serilog:MinimumLevel:*` | todos | — | ex.: `Serilog__MinimumLevel__Default=Debug` |

Health: `/health` (liveness, sempre 200) e `/health/ready` (Postgres; no Tasks também o alcance do
Identity, que fora do ar dá `Degraded`). O Gateway só tem liveness.

## Problemas comuns

| Sintoma | Causa |
|---|---|
| Identity ou Gateway não sobe | chave JWT ausente: rode `./scripts/new-jwt-keys.ps1` |
| `503` + `Retry-After` (`identity.unavailable` / `tasks.unavailable`) | backend fora do ar ou endereço errado. É fail-closed de propósito (ADR-0006): nunca vira 401 |
| erro de protocolo no canal gRPC | endpoint gRPC sem `Protocols: Http2`. Corrija no servidor, não com `Http2UnencryptedSupport` no cliente |
| `500` no `INSERT` de tarefa | dono não existe em `identity.users` (FK cruzada): conta não cadastrada ou migration do Identity não aplicada |
| `esquema "identity" não existe` | migrations do Tasks rodadas antes das do Identity |
| `migrate` do compose falha | faltam os SQL: `./scripts/new-migrations-sql.ps1` |
| refresh devolve 401 via `curl` em `http://` | cookie `Secure`: suba o Gateway com `RefreshCookie__Secure=false` |

## Logs e `traceId`

JSON compacto, uma linha por evento, no console (Serilog). O `traceId` do `ProblemDetails` de um erro é
o mesmo das linhas de log da requisição em todos os serviços. Nunca aparecem senha, hash, token ou
`Authorization` (o `LogLeakageTests` garante).

```bash
docker compose logs --no-log-prefix gateway tasks identity | grep '"<traceId>"'
docker compose logs --no-log-prefix gateway tasks identity | grep -E 'CreateTask|ValidateUser'
```

Na VM, ver [deploy/README.md](deploy/README.md) §10.

## Testes, estilo e CI

```powershell
dotnet build                               # sem warnings (analyzers = erro)
dotnet test                                # inclui Testcontainers (precisa de Docker)
dotnet test --filter "Category!=Docker"    # sem Docker
$env:XUNIT_ORDER_SEED = "<semente>"; dotnet test   # reproduz a ordem aleatória impressa no início
dotnet format --verify-no-changes

./scripts/coverage.ps1                     # testes + cobertura + gate (coverage-report/index.html)
./scripts/check-vulnerable.ps1             # NuGet High/Critical

cd frontend; npm test; npm run lint; npm run test:coverage; npm audit --audit-level=high
cd frontend; npm run e2e:stack             # sobe o compose (perfil full) e roda o Playwright
docker run --rm -v "${PWD}:/repo" ghcr.io/gitleaks/gitleaks:v8.30.1 git /repo --no-banner --redact
```

O `.github/workflows/ci.yml` roda esses gates em todo PR e em push na `main`, com os jobs `backend`,
`frontend`, `e2e` (só Chromium) e `secrets`.

**Pisos de cobertura:**
- Backend: ≥ 75% de linhas no global e ≥ 85% em `*.Domain` + `*.Application`.
- Frontend: ≥ 75% no global e ≥ 80% em `core/`, stores, guards e interceptors.

As exclusões do backend estão em `coverlet.runsettings`. Não amplie essa lista para passar no gate:
escreva o teste. Cair abaixo do `coverage-baseline.json` só avisa; quando a cobertura subir de
propósito, atualize o baseline à mão.

## Mais

- [CONVENCOES-CODIGO.md](CONVENCOES-CODIGO.md): convenções normativas, inclusive as de EF Core
- [REGRAS-DE-NEGOCIO.md](REGRAS-DE-NEGOCIO.md): regras `RN-*`
- [tarefas/](tarefas/): tasks `BE-*`/`FE-*` e decisões `D-*`
- [docs/adr/](docs/adr/): decisões de arquitetura
- [deploy/README.md](deploy/README.md): VM do GCP, roteiro da apresentação, mapa de requisitos do T2
- [frontend/README.md](frontend/README.md)
