# Runbook do Cloud Run (T3)

Quatro serviços + um job no **Cloud Run**, imagens no Artifact Registry, banco no Cloud SQL `banco-1`.
Tudo em `us-central1`, projeto `sd-26-2`, VPC `default`. **Quem roda os comandos é você**; os scripts
só imprimem (`-DryRun`) ou executam o que está escrito aqui. Histórico da VM (T2): branch `entrega-t2`.

```
Internet ──► frontend (nginx) ──HTTPS──► gateway ──gRPC/HTTPS──► identity (ingress internal) ─┐
                                            └─────gRPC/HTTPS──► tasks    (ingress internal) ─┼─► Cloud SQL banco-1
                                                                  tasks ──► identity         │   10.30.240.3, TLS
             job todolist-migrate (SQL idempotente, identity antes de tasks) ────────────────┘
```

## 1. Pré-requisitos

- `gcloud` autenticado (`gcloud auth login`) com o projeto `sd-26-2` ativo; `docker` (Docker Desktop);
  `pwsh` 7; Git Bash (para o `smoke.sh`).
- Par JWT em `.secrets/jwt/{private,public}.pem`: `./scripts/new-jwt-keys.ps1`. **Não reaproveite** o par
  de outro ambiente.
- A senha do usuário do banco (sem `;` nem `=`, que quebram a connection string do Npgsql).

## 2. Ordem

```powershell
./deploy/cloudrun/provision.ps1 -DryRun     # leia o que vai fazer; depois rode sem -DryRun (uma vez)
./scripts/publish-images.ps1                # anote a tag impressa
./deploy/cloudrun/deploy.ps1 -Tag <tag>     # job de migração + identity + tasks + gateway + frontend
DEMO_PASSWORD=... bash deploy/cloudrun/smoke.sh <url-do-frontend>
```

- `provision.ps1` habilita `run`, `secretmanager` e `iam` (a `iam` está desligada hoje), confere o
  Private Google Access, cria 5 service accounts, 5 secrets e o `secretAccessor` **por secret**
  (chave privada só para o identity, pública só para o gateway, senha só para o job), e faz o
  `patch` do banco (seção 9). Pede a senha do banco por `Read-Host -AsSecureString`. É idempotente.
- `deploy.ps1` é repetível: cada deploy cria uma revisão nova com a tag pedida. Termina com a tabela
  das 4 URLs (cole no relatório; identity e tasks entram como **internas**).
- Tudo que o `-DryRun` imprime é só escrita; as leituras do `gcloud` rodam de verdade.
- **Armadilha:** comando `gcloud` contra API desligada abre `(y/N)` e trava. Os scripts conferem antes.

## 3. Mudar a exposição (se o professor pedir tudo público)

```powershell
./deploy/cloudrun/deploy.ps1 -Tag <tag> -Exposure Public     # identity e tasks com --ingress all
```

Padrão e ensaios: `Internal`. A troca é só rodar o deploy de novo. As chamadas entre serviços
continuam sem IAM (D-45); a barreira é o ingress.

## 4. Dia da apresentação

1. **~30 min antes:** `./deploy/cloudrun/demo.ps1 -Action Warmup` (liga o banco, `min-instances 1` nos
   4 serviços). Depois o `smoke.sh` contra a URL pública: tudo OK, e deixe uma aba do app já logada.
2. **Depois:** `./deploy/cloudrun/demo.ps1 -Action Cooldown` (volta a 0 e para o banco). Não esqueça.
3. Se o Tasks ficou derrubado: `demo.ps1 -Action TasksUp` antes de qualquer outra coisa.

## 5. Roteiro de 15 minutos

Estourar zera o quesito oral: ensaie com cronômetro. Abas abertas antes: Compute Engine, Cloud SQL
Studio (já autenticado como `todolist`), Cloud Run (lista de serviços), Cloud Logging com o filtro
abaixo, o app (logado em outra conta), terminal com `demo.ps1`.

| Min | Ato | Requisito do T3 | Evidência |
|---|---|---|---|
| 0:00-3:00 | Slides: arquitetura, retry, queda | arquitetura e resiliência | diagrama T3 |
| 3:00-3:30 | Console do Compute Engine | sem VMs | lista de instâncias vazia |
| 3:30-6:00 | Cadastro, login, criar, listar pela URL HTTPS do frontend | fluxo ponta a ponta no Cloud Run | app em `https://...run.app` |
| 6:00-7:30 | Cloud SQL Studio: `select email from identity.users order by created_at desc limit 3;` e `select title, owner_id from tasks.tasks order by created_at desc limit 3;` | persistência real | a linha recém-criada |
| 7:30-9:00 | Painel do Cloud Run: variáveis e secrets do identity/gateway; job `todolist-migrate` (execuções) | config pelo GCP, sem segredo em texto puro | `Jwt__PrivateKeyPath` e connection string como referência a secret |
| 9:00-12:00 | Queda do Tasks (abaixo) | tolerância a falha, retry | aviso na lista, log das retentativas, recuperação |
| 12:00-12:30 | URL do `todolist-tasks` aberta no navegador: 404 | não exposição | página 404 do Google |
| 12:30-15:00 | Folga e perguntas | | |

**Queda do Tasks (3 min):** `demo.ps1 -Action TasksDown` -> na aba do app, login e perfil seguem
funcionando, a lista mostra o aviso de indisponível -> Cloud Logging -> `TasksUp` -> "tentar de novo"
traz as mesmas tarefas do banco. Login e perfil funcionam porque o Identity está de pé; Identity fora
do ar (D-28, fail-closed) fica em slide, não na demo.

**Filtro do Cloud Logging** (as retentativas do gateway, nível Debug da categoria do cliente gRPC):

```
resource.type="cloud_run_revision"
resource.labels.service_name="todolist-gateway"
jsonPayload.SourceContext="Grpc.Net.Client.Internal.RetryCall"
```

A mensagem é `Evaluated retry for failed gRPC call. Status code: 'Unavailable', Attempt: N, Retry: True|False`.
Operações de escrita (criar) **não** são retentadas (D-44): só leituras.

**Plano B** se `--scaling=0` falhar no ensaio: apagar o serviço (`gcloud run services delete todolist-tasks
--region us-central1 --quiet`) e recriar com `deploy.ps1`. A URL é determinística.

## 6. Sintomas e causas

| Sintoma | Causa provável |
|---|---|
| gateway -> tasks/identity devolve **403** | Private Google Access desligado na sub-rede `default`, ou o gateway/tasks sem `--vpc-egress all-traffic`: a chamada para `*.run.app` saiu pela internet e o ingress `internal` a barrou |
| 404 do **nginx** (ou do Google) ao abrir a SPA/`/api` | `Host` errado no proxy; `GATEWAY_UPSTREAM` sem esquema (`https://`) ou com barra no fim |
| Identity não sobe (revisão falha) | secret ou volume: a SA sem `secretAccessor`, ou `Jwt__PrivateKeyPath` diferente do ponto de montagem (`/secrets/jwt/private.pem`) |
| Gateway não sobe | idem, com `Jwt__PublicKeyPath` e o secret público |
| "connection refused" / "too many clients" no Postgres | `max_connections` (seção 8) |
| Job de migração falha na conexão | banco parado (`NEVER`), ou a SA do job sem acesso ao `todolist-db-password` |
| Login funciona e a sessão morre em 15 min | cookie `Secure` em HTTP; aqui tudo é HTTPS e o padrão `true` basta |

## 7. O que medir no 1º ensaio

1. **Código gRPC recebido com `TasksDown`.** Se não for `Unavailable` (por exemplo `Unimplemented`
   vindo de um 404), **abra uma tarefa nova antes de mexer**: o código entra nos retentáveis e no
   mapeamento para `503`, com teste. Registre o resultado aqui.
2. O `db-f1-micro` aguenta? Se faltar conexão ou memória, `db-g1-small` antes de qualquer tier dedicado.
3. Latência do Register/Login (PBKDF2 de 600 mil iterações, ~1,6 s numa vCPU compartilhada; o timeout
   do Identity no gateway é 10 s).
4. `min-instances 0`: tempo do primeiro request depois de ocioso (cold start encadeado).

## 8. Orçamento de conexões

`db-f1-micro`: `max_connections` baixo (cerca de 25). Cada connection string tem
`Maximum Pool Size=5` e identity e tasks têm `--max-instances 2`: no pior caso 2x5 + 2x5 = 20, mais o
job de migração. Não suba `max-instances` nem o pool sem refazer a conta.

## 9. Banco `banco-1` (do runbook do T2, resumido)

- PostgreSQL 18, edição ENTERPRISE, IP privado **`10.30.240.3`** na VPC `default` (peering
  `10.30.240.0/20`), `sslMode: ENCRYPTED_ONLY`: `SSL Mode=Require` (e `PGSSLMODE=require`) é obrigatório.
- Banco `todolist` e usuário `todolist` já existem e o banco está migrado. Um só usuário é dono dos
  dois schemas (a FK cruzada `tasks -> identity` exige isso). Existe um usuário lixo `todolist `
  (com espaço no fim): não redefina a senha do errado.
- Parado = `activationPolicy: NEVER`; não existe `sql instances start`, quem liga é o `patch` com
  `--activation-policy=ALWAYS`. Parada, a instância responde HTTP 400 a `databases list`/`users list`.
- `provision.ps1` troca para `db-f1-micro` e **remove o IP público**; o patch reinicia a instância e só
  vale com ela ligada (o script liga junto, se estiver parada). **Nunca apague** a `banco-1`.
- Sem IP público, consultar dados é pelo **Cloud SQL Studio** (console). Emergência: ligar o IP público
  (`gcloud sql instances patch banco-1 --assign-ip`) e restringir a rede autorizada; desligue depois.

## 10. Custos

| Item | Quando cobra |
|---|---|
| Cloud SQL `db-f1-micro` | só ligado (do Warmup ao Cooldown, e nos ensaios); parado paga só o disco de 10 GB |
| Cloud Run `min-instances 1` (4 serviços) | só entre Warmup e Cooldown; com `0`, só durante requisições |
| Artifact Registry, Secret Manager | centavos por mês |

Depois de cada ensaio: `Cooldown`. `gcloud sql instances describe banco-1 --format "value(state)"`
deve dizer `STOPPED`.
