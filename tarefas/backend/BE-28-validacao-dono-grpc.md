# BE-28 — Validação do dono na criação de tarefa (via gRPC)

| | |
|---|---|
| **Domínio** | Tarefas / Autorização |
| **Serviço** | Tasks (Microsserviço A) |
| **Depende de** | [BE-17](BE-17-criar-tarefa.md), [BE-27](BE-27-tasks-cliente-grpc.md) |
| **Bloqueia** | [BE-31](BE-31-verificacao-t1.md) |
| **Regras cobertas** | RN-AUTZ-01, RN-USER-04, RN-TASK-10, RN-TASK-07, RN-TASK-15 |
| **Estimativa** | M |

## Objetivo

Nenhuma tarefa é persistida sem que o Identity Service confirme, por gRPC, que o dono existe e está ativo. A rejeição vem da **resposta do outro serviço**, não de uma checagem local.

## Escopo

### Inclui

- `CreateTaskHandler` ([BE-17](BE-17-criar-tarefa.md)) chamando `IIdentityGateway.ValidateUserAsync(ownerId, ct)` **antes** de contar o limite de tarefas ativas e **antes** de persistir.
- Mapeamento da resposta para o desfecho:

  | Resposta do Identity | Desfecho | Código de erro | HTTP |
  |---|---|---|---|
  | `Exists=true`, `Active=true` | segue o fluxo de criação | — | 201 |
  | `Exists=false` | rejeita (RN-AUTZ-01) | `task.owner_not_found` | **404** |
  | `Exists=true`, `Active=false` | rejeita (RN-USER-04) | `task.owner_inactive` | **409** |
  | falha de transporte / deadline | rejeita, **fail-closed** (**D-28**) | `identity.unavailable` | **503** |

- Entradas novas no catálogo `TaskErrors` da `Application` do Tasks para os três códigos acima ([BE-03](BE-03-result-erros-validacao.md)) — sem strings soltas no handler.
- O `ErrorType` correspondente para cada caso, de forma que o mapeamento para HTTP saia do mecanismo de [BE-03](BE-03-result-erros-validacao.md) sem `switch` no endpoint. `503` exige acrescentar `Unavailable` ao enum `ErrorType` e à tabela de mapeamento daquela task.
- Log de nível `Warning` na rejeição por dono inválido, e `Error` na indisponibilidade do Identity — com `userId` e o motivo, sem dado sensível.

### Não inclui

- Validação de dono em edição, conclusão, remoção ou listagem. Nessas operações a tarefa **já existe** e o dono já foi validado na criação; a autorização ali é por propriedade do recurso ([BE-18](BE-18-consultar-tarefa-autorizacao.md)), local e sem chamada de rede.
- Retentativa ou circuit breaker sobre a chamada — fora do escopo desta etapa.
- Cache do resultado de `ValidateUser`. Cachear "usuário ativo" reintroduz exatamente o problema que a chamada resolve: o Tasks passaria a decidir com informação velha sobre um usuário que o Identity já desativou.

## Notas técnicas

- **Por que fail-closed (D-28), mesmo com a FK.** Se o Identity estiver inalcançável, o Tasks **não** cria a tarefa. A FK (**D-27**) já impediria uma tarefa de dono inexistente, mas ela nada sabe sobre o **estado** do usuário: criar assumindo "ativo" grava uma tarefa que a RN-USER-04 proibia, para um usuário que o Identity já desativou. Uma rejeição temporária com 503 é reversível pelo cliente; o registro indevido não é.
- **A FK nunca deve ser o que barra uma criação no caminho normal.** Se uma violação `23503` chegar do banco, é sintoma de que a validação gRPC foi pulada — por isso o CA-15.
- **Por que 404 e não 400 para dono inexistente.** O dono não vem do corpo do request: ele é a identidade de quem chamou (`ICurrentUser.Id`). Um id de usuário que não existe no Identity não é payload malformado — é um recurso ausente. `400` sugeriria ao cliente que ele pode corrigir o corpo, e não pode.
- **Por que 409 e não 403 para dono inativo.** O pedido é legítimo e a identidade é válida; o que impede é o **estado** do usuário (RN-USER-04). `409` comunica conflito com o estado atual, que é o que de fato ocorre.
- **A rejeição precisa vir do gRPC, não de uma checagem local.** Este é o ponto central da task: se o Tasks pudesse decidir sozinho, não haveria comunicação entre microsserviços para demonstrar. Por isso CA-05 exige que a rejeição desapareça quando o Identity muda de resposta — a decisão é dele, não do Tasks.
- A ordem (dono → limite → persistência) evita contar tarefas de um usuário inexistente e mantém a chamada de rede única por criação.
- O `503` **DEVE** trazer o cabeçalho `Retry-After`, já que o desfecho é explicitamente temporário.

## Critérios de aceite

### Caminho de sucesso

- [ ] **CA-01** — Usuário existente e ativo: a tarefa é criada e a resposta é **201** com o DTO completo ([BE-17](BE-17-criar-tarefa.md), CA-01).
- [ ] **CA-02** — `ValidateUserAsync` é chamado **uma única vez** por criação (não uma vez por validação de campo, não duas por engano).
- [ ] **CA-03** — A chamada acontece **antes** da contagem do limite e **antes** do `SaveChanges` — verificado por ordem de invocação no teste de unidade.

### Rejeição

- [ ] **CA-04** — Usuário inexistente no Identity (`exists=false`): resposta **404** com `task.owner_not_found`, e **nenhuma linha** é gravada em `tasks` (verificado no banco).
- [ ] **CA-05** — A rejeição do CA-04 desaparece quando o mesmo usuário passa a existir no Identity, **sem mudança no Tasks** — prova de que a decisão vem da resposta gRPC e não de uma validação local.
- [ ] **CA-06** — Usuário existente porém inativo (`active=false`): resposta **409** com `task.owner_inactive`, sem gravação (RN-USER-04).
- [ ] **CA-07** — As duas rejeições geram log `Warning` com `userId` e motivo.

### Indisponibilidade

- [ ] **CA-08** — Com o Identity **desligado**, `POST /tasks` responde **503** com `identity.unavailable` e cabeçalho `Retry-After` — nunca 500, nunca 201 (**D-28**).
- [ ] **CA-09** — Nesse cenário **nenhuma** tarefa é persistida (verificado no banco).
- [ ] **CA-10** — A resposta 503 não vaza detalhe de transporte (endereço do Identity, `StatusCode` gRPC, mensagem da `RpcException`).
- [ ] **CA-11** — A requisição falha dentro do deadline configurado, não após espera indefinida ([BE-27](BE-27-tasks-cliente-grpc.md), CA-09).

### Não regressão

- [ ] **CA-12** — Todos os critérios de [BE-17](BE-17-criar-tarefa.md) continuam válidos: validação de campos ainda retorna **400** **antes** de qualquer chamada gRPC (não se gasta uma chamada de rede com request inválido).
- [ ] **CA-13** — O limite de 500 tarefas ativas (RN-TASK-15) continua sendo aplicado e continua retornando **409** com `task.active_limit_reached` — código distinto do `task.owner_inactive`.
- [ ] **CA-14** — A tarefa criada continua nascendo `Pending` com `OwnerId` igual ao usuário corrente (RN-TASK-07, RN-AUTZ-01).
- [ ] **CA-15** — Nenhum cenário de teste produz violação de FK (`23503`) vinda do banco: a rejeição por dono inexistente sempre acontece na validação gRPC, **antes** do `SaveChanges` ([BE-02](BE-02-persistencia-base.md), CA-15).

## Testes obrigatórios

- Unidade: `CreateTaskHandler` com `IIdentityGateway` substituído, cobrindo as quatro linhas da tabela de desfechos — CA-02 a CA-04, CA-06, CA-12, CA-13.
- Integração: Tasks + Identity reais, com o Identity subido em `WebApplicationFactory` — CA-01, CA-04, CA-05, CA-09.
- Integração: Tasks com o Identity **apontando para um endereço morto** — CA-08, CA-10, CA-11. Este teste é obrigatório: é o único que exercita o comportamento em falha, e ele não pode ser demonstrado ao vivo sem risco.

## Decisões em aberto

- **D-28** — Fail-closed com 503 na indisponibilidade do Identity. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
- **D-27** — Banco único com schema por serviço e FK cruzada. A FK cobre a existência do dono; esta validação cobre o **estado** dele e produz a resposta de negócio.
