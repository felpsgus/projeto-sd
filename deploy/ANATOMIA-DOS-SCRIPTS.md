# Anatomia dos scripts de deploy

O `README.md` desta pasta é o **runbook**: o que digitar, em que ordem.
Este documento é a **explicação**: o que cada script faz por dentro, por que faz
assim, e o que aconteceria se não fizesse.

Escrito para você conseguir responder, na banca, qualquer pergunta do tipo
"e como isso sobe?" sem precisar abrir o código.

---

## O mapa: quem roda quando

Quatro scripts, quatro momentos distintos. Nenhum deles chama o outro — quem
encadeia é você. Desde o T2, `install-on-vm.sh` instala e sobe **três** units
(Identity, Tasks e Gateway) em vez de duas, e `smoke.sh`/`demo.sh` falam com o
Gateway (porta 8080) em vez de falar direto com o Tasks (porta 5100).

| Script | Onde roda | Quando | Precisa de root? |
|---|---|---|---|
| `scripts/publish.ps1` | sua máquina (Windows) | antes de cada upload | não |
| `install-on-vm.sh` | `maquina-1-psd` | a cada deploy | **sim** (`sudo`) |
| `smoke.sh` | `maquina-1-psd` | depois de cada deploy, e ~1h antes da apresentação | não |
| `tmux-demo.sh` | `maquina-1-psd` | montar a tela da apresentação | não (mas os painéis usam `sudo`) |
| `demo.sh` | dentro do tmux | a apresentação em si | usa `sudo` no ato de indisponibilidade |

O fluxo completo, do seu Windows até a tela projetada:

```
  SUA MÁQUINA                          maquina-1-psd (10.128.0.4)
  ───────────                          ─────────────────────────
  publish.ps1
    ├─ build Release
    ├─ dotnet test  ────── portão: teste vermelho não vira pacote
    ├─ dotnet publish -r linux-x64  (identity, tasks, gateway)
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
                                              │  (Identity → Tasks → Gateway)
                              DEMO_PASSWORD=... ./smoke.sh   ← verificação, via Gateway :8080
                                              │
                              ./tmux-demo.sh → DEMO_PASSWORD=... ./demo.sh
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
2. `publish/identity`, `publish/tasks` e `publish/gateway` existem? Se você
   extraiu o tarball errado ou incompleto, é aqui que descobre — não depois de
   já ter parado os serviços que estavam funcionando.

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
presente. Desde o T2 o `identity.env` carrega dois placeholders distintos —
`TROQUE_ESTA_SENHA` (connection string e `UserStore__DemoUserPassword`) e
`TROQUE_ESTA_CHAVE` (`Jwt__SigningKey`) — e o `gateway.env` entra na mesma
checagem por consistência, ainda que não tenha segredo nenhum (D-31): é o
mesmo portão, para não haver um quarto arquivo com regra própria.

Ele também **corrige as permissões toda vez**: `600` (só o dono lê e escreve) e
dono `root`. Repare na consequência prática: o arquivo com a senha do Postgres é
ilegível para o usuário `todolist` sob o qual o serviço roda — quem lê é o
systemd, que roda como root, e injeta as variáveis no processo já iniciado.

Repare também no que o script **não** faz: ele nunca cria nem sobrescreve esses
arquivos. Os segredos são criados uma vez, à mão, e sobrevivem intactos a
qualquer número de redeploys. Um script que gerasse `.env` sozinho acabaria, mais
cedo ou mais tarde, com um segredo dentro do repositório.

### 1.7 Subir na ordem, e esperar

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

---

## 2. `smoke.sh` — a verificação

```bash
DEMO_PASSWORD=... ./smoke.sh                          # contra http://127.0.0.1:8080
DEMO_PASSWORD=... ./smoke.sh http://10.128.0.4:8080
```

> **T2 muda a entrada, não o papel.** O papel do script continua o mesmo do T1
> — uma verificação de fumaça, rodada depois de todo deploy e de novo cerca de
> uma hora antes da apresentação, que devolve OK/ERRO por cenário e `exit 1` se
> algo falhou, para encadear em automação. O que muda é o alvo: em vez de bater
> direto no Tasks (5100) com um `X-User-Id` arbitrário, o script agora fala com
> o **Gateway** (8080) — o que exige login de verdade primeiro
> (`POST /api/auth/login` com `DEMO_PASSWORD`, a mesma senha de
> `UserStore__DemoUserPassword`), para obter o access token que autentica as
> chamadas seguintes. Os cenários exatos cobertos (login válido/; inválido,
> token ausente/expirado, dono ativo/inativo/inexistente e afins) e os detalhes
> de implementação são do script em si — evite documentar aqui algo que possa
> divergir do código, já que ele está em reescrita nesta mesma etapa (BE-37).
>
> O que continua valendo do desenho do T1, e vale conferir se algo quebrar: a
> verificação é sempre **dupla** (status HTTP e `errorCode`/corpo, nunca só o
> primeiro) e a saída acumula falhas em vez de abortar no primeiro erro
> (`set -uo pipefail`, não `-e`) — assim uma falha isolada não esconde o
> resultado dos demais cenários.

---

## 3. `demo.sh` — o roteiro

```bash
DEMO_PASSWORD=... ./demo.sh
```

> **T2 muda o protocolo de entrada, o papel do roteiro continua o mesmo.** Um
> roteiro de apresentação, avançando por interação (Enter), narrado por você.
> A ideia geral do T1 se manteve: mostrar o caminho feliz completo (agora
> passando por **login real** no Gateway antes de criar a tarefa), um caminho
> negado por regra de negócio, e o comportamento de indisponibilidade de um
> back-end (fail-closed, D-28) — só que agora entrando por `POST
> /api/auth/login` e `POST /api/tasks` no Gateway (8080), com o access token
> emitido pelo Identity, em vez de um `X-User-Id` batendo direto no Tasks.
>
> Dois cuidados que continuam se aplicando, quaisquer que sejam os atos exatos
> do roteiro reescrito: um **aquecimento** antes do primeiro ato (a primeira
> chamada de um processo recém-iniciado paga conexão HTTP/2 e a primeira query
> do EF Core — melhor que isso aconteça enquanto você ainda fala, não diante da
> banca) e um `trap ... EXIT` em volta de qualquer ato que pare um serviço de
> propósito, para religá-lo mesmo se o script for interrompido no meio.
>
> Os atos exatos, o texto exibido e os detalhes de implementação são do script
> em si, que está sendo reescrito nesta mesma etapa (BE-37) — não documentados
> aqui para não divergir do código.

---

## 4. `tmux-demo.sh` — a tela

```
┌───────────────────────┬──────────────────────────┐
│                       │  log do IDENTITY         │
│                       ├──────────────────────────┤
│   roteiro (demo.sh)   │  log do TASKS            │
│                       ├──────────────────────────┤
│                       │  log do GATEWAY          │
└───────────────────────┴──────────────────────────┘
```

**Desde o T2, o layout ganhou um quarto painel** — o log do Gateway, empilhado
com os outros dois à direita. É a mesma ideia do T1, estendida a mais um
serviço: com três back-ends de log e um roteiro de apresentação, você ainda
precisa ver os quatro ao mesmo tempo numa única sessão SSH.

### O problema que o tmux resolve

Você tem **uma** sessão SSH pelo navegador e precisa mostrar **quatro** coisas ao
mesmo tempo: a requisição e os três lados do log. Sem multiplexador, você
alternaria entre abas e a correlação entre painéis — que é justamente o que
prova a comunicação entre os serviços — se perderia.

O tmux também protege contra a queda da conexão: a sessão continua viva no
servidor e `tmux attach -t demo` reconecta exatamente onde estava.

### Por que montar por script

```bash
tmux new-session -d -s demo -c "$DIR"
tmux split-window -h -t demo:0.0 -c "$DIR"
tmux split-window -v -t demo:0.1 -c "$DIR"
tmux split-window -v -t demo:0.2 -c "$DIR"
```

Dividir painel com `Ctrl+B` ao vivo, com a turma esperando, é onde se perde meio
minuto dos cinco — e às vezes o painel abre no lugar errado. Comandos
determinísticos produzem sempre o mesmo layout, agora com quatro painéis em vez
de três.

A numeração é posicional: `0.0` é o painel original; o primeiro `split -h` cria
`0.1` à direita; cada `split -v` subsequente sobre o último painel da direita
empilha mais um abaixo dele (`0.2`, depois `0.3`).

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

Se o roteiro reescrito precisar filtrar por um termo específico (como o T1 fazia
com `grep --line-buffered ValidateUser`), o mesmo cuidado se aplica: use
`--line-buffered`, sem o qual o grep no meio de um pipe acumula linhas antes de
imprimir e o log parece não responder à requisição, quando na verdade só está
atrasado.

### Dois cuidados práticos

**Aumente a fonte do terminal antes de começar.** Todo o cuidado com `-o cat`
existe para a linha caber; uma fonte grande demais desfaz isso — e com quatro
painéis em vez de três, cada um fica um pouco menor.

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
- `UserStore__Provider=Persisted` — com o padrão `InMemory`, o Identity aprovaria
  por gRPC um dono que não existe em `identity.users`, e a criação quebraria só no
  `INSERT`, na FK cruzada. Falha tardia, no pior momento.
- `Jwt__SigningKey` (novo no T2, só em `identity.env`) — obrigatória, gerada na
  VM (`openssl rand -base64 48`), nunca versionada. É a chave que assina e
  valida os tokens; trocá-la invalida toda sessão em andamento (D-31: o Identity
  é a única autoridade sobre tokens, a chave nunca sai dele).
- `UserStore__DemoUserPassword` (novo no T2, só em `identity.env`) — obrigatória
  quando `UserStore__SeedDemoUsers=true` (D-36); é a senha em texto puro que o
  seed usa para gerar o hash dos usuários de demonstração.
- `Backends__IdentityGrpcAddress` / `Backends__TasksGrpcAddress` (novo no T2, só
  em `gateway.env`) — os dois endereços gRPC internos, sempre `127.0.0.1`
  (D-33). Este é o único `.env` sem nenhum segredo — nenhuma chave `Jwt:*` (D-31).
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
Só o Identity — via RPC `ValidateToken` — porque a chave de assinatura (HS256)
nunca sai dele: distribuí-la tornaria qualquer consumidor um emissor em
potencial (D-31). O Gateway chama `ValidateToken` a cada requisição de entrada;
o Tasks nunca vê o JWT, só a identidade já resolvida na metadata `x-user-id`
(D-34).

**"Por que o Identity e o Tasks não são acessíveis de fora da VM?"**
Porque os dois confiam no chamador para saber quem é o usuário (D-30/D-34) —
segurança que só existe enquanto o Gateway for o único caminho até eles (D-32).
É por isso que só a porta 8080 tem regra de firewall liberando `0.0.0.0/0`; as
outras quatro (5080/5081/5100/5101) são verificadas explicitamente, de fora da
VPC, para confirmar que **não** respondem.

**"E se a máquina reiniciar?"**
`systemctl enable` — os três serviços sobem no boot, na ordem certa (Identity,
Tasks, Gateway).

**"Onde estão os segredos (senha do banco, chave JWT, senha de demonstração)?"**
Em `/etc/todolist/*.env` na VM, `600 root:root`, fora do repositório.
`install-on-vm.sh` se recusa a instalar se algum `.env` faltar ou ainda tiver um
placeholder (`TROQUE_ESTA_SENHA` ou `TROQUE_ESTA_CHAVE`).

**"Por que o Tasks fala com o Identity, e o Gateway fala com os dois, em `127.0.0.1`?"**
Os três rodam na mesma VM, então o salto gRPC é interno. Isso mantém 5081 e 5101
fora da rede mesmo que a regra de firewall interna seja permissiva demais — o
caminho crítico nunca depende dela.
