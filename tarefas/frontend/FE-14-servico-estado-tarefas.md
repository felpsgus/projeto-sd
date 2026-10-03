# FE-14 — Serviço e estado de tarefas

| | |
|---|---|
| **Domínio** | Tarefas |
| **Depende de** | [FE-07](FE-07-roteamento-guards.md) · backend: [BE-17](../backend/BE-17-criar-tarefa.md) a [BE-22](../backend/BE-22-listagem-tarefas.md) |
| **Bloqueia** | FE-15 a FE-20 |
| **Regras cobertas** | RN-AUTZ-02, RN-AUTZ-03 (tratamento no cliente) |
| **Estimativa** | M |

> **Recorte do T2 (21/09/2026):** entra **parcial** — a camada única de estado (`TasksStore`) é usada por [FE-15](FE-15-listagem-paginacao.md) (listar) e [FE-17](FE-17-criar-tarefa.md) (criar). Entram só `load` (paginação, **sem** filtros de `query`), `create` e `getById`, coerentes com o recorte de FE-02. `update`, `complete`, `reopen` e `remove` **não** são implementados no T2: chamariam rotas que o Gateway não tem (revisão do tech lead — código morto apontando para endpoint inexistente); entram junto com FE-18 a FE-20. O backend devolve `totalCount`; `totalPages` é `computed` no cliente. Depende de [BE-36](../backend/BE-36-api-gateway.md) (criar) e de [BE-41](../backend/BE-41-listar-e-consultar-tarefas-grpc.md) (listar/obter).

## Objetivo

Existe uma camada única de estado para tarefas, exposta por signals, que todas as telas de tarefa consomem — e nenhuma delas chama a API diretamente nem mantém cópia própria da lista.

## Escopo

### Inclui

- `TasksStore` em `features/tasks/data/`, serviço com signals (FD-05):

  | Membro | Tipo |
  |---|---|
  | `items` | `Signal<TaskResponse[]>` |
  | `pagination` | `Signal<{ page, pageSize, totalItems, totalPages }>` |
  | `query` | `Signal<TaskListQuery>` — filtros correntes |
  | `status` | `Signal<'idle' \| 'loading' \| 'success' \| 'error'>` |
  | `error` | `Signal<AppError \| null>` |
  | `isEmpty` | `computed` — sucesso com zero itens |
  | `isFilteredEmpty` | `computed` — vazio **com** filtro ativo (mensagem diferente) |

- Ações: `load(query)`, `reload()`, `create(...)`, `update(id, ...)`, `complete(id)`, `reopen(id)`, `remove(id)`, `getById(id)`.
- **Regra de sincronização após mutação**: toda mutação bem-sucedida atualiza o item na lista em memória **e** mantém a paginação coerente. Recarregar a página inteira após cada ação é aceitável apenas quando a mutação pode mudar a ordenação ou a filtragem — ver notas.
- Tratamento uniforme de **404** (RN-AUTZ-03): tarefa inexistente, de outro usuário ou removida produzem o mesmo estado `not_found` e a mesma mensagem "Tarefa não encontrada". A camada **não** distingue os casos, porque o backend não distingue.
- `clear()` chamado por `SessionStore.endSession` ([FE-05](FE-05-estado-sessao.md)) — nenhum dado de tarefa sobrevive à troca de usuário na aba.
- Contagem de tarefas ativas derivada, usada por [FE-13](FE-13-exclusao-conta.md) e [FE-17](FE-17-criar-tarefa.md).

### Não inclui

- Qualquer componente de tela.
- Cache offline, persistência local, sincronização em background.

## Notas técnicas

- **Quando recarregar e quando atualizar em memória** é a decisão de projeto desta task, e errar gera bugs sutis:
  - **concluir/reabrir** ([FE-19](FE-19-concluir-reabrir.md)) muda o estado, e a ordenação padrão põe pendentes antes de concluídas (RN-LIST-06) — logo, a posição do item muda. Com filtro `status` ativo, o item pode até **sair** da lista. Atualizar só o item em memória deixaria a lista em ordem errada;
  - **editar** pode mudar o vencimento e, com isso, a posição e a condição de atrasada;
  - **remover** tira o item e altera `totalItems`, podendo esvaziar a página atual.
  Regra adotada: **atualizar o item em memória para o retorno imediato, e recarregar a página corrente em seguida** — o usuário vê a mudança na hora e a lista converge para o estado correto do servidor. As tasks de mutação detalham o comportamento.
- **RN-AUTZ-03 depende desta camada não ser "prestativa":** se o store tentar deduzir "essa tarefa é de outro usuário" a partir de um 404, e a tela exibir isso, o cliente vaza o que o backend protegeu. Um 404 é um 404.
- Sem `effect()` para disparar carregamento — a rota e os parâmetros comandam a chamada ([FE-16](FE-16-filtros-busca-url.md)).
- Nenhum componente injeta `TasksApi` diretamente; sempre o `TasksStore`.

## Critérios de aceite

- [x] **CA-01** — `load(query)` popula `items` e `pagination` a partir da resposta paginada da API.
- [x] **CA-02** — Durante a carga, `status` é `'loading'`; ao concluir, `'success'`; em falha, `'error'` com `error` preenchido.
- [x] **CA-03** — `isEmpty` é `true` apenas em sucesso com zero itens — nunca durante o carregamento.
- [x] **CA-04** — `isFilteredEmpty` distingue "você ainda não tem tarefas" de "nenhuma tarefa corresponde ao filtro".
- [x] **CA-05** — Uma resposta **404** em `getById`, `update`, `complete`, `reopen` ou `remove` produz sempre o mesmo estado `not_found` e a mesma mensagem (RN-AUTZ-03).
- [x] **CA-06** — A camada **não** expõe nenhuma informação que permita distinguir tarefa inexistente de tarefa alheia.
- [x] **CA-07** — Toda mutação bem-sucedida deixa `items` consistente com o servidor após a reconciliação (verificado comparando com um segundo `load`).
- [x] **CA-08** — Duas chamadas de `load` em sequência rápida não deixam a lista com o resultado da **primeira** (proteção contra resposta fora de ordem).
- [x] **CA-09** — `clear()` esvazia `items`, `pagination`, `query` e `error`.
- [x] **CA-10** — `endSession` dispara `clear()`: após trocar de usuário na mesma aba, nenhum dado do anterior aparece.
- [x] **CA-11** — Nenhum componente injeta `TasksApi` diretamente (verificado por busca no código e lint).
- [ ] **CA-12** — Nenhum `effect()` é usado para disparar requisição.
- [x] **CA-13** — Um erro em uma mutação **não** corrompe a lista: `items` permanece no último estado válido conhecido.
- [x] **CA-14** — `TaskResponse` é consumido como veio da API; `isOverdue` **não** é recalculado no cliente (FD-09).

## Testes obrigatórios

- Unidade: `TasksStore` com `TasksApi` simulado — todos os CA acima.
- **CA-08 (corrida entre cargas) e CA-10 (vazamento entre usuários) são obrigatórios** — ambos falham em produção de forma intermitente e silenciosa.

## Decisões em aberto

- **FD-05** — Serviços com signals.
- **FD-09** — `isOverdue` vem da API.

## Auditoria dos critérios (03/10/2026)

Critérios conferidos contra o código em 03/10/2026. Marcados: 13 de 14.

| CA | Situação | Evidência / motivo |
|---|---|---|
| CA-05 | atendido em outro lugar | O store não tem estado `not_found` próprio; 404 vira `AppError` com `NOT_FOUND_MESSAGE` fixa em `core/errors/error-mapper.ts` (testado em `error-mapper.spec.ts`), e as telas reagem (E2E 11, `edit-task.component.spec.ts`). `getById`/`update` repassam o erro; `complete`/`reopen`/`remove` removem o item. |
| CA-06 | atendido em outro lugar | Mesma origem: o mapeador não distingue tarefa alheia de inexistente (E2E 11 compara o DOM das duas). |
| CA-11 | atendido em outro lugar | Verificado por busca: só `tasks.store.ts` importa `TasksApi` fora de `core/api`. Não há regra de lint que o imponha. |
| CA-12 | em aberto | O store não usa `effect()` para requisição, mas `TasksPageComponent` dispara `store.load` dentro de um `effect()` (URL como fonte de verdade, FE-16). Desvio deliberado e documentado no componente; nenhuma decisão `FD-*` o oficializa, então o critério à letra não é atendido. |
