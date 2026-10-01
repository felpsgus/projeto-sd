# Anatomia dos scripts de deploy

O `README.md` desta pasta é o **runbook**: o que digitar, em que ordem.
Este documento é a **explicação**: o que cada script faz por dentro, por que faz
assim, e o que aconteceria se não fizesse.

Escrito para você conseguir responder, na banca, qualquer pergunta do tipo
"e como isso sobe?" sem precisar abrir o código.

---

## O mapa: quem roda quando

Nenhum script chama o outro — quem encadeia é você.

| Script | Onde roda | Quando | Precisa de root? |
|---|---|---|---|
| `scripts/new-migrations-sql.ps1` | sua máquina (Windows) | antes de copiar para a VM, e a cada migration nova | não |
| `scripts/publish-images.ps1` | sua máquina (Windows) | a cada versão nova do código | não |
| `install-docker-on-vm.sh` | `maquina-1-psd` | uma vez, e a cada vez que compose/unit mudarem | **sim** (`sudo`) |
| `todolist.service` | `maquina-1-psd` (systemd) | no boot, e `systemctl start/restart` | é uma unit (root) |
| `smoke.sh` | `maquina-1-psd` | depois de cada deploy, e ~1h antes da apresentação | não |
| `tmux-demo-docker.sh` | `maquina-1-psd` | montar a tela da apresentação | não (usa `sudo` no `docker`, se preciso) |
| `demo.sh` | dentro do tmux | apoio de linha de comando à apresentação (401) | não |
| `scripts/demo-t2.ps1` | seu notebook (Windows) | o 401 ao vivo, num segundo terminal | não |

O fluxo completo, do seu Windows até a tela projetada:

```
  SUA MÁQUINA                                maquina-1-psd (10.128.0.4)
  ───────────                                ─────────────────────────
  publish-images.ps1
    ├─ docker build  (identity, tasks, gateway, frontend)
    └─ docker push  → Artifact Registry (tag impressa no fim)
  new-migrations-sql.ps1
    └─ dotnet ef migrations script --idempotent → artifacts/sql/*.sql
                    │
                    │  gcloud compute scp (compose, .example, unit, scripts, sql/)
                    ▼
                                    sudo ./install-docker-on-vm.sh
                                      (Docker, /opt/todolist/docker, chave JWT, auth do registry, unit)
                                                  │
                                    preencher /opt/todolist/docker/.env   (IMAGE_TAG!)
                                    sudo systemctl start todolist.service
                                      └─ docker compose up -d: migrate → identity, tasks → gateway → frontend
                                                  │
                                    DEMO_PASSWORD=... ./smoke.sh          ← verificação, via nginx :80
                                                  │
                                    ./tmux-demo-docker.sh → ./demo.sh
                                                  ▲
                    scripts/demo-t2.ps1 -BaseUrl http://<IP_EXTERNO>  (segundo terminal, o 401)
```

---

## 1. `scripts/new-migrations-sql.ps1` — o SQL das migrations

Gera só `artifacts/sql/01-identity.sql` e `artifacts/sql/02-tasks.sql`
(`dotnet ef migrations script --idempotent`, build Release explícito). O serviço
`migrate` dos dois compose files (`docker-compose.yml` e `docker-compose.prod.yml`)
aplica esses arquivos na subida e **falha se a pasta não existir**.

- **Idempotente** (cada migration é guardada pelo histórico `__EFMigrationsHistory`): rodar de novo contra um banco já
  migrado não faz nada. É por isso que o `migrate` pode rodar a cada
  `docker compose up`.
- **O número no nome é a ordem de aplicação.** A FK cruzada
  `tasks.tasks.owner_id -> identity.users(id)` faz o script do Tasks depender da
  tabela criada pelo do Identity; fora de ordem, falha.
- O build é explícito, em Release: sem isso, com os serviços rodando localmente, o
  executável em `bin/Debug` fica travado e o `dotnet ef` falha com `MSB3027` — um
  erro sem relação nenhuma com migration.

---

## 2. `install-docker-on-vm.sh` — preparar a VM

**Rode com `sudo`, de dentro da pasta onde os arquivos foram copiados.** É idempotente:
rodar de novo é o jeito de atualizar compose/unit sem perder o `.env` preenchido nem
a chave JWT.

```bash
set -euo pipefail
```

Três guardas numa linha, e o resto do script depende delas:

- `-e` — **aborta no primeiro comando que falhar**, em vez de seguir com a VM
  pela metade.
- `-u` — variável não definida é erro (protege contra um `rm -rf "$DESTINO/"` com
  `$DESTINO` vazio).
- `-o pipefail` — num pipe `a | b`, falha se **qualquer** parte falhar.

Antes de tocar em qualquer coisa ele confere se é root e se os cinco arquivos
esperados existem (compose, `.env.example`, unit, `sql/01-identity.sql`,
`sql/02-tasks.sql`) — **falhar cedo**: um script de deploy que aborta no meio deixa a
máquina num estado que ninguém projetou.

Etapas, na ordem:

1. **Docker Engine + plugin `compose`** pelo repositório **oficial** da Docker, não
   `apt-get install docker.io`: o pacote da distro costuma ser velho e sem o
   subcomando `docker compose` (v2, sem hífen) que o compose de produção e a unit usam.
2. **`/opt/todolist/docker/`**: copia compose e SQL e cria o `.env` a partir do
   `.example` **só se ainda não existir** (modo `600`, `root`). O script nunca cria nem
   sobrescreve um `.env` preenchido: segredos são preenchidos uma vez, à mão, e
   sobrevivem a qualquer número de reexecuções. Um script que gerasse `.env` sozinho
   acabaria, cedo ou tarde, com um segredo dentro do repositório.
3. **Chave JWT RS256** em `/etc/todolist/jwt/` (`openssl genpkey`, RSA 2048), gerada **só
   se `private.pem` não existir**. Isso tem consequência de segurança, não só de
   conveniência: trocar a chave invalida instantaneamente todo token já emitido (D-38,
   sem rotação), então um script que regerasse o par a cada execução derrubaria toda
   sessão em andamento.
4. **Permissão da chave — a armadilha central do Docker.** Sem Docker Swarm, um
   `secrets:` do compose com `file:` é um bind mount comum: o arquivo chega ao container
   com o **mesmo dono e modo do host**; ninguém intermedeia a leitura. O processo do
   Identity roda como uid **1654** (usuário `app` da imagem `aspnet:10.0` — detalhe da
   imagem base, não constante do .NET). Por isso `private.pem` fica `1654:1654 0400` e
   `public.pem` `1654:1654 0444` (não é segredo, só verifica assinatura). Se uma imagem
   futura mudar o uid, o sintoma é "permission denied" ao ler o PEM, sem nada apontando
   para o uid; reconfira com `docker run --rm mcr.microsoft.com/dotnet/aspnet:10.0 id app`.
   Só o Identity recebe a privada; só o Gateway recebe a pública; o Tasks não recebe
   chave nenhuma.
5. **Autenticação do Docker no Artifact Registry, como root** — ver abaixo.
6. **Instala e habilita `todolist.service`.**

### A autenticação do registry (e o teste de pull)

Quem executa `docker compose up -d` é a unit, **no boot, como root**. Por isso o script
roda `gcloud auth configure-docker us-central1-docker.pkg.dev` **como root**: o alvo é
`/root/.docker/config.json`, o arquivo que a unit lê. Rodado pelo usuário do SSH, grava
em `/home/<você>/.docker/` — um arquivo que o root nunca lê. Testar à mão "funciona" e a
unit falha no boot do mesmo jeito, sem pista de que a causa é "autenticado como a pessoa
errada".

Antes, testa o `docker-credential-gcloud get` e avisa se ele não obtiver token. Depois
tenta um `docker pull` real do `todolist-gateway` **antes de habilitar a unit** — falhar
aqui, com mensagem clara, é muito melhor do que a stack ficar parada no primeiro boot
sem ninguém notar. O desfecho:

- pull funcionou → segue;
- mensagem de imagem/manifest não encontrado (esperado antes da primeira publicação) →
  avisa que a imagem provavelmente ainda não foi publicada e segue;
- qualquer outra falha é tratada como autenticação → aborta e imprime os remédios, nesta
  ordem: conceder `roles/artifactregistry.reader` à service account da VM e, só depois,
  o escopo OAuth da VM (este último exige **parar a VM**).

---

## 3. `todolist.service` — a stack no boot

Unit `Type=oneshot` com `RemainAfterExit=yes`:

```ini
ExecStart=/usr/bin/docker compose -f docker-compose.prod.yml --env-file .env up -d
ExecStop=/usr/bin/docker compose -f docker-compose.prod.yml --env-file .env down
```

- **`oneshot` + `RemainAfterExit=yes`:** o `up -d` sobe os containers em segundo plano e
  o comando termina assim que eles estão criados — não há processo de primeiro plano
  para o systemd supervisionar. `RemainAfterExit` diz "trate a unit como ativa mesmo
  assim"; sem ele, `systemctl status todolist` mostraria `inactive (dead)` segundos após
  o boot, com os containers de pé.
- **`TimeoutStartSec=300`:** o primeiro `up -d` depois de uma imagem nova faz `pull` de
  todas as imagens antes de criar qualquer container; numa `e2-small` isso passa dos 90 s
  padrão do systemd e a unit falharia por timeout com tudo indo bem.
- **Por que não é redundante com `restart: unless-stopped` do compose:** o `restart`
  resolve **um container** que morre com o daemon no ar (exceção não tratada, OOM). A
  unit resolve o **daemon do Docker** reiniciar (reboot, kernel, preempção do GCP): o
  `dockerd` não recria sozinho containers de um `docker compose up` anterior, então sem
  a unit a VM subiria com o Docker rodando e a stack parada. Camadas diferentes (processo
  vs. máquina); nenhuma cobre o caso da outra.

### O que o compose faz na subida (`docker-compose.prod.yml`)

- `migrate` (`postgres:17-alpine`, `restart: "no"`) aplica os dois SQL com
  `ON_ERROR_STOP=1`; `identity` e `tasks` só sobem com `service_completed_successfully`
  — migration falhou, ninguém sobe contra um schema velho.
- Os serviços **não** têm `ports:`; só o `frontend` (nginx) publica a 80 (D-32).
  Falam entre si pelo DNS do compose, nunca `127.0.0.1`.
- `ASPNETCORE_ENVIRONMENT=Production` explícito: em `Development` o serviço leria o
  `appsettings.Development.json` e escutaria em `localhost`, inalcançável de fora.
- `UserStore__Provider=Persisted`: com `InMemory` o Identity aprovaria por gRPC um dono
  que não existe em `identity.users`, e a criação quebraria só no `INSERT`, na FK
  cruzada — falha tardia, no pior momento.
- Segredos do banco só no `.env` (`600`), a connection string em formato Npgsql
  (`ConnectionStrings__IdentityDb`) e, para o `migrate`, em variáveis `PG*` (o `psql`
  não lê formato Npgsql). A senha não pode conter `;` nem `=`.
- Não existe `Jwt__SigningKey`: a chave é arquivo (`Jwt__PrivateKeyPath` no Identity,
  `Jwt__PublicKeyPath` no Gateway), montado como secret.

O duplo sublinhado é a convenção do .NET para hierarquia:
`ConnectionStrings__IdentityDb` no ambiente equivale a `ConnectionStrings:IdentityDb`
no `appsettings.json`, e variável de ambiente vence o arquivo.

---

## 4. `smoke.sh` — a verificação

```bash
DEMO_PASSWORD=... ./smoke.sh                          # contra http://127.0.0.1 (porta 80, via nginx)
DEMO_PASSWORD=... ./smoke.sh http://10.128.0.4
```

Verificação de fumaça, rodada depois de todo deploy e de novo cerca de uma hora antes da
apresentação. Devolve OK/ERRO por cenário e `exit 1` se algo falhou, para encadear em
automação. Fala com o **nginx** (porta 80, sem porta na URL), que repassa `/api/*` ao
Gateway — o mesmo caminho que a plateia vê; o Gateway não é alcançável de outro jeito.

- O primeiro passo nem fala com `/api`: um `GET /tasks` conferindo que o nginx devolve o
  `index.html` do Angular (`<app-root>` no corpo) em vez de 404 — a prova de que o
  `try_files` do SPA está configurado.
- Os demais exigem login de verdade: o script **cadastra uma conta nova a cada
  execução** (`POST /api/auth/register`, e-mail com o instante atual), com
  `DEMO_PASSWORD` como senha, e faz login. Por isso pode rodar duas vezes seguidas sem
  um 409 de e-mail duplicado.
- Inclui um passo com um token **adulterado** (um JWT real do login com um caractere
  trocado), além de "sem token" e "token lixo" — os três exercitam caminhos diferentes
  do middleware de autenticação.
- O passo do **usuário inativo** (RN-AUTH-09) precisa de uma conta desativada por `UPDATE`
  direto no banco (ver `README.md`, "No dia da apresentação"), informada em
  `DEMO_INACTIVE_EMAIL`. Sem ela o passo é **pulado com aviso** — nunca falha em silêncio.
- A verificação é sempre **dupla** (status HTTP **e** `errorCode`/corpo), e a saída
  acumula falhas em vez de abortar na primeira (`set -uo pipefail`, não `-e`): uma falha
  isolada não esconde o resultado dos demais cenários.

---

## 5. `demo.sh` — apoio de linha de comando à apresentação

```bash
DEMO_PASSWORD=... ./demo.sh
DEMO_PASSWORD=... ./demo.sh --warmup     # só a chamada de aquecimento
```

A demonstração de verdade **parte do frontend** no navegador (`README.md`, "No dia da
apresentação"). `demo.sh` cobre o que o navegador não mostra bem sob pressão: os atos de
401, como ensaio/backup em linha de comando do que `scripts/demo-t2.ps1` faz ao vivo
num segundo terminal. `BASE` é `http://127.0.0.1` (via nginx).

Dois cuidados que valem para qualquer roteiro: um **aquecimento** antes do primeiro ato
(a primeira chamada paga conexão HTTP/2 e a primeira query do EF Core — que isso aconteça
enquanto você ainda fala, não diante da banca) e um `trap ... EXIT` em volta de qualquer
ato que pare um serviço de propósito, para religá-lo mesmo se o script for interrompido.

O ato opcional `--falha` (`DEMO_PASSWORD=... ./demo.sh --falha`) derruba o serviço
`identity` com `docker compose stop` (via o helper `dc()`, que usa o compose de
`/opt/todolist/docker`), repete a requisição do Ato 5 e o religa. A flag
`identity_parado` é marcada **antes** do `stop`; se o script morrer no meio do ato, o
`trap` de saída vê a flag e sobe o Identity de volta.
O nginx **não** participa da correlação por `traceId` (D-40: só encaminha bytes, o
`traceparent` atravessa intacto), então não é esperado vê-lo como uma quarta linha do
`grep` de correlação.

---

## 6. `tmux-demo-docker.sh` — a tela

```
┌───────────────────────┬──────────────────────────┐
│                       │  log do IDENTITY         │
│                       ├──────────────────────────┤
│   roteiro (demo.sh)   │  log do TASKS            │
│                       ├──────────────────────────┤
│                       │  log do GATEWAY          │
│                       ├──────────────────────────┤
│                       │  log do FRONTEND (nginx) │
└───────────────────────┴──────────────────────────┘
```

Você tem **uma** sessão SSH pelo navegador e precisa mostrar **cinco** coisas ao mesmo
tempo: o roteiro e os quatro logs. Sem multiplexador, você alternaria entre abas e a
correlação entre painéis — que é justamente o que prova a comunicação entre os
serviços — se perderia. O tmux também protege contra a queda da conexão: a sessão
continua viva no servidor e `tmux attach -t demo-docker` reconecta onde estava.

### Por que montar por script

```bash
tmux new-session -d -s demo-docker -c "$DIR"
tmux split-window -h -t demo-docker:0.0 -c "$DIR"
tmux split-window -v -t demo-docker:0.1 -c "$DIR"
...
```

Dividir painel com `Ctrl+B` ao vivo, com a turma esperando, é onde se perde tempo do
limite de 10 minutos — e às vezes o painel abre no lugar errado. Comandos
determinísticos produzem sempre o mesmo layout. A numeração é posicional: `0.0` é o
painel original; o `split -h` cria `0.1` à direita; cada `split -v` sobre o último
painel da direita empilha mais um abaixo (`0.2`, `0.3`, `0.4`). Se a sessão já
existir, o script apenas **reconecta**.

### O comando de log

```bash
docker compose -f docker-compose.prod.yml --env-file .env logs -f --no-log-prefix --since 0s identity
```

- `-f` — segue o log ao vivo.
- `--since 0s` — **sem histórico**: os painéis começam vazios, e tudo que aparecer foi
  causado por você durante a apresentação.
- `--no-log-prefix` — tira o nome do serviço de cada linha. Numa tela projetada a linha
  precisa caber **sem quebrar**; se um `traceId` for para a segunda linha, o ponto de
  mostrar os logs lado a lado deixa de ser visível da última fileira.

O nginx roda **dentro** do container `frontend`, que já escreve access e error log no
stdout — então um `logs -f frontend` basta, sem `tail` num arquivo.

### Dois cuidados práticos

**Aumente a fonte do terminal antes de começar** — mas com cinco painéis cada um fica
estreito; o `--no-log-prefix` só ajuda até certo ponto.

**Rode `sudo -v` no painel do roteiro antes de começar.** O `sudo` guarda a credencial
**por terminal** (`tty_tickets`), e cada painel do tmux é um terminal diferente; um
`sudo` disparado pelo roteiro pode abrir um prompt de senha no meio da demonstração.

---

## 7. Perguntas prováveis, e o que responder

**"Como você garante que a tarefa não é criada se o Identity estiver fora?"**
O Tasks chama `ValidateUser` por gRPC antes do `INSERT`. Se a chamada falha ou estoura o
deadline, ele devolve `503` e não persiste nada — fail-closed (D-28). Com o Identity
parado (`docker compose stop identity`) a resposta é `503` com `Retry-After: 5`, nunca
401 nem 500.

**"Quem valida o token, e por que o Tasks não faz isso sozinho?"**
O **Gateway** valida localmente, com `AddJwtBearer` e a chave pública (RS256, D-38) — não
pergunta ao Identity a cada requisição. Só o Identity tem a chave que **assina**; a
pública do Gateway só verifica, nunca poderia forjar um token. O Tasks nunca vê o JWT,
só a identidade já resolvida na metadata `x-user-id` (D-34).

**"Por que Identity, Tasks e o próprio Gateway não são acessíveis de fora da VM?"**
Identity e Tasks confiam no chamador para saber quem é o usuário (D-30/D-34) —
segurança que só existe enquanto o Gateway for o único caminho até eles (D-32). O
Gateway confia no nginx para o `X-Forwarded-For`. Por isso só o `frontend` publica
porta (80) e só ela tem regra de firewall liberando `0.0.0.0/0`; as outras cinco
(5080/5081/5100/5101/8080) são verificadas explicitamente, de fora, para confirmar que
**não** respondem.

**"E se a máquina reiniciar?"**
`todolist.service` está `enabled` e roda `docker compose up -d` no boot; o
`restart: unless-stopped` do compose cobre um container que morre sozinho.

**"Onde estão os segredos (senha do banco, chave JWT)?"**
Senha do banco: em `/opt/todolist/docker/.env` na VM, `600 root:root`, fora do
repositório; o `install-docker-on-vm.sh` nunca o sobrescreve. A chave JWT é arquivo, não
variável: `/etc/todolist/jwt/private.pem` (`1654:1654 0400`), montada como secret só no
container do Identity.

**"Por que o banco é Cloud SQL, e como o tráfego chega nele?"**
Banco gerenciado, fora da VM; o tráfego vai pelo peering de IP privado
(`10.30.240.3:5432`) com TLS obrigatório (`ENCRYPTED_ONLY`), o que dispensa regra de
firewall da VPC.
