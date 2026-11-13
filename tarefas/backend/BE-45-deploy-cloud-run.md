# BE-45 — Scripts e runbook do Cloud Run

> **T3 (Cloud Run).** Entrega os scripts que o usuário executa no GCP e o runbook com o roteiro de 15 min. O agente não cria nem altera recurso no GCP.

| | |
|---|---|
| **Domínio** | Infraestrutura / Deploy |
| **Serviço** | todos (identity, tasks, gateway, frontend, migrate) |
| **Depende de** | [BE-43](BE-43-retry-grpc.md), [BE-44](BE-44-imagens-cloud-run.md) |
| **Regras cobertas** | nenhuma de negócio (D-28, D-32, D-37, D-38, D-42, D-44, D-45) |

## Escopo

Em `deploy/cloudrun/`, no padrão de `scripts/publish-images.ps1` (pwsh 7, pré-condições só de leitura, `-DryRun`, `--quiet`, idempotente):

- `provision.ps1` (uma vez): APIs `run`, `secretmanager` e `iam`; Private Google Access na sub-rede `default`; 5 service accounts; secrets (chaves JWT de `.secrets/jwt`, connection strings e senha por `Read-Host -AsSecureString`); `secretAccessor` por secret só para quem usa; `banco-1` para `db-f1-micro` sem IP público.
- `deploy.ps1 -Tag <tag> [-Exposure Internal|Public]`: job `todolist-migrate` (+ execução), identity, tasks, gateway, frontend, nesta ordem; imprime a tabela das 4 URLs.
- `demo.ps1 -Action Warmup|Cooldown|TasksDown|TasksUp`.
- `smoke.sh <url>`: rota do SPA, 401 (sem token, lixo, adulterado), cadastro, login, 400, 201 com `Location`, listagem.
- `README.md`: runbook (ordem, exposição, dia da apresentação, roteiro de 15 min, sintomas, medições do 1º ensaio, conexões, banco, custos).

### Decisões que ajustam o design

- **(a) `ForwardedHeaders:KnownProxies` fica como está.** No Cloud Run o IP remoto só aparece em log, e o cookie `Secure` é configuração explícita (D-42), não `Request.IsHttps`. Abrir a lista de proxies sem IP fixo deixaria o `X-Forwarded-For` falsificável.
- **(b) O botão "tentar de novo" já existe** (`app-error-state` em `tasks-page.component.html`): o front não muda.
- **`--allow-unauthenticated` também em identity e tasks, mesmo no modo interno.** A D-T3-1 descarta ID token entre serviços; sem a flag, o IAM barraria o gateway. A barreira é o `--ingress internal` (D-45).
- **`Logging__Console__FormatterName=json` não é usado.** Os serviços usam Serilog com `RenderedCompactJsonFormatter` fixo (`StructuredLogging.cs`): o log já sai em JSON e essa chave não tem efeito. Consequência a tratar à parte: o formato compacto usa `@l` para o nível, e o Cloud Logging só lê `severity`, então as entradas aparecem sem severidade. O filtro das retentativas usa `jsonPayload.SourceContext`, que funciona.
- **`iam.googleapis.com` entra no `provision.ps1`**: não estava habilitada e criar service account precisa dela.
- Job com `--max-retries 0` (falha alto) e frontend com `--max-instances 2` (teto de custo).

## Critérios de aceite

- [x] **CA-01** — `pwsh deploy/cloudrun/provision.ps1 -DryRun` roda sem erro e imprime, nesta ordem: APIs, Private Google Access, SAs, secrets, bindings por secret, `sql instances patch`.
- [x] **CA-02** — `deploy.ps1 -Tag test -DryRun` e `-Exposure Public -DryRun` imprimem job, execução, identity, tasks, gateway, frontend, com `--ingress internal` (padrão) ou `all` em identity/tasks.
- [x] **CA-03** — Os 4 `demo.ps1 -Action ... -DryRun` imprimem os comandos esperados (Cooldown: serviços a 0 antes de parar o banco).
- [x] **CA-04** — Nenhum valor de secret aparece no texto impresso (connection strings e senha só em arquivo temporário apagado; o dry-run mostra `<valor-pedido-no-prompt>`).
- [x] **CA-05** — `DEMO_PASSWORD=... bash deploy/cloudrun/smoke.sh http://localhost` contra o compose de dev: todos os passos OK.
- [x] **CA-07** — Toda linha de log JSON carrega `severity` top-level (`LoggingTests.LogDeRequisicao_TrazSeverityTopLevelParaOCloudLogging`).
- [ ] **CA-06** — Executado pelo usuário no 1º ensaio (provision, publish, deploy, smoke, roteiro com cronômetro, medições da seção 7 do runbook). **Em aberto.**

## Notas

- Registro das decisões: **D-45** em [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
