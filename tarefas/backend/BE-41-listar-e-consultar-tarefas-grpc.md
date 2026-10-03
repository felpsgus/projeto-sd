# BE-41 — Listar e consultar tarefas via gRPC (recorte de BE-22 e BE-18)

> **Nova task, emenda ao T2 (21/09/2026).** O enunciado antigo do T2 só exigia `POST /api/tasks` via Gateway; o novo exige um frontend funcional que **cria e lista** tarefas (decisão do usuário, 21/09/2026: front cobre login, criar e listar). Sem leitura, o front não tem o que mostrar, e o **201** do `POST` aponta um `Location` para uma rota que hoje não existe. Esta task estende `tasks.proto` (BE-32) e o Gateway (BE-36) com `ListTasks`/`GetTask`, como um recorte estrito de [BE-22](BE-22-listagem-tarefas.md) (sem filtro, busca ou ordenação configurável) e de [BE-18](BE-18-consultar-tarefa-autorizacao.md) (autorização por dono, resposta idêntica para inexistente/alheia).

| | |
|---|---|
| **Domínio** | Tarefas / Borda |
| **Serviço** | Tasks, Gateway |
| **Depende de** | [BE-32](BE-32-contratos-grpc-t2.md), [BE-35](BE-35-tasks-servidor-grpc.md), [BE-36](BE-36-api-gateway.md) |
| **Bloqueia** | recorte FE do T2 (listagem depende deste contrato) |
| **Regras cobertas** | RN-LIST-01, RN-LIST-06 (parcial — só o desempate por criação, sem os demais critérios de BE-22), RN-LIST-07, RN-AUTZ-01 a RN-AUTZ-04, RN-TASK-16 |
| **Estimativa** | M |

## Objetivo

O usuário lista as próprias tarefas (paginado, mais recentes primeiro) e consulta uma tarefa específica por id — os dois caminhos exclusivamente do dono, com o mesmo desenho de autorização de [BE-18](BE-18-consultar-tarefa-autorizacao.md). É o mínimo que o frontend do T2 precisa para mostrar o que `POST /api/tasks` acabou de criar.

## Escopo

### Inclui

**Contrato (`contracts/tasks/v1/tasks.proto`)** — mudança **compatível** na v1 (acrescenta RPCs e mensagens; nada que já existia muda de forma):

```protobuf
service TasksService {
  rpc CreateTask (CreateTaskRequest) returns (TaskReply);
  rpc ListTasks (ListTasksRequest) returns (ListTasksReply);
  rpc GetTask (GetTaskRequest) returns (TaskReply);
}

message ListTasksRequest {
  int32 page = 1;
  int32 page_size = 2;
}

message ListTasksReply {
  repeated TaskReply items = 1;
  int32 page = 2;
  int32 page_size = 3;
  int32 total_count = 4;
}

message GetTaskRequest {
  string id = 1;
}
```

`TaskReply` é o mesmo já declarado por BE-32 — nenhum campo novo.

**Tasks — handlers em Application**

- `ListTasks`: filtra **sempre** por `owner_id == x-user-id` (metadata gRPC, D-34) **e** não removida (RN-LIST-01) — mesmo filtro base inegociável de BE-22, sem exceção por parâmetro.
- Ordenação: **por criação decrescente** (mais recente primeiro) — é o único critério desta task. **Sem** filtro por status/prioridade/atraso, **sem** busca textual, **sem** parâmetro de ordenação alternativa: isso é o recorte de [BE-22](BE-22-listagem-tarefas.md), que continua sendo a task que os introduz quando (e se) entrar em outra etapa. Desempate por `Id` para paginação estável, mesma razão de BE-22.
- Limites de `page`/`pageSize`: **os mesmos de BE-22** — `page ≥ 1`, `pageSize` 1–100, padrão `pageSize=20` (`Paging:DefaultPageSize`/`Paging:MaxPageSize`, D-09). Fora da faixa → erro de validação (`InvalidArgument`), traduzido pelo Gateway em 400 na borda (ver abaixo) antes mesmo de chegar aqui quando possível, e como segunda linha de defesa no Tasks quando um chamador gRPC direto não passar pelo Gateway.
- `isOverdue` na projeção de cada item usa `x-client-date` (D-18/D-34), mesmo mecanismo de `CreateTask`/`TaskReply` já em uso — nenhum novo transporte de data.
- `GetTask`: resolve pelo mesmo método único de BE-18 (`GetOwnedTaskAsync`-equivalente no Tasks) — filtra por `owner_id` e não-removida na própria query. Tarefa de **outro dono** e tarefa **inexistente** produzem a **mesma** resposta gRPC (`NotFound`, sem distinção de causa) — RN-AUTZ-03 aplicada ao transporte gRPC: um `NotFound` não revela se o id existe e pertence a outro usuário, ou se simplesmente não existe.
- Toda a consulta é executada no banco (`Skip`/`Take`, `WHERE owner_id = ...`), nunca carregando tudo em memória — mesma exigência de BE-22.

**Gateway — tradução REST**

- `GET /api/tasks?page=&pageSize=` → **200** com `{items, page, pageSize, totalCount}` (nomes de campo em `camelCase`, mesmo padrão dos demais DTOs do Gateway). `page`/`pageSize` fora dos limites → **400** de validação **na borda** — o Gateway rejeita antes de chamar o Tasks (mesmo espírito de defesa em profundidade de BE-36: os limites existem duplicados em Gateway e Tasks, mas o Gateway é quem evita a chamada de rede desnecessária).
- `GET /api/tasks/{id}` → **200** com `TaskHttpResponse` (o mesmo DTO que `POST` já devolve) ou **404** — o `RpcException.NotFound` do Tasks vira 404 pelo `GrpcErrorMapping` já existente (D-35), sem mapeamento novo.
- Id em formato inválido (`/api/tasks/abc`) → **400** de validação na borda, sem round-trip gRPC — mesmo padrão de BE-18 CA-07. **Não** usar a restrição de rota `{id:guid}`: com ela, um id inválido não casa com a rota e vira **404**, não 400. A rota recebe `{id}` como string e o endpoint faz `Guid.TryParse`.
- **Consequência para o `POST` existente:** o `Location: /api/tasks/{id}` do 201 de `CreateTask` (BE-36) passa a apontar para uma rota que **existe de fato** — antes desta task, o `Location` apontava para um caminho sem handler correspondente no Gateway. Nenhuma mudança de código no endpoint de criação: só o efeito de `GET /api/tasks/{id}` agora resolver.

### Não inclui

- Filtro por `status`, `priority`, `overdue`, busca textual (`search`) ou parâmetro de ordenação configurável — tudo isso é o escopo pleno de [BE-22](BE-22-listagem-tarefas.md), que fica registrada como upgrade natural desta task se/quando entrar em uma etapa futura.
- Edição, conclusão/reabertura, remoção de tarefa via gRPC — fora do recorte do T2 (front só cria e lista).
- Qualquer mudança em `CreateTask` — esta task só acrescenta RPCs.
- Cache de listagem — cada chamada é uma consulta nova, mesma postura de não-cache já adotada para `ValidateUser` (BE-28) e `ValidateToken` (BE-34).

## Notas técnicas

- **Por que "recorte estrito", e não "BE-22 via gRPC".** O objetivo desta task é destravar o frontend mínimo do T2 (criar + listar) dentro do prazo apertado da nova apresentação, não portar toda a listagem avançada para gRPC. Introduzir filtro/busca/ordenação aqui multiplicaria CAs e testes sem necessidade imediata — e faria esta task carregar a mesma densidade de BE-22, que já existe e não está bloqueando nada.
- **Por que a ordenação é só "criação decrescente", e não a ordenação completa de RN-LIST-06.** A ordenação completa de BE-22 (pendente antes de concluída, depois vencimento, depois criação) existe para uma lista de trabalho com prioridades; o requisito do frontend do T2 é mais simples — mostrar o que foi criado, mais recente primeiro, para provar visualmente que o `POST` anterior persistiu. Se BE-22 entrar depois, ela **substitui** este critério de ordenação, não convive com ele.
- **Por que a mudança no `.proto` é "compatível" e não uma nova versão.** RPCs e mensagens novos, sem alterar campo existente — um cliente gerado contra o `.proto` antigo continua compilando e funcionando para `CreateTask`; só ganha acesso a `ListTasks`/`GetTask` quem regenerar o código. Não há necessidade de `v2`.
- **Por que `GetTask` reaproveita o desenho de BE-18 e não inventa um novo.** O ponto central de BE-18 — "não existe caminho de leitura sem filtro de dono" — vale tanto para REST quanto para gRPC; duplicar a regra sem duplicar o método de resolução é o tipo de divergência silenciosa que BE-18 já existe para evitar. O handler de `GetTask` **DEVE** chamar o mesmo método de resolução que qualquer handler futuro de edição/remoção via gRPC usaria.
- **Por que os limites de paginação são os mesmos de BE-22, e não um valor novo.** Introduzir uma segunda constante de paginação (uma para REST direto, outra para gRPC) é o mesmo risco que D-09 já resolveu centralizando em `Paging:DefaultPageSize`/`Paging:MaxPageSize` — como BE-22 ainda não foi implementada, **esta task cria** `PagingOptions` (seção `Paging`, com `ValidateOnStart`) no Tasks, no formato que BE-22 vai reaproveitar. O Gateway não pode referenciar o Tasks (D-33), então tem a sua própria seção `Paging` com os mesmos valores — a duplicação é deliberada e a do Tasks é a fonte de verdade (segunda linha de defesa).

## Critérios de aceite

### Contrato

- [x] **CA-01** — `tasks.proto` compila nos dois lados (`Server` no Tasks, `Client` no Gateway) sem alterar a forma de `CreateTaskRequest`/`TaskReply` já existentes.

### Tasks — `ListTasks`

- [x] **CA-02** — `ListTasks` retorna **somente** tarefas do `owner_id` presente em `x-user-id` — verificado com dois donos populados.
- [x] **CA-03** — Tarefas removidas (soft delete) não aparecem em nenhum cenário.
- [x] **CA-04** — Sem tarefas, retorna lista vazia com `total_count=0` — nunca erro.
- [ ] **CA-05** — Os itens vêm ordenados por criação **decrescente** — mais recente primeiro.
- [x] **CA-06** — Sem `page`/`page_size` (ou com `0`), aplica o padrão (`page=1`, `page_size=Paging:DefaultPageSize`).
- [x] **CA-07** — `page_size` acima de `Paging:MaxPageSize`, ou `page`/`page_size` negativos, retornam erro de validação (`InvalidArgument`), não são silenciosamente truncados no Tasks.
- [x] **CA-08** — `total_count` reflete o total de tarefas do dono (não removidas), não o total da página.
- [x] **CA-09** — Percorrer todas as páginas devolve cada tarefa exatamente uma vez, sem repetição nem omissão (mesmo teste de BE-22 CA-27, adaptado ao gRPC).
- [x] **CA-10** — `is_overdue` de cada item usa `x-client-date`, não a data UTC do servidor — mesmo cenário de fuso de BE-22 CA-33b.
- [x] **CA-11** — A consulta é executada no banco (`WHERE`, `ORDER BY`, `LIMIT`/`OFFSET`) — verificado por captura de SQL em teste, não avaliação em memória.

### Tasks — `GetTask`

- [x] **CA-12** — `GetTask` de uma tarefa própria retorna o `TaskReply` completo, com `is_overdue` calculado.
- [x] **CA-13** — `GetTask` de id inexistente retorna `NotFound`.
- [x] **CA-14** — `GetTask` de tarefa de **outro** dono retorna `NotFound` — nunca outro status, nunca a tarefa.
- [x] **CA-15** — As respostas de CA-13 e CA-14 são indistinguíveis no `RpcException` observado pelo chamador (mesmo `StatusCode`, sem detalhe que vaze qual dos dois casos ocorreu) — RN-AUTZ-03 aplicada ao gRPC.
- [x] **CA-16** — `GetTask` de tarefa própria **removida** retorna o mesmo `NotFound` de CA-13/CA-14.

### Gateway

- [x] **CA-17** — `GET /api/tasks?page=1&pageSize=20` com token válido retorna **200** com `{items, page, pageSize, totalCount}`.
- [x] **CA-18** — `GET /api/tasks` sem token retorna **401**.
- [x] **CA-19** — `GET /api/tasks?page=0` ou `pageSize` fora de 1–100 retorna **400** na borda, **sem** chamar `ListTasks` no Tasks (verificado no fake).
- [x] **CA-20** — `GET /api/tasks/{id}` com um `id` de tarefa própria retorna **200** com o `TaskHttpResponse`.
- [x] **CA-21** — `GET /api/tasks/{id}` de tarefa alheia ou inexistente retorna **404**, corpos idênticos entre os dois casos.
- [x] **CA-22** — `GET /api/tasks/abc` (id não é GUID) retorna **400** sem round-trip gRPC.
- [x] **CA-23** — O `Location` devolvido por `POST /api/tasks` (201) resolve com sucesso num `GET` subsequente ao mesmo caminho, com o **mesmo** token — verificação de ponta a ponta de que a rota passou a existir.
- [x] **CA-24** — O tipo gerado do `.proto` (`ListTasksReply`, `TaskReply`) nunca é serializado direto na resposta HTTP — mesma regra de BE-36 CA-04.

## Testes obrigatórios

- Tasks: integração com **Testcontainers** (Postgres real) — CA-02 a CA-16, mesmo padrão de infraestrutura de teste já usado por BE-17/BE-18/BE-22.
- Gateway: integração com os **fakes gRPC existentes** (mesmos fakes de `TodoList.Gateway.IntegrationTests` usados por BE-36) — CA-17 a CA-24.
- Unitários: validadores de `page`/`pageSize` na borda do Gateway, e a tradução `ListTasksReply` ↔ `{items, page, pageSize, totalCount}` / `TaskReply` ↔ `TaskHttpResponse` (reaproveitando a tradução já existente de `CreateTask`, sem duplicar).

## Decisões em aberto

- **D-09** — Tamanho de página (20/100), reaproveitado sem alteração. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
- **D-18** — "Atrasada" usa a data local do usuário via `x-client-date`, reaproveitado sem alteração. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
- **D-34** — Identidade via metadata `x-user-id`/`x-client-date`, reaproveitada sem alteração. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).

## Auditoria dos critérios (03/10/2026)

Critérios conferidos contra o código em 03/10/2026. Marcados: 23 de 24.

| CA | Situação | Evidência / motivo |
|---|---|---|
| CA-05 | em aberto (superado por BE-22) | A ordenação por criação decrescente era provisória e foi substituída pelo critério fixo de RN-LIST-06 (`tasks.proto`, comentário de `ListTasks`). `ListTasksGrpcTests.ListTasks_ComVariasTarefas_OrdenaPorCriacaoCrescenteQuandoEmpatadas` afirma o desempate por criação CRESCENTE, o oposto do que o CA exige. |

Notas: CA-11 é coberto por `Tasks.IntegrationTests/Persistence/ListTasksPostgresQueryTests` (SQL capturado contra Postgres real, categoria Docker). CA-19 e CA-22 afirmam "sem chamar o Tasks" no fake do Gateway.
