# Implantação no GCP — runbook do T1 e T2

Passo a passo para colocar os microsserviços de pé e verificar que a comunicação gRPC
funciona lá. Escrito para o fluxo **sem `gcloud` local**: tudo pelo Console web do GCP e pelo
SSH no navegador.

> **Estado destes arquivos.** `scripts/publish.ps1` foi executado e verificado na máquina de
> desenvolvimento. O deploy do T1 nas VMs (`install-on-vm.sh`, os `*.service`, os `.env`) foi
> executado pelo Felipe em 06/09/2026 e os serviços subiram. O upgrade para o T2 (seção
> abaixo) ainda **não foi executado na VM real** — as instruções foram preparadas e revisadas
> no repositório, mas a execução em campo (CA-01 a CA-08 de BE-37) fica registrada como
> pendência até acontecer. Trate o primeiro ensaio com folga, não como formalidade.

> Este arquivo é o **runbook** (o que digitar). Para entender **o que cada script faz por
> dentro e por quê**, veja [ANATOMIA-DOS-SCRIPTS.md](ANATOMIA-DOS-SCRIPTS.md).

## Topologia

| VM | Zona | IP interno | Papel |
|---|---|---|---|
| `maquina-1-psd` | `us-central1-a` | `10.128.0.4` | Identity Service (5080 `/health`, 5081 gRPC), Tasks Service (5100 `/health`, 5101 gRPC) **e** API Gateway (8080 HTTP) |
| `maquina-2-psd` | `us-central1-a` | `10.128.0.5` | PostgreSQL (5432) |

O salto gRPC acontece **dentro** da `maquina-1-psd`, por `127.0.0.1:5081` e `127.0.0.1:5101`.
O que cruza a rede entre VMs é o acesso ao Postgres — e é essa a regra de firewall VPC do
requisito 3 do enunciado. Desde o T2, a **única** porta de aplicação alcançável de fora da
VPC é a 8080 do Gateway (**D-32**) — 5080/5081/5100/5101 não devem responder de fora.

> Confirme os IPs internos antes de preencher os arquivos de ambiente:
> **Compute Engine → Instâncias de VM**, coluna "IP interno".

## 1. Firewall VPC (Console web)

**VPC network → Firewall → Create firewall rule.** Quatro regras (a quarta é nova no T2):

| Nome | Targets (tags) | Source IPv4 ranges | Protocolos/portas | Para quê |
|---|---|---|---|---|
| `todolist-allow-postgres` | `todolist-db` | `10.128.0.4/32` | `tcp:5432` | **a regra do requisito 3** — só a VM de aplicação fala com o banco |
| `todolist-allow-grpc-internal` | `todolist-app` | `10.128.0.0/20` | `tcp:5081,5101` | declara a intenção do canal gRPC na sub-rede (Identity + Tasks) |
| `todolist-allow-iap-ssh` | (em branco = todas) | `35.235.240.0/20` | `tcp:22` | SSH no navegador via IAP |
| `todolist-allow-gateway` | `todolist-app` | `0.0.0.0/0` | `tcp:8080` | **a única regra de ingresso de aplicação vinda da internet** (T2, D-32) |

Direção `Ingress`, ação `Allow`, prioridade `1000` nas quatro.

Depois marque as VMs com as tags — **Compute Engine → a VM → Editar → Tags de rede**:

- `maquina-1-psd` → `todolist-app`
- `maquina-2-psd` → `todolist-db`

> **A regra de gRPC interno é intencionalmente redundante.** Como os três serviços estão na
> mesma VM, o gRPC não depende dela — o caminho crítico é `127.0.0.1`. Ela existe para
> declarar a intenção do canal e para ser o artefato concreto quando a pergunta "mostre a
> regra que deixa seus serviços conversarem" aparecer na banca. A regra que está de fato no
> caminho crítico do tráfego entre VMs é a do Postgres.

> **Não abra 5080, 5081, 5100 ou 5101 para a internet — só a 8080.** Desde o T2, Identity e
> Tasks confiam no chamador para saber quem é o usuário (`X-User-Id` / metadata `x-user-id`,
> **D-30**/**D-34**): isso só é seguro enquanto o Gateway for o único caminho até eles. Um
> desses back-ends acessível publicamente vira falsificação de identidade trivial.

> **Verificação de fora, obrigatória (D-32, CA-03 de BE-37).** Do seu notebook, **não** de
> dentro da VM, contra o **IP externo** da `maquina-1-psd`:
>
> ```bash
> for porta in 5080 5081 5100 5101; do
>     echo "porta $porta:"
>     curl --max-time 3 "http://$IP_EXTERNO:$porta/health"
>     echo "  (esperado: timeout ou recusa de conexão, nunca resposta HTTP)"
> done
> curl --max-time 3 "http://$IP_EXTERNO:8080/health"   # esperado: 200 OK
> ```
>
> Uma regra de firewall mal escrita é um erro silencioso: a aplicação continua funcionando
> via Gateway e ninguém percebe que uma porta interna também ficou aberta até ser tarde. Só a
> comprovação de campo conta — não "confiar na regra".

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

Roda build em Release, a suíte inteira, e produz `artifacts/todolist-deploy.tar.gz` (~25 MB).

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
sai do Tasks e o Identity passa a exigir chave JWT e senha de demonstração explícitas. O banco
**já tem** as linhas de `identity.users` com o hash placeholder do T1 — nada precisa ser
recriado; o seed, ao rodar de novo, vê que a senha não confere com o hash armazenado e o
regrava (BE-33 CA-09). **Não há migration nova.**

1. **Firewall e IP** — se ainda não feito: seção 1 (regra `todolist-allow-gateway`, revisão de
   `todolist-allow-grpc-internal`) e seção 1.1 (IP estático).

2. **Editar os `.env` existentes na VM**, na sessão SSH da `maquina-1-psd`:

   ```bash
   sudo nano /etc/todolist/identity.env
   ```

   Acrescente (gerando a chave **na própria VM**, nunca reaproveitando algo de teste):

   ```bash
   openssl rand -base64 48    # cole o resultado em Jwt__SigningKey abaixo
   ```

   ```
   Jwt__SigningKey=<a chave gerada acima>
   Jwt__Issuer=todolist-identity
   Jwt__Audience=todolist
   UserStore__DemoUserPassword=<uma senha de demonstração — nunca versione este valor>
   ```

   ```bash
   sudo nano /etc/todolist/tasks.env
   ```

   Remova a linha `Tasks__AllowAnonymousCreate=true` (e o comentário acima dela) — o gatilho
   REST provisório foi removido (BE-35); o Tasks agora é só gRPC.

   ```bash
   sudo cp ~/todolist-deploy/gateway.env.example /etc/todolist/gateway.env
   sudo chmod 600 /etc/todolist/gateway.env
   sudo chown root:root /etc/todolist/gateway.env
   ```

   O `gateway.env` só tem endereços — nenhum segredo a preencher, mas confira os dois
   endereços `127.0.0.1` antes de seguir.

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

4. **Instalar** — agora com o terceiro serviço:

   ```bash
   sudo ./install-on-vm.sh
   ```

   O script confere os três `.env` (recusa continuar se `gateway.env` estiver faltando ou com
   placeholder, mesmo padrão de `identity.env`/`tasks.env`), instala a unit
   `todolist-gateway.service` e sobe os três serviços **na ordem Identity → Tasks → Gateway**,
   esperando o `/health` de cada um antes de seguir para o próximo — a mesma lógica de espera
   por condição que já existia entre Identity e Tasks, estendida a mais um salto. É a mesma
   ordem para qualquer restart manual depois:

   ```bash
   sudo systemctl restart todolist-identity && sleep 2 \
     && sudo systemctl restart todolist-tasks && sleep 2 \
     && sudo systemctl restart todolist-gateway
   ```

   **Por que essa ordem:** o Gateway é cliente gRPC dos outros dois (D-33). Reiniciá-lo
   primeiro não quebra nada de fato (ele reconecta na primeira chamada), mas subir os
   back-ends primeiro evita que as primeiras requisições reais — inclusive as do ensaio —
   encontrem 503 por um back-end ainda de pé.

5. **Conferir a partir de fora**, IP externo, porta 8080, antes de considerar a VM pronta
   (ver o bloco de verificação na seção 1):

   ```bash
   curl --max-time 3 "http://$IP_EXTERNO:8080/health"
   DEMO_PASSWORD=... ./smoke.sh "http://$IP_EXTERNO:8080"
   ```

> **Tempo do roteiro de subida (CA-06 de BE-37).** Meça, em pelo menos uma execução real, o
> tempo do upload do tarball até as três units `active` e a verificação de fora respondendo, e
> registre aqui:
>
> `[PENDENTE — medir na primeira execução real na VM]`

## 8. No dia da apresentação

Sem `gcloud` não há túnel IAP — e tudo bem, porque **rodar de dentro da VM é a opção mais
robusta mesmo**: o SSH do navegador só precisa de HTTPS, que nenhuma rede institucional
bloqueia. Nada de depender do Wi-Fi da sala liberar porta estranha.

Abra **um** SSH no navegador na `maquina-1-psd`. Dois scripts montam tudo:

```bash
sudo apt-get install -y tmux     # uma vez
cd ~/todolist-deploy
./tmux-demo.sh                   # monta a tela em 4 painéis e entra nela
```

```
┌───────────────────────┬──────────────────────────┐
│                       │  log do IDENTITY         │
│                       ├──────────────────────────┤
│   roteiro (demo.sh)   │  log do TASKS            │
│                       ├──────────────────────────┤
│                       │  log do GATEWAY          │
└───────────────────────┴──────────────────────────┘
```

No painel da esquerda, com a senha de demonstração (a mesma de `UserStore__DemoUserPassword`
em `identity.env`):

```bash
DEMO_PASSWORD=... ./demo.sh
```

O roteiro faz login de verdade contra o Gateway e usa o access token nas chamadas
subsequentes — narre, aperte Enter, a resposta aparece. Os detalhes de cada ato (o que prova
cada um) ficam no roteiro do próprio script e em
[ANATOMIA-DOS-SCRIPTS.md](ANATOMIA-DOS-SCRIPTS.md); a ideia geral se manteve: login válido, um
caso negado pelo Identity, e o Identity fora do ar — só que agora entrando por HTTP no Gateway
(8080), não mais direto no Tasks.

Atalhos de tmux que importam: `Ctrl+B` + seta navega entre painéis, `Ctrl+B d` sai sem matar a
sessão, `tmux attach -t demo` volta.

> **Aumente a fonte do terminal antes.** Cada linha de log precisa caber sem quebrar — se um
> identificador de correlação for para a segunda linha, o ponto da demonstração deixa de ser
> visível da última fileira.

### Checklist da última hora

- [ ] As duas VMs **ligadas** (Compute Engine → Instâncias de VM).
- [ ] `sudo systemctl is-active todolist-identity todolist-tasks todolist-gateway` → `active`
      nos três.
- [ ] `DEMO_PASSWORD=... ./smoke.sh` verde (padrão contra `http://127.0.0.1:8080`).
- [ ] Verificação de fora feita de novo pouco antes: 8080 responde, 5080/5081/5100/5101 não.
- [ ] Aquecimento: uma requisição descartável disparada — a primeira chamada paga conexão
      HTTP/2 e a primeira query do EF Core; que isso aconteça antes da plateia.
- [ ] `tmux` montado, fonte do terminal aumentada, os quatro painéis visíveis.
- [ ] O Identity **religado**, se você testou a indisponibilidade no ensaio.

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

## Depois do T1

Três coisas foram listadas no T1 como dívida a pagar antes de qualquer ambiente de verdade.
O T2 resolveu duas delas; a terceira segue de pé:

1. ~~`Tasks__AllowAnonymousCreate=true`~~ — **caiu.** BE-35 removeu o gatilho HTTP provisório
   do Tasks; a flag deixou de existir no código e no `.env`. A identidade agora chega pela
   metadata gRPC `x-user-id`, preenchida pelo Gateway depois de validar o token (D-34).
2. `UserStore__SeedDemoUsers=true` — **continua de pé.** Ainda não há cadastro real (BE-07
   segue fora do escopo do T2); o seed continua sendo a única forma de existir usuário para o
   roteiro de login. Cai quando o cadastro entrar.
3. ~~O `PasswordHash` placeholder dos usuários de demonstração~~ — **caiu.** BE-06 trouxe hash
   real (PBKDF2), e o seed (BE-33 CA-09) regrava automaticamente qualquer hash placeholder ou
   senha de demonstração desatualizada que encontrar.

## Pendências na VM (T2)

O que falta fazer manualmente, na VM real, para fechar BE-37 (nada disto foi executado por
este agente — só preparado no repositório):

- [ ] Criar a regra de firewall `todolist-allow-gateway` e revisar `todolist-allow-grpc-internal`
      para `tcp:5081,5101` (seção 1).
- [ ] Reservar o IP estático da `maquina-1-psd` (seção 1.1).
- [ ] Subir o novo `todolist-deploy.tar.gz` (`scripts/publish.ps1` + upload pelo SSH do
      navegador).
- [ ] Editar os três `.env` na VM com os segredos reais: `identity.env` (`Jwt__SigningKey`
      gerada com `openssl rand -base64 48`, `Jwt__Issuer`/`Jwt__Audience`,
      `UserStore__DemoUserPassword`), `tasks.env` (remover `Tasks__AllowAnonymousCreate`),
      `gateway.env` (criado a partir do `.example`, sem segredo).
- [ ] Rodar `sudo ./install-on-vm.sh` e confirmar as três units `active` na ordem
      Identity → Tasks → Gateway.
- [ ] Verificar de fora da VPC: `curl --max-time 3` no IP externo confirma que 8080 responde e
      que 5080/5081/5100/5101 **não** respondem (seção 1).
- [ ] `DEMO_PASSWORD=... ./smoke.sh` verde contra `http://<IP_EXTERNO>:8080`.
- [ ] Medir e registrar na seção 7 o tempo do roteiro de subida completo, do upload do
      tarball às três units `active` e à verificação de fora (CA-06 de BE-37).
- [ ] Ensaio cronometrado do roteiro de apresentação (`tmux-demo.sh` + `demo.sh`), com pelo
      menos uma execução real do caminho de indisponibilidade e a religada do serviço
      conferida no fim.
