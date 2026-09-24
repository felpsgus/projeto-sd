# `features/tasks/` — tarefas

Implementa FE-14, FE-15, FE-17, FE-18, FE-19 e FE-20: carregar, listar paginado, criar,
editar, concluir/reabrir e remover tarefas (terceira onda da Fase 1, 23/09/2026 — ver
`tarefas/PLANO-REGRAS-RESTANTES.md`).

## Estrutura

- `tasks.store.ts` (FE-14) — única camada de estado, com signals: `items`, `page`,
  `pageSize`, `totalCount`, `totalPages` (`computed` no cliente), `status`
  (`idle | loading | success | error`), `error`, `isEmpty`. Operações: `load` (paginado,
  sem filtros — FE-16 fica para a Fase 2), `create`, `getById`, `update`, `complete`,
  `reopen`, `remove`. Reage a `SessionStore.isAuthenticated()` voltando a `false` com um
  `effect()` que chama `clear()`, para nenhum dado de tarefa sobreviver à troca de usuário
  na mesma aba. **Como cada mutação reflete na lista** está documentado no comentário de
  classe do store — resumo: nunca recarrega a página inteira, só troca/remove o item
  afetado, para não "piscar" a lista nem descartar o estado dos outros itens.
- `task-form/` — `<app-task-form>`, formulário reativo **compartilhado por criar e editar**
  (FE-17/FE-18, CA-15 de FE-18: mesma validação nos dois casos, sem duplicação): título
  obrigatório (1–200, não só espaços), descrição opcional (≤2000), prioridade obrigatória,
  vencimento opcional (aceita datas passadas, RN-TASK-05). Recebe `initialValue` (`null`
  para criar) e emite `save`/`cancel`; o container trata erro de servidor via
  `submitFailed(error)`.
- `task-item/` — `<app-task-item>`, item de apresentação (recebe `task`, `pending`,
  `actionError` por `input()`; emite `complete`/`reopen`/`remove`), mostra título,
  descrição, prioridade, vencimento, selo "Atrasada" (`isOverdue` vindo pronto da API),
  data de conclusão (quando concluída) e data de atualização. Ações na própria linha:
  caixa de marcação para concluir/reabrir, link para editar, botão remover atrás de
  `<app-confirm-dialog>`.
- `tasks-page/` — container da rota `/tasks`: consome `TasksStore`, trata os três estados
  globais (carregando/vazio/erro) e mantém estado **por item** (`pendingIds`,
  `itemErrors`) para as ações de FE-19/FE-20 não afetarem o resto da lista.
- `create-task/` — container da rota `/tasks/new`: delega a `<app-task-form>`, chama
  `TasksStore.create` e volta a `/tasks` em sucesso.
- `edit-task/` — container da rota `/tasks/:id/edit` (FE-18): carrega a tarefa por
  `getById`, delega a `<app-task-form>` pré-preenchido e chama `TasksStore.update`. Um 404
  (id inexistente, alheio, removido ou mal formado na URL) sempre cai na mesma tela "Tarefa
  não encontrada" (RN-AUTZ-03) — nunca uma mensagem distinta. `unsaved-changes.guard.ts`
  implementa o `canDeactivate` de saída com alterações não salvas (CA-23).
- `task-date.util.ts` / `task-labels.ts` — utilitários puros de formatação/rótulo,
  compartilhados entre os componentes acima.

## Fora do escopo (próxima fase)

- Filtros, busca e ordenação no cliente (FE-16) — a ordem exibida continua sendo sempre a
  do servidor (hoje, só por `createdAt` decrescente).
- Limite de tarefas ativas na tela de criação (409 `task.active_limit_reached`) — o código
  de erro já existe no catálogo do cliente (`core/errors`) porque `reopen` também pode
  recebê-lo (decisão de 23/09/2026), mas a UI de "você atingiu o limite" na criação em si
  não foi revisitada nesta onda.
