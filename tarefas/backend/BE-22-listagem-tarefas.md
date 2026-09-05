# BE-22 — Listagem: filtros, busca, ordenação e paginação

| | |
|---|---|
| **Domínio** | Tarefas |
| **Depende de** | [BE-17](BE-17-criar-tarefa.md), [BE-18](BE-18-consultar-tarefa-autorizacao.md) |
| **Bloqueia** | — |
| **Regras cobertas** | RN-LIST-01 a RN-LIST-07, RN-TASK-16, RN-AUTZ-02, RN-AUTZ-04 |
| **Estimativa** | G |

## Objetivo

O usuário lista as próprias tarefas com filtros combináveis, busca textual, ordenação padrão previsível e paginação.

## Escopo

### Inclui

- **`GET /api/tasks`** (autenticado), com query string:

  | Parâmetro | Valores | Padrão | Regra |
  |---|---|---|---|
  | `status` | `pending` \| `completed` \| `all` | `all` | RN-LIST-02 |
  | `priority` | `low` \| `medium` \| `high` (repetível) | todas | RN-LIST-03 |
  | `overdue` | `true` \| `false` | não filtra | RN-LIST-04 |
  | `search` | texto livre | — | RN-LIST-05 |
  | `page` | ≥ 1 | 1 | RN-LIST-07 |
  | `pageSize` | 1–100 | **20** | RN-LIST-07 / D-09 |

- **Ordenação padrão fixa** (RN-LIST-06), nesta ordem exata:
  1. **pendentes antes de concluídas**;
  2. **data de vencimento crescente**, com **tarefas sem vencimento por último**;
  3. **data de criação** (crescente);
  4. `Id` como desempate final — sem ele a paginação pode repetir ou pular itens entre páginas.
- Filtro base **sempre** aplicado, não opcional: `OwnerId == ICurrentUser.Id` e não-removida (RN-LIST-01).
- Busca textual em **título e descrição** (D-16), case-insensitive e acento-insensível quando o collation permitir.
- Resposta **200**:

  ```json
  {
    "items": [ /* TaskResponse[] */ ],
    "page": 1, "pageSize": 20,
    "totalItems": 137, "totalPages": 7
  }
  ```

- `PagingOptions`: `Paging:DefaultPageSize` = 20, `Paging:MaxPageSize` = 100.
- Índices de suporte criados em BE-05 (`(OwnerId, Status)`, `(OwnerId, DueDate)`).

### Não inclui

- Ordenação configurável pelo cliente — a RN-LIST-06 define **a** ordenação. Se o produto quiser `sort=`, é task nova.
- Busca full-text avançada, relevância, fuzzy.
- Cursor pagination.

## Notas técnicas

- **"Sem vencimento por último" não sai de graça.** Em SQL, `NULL` ordena antes ou depois conforme o banco; no PostgreSQL, `ORDER BY due_date ASC` põe `NULL` **por último** por padrão, mas depender disso é frágil. Usar `ORDER BY due_date ASC NULLS LAST` explicitamente, ou uma chave auxiliar (`CASE WHEN due_date IS NULL THEN 1 ELSE 0 END`).
- **O filtro `overdue` é derivado, não uma coluna** (RN-TASK-16). Ele **DEVE** ser traduzido para SQL — `status = pending AND due_date < @today` — e não avaliado em memória, senão a paginação e o `totalItems` ficam errados. Este é o defeito mais provável da task.
- **"Hoje" é a data local do usuário**, vinda de `IClientDate.Today` ([BE-13](BE-13-protecao-endpoints.md), decisão **D-18**), e é passada como parâmetro `@today` à query. O mesmo valor alimenta o filtro `overdue` e o `isOverdue` da projeção — se os dois divergirem, a lista mostra itens que o filtro diz não existir.
- Filtros são **combináveis** por conjunção (`AND`); múltiplas prioridades são disjunção entre si (`priority IN (...)`).
- Toda a consulta é uma única query no banco com `Skip`/`Take`; nunca carregar tudo e filtrar em memória. Verificar com log de SQL.
- `search` precisa ser tratado contra `%` e `_` do `LIKE` — escapar antes de compor o filtro.
- A projeção vai direto de `IQueryable<TodoTask>` para `TaskResponse` (`Select`), sem materializar entidades — evita expor domínio e reduz I/O.

## Critérios de aceite

### Base e escopo

- [ ] **CA-01** — `GET /api/tasks` retorna **somente** tarefas do usuário autenticado (RN-LIST-01) — verificado com duas contas povoadas.
- [ ] **CA-02** — Tarefas removidas (soft delete) **não** aparecem em nenhum cenário, inclusive com `status=all` (RN-LIST-01).
- [ ] **CA-03** — Sem token retorna **401** (RN-AUTZ-04).
- [ ] **CA-04** — Usuário sem tarefas recebe **200** com `items: []`, `totalItems: 0`, `totalPages: 0` — não 404.

### Filtros

- [ ] **CA-05** — `status=pending` retorna só pendentes; `status=completed` só concluídas; `status=all` (e ausência do parâmetro) retorna ambas (RN-LIST-02).
- [ ] **CA-06** — `priority=high` retorna só as de prioridade alta (RN-LIST-03).
- [ ] **CA-07** — `priority=low&priority=high` retorna as duas prioridades, e nenhuma `medium`.
- [ ] **CA-08** — `overdue=true` retorna apenas tarefas **pendentes com vencimento anterior a hoje** (RN-LIST-04 + RN-TASK-16): exclui as sem vencimento, as com vencimento hoje, as futuras e as **concluídas mesmo que vencidas**.
- [ ] **CA-09** — `overdue=false` retorna as **não** atrasadas.
- [ ] **CA-10** — Filtros combinam: `status=pending&priority=high&overdue=true` aplica os três simultaneamente.
- [ ] **CA-11** — Valor inválido em `status`, `priority` ou `overdue` retorna **400**, não é ignorado silenciosamente.

### Busca

- [ ] **CA-12** — `search=relatório` encontra tarefas com o termo no **título** (RN-LIST-05).
- [ ] **CA-13** — `search` também encontra pelo termo na **descrição** (D-16).
- [ ] **CA-14** — A busca é case-insensitive: `search=RELATÓRIO` encontra `"relatório"`.
- [ ] **CA-15** — `search=%` e `search=_` não funcionam como curinga: retornam apenas tarefas que contêm literalmente esses caracteres.
- [ ] **CA-16** — `search` combina com os demais filtros.
- [ ] **CA-17** — `search` vazio ou só-espaços é tratado como ausente.

### Ordenação

- [ ] **CA-18** — Todas as pendentes aparecem antes de todas as concluídas (RN-LIST-06), independentemente de vencimento.
- [ ] **CA-19** — Dentro do mesmo estado, ordena por vencimento **crescente**.
- [ ] **CA-20** — Tarefas **sem vencimento** vêm **depois** das com vencimento, dentro do mesmo estado.
- [ ] **CA-21** — Empates de vencimento (ou ambas sem vencimento) são resolvidos por data de criação crescente.
- [ ] **CA-22** — A ordenação é **estável e determinística**: a mesma consulta repetida N vezes devolve a mesma sequência exata (desempate por `Id`).
- [ ] **CA-23** — Um cenário com ao menos 8 tarefas cobrindo todas as combinações (pendente/concluída × com/sem vencimento × vencimentos iguais) valida a ordem completa numa única asserção de sequência.

### Paginação

- [ ] **CA-24** — Sem `page`/`pageSize`, retorna a página 1 com **20** itens (RN-LIST-07 / D-09).
- [ ] **CA-25** — `totalItems` reflete o total **após os filtros**, não o total geral do usuário.
- [ ] **CA-26** — `totalPages` = `ceil(totalItems / pageSize)`.
- [ ] **CA-27** — Percorrer todas as páginas retorna **cada tarefa exatamente uma vez**, sem repetição nem omissão (teste com 25 tarefas e `pageSize=10`).
- [ ] **CA-28** — `page` além do total retorna **200** com `items: []` e `totalItems` correto — não 404.
- [ ] **CA-29** — `pageSize` acima de `Paging:MaxPageSize` retorna **400** (ou é limitado ao máximo — escolher **um** comportamento e testá-lo).
- [ ] **CA-30** — `page=0`, `page=-1` ou `pageSize=0` retornam **400**.
- [ ] **CA-31** — Alterar `Paging:DefaultPageSize` para 5 muda o padrão sem alteração de código.

### Desempenho e corretude de query

- [ ] **CA-32** — Todos os filtros, a busca, a ordenação e a paginação são executados **no banco**: o SQL gerado contém `WHERE`, `ORDER BY`, `LIMIT`/`OFFSET`. Verificado por captura do SQL em teste.
- [ ] **CA-33** — O filtro `overdue` aparece no `WHERE` do SQL, não é avaliado em memória.
- [ ] **CA-33b** — O `@today` do `WHERE` é a data de `IClientDate`, não a data UTC do servidor (**D-18**): com relógio UTC em `2026-08-21T00:30` e header `X-Client-Date: 2026-08-20`, uma tarefa vencendo em `2026-08-20` **não** aparece em `overdue=true` e vem com `isOverdue: false`.
- [ ] **CA-33c** — Filtro e projeção usam **o mesmo** valor de "hoje": nenhum item retornado por `overdue=true` traz `isOverdue: false`, e vice-versa.
- [ ] **CA-34** — A consulta não materializa mais linhas do que `pageSize` (+ a contagem).
- [ ] **CA-35** — Com 1000 tarefas para um usuário, a listagem paginada responde dentro de um limite razoável documentado no PR (medição, não assert flaky).

## Testes obrigatórios

- Integração: **todos** os CA acima. Esta é a task com maior densidade de casos.
- Um `ICollectionFixture` com um dataset determinístico (tarefas com estados, prioridades e vencimentos controlados) reaproveitado pelos testes de filtro e ordenação.
- Teste de captura de SQL para CA-32 e CA-33.

## Decisões em aberto

- **D-09** — Tamanho padrão da página. **Adotado aqui: 20**, configurável. Confirmar com produto.
- **D-16** — Busca inclui descrição. Padrão provisório: sim.
- **D-18** — ✅ decidida: "hoje" é a data local do usuário, via `IClientDate`.
