# Implantação no GCP — runbook do T1 e T2

Passo a passo para colocar os microsserviços de pé e verificar que a comunicação gRPC
funciona lá. Escrito para o fluxo **sem `gcloud` local**: tudo pelo Console web do GCP e pelo
SSH no navegador.

> **Estado destes arquivos.** `scripts/publish.ps1` foi executado e verificado na máquina de
> desenvolvimento. O deploy do T1 nas VMs (`install-on-vm.sh`, os `*.service`, os `.env`) foi
> executado pelo Felipe em 06/09/2026 e os serviços subiram. O upgrade para o T2 (seção
> abaixo) ainda **não foi executado na VM real** — as instruções foram preparadas e revisadas
> no repositório, mas a execução em campo (CA-01 a CA-08 de BE-37) fica registrada como
> pendência até acontecer. **A emenda de BE-42 (nginx, Gateway em `127.0.0.1`, firewall na
> porta 80) é mais recente ainda e também não foi executada na VM** — o `install-on-vm.sh` e
> `deploy/nginx/` que a implementam estavam sendo escritos em paralelo à revisão deste
> documento. Trate o primeiro ensaio com folga, não como formalidade.

> Este arquivo é o **runbook** (o que digitar). Para entender **o que cada script faz por
> dentro e por quê**, veja [ANATOMIA-DOS-SCRIPTS.md](ANATOMIA-DOS-SCRIPTS.md).

> **Dois caminhos de deploy, a partir daqui.** O roteiro de apresentação mudou (22/10): o
> plano agora é uma VM só, tudo em Docker, com o Postgres no Cloud SQL — ver a seção **"9.
> Caminho Docker"**, mais adiante. As seções **1 a 8 abaixo descrevem o caminho systemd/binário
> original (T1/T2), mantido como PLANO B até 22/10** — decisão deliberada do usuário, não
> abandono: `scripts/publish.ps1`, `install-on-vm.sh`, as três units `todolist-*.service` e os
> `.env.example` de cada serviço continuam funcionando exatamente como descrito nelas, e nada
> nesta onda os alterou. Os dois caminhos podem coexistir na mesma VM (pastas diferentes), mas
> só um deve estar servindo a porta 80 de fato antes da apresentação — ver o aviso no topo de
> `deploy/todolist.service`.

## Topologia

| VM | Zona | IP interno | Papel |
|---|---|---|---|
| `maquina-1-psd` | `us-central1-a` | `10.128.0.4` | **nginx** (80, HTTP público — Angular + proxy `/api/*`), Identity Service (5080 `/health`, 5081 gRPC), Tasks Service (5100 `/health`, 5101 gRPC), API Gateway (**127.0.0.1**:8080, só local) |
| `maquina-2-psd` | `us-central1-a` | `10.128.0.5` | PostgreSQL (5432) |

O salto gRPC acontece **dentro** da `maquina-1-psd`, por `127.0.0.1:5081` e `127.0.0.1:5101`.
O que cruza a rede entre VMs é o acesso ao Postgres — e é essa a regra de firewall VPC do
requisito 3 do enunciado.

**Desde BE-42, a origem pública mudou de porta.** Até então a única porta de aplicação
alcançável de fora era a 8080 do Gateway (D-32). Agora é a **80**, servida pelo **nginx** —
um daemon do sistema, não uma unit .NET —, que serve o Angular compilado e faz
`proxy_pass` de `/api/*` para o Gateway. O Gateway recuou para `127.0.0.1:8080`: só o
nginx (mesma máquina) fala com ele agora, o navegador nunca o vê diretamente. O nginx não
decide nada de negócio — se o Gateway cair, o nginx devolve 502/504, não substitui a
resposta por conta própria (**D-40**); toda autenticação, validação e tradução de
protocolo continuam exclusivamente no Gateway. As portas 5080/5081/5100/5101 (backends
gRPC) **e agora também a 8080** (o próprio Gateway) não devem responder de fora da VPC —
ver a verificação explícita mais abaixo.

```
                    internet
                        │
                 porta 80 (pública)
                        ▼
                 ┌─────────────┐
                 │    nginx    │  serve o Angular; proxy_pass /api/* →
                 └──────┬──────┘
                        │ 127.0.0.1:8080
                        ▼
                 ┌─────────────┐        127.0.0.1:5081        ┌──────────┐
                 │   Gateway   │ ───────────────────────────► │ Identity │
                 └──────┬──────┘                              └──────────┘
                        │ 127.0.0.1:5101
                        ▼
                 ┌─────────────┐
                 │    Tasks    │
                 └─────────────┘
```

> Confirme os IPs internos antes de preencher os arquivos de ambiente:
> **Compute Engine → Instâncias de VM**, coluna "IP interno".

## 1. Firewall VPC (Console web)

**VPC network → Firewall → Create firewall rule.** Quatro regras (a quarta mudou de porta no
BE-42 — era `todolist-allow-gateway`/`tcp:8080`, virou `todolist-allow-nginx`/`tcp:80`):

| Nome | Targets (tags) | Source IPv4 ranges | Protocolos/portas | Para quê |
|---|---|---|---|---|
| `todolist-allow-postgres` | `todolist-db` | `10.128.0.4/32` | `tcp:5432` | **a regra do requisito 3** — só a VM de aplicação fala com o banco |
| `todolist-allow-grpc-internal` | `todolist-app` | `10.128.0.0/20` | `tcp:5081,5101` | declara a intenção do canal gRPC na sub-rede (Identity + Tasks) |
| `todolist-allow-iap-ssh` | (em branco = todas) | `35.235.240.0/20` | `tcp:22` | SSH no navegador via IAP |
| `todolist-allow-nginx` | `todolist-app` | `0.0.0.0/0` | `tcp:80` | **a única regra de ingresso de aplicação vinda da internet** (BE-42, supera D-32 na porta — o Gateway deixou de ser público) |

> **Se você já tinha a regra antiga `todolist-allow-gateway` (`tcp:8080`, pública) de uma
> execução anterior desta seção:** feche/remova-a — não basta criar a nova em cima; a 8080
> pública precisa deixar de existir, senão o Gateway continua alcançável de fora mesmo com
> o nginx no ar, e a comprovação de campo abaixo (8080 não responde) falharia.

Direção `Ingress`, ação `Allow`, prioridade `1000` nas quatro.

Depois marque as VMs com as tags — **Compute Engine → a VM → Editar → Tags de rede**:

- `maquina-1-psd` → `todolist-app`
- `maquina-2-psd` → `todolist-db`

> **A regra de gRPC interno é intencionalmente redundante.** Como os três serviços estão na
> mesma VM, o gRPC não depende dela — o caminho crítico é `127.0.0.1`. Ela existe para
> declarar a intenção do canal e para ser o artefato concreto quando a pergunta "mostre a
> regra que deixa seus serviços conversarem" aparecer na banca. A regra que está de fato no
> caminho crítico do tráfego entre VMs é a do Postgres.

> **Não abra 5080, 5081, 5100, 5101 nem 8080 para a internet — só a 80.** Desde o T2, Identity
> e Tasks confiam no chamador para saber quem é o usuário (`X-User-Id` / metadata `x-user-id`,
> **D-30**/**D-34**): isso só é seguro enquanto o Gateway for o único caminho até eles. Desde
> BE-42, o próprio Gateway entra nessa mesma lista de portas que não podem responder de fora —
> ele confia no nginx para o `X-Forwarded-For` (`ForwardedHeaders` com `KnownProxies` restrito
> a `127.0.0.1`), e isso só vale enquanto ninguém alcançar a 8080 diretamente.

> **Verificação de fora, obrigatória (D-32/D-40, CA-03/CA-04 de BE-42 — substitui e amplia a
> verificação equivalente de BE-37, que cobria só 5080/5081/5100/5101).** Do seu notebook,
> **não** de dentro da VM, contra o **IP externo** da `maquina-1-psd`:
>
> ```bash
> for porta in 5080 5081 5100 5101 8080; do
>     echo "porta $porta:"
>     curl --max-time 3 "http://$IP_EXTERNO:$porta/health"
>     echo "  (esperado: timeout ou recusa de conexão, nunca resposta HTTP)"
> done
> curl --max-time 3 "http://$IP_EXTERNO/"        # esperado: 200 OK, o index.html do Angular
> curl --max-time 3 -X POST "http://$IP_EXTERNO/api/tasks" \
>      -H "Content-Type: application/json" -d '{"title":"probe"}'
> #   esperado: 401 auth.unauthorized — prova que /api/* chegou ao Gateway pelo proxy do
> #   nginx (o Gateway não tem /health sob /api/; só as rotas de negócio ficam ali).
> ```
>
> Uma regra de firewall mal escrita é um erro silencioso: a aplicação continua funcionando
> via nginx e ninguém percebe que uma porta interna (ou o próprio Gateway) também ficou
> aberta até ser tarde. Só a comprovação de campo conta — não "confiar na regra". **Esta
> verificação ainda não foi executada contra uma VM real** — ver a pendência no fim deste
> arquivo.

### 1.1 IP externo estático (T2)

**Compute Engine → Endereços IP → Promover a estático**, na `maquina-1-psd`. Um IP efêmero
pode trocar se a VM for parada e reiniciada; promover **depois** que ele já mudou é tarde
demais, porque qualquer material de apresentação (slide, script salvo) que cite o IP
precisaria ser refeito. Reservar com antecedência custa uma tela do Console e elimina o
risco — faça isso bem antes do ensaio, não no dia.

## 2. PostgreSQL na `maquina-2-psd`

Abra o SSH no navegador: **Compute Engine → Instâncias de VM → `maquina-2-psd` → SSH**.

```bash
sudo apt-get update && sudo apt-get install -y postgresql

# Um único papel, dono dos dois schemas. Separar em dois usuários complicaria a FK
# cruzada (tasks.tasks.owner_id -> identity.users), que exige REFERENCES no schema
# do outro serviço — custo desproporcional para esta etapa.
sudo -u postgres psql <<'SQL'
CREATE ROLE todolist LOGIN PASSWORD 'ESCOLHA_UMA_SENHA_FORTE';
CREATE DATABASE todolist OWNER todolist;
SQL
```

Aceitar conexão só da VM de aplicação:

```bash
sudo sed -i "s/^#*listen_addresses.*/listen_addresses = 'localhost,10.128.0.5'/" \
  /etc/postgresql/*/main/postgresql.conf

echo "host    todolist    todolist    10.128.0.4/32    scram-sha-256" \
  | sudo tee -a /etc/postgresql/*/main/pg_hba.conf

sudo systemctl restart postgresql
sudo systemctl is-active postgresql
```

Três camadas independentes protegendo a 5432: a regra de firewall, o `listen_addresses` e o
`pg_hba.conf`. Nenhuma sozinha é suficiente — a de firewall é a que mais some quando alguém
"arruma" a rede depois.

> A credencial `postgres/postgres` do `docker-compose.yml` é de desenvolvimento local e **não
> pode** atravessar a rede. Aqui a senha é real e vive só nos arquivos de ambiente da
> `maquina-1-psd`, com permissão 600.

## 3. Runtime .NET na `maquina-1-psd`

SSH no navegador na `maquina-1-psd`. O pacote é framework-dependent (25 MB em vez de 280),
então a VM precisa do runtime **ASP.NET Core 10** — o runtime, não o SDK.

> **`apt-get install aspnetcore-runtime-10.0` falha de saída** com
> `Unable to locate package`. Isso é esperado: nenhuma imagem padrão do Compute Engine traz
> .NET 10 nos repositórios dela. É preciso ou adicionar o repositório da Microsoft, ou usar o
> instalador oficial — abaixo, nessa ordem de preferência.

### Caminho A — instalador oficial (recomendado)

Independe de distro e de versão, não mexe no gerenciador de pacotes e não conflita com nada
que a imagem já tenha:

```bash
sudo apt-get update && sudo apt-get install -y curl postgresql-client

curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
chmod +x dotnet-install.sh
sudo ./dotnet-install.sh --channel 10.0 --runtime aspnetcore --install-dir /usr/share/dotnet

sudo ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
```

**Registre o local da instalação** — este passo é o que costuma faltar:

```bash
sudo mkdir -p /etc/dotnet
echo /usr/share/dotnet | sudo tee /etc/dotnet/install_location
```

O `dotnet-install.sh` instala o runtime mas **não** avisa o sistema onde ele ficou. Nossos
serviços não são iniciados por `dotnet App.dll` — eles têm um executável nativo próprio
(`TodoList.Identity.Api`), e esse executável procura o runtime em `DOTNET_ROOT`, depois em
`/etc/dotnet/install_location`, depois no caminho padrão. Sem o registro, o serviço morre no
systemd com uma mensagem sobre framework não encontrado, e o `dotnet --list-runtimes` continua
respondendo certinho — o que manda você investigar o lugar errado.

### Caminho B — repositório da Microsoft

Só se você preferir gerenciar por `apt`. Confira a distro primeiro:

```bash
cat /etc/os-release        # anote ID (debian/ubuntu) e VERSION_ID (12, 24.04, ...)

# troque debian/12 pelo que corresponder — ex.: ubuntu/24.04
wget https://packages.microsoft.com/config/debian/12/packages-microsoft-prod.deb -O ms-prod.deb
sudo dpkg -i ms-prod.deb && rm ms-prod.deb

sudo apt-get update
sudo apt-get install -y aspnetcore-runtime-10.0 postgresql-client
```

> No Ubuntu, o feed da Microsoft e o do próprio Ubuntu publicam pacotes .NET com os mesmos
> nomes, e a mistura dos dois produz erros de dependência difíceis de desfazer. É a razão de o
> caminho A ser o recomendado a cinco dias da apresentação.

### Verificar antes de seguir

```bash
dotnet --list-runtimes | grep Microsoft.AspNetCore.App
```

Precisa aparecer uma linha `10.x`. O teste que vale de verdade, porém, é o executável nativo
achar o runtime — e isso só dá para confirmar depois do passo 4, com:

```bash
~/todolist-deploy/publish/identity/TodoList.Identity.Api --help 2>&1 | head -5
```

Qualquer saída que não seja erro de framework serve: significa que o apphost encontrou o
runtime. `Ctrl+C` se ele começar a subir.

> Alternativa sem instalar nada na VM: `./scripts/publish.ps1 -SelfContained` empacota o
> runtime junto. O tarball vai a ~280 MB, inviável pelo upload do navegador — só vale se você
> passar a ter `gcloud scp`.

## 4. Empacotar (na sua máquina) e subir

```powershell
./scripts/publish.ps1
```

Roda build em Release, a suíte inteira, e produz `artifacts/todolist-deploy.tar.gz`. Desde
BE-42, o tarball também carrega `publish/frontend/` — o build de produção do Angular
(`ng build`), ao lado dos três `publish/<serviço>/` já existentes — é o que
`install-on-vm.sh` copia para o lugar que o `root` do nginx espera. O tamanho do pacote
cresce um pouco por causa disso; ainda cabe no upload pelo SSH do navegador.

No SSH do navegador da `maquina-1-psd`: **engrenagem (canto superior direito) → Fazer upload de
arquivo** → escolha o `todolist-deploy.tar.gz`. Ele cai no home do seu usuário.

```bash
mkdir -p ~/todolist-deploy && tar -xzf ~/todolist-deploy.tar.gz -C ~/todolist-deploy
cd ~/todolist-deploy
chmod +x *.sh
```

> O `chmod +x` não é opcional: o bit de execução não sobrevive de forma confiável a um tarball
> gerado no NTFS.

## 5. Segredos e migrations

Os arquivos de ambiente são criados **uma vez** e sobrevivem a todos os redeploys:

```bash
sudo mkdir -p /etc/todolist
sudo cp identity.env.example /etc/todolist/identity.env
sudo cp tasks.env.example    /etc/todolist/tasks.env
sudo nano /etc/todolist/identity.env    # troque TROQUE_ESTA_SENHA e confira o IP do banco
sudo nano /etc/todolist/tasks.env       # idem
sudo chmod 600 /etc/todolist/*.env
sudo chown root:root /etc/todolist/*.env
```

Migrations — **Identity primeiro** (a FK cruzada do Tasks depende da tabela criada pelo
Identity; fora de ordem falha):

```bash
export PGPASSWORD='A_SENHA_QUE_VOCE_ESCOLHEU'
psql -h 10.128.0.5 -U todolist -d todolist -v ON_ERROR_STOP=1 -f sql/01-identity.sql
psql -h 10.128.0.5 -U todolist -d todolist -v ON_ERROR_STOP=1 -f sql/02-tasks.sql
unset PGPASSWORD
```

Se o `psql` travar aqui, o problema é a seção 1 ou a 2 — não continue antes de resolver.

## 6. Instalar e verificar

```bash
sudo ./install-on-vm.sh
```

O script cria o usuário de serviço, copia os binários para `/opt/todolist`, instala as units,
sobe o Identity, espera o health check, sobe o Tasks e mostra a situação. Ele **se recusa a
continuar** se algum `.env` faltar ou ainda tiver a senha placeholder.

```bash
./smoke.sh
```

Esperado:

```
  OK    dono ativo -> cria                    HTTP 201
  OK    dono inexistente -> Identity nega     HTTP 404 task.owner_not_found
  OK    dono inativo -> Identity nega         HTTP 409 task.owner_inactive
  OK    sem X-User-Id -> validacao            HTTP 400
```

E a evidência da chamada de rede — o mesmo `traceId` nos dois serviços:

```bash
sudo journalctl -u todolist-tasks -u todolist-identity --since '2 min ago' | grep ValidateUser
```

**Redeploy** depois de mudar código: `./scripts/publish.ps1`, novo upload, extrair,
`sudo ./install-on-vm.sh`. As migrations só quando houver migration nova.

## 7. T2 — upgrade da VM do T1

Esta seção é um **upgrade**, não uma reinstalação. `install-on-vm.sh` já é idempotente e
nunca recria os `.env` — o T2 usa exatamente esse mecanismo. Reinstalar do zero jogaria fora
os usuários já semeados e obrigaria recriar segredos que já estão corretos.

**O que muda:** um terceiro serviço (o API Gateway) passa a existir, `Tasks__AllowAnonymousCreate`
sai do Tasks e o Identity passa a exigir senha de demonstração explícita. O banco **já tem** as
linhas de `identity.users` com o hash placeholder do T1 — nada precisa ser recriado; o seed, ao
rodar de novo, vê que a senha não confere com o hash armazenado e o regrava (BE-33 CA-09). **Não
há migration nova.**

> **Emenda (BE-42, 21/09/2026) — nginx na frente de tudo, Gateway recua para `127.0.0.1`.**
> Um quarto processo entra em cena, e ele **não** é uma unit .NET: o nginx, instalado e
> configurado pelo próprio `install-on-vm.sh`, passa a ser a única origem pública (porta 80),
> servindo o Angular compilado e repassando `/api/*` ao Gateway. O Gateway deixa de escutar em
> `0.0.0.0:8080` e passa para `127.0.0.1:8080` — só o nginx (mesma máquina) fala com ele. A
> regra de firewall pública muda de porta (8080 → 80, ver seção 1) e o tarball de
> `scripts/publish.ps1` ganha `publish/frontend/` (seção 4). Os passos abaixo já refletem essa
> mudança; o texto anterior a 21/09 falava em abrir 8080 para a internet — isso **não vale
> mais**.

> **Emenda (BE-40, 21/09/2026) — JWT deixou de ser HS256 (chave em `.env`) e virou RS256 (par de
> arquivos PEM gerado pelo próprio `install-on-vm.sh`).** Se você já tinha uma VM do T2 com
> `Jwt__SigningKey` em `identity.env` de uma execução anterior desta seção, remova essa linha —
> ela não existe mais em nenhum `appsettings*.json`/`.env` do Identity, e uma linha
> `Jwt__SigningKey` esquecida não quebra nada sozinha (o Identity simplesmente ignora uma
> variável que `JwtOptions` não lê mais), mas é lixo que confunde numa auditoria.

1. **Firewall e IP** — se ainda não feito: seção 1 (regra `todolist-allow-nginx` em `tcp:80`,
   revisão de `todolist-allow-grpc-internal`, **fechamento/remoção** de uma eventual
   `todolist-allow-gateway` antiga em `tcp:8080`) e seção 1.1 (IP estático).

2. **Editar os `.env` existentes na VM**, na sessão SSH da `maquina-1-psd`:

   ```bash
   sudo nano /etc/todolist/identity.env
   ```

   Acrescente (sem chave JWT nenhuma aqui — ela deixou de ser configuração de `.env`, ver passo 4):

   ```
   Jwt__Issuer=todolist-identity
   Jwt__Audience=todolist
   ```

   Onda E (T2): não acrescente `UserStore__DemoUserPassword` — essa variável (e
   `UserStore__SeedDemoUsers`) foi removida junto com o seed de demonstração. Cadastro é real agora
   (`POST /api/auth/register`); veja a seção 8 abaixo para o roteiro de apresentação.

   ```bash
   sudo nano /etc/todolist/tasks.env
   ```

   Remova a linha `Tasks__AllowAnonymousCreate=true` (e o comentário acima dela) — o gatilho
   REST provisório foi removido (BE-35); o Tasks agora é só gRPC. O Tasks não recebe, e nunca deve
   ganhar, nenhuma chave `Jwt:*` (BE-40, D-38).

   ```bash
   sudo cp ~/todolist-deploy/gateway.env.example /etc/todolist/gateway.env
   sudo chmod 600 /etc/todolist/gateway.env
   sudo chown root:root /etc/todolist/gateway.env
   ```

   O `gateway.env` só tem endereços — nenhum segredo a preencher, mas confira os dois
   endereços `127.0.0.1` antes de seguir. `Jwt__PublicKeyPath` também não vai neste arquivo: já
   vem fixado em `todolist-gateway.service` (`Environment=`), instalado no passo 4.

3. **Empacotar e subir**, do mesmo jeito do T1:

   ```powershell
   ./scripts/publish.ps1
   ```

   Upload do novo `todolist-deploy.tar.gz` pelo SSH do navegador, extraindo **por cima** do
   `~/todolist-deploy` existente:

   ```bash
   tar -xzf ~/todolist-deploy.tar.gz -C ~/todolist-deploy
   cd ~/todolist-deploy
   chmod +x *.sh
   ```

4. **Instalar** — agora com o terceiro serviço **e o nginx**:

   ```bash
   sudo ./install-on-vm.sh
   ```

   O script confere os três `.env` (recusa continuar se `gateway.env` estiver faltando ou com
   placeholder, mesmo padrão de `identity.env`/`tasks.env`), instala a unit
   `todolist-gateway.service` e sobe os três serviços **na ordem Identity → Tasks → Gateway**,
   esperando o `/health` de cada um antes de seguir para o próximo — a mesma lógica de espera
   por condição que já existia entre Identity e Tasks, estendida a mais um salto. É a mesma
   ordem para qualquer restart manual depois:

   **Novo nesta execução (BE-42):** o script também instala o pacote `nginx`, copia
   `deploy/nginx/todolist.conf` para o lugar que a distro espera, roda `nginx -t` antes de
   qualquer `reload`/`restart`, e sobe/recarrega o serviço — mesma filosofia idempotente dos
   outros passos. O `root` do site aponta para onde `publish/frontend/` foi extraído. Depois
   do install, confirme:

   ```bash
   sudo nginx -t
   sudo systemctl is-active nginx
   ```

   **Novo nesta execução (BE-40, D-38):** antes de instalar as units, o script gera — só se
   `/etc/todolist/jwt/private.pem` ainda não existir — o par de chaves RSA 2048 com `openssl
   genpkey`/`openssl pkey -pubout`, direto na VM. `private.pem` fica `root:root 0400` e só chega
   à unit do Identity via `LoadCredential=`; `public.pem` fica `root:root 0444`, legível por
   qualquer processo da VM (inclusive o Gateway, que a lê direto do caminho fixo). É idempotente
   e silencioso: numa segunda execução (redeploy) o script vê que o par já existe e **não
   regera** — trocar a chave invalidaria toda sessão em andamento.

   **Verificação manual obrigatória (CA-25 de BE-40) — registre no PR.** As três units rodam como
   o mesmo usuário `todolist`; a garantia de que o Gateway e o Tasks não conseguem ler a chave
   privada do Identity não vem de rodarem como usuários diferentes (não rodam), vem de
   `private.pem` ser `root:0400` e só a unit do Identity recebê-la via `LoadCredential=`. Confirme
   isso de fato, depois do install:

   ```bash
   sudo -u todolist cat /etc/todolist/jwt/private.pem
   # esperado: Permission denied — se isso imprimir o PEM, a permissão 0400/root
   # não foi aplicada e a chave está exposta ao mesmo usuário que roda Gateway e Tasks.
   ```

   ```bash
   ls -l /etc/todolist/jwt/
   # esperado: private.pem -r-------- root root ; public.pem -r--r--r-- root root
   ```

   ```bash
   sudo systemctl restart todolist-identity && sleep 2 \
     && sudo systemctl restart todolist-tasks && sleep 2 \
     && sudo systemctl restart todolist-gateway
   ```

   **Por que essa ordem:** o Gateway é cliente gRPC dos outros dois (D-33). Reiniciá-lo
   primeiro não quebra nada de fato (ele reconecta na primeira chamada), mas subir os
   back-ends primeiro evita que as primeiras requisições reais — inclusive as do ensaio —
   encontrem 503 por um back-end ainda de pé.

5. **Conferir a partir de fora**, IP externo, porta 80 (**não** mais 8080 — o Gateway recuou
   para `127.0.0.1`, BE-42), antes de considerar a VM pronta (ver o bloco de verificação na
   seção 1):

   ```bash
   curl --max-time 3 "http://$IP_EXTERNO/"
   DEMO_PASSWORD=... ./smoke.sh "http://$IP_EXTERNO"
   ```

   `smoke.sh` já confere, como primeiro passo, que a rota profunda do SPA (`/tasks`) devolve
   o `index.html` do Angular em vez de 404 (BE-42 CA-01) — ver
   [ANATOMIA-DOS-SCRIPTS.md](ANATOMIA-DOS-SCRIPTS.md).

> **Tempo do roteiro de subida (CA-06 de BE-37).** Meça, em pelo menos uma execução real, o
> tempo do upload do tarball até as três units `active` e a verificação de fora respondendo, e
> registre aqui:
>
> `[PENDENTE — medir na primeira execução real na VM]`

## 8. No dia da apresentação

> **Emenda (21/09/2026) — `t2.md` novo: até 10 minutos, partindo do frontend.** O roteiro
> abaixo substitui a versão anterior (5 minutos, começando por `curl` direto no Gateway). O
> limite **dobrou**, mas a exigência de ensaio cronometrado (mais abaixo) não afrouxou — o
> enunciado novo continua zerando a nota da apresentação oral em caso de estouro.

Sem `gcloud` não há túnel IAP — e tudo bem, porque **rodar de dentro da VM é a opção mais
robusta mesmo** para o terminal de apoio: o SSH do navegador só precisa de HTTPS, que nenhuma
rede institucional bloqueia. O **frontend**, porém, é mostrado num navegador comum, apontado
para `http://<IP_EXTERNO>/` — a origem pública servida pelo nginx (BE-42). Dois lugares em
jogo, então: o navegador (frontend) e **dois** terminais — um com `tmux` na VM (roteiro de
apoio + logs), outro no seu notebook Windows (`scripts/demo-t2.ps1`, para o 401).

### Preparar os dois terminais

**Terminal 1 — SSH no navegador, na `maquina-1-psd`:**

```bash
sudo apt-get install -y tmux     # uma vez
cd ~/todolist-deploy
./tmux-demo.sh                   # monta a tela em 5 painéis e entra nela
```

```
┌───────────────────────┬──────────────────────────┐
│                       │  log do IDENTITY         │
│                       ├──────────────────────────┤
│   roteiro (demo.sh)   │  log do TASKS            │
│                       ├──────────────────────────┤
│                       │  log do GATEWAY          │
│                       ├──────────────────────────┤
│                       │  log do NGINX (acesso)   │
└───────────────────────┴──────────────────────────┘
```

Atalhos de tmux que importam: `Ctrl+B` + seta navega entre painéis, `Ctrl+B d` sai sem matar a
sessão, `tmux attach -t demo` volta.

**Terminal 2 — PowerShell no seu notebook**, pronto para disparar o 401 no Ato 5 (sem digitar
nada sob pressão):

```powershell
$env:DEMO_PASSWORD = "..."
# não execute ainda — o Ato 5 abaixo é a hora
```

**Navegador — aba nova**, apontada para `http://<IP_EXTERNO>/` (a tela de login/cadastro).

**Antes de tudo isso, com antecedência (não durante os 10 minutos): a conta do usuário
inativo.** Onda E removeu o seed de demonstração — não existe rota para desativar uma conta
pela API (decisão consciente: seria superfície de negócio nova, fora de escopo), então a conta
usada no Ato 5 (o 401 de usuário inativo, dentro do `demo-t2.ps1`) precisa ser cadastrada e
desativada por `SQL` antes da apresentação, uma vez só:

```bash
# 1. Cadastre pela tela (ou por curl) um e-mail qualquer, ex.: inativo@todolist.example
# 2. Desative-o direto no banco (maquina-2-psd, psql):
UPDATE identity.users SET is_active = false, updated_at = now() WHERE email = 'inativo@todolist.example';
```

Passe esse e-mail para o Ato 5 com `-InactiveEmail inativo@todolist.example`. Sem isso, o passo
7 do script é **pulado com aviso** — nunca falha silenciosamente, mas também não demonstra
RN-AUTH-09 se você esquecer de preparar a conta.

> **Aumente a fonte de tudo antes.** Terminal e navegador — se um identificador de
> correlação for para a segunda linha do log, ou o formulário for pequeno demais para a
> última fileira ler o `400`, o ponto da demonstração deixa de ser visível.

### Roteiro cronometrado — até 10 minutos, com folga

| # | Ato | O que fazer / narrar | Tempo do ato | Acumulado |
|---|---|---|---|---|
| 0 | Abertura | Contexto de 1 frase: Angular → nginx → Gateway → gRPC → Identity/Tasks → Postgres. | 0:20 | 0:20 |
| 1 | **Cadastro + login pelo frontend** | Tela de cadastro: criar uma conta nova, ao vivo (e-mail à vista da plateia — prova que é cadastro real, não um usuário fixo pronto), depois login com essa mesma conta. Narrar: o navegador só fala com o nginx, mesma origem, sem CORS. | 1:20 | 1:40 |
| 2 | **Título vazio → 400** | Tentar criar tarefa sem título; o formulário mostra o erro **sem** round-trip até o Tasks — é o Gateway validando na borda. | 0:40 | 2:20 |
| 3 | **Tarefa válida (201) + tarefa atrasada** | Criar uma tarefa com título e, em seguida, **uma segunda com vencimento no passado** — a conta é nova, então não tem tarefa vencida; sem criar uma agora, o destaque de atrasada nunca aparece na tela. As duas surgem na lista sem recarregar a página; aponte o destaque visual da atrasada. | 1:10 | 3:30 |
| 4 | **Banco real, por `psql`** | No painel do roteiro (ou um painel extra), `psql` contra `10.128.0.5`, `SELECT` em `tasks.tasks` mostrando a linha recém-criada — mesmo `title`/`id` da tela. É a prova ao vivo de persistência real do `t2.md`, não só o `201`. | 1:00 | 4:30 |
| 5 | **401, pelo `demo-t2.ps1`** | No **terminal 2** (notebook): `./scripts/demo-t2.ps1 -BaseUrl http://<IP_EXTERNO> -Password $env:DEMO_PASSWORD -InactiveEmail inativo@todolist.example`. Narrar os três casos de token inválido — sem token, token lixo, token **adulterado** (exercita a assinatura RS256) — todos 401 com o mesmo corpo, e o passo do usuário inativo (a conta preparada com antecedência, ver acima) — mesmo 401, corpo idêntico ao de senha errada (RN-AUTH-09). Mostrar rapidamente o interceptor do frontend redirecionando ao login num 401 (uma vez, não repetido para os três). | 1:30 | 6:00 |
| 6 | **Logs, mesmo `traceId`** | Voltar ao terminal 1 (tmux); nos três painéis (Identity/Tasks/Gateway), localizar o mesmo `traceId` da criação do Ato 3 — `grep -E 'ValidateToken\|CreateTask\|ValidateUser'`. Mencionar que o nginx (painel de baixo) repassa o `traceparent` intacto, sem participar da correlação (D-40). | 1:00 | 7:00 |
| 7 | **Código** | Tela de código: middleware `AddJwtBearer` do Gateway (BE-40), o validador de payload (`CreateTaskHttpRequestValidator`), o handler que traduz JSON → `CreateTaskRequest` gRPC, e `tasks.proto` (`CreateTask`/`ListTasks`/`GetTask`, BE-41). | 2:00 | 9:00 |
| — | Encerramento/perguntas | Buffer deliberado — não é tempo "sobrando", é a margem contra qualquer travada. | 1:00 | 10:00 |

**Soma dos atos (sem o buffer): 9:00.** Com o buffer de encerramento, o roteiro cabe
exatamente nos 10 minutos **no papel** — o ensaio cronometrado (obrigatório, ver abaixo) é o
que confirma isso na prática, não a soma aritmética. O cadastro ao vivo do Ato 1 é o item mais
novo desta tabela (Onda E) e o mais provável de estourar o tempo estimado num primeiro ensaio —
prefira ter o formulário memorizado (e-mail/senha rápidos de digitar) a economizar no roteiro.

### Tabela requisito do `t2.md` → ato → evidência

| Requisito (`t2.md`, 1–7) | Ato | Evidência |
|---|---|---|
| 1. Frontend funcional, só fala com o Gateway | 1–3 | Interação na tela; DevTools → Network mostra só chamadas a `/api/*`, mesma origem |
| 2. API Gateway como ponto único de entrada REST | 1–5 | Toda chamada do frontend e do `demo-t2.ps1` vai para `http://<IP_EXTERNO>/api/*` |
| 3. ≥ 2 microsserviços internos via gRPC | 6, 7 | `traceId` correlacionado Gateway→Tasks→Identity; `.proto` na tela |
| 4. Banco real, persistência **exibida** | 4 | `SELECT` no `psql` mostrando a linha criada no Ato 3 |
| 5. Validação de payload (400/201) | 2, 3 | 400 no título vazio; 201 na tarefa válida |
| 6. Middleware JWT no Gateway (401 na borda) | 5 | Três variações de token inválido, mesmo corpo `auth.unauthorized` |
| 7. Tradução REST → gRPC/Protobuf | 3, 7 | Tarefa criada de fato; handler e `.proto` na tela |

### Checklist da última hora

- [ ] As duas VMs **ligadas** (Compute Engine → Instâncias de VM).
- [ ] `sudo systemctl is-active todolist-identity todolist-tasks todolist-gateway` → `active`
      nos três; `sudo systemctl is-active nginx` → `active`.
- [ ] A conta do usuário inativo já cadastrada e desativada por `SQL` (ver acima) — sem ela, o
      passo 7 de `demo-t2.ps1`/`smoke.sh` é pulado, e RN-AUTH-09 fica sem demonstração.
- [ ] `DEMO_PASSWORD=... DEMO_INACTIVE_EMAIL=inativo@todolist.example ./smoke.sh` verde na VM
      (padrão contra `http://127.0.0.1`, via nginx) — inclui a checagem da rota `/tasks`
      (BE-42 CA-01).
- [ ] `./scripts/demo-t2.ps1 -BaseUrl http://<IP_EXTERNO> -Password ... -InactiveEmail
      inativo@todolist.example` verde, do notebook, **de fora** da VM.
- [ ] Verificação de fora feita de novo pouco antes: **80** responde (Angular); **8080**,
      **5080**, **5081**, **5100**, **5101** **não** respondem (seção 1).
- [ ] Aquecimento: uma requisição descartável disparada (`./demo.sh --warmup` ou um login
      manual pela tela) — a primeira chamada paga conexão HTTP/2 e a primeira query do EF
      Core; que isso aconteça antes da plateia.
- [ ] `tmux` montado, fonte do terminal e do navegador aumentadas, os cinco painéis e a tela
      de login visíveis.
- [ ] A conta cadastrada ao vivo no Ato 1 nasce sem tarefas (conta nova) — não há mais um
      usuário fixo com histórico para conferir aqui. Se preferir não cadastrar ao vivo por
      segurança do tempo, cadastre a conta minutos antes e apenas logue no Ato 1 (decisão de
      quem apresenta) — nesse caso confira que ela não tem tarefa atrasada pré-existente que
      estrague a narrativa do Ato 3.
- [ ] O Identity **religado**, se você testou a indisponibilidade no ensaio.

### Ensaio cronometrado — obrigatório, não opcional

O roteiro só está pronto depois de ser executado **contra o relógio**, do zero (navegador
fechado, terminais limpos) até o fim do Ato 7, pelo menos uma vez — mesma exigência de
"alguém que não escreveu o código" já usada no T1, adaptada ao cronômetro. A soma da tabela
acima é uma estimativa de planejamento; o enunciado zera a apresentação oral por estouro de
tempo, e só a execução real prova que os 10 minutos cabem.

**Tempo real do ensaio:** `[PENDENTE — cronometrar numa execução real, do zero ao fim do Ato 7, e registrar aqui antes da apresentação]`

## Sintomas e causas

| Sintoma | Causa provável | O que fazer |
|---|---|---|
| `install-on-vm.sh` para dizendo que falta `.env` | arquivos de ambiente não criados | seção 5 |
| Serviço em `activating (auto-restart)` | erro de inicialização | `sudo journalctl -u todolist-identity -n 60 --no-pager` |
| `Unable to locate package aspnetcore-runtime-10.0` | nenhuma imagem padrão traz .NET 10 no `apt` | caminho A da seção 3 (`dotnet-install.sh`) |
| Serviço falha com "framework not found", mas `dotnet --list-runtimes` mostra o 10.x | o apphost não sabe onde o runtime foi instalado | `echo /usr/share/dotnet \| sudo tee /etc/dotnet/install_location` — seção 3 |
| `No such file or directory` ao rodar o binário | runtime ausente ou arquitetura errada | `dotnet --list-runtimes` |
| `503 identity.unavailable` em tudo | Identity fora do ar, ou `Identity__GrpcAddress` errado | `systemctl status todolist-identity`; conferir `tasks.env` |
| `500` no INSERT depois de o Identity aprovar | `UserStore__Provider` não é `Persisted`, ou o seed não rodou | conferir `identity.env`; reiniciar o Identity |
| `psql` trava conectando em `10.128.0.5` | firewall, `listen_addresses` ou `pg_hba.conf` | seções 1 e 2 — as três camadas |
| Erro de protocolo no canal gRPC | endpoint não declarado `Http2` | não sobrescreva `Kestrel__Endpoints__Grpc__Url` |
| `bad interpreter: ...^M` | fim de linha CRLF no `.sh` | `sed -i 's/\r$//' *.sh` |
| Serviço morre quando o SSH cai | rodaram `dotnet` à mão em vez do systemd | `sudo systemctl start todolist-identity todolist-tasks` |
| `502 Bad Gateway`/`504 Gateway Timeout` na tela | Gateway fora do ar, ou escutando no endereço errado | `systemctl status todolist-gateway`; confirme que ele está em `127.0.0.1:8080` (BE-42) |
| `404` ao dar F5 numa rota do Angular (ex.: `/tasks`) | `try_files` do nginx ausente/errado — o fallback para `index.html` não está configurado | `sudo nginx -t`; conferir `deploy/nginx/todolist.conf` |
| `http://$IP_EXTERNO:8080/...` ainda responde de fora | a regra antiga `todolist-allow-gateway` não foi removida | seção 1 — fechar/remover a regra de `tcp:8080` |
| Tela do frontend não atualiza depois de um novo deploy | `index.html` cacheado pelo navegador | confirme `Cache-Control: no-cache` na resposta (`curl -I`); force-refresh no navegador |

## Depois do T1

Três coisas foram listadas no T1 como dívida a pagar antes de qualquer ambiente de verdade.
O T2 resolveu duas delas; a terceira segue de pé:

1. ~~`Tasks__AllowAnonymousCreate=true`~~ — **caiu.** BE-35 removeu o gatilho HTTP provisório
   do Tasks; a flag deixou de existir no código e no `.env`. A identidade agora chega pela
   metadata gRPC `x-user-id`, preenchida pelo Gateway depois de validar o token (D-34).
2. ~~`UserStore__SeedDemoUsers=true`~~ — **caiu (Onda E).** O cadastro real chegou
   (`POST /api/auth/register`, fase 3) e o seed de demonstração — `DemoUserSeeder`,
   `UserStore__SeedDemoUsers`/`UserStore__DemoUserPassword` — foi removido do código e de todo
   `.env`. Cadastre a conta do roteiro pela tela; a conta do usuário inativo é criada por
   `UPDATE` direto no banco (seção 8 acima).
3. ~~O `PasswordHash` placeholder dos usuários de demonstração~~ — **caiu.** BE-06 trouxe hash
   real (PBKDF2) desde o cadastro (`RegisterUserHandler`); não existe mais placeholder algum a
   regravar.

## Pendências na VM (T2)

O que falta fazer manualmente, na VM real, para fechar BE-37/BE-42 (nada disto foi executado
por este agente — só preparado no repositório; o `install-on-vm.sh`/`deploy/nginx/` que
executam parte disto estão sendo escritos em paralelo a este documento):

- [ ] Criar a regra de firewall `todolist-allow-nginx` (`tcp:80`, pública) e revisar
      `todolist-allow-grpc-internal` para `tcp:5081,5101` (seção 1).
- [ ] **Fechar/remover** a regra antiga `todolist-allow-gateway` (`tcp:8080`, pública), se ela
      existir de uma implantação anterior a BE-42 — o Gateway deixou de ser público.
- [ ] Reservar o IP estático da `maquina-1-psd` (seção 1.1).
- [ ] Subir o novo `todolist-deploy.tar.gz`, agora com `publish/frontend/` incluído
      (`scripts/publish.ps1` + upload pelo SSH do navegador).
- [ ] Editar os três `.env` na VM com os segredos reais: `identity.env` (`Jwt__Issuer`/
      `Jwt__Audience` — sem `Jwt__SigningKey`, que não existe mais, BE-40, e sem
      `UserStore__DemoUserPassword`, removida na Onda E), `tasks.env` (remover
      `Tasks__AllowAnonymousCreate`), `gateway.env` (criado a partir do `.example`, sem
      segredo).
- [ ] Rodar `sudo ./install-on-vm.sh` e confirmar as três units `active` na ordem
      Identity → Tasks → Gateway, **e** `nginx` `active` (BE-42 — instalado e configurado pelo
      mesmo script). O script gera o par de chaves RS256 em `/etc/todolist/jwt/` na primeira
      execução (BE-40, D-38) — confira que `private.pem` ficou `root:root 0400` e
      `public.pem` `root:root 0444`.
- [ ] **CA-25 de BE-40** — confirmar que `sudo -u todolist cat /etc/todolist/jwt/private.pem`
      **falha** com `Permission denied` (o usuário que roda as três units não consegue ler a
      chave privada do Identity) e registrar o resultado no PR.
- [ ] `sudo nginx -t` sem erro, e confirmar que o `root` do site aponta para onde
      `publish/frontend/` foi extraído (BE-42 CA-06).
- [ ] Verificar **de fora** da VPC, pelo IP externo: a porta **80** responde com o
      `index.html` do Angular; **8080, 5080, 5081, 5100 e 5101 não respondem** (seção 1, BE-42
      CA-03/CA-04 — a lista de portas fechadas cresceu: 8080 entrou, 8080 direto deixou de ser
      a porta pública).
- [ ] `DEMO_PASSWORD=... ./smoke.sh` verde contra `http://<IP_EXTERNO>` (sem porta — inclui a
      checagem da rota `/tasks` do SPA, BE-42 CA-01).
- [ ] `./scripts/demo-t2.ps1 -BaseUrl http://<IP_EXTERNO> -Password ...` verde, do
      notebook, de fora da VM (os três casos de 401: sem token, token lixo, token adulterado).
- [ ] No navegador, contra `http://<IP_EXTERNO>/`: login, título vazio (400 na tela), tarefa
      válida (aparece na lista), tarefa com vencimento no passado (destaque de atrasada
      aparece) — o roteiro completo do Ato 1 ao Ato 4 da seção 8.
- [ ] Medir e registrar na seção 7 o tempo do roteiro de subida completo, do upload do
      tarball às quatro units/serviços `active` (Identity, Tasks, Gateway, nginx) e à
      verificação de fora (CA-06 de BE-37).
- [ ] **Ensaio cronometrado do roteiro de apresentação de 10 minutos** (seção 8), do zero ao
      fim do Ato 7, com o tempo real registrado no campo `[PENDENTE]` da seção 8 — obrigatório
      antes do dia da apresentação, não opcional.

## 9. Caminho Docker (Onda D — plano PREFERIDO para 22/10)

> As seções 1 a 8 acima são o caminho systemd/binário e ficam como **plano B até 22/10** (ver
> o aviso no topo deste arquivo). Esta seção é nova (Onda D) e descreve o caminho Docker: uma
> VM só, tudo em container, Postgres no Cloud SQL for PostgreSQL. Nenhum comando de escrita do
> `gcloud` (criar recurso, habilitar API) nem `docker push` foi executado ao preparar esta
> seção — o que está descrito abaixo foi verificado com `-DryRun`/checagens de leitura, não
> rodado de ponta a ponta contra a VM real. Trate o primeiro ensaio com folga.
>
> Diferente das seções 1-8 (pensadas para o SSH pelo navegador, sem `gcloud` local), este
> caminho assume que você **tem `gcloud` local** (o usuário confirmou SDK 584.0.0 instalado) —
> por isso os passos usam `gcloud compute scp`/`gcloud compute ssh` em vez do upload manual
> pelo navegador que o T1 precisou usar por uma limitação que não existe mais.

### 9.0 Visão geral da ordem

```
1. Provisionar o Cloud SQL          (manual, uma vez — deploy/cloudsql/README.md)
2. Habilitar a API do Artifact
   Registry e criar o repositório   (manual, uma vez — comandos abaixo)
3. scripts/publish-images.ps1       (na sua máquina — builda e publica as 4 imagens)
4. Copiar arquivos para a VM        (gcloud compute scp)
5. deploy/install-docker-on-vm.sh   (na VM — Docker, pastas, chave JWT, auth do registry, unit)
   5b. Autenticação do registry     (o PRÓPRIO script testa — 9.6, falha cedo se não autenticar)
6. Preencher o .env na VM           (manual — IP do Cloud SQL, senha, IMAGE_TAG)
7. Subir a stack                    (systemctl start todolist.service)
8. Verificar                        (deploy/smoke.sh, scripts/demo-t2.ps1 — SEM MUDANÇA)
```

### 9.1 Provisionar o Cloud SQL

Runbook completo, nunca executado, com todos os comandos e o porquê de cada um:
[deploy/cloudsql/README.md](cloudsql/README.md). Resultado esperado ao final: uma instância
`todolist-cloudsql` com IP privado na mesma VPC de `maquina-1-psd`, banco `todolist`, usuário
`todolist` com senha definida.

### 9.2 Habilitar a API do Artifact Registry e criar o repositório

Confirmado em 25/09/2026 (leitura, sem alterar nada): no projeto `sd-26-2`, a API
`artifactregistry.googleapis.com` está **desabilitada** e portanto o repositório `todolist`
**não existe**. `scripts/publish-images.ps1` detecta os dois problemas e se recusa a publicar
enquanto eles existirem — mas não os resolve sozinho (habilitar API/criar recurso não é ação
que um script deva tomar sem pedido explícito). Rode você mesmo, uma vez:

```bash
gcloud services enable artifactregistry.googleapis.com --project sd-26-2
# espere alguns minutos para propagar antes do comando abaixo

gcloud artifacts repositories create todolist \
  --repository-format=docker \
  --location=us-central1 \
  --project=sd-26-2
```

### 9.3 Publicar as quatro imagens

Da sua máquina (Windows, com Docker Desktop e `gcloud` já autenticado no projeto certo):

```powershell
./scripts/publish-images.ps1 -DryRun    # ensaie primeiro — mostra os 4 builds/tags/pushes sem tocar em nada
./scripts/publish-images.ps1            # publicação de verdade
```

Leia o comentário de ajuda do script (`Get-Help ./scripts/publish-images.ps1 -Full`) antes da
primeira publicação — em especial a seção sobre a **tag**: com a árvore de trabalho suja (o
estado normal enquanto a Fase 3/Ondas A-D não forem commitadas), a tag NÃO é o SHA do commit
puro, e o script avisa em destaque qual tag foi usada. Anote essa tag — é ela que entra em
`IMAGE_TAG` no `.env` da VM (passo 9.7).

### 9.4 Copiar os arquivos para a VM

Com `gcloud` local, sem precisar do SSH pelo navegador:

```powershell
gcloud compute scp deploy/docker-compose.prod.yml deploy/todolist.env.example deploy/todolist.service `
  deploy/install-docker-on-vm.sh `
  maquina-1-psd:~/docker-deploy/ --zone=us-central1-a

gcloud compute scp --recurse artifacts/sql `
  maquina-1-psd:~/docker-deploy/sql --zone=us-central1-a
```

(`artifacts/sql/*.sql` vem do mesmo `scripts/publish.ps1` do plano B — os scripts SQL
idempotentes não mudam entre os dois caminhos de deploy, só o que os aplica muda: `psql` direto
lá, o serviço `migrate` do compose aqui.)

### 9.5 Preparar a VM

```bash
gcloud compute ssh maquina-1-psd --zone=us-central1-a
# já dentro da VM:
cd ~/docker-deploy
chmod +x install-docker-on-vm.sh
sudo ./install-docker-on-vm.sh
```

Isso instala o Docker Engine + plugin `compose` (repositório oficial, não o pacote da distro),
monta `/opt/todolist/docker/` com o compose e os SQL, cria (sem sobrescrever) o `.env` a partir
do `.example`, garante o par de chaves JWT em `/etc/todolist/jwt/` com o dono trocado para uid
1654 (ver o comentário longo dentro do script e em `docker-compose.prod.yml`, seção
`secrets:`, sobre por que isso é necessário sem Docker Swarm), e instala/habilita
`deploy/todolist.service`.

### 9.6 Autenticação do Docker no Artifact Registry — passo crítico, leia antes de subir a stack

`install-docker-on-vm.sh` (passo 9.5) já tenta resolver isto sozinho, mas o resultado importa o
bastante para merecer um passo próprio aqui, e não só uma frase dentro do 9.5: quem executa
`docker compose ... up -d` de verdade é a unit `todolist.service`, `Type=oneshot`, **disparada
no BOOT, como root** (ver `deploy/todolist.service`) — não a pessoa que faz `gcloud compute
ssh`. Dois fatos, juntos, tornam isto perigoso:

1. A documentação do Artifact Registry só dispensa configurar autenticação para "Cloud Build e
   ambientes de runtime como GKE e Cloud Run" — **Compute Engine não está nessa lista.** Numa
   VM comum, sem `gcloud auth configure-docker` rodado como quem executa o `docker pull`, o pull
   falha com `unauthorized`/`denied`.
2. A `maquina-1-psd` roda com a service account padrão
   `36621986996-compute@developer.gserviceaccount.com` e os escopos OAuth padrão do Compute
   Engine (`devstorage.read_only`, `logging.write`, `monitoring.write`,
   `service.management.readonly`, `servicecontrol`, `trace.append`) — **sem** `cloud-platform`.
   Isso pode não bastar para ler o Artifact Registry, dependendo de como o IAM do projeto está
   configurado.

E, para fechar a armadilha: `gcloud auth configure-docker` rodado pelo usuário comum do SSH
grava em `/home/<você>/.docker/config.json` — um arquivo que o **root nunca lê**. Testar
manualmente "funciona" e a unit falha no boot do mesmo jeito, sem nenhuma pista de que a causa é
"autenticado como a pessoa errada". Por isso `install-docker-on-vm.sh` roda
`gcloud auth configure-docker us-central1-docker.pkg.dev --quiet` **como root** (o script
inteiro já exige `sudo`) — o alvo é `/root/.docker/config.json`, o mesmo arquivo que a unit lê.

Depois de configurar, o script tenta um `docker pull` real de `todolist-gateway` **antes de
habilitar a unit systemd** — falhar aqui, na instalação, com mensagem clara, é infinitamente
melhor do que a stack ficar parada no primeiro boot sem ninguém notar:

- **Pull funcionou** → autenticação OK, instalação segue.
- **Imagem não encontrada** (esperado antes do primeiro `publish-images.ps1`) → o script
  distingue isto de falha de autenticação (o Artifact Registry só revela "não encontrada" para
  quem já tem permissão de leitura) e apenas AVISA — pede para rodar de novo depois da primeira
  publicação.
- **Qualquer outra falha** (unauthorized, denied, ou mensagem não reconhecida) → o script
  ABORTA antes de habilitar a unit, e imprime os dois remédios possíveis, **nesta ordem**:

  1. Conceder `roles/artifactregistry.reader` à service account da VM:

     ```bash
     gcloud artifacts repositories add-iam-policy-binding todolist \
       --location=us-central1 --project=sd-26-2 \
       --member="serviceAccount:36621986996-compute@developer.gserviceaccount.com" \
       --role="roles/artifactregistry.reader"
     ```

  2. **Se mesmo assim continuar falhando**, o escopo OAuth da própria VM pode ser insuficiente.
     Confirme com:

     ```bash
     gcloud compute instances describe maquina-1-psd --zone=us-central1-a \
       --format="value(serviceAccounts[0].scopes)"
     ```

     **ATENÇÃO — isto exige PARAR A VM.** Mudar o escopo de uma instância do Compute Engine só é
     possível com ela desligada (`gcloud compute instances stop`, depois `set-scopes`, depois
     `start`) — derruba a stack em produção enquanto dura. **Descubra isto num ensaio, com
     folga, não na véspera da apresentação com a VM em uso.** Nem o script nem este runbook
     param ou reconfiguram a VM sozinhos — é decisão e ação manual do usuário.

### 9.7 Preencher o `.env` e subir

```bash
sudo nano /opt/todolist/docker/.env
```

Preencha `<IP_PRIVADO_CLOUDSQL>` (passo 9.1), a senha real, e `IMAGE_TAG` com a tag que
`publish-images.ps1` imprimiu no passo 9.3. Depois:

```bash
sudo systemctl start todolist.service
docker compose -f /opt/todolist/docker/docker-compose.prod.yml --env-file /opt/todolist/docker/.env ps
```

### 9.8 Firewall — ação manual, fora deste runbook de scripts

Nem `install-docker-on-vm.sh` nem nenhum outro script desta onda abre porta ou mexe em regra
de firewall — isso é decisão e ação do usuário no Console/`gcloud` (mesmas regras da seção 1
acima: só a porta 80 pública, os backends internos nunca expostos). Confirme a regra
`todolist-allow-nginx` (ou equivalente) antes de testar de fora da VPC.

### 9.9 Verificar — logs e smoke test

Para acompanhar os logs durante o ensaio/apresentação: `deploy/tmux-demo-docker.sh` (novo,
equivalente de `deploy/tmux-demo.sh` usando `docker compose logs -f` por serviço em vez de
`journalctl -u todolist-*`, que não existe no mundo container).

**`deploy/smoke.sh` e `scripts/demo-t2.ps1` NÃO precisam de nenhuma mudança** — os dois já
batem em `http://<IP>/` (SPA) e `http://<IP>/api/*` (Gateway via proxy), que é exatamente a
mesma forma como o `frontend`/nginx do caminho Docker serve a stack na porta 80. A origem HTTP
pública é idêntica nos dois caminhos de deploy; só o que está por trás dela muda (processos
systemd vs. containers).

```bash
DEMO_PASSWORD=... ./deploy/smoke.sh http://<IP_EXTERNO>
```

```powershell
./scripts/demo-t2.ps1 -BaseUrl http://<IP_EXTERNO> -Password ...
```

### 9.10 O que este runbook do caminho Docker NÃO verificou

- Nenhum comando de escrita do `gcloud` (habilitar API, criar repositório, criar instância
  Cloud SQL, conceder papel IAM) foi executado — só leitura, para confirmar o estado atual (API
  desabilitada, repositório inexistente) e escrever as mensagens de erro corretas em
  `scripts/publish-images.ps1` e `deploy/install-docker-on-vm.sh`.
- `docker push` nunca rodou — `scripts/publish-images.ps1 -DryRun` foi o único modo exercitado.
- `deploy/install-docker-on-vm.sh` nunca rodou numa VM real — só revisado linha a linha contra
  o padrão de `deploy/install-on-vm.sh` e a sintaxe conferida com `bash -n`. Em particular, o
  teste de `docker pull` do passo 9.6 nunca foi exercitado contra o Artifact Registry real deste
  projeto (a API está desabilitada — ver 9.2).
- A instalação do Docker Engine pelo repositório oficial assume Debian/Ubuntu (a família de
  imagem que o Compute Engine do projeto já usa nas duas VMs do T1/T2) — não testada contra
  outra distribuição.
- Os escopos OAuth reais da `maquina-1-psd` não foram confirmados por este agente com
  `gcloud compute instances describe` (o comando é leitura pura e seria permitido, mas não foi
  necessário para escrever o runbook — os escopos padrão citados acima vêm da documentação do
  Compute Engine, não de uma consulta a esta VM específica). Confirme antes do ensaio.
