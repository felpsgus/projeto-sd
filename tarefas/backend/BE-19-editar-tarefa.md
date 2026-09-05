# BE-19 — Editar tarefa

| | |
|---|---|
| **Domínio** | Tarefas |
| **Depende de** | [BE-18](BE-18-consultar-tarefa-autorizacao.md) |
| **Bloqueia** | — |
| **Regras cobertas** | RN-TASK-11, RN-TASK-14, RN-AUTZ-02, RN-AUTZ-03 |
| **Estimativa** | M |

## Objetivo

O usuário edita título, descrição, prioridade e data de vencimento das próprias tarefas, e a data de última atualização acompanha.

## Escopo

### Inclui

- **`PUT /api/tasks/{id}`** (autenticado) — substituição completa dos campos editáveis:

  ```json
  {
    "title": "string",
    "description": "string|null",
    "priority": "Low|Medium|High",
    "dueDate": "yyyy-MM-dd|null"
  }
  ```

- Caso de uso `UpdateTaskHandler`:
  1. resolve a tarefa por `GetOwnedTaskAsync` (BE-18);
  2. chama `TodoTask.UpdateDetails(...)`;
  3. persiste.
- Resposta **200** com o `TaskResponse` atualizado.
- Validação idêntica à da criação (mesmas regras RN-TASK-02 a RN-TASK-05) — validador **compartilhado** com BE-17, não copiado.

### Não inclui

- Alterar `status` por este endpoint — conclusão e reabertura têm endpoints próprios (BE-20). Enviar `status` no corpo é ignorado.
- Alterar dono, `createdAt`, `completedAt` ou `id`.

## Notas técnicas

- **`PUT` com semântica de substituição**: campos omitidos viram `null`. `{"title": "X"}` limpa a descrição e o vencimento e volta a prioridade ao padrão. Isso é previsível e testável; o alternativo (`PATCH` com merge) exige distinguir "ausente" de "null" no JSON, o que complica sem necessidade nesta versão. **Documentar claramente no OpenAPI** — é a maior fonte de confusão do contrato.
- A separação entre editar dados e mudar estado é deliberada: mantém as transições de RN-TASK-08/09 num único lugar (BE-20) e impede um `PUT` de "concluir por acidente".
- Editar uma tarefa **concluída** é permitido (nada nas regras proíbe) e não a reabre.

## Critérios de aceite

- [ ] **CA-01** — `PUT` válido em tarefa própria retorna **200** com os novos valores (RN-TASK-11).
- [ ] **CA-02** — Todos os quatro campos editáveis efetivamente mudam: título, descrição, prioridade e vencimento.
- [ ] **CA-03** — `updatedAt` muda; `createdAt` **não** muda (RN-TASK-14).
- [ ] **CA-04** — `description: null` e `dueDate: null` **limpam** os campos.
- [ ] **CA-05** — Campos omitidos no corpo assumem o padrão de substituição (descrição e vencimento nulos, prioridade `Medium`) — comportamento verificado e documentado.
- [ ] **CA-06** — As mesmas validações da criação valem: título vazio/só-espaços/201 caracteres → **400**; descrição com 2001 → **400**; prioridade inválida → **400**.
- [ ] **CA-07** — `dueDate` no passado é aceita e a resposta reflete `isOverdue: true`.
- [ ] **CA-08** — Enviar `"status": "Completed"` no corpo **não** altera o estado da tarefa (RN-TASK-11 lista apenas os quatro campos).
- [ ] **CA-09** — Enviar `"id"` ou `"ownerId"` diferentes no corpo não altera nada no banco.
- [ ] **CA-10** — Editar uma tarefa **concluída** funciona e ela permanece `Completed`, com `completedAt` inalterado.
- [ ] **CA-11** — `PUT` em tarefa de **outro usuário** retorna **404**, com corpo idêntico ao de id inexistente (RN-AUTZ-02, RN-AUTZ-03).
- [ ] **CA-12** — `PUT` em tarefa removida (soft delete) retorna **404**.
- [ ] **CA-13** — `PUT` sem token retorna **401**.
- [ ] **CA-14** — Uma edição que **falha na validação** não altera `updatedAt` no banco.
- [ ] **CA-15** — O validador é a mesma classe usada por BE-17 (sem duplicação de regra — verificado em revisão).
- [ ] **CA-16** — O OpenAPI descreve explicitamente a semântica de substituição do `PUT`.

## Testes obrigatórios

- Unidade: `UpdateTaskHandler` — CA-03, CA-08, CA-10, CA-14.
- Integração: CA-01, CA-02, CA-04 a CA-07, CA-09, CA-11 a CA-13.
- CA-11 é coberto pelo teste transversal de BE-18 (CA-11 daquela task) — adicionar o endpoint à lista.
