# FE-17 — Criar tarefa

| | |
|---|---|
| **Domínio** | Tarefas |
| **Depende de** | [FE-14](FE-14-servico-estado-tarefas.md) · backend: [BE-17](../backend/BE-17-criar-tarefa.md) |
| **Bloqueia** | FE-18 |
| **Regras cobertas** | RN-TASK-10, RN-TASK-02 a RN-TASK-05, RN-TASK-07, RN-TASK-15 |
| **Estimativa** | M |

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

- [ ] **CA-01** — Criar com **apenas o título** funciona e retorna à lista com a tarefa visível (RN-TASK-10).
- [ ] **CA-02** — A tarefa criada aparece como **Pendente** (RN-TASK-07).
- [ ] **CA-03** — Sem escolher prioridade, a tarefa é criada como **Média** (RN-TASK-04), e o campo já vem pré-selecionado assim.
- [ ] **CA-04** — Criar com todos os campos preenchidos persiste todos corretamente.
- [ ] **CA-05** — Após criar, a lista reflete o estado do servidor (a tarefa aparece na posição correta da ordenação).
- [ ] **CA-06** — O botão de envio fica desabilitado durante a requisição; clique duplo cria **uma** tarefa.

### Validação

- [ ] **CA-07** — Título vazio ou só espaços impede o envio, com erro no campo (RN-TASK-02).
- [ ] **CA-08** — Título com 201 caracteres é rejeitado; com 1 e com 200 é aceito.
- [ ] **CA-09** — Descrição com 2001 caracteres é rejeitada; com 2000 é aceita (RN-TASK-03).
- [ ] **CA-10** — O contador de caracteres aparece ao se aproximar do limite e reflete o valor real.
- [ ] **CA-11** — Vencimento no passado é **aceito** e exibe aviso não-bloqueante de que a tarefa ficará atrasada (RN-TASK-05).
- [ ] **CA-12** — O seletor de data **não** impede escolher datas passadas.
- [ ] **CA-13** — A data enviada é `yyyy-MM-dd` e corresponde ao dia escolhido, mesmo em fuso `UTC-3`.

### Limite

- [ ] **CA-14** — Resposta **409** exibe mensagem clara informando o limite, com o número **vindo da resposta da API** (RN-TASK-15).
- [ ] **CA-15** — A mensagem orienta o que fazer (concluir ou remover tarefas) e não é um erro técnico genérico.
- [ ] **CA-16** — Nesse caso o formulário permanece preenchido, permitindo tentar de novo após liberar espaço.
- [ ] **CA-17** — Nenhuma constante `500` existe no código do frontend relacionada a esse limite.

### Navegação e erros

- [ ] **CA-18** — Sair da tela com alterações não salvas exibe aviso e permite cancelar a saída.
- [ ] **CA-19** — Sair sem alterações não exibe aviso.
- [ ] **CA-20** — Erro 400 do backend é exibido nos campos correspondentes.
- [ ] **CA-21** — Erro de rede exibe mensagem e **preserva** o que foi digitado.
- [ ] **CA-22** — Cancelar volta à lista sem criar nada.

### Acessibilidade

- [ ] **CA-23** — Todos os campos têm label associado; erros ligados por `aria-describedby`.
- [ ] **CA-24** — O formulário é preenchível e enviável só pelo teclado, incluindo o seletor de data.
- [ ] **CA-25** — Ao falhar, o foco vai para o primeiro campo com erro.
- [ ] **CA-26** — Usável em 360 px.

## Testes obrigatórios

- Componente (Testing Library): CA-01 a CA-12, CA-14 a CA-25.
- **CA-13 (fuso) e CA-17 (limite não hardcoded) são obrigatórios.**
- Criar tarefa é fluxo crítico no E2E de [FE-22](FE-22-testes-e2e.md).

## Decisões em aberto

- **FD-07** — Rota dedicada em vez de diálogo.
