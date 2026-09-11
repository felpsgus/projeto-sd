# BE-37 — Deploy do T2 na VM: Gateway, firewall e upgrade do ambiente do T1

| | |
|---|---|
| **Domínio** | Infraestrutura |
| **Serviço** | todos (Identity, Tasks, Gateway) |
| **Depende de** | [BE-36](BE-36-api-gateway.md) |
| **Bloqueia** | [BE-39](BE-39-verificacao-t2.md) |
| **Regras cobertas** | nenhuma de negócio — implanta o que BE-32 a BE-36 produziram, sob **D-32** (única origem pública é o Gateway) |
| **Estimativa** | M |

## Objetivo

A `maquina-1-psd` passa a rodar três units systemd — Identity, Tasks e Gateway —, com o Gateway como única porta alcançável de fora da VPC, sem perder o que o T1 já tinha no ar.

## Escopo

### Inclui

- **`scripts/publish.ps1` publica um terceiro serviço.** `publish/gateway/` é gerado com o mesmo padrão de `publish/identity` e `publish/tasks` (mesmo `-r linux-x64`, mesma remoção de `appsettings.Development.json`, mesma entrada no tarball). O T2 **não tem migration nova**: o login lê `identity.users` como a tabela já existe (BE-33 apenas regrava o hash); `sql/01-identity.sql` e `sql/02-tasks.sql` continuam sendo os dois únicos scripts gerados.
- **`deploy/todolist-gateway.service`**, novo unit, no mesmo estilo dos outros dois (`User=todolist`, `EnvironmentFile=/etc/todolist/gateway.env`, `Restart=always`, `NoNewPrivileges=true`, `ProtectSystem=full`):
  - `After=network-online.target todolist-identity.service todolist-tasks.service`;
  - `Wants=todolist-identity.service todolist-tasks.service` (não `Requires=` — mesmo raciocínio do unit do Tasks: o Gateway deve continuar de pé e devolver 503 se um backend cair, não morrer junto).
- **`deploy/gateway.env.example`**, novo arquivo, versionado:
  - `ASPNETCORE_ENVIRONMENT=Production`;
  - `Backends__IdentityGrpcAddress=http://127.0.0.1:5081`;
  - `Backends__TasksGrpcAddress=http://127.0.0.1:5101`;
  - **nenhuma chave `Jwt:*`** — o Gateway não valida token localmente, só pergunta ao Identity via `ValidateToken` (**D-31**, [BE-36](BE-36-api-gateway.md) CA-15). O `.example` traz um comentário dizendo isso explicitamente, para ninguém "completar" o arquivo com a chave por engano;
  - comentário explicando por que os dois endereços são `127.0.0.1`: os três serviços seguem na mesma VM, o salto gRPC é interno, e isso é o que mantém 5081/5101 fora do alcance de quem não é o Gateway mesmo que a regra de firewall falhe.
- **`deploy/identity.env.example` ganha quatro chaves novas:**
  - `Jwt__SigningKey` — **obrigatória**, só variável de ambiente (nunca em `appsettings*.json`), com comentário instruindo a gerar com `openssl rand -base64 48` (≥ 32 bytes, [BE-08](BE-08-emissao-jwt.md) CA-03) e um lembrete de que trocar a chave invalida todo token emitido antes;
  - `Jwt__Issuer`, `Jwt__Audience` — obrigatórias, valores de exemplo (`todolist-identity`, `todolist-gateway` ou equivalentes);
  - `UserStore__DemoUserPassword` — obrigatória **quando** `UserStore__SeedDemoUsers=true` (D-36, BE-33 CA-07), com o mesmo comentário de cuidado que já existe para a connection string: nunca versionar o valor preenchido.
- **`deploy/tasks.env.example` perde `Tasks__AllowAnonymousCreate`** (o gatilho HTTP provisório foi removido em BE-35: Tasks passa a ser só gRPC). A porta 5101 (`Http2`) é documentada no lugar da 5100 REST — a 5100 continua existindo, mas só para `/health` (`Http1`), e o comentário explica a divisão de portas por protocolo, no mesmo estilo de BE-30 para o Identity.
- **`install-on-vm.sh` instala e habilita a terceira unit:**
  - a lista de diretórios verificados no início (`publish/identity`, `publish/tasks`) ganha `publish/gateway`;
  - a função `copiar_servico` é chamada também para `gateway`;
  - o portão de segredos (`/etc/todolist/*.env` presente, sem placeholder) passa a checar também `gateway.env`, junto de `identity.env` e `tasks.env`;
  - a instalação de units (`install -m 644 ...`) ganha `todolist-gateway.service`;
  - **a ordem de subida passa a ser Identity → Tasks → Gateway** (ver Notas técnicas), cada uma esperando o `/health` da anterior antes de prosseguir, com o mesmo padrão de espera por condição (`for _ in $(seq 1 30)`) já usado entre Identity e Tasks.
- **Firewall VPC**, revisão das três regras existentes e uma nova:
  - nova regra **`todolist-allow-gateway`** — tag `todolist-app`, origem `0.0.0.0/0`, `tcp:8080`. É a **única** regra de ingresso de aplicação vinda da internet;
  - **`todolist-allow-grpc-internal` é revista**: de `tcp:5081` (só o Identity) para `tcp:5081,5101` (Identity + Tasks), continuando restrita a `10.128.0.0/20` — a declaração de intenção do canal gRPC interno, agora cobrindo os dois back-ends que o Gateway chama;
  - `todolist-allow-postgres` e `todolist-allow-iap-ssh` seguem como no T1, sem alteração;
  - **verificação explícita, de fora da VPC** (do notebook do aluno, não de dentro da VM), de que `5080`, `5081`, `5100` e `5101` **não respondem** — um `curl --max-time 3` contra o IP externo em cada porta, esperando timeout ou recusa de conexão, nunca uma resposta HTTP. É a comprovação de campo de D-32.
- **IP externo:** recomendar reservar um IP estático para a `maquina-1-psd` (**Compute Engine → Endereços IP → Promover a estático**), para o endereço não mudar entre o ensaio e o dia da apresentação — um IP efêmero pode trocar se a VM for parada e reiniciada.
- **`deploy/tmux-demo.sh` ganha um quarto painel** para o log do Gateway (`journalctl -u todolist-gateway`), reorganizando o layout de 3 para 4 painéis (ex.: roteiro à esquerda, três logs empilhados à direita, ou o layout que couber legível na tela — a costura exata fica a critério de quem editar o script, desde que a fonte continue legível de longe, mesmo cuidado do T1).
- **`deploy/README.md` ganha uma seção do T2**, no mesmo formato de runbook do T1 (numerada, com blocos de comando prontos para copiar): os passos novos de firewall, o novo `.env`, a nova unit, a ordem de restart, e a verificação de fora.
- **A lista "Depois do T1" é revista**: os itens 1 (`Tasks__AllowAnonymousCreate=true`) e 3 (`PasswordHash` placeholder) **caem** — o T2 os resolve (BE-35 remove a flag; BE-33 substitui o placeholder). O item 2 (`UserStore__SeedDemoUsers=true`) **continua** de pé — ainda não há cadastro real (BE-07 segue fora do escopo do T2), então o seed continua sendo a única forma de existir usuário para o roteiro de login.
- **Roteiro de upgrade, não reinstalação**, documentado como uma sequência própria dentro da seção do T2:
  1. o banco **já tem** as linhas de `identity.users` com o hash placeholder (estado em que o T1 deixou); nada precisa ser recriado — o seed do BE-33, ao rodar de novo, vê que a senha de demonstração não confere com o hash armazenado e o regrava ([BE-33](BE-33-login-minimo-grpc.md), CA-09);
  2. nenhuma migration nova a aplicar;
  3. editar `/etc/todolist/identity.env`, acrescentando `Jwt__SigningKey` (gerada uma vez na VM, nunca reaproveitando algo de teste), `Jwt__Issuer`, `Jwt__Audience` e `UserStore__DemoUserPassword`; editar `/etc/todolist/tasks.env`, removendo `Tasks__AllowAnonymousCreate`; criar `/etc/todolist/gateway.env` a partir do exemplo — ele só tem endereços, nenhum segredo;
  4. `scripts/publish.ps1` → upload do tarball → extrair por cima do `~/todolist-deploy` existente (mesmo fluxo do T1);
  5. `sudo ./install-on-vm.sh`, que agora também instala e sobe o Gateway;
  6. **ordem de restart: Identity → Tasks → Gateway** — a mesma ordem de subida do script, e a razão de ser assim (ver Notas técnicas);
  7. conferir a partir de fora (IP externo, porta 8080) antes de considerar a VM pronta para o ensaio.

### Não inclui

- Docker na VM — os Dockerfiles do T3 são escritos nesta mesma etapa (BE-38), mas validados **só localmente**, nunca implantados na `maquina-1-psd`.
- HTTPS na VM — o Gateway responde em HTTP puro na 8080; TLS fica para o Cloud Run do T3.
- Qualquer coisa do Cloud Run, Artifact Registry ou desligamento de VM — isso é T3.
- Migration nova de banco — não há, como registrado acima.
- Alterar o conteúdo funcional do Gateway, do Identity ou do Tasks — esta task só implanta o que BE-33/BE-34/BE-35/BE-36 produziram.

## Notas técnicas

- **Por que a ordem Identity → Tasks → Gateway, tanto na subida quanto no restart.** O Gateway é cliente gRPC dos outros dois (D-33); subir ou reiniciar o Gateway primeiro não quebra nada de fato (ele reconecta na primeira chamada), mas subir os backends primeiro evita que as primeiras requisições reais — inclusive as do ensaio — encontrem 503 por um backend ainda de pé. É a mesma lógica de espera por `/health` que o T1 já aplicava entre Identity e Tasks, estendida a mais um salto.
- **Por que a regra de firewall interna cresce para 5081 **e** 5101, e não vira uma porta só.** Identity e Tasks continuam sendo processos e portas gRPC distintos, na mesma VM; a regra existe para declarar a intenção do canal (como já era no T1) — o gRPC efetivo entre os três continua sendo por `127.0.0.1`, então a regra de sub-rede é redundância intencional, não o caminho crítico.
- **Por que verificar as portas de fora é um passo explícito, e não apenas "confiar na regra".** Uma regra de firewall mal escrita (origem errada, porta errada, tag não aplicada à VM) é um erro silencioso — a aplicação continua funcionando via Gateway e ninguém percebe que 5081 também ficou aberto para a internet até ser tarde. **D-32** exige a comprovação, não só a declaração.
- **Por que reservar IP estático agora, e não no dia.** Promover um IP efêmero para estático depois que ele mudou é tarde: qualquer material de apresentação (slide, script salvo) que já cite o IP precisaria ser refeito. Reservar com antecedência custa uma tela do Console e elimina o risco.
- **Por que o roteiro é upgrade e não reinstalação.** `install-on-vm.sh` já é idempotente (T1) e nunca recria os `.env`; o T2 usa exatamente esse mecanismo. Reinstalar do zero jogaria fora os usuários já semeados e obrigaria recriar segredos que já estão corretos — trabalho e risco desnecessários.

## Critérios de aceite

- [ ] **CA-01** — As três units (`todolist-identity`, `todolist-tasks`, `todolist-gateway`) estão `active (running)` na `maquina-1-psd` e continuam assim depois de um `sudo reboot` da VM (`systemctl is-enabled` confirma as três habilitadas).
- [ ] **CA-02** — O Gateway responde a uma requisição real (`POST /api/auth/login` ou o roteiro de BE-39) a partir de **fora** da VPC, pelo IP externo na porta 8080.
- [ ] **CA-03** — As portas 5080, 5081, 5100 e 5101 **não respondem** a partir de fora da VPC — verificado por `curl --max-time 3` contra o IP externo, recusa de conexão ou timeout em todas.
- [ ] **CA-04** — Nenhum segredo (chave JWT, senha de banco, senha de demonstração) está versionado em `deploy/*.env.example`, no `.git` ou em qualquer arquivo do repositório — só os `.env` reais na VM, `600 root:root`.
- [ ] **CA-05** — O upgrade da VM do T1 para o T2 preserva os dados: os usuários semeados no T1 continuam existindo (mesmo `id`), agora com hash real em vez do placeholder, e nenhuma tarefa pré-existente em `tasks.tasks` é perdida.
- [ ] **CA-06** — O tempo do roteiro de subida completo (do upload do tarball até as três units `active` e a verificação de fora respondendo) está documentado no `deploy/README.md`, medido em pelo menos uma execução real.
- [ ] **CA-07** — `install-on-vm.sh` se recusa a continuar se `gateway.env` estiver faltando ou com algum valor placeholder, no mesmo padrão já aplicado a `identity.env`/`tasks.env`.
- [ ] **CA-08** — A lista "Depois do T1" no `deploy/README.md` reflete a saída dos itens 1 e 3 e a permanência do item 2, com a justificativa de cada um.

## Testes obrigatórios

- Verificação manual na VM real (não há como automatizar contra infraestrutura GCP nesta etapa): CA-01 a CA-03, CA-05 a CA-08, executados e registrados no PR com a saída dos comandos (`systemctl status`, `curl` de fora, timestamps do roteiro).
- CA-04 é verificável por varredura textual do repositório (`git grep`) antes do commit — nenhuma ocorrência de valor de segredo preenchido.

## Decisões em aberto

- **D-32** — Gateway como única origem pública; Identity e Tasks não acessíveis de fora. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
- **D-33** — Endereços do Gateway em `Backends:IdentityGrpcAddress`/`Backends:TasksGrpcAddress`, Kestrel em `0.0.0.0:8080`. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
- **D-36** — Chaves obrigatórias de JWT e senha de demonstração, exigidas na inicialização do Identity. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
