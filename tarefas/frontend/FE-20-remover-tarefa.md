# FE-20 — Remover tarefa

| | |
|---|---|
| **Domínio** | Tarefas |
| **Depende de** | [FE-15](FE-15-listagem-paginacao.md) · backend: [BE-21](../backend/BE-21-remover-tarefa.md) |
| **Bloqueia** | — |
| **Regras cobertas** | RN-TASK-12, RN-TASK-13, RN-AUTZ-02, RN-AUTZ-03 |
| **Estimativa** | P |

> **Recorte do T2 (21/09/2026):** fica fora do T2 — o backend do T2 não tem endpoint de remoção de tarefa.
>
> **Entrou em 23/09/2026:** o recorte acima foi revogado — o backend já expõe `DELETE /api/tasks/{id}` (BE-21), e esta task foi implementada em escopo pleno na terceira onda da Fase 1 (ver [PLANO-REGRAS-RESTANTES.md](../PLANO-REGRAS-RESTANTES.md)).

## Objetivo

O usuário remove uma tarefa sua com uma confirmação no caminho, e ela desaparece da lista.

## Escopo

### Inclui

- Ação "Remover" em cada item da lista.
- **Diálogo de confirmação** (`<app-confirm-dialog>` de [FE-04](FE-04-layout-design-base.md)) exibindo o **título da tarefa** e avisando que a remoção não pode ser desfeita (FD-15).
- `DELETE /api/tasks/{id}`. Sucesso (204) → o item some da lista e a página corrente é recarregada, para `totalItems` e a paginação ficarem corretos.
- Se a remoção esvaziar a página atual e existirem páginas anteriores, voltar uma página — em vez de exibir uma lista vazia.
- Tratamento de **404** (RN-AUTZ-03): mesma mensagem "Tarefa não encontrada"; o item é removido da lista de qualquer forma.
- Após remover, o limite de tarefas ativas ([FE-17](FE-17-criar-tarefa.md)) libera espaço — se o usuário estava no teto, a criação volta a funcionar.

### Não inclui

- "Desfazer" por toast — o backend faz soft delete (RN-TASK-13), mas **não expõe endpoint de restauração** ([BE-21](../backend/BE-21-remover-tarefa.md)). Oferecer "desfazer" sem endpoint seria uma promessa que a interface não pode cumprir.
- Remoção em lote.
- Lixeira / visualização de removidas — não há endpoint.

## Notas técnicas

- **O soft delete do backend é invisível aqui, e deve continuar assim.** Do ponto de vista do usuário a tarefa foi removida; contar que ela "fica guardada por 30 dias" criaria a expectativa de recuperá-la, o que não é possível nesta versão.
- **Por isso a confirmação não é opcional** (FD-15): sem "desfazer", o diálogo é a única proteção contra o clique errado.
- Exibir o título da tarefa no diálogo evita o erro clássico de remover o item vizinho — especialmente em telas pequenas, onde os alvos ficam próximos.
- Diferente de [FE-13](FE-13-exclusao-conta.md), aqui **não** se pede senha: o dano é limitado a um item e a fricção não se justifica.
- Ao remover o último item de uma página, recarregar a mesma página traria vazio; ajustar a página **antes** de recarregar.

## Critérios de aceite

### Confirmação

- [x] **CA-01** — A ação abre um diálogo; **nada** é removido com um clique só.
- [x] **CA-02** — O diálogo exibe o **título da tarefa** que será removida.
- [x] **CA-03** — O diálogo avisa que a remoção não pode ser desfeita.
- [x] **CA-04** — Cancelar e `Esc` fecham o diálogo sem remover, devolvendo o foco ao item de origem.
- [x] **CA-05** — O botão de confirmar usa cor de perigo e o diálogo prende o foco enquanto aberto.
- [x] **CA-06** — O diálogo **não** pede senha.

### Remoção

- [x] **CA-07** — Confirmar remove a tarefa e ela some da lista (RN-TASK-12).
- [x] **CA-08** — A tarefa removida **não** reaparece ao recarregar a página nem em nenhum filtro, inclusive "Todas" (RN-TASK-13).
- [x] **CA-09** — `totalItems` e o total de páginas são atualizados após a remoção.
- [x] **CA-10** — Remover o único item da página 3 leva o usuário à página 2, não a uma lista vazia.
- [x] **CA-11** — Remover o único item da página 1 exibe o estado vazio apropriado.
- [x] **CA-12** — Uma tarefa **concluída** também pode ser removida.
- [x] **CA-13** — Após remover estando no limite de tarefas ativas, criar uma nova volta a funcionar (RN-TASK-15).
- [x] **CA-14** — O botão de confirmar fica desabilitado durante a requisição; clique duplo dispara **uma** chamada.

### Erros

- [x] **CA-15** — Resposta **404** exibe "Tarefa não encontrada" e o item é retirado da lista. *(Atendido em 03/10/2026, issue #9: mesmo aviso da página usado por concluir e reabrir.)*
- [x] **CA-16** — O 404 tem a mesma mensagem para tarefa alheia e inexistente (RN-AUTZ-03).
- [x] **CA-17** — Erro de rede exibe mensagem e a tarefa **permanece** na lista — a interface não mente sobre o que aconteceu.
- [x] **CA-18** — Após um erro, tentar remover de novo funciona.

### Acessibilidade

- [x] **CA-19** — A ação tem rótulo acessível identificando a tarefa (ex.: "Remover: Comprar pão").
- [x] **CA-20** — Após a remoção, o foco vai para um destino previsível (item seguinte ou cabeçalho da lista), não se perde no `<body>`. *(Atendido em 03/10/2026, issue #10: foco no título do item que ocupou o índice, senão no anterior, senão no `<h1>`. Coberto por `tasks-page.component.spec.ts` em jsdom; não exercitado em navegador real.)*
- [x] **CA-21** — A remoção é anunciada a leitor de tela. *(Atendido em 03/10/2026, issue #11: região `aria-live="polite"` da página anuncia `Tarefa "<título>" removida.`. Coberto por `tasks-page.component.spec.ts`; não ouvido num leitor de tela real.)*
- [x] **CA-22** — Operável só pelo teclado; usável em 360 px.

## Testes obrigatórios

- Componente (Testing Library): CA-01 a CA-21.
- **CA-10 e CA-20 são obrigatórios** — a página vazia após remover o último item e o foco perdido são defeitos que só aparecem em uso real.
- **CA-17 é obrigatório**: remover otimisticamente sem confirmação do servidor deixaria a lista divergente após falha. Diferente de [FE-19](FE-19-concluir-reabrir.md), aqui **não** há otimismo — a remoção só reflete na tela após o 204.

## Decisões em aberto

- **FD-15** — Confirmação obrigatória antes de remover.

## Auditoria dos critérios (03/10/2026)

Critérios conferidos contra o código em 03/10/2026. Marcados: 18 de 22.

| CA | Situação | Evidência / motivo |
|---|---|---|
| CA-04 | atendido em outro lugar | Cancelar: `task-item.component.spec.ts`; Esc e devolução do foco: E2E `e2e/a11y.spec.ts` (foco preso, Esc, foco devolvido) e `confirm-dialog.component.spec.ts`. |
| CA-05 | atendido em outro lugar | Cor de perigo em `confirm-dialog.component.scss` (`--color-danger`); foco preso pelo E2E de acessibilidade. |
| CA-11 | atendido em outro lugar | Por composição: `dropItem` zera `items` e `isEmpty` já é testado em `tasks.store.spec.ts`; E2E 4 vê o estado vazio após recarregar. Sem teste do estado vazio logo após a remoção. |
| CA-12 | atendido em outro lugar | O botão "Remover" é renderizado independente do estado da tarefa (`task-item.component.html`); sem teste de tarefa concluída. |
| CA-13 | atendido em outro lugar | Regra do backend: `CreateTaskActiveLimitTests.cs` em `tests/TodoList.Tasks.IntegrationTests`. Não há fluxo do front/E2E. |
| CA-15 | em aberto | Em 404 o item sai da lista, mas a mensagem "Tarefa não encontrada" fica atrelada ao item removido e não aparece; o usuário vê a linha sumir em silêncio. |
| CA-18 | em aberto | Sem teste de nova tentativa após erro (o estado `pending` é limpo no erro, o que sugere que funciona). |
| CA-20 | em aberto | Não implementado, teste obrigatório ausente: depois do 204 o item sai do DOM e o `ConfirmDialog` tenta devolver o foco ao elemento de origem, que já não existe, então o foco cai no `<body>`. |
| CA-21 | em aberto | Sem anúncio explícito de remoção; só o resumo de contagem (`role=status`) muda, sem teste. |
| CA-22 | atendido em outro lugar | E2E `a11y.spec.ts` abre o diálogo por Enter e fecha por Esc; 360 px coberto em `/tasks`. Sem fluxo de confirmar por teclado. |
