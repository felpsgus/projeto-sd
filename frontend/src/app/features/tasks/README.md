# `features/tasks/` — tarefas

Implementa FE-14 a FE-20: carregar, listar paginada e filtrada, criar, editar,
concluir/reabrir e remover tarefas. FE-16 (filtros, busca e sincronia com URL) entrou em
24/09/2026, na Fase 2 (ver `tarefas/frontend/FE-16-filtros-busca-url.md`).

## Estrutura

- `tasks.store.ts` (FE-14/FE-16) — única camada de estado, com signals: `items`, `page`,
  `pageSize`, `totalCount`, `totalPages` (`computed` no cliente), `status`
  (`idle | loading | success | error`), `error`, `filters`, `isEmpty`, `isFilteredEmpty`.
  Operações: `load` (paginado e filtrado), `create`, `getById`, `update`, `complete`,
  `reopen`, `remove`. Reage a `SessionStore.isAuthenticated()` voltando a `false` com um
  `effect()` que chama `clear()`, para nenhum dado de tarefa sobreviver à troca de usuário
  na mesma aba. **Como cada mutação reflete na lista** está documentado no comentário de
  classe do store — resumo: `update` só troca o item em memória (a substituição de `PUT`
  não muda a posição do item); `complete`/`reopen` trocam o item **e** recarregam a página
  atual em silêncio (sem "piscar" a lista inteira), porque RN-LIST-06 (BE-22) muda a posição
  do item ao mudar de estado.
- `tasks-page/tasks-query.util.ts` (FE-16) — conversão pura entre `ActivatedRoute.queryParamMap`
  e `{ page, filters }`: satura/ignora parâmetro inválido (`?status=xyz`, `?page=abc`) em vez
  de propagar erro, e nunca inclui na URL um filtro no valor "ausente".
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
  `itemErrors`) para as ações de FE-19/FE-20 não afetarem o resto da lista. Barra de
  filtros (estado, prioridade múltipla, atrasadas, busca) acima da lista — a URL é a única
  fonte de verdade (FE-16): o componente não guarda filtro em signal próprio, só lê
  `ActivatedRoute.queryParamMap` saneado por `tasks-query.util.ts` e chama `TasksStore.load`
  num `effect()`. Alterar um filtro navega com `replaceUrl: true` (volta à página 1); mudar
  de página navega normalmente; a busca usa um signal local com debounce de ~300 ms antes de
  virar navegação.
- `create-task/` — container da rota `/tasks/new`: delega a `<app-task-form>`, chama
  `TasksStore.create` e volta a `/tasks` em sucesso.
- `edit-task/` — container da rota `/tasks/:id/edit` (FE-18): carrega a tarefa por
  `getById`, delega a `<app-task-form>` pré-preenchido e chama `TasksStore.update`. Um 404
  (id inexistente, alheio, removido ou mal formado na URL) sempre cai na mesma tela "Tarefa
  não encontrada" (RN-AUTZ-03) — nunca uma mensagem distinta. `unsaved-changes.guard.ts`
  implementa o `canDeactivate` de saída com alterações não salvas (CA-23).
- `task-date.util.ts` / `task-labels.ts` — utilitários puros de formatação/rótulo,
  compartilhados entre os componentes acima.

## Fora do escopo

- Ordenação escolhida pelo usuário — RN-LIST-06 define a única ordenação; o cliente nunca
  reordena o que o servidor devolve, com ou sem filtro.
- Filtros salvos, visões nomeadas, favoritos, busca com destaque do termo (FE-16, "não
  inclui").
- Limite de tarefas ativas na tela de criação (409 `task.active_limit_reached`) — o código
  de erro já existe no catálogo do cliente (`core/errors`) porque `reopen` também pode
  recebê-lo (decisão de 23/09/2026), mas a UI de "você atingiu o limite" na criação em si
  não foi revisitada nesta onda.
