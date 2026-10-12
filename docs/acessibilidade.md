# Acessibilidade e responsividade do frontend (FE-21)

Registro da auditoria de 03/10/2026, ampliado em 04/10/2026. Referência prática: WCAG 2.1 AA (não
é certificação). Tudo que diz "automatizado" roda em `frontend/e2e/a11y.spec.ts` e
`frontend/e2e/flows.spec.ts` contra a stack real (nginx + build de produção + backend) — ver
`frontend/README.md` —, em Chromium, no desktop e no viewport de 360 px (`mobile-360`), salvo onde
indicado. **Nenhuma verificação desta página usou leitor de tela**: onde o texto diz "estrutural", a
prova é sobre o que o leitor consome (nome/descrição acessíveis, papéis, `aria-live`), não sobre o
que ele anuncia.

## O que foi auditado, e com qual ferramenta

Todas as varreduras "por tela" passam pelas mesmas 9 telas: cadastro, login e 404 (sem sessão);
lista (com tarefa pendente, **concluída** e **atrasada**), criar, editar, "tarefa não encontrada",
conta e trocar senha (autenticadas).

| Verificação | Ferramenta | Data | Resultado |
|---|---|---|---|
| Violações WCAG 2.0/2.1 A e AA em todas as telas, incluindo diálogo de remoção aberto (CA-01) | `axe-core` via `@axe-core/playwright`; falha em `critical`/`serious` | 03/10 | Zero críticas/sérias |
| Um `<h1>` por tela e sem salto de nível (CA-16) | contagem de `<h1>` + regras `page-has-heading-one` e `heading-order` do axe, nas 9 telas | 04/10 | Passa |
| Contraste de texto (`color-contrast` do axe) **nos temas claro e escuro** (CA-17; FE-04 CA-06; FE-15 CA-10) — telas acima com tarefa concluída e atrasada, cadastro com erros de campo visíveis e botão desabilitado, login com dicas de erro, trocar senha com erros, diálogo de remoção | `axe-core`, `page.emulateMedia({ colorScheme })` | 04/10 | Zero violações nos dois temas. Ver limites abaixo |
| Indicador de foco em **todo** focável por `Tab`, em cada tela, nos dois temas (CA-06; FE-04 CA-11): `:focus-visible` verdadeiro, `outline`/`box-shadow` computado diferente de `none`, e contraste do anel contra o fundo do elemento pai >= 3:1 | Playwright, `Tab` real + `getComputedStyle` | 04/10 | Passa (mais de 50 paradas por tema) |
| Ordem de tabulação = ordem visual, sem `tabindex` positivo (CA-05) | Playwright: sequência de `Tab` comparada às posições (`getBoundingClientRect`), tolerância de 8 px | 04/10 | Passa. É uma heurística geométrica: linha seguinte, ou mesma linha da esquerda para a direita |
| Sem armadilha de foco fora de diálogo; `Tab` alcança todo focável visível; `Shift+Tab` percorre a ordem inversa (CA-07) | Playwright: laço completo em cada tela, comparado ao conjunto de focáveis calculado do DOM | 04/10 | Passa. `<input type="date">` conta como um elemento (tem um `Tab` por segmento) |
| Fluxos só com teclado, sem `click` (CA-04; FE-08 CA-19, FE-11 CA-16, FE-12 CA-20, FE-17 CA-24, FE-18 CA-26): cadastro -> login -> sair; criar (campos, prioridade por digitação e **data digitada no `<input type="date">`**), concluir (`Space`), filtrar (setas no grupo de radio) e buscar, limpar filtros, editar, remover pelo diálogo; editar perfil, trocar senha, login com a nova, sair; excluir conta pelo diálogo; foco no primeiro campo inválido após falha | Playwright, `Tab`/`Shift+Tab`/`Enter`/`Space`/setas/digitação | 04/10 | Passa. O cadastro prévio dos testes de lista usa API (não é o alvo) |
| Skip link é o primeiro focável e funciona (CA-09); foco no `<h1>` na mudança de rota (CA-10, parte do foco); diálogo prende foco, `Esc` fecha e devolve o foco (CA-08) | Playwright | 03/10 | Passa |
| Sem rolagem horizontal, sem elemento fora da janela e sem conteúdo cortado a **360, 768, 1440 e 1920 px**; coluna de conteúdo (`.app-shell__content`/`.auth-layout__card`) <= `--content-max-width` (CA-18, CA-20; FE-04 CA-04) | Playwright, `scrollWidth`/`getBoundingClientRect`/`overflow` computado | 04/10 | Passa. "Linha excessivamente longa" é provada só pelo teto de largura da coluna, não por medição de caracteres por linha |
| Título de tarefa de 200 caracteres, sem e com espaços, a 360 px e 1280 px: sem rolagem horizontal e nada além da borda do item (FE-15 CA-04) | Playwright | 04/10 | Passa |
| Alvos de toque >= 44x44 px a 360 px em **todos** os interativos: botões, links, campos, `select`, e `<label>` de checkbox/radio; mais o skip link focado (CA-19) | Playwright, `getBoundingClientRect` | 04/10 | Passa depois da correção. Links em linha dentro de frase foram medidos e **isentados** (WCAG 2.5.8, alvo em linha): ver "Links em linha" |
| Zoom de **texto** a 200% (`font-size` da raiz = 200%, o que o zoom de texto do navegador faz), nas 9 telas, em 1280 px e em 360 px: sem corte, sem rolagem horizontal e ações principais da tela visíveis e não cobertas (`elementFromPoint`) (CA-21) | Playwright | 04/10 | Passa depois das correções. "Acionável" = visível, dentro da janela e recebendo o ponteiro; não se clicou em cada uma |
| Nome acessível em todo campo de formulário, nas 9 telas (CA-12) | Playwright `toHaveAccessibleName` | 04/10 | Passa (estrutural) |
| Campo inválido expõe `aria-invalid="true"` e a mensagem pela descrição acessível: cadastro (e-mail, senha), login (e-mail, senha), criar tarefa (título), trocar senha (senha atual, nova) (CA-12) | Playwright `toHaveAccessibleDescription` | 04/10 | Passa depois da correção do login (estrutural) |
| Erro em `role="alert"` com `aria-live="assertive"`; sucesso do cadastro, "Você saiu da sua conta.", aviso de data passada e "Tarefa concluída." em região `polite` (CA-11) | Playwright | 04/10 | Passa (estrutural: papel/atributo, não o anúncio) |
| Ação de item (concluir, editar, remover) com nome acessível contendo o título (CA-13) | Playwright `toHaveAccessibleName` | 04/10 | Passa (estrutural) |
| Contagem da lista em `role="status"` e texto que muda ao filtrar: "3 tarefas encontradas" -> "2 ... com os filtros aplicados" -> "1 tarefa encontrada ..." (CA-14) | Playwright | 04/10 | Passa (estrutural) |
| Prioridade, situação e "Atrasada" têm texto, não só cor/ícone (CA-15) | Playwright | 04/10 | Passa; ausência de informação só por cor **fora** destes três campos não foi varrida |
| Console limpo: nenhum `console.error`, `pageerror` nem evento `securitypolicyviolation` ao navegar pelas 9 telas e ao falhar um login (FE-01 CA-03; apoia BE-42 CA-08) | Playwright | 04/10 | Passa **contra o build de produção atrás do nginx**, não contra `npm start`. Respostas 4xx esperadas (401 do refresh sem cookie e do login errado, 404 de tarefa inexistente) são conferidas à parte; qualquer outro 4xx/5xx falha |
| `prefers-reduced-motion` (CA-22) | regra global em `src/styles.scss` | 03/10 | Existe; **sem** teste automatizado |

### Links em linha (CA-19)

A regra global de 44 px (`@media (max-width: 40rem), (pointer: coarse)` em `src/styles.scss`) vale
para tudo que não é link dentro de parágrafo (`a:not(p a)`). Links em linha dentro de frase
("Cadastre-se", "Entrar", "Entrar com essa conta") são isentos pelo critério
2.5.8 da WCAG 2.2 (alvo em linha no texto) e esticá-los quebraria a linha; o teste os mede, imprime
o tamanho e **não** os reprova.

Medido a 360 px em 04/10/2026, os únicos links isentos nas 9 telas foram "Cadastre-se" no login
(74x16 px) e "Entrar" no cadastro (37x16 px). Ambos têm 16 px de altura, abaixo até do mínimo de
24 px da 2.5.8; a isenção é pelo alvo estar em linha numa frase, não por o tamanho atender. O terceiro
caso do texto ("Entrar com essa conta", no aviso de e-mail já cadastrado) só aparece nesse erro e
não é varrido pelo teste.

### Limites do que "contraste" significa aqui

- O `axe` ignora elementos **desabilitados** (a WCAG isenta componentes inativos). O botão
  desabilitado do cadastro entra na tela auditada, mas seu contraste **não é medido**.
- Cobertos nos dois temas: texto em repouso, texto de erro (campo e aviso de formulário), "Atrasada",
  tarefa concluída (riscada), botões primários/secundários, diálogo aberto. **Não cobertos**: estados
  `:hover`, texto sobre `::selection`, a régua de prioridade (decorativa, `aria-hidden`), e uma
  planilha de todos os pares de tokens.
- O contraste do anel de foco usa a cor computada do `outline` contra o primeiro ancestral com fundo
  opaco (o contorno é desenhado fora do elemento).

## O que foi corrigido

| Achado | Gravidade (axe) / origem | Correção |
|---|---|---|
| `<li>` do item da lista dentro do elemento customizado `<app-task-item>`, fora de `<ul>`/`<ol>` | `listitem`, serious | O host `app-task-item` passou a ser o `role="listitem"`; o `<li>` interno virou `<div>` |
| Título de tarefa em `<h3>` logo abaixo do `<h1>` (salto de nível) | `heading-order`, moderate | `<h3>` -> `<h2>` (tamanho visual mantido por `font-size` explícito) |
| Skip link navegava para `/#main-content` (por causa do `<base href="/">`), recarregando a rota raiz em vez de focar o conteúdo | achado do teste de CA-09 | `click` intercepta e foca `#main-content` |
| Nenhuma gestão de foco na mudança de rota | achado do teste de CA-10 | `App` foca o `<h1>` (ou o `<main>`) após cada navegação que muda o caminho |
| Diálogo de confirmação abria sem levar o foco para dentro | achado do teste de CA-08 | foco via `afterNextRender` |
| Alvos de toque a 360 px: "Sair" 57x34, "Remover" 72x26, checkboxes/radios 20 px, botões de conta 19 px | achado do teste de CA-19 | regra global em `styles.scss`: `min-height`/`min-width` de 44 px em `button` e `label` de checkbox/radio |
| `<html lang="en">` num app em pt-BR | revisão de código | `lang="pt-BR"` |
| **CSS global não aplicado em produção** (skip link visível): o "inline critical CSS" do Angular usa `onload` inline, bloqueado pela CSP | achado do E2E (`console`) | `optimization.styles.inlineCritical: false` no `angular.json`. Ver `docs/seguranca-frontend.md` |
| **Login: mensagem de campo inválido não ligada ao campo** (`aria-invalid` sem `aria-describedby`; a dica "Informe um e-mail válido." existia só visualmente) | achado do teste de CA-12 (04/10) | `id` na dica e `aria-describedby` no campo, em `login.component.html` |
| **Alvos de toque de links e campos** a 360 px, que a auditoria de 03/10 não media: link de nome no cabeçalho 40 px, "Nova tarefa" 39 px, "Editar" 36x20, "Alterar senha" 19 px, "Voltar ao início" 23 px, campo de busca 29 px, skip link 41 px | achado do teste de CA-19 (04/10) | em `styles.scss`, `a:not(p a)` vira `inline-flex` com 44 px mínimos; `input` (exceto checkbox/radio) e `select` com `min-height` de 44 px |
| **Zoom de texto a 200% em 360 px**: rolagem horizontal de até 490 px; cabeçalho (e-mail + "Sair"), filtros da lista, ações do item, botões do formulário, e-mail na conta e busca saíam da tela | achado do teste de CA-21 (04/10) | `flex-wrap` no cabeçalho, nas ações do item e nas ações do formulário de tarefa; `max-width: 100%` nos grupos de filtro e em `input`/`select`/`textarea` (global); `min-width` do campo de busca com teto de 100%; `overflow-wrap: anywhere` no nome do cabeçalho e no e-mail da conta |

## O que ficou pendente — e por quê

Estes itens **não foram feitos** (nem verificados); nenhum resultado é afirmado.

- **Auditoria com leitor de tela (NVDA/VoiceOver)** — o que cada leitor **anuncia** (CA-10 anúncio
  efetivo, CA-11 a CA-14): exige pessoa e leitor de tela. Está verificado só o que o leitor consome
  (nomes, descrições, papéis, `aria-live`, texto da contagem), conforme a tabela. Se a região viva é
  de fato lida na hora certa e sem repetição só se descobre com leitor.
- **Foco ao anunciar mudança de rota** — o foco vai ao `<h1>`; o anúncio disso por leitor de tela
  não foi ouvido.
- **"Sair de todos os dispositivos"** e **paginação** por teclado: o fluxo de teclado cobre "Sair" e
  não essas duas ações.
- **Estados de contraste não varridos** — `:hover`, `::selection`, botão desabilitado (isento pela
  WCAG; ver acima) e planilha de todos os pares de tokens (CA-17 de ponta a ponta).
- **Informação só por cor/ícone fora de prioridade, situação e "Atrasada"** (CA-15).
- **Zoom do navegador (Ctrl +)**, que é reflow e não zoom de texto (WCAG 1.4.10): não testado. O
  teste cobre o zoom de **texto** a 200%.
- **`prefers-reduced-motion`** (CA-22): a regra global existe; sem teste automatizado nem verificação
  visual.
- **Integração do `axe` no CI** — CA-02: depende do workflow de CI (outra etapa).
- **Firefox/WebKit**: os projetos existem no Playwright (`npm run e2e:all`), mas a suíte de
  acessibilidade só foi executada em Chromium (desktop e viewport 360 px). `<input type="date">` e o
  comportamento de `Tab` em radio/segmentos de data variam entre navegadores.
