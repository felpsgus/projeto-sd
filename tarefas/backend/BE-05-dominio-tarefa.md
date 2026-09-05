# BE-05 — Domínio: entidade `TodoTask` (estrutura, estados e ciclo de vida)

| | |
|---|---|
| **Domínio** | Tarefas |
| **Depende de** | [BE-02](BE-02-persistencia-base.md), [BE-03](BE-03-result-erros-validacao.md), [BE-04](BE-04-dominio-usuario.md) |
| **Bloqueia** | BE-17 a BE-23 |
| **Regras cobertas** | RN-TASK-01 a RN-TASK-09, RN-TASK-14, RN-TASK-16, RN-AUTZ-01 |
| **Estimativa** | G |

## Objetivo

Existe a entidade `TodoTask` com todas as suas invariantes e transições de estado garantidas no domínio, persistida com soft delete e auditoria. Nenhum caso de uso precisa re-validar título, prioridade ou transição.

## Escopo

### Inclui

- Entidade `TodoTask` em `Domain` (RN-TASK-01):

  | Propriedade | Tipo | Regra |
  |---|---|---|
  | `Id` | `Guid` | identificador único |
  | `OwnerId` | `Guid` | dono; obrigatório e **imutável** (RN-AUTZ-01) |
  | `Title` | `string` | 1–200 caracteres, não só espaços (RN-TASK-02) |
  | `Description` | `string?` | opcional, ≤ 2000 caracteres (RN-TASK-03) |
  | `Status` | `TaskStatus` | `Pending` \| `Completed` (RN-TASK-06) |
  | `Priority` | `TaskPriority` | `Low` \| `Medium` \| `High`, padrão `Medium` (RN-TASK-04) |
  | `DueDate` | `DateOnly?` | opcional, pode ser passada (RN-TASK-05) |
  | `CompletedAt` | `DateTime?` (UTC) | preenchida só quando `Completed` |
  | `CreatedAt` / `UpdatedAt` | `DateTime` (UTC) | auditoria (RN-TASK-14) |
  | `DeletedAt` | `DateTime?` (UTC) | soft delete (implementa `ISoftDeletable`) |

- Enums `TaskStatus` e `TaskPriority` em `Domain`. **Nada de string mágica** para estado ou prioridade.
- Factory `TodoTask.Create(ownerId, title, description?, priority?, dueDate?, TimeProvider)` retornando `Result<TodoTask>`:
  - `Status = Pending` sempre (RN-TASK-07);
  - `Priority = Medium` quando não informada (RN-TASK-04);
  - `CompletedAt = null`.
- Comportamentos:
  - `UpdateDetails(title, description, priority, dueDate)` → `Result` (RN-TASK-11 usará);
  - `Complete(TimeProvider)` → `Result`: só a partir de `Pending`; grava `CompletedAt` (RN-TASK-08);
  - `Reopen()` → `Result`: só a partir de `Completed`; limpa `CompletedAt` (RN-TASK-09);
  - `SoftDelete(TimeProvider)` → marca `DeletedAt`.
- **Toda** mutação bem-sucedida atualiza `UpdatedAt` (RN-TASK-14).
- `IsOverdue(DateOnly today)` como método/propriedade **calculada**: `Status == Pending && DueDate is not null && DueDate < today`. **Não** é coluna persistida (RN-TASK-16).
- Mapeamento EF: `OwnerId` como FK para `User`, enums persistidos como `int` (ou string com conversão explícita), filtro global de soft delete, índices em `(OwnerId, Status)` e `(OwnerId, DueDate)` para suportar BE-22.
- Repositório `ITodoTaskRepository` em `Application` (`GetByIdAsync`, `Add`, `Remove`, `CountActiveByOwnerAsync`, `Query`).

### Não inclui

- Endpoints e casos de uso — BE-17 a BE-22.
- Limite de 500 tarefas ativas (BE-17): o domínio não conhece esse limite, ele é regra do caso de uso.
- Expurgo definitivo (BE-23).

## Notas técnicas

- **RN-TASK-05 / D-06**: data de vencimento no passado é **aceita**. A entidade não a rejeita; a condição "atrasada" é derivada.
- **D-18**: `DueDate` é `DateOnly` e "hoje" é a **data local do usuário**, fornecida por `IClientDate` ([BE-13](BE-13-protecao-endpoints.md)) na borda. O domínio continua puro: `IsOverdue(DateOnly today)` recebe a data por parâmetro e não conhece fuso, header nem relógio.
- Transição inválida (concluir uma tarefa já concluída, reabrir uma pendente) é **erro de negócio** → `Result` de falha, não exceção.
- `Title` é armazenado com trim. Um título de 200 caracteres é válido; 201 não é. Testar exatamente as bordas.
- `IsOverdue` recebe "hoje" como parâmetro em vez de ler o relógio internamente — mantém `Domain` puro e o teste determinístico.

## Critérios de aceite

### Estrutura e validação

- [ ] **CA-01** — `Create` rejeita título vazio, só-espaços (`"   "`, `"\t"`) e com 201 caracteres; aceita com 1 e com 200.
- [ ] **CA-02** — O título é persistido com trim: `"  Comprar pão  "` vira `"Comprar pão"`.
- [ ] **CA-03** — `Create` aceita descrição nula e com 2000 caracteres; rejeita com 2001.
- [ ] **CA-04** — `Create` sem prioridade produz `Priority == Medium`.
- [ ] **CA-05** — Prioridade fora de `Low`/`Medium`/`High` é impossível de representar (enum), e um valor de enum inválido vindo de fora (`(TaskPriority)99`) é rejeitado por `Create`.
- [ ] **CA-06** — `Create` com `DueDate` no passado **sucede** (RN-TASK-05) e a tarefa resultante reporta `IsOverdue == true` para "hoje".
- [ ] **CA-07** — `Create` sempre produz `Status == Pending` e `CompletedAt == null`, independentemente dos argumentos.
- [ ] **CA-08** — `OwnerId` é obrigatório: `Create` com `Guid.Empty` falha.

### Ciclo de vida

- [ ] **CA-09** — `Complete()` em tarefa `Pending`: `Status` vira `Completed` e `CompletedAt` recebe o instante do `TimeProvider`.
- [ ] **CA-10** — `Complete()` em tarefa já `Completed` retorna `Result` de falha com código estável e **não** altera `CompletedAt`.
- [ ] **CA-11** — `Reopen()` em tarefa `Completed`: `Status` vira `Pending` e `CompletedAt` volta a `null`.
- [ ] **CA-12** — `Reopen()` em tarefa `Pending` retorna `Result` de falha e não altera nada.
- [ ] **CA-13** — Nenhuma transição inválida lança exceção — todas retornam `Result` de falha.

### Auditoria e derivações

- [ ] **CA-14** — `UpdateDetails`, `Complete`, `Reopen` e `SoftDelete` **todos** atualizam `UpdatedAt` (um teste por método, com `TimeProvider` avançando).
- [ ] **CA-15** — Uma operação que **falha** (ex.: `Complete` duas vezes) **não** altera `UpdatedAt`.
- [ ] **CA-16** — `IsOverdue` é `true` apenas quando `Pending` **e** `DueDate < hoje`. Verificar: sem `DueDate` → false; `DueDate == hoje` → false; `DueDate` passada mas `Completed` → false.
- [ ] **CA-17** — `IsOverdue` não é coluna no banco (conferir a migration gerada).

### Persistência

- [ ] **CA-18** — Salvar e recarregar uma tarefa preserva todos os campos, inclusive enums e `DueDate` como data (sem componente de hora).
- [ ] **CA-19** — Uma tarefa com `SoftDelete` aplicado não retorna em consulta normal do repositório e retorna com `IgnoreQueryFilters()`.
- [ ] **CA-20** — A migration cria os índices `(OwnerId, Status)` e `(OwnerId, DueDate)`.
- [ ] **CA-21** — A entidade não expõe setters públicos; todo estado muda por método de domínio.

## Testes obrigatórios

- Unidade (Domain, **cobertura ≥ 85%** — é o núcleo de regra de negócio): CA-01 a CA-16, CA-21.
- Integração: CA-18, CA-19, CA-20.
- Padrão AAA explícito, nomes `Metodo_Cenario_ResultadoEsperado`.

## Decisões em aberto

- **D-06** — Aceitar vencimento no passado. Padrão adotado: aceita.
- **D-18** — ✅ decidida: "hoje" é a data local do usuário, injetada na borda. O domínio permanece agnóstico a fuso.
