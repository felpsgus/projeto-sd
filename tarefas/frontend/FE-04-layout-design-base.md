# FE-04 — Layout, design base e acessibilidade

| | |
|---|---|
| **Domínio** | UI |
| **Depende de** | [FE-01](FE-01-fundacao-workspace.md) |
| **Bloqueia** | todas as telas |
| **Regras cobertas** | nenhuma diretamente |
| **Estimativa** | M |

> **Recorte do T2 (21/09/2026):** entra **mínimo** — só o necessário para as três telas do recorte (login, lista de tarefas, criar tarefa): `AuthLayout`, `AppShell` simplificado (sem menu de conta com perfil, já que não há perfil no T2 — só "Sair"), tokens de estilo básicos, skip link e foco por navegação. `<app-confirm-dialog>` fica para depois (usado só por FE-13/FE-20, fora do T2). Sem dependência de backend.

## Objetivo

Existe um esqueleto visual consistente: dois layouts (público e autenticado), tokens de estilo, e um conjunto mínimo de componentes de apresentação que as features reaproveitam em vez de recriar.

## Escopo

### Inclui

- **Angular Material 22** instalado e configurado (FD-02), com tema próprio a partir dos tokens abaixo.
- **Tokens de estilo** em CSS custom properties: cores (superfície, texto, primária, perigo, sucesso, aviso), espaçamento, tipografia, raio de borda, sombras. **Nenhum valor de cor solto** em componente.
- **Layout público** (`AuthLayout`): usado por cadastro e login — cartão centralizado, sem navegação.
- **Layout autenticado** (`AppShell`): cabeçalho com nome do app, nome de exibição do usuário, menu de conta (perfil, sair) e área de conteúdo. Responsivo.
- **Skip link** ("pular para o conteúdo") como primeiro elemento focável.
- Landmarks semânticos: `<header>`, `<nav>`, `<main>`, `<footer>` — um `<main>` por página.
- Componentes de apresentação compartilhados, todos **standalone**, `OnPush`, sem injeção de serviço de dados:
  - `<app-page-header>` — título da página e ações;
  - `<app-confirm-dialog>` — confirmação genérica (usado por FE-13 e FE-20);
  - `<app-form-field-error>` — exibe `fieldErrors` de FE-03 junto ao input, com `aria-describedby`;
  - `<app-button>` (ou uso direto do Material) com estado de **carregando** e `aria-busy`.
- Título do documento (`<title>`) atualizado por rota.
- Foco movido para o `<h1>` (ou para o `<main>`) a cada navegação — sem isso, leitor de tela não percebe a troca de página numa SPA.

### Não inclui

- Tema escuro (FD-12) — os tokens ficam preparados, mas o tema não é entregue.
- Auditoria completa de acessibilidade e responsividade — é [FE-21](FE-21-acessibilidade-responsividade.md); aqui entra a base.
- Qualquer tela de feature.

## Notas técnicas

- **Angular Material foi escolhido (FD-02) principalmente pela acessibilidade que já vem pronta** em diálogo, menu, campo de formulário e foco. Reimplementar isso com CSS próprio custa mais do que parece e costuma sair errado.
- **Anúncio de navegação em SPA é um item que quase sempre é esquecido.** Sem mover o foco, quem usa leitor de tela clica em "Nova tarefa" e nada é anunciado. CA-09 existe por isso.
- Componentes de apresentação **não** injetam `HttpClient`, store ou router — recebem `input()` e emitem `output()` (convenção 2.2). Um `<app-confirm-dialog>` que sabe o que está confirmando já é um componente errado.
- Contraste mínimo **4.5:1** para texto normal, verificado ao definir os tokens — não depois.

## Critérios de aceite

- [x] **CA-01** — Rotas públicas (cadastro, login) usam o `AuthLayout`; rotas autenticadas usam o `AppShell`.
- [x] **CA-02** — O `AppShell` exibe o nome de exibição do usuário autenticado, vindo do estado de sessão.
- [x] **CA-03** — O layout é utilizável em 360 px de largura, sem rolagem horizontal.
- [x] **CA-04** — O layout é utilizável em 1920 px, sem linhas de texto excessivamente longas (largura máxima de conteúdo definida).
- [x] **CA-05** — Nenhum valor de cor literal (`#fff`, `rgb(...)`) existe fora do arquivo de tokens.
- [x] **CA-06** — Todo par texto/fundo do tema atinge contraste **≥ 4.5:1** (verificado com ferramenta e documentado no PR). *(04/10/2026: axe nos temas claro e escuro, registrado em [docs/acessibilidade.md](../../docs/acessibilidade.md); `:hover`, `::selection` e elementos desabilitados não são medidos.)*
- [x] **CA-07** — O skip link é o **primeiro** elemento focável e leva ao `<main>`.
- [x] **CA-08** — Cada página tem exatamente **um** `<h1>` e um `<main>`.
- [x] **CA-09** — Ao navegar entre rotas, o foco vai para o cabeçalho da nova página — verificado por teste automatizado, não só por inspeção.
- [x] **CA-10** — O `<title>` do documento muda a cada rota e descreve a página.
- [x] **CA-11** — Todo elemento interativo é alcançável e acionável **apenas pelo teclado**, com indicador de foco visível.
- [x] **CA-12** — O `<app-confirm-dialog>` prende o foco enquanto aberto, fecha com `Esc` e devolve o foco ao elemento que o abriu.
- [x] **CA-13** — `<app-form-field-error>` associa a mensagem ao input via `aria-describedby` e marca o campo com `aria-invalid`.
- [x] **CA-14** — Botão em estado de carregando fica desabilitado, marcado com `aria-busy`, e **não** dispara a ação duas vezes em clique duplo.
- [x] **CA-15** — Nenhum componente de apresentação injeta serviço de dados (verificado por revisão e lint).
- [x] **CA-16** — Todos os componentes desta task são standalone e `OnPush`.

## Testes obrigatórios

- Componente (Testing Library): `<app-confirm-dialog>` — CA-12, incluindo o retorno de foco.
- Componente: `<app-form-field-error>` — CA-13.
- Componente: botão com carregando — CA-14.
- Integração de roteamento: CA-09, CA-10.
- CA-06 é medição documentada no PR, não teste automatizado.

## Decisões em aberto

- **FD-02** — Angular Material. Padrão provisório.
- **FD-12** — Tema escuro fora do escopo.

## Auditoria dos critérios (03/10/2026)

Critérios conferidos contra o código em 03/10/2026. Marcados: 10 de 16.

| CA | Situação | Evidência / motivo |
|---|---|---|
| CA-04 | em aberto (não verificável por código) | `--content-max-width: 46rem` existe (`styles.scss`, usado em `app-shell.component.scss`), mas "utilizável em 1920 px" é inspeção visual; nenhum teste em viewport largo. |
| CA-06 | em aberto | O E2E roda `axe-core` (inclui contraste) só no tema claro; o tema escuro (`prefers-color-scheme`) nunca é emulado e não há medição documentada de contraste. |
| CA-08 | em aberto | Nenhum teste conta `<h1>`/`<main>` por página (o axe do E2E usa só tags WCAG, sem `page-has-heading-one`/`landmark-one-main`). Pela leitura do código cada layout tem um `<main>` e as telas um `<h1>`. |
| CA-10 | em aberto | As rotas declaram `title` em `app.routes.ts`, mas nenhum teste (Vitest ou E2E) verifica o `<title>` mudando por rota. |
| CA-11 | em aberto (não verificável por código) | Há `:focus-visible` global e um fluxo E2E só por teclado (login, criar, concluir), mas "todo elemento interativo" exige auditoria manual. |
| CA-12 | atendido em outro lugar | Foco preso, `Esc` e retorno do foco cobertos no E2E `a11y.spec.ts` ("diálogo prende o foco, fecha com Esc e devolve o foco"); o spec Vitest do diálogo cobre `Esc`, mas não o retorno de foco. |
| CA-13 | em aberto | `app-form-field-error` não tem spec; `aria-invalid` é testado (register/create-task), mas `aria-describedby` (ligado nos templates) não é asserido em nenhum teste. |
| CA-14 | atendido em outro lugar | Não há `<app-button>`; o padrão (disabled + `aria-busy` + guarda `submitting`) está em cada formulário, com teste de clique duplo em `register.component.spec.ts` e `change-password.component.spec.ts`. |

Observações: Angular Material (FD-02) não é usado (UI própria com tokens CSS); `<app-page-header>` e `<app-button>` do escopo não existem. FD-02 está efetivamente superada na implementação, sem registro em `DECISOES-PENDENTES.md`.
