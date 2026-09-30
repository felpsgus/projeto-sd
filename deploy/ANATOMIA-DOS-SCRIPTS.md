# Anatomia dos scripts de deploy

O `README.md` desta pasta é o **runbook**: o que digitar, em que ordem.
Este documento é a **explicação**: o que cada script faz por dentro, por que faz
assim, e o que aconteceria se não fizesse.

Escrito para você conseguir responder, na banca, qualquer pergunta do tipo
"e como isso sobe?" sem precisar abrir o código.

---

## O mapa: quem roda quando

Quatro scripts (mais o `scripts/demo-t2.ps1`, do notebook), momentos distintos.
Nenhum deles chama o outro — quem encadeia é você. Desde o T2, `install-on-vm.sh`
instala e sobe **três** units (Identity, Tasks e Gateway) em vez de duas; desde
BE-42, ele também instala e configura o **nginx**, que passa a ser a única
origem HTTP pública (porta 80) — `smoke.sh`/`demo.sh` falam com o **nginx**,
não mais direto com o Gateway (que recuou para `127.0.0.1:8080`, inalcançável
de fora mesmo de dentro da VM por outra rota).

| Script | Onde roda | Quando | Precisa de root? |
|---|---|---|---|
| `scripts/publish.ps1` | sua máquina (Windows) | antes de cada upload | não |
| `install-on-vm.sh` | `maquina-1-psd` | a cada deploy | **sim** (`sudo`) |
| `smoke.sh` | `maquina-1-psd` | depois de cada deploy, e ~1h antes da apresentação | não |
| `tmux-demo.sh` | `maquina-1-psd` | montar a tela da apresentação | não (mas os painéis usam `sudo`) |
| `demo.sh` | dentro do tmux | apoio de linha de comando à apresentação (401, indisponibilidade) | usa `sudo` no ato de indisponibilidade |
| `scripts/demo-t2.ps1` | seu notebook (Windows) | o 401 ao vivo, num segundo terminal, e checagens locais | não |

O fluxo completo, do seu Windows até a tela projetada:

```
  SUA MÁQUINA                          maquina-1-psd (10.128.0.4)
  ───────────                          ─────────────────────────
  publish.ps1
    ├─ build Release
    ├─ dotnet test  ────── portão: teste vermelho não vira pacote
    ├─ dotnet publish -r linux-x64  (identity, tasks, gateway)
    ├─ ng build --configuration production  (frontend, BE-42)
    ├─ dotnet ef migrations script --idempotent
    └─ tar -czf todolist-deploy.tar.gz
                    │
                    │  upload pelo SSH do navegador (1 arquivo)
                    ▼
                              tar -xzf  →  ~/todolist-deploy/
                                              │
                              psql < sql/01-identity.sql    ┐ uma vez,
                              psql < sql/02-tasks.sql       ┘ na maquina-2-psd
                                              │
                              sudo ./install-on-vm.sh
                                              │  (Identity → Tasks → Gateway → nginx)
                              DEMO_PASSWORD=... ./smoke.sh   ← verificação, via nginx :80
                                              │
                              ./tmux-demo.sh → DEMO_PASSWORD=... ./demo.sh
                                              │
                                              ▲
                    scripts/demo-t2.ps1 -BaseUrl http://<IP_EXTERNO>  (segundo terminal, o 401)
```

---

## 1. `install-on-vm.sh` — o deploy

**Rode com `sudo`, a partir da pasta extraída.** É idempotente: rodar de novo é
exatamente o procedimento de reimplantar uma versão nova. Não existe script de
"update" separado.

```bash
set -euo pipefail
```

Três guardas de uma linha só, e valem a pena entender porque o resto do script
depende delas:

- `-e` — **aborta no primeiro comando que falhar**. Sem isso, um `cp` que falhasse
  seria seguido alegremente por um `systemctl start`, e o serviço subiria com os
  binários pela metade.
- `-u` — usar variável não definida é erro. Protege contra o clássico
  `rm -rf "$DESTINO/"` onde `$DESTINO` está vazio por um erro de digitação.
- `-o pipefail` — num pipe `a | b`, o conjunto falha se **qualquer** parte falhar,
  não só a última.

### 1.1 Onde eu estou?

```bash
ORIGEM="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
```

O script descobre a própria pasta em vez de assumir o diretório atual. Assim
`sudo /home/felipe/todolist-deploy/install-on-vm.sh` funciona de qualquer lugar —
e um caminho relativo quebraria assim que você rodasse o script de outro
diretório.

### 1.2 Verificações antes de tocar em qualquer coisa

Duas checagens acontecem **antes** de o script modificar o sistema:

1. É root? Se não, sai com mensagem clara. Melhor do que falhar no meio, com
   metade dos arquivos copiados.
2. `publish/identity`, `publish/tasks`, `publish/gateway` e, desde BE-42,
   `publish/frontend` existem? Se você extraiu o tarball errado ou
   incompleto, é aqui que descobre — não depois de já ter parado os
   serviços que estavam funcionando.

> **BE-42 em paralelo.** A instalação e configuração do nginx
> (`deploy/nginx/todolist.conf`, o pacote `nginx` da distro, `nginx -t` antes
> de qualquer `reload`) está sendo escrita em `install-on-vm.sh` no mesmo
> momento em que este documento foi revisado — o desenho abaixo é o que a
> task BE-42 especifica, não uma leitura de código já mesclado. Confira o
> `install-on-vm.sh` real antes de confiar cegamente num detalhe fino daqui.

Esse padrão tem nome: **falhar cedo**. Um script de deploy que aborta no meio
deixa a máquina num estado que ninguém projetou.

### 1.3 O usuário de serviço

```bash
useradd --system --no-create-home --shell /usr/sbin/nologin todolist
```

Os serviços não rodam como `root` nem como você. Rodam como um usuário que:

- `--system` — não tem senha e não aparece na tela de login;
- `--no-create-home` — não tem `/home`, porque não precisa;
- `--shell /usr/sbin/nologin` — **não consegue abrir shell**. Se alguém explorasse
  uma falha no serviço, não ganharia um terminal.

É o princípio do menor privilégio. Cada uma dessas três flags remove uma
superfície de ataque que o serviço não usaria de qualquer forma.

O `if ! id -u ...` em volta é o que torna o passo idempotente: na segunda
execução ele apenas reporta "já existe".

### 1.4 Parar antes de copiar

```bash
systemctl stop todolist-gateway.service 2>/dev/null || true
systemctl stop todolist-tasks.service 2>/dev/null || true
systemctl stop todolist-identity.service 2>/dev/null || true
```

Ordem invertida da subida — **Gateway primeiro, depois Tasks, Identity por
último**. Derruba-se cada cliente antes do servidor de quem ele depende, para
que ninguém receba requisições enquanto quem está atrás dele já sumiu.

O `|| true` existe porque no **primeiro** deploy esses serviços não existem, e com
`set -e` um `systemctl stop` de serviço inexistente abortaria o script inteiro.
`|| true` diz: "esta falha específica é esperada, siga".

### 1.5 Copiar apagando antes

```bash
copiar_servico() {
    local nome="$1"
    rm -rf "${DESTINO:?}/$nome"
    mkdir -p "$DESTINO/$nome"
    cp -a "$ORIGEM/publish/$nome/." "$DESTINO/$nome/"
}
```

O destino é **apagado** antes de receber a versão nova. Não é excesso de zelo: se
uma DLL existia na versão anterior e não existe mais nesta, copiar por cima a
deixaria lá — e o .NET pode carregá-la. O sintoma disso é uma exceção
incompreensível sobre um tipo que você jurava ter removido.

Detalhes que valem a leitura:

- `"${DESTINO:?}"` — a sintaxe `:?` faz o bash **abortar** se a variável estiver
  vazia. É a rede de segurança contra o `rm -rf /` acidental.
- `cp -a` preserva permissões e timestamps. Foi escolhido em vez de
  `rsync --delete`, que seria mais elegante, porque **rsync não vem em toda imagem
  do Compute Engine** — e descobrir isso no meio de um deploy na véspera não é o
  momento. `cp` está em qualquer Linux.
- `publish/$nome/.` — o ponto final copia o *conteúdo* da pasta, não a pasta
  dentro da pasta.

Depois: `chmod +x` nos três executáveis (o bit de execução não sobrevive de forma
confiável do NTFS para dentro do `.tar.gz`) e `chown -R todolist:todolist`. É a
mesma função chamada três vezes — `copiar_servico identity`, `copiar_servico
tasks`, `copiar_servico gateway` — desde o T2.

### 1.6 O portão dos segredos

```bash
for arquivo in /etc/todolist/identity.env /etc/todolist/tasks.env /etc/todolist/gateway.env; do
    if [[ ! -f "$arquivo" ]]; then  faltando=1
    else
        chmod 600 "$arquivo"; chown root:root "$arquivo"
        if grep -qE 'TROQUE_ESTA_SENHA|TROQUE_ESTA_CHAVE' "$arquivo"; then faltando=1; fi
    fi
done
```

Este bloco é o mais importante do script sob o ponto de vista de segurança, e
**recusa continuar** em dois casos: arquivo ausente, ou algum placeholder ainda
presente. Desde o T2 o `identity.env` carrega o placeholder `TROQUE_ESTA_SENHA`
(connection string) — e o `gateway.env` entra na mesma checagem por
consistência, ainda que não tenha segredo nenhum: é o mesmo portão, para não
haver um quarto arquivo com regra própria.

> **Onda E (T2): o seed de demonstração saiu.** Até então, `identity.env`
> também carregava `UserStore__SeedDemoUsers`/`UserStore__DemoUserPassword` —
> um segundo placeholder de senha (`DemoUserSeeder`, dois usuários fixos em
> `identity.users`). Com o cadastro real (`POST /api/auth/register`) esse
> atalho deixou de fazer sentido e foi removido; `identity.env` só carrega mais
> a connection string.

> **BE-40 mudou onde a chave JWT vive.** Até o T2, havia um segundo placeholder
> aqui, `TROQUE_ESTA_CHAVE` (`Jwt__SigningKey`, HS256). Desde BE-40 (RS256,
> D-38) a chave deixou de ser uma variável de `.env`: é um par de arquivos PEM
> em `/etc/todolist/jwt/`, gerado por este mesmo script logo depois do portão
> dos segredos (seção 1.7 abaixo) — nunca copiado, nunca placeholder para
> conferir.

Ele também **corrige as permissões toda vez**: `600` (só o dono lê e escreve) e
dono `root`. Repare na consequência prática: o arquivo com a senha do Postgres é
ilegível para o usuário `todolist` sob o qual o serviço roda — quem lê é o
systemd, que roda como root, e injeta as variáveis no processo já iniciado.

Repare também no que o script **não** faz: ele nunca cria nem sobrescreve esses
arquivos. Os segredos são criados uma vez, à mão, e sobrevivem intactos a
qualquer número de redeploys. Um script que gerasse `.env` sozinho acabaria, mais
cedo ou mais tarde, com um segredo dentro do repositório.

### 1.7 A chave JWT: gerada uma vez, na própria VM (BE-40)

```bash
JWT_DIR=/etc/todolist/jwt
if [[ ! -f "$JWT_DIR/private.pem" ]]; then
    mkdir -p "$JWT_DIR"
    openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$JWT_DIR/private.pem"
    openssl pkey -in "$JWT_DIR/private.pem" -pubout -out "$JWT_DIR/public.pem"
    chmod 0400 "$JWT_DIR/private.pem"
    chmod 0444 "$JWT_DIR/public.pem"
fi
```

Este bloco é a contrapartida do "portão dos segredos" acima, só que para uma
chave em vez de uma senha — e o motivo de ela **não** viver num `.env` é
diferente dos outros segredos. `Jwt__PrivateKeyPath` (Identity) e
`Jwt__PublicKeyPath` (Gateway) são *caminhos de arquivo*, não os próprios
segredos: o conteúdo sensível é o `private.pem`, e o jeito de restringir quem o
lê é permissão de sistema de arquivos — algo que um `.env`, por si só, não
oferece.

**Por que isolar por permissão de arquivo, e não confiar no usuário do
serviço.** As três units rodam como o **mesmo** usuário `todolist`
(`User=todolist` nos três `.service`). Se `private.pem` fosse
`todolist:todolist`, o processo do Gateway e o do Tasks — que rodam com essa
mesma identidade de sistema operacional — conseguiriam ler o arquivo tão bem
quanto o Identity, mesmo sem nenhuma linha de configuração apontando para ele.
A separação vem de dono `root` + modo `0400`: só `root` pode ler, e é o
systemd (que processa a unit como root antes de fazer `setuid` para
`todolist`) quem consegue repassar o conteúdo — via `LoadCredential=`, não via
permissão herdada pelo processo final.

**`LoadCredential=jwt-private:/etc/todolist/jwt/private.pem`** (no unit do
Identity, não neste script) é o mecanismo do systemd (≥ 248 — presente em
Debian 12 e Ubuntu 24.04, as duas distros-alvo) que copia o arquivo para um
diretório de credenciais efêmero, exposto **só** ao processo daquela unit
específica; `%d` no unit resolve para esse diretório em runtime
(`Environment=Jwt__PrivateKeyPath=%d/jwt-private`). Um `EnvironmentFile=`
comum não serviria aqui pelo motivo oposto do de sempre: ele não expande nada
— `%d` só existe dentro do próprio arquivo de unit, então o caminho tem que
estar na diretiva `Environment=`, não em `identity.env`.

`public.pem` não passa por `LoadCredential=` — não é segredo (só verifica
assinatura, não assina nada), então o caminho fixo
`/etc/todolist/jwt/public.pem`, modo `0444`, é suficiente; o
`todolist-gateway.service` a lê direto de lá via `Environment=`.

**Idempotência com consequência de segurança, não só de conveniência.** O
`if [[ ! -f ... ]]` não é só "não regerar à toa" — é "nunca trocar a chave
sem decisão explícita". Trocar a chave privada invalida instantaneamente todo
access token já emitido (D-38, sem rotação/`kid` secundário nesta etapa); um
script que regenerasse o par a cada deploy derrubaria toda sessão em
andamento a cada `sudo ./install-on-vm.sh`.

### 1.8 Subir na ordem, e esperar

```bash
systemctl enable --now todolist-identity.service
for _ in $(seq 1 30); do
    if curl -fsS --max-time 2 http://127.0.0.1:5080/health >/dev/null 2>&1; then break; fi
    sleep 1
done
systemctl enable --now todolist-tasks.service
for _ in $(seq 1 30); do
    if curl -fsS --max-time 2 http://127.0.0.1:5100/health >/dev/null 2>&1; then break; fi
    sleep 1
done
systemctl enable --now todolist-gateway.service
for _ in $(seq 1 30); do
    if curl -fsS --max-time 2 http://127.0.0.1:8080/health >/dev/null 2>&1; then break; fi
    sleep 1
done
```

`enable --now` faz duas coisas: **enable** (subir sozinho no boot) e **start**
(subir agora). O `enable` é a sua apólice de seguro se a VM reiniciar entre hoje
e a apresentação.

A espera pelo `/health` é o detalhe que separa um deploy tranquilo de um susto.
O Tasks **sobe sem o Identity**, e o Gateway **sobe sem os dois** — por desenho
(D-28 fail-closed no Tasks; `Wants=`, não `Requires=`, no Gateway). O que cada
um faz sem quem está atrás dele é devolver `503`. Sem essa espera, o primeiro
`smoke.sh` depois do deploy pegaria um serviço ainda inicializando e devolveria
`503` — indistinguível de um defeito real. Por isso a ordem — **Identity, Tasks,
Gateway** — e a mesma espera por condição repetida a cada salto, e não só entre
os dois primeiros.

É uma espera **por condição**, não por tempo: sai assim que o health responde, no
máximo 30 segundos. Um `sleep 30` fixo seria pior nos dois sentidos — lento
quando desnecessário, curto quando importa.

No fim, `systemctl status` dos três e uma chamada a cada `/health`, para você não
precisar conferir nada manualmente.

**Um quinto passo, desde BE-42: o nginx.** Depois do Gateway responder, o script
instala/atualiza o site (`deploy/nginx/todolist.conf`), roda `nginx -t` — a
configuração precisa validar **antes** de qualquer `reload`/`restart`, mesmo
princípio de falhar cedo do resto do script — e só então recarrega (ou
`enable --now nginx` na primeira instalação). O nginx entra **depois** do
Gateway estar de pé por um motivo direto: ele é o único caminho até o Gateway
agora (`127.0.0.1:8080`, D-32/D-40) — recarregar o site antes disso não quebra
nada tecnicamente, mas testar a cadeia inteira só faz sentido com todo mundo
já respondendo.

---

## 2. `smoke.sh` — a verificação

```bash
DEMO_PASSWORD=... ./smoke.sh                          # contra http://127.0.0.1 (porta 80, via nginx)
DEMO_PASSWORD=... ./smoke.sh http://10.128.0.4
```

> **T2 mudou a entrada para o Gateway; BE-42 muda de novo, para o nginx.** O
> papel do script continua o mesmo desde o T1 — uma verificação de fumaça,
> rodada depois de todo deploy e de novo cerca de uma hora antes da
> apresentação, que devolve OK/ERRO por cenário e `exit 1` se algo falhou,
> para encadear em automação. O alvo mudou duas vezes: T2 tirou o Tasks
> direto (5100) e passou a falar com o **Gateway** (8080); BE-42 tira o
> Gateway direto e passa a falar com o **nginx** (porta 80, sem porta na
> URL), que repassa `/api/*` ao Gateway — agora só alcançável em
> `127.0.0.1:8080`, de dentro da própria VM. O primeiro passo do script,
> desde BE-42, nem fala com `/api`: é um `GET /tasks` simples, conferindo que
> o nginx devolve o `index.html` do Angular (`<app-root>` no corpo) em vez de
> um 404 — a prova de que o `try_files` do SPA está configurado (BE-42
> CA-01). Os demais passos continuam exigindo login de verdade primeiro
> (`POST /api/auth/login` com `DEMO_PASSWORD`), incluindo, desde a emenda de
> BE-39, um passo com um token **adulterado** (um JWT real do login, com um
> caractere trocado) além do "sem token"/"token lixo" originais — os três
> exercitam caminhos de código diferentes no middleware de autenticação.
>
> O que continua valendo do desenho do T1, e vale conferir se algo quebrar: a
> verificação é sempre **dupla** (status HTTP e `errorCode`/corpo, nunca só o
> primeiro) e a saída acumula falhas em vez de abortar no primeiro erro
> (`set -uo pipefail`, não `-e`) — assim uma falha isolada não esconde o
> resultado dos demais cenários.

---

## 3. `demo.sh` — apoio de linha de comando à apresentação

```bash
DEMO_PASSWORD=... ./demo.sh
```

> **Desde BE-42, este script deixou de ser "o roteiro" e virou o apoio a ele.**
> A demonstração de verdade **parte do frontend** (login, título vazio → 400
> no formulário, tarefa válida → 201 na lista, tarefa vencida → destaque de
> atrasada), no navegador, apontado para `http://<IP_EXTERNO>/` — ver
> `deploy/README.md`, seção "No dia da apresentação". `demo.sh` cobre o que o
> navegador não mostra bem sob pressão de tempo: os atos de 401 (como
> ensaio/backup em linha de comando do que `scripts/demo-t2.ps1` faz ao vivo
> num segundo terminal) e o caminho de indisponibilidade do Identity
> (fail-closed, D-28, com `--falha`).
>
> `BASE` mudou de `http://127.0.0.1:8080` (direto no Gateway) para
> `http://127.0.0.1` (via nginx, sem porta) — mesma razão do `smoke.sh`: falar
> com o nginx é o que de fato prova o caminho que a plateia vai ver, e o
> Gateway não é mais alcançável de outro jeito de qualquer forma.
>
> Dois cuidados que continuam se aplicando: um **aquecimento** antes do
> primeiro ato (a primeira chamada de um processo recém-iniciado paga conexão
> HTTP/2 e a primeira query do EF Core — melhor que isso aconteça enquanto
> você ainda fala, não diante da banca) e um `trap ... EXIT` em volta de
> qualquer ato que pare um serviço de propósito, para religá-lo mesmo se o
> script for interrompido no meio.
>
> O nginx é mencionado no fim do script (`/var/log/nginx/access.log`) só como
> ponto de atenção — ele não participa da correlação por `traceId` (D-40: só
> encaminha bytes, o `traceparent` atravessa intacto), então não é esperado
> vê-lo como uma quarta linha do `grep` de correlação.

---

## 4. `tmux-demo.sh` — a tela

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

**O layout ganhou um painel a cada etapa que somou um processo:** o T2
acrescentou o log do Gateway (terceiro back-end); BE-42 acrescenta o do
**nginx** — um daemon do sistema, não uma unit .NET, mas ainda um processo
cujo comportamento vale mostrar (roteamento, proxy, 502/504 se o Gateway
cair). Com cinco coisas para ver ao mesmo tempo numa única sessão SSH, o
layout cresceu de três para cinco painéis.

### O problema que o tmux resolve

Você tem **uma** sessão SSH pelo navegador e precisa mostrar **cinco** coisas
ao mesmo tempo: o roteiro e os quatro processos de log. Sem multiplexador,
você alternaria entre abas e a correlação entre painéis — que é justamente o
que prova a comunicação entre os serviços — se perderia.

O tmux também protege contra a queda da conexão: a sessão continua viva no
servidor e `tmux attach -t demo` reconecta exatamente onde estava.

### Por que montar por script

```bash
tmux new-session -d -s demo -c "$DIR"
tmux split-window -h -t demo:0.0 -c "$DIR"
tmux split-window -v -t demo:0.1 -c "$DIR"
tmux split-window -v -t demo:0.2 -c "$DIR"
tmux split-window -v -t demo:0.3 -c "$DIR"
```

Dividir painel com `Ctrl+B` ao vivo, com a turma esperando, é onde se perde
tempo do limite de 10 minutos — e às vezes o painel abre no lugar errado.
Comandos determinísticos produzem sempre o mesmo layout, agora com cinco
painéis.

A numeração é posicional: `0.0` é o painel original; o primeiro `split -h` cria
`0.1` à direita; cada `split -v` subsequente sobre o último painel da direita
empilha mais um abaixo dele (`0.2`, depois `0.3`, depois `0.4`).

O `if tmux has-session` no início faz o script **reconectar** em vez de recriar,
se a sessão já existir. Rodar duas vezes por engano não estraga nada.

### O comando de log

```bash
sudo journalctl -fu todolist-identity -n 0 -o cat
```

Um comando por painel, um serviço por comando — `todolist-identity`,
`todolist-tasks`, `todolist-gateway`. Cada flag resolve um problema concreto:

- `-f` — segue o log ao vivo, como `tail -f`.
- `-n 0` — **não** mostra histórico. Os painéis começam vazios, e tudo que
  aparecer durante a apresentação foi causado por você.
- `-o cat` — remove data/hora e nome do host de cada linha. Numa tela projetada,
  cada linha precisa caber **sem quebrar**: se um identificador de correlação for
  para a segunda linha, o ponto de mostrar os logs lado a lado deixa de ser
  visível da última fileira da sala.

**O painel do nginx é diferente: `tail -f` num arquivo, não `journalctl -u`.**
O pacote padrão da distro grava requisição por requisição em
`/var/log/nginx/access.log`, não no journal — `journalctl -u nginx` só
mostraria start/reload/stop do processo, silencioso durante toda a
demonstração. Por isso o painel do nginx usa
`sudo tail -n 0 -f /var/log/nginx/access.log` em vez do padrão dos outros
três.

Se o roteiro reescrito precisar filtrar por um termo específico (como o T1 fazia
com `grep --line-buffered ValidateUser`), o mesmo cuidado se aplica: use
`--line-buffered`, sem o qual o grep no meio de um pipe acumula linhas antes de
imprimir e o log parece não responder à requisição, quando na verdade só está
atrasado.

### Dois cuidados práticos

**Aumente a fonte do terminal antes de começar.** Todo o cuidado com `-o cat`
existe para a linha caber; uma fonte grande demais desfaz isso — e com cinco
painéis em vez de três, cada um fica ainda mais estreito.

**Rode `sudo -v` no painel do roteiro antes de começar.** O `sudo` guarda a
credencial **por terminal** (`tty_tickets`, que é o padrão), e cada painel do tmux
é um terminal diferente. Os `sudo journalctl` dos painéis da direita não aquecem o
painel da esquerda — então um `sudo systemctl stop` disparado pelo roteiro (para
demonstrar indisponibilidade) pode abrir um prompt de senha no meio da
demonstração. Trinta segundos de prevenção.

---

## 5. O que os scripts pressupõem: units e `.env`

Os `.sh` não fazem sentido sozinhos. Vale saber o que eles instalam.

### Os units do systemd

Por que systemd e não `dotnet run` numa sessão SSH: o serviço precisa **sobreviver
à queda da conexão** e voltar sozinho se o processo morrer. Uma sessão SSH que cai
no meio da apresentação levaria os três serviços junto.

As diretivas que importam, iguais nos três units (`todolist-identity.service`,
`todolist-tasks.service` e, desde o T2, `todolist-gateway.service`):

- `Restart=always` / `RestartSec=5` — o processo morreu, o systemd sobe de novo em
  5 segundos.
- `EnvironmentFile=/etc/todolist/*.env` — os segredos vêm de fora do unit. O unit
  é legível por qualquer usuário da VM; o `.env` é `600 root:root`.
- `User=todolist` — nunca root.
- `ProtectSystem=full` deixa `/usr` e `/boot` somente-leitura; `PrivateTmp=true` dá
  ao serviço um `/tmp` próprio; `NoNewPrivileges=true` impede escalada via setuid.
  Não usamos `ProtectSystem=strict` porque ele tornaria **todo** o sistema de
  arquivos read-only, e qualquer escrita temporária do runtime viraria uma falha
  difícil de diagnosticar na véspera.

**A linha mais importante está nos units do Tasks e do Gateway:**

```ini
# no unit do Tasks
Wants=todolist-identity.service          # deliberadamente Wants=, não Requires=

# no unit do Gateway
Wants=todolist-identity.service todolist-tasks.service
```

`Requires=` derrubaria o cliente junto quando o serviço do qual ele depende
caísse. O caminho de indisponibilidade — fail-closed no Tasks (D-28), 503 no
Gateway quando um back-end não responde — **deixaria de existir**: em vez de um
erro bem-comportado, você teria connection refused. `Wants=` expressa "prefiro
que ele esteja de pé", não "não existo sem ele". É por isso que o Gateway
continua respondendo (com 503 nos endpoints que dependem do back-end fora do
ar) mesmo se o Identity ou o Tasks caírem.

### Os `.env`

Formato lido pelo systemd como `KEY=VALUE` **literal**: nada de aspas em volta do
valor (elas viram parte do valor) e nada de `$VAR` (não há expansão de shell).

O duplo sublinhado é a convenção do .NET para hierarquia:
`ConnectionStrings__IdentityDb` no ambiente equivale a
`ConnectionStrings:IdentityDb` no `appsettings.json`. Variável de ambiente vence o
arquivo.

Valores que merecem atenção:

- `ASPNETCORE_ENVIRONMENT=Production` — explícito de propósito, nos três `.env`.
  Em `Development` o serviço leria o `appsettings.Development.json`, passaria a
  escutar em `localhost` e ficaria **inalcançável de fora da VM**. (O
  `publish.ps1` remove esse arquivo do pacote dos três serviços justamente para
  tornar o acidente impossível.)
- `UserStore__Provider=Persisted` — padrão desde BE-40/D-39 (era `InMemory` até o
  T2). Com `InMemory` (hoje restrito a teste), o Identity aprovaria por gRPC um
  dono que não existe em `identity.users`, e a criação quebraria só no `INSERT`,
  na FK cruzada. Falha tardia, no pior momento — e, no T2, `InMemory` em
  ambiente de deploy também violaria a proibição de dado em memória na demo.
- **Não existe mais `Jwt__SigningKey` em nenhum `.env`.** Desde BE-40 (RS256,
  D-38) a chave não é variável de ambiente: `Jwt__PrivateKeyPath` (Identity) e
  `Jwt__PublicKeyPath` (Gateway) são caminhos para os PEMs gerados por
  `install-on-vm.sh` em `/etc/todolist/jwt/` (seção 1.7 acima) — o primeiro
  chega via `LoadCredential=` do unit do Identity, o segundo via `Environment=`
  do unit do Gateway. Trocar a chave ainda invalida toda sessão em andamento
  (isso não mudou); o que mudou é onde ela mora e quem pode lê-la.
- **`UserStore__SeedDemoUsers`/`UserStore__DemoUserPassword` não existem mais.**
  Chegaram a existir em `identity.env` no T2 (D-36), quando ainda não havia
  cadastro real: o `DemoUserSeeder` populava `identity.users` com dois usuários
  fixos, e a senha em texto puro deles vinha por aqui. Onda E removeu o seed —
  cadastro é real agora (`POST /api/auth/register`) — e junto com ele estas
  duas variáveis.
- `Backends__IdentityGrpcAddress` / `Backends__TasksGrpcAddress` (novo no T2, só
  em `gateway.env`) — os dois endereços gRPC internos, sempre `127.0.0.1`
  (D-33). Este continua sendo o único `.env` sem segredo (a chave pública do
  Gateway não é segredo, e de todo modo mora no unit, não aqui — BE-40).
- **O endereço em que o Gateway escuta muda de novo em BE-42.** Era
  `0.0.0.0:8080` (D-33/D-37, T2) — o Gateway era a origem pública. Desde
  BE-42 passa a ser `127.0.0.1:8080`: só o nginx (mesma máquina) fala com
  ele. É configuração de endpoint Kestrel (`ASPNETCORE_URLS` ou
  `Kestrel__Endpoints__Http__Url`, conforme o que `gateway.env`/o unit
  fixarem), não código — mover de volta para `0.0.0.0` seria reabrir o
  Gateway para fora sem tocar em uma linha de C#, o que é exatamente o tipo
  de regressão silenciosa que a verificação de fora (seção 1 do README)
  existe para pegar.
- `Tasks__AllowAnonymousCreate` — **caiu no T2** (BE-35). O gatilho HTTP
  provisório do Tasks foi removido; a identidade agora chega só por gRPC, na
  metadata `x-user-id` preenchida pelo Gateway (D-34).

---

## 6. Perguntas prováveis, e o que responder

**"Como você garante que a tarefa não é criada se o Identity estiver fora?"**
O Tasks chama `ValidateUser` por gRPC antes do `INSERT`. Se a chamada falha ou
estoura o deadline, ele devolve `503 identity.unavailable` e não persiste nada —
fail-closed (D-28). O roteiro de apresentação demonstra isso ao vivo.

**"Quem valida o token, e por que o Tasks não faz isso sozinho?"**
Desde BE-40 (RS256, D-38), o **Gateway** valida localmente, com `AddJwtBearer`
e a chave pública — ele não pergunta mais ao Identity a cada requisição
(`ValidateToken` deixou de ter consumidor nesse caminho). O que continua igual
ao desenho original (D-31): só o Identity tem a chave que **assina**; a chave
pública do Gateway só verifica, nunca poderia forjar um token. O Tasks nunca
vê o JWT nos dois desenhos, só a identidade já resolvida na metadata
`x-user-id` (D-34).

**"Por que o Identity, o Tasks e, desde BE-42, o próprio Gateway não são
acessíveis de fora da VM?"**
Identity e Tasks confiam no chamador para saber quem é o usuário
(D-30/D-34) — segurança que só existe enquanto o Gateway for o único caminho
até eles (D-32). O Gateway, por sua vez, confia no nginx para o
`X-Forwarded-For` (`ForwardedHeaders` com `KnownProxies` restrito a
`127.0.0.1`, BE-42) — segurança que só existe enquanto o nginx for o único
caminho até ele. Por isso só a porta **80** (nginx) tem regra de firewall
liberando `0.0.0.0/0`; as outras cinco (5080/5081/5100/5101/**8080**) são
verificadas explicitamente, de fora da VPC, para confirmar que **não**
respondem — a 8080 entrou nessa lista com BE-42; antes dela, era a porta
pública.

**"E se a máquina reiniciar?"**
`systemctl enable` — os três serviços sobem no boot, na ordem certa (Identity,
Tasks, Gateway); o nginx sobe pelo próprio `enable` do pacote da distro,
independente da ordem dos três `.NET`.

**"Onde estão os segredos (senha do banco, chave JWT)?"**
Senha do banco: em `/etc/todolist/*.env` na VM, `600
root:root`, fora do repositório — `install-on-vm.sh` se recusa a instalar se
algum `.env` faltar ou ainda tiver o placeholder `TROQUE_ESTA_SENHA`. A chave
JWT (desde BE-40) não é um `.env`: é `/etc/todolist/jwt/private.pem`, `root:root
0400`, que só chega ao processo do Identity via `LoadCredential=` do systemd —
nem o `.env` nem o unit em si guardam o conteúdo da chave.

**"Por que o Tasks fala com o Identity, e o Gateway fala com os dois, em `127.0.0.1`?"**
Os três rodam na mesma VM, então o salto gRPC é interno. Isso mantém 5081 e 5101
fora da rede mesmo que a regra de firewall interna seja permissiva demais — o
caminho crítico nunca depende dela.
