# BE-20 — Concluir e reabrir tarefa

| | |
|---|---|
| **Domínio** | Tarefas |
| **Depende de** | [BE-18](BE-18-consultar-tarefa-autorizacao.md) |
| **Bloqueia** | — |
| **Regras cobertas** | RN-TASK-08, RN-TASK-09, RN-TASK-14, RN-AUTZ-02, RN-AUTZ-03 |
| **Estimativa** | M |

## Objetivo

O usuário marca uma tarefa como concluída e pode reabri-la; a data de conclusão é registrada e limpa de acordo.

## Escopo

### Inclui

- **`POST /api/tasks/{id}/complete`** (autenticado), sem corpo → **200** com o `TaskResponse`.
- **`POST /api/tasks/{id}/reopen`** (autenticado), sem corpo → **200** com o `TaskResponse`.
- Ambos: resolvem a tarefa por `GetOwnedTaskAsync` (BE-18), delegam a transição ao domínio (`Complete` / `Reopen` de BE-05) e persistem.
- Transição inválida → **409 Conflict** com código estável (`task.already_completed`, `task.not_completed`).

### Não inclui

- Um endpoint genérico de mudança de estado (`PATCH /status`) — endpoints por ação deixam a transição explícita e o histórico de log legível.
- Conclusão em lote.

## Notas técnicas

- A regra de transição vive **no domínio** (BE-05). O handler não escreve `if (status == Completed)` — ele repassa o `Result`.
- `completedAt` vem do `TimeProvider`, em UTC.
- Uma tarefa concluída nunca é "atrasada" (RN-TASK-16): concluir uma tarefa vencida faz `isOverdue` virar `false`. Isso é consequência da definição, e CA-06 fixa o comportamento.
- **Idempotência:** concluir duas vezes retorna 409, não 200. Escolha deliberada — a regra RN-TASK-08 fala em "tarefa Pendente PODE ser marcada como Concluída", e sinalizar o conflito ajuda o frontend a detectar estado dessincronizado.

## Critérios de aceite

### Concluir

- [x] **CA-01** — `POST /complete` em tarefa `Pending` retorna **200** com `status: "Completed"`.
- [x] **CA-02** — `completedAt` vem preenchido na resposta e no banco, em UTC (RN-TASK-08).
- [x] **CA-03** — `updatedAt` muda (RN-TASK-14).
- [x] **CA-04** — `POST /complete` em tarefa **já concluída** retorna **409** com `task.already_completed`.
- [x] **CA-05** — A tentativa de CA-04 **não** altera `completedAt` nem `updatedAt` no banco.
- [x] **CA-06** — Concluir uma tarefa com vencimento passado faz `isOverdue` passar de `true` para `false` (RN-TASK-16).

### Reabrir

- [x] **CA-07** — `POST /reopen` em tarefa `Completed` retorna **200** com `status: "Pending"`.
- [x] **CA-08** — `completedAt` volta a `null` na resposta e no banco (RN-TASK-09).
- [x] **CA-09** — `updatedAt` muda.
- [x] **CA-10** — `POST /reopen` em tarefa `Pending` retorna **409** com `task.not_completed`, sem alterar nada.
- [x] **CA-11** — Reabrir uma tarefa com vencimento passado faz `isOverdue` voltar a `true`.
- [x] **CA-12** — Ciclo completo `complete → reopen → complete` funciona, e o segundo `completedAt` é **posterior** ao primeiro (relógio avançado).

### Autorização

- [x] **CA-13** — `POST /complete` e `/reopen` em tarefa de **outro usuário** retornam **404**, com corpo idêntico ao de id inexistente (RN-AUTZ-02, RN-AUTZ-03).
- [x] **CA-14** — Ambos em tarefa removida (soft delete) retornam **404**.
- [x] **CA-15** — Ambos sem token retornam **401**.
- [x] **CA-16** — O 409 de transição inválida é **distinguível** do 404 de tarefa alheia — um cliente sabe que a tarefa existe e é sua, mas está no estado errado.

## Testes obrigatórios

- Unidade: handlers de conclusão e reabertura com `TimeProvider` fake — CA-02 a CA-05, CA-08 a CA-10, CA-12.
- Integração: CA-01, CA-06, CA-07, CA-11, CA-13 a CA-16.
- Os endpoints entram no teste transversal de autorização de BE-18 (CA-11 daquela task).

## Auditoria dos critérios (03/10/2026)

Critérios conferidos contra o código em 03/10/2026. Marcados: 16 de 16.

| CA | Situação | Evidência / motivo |
|---|---|---|
| CA-13/CA-14 | atendidos (com ressalva) | Tarefa alheia, inexistente e removida passam pelo mesmo `GetOwnedTaskAsync` → `TaskErrors.NotFound`, então o corpo é o mesmo por construção. Testes: `CompleteTask_TarefaDeOutroDono`/`ReopenTask_TarefaDeOutroDono` e `CompleteTask_TarefaRemovida_RetornaNotFound` no Tasks; não há teste comparando corpos de complete/reopen nem `ReopenTask` em tarefa removida. |
| CA-01 a CA-16 | atendidos em outro lugar | Rotas `POST /api/tasks/{id}/complete` e `/reopen` no Gateway (D-32); regra em `CompleteReopenTaskGrpcTests`/handlers do Tasks, tradução HTTP (200/404/409/401) em `CompleteReopenTaskTests` do Gateway. |
