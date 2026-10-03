# FE-19 — Concluir e reabrir tarefa

| | |
|---|---|
| **Domínio** | Tarefas |
| **Depende de** | [FE-15](FE-15-listagem-paginacao.md) · backend: [BE-20](../backend/BE-20-concluir-reabrir-tarefa.md) |
| **Bloqueia** | — |
| **Regras cobertas** | RN-TASK-06, RN-TASK-08, RN-TASK-09, RN-TASK-16, RN-AUTZ-02, RN-AUTZ-03 |
| **Estimativa** | M |

> **Recorte do T2 (21/09/2026):** fica fora do T2 — o backend do T2 não tem endpoint de concluir/reabrir tarefa.
>
> **Entrou em 23/09/2026:** o recorte acima foi revogado — o backend já expõe `POST /api/tasks/{id}/complete` e `POST /api/tasks/{id}/reopen` (BE-20), e esta task foi implementada em escopo pleno na terceira onda da Fase 1 (ver [PLANO-REGRAS-RESTANTES.md](../PLANO-REGRAS-RESTANTES.md)). Decisão adicional dessa data: o limite de tarefas ativas (RN-TASK-15) também vale para reabrir, então o 409 de reabertura pode ser `task.active_limit_reached`, não só `task.not_completed`.

## Objetivo

Marcar uma tarefa como feita é a ação mais frequente do aplicativo — ela responde na hora, e volta atrás sozinha se o servidor recusar.

## Escopo

### Inclui

- Controle de conclusão em cada item da lista (caixa de seleção ou botão), alternando entre concluir e reabrir conforme o estado (RN-TASK-06).
- `POST /api/tasks/{id}/complete` e `POST /api/tasks/{id}/reopen`.
- **Atualização otimista** (FD-06): o item muda de estado imediatamente na interface; em caso de erro, **reverte** ao estado anterior e exibe mensagem.
- Após o sucesso, reconciliação com o servidor ([FE-14](FE-14-servico-estado-tarefas.md)): o item mantém os dados retornados e a página corrente é recarregada, porque a mudança de estado altera a posição na ordenação (RN-LIST-06) e pode remover o item do filtro ativo.
- Exibição da **data de conclusão** quando a tarefa está concluída (RN-TASK-08).
- Ao reabrir, a data de conclusão some e o selo "Atrasada" volta se o vencimento já passou (RN-TASK-09, RN-TASK-16).
- Tratamento de **409** (transição inválida — a tarefa mudou em outra aba): reverte o otimismo, exibe mensagem e recarrega o item.
- Tratamento de **404** (RN-AUTZ-03): mesma mensagem "Tarefa não encontrada", remove o item da lista.
- Bloqueio de cliques repetidos no mesmo item enquanto a requisição está em voo.

### Não inclui

- Conclusão em lote.
- Desfazer com prazo ("desfazer" por toast) — reabrir já está a um clique.

## Notas técnicas

- **O otimismo é o que torna a ação agradável, e o rollback é o que a torna correta.** Sem rollback, um erro de rede deixa a interface mentindo: o usuário vê "concluída" e o servidor discorda. CA-05 e CA-06 são inegociáveis.
- **A interação entre otimismo e filtro ativo é o ponto sutil.** Com o filtro "Pendentes", concluir uma tarefa faz ela **sair** da lista. Fazer isso instantaneamente é o comportamento esperado — mas se a chamada falhar, o item precisa **voltar** para a posição em que estava. Guardar o item e o índice antes de mutar é o que permite reverter direito.
- Concluir uma tarefa atrasada faz o selo "Atrasada" desaparecer (RN-TASK-16), porque a condição exige estado Pendente. Isso vem do backend via `isOverdue`; o cliente não recalcula (FD-09).
- **O 409 não é um erro do usuário** — significa que o estado divergiu (outra aba, outro dispositivo). A mensagem deve refletir isso ("esta tarefa já foi concluída em outro lugar"), não parecer falha da ação.
- Acessibilidade: a mudança de estado precisa ser anunciada. Uma caixa de seleção com label descritivo resolve isso naturalmente; um ícone sem texto, não.

## Critérios de aceite

### Concluir

- [x] **CA-01** — Concluir uma tarefa pendente atualiza o item para "Concluída" (RN-TASK-08).
- [x] **CA-02** — A mudança visual acontece **imediatamente**, antes da resposta do servidor (FD-06).
- [x] **CA-03** — A data de conclusão passa a ser exibida.
- [ ] **CA-04** — Concluir uma tarefa **atrasada** remove o selo "Atrasada" (RN-TASK-16).
- [x] **CA-05** — Se a chamada **falhar**, o item volta ao estado "Pendente" e uma mensagem de erro é exibida.
- [x] **CA-06** — Com o filtro "Pendentes" ativo, concluir remove o item da lista imediatamente; **se a chamada falhar, o item volta à posição original**. *(Atendido em 03/10/2026, issue #8: `TasksStore.transition` tira o item que deixa de casar com o filtro de estado ou de atraso e o reinsere no índice original em caso de falha; vale também para reabrir com o filtro "Concluídas". Coberto por `tasks.store.spec.ts`.)*
- [x] **CA-07** — Após o sucesso, a lista reflete a ordenação do servidor (a tarefa concluída aparece depois das pendentes).

### Reabrir

- [x] **CA-08** — Reabrir uma tarefa concluída volta o estado para "Pendente" (RN-TASK-09).
- [ ] **CA-09** — A data de conclusão deixa de ser exibida.
- [ ] **CA-10** — Reabrir uma tarefa com vencimento passado faz o selo "Atrasada" reaparecer.
- [x] **CA-11** — Falha na chamada reverte para "Concluída".
- [x] **CA-12** — O controle alterna corretamente: em tarefa pendente oferece concluir, em concluída oferece reabrir.

### Conflitos e erros

- [x] **CA-13** — Resposta **409** reverte o otimismo e exibe mensagem indicando que o estado mudou em outro lugar — não uma mensagem de falha genérica.
- [ ] **CA-14** — Resposta **404** exibe "Tarefa não encontrada" e remove o item da lista (RN-AUTZ-03).
- [x] **CA-15** — O 404 tem a mesma mensagem para tarefa alheia e tarefa inexistente — a tela não distingue.
- [x] **CA-16** — Erro de rede reverte o otimismo e exibe mensagem de conectividade.
- [x] **CA-17** — Cliques repetidos rápidos no mesmo item disparam **uma** requisição; o controle fica bloqueado enquanto ela está em voo.
- [ ] **CA-18** — Concluir duas tarefas diferentes em sequência rápida funciona: as duas requisições ocorrem e ambos os itens atualizam corretamente.

### Acessibilidade

- [x] **CA-19** — O controle tem rótulo acessível que identifica a tarefa (ex.: "Concluir: Comprar pão"), não apenas "Concluir".
- [x] **CA-20** — A mudança de estado é anunciada a leitor de tela.
- [ ] **CA-21** — O controle é acionável pelo teclado e o foco não se perde quando o item muda de posição ou sai da lista.
- [x] **CA-22** — O estado não é comunicado apenas por cor ou ícone: há texto.

## Testes obrigatórios

- Componente/integração (Testing Library): CA-01 a CA-22.
- **CA-05, CA-06 e CA-11 (rollback) são obrigatórios** — otimismo sem rollback testado é uma interface que mente sob falha.
- **CA-21 é obrigatório**: perder o foco quando o item sai da lista é uma quebra real de acessibilidade e passa despercebida em revisão visual.
- Concluir tarefa entra no E2E crítico de [FE-22](FE-22-testes-e2e.md).

## Decisões em aberto

- **FD-06** — Atualização otimista com rollback.

## Auditoria dos critérios (03/10/2026)

Critérios conferidos contra o código em 03/10/2026. Marcados: 15 de 22.

| CA | Situação | Evidência / motivo |
|---|---|---|
| CA-04 | em aberto | Sem teste de concluir tarefa atrasada. O patch otimista zera `isOverdue` e o reload traz o valor do servidor. |
| CA-06 | em aberto | Não implementado como descrito: com filtro "Pendentes", o item permanece na lista (marcado como concluído) até o reload silencioso; não sai imediatamente. Sem teste do cenário. |
| CA-09 | em aberto | O patch otimista zera `completedAt` e o template oculta a data, mas nenhum teste afirma que a data some após reabrir. |
| CA-10 | em aberto | Sem teste; o selo de atraso só volta com o `isOverdue` do reload (o patch otimista de `reopen` não o restaura). |
| CA-14 | em aberto | Visível ao usuário: em 404 o store remove o item e a mensagem "Tarefa não encontrada" fica atrelada a um item que já saiu da tela, então o usuário vê a linha sumir em silêncio. |
| CA-15 | atendido em outro lugar | O mapeador devolve a mesma mensagem fixa para todo 404 (`error-mapper.ts`), sem distinguir alheia de inexistente. |
| CA-16 | atendido em outro lugar | Por composição: rollback do store é indiferente ao tipo de erro (`tasks.store.spec.ts`), mensagem de rede em `error-mapper.spec.ts`, exibição na linha em `tasks-page.component.spec.ts`. Sem teste de rede específico em `complete`. |
| CA-18 | em aberto | O estado por id (`pendingIds`) permite paralelismo, mas não há teste de duas tarefas em sequência rápida. |
| CA-21 | em aberto | Teste obrigatório ausente e sem gestão de foco; `track task.id` preserva o nó em mudança de posição, mas, quando o item sai da lista (404), o foco se perde. |
