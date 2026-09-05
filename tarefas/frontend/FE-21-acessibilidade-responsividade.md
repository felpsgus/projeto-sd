# FE-21 — Acessibilidade e responsividade

| | |
|---|---|
| **Domínio** | Qualidade |
| **Depende de** | [FE-04](FE-04-layout-design-base.md) (acompanha as features; fecha ao final) |
| **Bloqueia** | — |
| **Regras cobertas** | nenhuma de negócio |
| **Estimativa** | M |

## Objetivo

A aplicação inteira é utilizável por teclado, por leitor de tela e em telas pequenas — verificado por auditoria automatizada no CI, não por inspeção pontual.

> **Como executar esta task:** os critérios por tela vivem nas tasks de cada tela. Esta é a **auditoria transversal** que fecha o assunto e instala a rede de proteção automatizada. Ela não substitui os critérios individuais.

## Escopo

### Inclui

- **Auditoria automatizada** com `axe-core` integrado aos testes de componente e ao Playwright ([FE-22](FE-22-testes-e2e.md)), rodando em **todas** as telas: cadastro, login, lista, criar, editar, perfil, senha, 404.
- Violações de nível **crítico e sério** falham o build.
- Auditoria manual de teclado: percorrer cada fluxo sem usar o mouse.
- Auditoria de leitor de tela em ao menos um leitor (NVDA ou VoiceOver), com achados registrados.
- Verificação de contraste de **todos** os pares texto/fundo do tema, incluindo estados (foco, desabilitado, erro, item concluído).
- Verificação de responsividade em três larguras de referência: **360 px**, **768 px** e **1440 px**.
- Suporte a `prefers-reduced-motion`: animações e transições reduzidas quando solicitado.
- Zoom de texto até **200%** sem perda de conteúdo ou funcionalidade.
- Documento em `docs/acessibilidade.md` com o resultado da auditoria, o que ficou pendente e por quê.

### Não inclui

- Certificação formal de conformidade WCAG.
- Suporte a navegadores fora da matriz definida em [FE-23](FE-23-ci-build-seguranca.md).

## Notas técnicas

- **A meta é WCAG 2.1 nível AA** como referência prática, não como selo.
- **`axe-core` pega cerca de um terço dos problemas reais** — os automatizáveis (contraste, label ausente, `alt` faltando, ordem de cabeçalhos). Ordem de foco ilógica, rótulo que não descreve a ação e anúncio ausente em mudança dinâmica **não** são detectados por ferramenta. Por isso a auditoria manual não é opcional.
- Os pontos que mais falham numa SPA como esta, e que a auditoria precisa cobrir explicitamente: mudança de rota não anunciada, foco perdido após remover item de lista, diálogo que não devolve o foco, e mensagem de erro que aparece sem `aria-live`.
- 360 px é a largura de referência porque cobre a maioria dos aparelhos pequenos ainda em uso.

## Critérios de aceite

### Automatizado

- [ ] **CA-01** — `axe-core` roda em todas as telas listadas e **zero** violações críticas ou sérias permanecem.
- [ ] **CA-02** — A auditoria está integrada ao CI: uma violação nova **falha** o build — comprovado introduzindo uma violação uma vez.
- [ ] **CA-03** — Violações de nível moderado que não forem corrigidas estão registradas com justificativa em `docs/acessibilidade.md`.

### Teclado

- [ ] **CA-04** — Todo fluxo (cadastro, login, criar, editar, concluir, remover, filtrar, trocar senha, excluir conta, sair) é completável **apenas pelo teclado**.
- [ ] **CA-05** — A ordem de tabulação segue a ordem visual em todas as telas.
- [ ] **CA-06** — O indicador de foco é visível em **todos** os elementos interativos, com contraste suficiente.
- [ ] **CA-07** — Não existe armadilha de foco fora de diálogo modal.
- [ ] **CA-08** — Todo diálogo prende o foco, fecha com `Esc` e devolve o foco ao elemento de origem.
- [ ] **CA-09** — O skip link funciona e é o primeiro elemento focável.

### Leitor de tela

- [ ] **CA-10** — Mudança de rota é anunciada (foco no cabeçalho da nova página).
- [ ] **CA-11** — Mensagens de erro e sucesso são anunciadas com `aria-live` apropriado.
- [ ] **CA-12** — Cada campo de formulário é anunciado com seu label e, quando inválido, com a mensagem de erro.
- [ ] **CA-13** — Ações de item de lista são anunciadas identificando **qual** tarefa.
- [ ] **CA-14** — A lista anuncia a quantidade de itens; a mudança de resultado após filtrar é anunciada.
- [ ] **CA-15** — Nenhuma informação é transmitida **apenas** por cor ou ícone (prioridade, estado, atrasada).
- [ ] **CA-16** — Cada página tem um `<h1>` único e hierarquia de cabeçalhos sem saltos.

### Visual

- [ ] **CA-17** — Contraste ≥ **4.5:1** para texto normal e ≥ **3:1** para texto grande, em todos os estados.
- [ ] **CA-18** — Em 360 px, nenhuma tela tem rolagem horizontal.
- [ ] **CA-19** — Em 360 px, todos os alvos de toque têm ao menos 44×44 px.
- [ ] **CA-20** — Em 768 px e 1440 px, o layout se adapta sem conteúdo cortado nem linhas excessivamente longas.
- [ ] **CA-21** — Zoom de texto a 200% não corta conteúdo nem impede nenhuma ação.
- [ ] **CA-22** — Com `prefers-reduced-motion`, animações e transições são reduzidas ou eliminadas.

### Registro

- [ ] **CA-23** — `docs/acessibilidade.md` documenta o que foi auditado, com qual ferramenta/leitor, o que foi corrigido e o que ficou pendente.

## Testes obrigatórios

- `axe-core` em testes de componente e em E2E — CA-01, CA-02.
- Testes automatizados de foco: CA-08, CA-10 (reaproveitando o de [FE-04](FE-04-layout-design-base.md)) e o foco após remoção de item ([FE-20](FE-20-remover-tarefa.md)/CA-20).
- CA-04, CA-05 e a auditoria de leitor de tela são **manuais e documentadas** — a ausência do registro em `docs/acessibilidade.md` reprova a task.
