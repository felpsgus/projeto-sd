# FE-18 — Editar tarefa

| | |
|---|---|
| **Domínio** | Tarefas |
| **Depende de** | [FE-17](FE-17-criar-tarefa.md) · backend: [BE-18](../backend/BE-18-consultar-tarefa-autorizacao.md), [BE-19](../backend/BE-19-editar-tarefa.md) |
| **Bloqueia** | — |
| **Regras cobertas** | RN-TASK-11, RN-TASK-14, RN-AUTZ-02, RN-AUTZ-03 |
| **Estimativa** | M |

> **Recorte do T2 (21/09/2026):** fica fora do T2 — o backend do T2 não tem `GET /api/tasks/{id}` para autorização nem `PUT`/`PATCH` de edição.
>
> **Entrou em 23/09/2026:** o recorte acima foi revogado — o backend já expõe `GET /api/tasks/{id}` e `PUT /api/tasks/{id}` (BE-18/BE-19), e esta task foi implementada em escopo pleno na terceira onda da Fase 1 (ver [PLANO-REGRAS-RESTANTES.md](../PLANO-REGRAS-RESTANTES.md)).

## Objetivo

O usuário edita título, descrição, prioridade e vencimento de uma tarefa sua — e uma tarefa que não é dele simplesmente não existe, do ponto de vista da tela.

## Escopo

### Inclui

- Rota `/tasks/:id/edit` (FD-07), protegida por `authGuard`.
- Carga inicial via `GET /api/tasks/{id}`, preenchendo o formulário com os valores atuais.
- Formulário **reaproveitado de [FE-17](FE-17-criar-tarefa.md)** — mesmo componente de apresentação, mesmas validações (RN-TASK-02 a RN-TASK-05), com título e rótulo de botão diferentes.
- Envio a `PUT /api/tasks/{id}` com **todos** os quatro campos editáveis (RN-TASK-11) — o backend usa semântica de **substituição** ([BE-19](../backend/BE-19-editar-tarefa.md)), então omitir um campo o limparia.
- Sucesso (200) → volta à lista com a tarefa atualizada e confirmação discreta.
- **404** → tela de "Tarefa não encontrada" com link para a lista. Vale igualmente para tarefa inexistente, removida ou de outro usuário (RN-AUTZ-03) — **sem** distinção de mensagem.
- Exibição, apenas para leitura, do estado atual (Pendente/Concluída) e da data de última atualização (RN-TASK-14).
- `canDeactivate` para alterações não salvas.

### Não inclui

- Alterar o estado por aqui — concluir/reabrir é [FE-19](FE-19-concluir-reabrir.md). O formulário **não** tem controle de estado.
- Remover por aqui — é [FE-20](FE-20-remover-tarefa.md).
- Histórico de alterações (fora do escopo, seção 8).

## Notas técnicas

- **A semântica de substituição do `PUT` é a armadilha desta task.** Como o backend trata campo ausente como `null` ([BE-19](../backend/BE-19-editar-tarefa.md)/CA-05), enviar um payload parcial apagaria silenciosamente a descrição ou o vencimento que o usuário não tocou. O formulário sempre envia os quatro campos com os valores atuais da tela. CA-06 e CA-07 travam isso.
- **RN-AUTZ-03 no cliente:** a tela recebe 404 e diz "Tarefa não encontrada", ponto. Nada de "você não tem permissão" — isso confirmaria a existência do recurso que o backend cuidou de esconder.
- Editar uma tarefa concluída é permitido e **não** a reabre — o estado não faz parte do payload.
- Reaproveitar o componente de formulário de FE-17 evita a divergência de validação entre criar e editar, que é o tipo de bug que só aparece meses depois.

## Critérios de aceite

### Carga

- [x] **CA-01** — A tela carrega e preenche todos os campos com os valores atuais da tarefa.
- [x] **CA-02** — Durante a carga, exibe indicador — não um formulário vazio que "pisca" com os valores depois.
- [x] **CA-03** — Exibe o estado atual e a data de última atualização, ambos somente leitura (RN-TASK-14).
- [x] **CA-04** — Tarefa sem descrição ou sem vencimento carrega com os campos vazios, sem `null` na tela.

### Edição

- [ ] **CA-05** — Alterar os quatro campos e salvar persiste todos (RN-TASK-11).
- [x] **CA-06** — Alterar **apenas o título** e salvar **preserva** descrição, prioridade e vencimento — não os apaga.
- [x] **CA-07** — O request `PUT` enviado contém **os quatro campos**, com os valores correntes da tela (verificado no teste da chamada).
- [x] **CA-08** — Limpar a descrição e salvar efetivamente a limpa.
- [x] **CA-09** — Limpar o vencimento e salvar efetivamente o remove.
- [ ] **CA-10** — Editar uma tarefa **concluída** funciona e ela **permanece concluída**.
- [x] **CA-11** — O formulário **não** tem nenhum controle de estado (Pendente/Concluída).
- [x] **CA-12** — Após salvar, a lista reflete a tarefa atualizada na posição correta da ordenação.
- [x] **CA-13** — O botão salvar fica desabilitado sem alterações pendentes e durante o envio; clique duplo envia **uma** requisição. *(Atendido em 03/10/2026, issue #14: na edição o envio exige `form.dirty`, a mesma noção do `canDeactivate`; a criação não muda. Coberto por `edit-task.component.spec.ts`, inclusive o clique duplo.)*

### Validação

- [ ] **CA-14** — As mesmas validações de FE-17 valem: título 1–200 e não só espaços, descrição ≤ 2000, prioridade válida.
- [x] **CA-15** — O componente de formulário é **o mesmo** de FE-17 (verificado por revisão — não há validação duplicada).
- [x] **CA-16** — Vencimento no passado continua sendo aceito (RN-TASK-05).

### Não encontrada (RN-AUTZ-03)

- [x] **CA-17** — Id inexistente exibe "Tarefa não encontrada" com link para a lista.
- [x] **CA-18** — Tarefa de **outro usuário** exibe **exatamente a mesma** tela e mensagem — nunca "sem permissão" nem "acesso negado".
- [x] **CA-19** — Tarefa removida exibe a mesma tela.
- [x] **CA-20** — Um teste compara o DOM renderizado nos três casos e confirma que são indistinguíveis.
- [x] **CA-21** — Id em formato inválido na URL (`/tasks/abc/edit`) exibe a mesma tela de não encontrada, sem erro técnico.
- [x] **CA-22** — Receber 404 **ao salvar** (tarefa removida em outra aba enquanto era editada) exibe a mensagem sem travar a tela.

### Navegação e acessibilidade

- [ ] **CA-23** — Sair com alterações não salvas exibe aviso; sem alterações, não exibe.
- [x] **CA-24** — Cancelar volta à lista sem salvar.
- [x] **CA-25** — Erro de rede preserva o que foi digitado.
- [ ] **CA-26** — Operável só pelo teclado; foco no primeiro campo com erro após falha; usável em 360 px.

## Testes obrigatórios

- Componente (Testing Library): CA-01 a CA-17, CA-21 a CA-26.
- **CA-06 e CA-07 são obrigatórios** — o apagamento silencioso por `PUT` parcial é o defeito mais provável e o mais difícil de perceber.
- **CA-18 e CA-20 são o guardião de RN-AUTZ-03 no cliente.**

## Auditoria dos critérios (03/10/2026)

Critérios conferidos contra o código em 03/10/2026. Marcados: 20 de 26.

| CA | Situação | Evidência / motivo |
|---|---|---|
| CA-05 | em aberto | O teste de edição altera só o título; nenhum teste altera os quatro campos juntos. |
| CA-10 | em aberto | Sem teste de edição de tarefa concluída. O front não envia `status` e o Gateway documenta que PUT mantém a tarefa concluída. |
| CA-13 | em aberto | Não implementado: "Salvar alterações" só desabilita durante o envio, não sem alterações pendentes. Clique duplo protegido por `submitting` mas sem teste. |
| CA-14 | em aberto | Mesmas validações do `TaskFormComponent` compartilhado, mas sem teste de limites (ver FE-17 CA-08/CA-09). |
| CA-18 | atendido em outro lugar | E2E 11 (`e2e/flows.spec.ts`): usuário B abre o link do usuário A e vê "Tarefa não encontrada"; unitário `edit-task.component.spec.ts` confere ausência de "permissão/acesso negado". |
| CA-20 | atendido em outro lugar | E2E 11 compara o `innerText` de `main` entre tarefa alheia e id inexistente (iguais). O caso "removida" usa o mesmo caminho 404 e só tem teste unitário de texto. |
| CA-23 | em aberto | Guarda e `canDeactivate` com `window.confirm` implementados e ligados em `app.routes.ts`; sem spec da guarda nem do componente. |
| CA-25 | atendido em outro lugar | Mesmo `TaskFormComponent` do `create-task.component.spec.ts` (erro de rede preserva o digitado); sem teste na tela de edição. |
| CA-26 | em aberto | Foco no primeiro campo com erro sem teste; o E2E de 360 px não visita `/tasks/:id/edit`; sem teste de teclado. |
