# FE-15 — Listagem e paginação

| | |
|---|---|
| **Domínio** | Tarefas |
| **Depende de** | [FE-14](FE-14-servico-estado-tarefas.md) · backend: [BE-22](../backend/BE-22-listagem-tarefas.md) |
| **Bloqueia** | FE-16 |
| **Regras cobertas** | RN-LIST-01, RN-LIST-06, RN-LIST-07, RN-TASK-14, RN-TASK-16 |
| **Estimativa** | G |

## Objetivo

A tela `/tasks` mostra as tarefas do usuário na ordem definida pelas regras, paginadas, com as tarefas atrasadas visivelmente sinalizadas.

## Escopo

### Inclui

- Rota `/tasks` como página inicial pós-login.
- **`<app-task-list>`** (container) consumindo `TasksStore`, e **`<app-task-item>`** (apresentação, recebe `input()` / emite `output()`).
- Cada item exibe:
  - título;
  - trecho da descrição, quando houver;
  - **prioridade** — com rótulo textual **e** cor (`Baixa` / `Média` / `Alta`), nunca só cor;
  - **data de vencimento** formatada em pt-BR, ou indicação de ausência;
  - **selo "Atrasada"** quando `isOverdue` (RN-TASK-16), com texto, não apenas cor;
  - **estado** (Pendente / Concluída), com tratamento visual distinto para concluídas;
  - data de última atualização (RN-TASK-14), em formato relativo ou absoluto legível;
  - ações: concluir/reabrir ([FE-19](FE-19-concluir-reabrir.md)), editar ([FE-18](FE-18-editar-tarefa.md)), remover ([FE-20](FE-20-remover-tarefa.md)).
- **Ordenação exibida é a do servidor** (RN-LIST-06): pendentes antes de concluídas, depois vencimento crescente com as sem vencimento por último, depois criação. O cliente **não reordena**.
- **Paginação clássica** (FD-10): controles de página, indicação de "página X de Y" e total de itens, com `pageSize` vindo do backend (padrão 20).
- Estados de tela: carregando (skeleton), vazio (com chamada para criar a primeira tarefa), erro (com "tentar novamente").
- Botão "Nova tarefa" em destaque.
- Semântica de lista: `<ul>`/`<li>` ou `role="list"`, para leitor de tela anunciar a quantidade de itens.

### Não inclui

- Filtros e busca — [FE-16](FE-16-filtros-busca-url.md).
- Mutações — FE-17 a FE-20.
- Rolagem infinita, seleção múltipla, arrastar para reordenar (a ordem é definida pela regra, não pelo usuário).

## Notas técnicas

- **O cliente não reordena nada.** A ordenação de RN-LIST-06 tem sutilezas (nulos por último, desempate por criação) que o backend já resolveu com desempate estável ([BE-22](../backend/BE-22-listagem-tarefas.md)/CA-22). Reordenar em memória divergiria da paginação e faria itens sumirem ou repetirem entre páginas.
- **`isOverdue` vem da API** (FD-09). O frontend informa a sua data local pelo header `X-Client-Date` ([FE-02](FE-02-contratos-camada-http.md), FD-17), e o backend calcula. Recalcular também no cliente criaria dois cálculos independentes que divergem — o selo diria uma coisa e o filtro `overdue` outra.
- **Prioridade e "atrasada" nunca são comunicadas só por cor** — falha para daltônicos e em impressão. Cor acompanha texto, nunca substitui.
- `dueDate` é string `yyyy-MM-dd` ([FE-02](FE-02-contratos-camada-http.md)); formatar direto para exibição, sem passar por `new Date()`, para não deslocar o dia.
- Item concluído com estilo distinto, mas o texto precisa continuar legível — riscado com contraste insuficiente é um problema de acessibilidade comum.

## Critérios de aceite

### Conteúdo

- [ ] **CA-01** — `/tasks` lista as tarefas do usuário autenticado (RN-LIST-01).
- [ ] **CA-02** — Cada item exibe título, prioridade, vencimento, estado e data de atualização.
- [ ] **CA-03** — Tarefa sem descrição ou sem vencimento é exibida sem campo vazio nem `null` na tela.
- [ ] **CA-04** — Título longo (200 caracteres) não quebra o layout.
- [ ] **CA-05** — A prioridade é exibida com **rótulo textual** em pt-BR, além da cor.
- [ ] **CA-06** — Tarefa com `isOverdue: true` exibe o selo **"Atrasada"** com texto (RN-TASK-16).
- [ ] **CA-07** — Tarefa concluída **não** exibe o selo de atrasada, mesmo com vencimento passado.
- [ ] **CA-08** — `isOverdue` é lido da resposta; não há cálculo de data no componente (verificado por revisão e ausência de `new Date()` na lógica de atraso).
- [ ] **CA-08b** — Com o navegador em `UTC−3` às `21:30` do dia 20 (UTC já no dia 21), uma tarefa pendente vencendo no dia **20** **não** exibe o selo "Atrasada" — o cenário que motivou a decisão do fuso (FD-17 / D-18), verificado ponta a ponta com o header sendo enviado.
- [ ] **CA-09** — A data de vencimento `2026-01-01` é exibida como **01/01/2026** mesmo em navegador com fuso `UTC-3`.
- [ ] **CA-10** — Tarefas concluídas têm tratamento visual distinto, mantendo contraste ≥ 4.5:1.

### Ordenação

- [ ] **CA-11** — A ordem exibida é **exatamente** a ordem retornada pela API; nenhum `sort` é aplicado no cliente (verificado por teste com resposta em ordem conhecida).
- [ ] **CA-12** — Com um conjunto cobrindo pendentes/concluídas, com/sem vencimento, a tela reproduz a sequência do servidor sem alteração.

### Paginação

- [ ] **CA-13** — Os controles exibem página atual, total de páginas e total de itens (RN-LIST-07).
- [ ] **CA-14** — Navegar entre páginas carrega os itens corretos, sem repetição nem omissão.
- [ ] **CA-15** — Na primeira página, "anterior" está desabilitado; na última, "próxima" está desabilitado.
- [ ] **CA-16** — Com uma única página, os controles são ocultados ou desabilitados, não exibidos como interativos inúteis.
- [ ] **CA-17** — O `pageSize` usado é o retornado pela API, não uma constante no frontend.
- [ ] **CA-18** — Mudar de página move o foco para o início da lista e anuncia a mudança a leitor de tela.

### Estados

- [ ] **CA-19** — Durante o carregamento, exibe indicador — não a lista vazia nem a mensagem de "nenhuma tarefa".
- [ ] **CA-20** — Usuário sem nenhuma tarefa vê estado vazio com chamada para criar a primeira.
- [ ] **CA-21** — Erro de carregamento exibe mensagem com "tentar novamente", e o botão refaz a chamada.
- [ ] **CA-22** — A lista tem semântica de lista, e a quantidade de itens é anunciada por leitor de tela.
- [ ] **CA-23** — Todas as ações de cada item são alcançáveis pelo teclado, com rótulo acessível que identifica **qual** tarefa (não apenas "Editar").
- [ ] **CA-24** — A tela é usável em 360 px: os itens se adaptam sem rolagem horizontal.
- [ ] **CA-25** — `<app-task-item>` é de apresentação pura: recebe por `input()`, emite por `output()`, não injeta o store.

## Testes obrigatórios

- Componente (Testing Library, consultando por texto e `role`): CA-01 a CA-24.
- **CA-09 e CA-11 são obrigatórios** — o deslocamento de fuso e a reordenação no cliente são os dois defeitos mais prováveis desta tela.
- A listagem entra no E2E crítico de [FE-22](FE-22-testes-e2e.md).

## Decisões em aberto

- **FD-09** — `isOverdue` vem da API.
- **FD-10** — Paginação clássica.
