# `features/tasks/` — tarefas (recorte do T2)

Esta pasta implementa o recorte do T2 de FE-14 (parcial), FE-15 e FE-17: carregar, listar
paginado e criar tarefas. Sem editar, concluir, reabrir ou remover — essas rotas não
existem no Gateway do T2 (ver `tarefas/frontend/README.md`, seção "Recorte do T2").

## Estrutura

- `tasks.store.ts` (FE-14, parcial) — única camada de estado, com signals: `items`, `page`,
  `pageSize`, `totalCount`, `totalPages` (`computed` no cliente), `status`
  (`idle | loading | success | error`), `error`, `isEmpty`. Só três operações: `load`
  (paginado, sem filtros), `create` e `getById`. Reage a `SessionStore.isAuthenticated()`
  voltando a `false` com um `effect()` que chama `clear()`, para nenhum dado de tarefa
  sobreviver à troca de usuário na mesma aba.
- `task-item/` — `<app-task-item>`, componente de apresentação puro (recebe `task` por
  `input()`), mostra título, descrição, prioridade (rótulo + cor), vencimento (formatado
  por string, nunca por `new Date()`), selo "Atrasada" (`isOverdue` vindo pronto da API) e
  data de atualização.
- `tasks-page/` — container da rota `/tasks`: consome `TasksStore`, trata os três estados
  (carregando/vazio/erro) com os componentes de `shared/ui/`, lista os itens e oferece
  paginação simples (anterior/próxima + "página X de Y").
- `create-task/` — container da rota `/tasks/new`: formulário reativo (título obrigatório,
  descrição/prioridade/vencimento opcionais), validação no cliente espelhando o backend,
  erro 400 mapeado por campo via `AppError.fieldErrors` (chaves camelCase), sucesso (201)
  volta para `/tasks`, que recarrega a lista do servidor.
- `task-date.util.ts` / `task-labels.ts` — utilitários puros de formatação/rótulo,
  compartilhados entre os componentes acima.

## Fora do recorte do T2 (próxima onda)

- Filtros, busca e ordenação no cliente (FE-16) — a ordem exibida é sempre a do servidor.
- Editar (FE-18), concluir/reabrir (FE-19), remover (FE-20) tarefas.
- Limite de tarefas ativas (409 `task.active_limit_reached`) e `canDeactivate` no
  formulário de criação — fora do briefing desta onda; revisar se o backend do T2 vier a
  implementar o limite.
