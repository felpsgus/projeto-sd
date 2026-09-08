# Implantação no GCP — runbook do T1

Passo a passo para colocar os dois microsserviços de pé e verificar que a comunicação gRPC
funciona lá. Escrito para o fluxo **sem `gcloud` local**: tudo pelo Console web do GCP e pelo
SSH no navegador.

> **Estado destes arquivos.** `scripts/publish.ps1` foi executado e verificado na máquina de
> desenvolvimento. O deploy nas VMs (`install-on-vm.sh`, os dois `*.service`, os `.env`) foi
> executado pelo Felipe em 06/09/2026 e os serviços subiram. `demo.sh` e `tmux-demo.sh` ainda
> não foram exercitados contra as VMs — trate o primeiro ensaio com folga, não como
> formalidade.

> Este arquivo é o **runbook** (o que digitar). Para entender **o que cada script faz por
> dentro e por quê**, veja [ANATOMIA-DOS-SCRIPTS.md](ANATOMIA-DOS-SCRIPTS.md).

## Topologia

| VM | Zona | IP interno | Papel |
|---|---|---|---|
| `maquina-1-psd` | `us-central1-a` | `10.128.0.4` | Identity Service (5080 REST, 5081 gRPC) **e** Tasks Service (5100 REST) |
| `maquina-2-psd` | `us-central1-a` | `10.128.0.5` | PostgreSQL (5432) |

O salto gRPC acontece **dentro** da `maquina-1-psd`, por `127.0.0.1:5081`. O que cruza a rede
entre VMs é o acesso ao Postgres — e é essa a regra de firewall VPC do requisito 3 do enunciado.

> Confirme os IPs internos antes de preencher os arquivos de ambiente:
> **Compute Engine → Instâncias de VM**, coluna "IP interno".

## 1. Firewall VPC (Console web)

**VPC network → Firewall → Create firewall rule.** Três regras:

| Nome | Targets (tags) | Source IPv4 ranges | Protocolos/portas | Para quê |
|---|---|---|---|---|
| `todolist-allow-postgres` | `todolist-db` | `10.128.0.4/32` | `tcp:5432` | **a regra do requisito 3** — só a VM de aplicação fala com o banco |
| `todolist-allow-grpc-internal` | `todolist-app` | `10.128.0.0/20` | `tcp:5081` | declara a intenção do canal gRPC na sub-rede |
| `todolist-allow-iap-ssh` | (em branco = todas) | `35.235.240.0/20` | `tcp:22` | SSH no navegador via IAP |

Direção `Ingress`, ação `Allow`, prioridade `1000` nas três.

Depois marque as VMs com as tags — **Compute Engine → a VM → Editar → Tags de rede**:

- `maquina-1-psd` → `todolist-app`
- `maquina-2-psd` → `todolist-db`

> **A segunda regra é intencionalmente redundante hoje.** Como os dois serviços estão na mesma
> VM, o gRPC não depende dela. Ela existe para declarar a intenção do canal e para ser o
> artefato concreto quando a pergunta "mostre a regra que deixa seus serviços conversarem"
> aparecer na banca. A regra que está de fato no caminho crítico é a do Postgres.

> **Não abra a 5100 para a internet.** O Tasks roda com `Tasks__AllowAnonymousCreate=true`
> (modo provisório de BE-29): sem autenticação, qualquer um cria tarefa em nome de qualquer
> usuário. Na apresentação, o `curl` sai de dentro da própria VM — seção 7.

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

## 7. No dia da apresentação

Sem `gcloud` não há túnel IAP — e tudo bem, porque **rodar de dentro da VM é a opção mais
robusta mesmo**: o SSH do navegador só precisa de HTTPS, que nenhuma rede institucional
bloqueia. Nada de depender do Wi-Fi da sala liberar porta estranha.

Abra **um** SSH no navegador na `maquina-1-psd`. Dois scripts montam tudo:

```bash
sudo apt-get install -y tmux     # uma vez
cd ~/todolist-deploy
./tmux-demo.sh                   # monta a tela em 3 painéis e entra nela
```

```
┌───────────────────────┬──────────────────────────┐
│                       │  log do IDENTITY         │
│   roteiro (demo.sh)   ├──────────────────────────┤
│                       │  log do TASKS            │
└───────────────────────┴──────────────────────────┘
```

No painel da esquerda:

```bash
./demo.sh
```

Três atos, avançando a cada Enter — você narra, aperta Enter, a resposta aparece:

1. **dono válido → 201.** Aponte para os dois painéis de log: a mesma chamada `ValidateUser`,
   com o **mesmo `traceId`**. É a evidência de que houve ida e volta pela rede.
2. **a mesma requisição, só mudando o `X-User-Id` → 404.** O Identity respondeu `exists=False`.
   Um Tasks que decidisse sozinho teria devolvido 201 aqui também.
3. **Identity parado → 503, nada gravado.** Fail-closed (D-28). O script religa o Identity
   sozinho no fim — e também se você interromper no meio, por um `trap`.

O `demo.sh` faz uma chamada de aquecimento antes do Ato 1, descartada e invisível. Se preferir
aquecer bem antes de começar: `./demo.sh --warmup`.

Atalhos de tmux que importam: `Ctrl+B` + seta navega entre painéis, `Ctrl+B d` sai sem matar a
sessão, `tmux attach -t demo` volta.

> **Aumente a fonte do terminal antes.** A linha do `ValidateUser` precisa caber sem quebrar —
> se o `traceId` for para a segunda linha, a correlação entre os dois painéis, que é o ponto
> inteiro da demonstração, deixa de ser visível da última fileira.

### Checklist da última hora

- [ ] As duas VMs **ligadas** (Compute Engine → Instâncias de VM).
- [ ] `sudo systemctl is-active todolist-identity todolist-tasks` → `active` nos dois.
- [ ] `./smoke.sh` verde.
- [ ] Aquecimento: uma requisição descartável disparada — a primeira chamada paga conexão
      HTTP/2 e a primeira query do EF Core; que isso aconteça antes da plateia.
- [ ] `tmux` montado, fonte do terminal aumentada.
- [ ] O Identity **religado**, se você testou o 503 no ensaio.

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

Três coisas que existem só porque a autenticação ainda não foi implementada, e que precisam
sair antes de qualquer ambiente de verdade:

1. `Tasks__AllowAnonymousCreate=true` — cai quando BE-13/API Gateway entrar.
2. `UserStore__SeedDemoUsers=true` — cai quando BE-07 (cadastro) entrar.
3. O `PasswordHash` placeholder dos usuários de demonstração — BE-06 substitui.
