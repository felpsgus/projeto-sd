# Acessibilidade e responsividade do frontend (FE-21)

Registro da auditoria de 03/10/2026. Referência prática: WCAG 2.1 AA (não é certificação).
Tudo abaixo que diz "automatizado" roda em `frontend/e2e/a11y.spec.ts` contra a stack real
(nginx + build de produção + backend) — ver `frontend/README.md`.

## O que foi auditado, e com qual ferramenta

| Verificação | Ferramenta | Resultado |
|---|---|---|
| Violações WCAG 2.0/2.1 A e AA em todas as telas: cadastro, login, lista vazia, lista com itens, criar, editar, "tarefa não encontrada", conta, trocar senha, 404 e diálogo de remoção aberto (CA-01) | `axe-core` via `@axe-core/playwright`; o teste falha em `critical` ou `serious` | Zero críticas/sérias ao final (ver correções) |
| Violações moderadas/menores (varredura única, incluindo regras `best-practice`) | mesma ferramenta, rodada avulsa | 1 achado moderado (`heading-order`), corrigido; nada pendente |
| Skip link é o primeiro focável e funciona (CA-09) | Playwright, teclado real | Passa (depois da correção) |
| Foco vai ao `<h1>` na mudança de rota (CA-10) | Playwright | Passa (depois da correção) |
| Diálogo: foco preso, `Esc` fecha, foco volta ao botão de origem (CA-08) | Playwright | Passa (depois da correção) |
| Sem rolagem horizontal a 360 px em login, cadastro, lista, criar, conta e trocar senha (CA-18) | Playwright, viewport 360 px | Passa |
| Alvos de toque >= 44 px a 360 px para botões e para o `<label>` de checkbox/radio (CA-19) | Playwright, medição de `boundingBox` | Passa (depois da correção). Links de texto em linha ("Editar", "Cadastre-se") **não** são medidos |
| Fluxo só por teclado: login -> criar tarefa -> concluir (CA-04, parcial) | Playwright, só `Tab`/`Enter`/`Space` | Passa |
| `prefers-reduced-motion` (CA-22) | regra global já existente em `src/styles.scss` | Existe; **não** há teste automatizado dela |

## O que foi corrigido

| Achado | Gravidade (axe) / origem | Correção |
|---|---|---|
| `<li>` do item da lista dentro do elemento customizado `<app-task-item>`, fora de `<ul>`/`<ol>` | `listitem`, serious | O host `app-task-item` passou a ser o `role="listitem"`; o `<li>` interno virou `<div>` |
| Título de tarefa em `<h3>` logo abaixo do `<h1>` (salto de nível) | `heading-order`, moderate | `<h3>` -> `<h2>` (tamanho visual mantido por `font-size` explícito) |
| Skip link navegava para `/#main-content` (por causa do `<base href="/">`), recarregando a rota raiz em vez de focar o conteúdo | achado do teste de CA-09 | `click` intercepta e foca `#main-content` |
| Nenhuma gestão de foco na mudança de rota (o `<main tabindex="-1">` existia, mas nada o focava) | achado do teste de CA-10 | `App` foca o `<h1>` (ou o `<main>`) após cada navegação que muda o caminho; carga inicial e mudança só de query string (filtros/busca) não mexem no foco |
| Diálogo de confirmação abria sem levar o foco para dentro (o foco era pedido num microtask, antes de o `@if` renderizar) — por isso `Esc` e o laço de `Tab` não funcionavam | achado do teste de CA-08 | foco via `afterNextRender` |
| Alvos de toque a 360 px: "Sair" 57x34, "Remover" 72x26, checkboxes/radios 20 px de altura, botões de conta 19 px | achado do teste de CA-19 | regra global em `styles.scss` (`@media (max-width: 40rem), (pointer: coarse)`): `min-height`/`min-width` de 44 px em `button` e `label` de checkbox/radio |
| `<html lang="en">` num app em pt-BR | revisão de código | `lang="pt-BR"` |
| **CSS global não aplicado em produção** (skip link visível, estilos de `body` ausentes): o build "inline critical CSS" do Angular carrega `styles.css` com `onload="this.media='all'"`, que a CSP (`script-src 'self'`) bloqueia | achado do E2E (`console`) | `optimization.styles.inlineCritical: false` no `angular.json`. Ver também `docs/seguranca-frontend.md` |

## O que ficou pendente — e por quê

Estes itens **não foram feitos** (nem verificados) nesta rodada; nenhum resultado é afirmado.

- **Auditoria com leitor de tela (NVDA/VoiceOver)** — CA-10 (anúncio efetivo), CA-11, CA-12, CA-13, CA-14: exige pessoa e leitor de tela; `axe` não cobre anúncio. Verificado só o que é estrutural (foco, `aria-live`/`role="alert"` presentes no código).
- **Auditoria manual completa de teclado** — CA-04 (todos os fluxos), CA-05 (ordem de tabulação = ordem visual), CA-06 (indicador de foco com contraste em todos os elementos), CA-07: automatizado apenas o fluxo login -> criar -> concluir e o diálogo.
- **Zoom de texto a 200%** — CA-21: não testado.
- **Contraste de todos os pares/estados** — CA-17: o `axe` verifica contraste dos estados renderizados nas telas visitadas (zero violações), mas não há planilha de todos os pares de tokens nem de estados raros (desabilitado, foco), nem o tema escuro foi auditado separadamente.
- **768 px e 1440 px** — CA-20: não verificados (só 360 px e o desktop padrão do Playwright, 1280 px).
- **Informação só por cor/ícone** (CA-15) e **contagem/anúncio ao filtrar** (CA-14): não auditados manualmente.
- **`prefers-reduced-motion`** (CA-22): a regra global existe; sem teste automatizado nem verificação visual.
- **Integração do `axe` no CI** — CA-02: depende do workflow de CI (outra etapa).
- **Firefox/WebKit**: os projetos existem no Playwright (`npm run e2e:all`), mas a suíte de acessibilidade só foi executada em Chromium (desktop e viewport 360 px).
