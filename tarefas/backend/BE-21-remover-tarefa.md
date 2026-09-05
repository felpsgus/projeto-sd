# BE-21 — Remover tarefa (soft delete)

| | |
|---|---|
| **Domínio** | Tarefas |
| **Depende de** | [BE-18](BE-18-consultar-tarefa-autorizacao.md) |
| **Bloqueia** | [BE-23](BE-23-expurgo-tarefas-removidas.md) |
| **Regras cobertas** | RN-TASK-12, RN-TASK-13, RN-TASK-14, RN-AUTZ-02, RN-AUTZ-03 |
| **Estimativa** | M |

## Objetivo

O usuário remove as próprias tarefas; elas desaparecem de tudo que ele enxerga, mas continuam no banco por um período antes do expurgo definitivo.

## Escopo

### Inclui

- **`DELETE /api/tasks/{id}`** (autenticado) → **204 No Content**.
- Caso de uso `DeleteTaskHandler`: resolve por `GetOwnedTaskAsync` (BE-18), chama `TodoTask.SoftDelete(TimeProvider)` (preenche `DeletedAt`), persiste. **Nenhum `DELETE` físico** (RN-TASK-13 / D-07).
- A partir daí, a tarefa some de: `GET /api/tasks/{id}`, listagem, contagem do limite de tarefas ativas (BE-17), e de qualquer outra consulta — via o filtro global de query de BE-02.

### Não inclui

- Endpoint de restauração ("desfazer") — não consta das regras de negócio. Se o produto quiser, é task nova; o dado já está preservado para isso.
- Expurgo definitivo após o período de retenção (BE-23).

## Notas técnicas

- O soft delete só funciona se **ninguém** puder esquecer o filtro. Ele é global no `DbContext` (BE-02), não uma cláusula repetida por consulta. As duas únicas exceções autorizadas a usar `IgnoreQueryFilters()` são o expurgo (BE-23) e a exclusão de conta (BE-16).
- **Idempotência:** `DELETE` de uma tarefa já removida retorna **404**, não 204. Como o filtro global já a esconde, `GetOwnedTaskAsync` não a encontra — e isso é consistente com RN-AUTZ-03 (o cliente não distingue "removida" de "nunca existiu"). Registrar a escolha, pois `DELETE` idempotente com 204 também seria defensável.
- Remover libera vaga no limite de 500 (RN-TASK-15), já que a tarefa deixa de ser ativa — coberto por CA-06 e por BE-17/CA-17.

## Critérios de aceite

- [ ] **CA-01** — `DELETE /api/tasks/{id}` de tarefa própria retorna **204** (RN-TASK-12).
- [ ] **CA-02** — A linha **continua existindo** no banco, com `DeletedAt` preenchido (RN-TASK-13) — verificado com `IgnoreQueryFilters()`.
- [ ] **CA-03** — `updatedAt` é atualizado pela remoção (RN-TASK-14).
- [ ] **CA-04** — Após a remoção, `GET /api/tasks/{id}` retorna **404**.
- [ ] **CA-05** — Após a remoção, a tarefa não aparece na listagem, em **nenhum** filtro — inclusive `status=all` (RN-LIST-01).
- [ ] **CA-06** — Após a remoção, a contagem de tarefas ativas do usuário diminui: estando no teto do limite, é possível criar uma nova tarefa.
- [ ] **CA-07** — Uma tarefa **concluída** também pode ser removida.
- [ ] **CA-08** — `DELETE` de tarefa já removida retorna **404**.
- [ ] **CA-09** — `DELETE` de tarefa de **outro usuário** retorna **404**, com corpo idêntico ao de id inexistente, e a tarefa da vítima permanece **intacta** no banco (`DeletedAt` continua nulo) — RN-AUTZ-02, RN-AUTZ-03.
- [ ] **CA-10** — `DELETE` de id inexistente retorna **404**.
- [ ] **CA-11** — `DELETE` sem token retorna **401**.
- [ ] **CA-12** — Nenhum caminho da API executa `DELETE` físico de tarefa (verificado por revisão do código e ausência de `Remove()` sobre `TodoTask` fora de BE-16 e BE-23).
- [ ] **CA-13** — A remoção de uma tarefa não afeta nenhuma outra tarefa do mesmo usuário.

## Testes obrigatórios

- Integração: CA-01 a CA-11, CA-13.
- Unidade: `DeleteTaskHandler` — CA-03, CA-08.
- O endpoint entra no teste transversal de autorização de BE-18 (CA-11 daquela task).

## Decisões em aberto

- **D-07** — Soft delete. Padrão adotado. Se virar hard delete, esta task simplifica e [BE-23](BE-23-expurgo-tarefas-removidas.md) deixa de existir.
