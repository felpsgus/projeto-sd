# FE-17 — Criar tarefa

| | |
|---|---|
| **Domínio** | Tarefas |
| **Depende de** | [FE-14](FE-14-servico-estado-tarefas.md) · backend: [BE-17](../backend/BE-17-criar-tarefa.md) |
| **Bloqueia** | FE-18 |
| **Regras cobertas** | RN-TASK-10, RN-TASK-02 a RN-TASK-05, RN-TASK-07, RN-TASK-15 |
| **Estimativa** | M |

> **Recorte do T2 (21/09/2026):** entra **integral**, incluindo o limite de tarefas ativas (RN-TASK-15) se o backend do T2 o implementar; se não implementar (a task depende de checar [BE-36](../backend/BE-36-api-gateway.md)), o bloco de 409/`task.active_limit_reached` fica sem cenário para exercitar na prática, mas o código permanece pronto. Depende de [BE-36](../backend/BE-36-api-gateway.md) (`POST /api/tasks`).
>
> **Contrato real de criação extraído do Gateway** (`Contracts/CreateTaskHttpRequest.cs`, `Endpoints/TaskEndpoints.cs`): corpo `{ title, description, priority, dueDate }`, todos string (inclusive `priority` e `dueDate` — um valor fora do domínio, ex. `"priority": "Urgente"`, vira 400 por campo, não erro de binding). Sucesso devolve **201** com header `Location: /api/tasks/{id}` e corpo `TaskHttpResponse { id, title, description, priority, status, dueDate, completedAt, isOverdue, createdAt, updatedAt }`. O 400 de validação usa `Results.ValidationProblem`, com as chaves de `errors` em **PascalCase** (`Title`, `Description`, `Priority`, `DueDate`) — não confundir com o camelCase do corpo de sucesso.

## Objetivo

O usuário cria uma tarefa informando apenas o título, com os demais campos opcionais — e recebe uma mensagem clara se atingir o limite de tarefas ativas.

## Escopo

### Inclui

- Rota `/tasks/new` (FD-07), protegida por `authGuard`, acessível pelo botão "Nova tarefa" da lista.
- Formulário Signal Forms:

  | Campo | Obrigatório | Validação | Regra |
  |---|---|---|---|
  | Título | **sim** | 1–200 caracteres, não só espaços | RN-TASK-02 |
  | Descrição | não | ≤ 2000 caracteres | RN-TASK-03 |
  | Prioridade | não | Baixa / Média / **Média por padrão** / Alta | RN-TASK-04 |
  | Vencimento | não | data; **passado é permitido** | RN-TASK-05 |

- Contador de caracteres nos campos com limite, aparecendo ao se aproximar do máximo.
- **Prioridade "Média" pré-selecionada** — reflete o padrão de RN-TASK-04 em vez de deixar o usuário escolher algo que já tem default.
- Vencimento no passado é **aceito** (RN-TASK-05 / D-06), com aviso não-bloqueante de que a tarefa nascerá atrasada.
- Envio a `POST /api/tasks`. Sucesso (201) → volta para `/tasks` com a tarefa nova visível e confirmação discreta.
- **Limite de tarefas ativas** (RN-TASK-15): resposta **409** `task.active_limit_reached` exibe mensagem clara com o limite **vindo da resposta**, orientando a concluir ou remover tarefas. O formulário permanece preenchido.
- `canDeactivate` ([FE-07](FE-07-roteamento-guards.md)): avisa ao sair com alterações não salvas.
- Botão "Cancelar" que volta à lista (com o mesmo aviso, se houver alterações).

### Não inclui

- Criação rápida em linha na própria lista — pode ser melhoria futura; a rota dedicada cobre o caso e é acessível.
- Escolher o estado inicial: toda tarefa nasce Pendente (RN-TASK-07).
- Tarefas recorrentes, subtarefas, anexos (fora do escopo, seção 8).

## Notas técnicas

- **A regra é "ao menos o título" (RN-TASK-10)**, então o formulário precisa ser enviável com só ele preenchido. Marcar prioridade ou vencimento como obrigatórios seria inventar regra.
- **Aceitar vencimento no passado é intencional** (D-06). O seletor de data **não** deve ter data mínima — bloquear o passado contradiria a regra e frustraria quem cadastra algo já vencido.
- **A mensagem do limite não pode ter "500" escrito no frontend**: o valor é configurável no backend (`Tasks:MaxActivePerUser`) e vem no `ProblemDetails`. CA-14 trava isso.
- Após criar, voltar para a lista e recarregá-la: a ordenação de RN-LIST-06 define onde a tarefa nova aparece, e o cliente não tem como saber a posição sem perguntar ao servidor.
- O campo de data envia `yyyy-MM-dd` ([FE-02](FE-02-contratos-camada-http.md)) — sem conversão por `Date`, para não deslocar o dia por fuso.

## Critérios de aceite

### Criação

- [x] **CA-01** — Criar com **apenas o título** funciona e retorna à lista com a tarefa visível (RN-TASK-10).
- [x] **CA-02** — A tarefa criada aparece como **Pendente** (RN-TASK-07).
- [x] **CA-03** — Sem escolher prioridade, a tarefa é criada como **Média** (RN-TASK-04), e o campo já vem pré-selecionado assim.
- [x] **CA-04** — Criar com todos os campos preenchidos persiste todos corretamente.
- [x] **CA-05** — Após criar, a lista reflete o estado do servidor (a tarefa aparece na posição correta da ordenação).
- [x] **CA-06** — O botão de envio fica desabilitado durante a requisição; clique duplo cria **uma** tarefa.

### Validação

- [x] **CA-07** — Título vazio ou só espaços impede o envio, com erro no campo (RN-TASK-02).
- [x] **CA-08** — Título com 201 caracteres é rejeitado; com 1 e com 200 é aceito.
- [x] **CA-09** — Descrição com 2001 caracteres é rejeitada; com 2000 é aceita (RN-TASK-03).
- [x] **CA-10** — O contador de caracteres aparece ao se aproximar do limite e reflete o valor real. *(04/10/2026: o contador fica sempre visível, não só perto do limite.)*
- [x] **CA-11** — Vencimento no passado é **aceito** e exibe aviso não-bloqueante de que a tarefa ficará atrasada (RN-TASK-05). *(Atendido em 03/10/2026, issue #7: aviso no `TaskFormComponent`, com "hoje" vindo do mesmo utilitário do `X-Client-Date`; desligado na edição de tarefa Concluída. Coberto por `create-task.component.spec.ts` e `edit-task.component.spec.ts`.)*
- [x] **CA-12** — O seletor de data **não** impede escolher datas passadas.
- [x] **CA-13** — A data enviada é `yyyy-MM-dd` e corresponde ao dia escolhido, mesmo em fuso `UTC-3`.

### Limite

- [x] **CA-14** — Resposta **409** exibe mensagem clara informando o limite, com o número **vindo da resposta da API** (RN-TASK-15). *(Atendido em 03/10/2026, issue #6: o número é extraído do `detail` do `ProblemDetails` em `error-mapper.ts`; sem número, cai na mensagem do catálogo. Coberto por `error-mapper.spec.ts`.)*
- [x] **CA-15** — A mensagem orienta o que fazer (concluir ou remover tarefas) e não é um erro técnico genérico.
- [x] **CA-16** — Nesse caso o formulário permanece preenchido, permitindo tentar de novo após liberar espaço.
- [x] **CA-17** — Nenhuma constante `500` existe no código do frontend relacionada a esse limite.

### Navegação e erros

- [x] **CA-18** — Sair da tela com alterações não salvas exibe aviso e permite cancelar a saída. *(Atendido em 03/10/2026, issue #5: `/tasks/new` usa a `unsavedChangesGuard`; coberto por `create-task.component.spec.ts`.)*
- [x] **CA-19** — Sair sem alterações não exibe aviso. *(Atendido em 03/10/2026, issue #5.)*
- [x] **CA-20** — Erro 400 do backend é exibido nos campos correspondentes.
- [x] **CA-21** — Erro de rede exibe mensagem e **preserva** o que foi digitado.
- [x] **CA-22** — Cancelar volta à lista sem criar nada.

### Acessibilidade

- [x] **CA-23** — Todos os campos têm label associado; erros ligados por `aria-describedby`.
- [x] **CA-24** — O formulário é preenchível e enviável só pelo teclado, incluindo o seletor de data.
- [x] **CA-25** — Ao falhar, o foco vai para o primeiro campo com erro.
- [x] **CA-26** — Usável em 360 px.

## Testes obrigatórios

- Componente (Testing Library): CA-01 a CA-12, CA-14 a CA-25.
- **CA-13 (fuso) e CA-17 (limite não hardcoded) são obrigatórios.**
- Criar tarefa é fluxo crítico no E2E de [FE-22](FE-22-testes-e2e.md).

## Decisões em aberto

- **FD-07** — Rota dedicada em vez de diálogo.

## Auditoria dos critérios (03/10/2026)

Critérios conferidos contra o código em 03/10/2026. Marcados: 14 de 26.

| CA | Situação | Evidência / motivo |
|---|---|---|
| CA-04 | em aberto | Nenhum teste cria com todos os campos preenchidos (só título; o E2E acrescenta prioridade Alta). |
| CA-06 | em aberto | `submit()` ignora reenvio durante `submitting`, mas nenhum teste faz clique duplo ou checa o botão desabilitado em voo. |
| CA-08 | em aberto | Sem teste de limites do título (1/200 aceitos, 201 rejeitado); não há spec de `task-form.validators.ts`. |
| CA-09 | em aberto | Sem teste de limites da descrição (2000/2001); validador `maxLength` presente. |
| CA-10 | em aberto | O contador é sempre visível (`n/200`), não só ao se aproximar do limite, e não tem teste. |
| CA-11 | em aberto | Não implementado: nenhum aviso não-bloqueante de que a tarefa ficará atrasada ao escolher vencimento passado (RN-TASK-05). |
| CA-12 | atendido em outro lugar | Sem atributo `min` no `<input type="date">` (`task-form.component.html`); verificado por leitura do template. |
| CA-13 | em aberto | Teste obrigatório ausente. `<input type="date">` entrega `yyyy-MM-dd` sem conversão por `Date`, então é TZ-safe por construção, mas nada o comprova. |
| CA-14 | em aberto | Visível ao usuário: 409 vira a mensagem fixa do catálogo (`task.active_limit_reached`), sem o número do limite; o backend manda o número na mensagem (`TaskErrors.ActiveLimitReached(limit)`), que o front ignora. `features/tasks/README.md` registra que a UI do limite na criação não foi revisitada. |
| CA-15 | atendido em outro lugar | Mensagem do catálogo (`error-messages.ts`) orienta concluir/remover; erro de rede e 409 passam pelo mesmo `submitFailed`. |
| CA-16 | atendido em outro lugar | O 409 usa o ramo de `submitFailed` sem resetar o formulário, o mesmo do teste de rede (CA-21); sem teste específico de 409 na criação. |
| CA-17 | atendido em outro lugar | Busca por `500` no código-fonte do front não encontra constante de limite (só README); sem teste automatizado. |
| CA-18 | em aberto | Não implementado: `/tasks/new` não tem `canDeactivate` (a guarda só está em `:id/edit`); sair com o formulário preenchido descarta tudo em silêncio. |
| CA-19 | em aberto | Vácuo: sem aviso de saída na criação o critério não tem o que verificar; segue o CA-18. |
| CA-24 | em aberto | O E2E de teclado preenche o título e envia por teclado, mas não usa o seletor de data; não verificável por código. |
| CA-25 | em aberto | `focusFirstInvalidField` está implementado, sem teste. |
