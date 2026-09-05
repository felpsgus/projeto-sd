# FE-20 — Remover tarefa

| | |
|---|---|
| **Domínio** | Tarefas |
| **Depende de** | [FE-15](FE-15-listagem-paginacao.md) · backend: [BE-21](../backend/BE-21-remover-tarefa.md) |
| **Bloqueia** | — |
| **Regras cobertas** | RN-TASK-12, RN-TASK-13, RN-AUTZ-02, RN-AUTZ-03 |
| **Estimativa** | P |

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

- [ ] **CA-01** — A ação abre um diálogo; **nada** é removido com um clique só.
- [ ] **CA-02** — O diálogo exibe o **título da tarefa** que será removida.
- [ ] **CA-03** — O diálogo avisa que a remoção não pode ser desfeita.
- [ ] **CA-04** — Cancelar e `Esc` fecham o diálogo sem remover, devolvendo o foco ao item de origem.
- [ ] **CA-05** — O botão de confirmar usa cor de perigo e o diálogo prende o foco enquanto aberto.
- [ ] **CA-06** — O diálogo **não** pede senha.

### Remoção

- [ ] **CA-07** — Confirmar remove a tarefa e ela some da lista (RN-TASK-12).
- [ ] **CA-08** — A tarefa removida **não** reaparece ao recarregar a página nem em nenhum filtro, inclusive "Todas" (RN-TASK-13).
- [ ] **CA-09** — `totalItems` e o total de páginas são atualizados após a remoção.
- [ ] **CA-10** — Remover o único item da página 3 leva o usuário à página 2, não a uma lista vazia.
- [ ] **CA-11** — Remover o único item da página 1 exibe o estado vazio apropriado.
- [ ] **CA-12** — Uma tarefa **concluída** também pode ser removida.
- [ ] **CA-13** — Após remover estando no limite de tarefas ativas, criar uma nova volta a funcionar (RN-TASK-15).
- [ ] **CA-14** — O botão de confirmar fica desabilitado durante a requisição; clique duplo dispara **uma** chamada.

### Erros

- [ ] **CA-15** — Resposta **404** exibe "Tarefa não encontrada" e o item é retirado da lista.
- [ ] **CA-16** — O 404 tem a mesma mensagem para tarefa alheia e inexistente (RN-AUTZ-03).
- [ ] **CA-17** — Erro de rede exibe mensagem e a tarefa **permanece** na lista — a interface não mente sobre o que aconteceu.
- [ ] **CA-18** — Após um erro, tentar remover de novo funciona.

### Acessibilidade

- [ ] **CA-19** — A ação tem rótulo acessível identificando a tarefa (ex.: "Remover: Comprar pão").
- [ ] **CA-20** — Após a remoção, o foco vai para um destino previsível (item seguinte ou cabeçalho da lista), não se perde no `<body>`.
- [ ] **CA-21** — A remoção é anunciada a leitor de tela.
- [ ] **CA-22** — Operável só pelo teclado; usável em 360 px.

## Testes obrigatórios

- Componente (Testing Library): CA-01 a CA-21.
- **CA-10 e CA-20 são obrigatórios** — a página vazia após remover o último item e o foco perdido são defeitos que só aparecem em uso real.
- **CA-17 é obrigatório**: remover otimisticamente sem confirmação do servidor deixaria a lista divergente após falha. Diferente de [FE-19](FE-19-concluir-reabrir.md), aqui **não** há otimismo — a remoção só reflete na tela após o 204.

## Decisões em aberto

- **FD-15** — Confirmação obrigatória antes de remover.
