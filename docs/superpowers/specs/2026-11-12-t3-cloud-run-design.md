# T3 — Migração para Cloud Run: design

Data: 12/11/2026 · Entrega do PDF: **02/12/2026 23:59** ·
Apresentação: **03/12/2026 18:50**, lab 154, **15 min** (estourar zera o quesito oral).

## Intenção

Tirar o TodoList da VM e rodá-lo 100% em serviços gerenciados do GCP (Cloud Run + Cloud SQL), provando
ao vivo, pelas URLs HTTPS, três coisas: o fluxo ponta a ponta com persistência real, **retry automático**
e **tratamento da queda de um microsserviço**, e a ausência de VMs. O código de negócio não muda; o que
muda é empacotamento, configuração, deploy e uma política de resiliência que hoje não existe (o sistema
só faz fail-closed com `503`, sem retentativa).

Peso da nota: 50% demo ao vivo, 30% Dockerfiles/arquitetura/config no painel, 20% relatório + oral.

## Objetivos

1. Os 4 componentes (frontend, gateway, identity, tasks) rodando no **Cloud Run**, imagens no **Artifact
   Registry** (repositório `todolist`, us-central1, já existe), banco no **Cloud SQL** `banco-1`.
2. **Retry automático** nas chamadas gRPC e demo de **queda do Tasks** com o resto do sistema de pé.
3. Variáveis de ambiente e segredos configurados pelo GCP (Cloud Run env + Secret Manager), visíveis no
   painel.
4. Logs legíveis no **Cloud Logging**, com severidade correta e as retentativas aparecendo.
5. **Scripts e runbook** que o usuário executa — o agente não cria nem altera recurso no GCP.
6. Relatório PDF (≤ 5 páginas), diagrama T3, roteiro de 15 min.

## Não-objetivos

- Circuit breaker, filas, fluxo assíncrono, Pub/Sub. O enunciado aceita tolerância a falhas *ou*
  assincronia; tolerância basta.
- Terraform/IaC, CI/CD de deploy, domínio próprio, Load Balancer, Cloud Armor.
- Qualquer regra de negócio nova (RN-*), qualquer task `BE-*`/`FE-*` funcional.
- Retirar o compose de dev local: `docker compose up` continua funcionando como hoje.

## Antiobjetivos (falha mesmo que "funcione")

- Gastar créditos à toa: tier de banco superdimensionado, instâncias mínimas ligadas fora dos dias de uso.
- Retentativa que **duplica dados** (criar tarefa duas vezes).
- Segredo versionado ou em variável de ambiente em texto puro no painel (chave JWT, senha do banco).
- Endereço/porta literal em código (o teste de arquitetura existente pega).
- Demo que depende de sorte: cold start ao vivo, comando que nunca foi ensaiado.

## Estado atual (lido em 12/11/2026, só leitura)

| Recurso | Estado |
|---|---|
| Compute Engine | **0 VMs** — já excluídas. Falta só o print do console. |
| Cloud SQL `banco-1` | PostgreSQL 18, edição **ENTERPRISE**, `db-custom-2-8192`, zonal, 10 GB, **STOPPED** (`activationPolicy: NEVER`). IP privado `10.30.240.3` na VPC `default`; IP público ligado. Banco `todolist`, usuário e migrations já existem (verificados em campo no T2, ver [deploy/cloudsql/README.md](../../../deploy/cloudsql/README.md)). |
| Artifact Registry `todolist` | Existe, com imagens do T2. |
| APIs | `run` e `secretmanager` **desabilitadas**. `artifactregistry`, `compute`, `sqladmin`, `logging` habilitadas. |

## Decisões fechadas com o usuário

### D-T3-1 · Exposição: só frontend e gateway públicos

| Serviço | Ingress | Saída |
|---|---|---|
| frontend | `all` + `--allow-unauthenticated` | padrão (fala com a URL pública do gateway) |
| gateway | `all` + `--allow-unauthenticated` | **Direct VPC egress**, `all-traffic` |
| identity | **`internal`** | Direct VPC egress (para o Cloud SQL pelo IP privado) |
| tasks | **`internal`** | Direct VPC egress, `all-traffic` (Cloud SQL + identity) |

Preserva a D-32. Chamadas gateway→identity/tasks e tasks→identity usam as URLs `https://*.run.app` e
saem pela VPC, o que o Cloud Run trata como tráfego interno. Sem autenticação IAM entre serviços (sem
ID token, zero código). O usuário **vai confirmar com o professor**; se ele pedir tudo público, a troca é
`--ingress all` em identity e tasks — o runbook tem essa seção pronta. As URLs internas entram na tabela
do relatório marcadas como internas; abrir uma no navegador e ver 404 é prova de não exposição.

### D-T3-2 · Resiliência: retry gRPC nativo + queda do Tasks ao vivo

- Retry pela **política nativa de `Grpc.Net.Client` (`ServiceConfig`/`RetryPolicy`)**, sem dependência
  nova: 3 tentativas, backoff curto, **só `StatusCode.Unavailable`**.
- **Sem retry em operação não idempotente** (`CreateTask`, `Register`, e qualquer outra cujo efeito
  duplicaria se repetida); essas devolvem `503` direto. Leituras e operações idempotentes ganham retry.
  O builder classifica RPC a RPC a partir dos `.proto` e registra a tabela na task.
- Aplica-se ao Gateway (clientes Identity e Tasks) **e** ao Tasks (cliente Identity).
- **Retentativas visíveis no Cloud Logging**: ligar por configuração o nível de log da categoria do
  cliente gRPC que registra as tentativas (o builder confirma qual categoria/nível emite a mensagem).
- O fail-closed da D-28 (Identity fora → `503`, nada é criado) não muda.
- Frontend: já mapeia `503` para mensagem própria ([error-mapper.ts](../../../frontend/src/app/core/errors/error-mapper.ts)).
  Falta um botão **"tentar de novo"** na lista de tarefas quando ela falha com `503`.

**Cena da demo (~2 min):**
1. `gcloud run services update tasks --scaling=0` (ou pelo console) — desliga o Tasks sem apagá-lo
   ([doc](https://docs.cloud.google.com/run/docs/configuring/services/manual-scaling)).
2. Login e perfil seguem funcionando (Identity de pé); a lista mostra o aviso de indisponível; o Cloud
   Logging mostra as tentativas do Gateway e o `503` com `Retry-After`.
3. `--scaling=auto` — religa; "tentar de novo" traz as mesmas tarefas do Cloud SQL.

Plano B se `--scaling=0` falhar no ensaio: apagar e recriar o serviço (a URL é determinística).
Identity fora do ar fica em slide (D-28), não na demo.

### D-T3-3 · Banco: downgrade para `db-f1-micro`

`banco-1` → `db-f1-micro` (vCPU compartilhada, 0,6 GB; permitido na edição ENTERPRISE). Fica **parado**
fora de ensaios e da apresentação. Se engasgar no ensaio, `db-g1-small` antes de qualquer tier dedicado.
Os serviços conectam pelo **IP privado** via Direct VPC egress — mesma connection string do T2
(`SSL Mode=Require`), só os valores vêm do Secret Manager.

### D-T3-4 · Migrations: Cloud Run Job

Um **Cloud Run Job** `todolist-migrate`, com imagem pequena contendo os dois SQL gerados por
[new-migrations-sql.ps1](../../../scripts/new-migrations-sql.ps1), aplicando **identity antes de tasks**
(FK cruzada), idempotente, pela VPC. Aparece no console e pode ser executado na demo.

### D-T3-5 · Execução no GCP é do usuário

O agente entrega **scripts e runbook**; o usuário roda. Scripts seguem o padrão de
[publish-images.ps1](../../../scripts/publish-images.ps1): PowerShell 7, pré-condições só de leitura
primeiro, `-DryRun`, falha alto, nunca habilita API ou cria recurso escondido dentro de outro passo.

## Decisões do agente (o usuário pode corrigir)

- **Segredos no Secret Manager**: chave privada JWT (montada como **volume** no identity →
  `Jwt__PrivateKeyPath`), chave pública (volume no gateway → `Jwt__PublicKeyPath`), as duas connection
  strings e a senha do banco (env var referenciando secret). Configuração não secreta é env var simples
  no Cloud Run.
- **Uma service account por serviço**, com só o que usa: `secretmanager.secretAccessor` nos secrets
  dele; nada de SA padrão do Compute.
- **Imagens .NET finais em `aspnet:10.0-noble-chiseled`** (menor, sem shell, não-root). Confirmar o uid
  e que nada no runtime depende de shell/ICU; se depender de ICU, usar a variante `-extra`.
- **Logs em JSON** no Cloud Run via `Logging__Console__FormatterName=json` (configuração, não código).
- **Cookie `Secure` = padrão `true`** (tudo HTTPS); some o `RefreshCookie__Secure=false` da VM.
- **`min-instances=1` só no dia** (e nos ensaios), em gateway/identity/tasks/frontend, para não pegar
  cold start ao vivo; volta a 0 depois. Está no runbook, não fixo no deploy.
- **Desligar o IP público do `banco-1`**: com IP privado e Job pela VPC, ninguém precisa dele.
- **Revisões**: deploy por tag imutável (SHA), como o `publish-images.ps1` já faz.
### D-T3-6 · Uma branch por entrega; artefatos do T2 saem da `main` (decisão do usuário)

- O trabalho do T3 acontece na branch **`entrega-t3`**, criada a partir de `entrega-t2`, que está 74
  commits à frente da `main`. Ao final, `entrega-t3` é mergeada na `main`. **`entrega-t2` não muda**: ela
  guarda os runbooks e artefatos do T2 como foram apresentados.
- O primeiro commit de `entrega-t3` **apaga todo o `deploy/` do T2**: `README.md` (runbook da VM),
  `cloudsql/README.md`, `docker-compose.prod.yml`, `todolist.env.example`, `todolist.service`,
  `install-docker-on-vm.sh`, `smoke.sh` e `demo.sh`. Também apaga as referências a eles no
  [README.md](../../../README.md) e no [CLAUDE.md](../../../CLAUDE.md), que passam a apontar para a
  branch `entrega-t2` como histórico.
- **Ficam** os itens que o T3 reaproveita: `scripts/publish-images.ps1`, `scripts/new-migrations-sql.ps1`,
  `scripts/new-jwt-keys.ps1`, os Dockerfiles e o `docker-compose.yml` de dev. O conteúdo útil do
  runbook antigo (estado do `banco-1`, TLS do Cloud SQL, armadilhas de nginx e de uid) é reescrito no
  runbook do T3, não copiado inteiro.
- As tasks antigas em `tarefas/` (BE-37 etc.) **não** são editadas. São registro histórico e podem
  citar arquivos que não existem mais na `main`.

### Respostas à revisão do builder (12/11/2026)

- **Ingress até o professor responder:** o padrão é **`internal`**, e é ele que sobe nos ensaios. O
  script de deploy recebe a exposição como **parâmetro desde o início** (`-Exposure Internal|Public`),
  para que a troca seja rodar o script de novo, não editá-lo.
- **Rede:** tudo em **us-central1** (Cloud SQL, Artifact Registry, Cloud Run), na VPC `default`. O
  runbook confere, por leitura, se a sub-rede `default` de us-central1 tem **Private Google Access**
  ligado, e traz o passo para ligar se não tiver, **antes** do deploy. Sem isso, a chamada para
  `*.run.app` não conta como interna e volta 403.
- **Conexões no `db-f1-micro`** (`max_connections` baixo, cerca de 25): `Maximum Pool Size` pequeno nas
  connection strings, por configuração, e `--max-instances` baixo (2) em identity e tasks. O
  orçamento de conexões fica no runbook. O **primeiro ensaio** valida o tier: se faltar conexão ou
  memória, sobe para `db-g1-small`.
- **Queda → `Unavailable`:** o primeiro ensaio mede o que o Gateway realmente recebe com
  `--scaling=0`. Se não for `Unavailable` (por exemplo, `Unimplemented` causado por 404), o código
  observado entra nos retentáveis **e** no mapeamento para `503`, com teste. Esse resultado fica
  registrado no runbook.
- **Na dúvida, sem retry.** Login, Refresh (rotaciona o token), Register, Create, ChangePassword e
  DeleteAccount ficam sem retry. Só leituras puras ganham retry, além de operações provadamente
  idempotentes.
- **HTTP/2:** `--use-http2` só em identity e tasks (gRPC h2c na 8080). Gateway e frontend ficam em
  HTTP/1. A porta 8081 de health **sai da imagem**: o Cloud Run usa uma porta só, e a probe vira
  TCP/gRPC health na 8080. O uso local da 8081 fica a critério do builder, se o compose de dev precisar.
- **Job de migração:** o banco já está migrado (T2); o SQL é idempotente, então rodar o Job contra o
  banco populado é o teste de idempotência. O Job tem **SA própria**, com acesso só ao secret da
  connection string ou da senha que usa.
- **IP público do banco desligado:** o acesso manual (consultar dados e o `UPDATE` do usuário inativo
  no roteiro) passa a ser pelo **Cloud SQL Studio** no console, que mostra a persistência real ao vivo.
  Ligar o IP público para ensaio fica como recurso de emergência descrito no runbook, não como passo.

## O que muda no repositório

1. **nginx do frontend** ([nginx.conf.template](../../../frontend/nginx.conf.template)): hoje fixa
   `http://` e repassa `Host $host`. No Cloud Run o upstream é `https://gateway-...run.app` e exige Host
   do próprio gateway e SNI. `GATEWAY_UPSTREAM` passa a ser URL com esquema; `Host $proxy_host`;
   `proxy_ssl_server_name on`. Conferir que o resolver lido do `resolv.conf` funciona no Cloud Run.
   O compose de dev precisa continuar funcionando (`http://gateway:8080`).
2. **Retry gRPC** (D-T3-2) no Gateway e no Tasks, com testes, configurável (não literal).
3. **Botão "tentar de novo"** na lista quando ela falha com `503`, com teste.
4. **Dockerfiles .NET em chiseled**; Dockerfile da imagem de migração. Identity/Tasks hoje escutam gRPC
   na 8080 e HTTP/1 (health) na 8081; o Cloud Run só roteia a porta do `PORT`. O builder decide se a
   8081 some do container ou fica só para uso local — a probe do Cloud Run usa TCP na 8080 ou gRPC
   health (o Tasks já implementa `grpc.health.v1`).
5. **`ForwardedHeaders:KnownProxies`**: no Cloud Run os proxies (front-end do Google, nginx) não têm IP
   fixo. Hoje o IP remoto só serve a log. Decidir a configuração de produção sem abrir spoofing.
6. **Scripts** em `deploy/cloudrun/` (ou `scripts/`): habilitar APIs, criar secrets e SAs, alterar o
   banco, deploy dos 4 serviços + Job, alternar `min-instances`, derrubar/religar o Tasks, trocar ingress.
   Runbook `deploy/cloudrun/README.md` com ordem, verificação e sintomas.
7. **Script de demo** apontando para a URL HTTPS do frontend.
8. **Tasks e decisões** no padrão do repo: `BE-43+` em [tarefas/backend/](../../../tarefas/backend/),
   decisões novas `D-*`, ADR para a resiliência (retry + exposição interna).
9. **Docs**: [arquitetura.svg](../../arquitetura.svg) versão T3, README, mapa requisito → evidência do T3.

## Entregáveis fora do código

- **Relatório PDF ≤ 5 páginas**: contexto de negócio, diagrama com os serviços gerenciados, tecnologias,
  estratégia de resiliência, **tabela de URLs HTTPS** (públicas e internas), link do GitHub.
- **Roteiro de 15 min** cronometrado, com o mapa requisito → ato → evidência (como o do T2 em
  [deploy/README.md](../../../deploy/README.md)), incluindo: print/console do Compute Engine vazio,
  cadastro → login → criar → listar, Cloud Logging, cena da queda do Tasks, painel de variáveis.
- Slides de arquitetura (opcional pelo enunciado).

## Restrições

- Regras do [CLAUDE.md](../../../CLAUDE.md) e do [CONVENCOES-CODIGO.md](../../../CONVENCOES-CODIGO.md):
  build sem warnings, testes, `dotnet format`, lint do front, regressão para bug corrigido, nenhum
  segredo versionado, endereço só em configuração, Conventional Commits.
- Migrations: Identity sempre antes do Tasks.
- Execução etapa a etapa: cada onda só começa com pedido explícito.

## Deixado para o builder

- A ordem e o tamanho das ondas, desde que repositório (testável localmente) venha antes do runbook, e
  o runbook antes de qualquer execução no GCP.
- Parâmetros finos do retry (backoff, deadline por tentativa) e a classificação RPC a RPC, dentro de
  D-T3-2.
- CPU/memória por serviço no Cloud Run (o PBKDF2 de 600 mil iterações custa ~1,6 s numa vCPU
  compartilhada; o deadline do Identity no Gateway já é 10 s).
- Como validar localmente o nginx com upstream HTTPS antes do deploy.
