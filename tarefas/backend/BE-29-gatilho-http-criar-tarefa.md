# BE-29 — Gatilho HTTP temporário para `POST /api/tasks`

| | |
|---|---|
| **Domínio** | Tarefas / Borda |
| **Serviço** | Tasks (Microsserviço A) |
| **Depende de** | [BE-17](BE-17-criar-tarefa.md), [BE-28](BE-28-validacao-dono-grpc.md) |
| **Bloqueia** | [BE-31](BE-31-verificacao-t1.md) |
| **Regras cobertas** | RN-TASK-10, RN-AUTZ-01 |
| **Estimativa** | P |

## Objetivo

Existe uma forma de disparar a criação de tarefa por HTTP **antes** de a autenticação estar montada, para que a comunicação gRPC entre Tasks e Identity possa ser exercitada e demonstrada de ponta a ponta.

## Escopo

### Inclui

- O endpoint **`POST /api/tasks`** de [BE-17](BE-17-criar-tarefa.md), em Minimal API, com o **mesmo** contrato de request e response já especificado lá. Esta task **não** cria um endpoint novo: ela define o modo de operação provisório do endpoint existente.
- Um modo **`Tasks:AllowAnonymousCreate`** (booleano, padrão **`false`**), lido da configuração:
  - `true` → o endpoint entra na allowlist de `AllowAnonymous` de [BE-13](BE-13-protecao-endpoints.md) e o dono vem do header **`X-User-Id`**;
  - `false` → comportamento definitivo: o endpoint exige token e o dono vem de `ICurrentUser.Id`, lido do claim `sub`.
- Implementação alternativa de `ICurrentUser` na `Api` do Tasks, selecionada quando o modo anônimo está ligado, que lê `X-User-Id` do `HttpContext`. **A `Application` não muda**: o handler continua consumindo `ICurrentUser.Id` e não sabe de onde o id veio.
- Header `X-User-Id` ausente ou malformado no modo anônimo → **400** com erro de validação claro. Não há fallback silencioso aqui: sem dono não há o que validar no Identity.
- Log de **aviso** na inicialização quando `Tasks:AllowAnonymousCreate=true`, deixando explícito que o serviço está sem autenticação.
- Anotação `TODO` **com dono e prazo** no ponto exato do código, apontando esta task — para que o modo provisório não sobreviva por esquecimento.

### Não inclui

- Middleware de autenticação, verificação de token ou resposta **401** — essa é a etapa do API Gateway. Aqui o objetivo é justamente **não** ter autenticação ainda.
- Mudança no contrato do request: o corpo continua **sem** campo de dono ([BE-17](BE-17-criar-tarefa.md), CA-22).
- Novos endpoints. Listagem, edição e remoção seguem exigindo autenticação normalmente.

## Notas técnicas

- **A rota é `/api/tasks`, não `/tasks`.** O prefixo `/api` é a convenção já estabelecida em todas as tasks de borda; abrir exceção aqui criaria uma rota órfã que precisaria ser renomeada depois — e renomear rota é justamente o tipo de mudança que quebra o Gateway na etapa seguinte.
- **Por que uma flag e não um endpoint separado.** Um `POST /api/tasks/debug` paralelo duplicaria o handler, e a versão de demonstração inevitavelmente divergiria da real. Com a flag, o caminho exercitado agora é **o mesmo** que ficará em produção — só a origem da identidade muda.
- **Por que `X-User-Id` em header e não no corpo.** O dono nunca é dado de negócio enviado pelo cliente: ele é identidade. Colocá-lo no corpo o transformaria em campo do modelo e violaria [BE-17](BE-17-criar-tarefa.md) CA-22, obrigando a desfazer isso depois. Em header, ele ocupa o mesmo lugar conceitual que o `Authorization` vai ocupar (decisão **D-30**).
- **O modo anônimo é um risco assumido e delimitado.** `Tasks:AllowAnonymousCreate=true` significa que qualquer um cria tarefa em nome de qualquer usuário. Aceitável em ambiente local e de demonstração, **inaceitável** em qualquer ambiente exposto — daí o padrão `false`, o aviso na inicialização e o CA-08.
- O endpoint entra na allowlist do teste de guarda de rotas ([BE-13](BE-13-protecao-endpoints.md), CA-07) **com justificativa escrita no próprio teste**, referenciando esta task. Sair da allowlist é o sinal de que o modo provisório acabou.

## Critérios de aceite

### Modo provisório ligado

- [ ] **CA-01** — Com `Tasks:AllowAnonymousCreate=true`, `POST /api/tasks` sem cabeçalho `Authorization`, com `X-User-Id` de usuário ativo e corpo `{"title":"..."}`, responde **201** (RN-TASK-10).
- [ ] **CA-02** — A tarefa criada tem `OwnerId` igual ao valor de `X-User-Id`, verificado no banco (RN-AUTZ-01).
- [ ] **CA-03** — `X-User-Id` ausente, vazio ou não-`Guid` responde **400** com mensagem apontando o header — não 500, não fallback silencioso.
- [ ] **CA-04** — O corpo continua sem campo de dono: enviar `"ownerId"` no JSON é ignorado ([BE-17](BE-17-criar-tarefa.md), CA-22 preservado).
- [ ] **CA-05** — A criação passa pela validação de dono via gRPC ([BE-28](BE-28-validacao-dono-grpc.md)): `X-User-Id` de usuário inexistente responde **404**, e de usuário inativo responde **409**.
- [ ] **CA-06** — Subir com o modo ligado emite log de **aviso** na inicialização.

### Modo definitivo

- [ ] **CA-07** — Com `Tasks:AllowAnonymousCreate=false` (padrão), `POST /api/tasks` sem token responde **401** ([BE-13](BE-13-protecao-endpoints.md)).
- [ ] **CA-08** — Com o modo `false`, o header `X-User-Id` é **completamente ignorado**: enviá-lo junto de um token válido não muda o dono da tarefa. Este é o critério que garante que a porta provisória não vira escalada de privilégio.
- [ ] **CA-09** — Trocar entre os dois modos é mudança de configuração, sem recompilar e **sem alterar o handler**.

### Não regressão

- [ ] **CA-10** — Nos dois modos, o `CreateTaskHandler` é exatamente o mesmo código e todos os critérios de [BE-17](BE-17-criar-tarefa.md) e [BE-28](BE-28-validacao-dono-grpc.md) continuam válidos.
- [ ] **CA-11** — Os demais endpoints de tarefa continuam exigindo autenticação mesmo com o modo ligado.
- [ ] **CA-12** — O endpoint consta da allowlist do teste de guarda de rotas com justificativa escrita, e **apenas** quando o modo está ligado.

## Testes obrigatórios

- Integração com o modo ligado: CA-01 a CA-05.
- Integração com o modo desligado: CA-07, CA-08, CA-11 — **CA-08 é obrigatório**, é a única barreira entre "atalho de demonstração" e "qualquer um cria tarefa em nome de qualquer um".
- Teste de guarda de rotas: CA-12.

## Decisões em aberto

- **D-30** — Origem provisória da identidade no Tasks (`X-User-Id` sob flag). Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
