# BE-17 — Criar tarefa (com limite de tarefas ativas)

| | |
|---|---|
| **Domínio** | Tarefas |
| **Serviço** | Tasks |
| **Depende de** | [BE-05](BE-05-dominio-tarefa.md), [BE-13](BE-13-protecao-endpoints.md) |
| **Bloqueia** | BE-18 a BE-22, [BE-28](BE-28-validacao-dono-grpc.md) |
| **Regras cobertas** | RN-TASK-10, RN-TASK-02 a RN-TASK-07, RN-TASK-15, RN-AUTZ-01, RN-AUTZ-04 |
| **Estimativa** | M |

## Objetivo

O usuário autenticado cria uma tarefa informando ao menos o título, e não passa do limite de 500 tarefas ativas.

## Escopo

### Inclui

- **`POST /api/tasks`** (autenticado).
- Request:

  ```json
  {
    "title": "string",
    "description": "string|null",
    "priority": "Low|Medium|High|null",
    "dueDate": "yyyy-MM-dd|null"
  }
  ```

- Caso de uso `CreateTaskHandler`, com esta ordem de passos:
  1. valida o request (FluentValidation, espelhando as regras do domínio);
  2. **valida o dono no Identity Service** via `IIdentityGateway.ValidateUserAsync(ownerId)` — chamada gRPC, especificada em [BE-28](BE-28-validacao-dono-grpc.md) (RN-AUTZ-01, RN-USER-04);
  3. verifica o limite de tarefas ativas do usuário (RN-TASK-15) → excedido → **409** com `task.active_limit_reached` e mensagem clara com o valor do limite;
  4. cria via `TodoTask.Create` com `OwnerId = ICurrentUser.Id` (RN-AUTZ-01);
  5. persiste.

  A validação de dono vem **antes** da contagem do limite: não faz sentido contar tarefas de um usuário que não existe, e a chamada de rede acontece uma vez só.
- Resposta **201 Created** com `Location: /api/tasks/{id}` e o DTO completo da tarefa:

  ```json
  {
    "id": "guid", "title": "string", "description": "string|null",
    "status": "Pending", "priority": "Medium", "dueDate": "yyyy-MM-dd|null",
    "completedAt": null, "isOverdue": false,
    "createdAt": "iso-8601", "updatedAt": "iso-8601"
  }
  ```

- `TaskOptions` tipado: `Tasks:MaxActivePerUser` = **500** (D-08); `null` desativa o limite.
- DTO de resposta compartilhado com BE-18/BE-19/BE-20/BE-22 — um único mapeamento de `TodoTask` para `TaskResponse`, sem duplicação. Esse mapeamento calcula `isOverdue` usando `IClientDate.Today` ([BE-13](BE-13-protecao-endpoints.md), decisão **D-18**), então **todos** os endpoints que devolvem uma tarefa herdam o comportamento correto de fuso sem código próprio.

### Não inclui

- Edição, conclusão, remoção e listagem — tasks próprias.
- O contrato, o cliente gRPC e o comportamento de falha da chamada ao Identity — [BE-25](BE-25-contrato-grpc-identity.md), [BE-27](BE-27-tasks-cliente-grpc.md) e [BE-28](BE-28-validacao-dono-grpc.md). Aqui só se declara **onde** a chamada entra no fluxo.

## Notas técnicas

- **Por que o dono é validado por rede, mesmo havendo FK.** A FK `tasks.owner_id → identity.users(id)` (**D-27**, [BE-02](BE-02-persistencia-base.md)) garante que o dono **existe** — e só isso. Ela não sabe se o usuário está **ativo** (RN-USER-04) e não devolve o nome de exibição. Além disso, deixar a FK barrar a criação produziria uma violação de constraint (falha técnica, 500), não uma rejeição de negócio com mensagem clara. A chamada gRPC decide antes; a FK é a rede de segurança embaixo.
- **"Tarefas ativas" (RN-TASK-15) = não concluídas E não removidas.** Uma tarefa concluída **não** conta para o limite; uma soft-deleted também não. Errar essa definição é o defeito mais provável — por isso CA-11 e CA-12.
- Enums na API trafegam como **string** (`"High"`), não número: o contrato fica legível e resistente a reordenação do enum. Configurar `JsonStringEnumConverter`.
- `dueDate` é data pura (`yyyy-MM-dd`), sem hora nem fuso.
- Validação em duas camadas é intencional: FluentValidation dá a mensagem por campo (400); o domínio garante a invariante mesmo se alguém chamar o handler direto. Não é duplicação ociosa.
- Concorrência no limite: duas criações simultâneas na fronteira dos 500 podem resultar em 501. Aceitável nesta versão (o limite é uma proteção, não uma regra contábil) — **documentar no PR**; se for inaceitável, exige contador transacional.

## Critérios de aceite

### Criação

- [ ] **CA-01** — `POST /api/tasks` apenas com `title` retorna **201** (RN-TASK-10).
- [ ] **CA-02** — A resposta traz o cabeçalho `Location` apontando para o recurso criado, e o `GET` naquele endereço retorna a tarefa.
- [ ] **CA-03** — A tarefa criada tem `status == "Pending"` e `completedAt == null` (RN-TASK-07).
- [ ] **CA-04** — Sem `priority`, a tarefa nasce com `"Medium"` (RN-TASK-04).
- [ ] **CA-05** — Com `priority: "High"`, o valor é preservado; `priority: "Urgente"` retorna **400**.
- [ ] **CA-06** — A tarefa criada tem `OwnerId` igual ao usuário do token — verificado no banco (RN-AUTZ-01).
- [ ] **CA-07** — `createdAt` e `updatedAt` vêm preenchidos e iguais na criação.

### Validação

- [ ] **CA-08** — Título ausente, vazio, `"   "` ou com 201 caracteres retorna **400** apontando o campo `title` (RN-TASK-02).
- [ ] **CA-09** — Título com 1 e com 200 caracteres é aceito (bordas).
- [ ] **CA-10** — Descrição com 2001 caracteres retorna **400**; com 2000 é aceita (RN-TASK-03).
- [ ] **CA-11** — `dueDate` no passado é **aceita** (201) e a resposta traz `isOverdue: true` (RN-TASK-05, RN-TASK-16).
- [ ] **CA-12** — `dueDate` igual a hoje é aceita com `isOverdue: false`.
- [ ] **CA-12b** — Com o relógio UTC em `2026-08-21T00:30` e header `X-Client-Date: 2026-08-20` (usuário em UTC−3, ainda dia 20 para ele), uma tarefa com `dueDate: 2026-08-20` retorna **`isOverdue: false`** — o cenário exato que motivou a decisão **D-18**.
- [ ] **CA-13** — `dueDate` em formato inválido (`"31/12/2026"`, `"2026-13-01"`) retorna **400**, não 500.

### Limite

- [ ] **CA-14** — Com 499 tarefas pendentes, a criação da 500ª sucede; a 501ª retorna **409** com `task.active_limit_reached` (RN-TASK-15).
- [ ] **CA-15** — A mensagem do 409 informa o limite de forma clara para o usuário final.
- [ ] **CA-16** — Tarefas **concluídas não contam** para o limite: com 500 concluídas, ainda é possível criar uma pendente.
- [ ] **CA-17** — Tarefas **soft-deleted não contam**: remover uma tarefa libera espaço no limite imediatamente.
- [ ] **CA-18** — O limite é **por usuário**: outro usuário com 0 tarefas cria normalmente enquanto o primeiro está no teto.
- [ ] **CA-19** — Alterar `Tasks:MaxActivePerUser` para 3 faz o bloqueio ocorrer na 4ª tarefa, sem mudança de código.
- [ ] **CA-20** — Com `Tasks:MaxActivePerUser` nulo, não há bloqueio.

### Autorização

- [ ] **CA-21** — Requisição sem token retorna **401** (RN-AUTZ-04).
- [ ] **CA-22** — Não é possível criar tarefa para outro usuário: o request **não tem** campo de dono, e enviar `ownerId` no corpo é ignorado (verificado no banco).
- [ ] **CA-23** — `CreateTaskHandler` chama `IIdentityGateway.ValidateUserAsync` **exatamente uma vez** e **antes** de contar o limite e de persistir (unidade, com o gateway substituído). Os cenários de resposta do Identity são cobertos em [BE-28](BE-28-validacao-dono-grpc.md).

## Testes obrigatórios

- Unidade: `CreateTaskHandler` com repositório e `IIdentityGateway` substituídos — CA-03, CA-04, CA-06, CA-14, CA-16 a CA-20, CA-23. Nos testes de unidade o gateway devolve `exists=true, active=true` por padrão.
- Unidade: validador do request — CA-05, CA-08 a CA-10, CA-13.
- Integração: CA-01, CA-02, CA-07, CA-11, CA-12, CA-21, CA-22.
- Os testes de limite usam `Tasks:MaxActivePerUser` reduzido — **não** criar 500 tarefas reais em teste.

## Decisões em aberto

- **D-06** — Vencimento no passado aceito.
- **D-08** — Limite de 500 tarefas ativas, configurável.
- **D-18** — ✅ decidida: `isOverdue` calculado com a data local do usuário.
