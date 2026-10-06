# ADR-0008 — `PUT /api/tasks/{id}` substitui; não faz merge

- **Status:** aceita — task BE-19 (nota técnica), RN-TASK-11
- **Contexto de regra:** RN-TASK-02 a RN-TASK-05, RN-TASK-11, RN-TASK-14

## Contexto

Editar uma tarefa muda título, descrição, prioridade e vencimento. Um `PUT` pode ser lido como "substitui o
recurso" ou como "atualiza o que veio". Distinguir "campo ausente" de "campo `null`" no JSON (e no proto3,
com `optional`) complica o cliente e o servidor.

## Decisão

`PUT` é **substituição completa dos quatro campos editáveis**:

| Campo | Se omitido ou `null` |
|---|---|
| `title` | **400** (obrigatório, 1–200 caracteres) |
| `description` | vira `null` (limpa) |
| `priority` | volta ao padrão `Medium` |
| `dueDate` | vira `null` (limpa) |

- As regras de validação são **as mesmas da criação** (validador compartilhado).
- `status`, `id`, `ownerId`, `createdAt` e `completedAt` enviados no corpo são **ignorados**: concluir e reabrir
  têm endpoints próprios (RN-TASK-08/09). Editar tarefa concluída é permitido e não a reabre.
- Tarefa inexistente, removida ou de outro usuário é o mesmo **404** (RN-AUTZ-03).
- O contrato está escrito no OpenAPI (`description` da operação) e no `tasks.proto` (`UpdateTaskRequest`).

## Alternativas consideradas

1. **`PATCH` com merge.** Exige separar "ausente" de "`null`" no JSON; complexidade sem necessidade nesta versão.
2. **`PUT` que mantém o valor dos campos omitidos.** Surpreende quem espera substituição e esconde perda de dado.

## Consequências

- Previsível e testável; a maior fonte de confusão é um cliente que envia só `{"title": "X"}` e perde a
  descrição e o vencimento — por isso o aviso em destaque no OpenAPI.
- Se surgir necessidade de edição parcial, é um `PATCH` novo, sem mudar este `PUT`.
