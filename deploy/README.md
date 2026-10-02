# Implantação no GCP — runbook do T2 (Docker + Cloud SQL)

Passo a passo para colocar a stack de pé na `maquina-1-psd` e apresentá-la em 22/10: uma VM só,
tudo em container, Postgres no Cloud SQL `banco-1`. Assume `gcloud` e Docker Desktop instalados
na máquina de desenvolvimento.

Este arquivo é o **runbook** (o que digitar). O runbook específico do banco está em
[cloudsql/README.md](cloudsql/README.md).

> **Estado.** O ensaio de 30/09/2026 percorreu o caminho inteiro e a stack funcionou de ponta a
> ponta contra o Cloud SQL (`smoke.sh` e a antiga verificação do notebook, verdes). O ambiente foi
> **desligado ao final**: `banco-1` em `STOPPED`/`NEVER` e `maquina-1-psd` `TERMINATED`. As
> correções de 01/10 (seção 13) ainda não foram exercitadas numa VM real. Trate o próximo
> ensaio com folga, não como formalidade.

## Topologia

| Recurso | Detalhe |
|---|---|
| `maquina-1-psd` | `us-central1-a`, `e2-small` (2 GB), Debian 13, IP interno `10.128.0.4`, tags `http-server`/`https-server`/`todolist-app`. Roda o compose: `frontend` (nginx, **porta 80**), `gateway`, `identity`, `tasks` e o `migrate` (vida curta) |
| `banco-1` (Cloud SQL) | `POSTGRES_18`, `us-central1`, IP privado **`10.30.240.3`** (peering `10.30.240.0/20`), `sslMode: ENCRYPTED_ONLY` — `SSL Mode=Require` é obrigatório |
| `maquina-2-psd` | **desligada.** Era o Postgres do T1; o banco agora é o Cloud SQL |

```
              internet
                  │ porta 80 (única pública)
                  ▼
           ┌─────────────┐
           │  frontend   │  nginx: serve o Angular; proxy /api/* →
           └──────┬──────┘
                  ▼
           ┌─────────────┐      gRPC       ┌──────────┐
           │   gateway   │ ───────────────►│ identity │──┐
           └──────┬──────┘                 └──────────┘  │
                  │ gRPC                   ┌──────────┐  ├──► Cloud SQL banco-1
                  └───────────────────────►│  tasks   │──┘    (10.30.240.3:5432, TLS)
                                           └──────────┘
```

Os containers conversam pela rede interna do compose (DNS por nome de serviço, nunca
`127.0.0.1`). **Só o `frontend` publica porta** (80); `gateway`, `identity` e `tasks` não têm
`ports:` (D-32). O nginx só repassa — se o Gateway cair, devolve 502/504 em vez de inventar
resposta (D-40); autenticação, validação e tradução de protocolo ficam no Gateway.

## 1. Visão geral da ordem

```
1. Ligar o Cloud SQL                (seção 2)
2. Artifact Registry + repositório  (seção 3 — uma vez)
3. scripts/publish-images.ps1       (seção 4 — na sua máquina, builda e publica as 4 imagens)
4. scripts/new-migrations-sql.ps1   (seção 5 — gera artifacts/sql/*.sql) + gcloud compute scp
5. deploy/install-docker-on-vm.sh   (seção 6 — na VM; testa a autenticação do registry, seção 7)
6. Preencher o .env na VM           (seção 8)
7. systemctl start todolist.service (seção 8)
8. Verificar                        (seções 9 e 10)
```

## 2. Ligar o Cloud SQL (não provisionar)

A instância **`banco-1` já existe**, com o peering pronto. Detalhes e comandos de cada passo em
[cloudsql/README.md](cloudsql/README.md). O caminho curto:

```bash
# 1) ligar (não existe `gcloud sql instances start` — quem liga é o patch da política)
gcloud sql instances patch banco-1 --project=sd-26-2 --activation-policy=ALWAYS

# 2) com a instância NO AR, conferir banco e usuário
#    (parada, estes dois comandos respondem HTTP 400 — não é erro de permissão)
gcloud sql databases list --instance=banco-1 --project=sd-26-2
gcloud sql users list     --instance=banco-1 --project=sd-26-2

# 3) criar o que faltar (senha pelo prompt, nunca no histórico do shell)
gcloud sql databases create todolist --instance=banco-1 --project=sd-26-2
gcloud sql users create todolist --instance=banco-1 --project=sd-26-2 --prompt-for-password
```

Banco `todolist` e usuário `todolist` já existiam no ensaio de 30/09.

- **Um único usuário, dono dos dois schemas** (`identity` e `tasks`). Separar em dois
  complicaria a FK cruzada `tasks.tasks.owner_id -> identity.users`, que exige `REFERENCES` no
  schema do outro serviço. Os schemas e a FK vêm dos dois SQL de migration, que o serviço
  `migrate` aplica **na ordem** `01-identity.sql`, `02-tasks.sql` (a FK do Tasks depende da
  tabela do Identity).
- **Senha:** a política da instância exige um caractere não alfanumérico, e ela **não pode
  conter `;` nem `=`** (quebram a connection string do Npgsql). Redefinir:
  `gcloud sql users set-password todolist --instance=banco-1 --project=sd-26-2 --prompt-for-password`.
- Existe um usuário lixo `todolist ` (com **espaço no fim**) na `banco-1`; no Console os dois
  parecem idênticos. Não redefina a senha do errado.
- A senha vive só no `.env` da VM (seção 8), `600`. A credencial `postgres/postgres` do
  `docker-compose.yml` é de desenvolvimento local e nunca atravessa a rede.
- A `banco-1` é anterior a este trabalho: **não apague**. Depois dos ensaios, só pare
  (`--activation-policy=NEVER`).

## 3. Artifact Registry

Uma vez. O `publish-images.ps1` detecta API desabilitada/repositório inexistente e se recusa a
publicar, mas não resolve sozinho:

```bash
gcloud services enable artifactregistry.googleapis.com --project sd-26-2
gcloud artifacts repositories create todolist \
  --repository-format=docker --location=us-central1 --project=sd-26-2
```

## 4. Publicar as quatro imagens

Da sua máquina (Docker Desktop rodando, `gcloud` autenticado no projeto certo):

```powershell
./scripts/publish-images.ps1 -DryRun    # ensaie primeiro: mostra builds/tags/pushes sem tocar em nada
./scripts/publish-images.ps1
```

Leia `Get-Help ./scripts/publish-images.ps1 -Full` antes da primeira vez, em especial a seção da
**tag**: com a árvore de trabalho suja, a tag não é o SHA do commit puro. O script imprime
qual tag usou — **anote**: ela entra em `IMAGE_TAG` no `.env` da VM (seção 8). O ensaio de
30/09 publicou `f2f0e82` e `latest`.

## 5. Gerar o SQL e copiar os arquivos para a VM

O `migrate` do compose exige `artifacts/sql/*.sql` na VM:

```powershell
./scripts/new-migrations-sql.ps1

gcloud compute scp deploy/docker-compose.prod.yml deploy/todolist.env.example deploy/todolist.service `
  deploy/install-docker-on-vm.sh deploy/smoke.sh deploy/demo.sh `
  maquina-1-psd:~/docker-deploy/ --zone=us-central1-a

gcloud compute scp --recurse artifacts/sql maquina-1-psd:~/docker-deploy/sql --zone=us-central1-a
```

> `gcloud compute ssh` normal está quebrado na máquina de desenvolvimento (a pasta `~/.ssh` está
> inacessível, ver seção 13). O ensaio usou uma chave dedicada: acrescente
> `--ssh-key-file=<chave>` aos comandos `scp`/`ssh`.

## 6. Preparar a VM

```bash
gcloud compute ssh maquina-1-psd --zone=us-central1-a
# dentro da VM:
cd ~/docker-deploy
chmod +x *.sh
sudo ./install-docker-on-vm.sh
```

O script instala o Docker Engine + plugin `compose` (repositório oficial, não o pacote da
distro), monta `/opt/todolist/docker/` com o compose e os SQL, cria (sem sobrescrever) o `.env`
a partir do `.example`, garante o par RS256 em `/etc/todolist/jwt/` com dono uid `1654`
(necessário sem Docker Swarm; ver o comentário em `docker-compose.prod.yml`, seção `secrets:`;
se uma imagem futura mudar o uid, o sintoma é "permission denied" ao ler o PEM — confira com
`docker run --rm mcr.microsoft.com/dotnet/aspnet:10.0 id app`),
e instala/habilita `deploy/todolist.service`. É idempotente: rodar de novo é o jeito normal de
atualizar compose/unit sem perder `.env` nem chaves. Trocar a chave invalida toda sessão em
andamento — o script nunca a regera.

O `chmod +x` não é opcional (o bit de execução não sobrevive de forma confiável a uma cópia a
partir do NTFS). Se aparecer `bad interpreter: ...^M`, é CRLF: `sed -i 's/\r$//' *.sh`.

## 7. Autenticação do Docker no Artifact Registry

Quem executa `docker compose up -d` é a unit `todolist.service` (`Type=oneshot`), **disparada
no boot, como root** — não a pessoa do SSH. Por isso `gcloud auth configure-docker` precisa
rodar **como root**: rodado pelo usuário comum, grava em `/home/<você>/.docker/config.json`, que
o root nunca lê. Testar à mão "funciona" e a unit falha no boot sem pista nenhuma.
`install-docker-on-vm.sh` já faz isso como root
(`gcloud auth configure-docker us-central1-docker.pkg.dev --quiet` → `/root/.docker/config.json`)
e, **antes de habilitar a unit**, tenta um `docker pull` real do `todolist-gateway`:

- **Funcionou** → segue.
- **Imagem não encontrada** (esperado antes da primeira publicação) → só avisa; rode de novo
  depois de publicar.
- **Falha com a credencial confirmada** → avisa e segue, dizendo qual tag testou e de onde veio.
  **Confira `IMAGE_TAG` contra as tags publicadas antes de mexer em IAM:** o erro mais provável
  é tag inexistente, não permissão.
- **Falha sem credencial** → aborta e imprime os remédios, nesta ordem:

  1. `roles/artifactregistry.reader` para a service account da VM
     (`36621986996-compute@developer.gserviceaccount.com`):

     ```bash
     gcloud artifacts repositories add-iam-policy-binding todolist \
       --location=us-central1 --project=sd-26-2 \
       --member="serviceAccount:36621986996-compute@developer.gserviceaccount.com" \
       --role="roles/artifactregistry.reader"
     ```

  2. Se persistir, o escopo OAuth da VM pode ser insuficiente. **Mudar escopo exige PARAR a VM**
     (`stop`, `set-scopes`, `start`) e derruba a stack — descubra num ensaio, não na véspera.
     Nada disto é feito sozinho.

A VM tem os escopos padrão do Compute Engine, incluindo `devstorage.read_only`, que o pull do
registry exige; **não** tem `cloud-platform`, então o `gcloud` **dentro** da VM não serve para
administrar o projeto. No ensaio de 30/09 nenhuma mudança de IAM ou de escopo foi necessária.

## 8. Preencher o `.env` e subir

```bash
sudo nano /opt/todolist/docker/.env
```

Preencha `<IP_PRIVADO_CLOUDSQL>` (`10.30.240.3`), a senha real (troque os `TROQUE_ESTA_SENHA`) e
`IMAGE_TAG` (a tag da seção 4). Depois:

```bash
sudo systemctl start todolist.service
sudo docker compose -f /opt/todolist/docker/docker-compose.prod.yml \
  --env-file /opt/todolist/docker/.env ps
```

Esperado: `frontend`, `gateway`, `identity` e `tasks` em `Up`; `migrate` terminou com código 0.
Reboot da VM: a unit sobe a stack sozinha (`restart: unless-stopped` no compose cobre só um
container que morre com o daemon no ar; a unit cobre o reinício do daemon).

## 9. Rede: firewall, IP externo e verificação de fora

**Nenhuma regra de firewall precisa ser criada, alterada ou removida.** Estado real do projeto
`sd-26-2`:

| Regra | Situação |
|---|---|
| `default-allow-http` | já libera `tcp:80` de `0.0.0.0/0` para a tag `http-server`, que a VM tem — é a única entrada pública necessária |
| `todolist-allow-gateway` (`tcp:8080` pública) | nunca existiu; se aparecer um dia, remova |
| `default-allow-ssh` | `tcp:22` de `0.0.0.0/0`, mais largo que o range do IAP. Restringir é decisão sua (remover esta regra), não deste runbook |
| `todolist-allow-grpc-internal` (só `5081`) e `todolist-allow-postgres` | existem e são **inócuas**: os containers falam pela rede do compose, e o Cloud SQL vai pelo peering de IP privado, que não passa por regra de firewall da VPC. Deixe como estão |

**Não abra 5080, 5081, 5100, 5101 nem 8080 para a internet — só a 80.** Identity e Tasks confiam
no chamador para saber quem é o usuário (`X-User-Id` / metadata `x-user-id`, D-30/D-34), o que
só é seguro enquanto o Gateway for o único caminho até eles; e o Gateway confia no nginx para o
`X-Forwarded-For`. No compose isso já vale por construção (sem `ports:`); a verificação abaixo
prova que ninguém desfez isso.

**Verificação de fora, obrigatória (D-32/D-40).** Do seu notebook, **não** de dentro da VM,
contra o **IP externo**:

```bash
for porta in 5080 5081 5100 5101 8080; do
    echo "porta $porta:"
    curl --max-time 3 "http://$IP_EXTERNO:$porta/health"
    echo "  (esperado: timeout ou recusa de conexão, nunca resposta HTTP)"
done
curl --max-time 3 "http://$IP_EXTERNO/"        # esperado: 200 OK, o index.html do Angular
curl --max-time 3 -X POST "http://$IP_EXTERNO/api/tasks" \
     -H "Content-Type: application/json" -d '{"title":"probe"}'
#   esperado: 401 auth.unauthorized — prova que /api/* chegou ao Gateway pelo proxy
```

Regra correta no Console não é prova de porta fechada: um erro de firewall é silencioso, a
aplicação continua funcionando pela 80 e ninguém vê a porta interna aberta até ser tarde.

### IP externo

A VM está `TERMINATED` e **não há IP estático reservado**: ao ligar, recebe um efêmero novo
(o do ensaio, `34.55.12.147`, foi perdido). Os dois scripts de verificação aceitam o endereço por
parâmetro, então dá para trabalhar com efêmero — mas o IP muda a cada parada, e qualquer
material que o cite precisa ser refeito. Para ler o IP do dia:

```bash
gcloud compute instances describe maquina-1-psd --zone us-central1-a \
  --format="value(networkInterfaces[0].accessConfigs[0].natIP)"
```

Para fixar (com a VM já no ar, promove o IP atual a estático sem trocá-lo):

```bash
gcloud compute addresses create maquina-1-psd-ip \
  --project=sd-26-2 --region=us-central1 \
  --addresses="$(gcloud compute instances describe maquina-1-psd --zone=us-central1-a \
      --format='value(networkInterfaces[0].accessConfigs[0].natIP)')"
```

Custa **US$ 0,005/h mesmo com a VM desligada** (~US$ 0,12/dia, ~US$ 2–3 até 22/10), porque um
estático anexado conta como "em uso", ligado ou não. Decisão pendente (seção 13).

## 10. Verificar: logs e smoke test

Logs durante o ensaio/apresentação (os quatro serviços num stream só, com o nome do serviço em
cada linha — bom para seguir o mesmo `traceId`):

```bash
cd /opt/todolist/docker
sudo docker compose -f docker-compose.prod.yml --env-file .env logs -f --since 0s
```

```bash
DEMO_PASSWORD=... ./smoke.sh http://<IP_EXTERNO>      # na VM, padrão http://127.0.0.1
DEMO_PASSWORD=... ./deploy/smoke.sh http://<IP_EXTERNO>   # do notebook (Git Bash), de fora
```

Saída esperada do `smoke.sh`: a rota profunda `/tasks` devolve o `index.html` do Angular (prova
do `try_files` do SPA), e os cenários seguintes (201 com `Location`, 400 de validação, três
casos de 401, usuário inativo) saem `OK`. O mesmo `traceId` aparece nos três serviços:

```bash
sudo docker compose -f /opt/todolist/docker/docker-compose.prod.yml --env-file /opt/todolist/docker/.env \
  logs --since 2m | grep -E 'ValidateUser|CreateTask'
```

## 11. No dia da apresentação

Roteiro de **até 10 minutos, partindo do frontend** (`t2.md`). O limite é duro: estouro zera a
nota da apresentação oral. O SSH do navegador só precisa de HTTPS, que nenhuma rede
institucional bloqueia. O frontend é mostrado num navegador comum em `http://<IP_EXTERNO>/`.
Três lugares: o navegador, um terminal SSH na VM (logs) e um Git Bash no notebook
(`deploy/smoke.sh`, para o 401).

### Preparar

**Antes de tudo, com antecedência: a conta do usuário inativo.** Não existe rota para
desativar conta pela API (decisão consciente: seria superfície de negócio nova), então a conta
do Ato 5 precisa ser cadastrada e desativada por SQL, uma vez só — é o único jeito de provar
RN-AUTH-09:

```bash
# 1. Cadastre pela tela (ou por curl) um e-mail qualquer, ex.: inativo@todolist.example
# 2. Desative-o direto no banco (psql a partir da VM; sudo apt-get install -y postgresql-client):
PGSSLMODE=require psql -h 10.30.240.3 -U todolist -d todolist
UPDATE identity.users SET is_active = false, updated_at = now() WHERE email = 'inativo@todolist.example';
```

Passe o e-mail ao Ato 5 com `DEMO_INACTIVE_EMAIL=inativo@todolist.example`. Sem isso o passo 7 do
script é **pulado com aviso** (não falha, mas não demonstra RN-AUTH-09). Nos ensaios, a conta
`inativo@todolist.example` já existe e está desativada na `banco-1`.

**Terminal 1 — SSH, na VM:**

```bash
cd /opt/todolist/docker
sudo docker compose -f docker-compose.prod.yml --env-file .env logs -f --since 0s
```

`--since 0s` começa sem histórico: tudo que aparecer foi causado durante a apresentação. **Aumente
a fonte** de terminal e navegador: se o `traceId` for para a segunda linha do log, a correlação
deixa de ser visível da última fileira.

**Terminal 2 — Git Bash no notebook**, pronto para o Ato 5:

```bash
export DEMO_PASSWORD="..."
# não execute ainda
```

**Navegador:** aba nova em `http://<IP_EXTERNO>/` (tela de login/cadastro).

### Roteiro cronometrado

| # | Ato | O que fazer / narrar | Ato | Acum. |
|---|---|---|---|---|
| 0 | Abertura | Contexto de 1 frase: Angular → nginx → Gateway → gRPC → Identity/Tasks → Postgres. | 0:20 | 0:20 |
| 1 | **Cadastro + login pelo frontend** | Criar uma conta nova ao vivo (e-mail à vista — prova que é cadastro real), depois login com ela. Narrar: o navegador só fala com o nginx, mesma origem, sem CORS. | 1:20 | 1:40 |
| 2 | **Título vazio → 400** | Criar tarefa sem título; o formulário mostra o erro **sem** round-trip até o Tasks — o Gateway validando na borda. | 0:40 | 2:20 |
| 3 | **Tarefa válida (201) + atrasada** | Criar uma tarefa com título e **uma segunda com vencimento no passado** (a conta é nova e não tem tarefa vencida; sem isso o destaque nunca aparece). Surgem na lista sem recarregar. | 1:10 | 3:30 |
| 4 | **Banco real, por `psql`** | `psql` contra `10.30.240.3` (`PGSSLMODE=require`), `SELECT` em `tasks.tasks` mostrando a linha recém-criada — mesmo `title`/`id` da tela. É a prova de persistência real, não só o `201`. | 1:00 | 4:30 |
| 5 | **401, pelo `smoke.sh`** | No terminal 2: `DEMO_INACTIVE_EMAIL=inativo@todolist.example ./deploy/smoke.sh http://<IP_EXTERNO>`. Três tokens inválidos (sem token, lixo, **adulterado** — exercita a assinatura RS256), todos 401 com o mesmo corpo, e o usuário inativo — mesmo 401, corpo idêntico ao de senha errada (RN-AUTH-09). Mostrar uma vez o interceptor do frontend redirecionando ao login num 401. | 1:30 | 6:00 |
| 6 | **Logs, mesmo `traceId`** | Terminal 1: nos painéis Identity/Tasks/Gateway, localizar o `traceId` da criação do Ato 3 (`grep -E 'CreateTask\|ValidateUser'`). O nginx repassa o `traceparent` intacto, sem participar da correlação (D-40). | 1:00 | 7:00 |
| 7 | **Código** | `AddJwtBearer` do Gateway (BE-40), `CreateTaskHttpRequestValidator`, o handler JSON → `CreateTaskRequest` gRPC, e `tasks.proto` (`CreateTask`/`ListTasks`/`GetTask`, BE-41). | 2:00 | 9:00 |
| — | Encerramento | Buffer deliberado, margem contra qualquer travada. | 1:00 | 10:00 |

A soma dos atos é 9:00; com o buffer cabe em 10:00 **no papel** — só o ensaio confirma. O
cadastro ao vivo do Ato 1 é o item mais provável de estourar: memorize e-mail/senha rápidos de
digitar. Alternativa (decisão de quem apresenta): cadastrar minutos antes e apenas logar no
Ato 1 — nesse caso confira que a conta não tem tarefa atrasada pré-existente que estrague o
Ato 3.

### Requisito do `t2.md` → ato → evidência

| Requisito (`t2.md`, 1–7) | Ato | Evidência |
|---|---|---|
| 1. Frontend funcional, só fala com o Gateway | 1–3 | DevTools → Network mostra só chamadas a `/api/*`, mesma origem |
| 2. API Gateway como ponto único de entrada REST | 1–5 | Toda chamada do frontend e do `smoke.sh` vai para `http://<IP_EXTERNO>/api/*` |
| 3. ≥ 2 microsserviços internos via gRPC | 6, 7 | `traceId` correlacionado Gateway→Tasks→Identity; `.proto` na tela |
| 4. Banco real, persistência **exibida** | 4 | `SELECT` no `psql` mostrando a linha criada no Ato 3 |
| 5. Validação de payload (400/201) | 2, 3 | 400 no título vazio; 201 na tarefa válida |
| 6. Middleware JWT no Gateway (401 na borda) | 5 | Três tokens inválidos, mesmo corpo `auth.unauthorized` |
| 7. Tradução REST → gRPC/Protobuf | 3, 7 | Tarefa criada de fato; handler e `.proto` na tela |

### Checklist da última hora

- [ ] `banco-1` ligada e `maquina-1-psd` **ligada**; IP externo do dia anotado (seção 9).
- [ ] `sudo docker compose -f /opt/todolist/docker/docker-compose.prod.yml --env-file /opt/todolist/docker/.env ps`
      → `frontend`, `gateway`, `identity`, `tasks` em `Up`.
- [ ] Conta do usuário inativo cadastrada e desativada por `SQL` (acima).
- [ ] `DEMO_PASSWORD=... DEMO_INACTIVE_EMAIL=inativo@todolist.example ./smoke.sh` verde na VM
      (contra `http://127.0.0.1`, via nginx).
- [ ] `DEMO_PASSWORD=... DEMO_INACTIVE_EMAIL=inativo@todolist.example ./deploy/smoke.sh
      http://<IP_EXTERNO>` verde, do notebook (Git Bash), **de fora** da VM.
- [ ] Verificação de fora refeita pouco antes: **80** responde; **8080/5080/5081/5100/5101**
      não respondem (seção 9).
- [ ] Aquecimento: uma requisição descartável (`./demo.sh --warmup` ou um login pela tela) — a
      primeira chamada paga conexão HTTP/2 e a primeira query do EF Core; que seja antes da
      plateia.
- [ ] Logs rodando (`logs -f`), fonte aumentada e tela de login visível.
- [ ] Se testou indisponibilidade no ensaio (`docker compose ... stop identity`), o Identity
      foi **religado** (`start identity`). Sem ele, o resultado esperado é `503` com
      `Retry-After: 5`, nunca 401 nem 500.

### Ensaio cronometrado — obrigatório

O roteiro só está pronto depois de executado **contra o relógio**, do zero (navegador fechado,
terminais limpos) até o fim do Ato 7, pelo menos uma vez, de preferência por alguém que não
escreveu o código. A soma da tabela é estimativa; só a execução real prova que cabe em 10
minutos.

**Tempo real do ensaio:** `[PENDENTE — cronometrar do zero ao fim do Ato 7 e registrar aqui antes da apresentação]`

## 12. Sintomas e causas

| Sintoma | Causa provável | O que fazer |
|---|---|---|
| `install-docker-on-vm.sh` aborta no teste de pull com receita de IAM | tag inexistente em `IMAGE_TAG` (o `.env` recém-criado traz `latest`) | conferir `IMAGE_TAG` contra as tags publicadas **antes** de mexer em IAM ou escopo (seção 7) |
| Stack não sobe no boot, pull `unauthorized`/`denied` | `gcloud auth configure-docker` rodado como usuário comum, não como root | rodar como root / reexecutar `install-docker-on-vm.sh` (seção 7) |
| `gcloud sql databases list` responde HTTP 400 | instância parada | `--activation-policy=ALWAYS` (seção 2) |
| `migrate` falha / `identity` e `tasks` não sobem | `.env` com IP/senha errados, ou SQL ausente em `~/docker-deploy/sql` | `docker compose ... logs migrate`; conferir seções 5 e 8 |
| Conexão ao banco recusada | senha com `;` ou `=`, `SSL Mode=Require` ausente, ou usuário `todolist ` (com espaço) | seção 2 |
| `503 identity.unavailable` em tudo | Identity fora do ar | `docker compose ... ps` e `logs identity` |
| `503 identity.unavailable` só no cadastro | PBKDF2 (600.000 iterações) leva ~1,6 s na `e2-small` e estoura o deadline | `Backends__IdentityGrpcTimeoutSeconds: 10` no `docker-compose.prod.yml` — recopiar o compose para a VM; **não** reduzir as iterações |
| `502 Bad Gateway`/`504 Gateway Timeout` na tela | Gateway fora do ar | `docker compose ... ps` e `logs gateway` |
| `404` ao dar F5 numa rota do Angular (ex.: `/tasks`) | `try_files` do nginx do `frontend` ausente/errado | `smoke.sh` confere isto no primeiro passo; ver a configuração em `frontend/nginx.conf.template` |
| Tela do frontend não atualiza depois de novo deploy | `index.html` cacheado pelo navegador | `curl -I` e confirme `Cache-Control: no-cache`; force-refresh |
| Mudança no frontend/serviço não aparece na VM | a imagem não foi republicada, ou `IMAGE_TAG` do `.env` aponta para a tag antiga | `publish-images.ps1`, atualizar `IMAGE_TAG`, `sudo systemctl restart todolist.service` |
| Erro de protocolo no canal gRPC | endpoint não declarado `Http2` | não sobrescreva `Kestrel__Endpoints__Grpc__Url` |
| `http://$IP_EXTERNO:8080/...` responde de fora | regra pública de `tcp:8080` criada | remover a regra (seção 9) |
| `bad interpreter: ...^M` | fim de linha CRLF no `.sh` | `sed -i 's/\r$//' *.sh` |
| `gcloud compute ssh` quer sobrescrever chaves e falha | `~/.ssh` da máquina de desenvolvimento inacessível | `--ssh-key-file=<chave dedicada>` (seção 13) |
| Serviços `todolist-identity`/`todolist-tasks` do T1 ainda ativos na VM | unit systemd antiga ainda instalada, consumindo ~500 MB num box de 2 GB | `sudo systemctl disable --now todolist-identity todolist-tasks` (já feito no ensaio de 30/09) |

## 13. Pendências (a lista que vale para 22/10)

### Feito e verificado em campo (30/09/2026)

API do Artifact Registry habilitada e repositório `todolist` criado; quatro imagens publicadas
(`f2f0e82` e `latest`); `install-docker-on-vm.sh` numa VM real (Debian 13, Docker 29.8.2);
pull autenticado sem mudança de IAM/escopo; stack no ar contra o Cloud SQL (~1,1 GB de RAM
livre); verificação de fora (80 serve o Angular, 8080/5080/5081/5100/5101 não respondem);
`smoke.sh` verde; Identity parado → 503 com `Retry-After: 5`; mesmo `traceId`
nos três serviços; persistência exibida por `SELECT` em `tasks.tasks`.

### Correções de 01/10 ainda não exercitadas numa VM real

- [x] Deadline do Identity em `docker-compose.prod.yml` (`Backends__IdentityGrpcTimeoutSeconds: 10`).
- [x] `install-docker-on-vm.sh`: reconhecimento de "imagem não encontrada" alargado; com a
      credencial confirmada, falha de pull desconhecida avisa e segue em vez de abortar com a
      receita de IAM.
- [x] `smoke.sh` imprime dicas de log do Docker.
- [x] `<title>` do Angular passou a `TodoList`.

O que cada uma exige para chegar na VM (esquecer disto faz a correção parecer que "não
funcionou"):

| Correção | Como chega na VM |
|---|---|
| `<title>` do Angular | **Republicar a imagem** (o Angular é compilado dentro de `todolist-frontend`; as tags de 30/09 não têm a mudança): `publish-images.ps1`, anotar a tag nova, atualizar `IMAGE_TAG` no `.env` da VM |
| Deadline do `docker-compose.prod.yml` | Recopiar o compose (`gcloud compute scp` e `install-docker-on-vm.sh`, ou cópia direta para `/opt/todolist/docker/`) e subir a stack de novo. Sem imagem nova |
| `install-docker-on-vm.sh`, `smoke.sh` | Recopiar os arquivos para a VM |

`frontend/src/index.html` ainda declara `<html lang="en">` numa interface em português —
cosmético, não corrigido.

### No ambiente

- [ ] **IP externo:** reservar (seção 9, ~US$ 2–3 até 22/10) ou ler o IP novo a cada boot.
- [ ] Consertar o `gcloud compute ssh` normal (o `~/.ssh` da máquina de desenvolvimento está
      inacessível; o ensaio usou `--ssh-key-file`, com a pública nos metadados do projeto).
- [ ] Remover o usuário `todolist ` (com espaço) da `banco-1`.
- [ ] Trocar a senha do usuário `todolist` depois da apresentação (a atual passou por uma
      conversa com o assistente).
- [ ] Limpar as contas de teste do ensaio (`demo-t2-*@todolist.example`,
      `ca07-*@todolist.example`), se incomodarem. **Não** apague `inativo@todolist.example`.

### Verificação que só o apresentador pode fazer

- [ ] Passear pelo roteiro no navegador, Atos 1 a 4, contra o IP do dia.
- [ ] **Ensaio cronometrado de 10 minutos**, tempo real registrado no campo `[PENDENTE]` da
      seção 11 — obrigatório (BE-39 CA-03), ainda não feito.

### Depois de cada ensaio

- [ ] Parar a `banco-1` (`--activation-policy=NEVER`) e a VM. **Não apagar** a instância.
- [ ] A `maquina-2-psd` fica desligada: o banco agora é o Cloud SQL, e o T3 vai exigir que ela
      não volte.

### Roteiro curto para religar

```bash
gcloud sql instances patch banco-1 --project=sd-26-2 --activation-policy=ALWAYS
gcloud compute instances start maquina-1-psd --zone us-central1-a
gcloud compute instances describe maquina-1-psd --zone us-central1-a \
  --format="value(networkInterfaces[0].accessConfigs[0].natIP)"   # o IP NOVO
```

A VM sobe a stack sozinha (`todolist.service` está `enabled`) e o `.env` já está preenchido.
Não repita nada das seções 2 a 8; só verifique, com o IP do dia:

```bash
DEMO_PASSWORD=<senha> DEMO_INACTIVE_EMAIL=inativo@todolist.example ./deploy/smoke.sh http://<IP_NOVO>
```
