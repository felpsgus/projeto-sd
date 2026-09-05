# FE-16 — Filtros, busca e sincronia com a URL

| | |
|---|---|
| **Domínio** | Tarefas |
| **Depende de** | [FE-15](FE-15-listagem-paginacao.md) · backend: [BE-22](../backend/BE-22-listagem-tarefas.md) |
| **Bloqueia** | — |
| **Regras cobertas** | RN-LIST-02, RN-LIST-03, RN-LIST-04, RN-LIST-05 |
| **Estimativa** | G |

## Objetivo

O usuário filtra e busca as próprias tarefas, e o resultado fica refletido na URL — recarregar a página ou compartilhar o link preserva exatamente a mesma visão.

## Escopo

### Inclui

- Barra de filtros acima da lista:

  | Controle | Valores | Regra |
  |---|---|---|
  | Estado | Todas / Pendentes / Concluídas | RN-LIST-02 |
  | Prioridade | seleção múltipla: Baixa, Média, Alta | RN-LIST-03 |
  | Atrasadas | alternador (todas / só atrasadas) | RN-LIST-04 |
  | Busca | campo de texto, título e descrição | RN-LIST-05 |

- **Sincronia bidirecional com query params** (FD-08): `/tasks?status=pending&priority=high&overdue=true&search=relatório&page=2`.
  - alterar um filtro **navega** (atualizando a URL), e a navegação dispara a carga;
  - abrir a URL diretamente aplica os filtros e carrega o resultado;
  - a URL é a **única** fonte de verdade dos filtros — o componente não guarda uma cópia divergente.
- **Debounce** na busca (~300 ms) para não disparar uma requisição por tecla.
- Alterar qualquer filtro **volta para a página 1** — permanecer na página 7 de um resultado que agora tem 2 páginas mostraria uma lista vazia sem explicação.
- Indicação de filtros ativos e ação **"limpar filtros"**, visível apenas quando há algum ativo.
- Estado vazio específico: "nenhuma tarefa corresponde aos filtros", com ação de limpar — distinto de "você ainda não tem tarefas" (`isFilteredEmpty` de [FE-14](FE-14-servico-estado-tarefas.md)).
- Uso do histórico: alterar filtro usa `replaceUrl` para não entupir o botão "voltar" com cada tecla digitada; mudança de página usa navegação normal.

### Não inclui

- Ordenação escolhida pelo usuário — RN-LIST-06 define **a** ordenação; não há controle de ordem.
- Filtros salvos, visões nomeadas, favoritos.
- Busca com destaque do termo encontrado.

## Notas técnicas

- **A URL como fonte de verdade é o que faz o resto funcionar sem esforço:** botão voltar, recarregar, compartilhar link e deep-link saem de graça. Manter os filtros só em signal e "também" atualizar a URL cria dois estados que divergem — e o sintoma é o clássico "voltei a página e os filtros ficaram errados".
- **Debounce sem cancelamento não basta.** Digitar rápido gera respostas que podem chegar fora de ordem; a proteção está em [FE-14](FE-14-servico-estado-tarefas.md)/CA-08, e esta task precisa exercitá-la.
- Filtros ausentes **não** aparecem na URL — `/tasks?status=all&overdue=false&search=` é ruído. URL limpa quando não há filtro.
- Parâmetro inválido vindo da URL (`?status=xyz`) não pode quebrar a tela: ignorar o inválido e usar o padrão, sem exibir erro técnico. O backend responderia 400 ([BE-22](../backend/BE-22-listagem-tarefas.md)/CA-11), então o saneamento acontece antes da chamada.
- O `search` vai para a API como texto; o escape de `%` e `_` é responsabilidade do backend ([BE-22](../backend/BE-22-listagem-tarefas.md)/CA-15).

## Critérios de aceite

### Filtros

- [ ] **CA-01** — Filtrar por "Pendentes" mostra só pendentes; "Concluídas" só concluídas; "Todas" mostra ambas (RN-LIST-02).
- [ ] **CA-02** — Filtrar por prioridade Alta mostra só as de prioridade alta (RN-LIST-03).
- [ ] **CA-03** — Selecionar Baixa **e** Alta envia `priority=low&priority=high` e mostra as duas.
- [ ] **CA-04** — O alternador "Atrasadas" mostra apenas tarefas atrasadas (RN-LIST-04).
- [ ] **CA-05** — Os filtros combinam: estado + prioridade + atrasadas + busca aplicados simultaneamente.
- [ ] **CA-06** — "Limpar filtros" volta ao estado inicial e some quando não há filtro ativo.
- [ ] **CA-07** — Os filtros ativos são indicados visualmente.

### Busca

- [ ] **CA-08** — Buscar por um termo presente no **título** retorna a tarefa (RN-LIST-05).
- [ ] **CA-09** — Buscar por um termo presente na **descrição** retorna a tarefa.
- [ ] **CA-10** — Digitar "relatorio" rapidamente dispara **uma** requisição, não uma por tecla (debounce).
- [ ] **CA-11** — Digitação rápida seguida de resultado fora de ordem **não** deixa a lista com o resultado antigo.
- [ ] **CA-12** — Limpar o campo de busca remove o parâmetro da URL e recarrega a lista completa.
- [ ] **CA-13** — Busca com termo sem resultado exibe o estado vazio **de filtro**, com opção de limpar (não "crie sua primeira tarefa").

### URL

- [ ] **CA-14** — Alterar qualquer filtro atualiza a query string da URL (FD-08).
- [ ] **CA-15** — Abrir `/tasks?status=pending&priority=high` diretamente aplica os dois filtros e a lista já vem filtrada.
- [ ] **CA-16** — Recarregar a página (`F5`) preserva filtros, busca e página.
- [ ] **CA-17** — Filtros ausentes **não** aparecem na URL.
- [ ] **CA-18** — Parâmetro inválido (`?status=xyz`, `?page=abc`, `?page=-1`) é ignorado e a tela carrega com o padrão, sem erro técnico visível.
- [ ] **CA-19** — O botão "voltar" após digitar uma busca não exige N cliques para sair da tela (uso de `replaceUrl`).
- [ ] **CA-20** — Não existe estado de filtro duplicado fora da URL (verificado por revisão: o `query` do store é derivado dos parâmetros da rota).

### Paginação e interação

- [ ] **CA-21** — Alterar um filtro estando na página 3 volta para a página 1.
- [ ] **CA-22** — A paginação preserva os filtros ativos ao mudar de página.
- [ ] **CA-23** — Os controles de filtro têm labels associados e são operáveis só pelo teclado.
- [ ] **CA-24** — A mudança de resultado é anunciada a leitor de tela (`aria-live` com a contagem, ex.: "12 tarefas encontradas").
- [ ] **CA-25** — A barra de filtros é usável em 360 px, colapsando se necessário.

## Testes obrigatórios

- Componente (Testing Library) com roteador de teste: CA-01 a CA-09, CA-12, CA-13, CA-21 a CA-24.
- **CA-10, CA-11, CA-15, CA-16 e CA-18 são obrigatórios** — cobrem debounce, corrida, deep link, reload e entrada inválida, que é onde essa tela costuma quebrar.
- Filtro + busca entram no E2E de [FE-22](FE-22-testes-e2e.md).

## Decisões em aberto

- **FD-08** — Filtros na URL como query params.
