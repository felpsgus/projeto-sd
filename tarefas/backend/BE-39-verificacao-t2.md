# BE-39 — Verificação da comunicação REST → gRPC do T2: roteiro e critérios de aceite

| | |
|---|---|
| **Domínio** | Qualidade / Documentação |
| **Serviço** | todos (Identity, Tasks, Gateway) |
| **Depende de** | [BE-35](BE-35-tasks-servidor-grpc.md), [BE-36](BE-36-api-gateway.md), [BE-37](BE-37-deploy-t2-vm.md) |
| **Bloqueia** | — (fecha a etapa) |
| **Regras cobertas** | RN-AUTH-08, RN-AUTH-09, RN-TASK-02, RN-TASK-10 (verificação de ponta a ponta, mesmo espírito de BE-31) |
| **Estimativa** | P |

## Objetivo

Qualquer pessoa consegue, seguindo apenas o `README.md`, subir os três serviços e o Postgres, ver os quatro desfechos exigidos pelo enunciado do T2 (401 sem token, 401 com token inválido, 400 de validação, 201 de sucesso) acontecerem contra o Gateway, e ver o mesmo `traceId` correlacionando os logs dos três serviços.

## Escopo

### Inclui

- **Seção "Rodando o T2" no `README.md`**, no mesmo formato de runbook da seção "Rodando os dois serviços" de BE-31: pré-requisitos, subida do Postgres via `docker-compose`, aplicação das migrations (Identity primeiro), e a ordem de execução dos **três** processos — Identity, Tasks, Gateway — cada um com o comando `dotnet run` e a porta em que escuta.
> **Nomes de arquivo (decisão do tech lead, 11/09/2026):** os papéis de script do T1 foram mantidos, para não desmontar runbook, `tmux-demo.sh` e `publish.ps1`. O `deploy/demo-t2.sh` previsto originalmente é o **`deploy/smoke.sh` reescrito**; o roteiro da apresentação é o **`deploy/demo.sh` reescrito**; `scripts/demo-t2.ps1` substitui e remove `scripts/demo-curl.ps1`.

- **`scripts/demo-t2.ps1`**, rodado do notebook Windows, parametrizado por `-BaseUrl` (padrão `http://localhost:8080`), no mesmo estilo do antigo `scripts/demo-curl.ps1` (BE-31): cada passo imprime o status esperado × obtido e acumula falhas num contador, saindo com `exit 1` se algo divergir.
- **`deploy/smoke.sh`** (reescrito), equivalente rodado **dentro da VM** — a versão de verificação de dentro do ambiente implantado —, contra `http://127.0.0.1:8080` por padrão.
- **`deploy/demo.sh`** (reescrito), o roteiro da apresentação em atos, com `--warmup`.
- **A sequência, igual nos dois scripts:**
  1. `POST /api/tasks` **sem token** → **401**;
  2. `POST /api/tasks` **com token lixo** (string arbitrária no header `Authorization`) → **401**;
  3. `POST /api/auth/login` com o usuário **ativo** do seed → **200** com `accessToken` e `expiresAt` no corpo. A senha é o valor de `UserStore:DemoUserPassword` e entra no script por **parâmetro** (`-DemoPassword` no `.ps1`, variável de ambiente no `.sh`) — **nunca** fixada no script versionado;
  4. `POST /api/tasks`, com o `accessToken` do passo 3 e **título vazio** → **400**;
  5. `POST /api/tasks`, com o mesmo token e um título **válido** → **201**, com header `Location` apontando para o recurso criado;
  6. `POST /api/auth/login` com o usuário **inativo** do seed → **401**, com corpo idêntico ao de um login com senha errada (RN-AUTH-09 — não revelar que o usuário existe, mas está inativo). **Obrigatório no script**, que o executa sem custo; **opcional na apresentação**, onde o tempo é curto.
- **Evidência de log**: para o par sucesso (passos 3 e 5), documentar o trecho de log esperado dos **três** serviços, todos carregando o **mesmo `traceId`**:
  - Gateway: as linhas de chamada gRPC de saída (`ValidateToken`, `CreateTask`) exigidas por [BE-36](BE-36-api-gateway.md) CA-26;
  - Tasks: a chamada de saída para `ValidateUser` no Identity (log já existente desde BE-27/BE-31);
  - Identity: a linha de `ValidateToken` respondida e a linha de `ValidateUser` respondida.
- **Caminho de falha controlada**: com o Identity **parado**, `POST /api/tasks` (com token válido emitido antes da queda, ou qualquer token) responde **503** com `Retry-After`, **nunca** 401 nem 500 — o Gateway distingue "não consigo validar o token porque o Identity está fora" de "o token é inválido" (a primeira é falha de infraestrutura, a segunda é decisão de negócio — mesma distinção de D-28 aplicada agora à validação de token, não só à validação de dono).
- **Roteiro de apresentação de até 5 minutos** (limite de `t2.md`), numa seção "No dia da apresentação do T2" do `deploy/README.md` — mesmo lugar e formato da seção equivalente do T1 —, com:
  - tempo alocado por passo (a soma **não pode ultrapassar 5 minutos**, com folga — o enunciado do T2 zera a nota da apresentação oral em caso de estouro);
  - o que mostrar do código em cada passo: o middleware de autenticação do Gateway (401 sem/@com token inválido), o validador de payload na borda (400), a tradução JSON → gRPC (o handler que monta `CreateTaskRequest` e chama `TasksService.CreateTask`);
  - **checklist da última hora** (mesmo formato do checklist de BE-31/deploy do T1): serviços ativos, IP externo confirmado, `smoke.sh` verde, aquecimento disparado, terminal com fonte legível;
  - **ensaio cronometrado obrigatório** — o roteiro só é considerado pronto depois de ser executado contra o relógio pelo menos uma vez, com o tempo real registrado (mesma exigência de "alguém que não escreveu o código" de BE-31, adaptada ao cronômetro em vez de à familiaridade com o código).

### Não inclui

- Qualquer novo comportamento nos serviços — esta task documenta e verifica o que BE-32 a BE-37 produziram; um defeito encontrado aqui é da task de origem, não desta.
- Refresh token, logout, cadastro de usuário — fora do escopo do T2 (D-36).
- Verificação de containers/Docker — isso é BE-38, com seu próprio CA-03 cobrindo o mesmo roteiro contra `localhost:8080` em compose.
- Cloud Run, HTTPS público, Artifact Registry — T3.

## Notas técnicas

- **Por que o par 401/401 (sem token, token lixo) entra separado, e não como um único passo.** São dois caminhos de código possivelmente diferentes no middleware (ausência de header vs. presença de um header que falha na validação) — um teste que cobrisse só um dos dois deixaria o outro sem verificação, e a diferença é exatamente o tipo de bug que passa despercebido em revisão de código, mas aparece na primeira demonstração real.
- **Por que o 503 do Identity fora do ar não pode virar 401.** Um Gateway que traduzisse "não consegui falar com o Identity" em "não autorizado" estaria mentindo sobre a causa — o cliente (ou a plateia) concluiria erroneamente que o token está errado, quando o problema é de infraestrutura. A distinção espelha D-28, agora do lado da validação de token em vez da validação de dono.
- **Por que o mesmo `traceId` nos três serviços, e não só nos dois do T1.** O Gateway é agora o ponto de entrada e faz **duas** chamadas gRPC de saída por requisição de criação de tarefa (`ValidateToken` no Identity, depois `CreateTask` no Tasks, que por sua vez chama `ValidateUser` no Identity de novo) — a cadeia inteira só é auditável se o identificador de correlação atravessar os três saltos, não apenas o par que já existia no T1.
- **Por que o ensaio cronometrado é obrigatório, e não "recomendado".** O enunciado do T2 zera a nota de apresentação oral por estouro de tempo — um roteiro nunca cronometrado é uma aposta, não uma verificação.

## Critérios de aceite

- [ ] **CA-01** — Existe uma tabela **requisito do `t2.md` → passo do roteiro → evidência** (log, resposta HTTP, ou ambos) cobrindo os quatro requisitos numerados do enunciado (REST público, validação na borda com 400/201, autenticação com 401, tradução JSON→gRPC).
- [ ] **CA-02** — `scripts/demo-t2.ps1` e `deploy/smoke.sh` saem com código **≠ 0** se qualquer status HTTP divergir do esperado, e com **0** quando todos os passos passam.
- [ ] **CA-03** — O roteiro de apresentação (`deploy/README.md`) cabe em **5 minutos** num ensaio cronometrado real, com o tempo registrado.
- [ ] **CA-04** — A verificação contra a VM (`scripts/demo-t2.ps1 -BaseUrl http://<IP externo>:8080`) é executada **a partir de fora** — pelo IP externo da `maquina-1-psd`, porta 8080 —, não de dentro da própria VM via `127.0.0.1`.
- [ ] **CA-05** — Os passos 1 e 2 da sequência (sem token, token lixo) respondem **401** nos dois — comprovando que o middleware trata os dois casos, não apenas um deles.
- [ ] **CA-06** — O passo 6 (usuário inativo) responde **401** com corpo indistinguível do de uma credencial simplesmente errada (RN-AUTH-09 estendida ao Gateway).
- [ ] **CA-07** — O caminho de falha controlada (Identity parado) responde **503** com `Retry-After`, nunca 401 nem 500, verificado com o Identity de fato encerrado.
- [ ] **CA-08** — O trecho de log documentado no README/roteiro mostra o mesmo `traceId` nas linhas do Gateway, do Tasks e do Identity, para o caminho de sucesso.
- [ ] **CA-09** — Nenhum log ou resposta mostrados no material da task contém senha, hash de senha ou o access token completo (RN-AUTH-05, mesmo cuidado de BE-31 CA-12).

## Testes obrigatórios

- Integração ponta a ponta, automatizada pelos próprios scripts: CA-02, CA-05, CA-06, CA-07 — os três serviços reais, não substituídos.
- Verificação manual documentada de CA-03 (ensaio cronometrado) e CA-04 (a partir de fora), com o resultado registrado no PR — mesmo padrão de "verificação manual não auto-certificável" já usado em BE-31 CA-02.

## Decisões em aberto

- **D-32** — Gateway como única origem pública; a verificação de fora (CA-04) é a comprovação de campo. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
- **D-34** — Identidade do Gateway ao Tasks via metadata `x-user-id`; referenciado aqui como parte do que a evidência de log (CA-08) precisa mostrar do lado do Tasks. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
- **D-36** — Login mínimo via `POST /api/auth/login`; a sequência do passo 3 depende diretamente deste contrato. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).

## Emenda (21/09/2026) — enunciado novo: 10 minutos, frontend obrigatório, banco real na tela

O `t2.md` mudou em quatro pontos que reescrevem o roteiro desta task: apresentação de **até 10 minutos** (era 5), frontend obrigatório como ponto de partida da demonstração, persistência real **exibida** (registros criados/consultados, não só inferidos pelo status HTTP), e o middleware de JWT agora vive no Gateway (BE-40). BE-41 (listar/consultar) e BE-42 (nginx) também mudam o que há para demonstrar. O texto original acima permanece como registro do roteiro de 5 minutos; esta seção o substitui como roteiro vigente.

### Roteiro de apresentação — até 10 minutos, partindo do frontend

O estouro de tempo continua **zerando a nota da apresentação oral** (critério do `t2.md`) — o limite dobrou, mas a exigência de ensaio cronometrado (ver Notas técnicas originais) não afrouxa.

1. **Login** pelo frontend (Angular, servido por nginx em `http://<IP>/`).
2. **Criar tarefa com título vazio** → o formulário mostra o erro (**400** do Gateway, traduzido pelo frontend em mensagem de campo) — sem round-trip até o Tasks.
3. **Criar tarefa válida** → **201**, a tarefa aparece na lista (BE-41) sem recarregar a página.
4. **Registro mostrado no `psql`** — `SELECT` direto em `tasks.tasks` na VM, mostrando a linha que acabou de ser criada pelo passo 3, com o mesmo `title`/`id` visto na tela. É a evidência ao vivo do requisito "persistência real comprovada" do `t2.md` — não basta o 201, alguém precisa ver a linha no banco.
5. **401** — demonstrado principalmente pelo `scripts/demo-t2.ps1` contra `http://<IP>/api` (ver abaixo), não pela interação manual no navegador; o interceptor HTTP do frontend trata um 401 vindo do Gateway redirecionando para a tela de login, o que também é mostrado rapidamente.
6. **Logs com o mesmo `traceId`** nos três serviços (Gateway, Tasks, Identity) para a requisição de criação do passo 3 — mesma evidência do roteiro original, agora atravessando também o nginx sem quebrar (BE-42).
7. **Código**: middleware de autenticação JWT do Gateway (`AddJwtBearer`, BE-40), o validador de payload na borda, a tradução JSON → gRPC (o handler que monta `CreateTaskRequest`), e o `.proto` (`tasks.proto`, com `CreateTask`/`ListTasks`/`GetTask`, BE-41).

### O 401 é demonstrado pelo script, não só pela mão

`scripts/demo-t2.ps1`, contra `http://<IP>/api` (porta 80, via nginx — **não** mais `http://<IP>:8080`), roda pelo menos três variações de token inválido durante a apresentação:

- sem token;
- token lixo (string arbitrária no header `Authorization`);
- token **adulterado** (um JWT real, com um caractere alterado no payload ou na assinatura) — caso novo em relação ao roteiro original, que cobria só "sem token" e "token lixo"; adulterado exercita especificamente a verificação de assinatura RS256 do `AddJwtBearer` (BE-40), que "token lixo" (nem chega a parecer um JWT) não exercita da mesma forma.

Os três casos respondem **401** com o mesmo corpo (`errorCode=auth.unauthorized`), e o **interceptor HTTP do frontend** intercepta esse 401 e leva o usuário à tela de login — esse comportamento do frontend é mostrado uma vez (passo 5 acima), não repetido para os três casos do script.

### CA-01 atualizado — sete requisitos numerados do `t2.md`

- [ ] **CA-01** — Existe uma tabela **requisito do `t2.md` (1 a 7) → passo do roteiro → evidência** (log, resposta HTTP, tela do frontend, ou combinação), cobrindo:
  1. Frontend funcional, falando só com o Gateway;
  2. API Gateway como ponto único de entrada REST;
  3. Backend com ≥ 2 microsserviços internos via gRPC;
  4. Banco de dados real, com persistência **exibida** (não só inferida);
  5. Validação de payload no Gateway (400/201);
  6. Middleware JWT no Gateway (401 na borda);
  7. Tradução REST → gRPC/Protobuf.

### Base dos scripts

`scripts/demo-t2.ps1` e `deploy/smoke.sh` passam a usar como padrão a base **`http://<IP>`** (porta 80, via nginx) — não mais `http://localhost:8080`/`http://127.0.0.1:8080` diretamente no Gateway. **CA-02 e CA-04 são superados por BE-42/D-40** nesse ponto específico: a verificação "a partir de fora" (CA-04) já era pela porta pública, que agora é 80 em vez de 8080; o comportamento dos scripts (código de saída ≠ 0 em divergência) não muda.

### CA-03 (tempo) atualizado

- [ ] **CA-03 (revisado)** — O roteiro de apresentação cabe em **10 minutos** num ensaio cronometrado real, partindo do frontend, com o tempo registrado — substitui o limite de 5 minutos do CA-03 original.

Os demais critérios de aceite do texto original (CA-05 a CA-09) continuam válidos: os dois 401 do par sem-token/token-lixo, o 401 do usuário inativo, o 503 do Identity fora do ar, o `traceId` compartilhado e a ausência de segredo em log seguem sendo evidência obrigatória — a diferença é que agora atravessam também o nginx e, no caso do 401, ganham o terceiro caso (token adulterado) e a mediação do frontend.
