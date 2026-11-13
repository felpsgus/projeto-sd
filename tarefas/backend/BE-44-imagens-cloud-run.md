# BE-44 — Imagens para o Cloud Run (nginx com upstream https, imagens enxutas, imagem de migração)

> **T3 (Cloud Run).** Prepara as cinco imagens publicadas no Artifact Registry. Não muda comportamento de negócio.

| | |
|---|---|
| **Domínio** | Infraestrutura / Imagens |
| **Serviço** | frontend (nginx), identity, tasks, gateway, migrate |
| **Depende de** | [BE-38](BE-38-containerizacao.md), [BE-42](BE-42-nginx-mesma-origem.md) |
| **Regras cobertas** | nenhuma de negócio (D-37: probes e portas no Cloud Run) |

## Escopo

### Parte 1 — nginx com upstream https

O proxy de `/api` do `frontend` aceita um `GATEWAY_UPSTREAM` https (URL do Gateway no Cloud Run), preservando o `Host` e o SNI do upstream. Verificado com `GATEWAY_UPSTREAM=https://httpbin.org/anything`: o eco mostrou `"Host": "httpbin.org"` e url `https://httpbin.org/anything/api/tasks?x=1`; `/tasks` respondeu 200; no compose (upstream http), `/api/tasks` segue 401.

### Parte 2 — imagens enxutas

- Estágio final dos três Dockerfiles .NET: `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra` (era `aspnet:10.0`). `USER $APP_UID` mantido; `docker inspect` confirma `User=1654` e `APP_UID=1654`.
- **Por que `-extra`:** traz ICU e tzdata. A variante sem ICU exigiria `InvariantGlobalization=true`, e o efeito disso (cultura, comparação de strings) não foi verificado.
- **Sem shell:** a imagem chiseled não tem `sh`. Nada no estágio final nem no compose depende de shell (os containers .NET não têm healthcheck; o `migrate` do compose usa a imagem do Postgres).
- **Porta 8081 fica.** Atende `/health` em HTTP/1 no compose e no `dotnet run`. No Cloud Run é inalcançável e inofensiva; a probe usa TCP na 8080. Isto ajusta o texto do design.
- **Imagem de migração** `deploy/cloudrun/migrate.Dockerfile`: `postgres:18-alpine` + `01-identity.sql` e `02-tasks.sql` (Identity antes do Tasks), `psql -v ON_ERROR_STOP=1`. Conexão por `PGHOST`/`PGPORT`/`PGDATABASE`/`PGUSER`/`PGPASSWORD`/`PGSSLMODE=require`. Contexto de build = `artifacts/sql/` (o `.dockerignore` da raiz exclui `artifacts/`).
- `scripts/publish-images.ps1` publica cinco imagens (gera os SQL se faltarem) e termina com `Use: deploy/cloudrun/deploy.ps1 -Tag <tag>`.

### Tamanhos (`docker images`)

| Imagem | Antes (`aspnet:10.0`) | Depois (chiseled-extra) |
|---|---|---|
| todolist-identity | 393 MB | 292 MB |
| todolist-tasks | 388 MB | 287 MB |
| todolist-gateway | 348 MB | 247 MB |
| todolist-frontend | 83 MB | 83 MB (inalterada) |
| todolist-migrate | - | 433 MB (nova; só roda uma vez) |

## Critérios de aceite

- [x] **CA-01** — nginx repassa `/api` a um upstream https com `Host` do upstream (eco acima).
- [x] **CA-02** — As três imagens .NET usam `chiseled-extra`, rodam com uid 1654 e ficam menores (tabela).
- [x] **CA-03** — `docker compose --profile full up` + `smoke.sh` da branch `entrega-t2` contra `http://localhost`: todos os passos OK (inclui login, ou seja, o Identity lê a chave JWT).
- [x] **CA-04** — `todolist-migrate` rodada duas vezes contra o Postgres do compose: exit 0 nas duas (idempotente).
- [x] **CA-05** — `pwsh scripts/publish-images.ps1 -DryRun` lista 5 imagens.
