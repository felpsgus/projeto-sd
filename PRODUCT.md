# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Usuário final: uma pessoa que se cadastra, entra e gerencia a **própria** lista de tarefas no dia
a dia (criar, editar, concluir, reabrir, remover, filtrar e buscar). É para ela que a interface é
otimizada.

Não há administrador nem colaboração: os únicos atores são **Visitante** (só cadastro e login) e
**Usuário** (só as próprias tarefas).

A apresentação do T2 (22/10/2026, laboratório, projetor) é uma circunstância de avaliação, não o
público primário. Em conflito, a experiência do usuário final vence.

## Product Purpose

TodoList pessoal com login básico: uma lista única e plana de tarefas por usuário, com título,
descrição, prioridade (Baixa/Média/Alta), vencimento opcional e estado (Pendente/Concluída).

Sucesso é o usuário manter a lista em dia sem atrito e sem surpresa: o que ele fez fica
persistido, o que deu errado é dito com clareza, e a sessão continua sem pedir a senha de novo
enquanto for válida.

## Positioning

Um todo-list simples feito com rigor de produção. O que um todo-list qualquer não pode afirmar
com verdade:

- acessibilidade WCAG 2.1 AA auditada por `axe-core` no CI, em tema claro e escuro, a 360 px;
- sessão com access token curto e refresh token rotativo de uso único;
- **fail-closed**: se o serviço de identidade está inalcançável, a operação falha com erro
  explícito em vez de degradar em silêncio.

A arquitetura distribuída (Gateway REST → gRPC → Identity/Tasks) é como o produto é construído,
não o que ele promete ao usuário.

## Operating Context

- Uso em navegador, de celular pequeno (360 px) a monitor largo e projetor.
- Stack: Angular 22 (signals, zoneless, OnPush) servido por nginx na mesma origem do Gateway. O
  frontend fala **só** com o Gateway.
- Interface em pt-BR (`lang="pt-BR"`); nome exibido: **TodoList**.
- Em ambiente de demo, um indicador discreto mostra o último status HTTP
  (`environment.showHttpStatusIndicator`). É recurso de demonstração, desligável.

## Capabilities and Constraints

Capacidades confirmadas (fonte normativa: [REGRAS-DE-NEGOCIO.md](REGRAS-DE-NEGOCIO.md)):

- Cadastro com e-mail e senha (mín. 8 caracteres, uma letra e um número); login; logout.
- Perfil: editar nome de exibição, trocar senha, excluir a conta (apaga as tarefas junto). O
  e-mail não é alterável.
- Tarefas: criar, editar, concluir, reabrir, remover (soft delete). Título 1–200 caracteres,
  descrição até 2000. Limite de 500 tarefas pendentes.
- **Atrasada** é condição derivada (Pendente com vencimento anterior a hoje), não um estado.
- Listagem paginada com filtros por estado, prioridade e atrasadas, e busca por texto. Ordem
  padrão: pendentes primeiro, depois vencimento crescente, depois criação.
- Login bloqueado por 15 minutos após 5 tentativas erradas; erro de credencial é sempre genérico.

Restrições:

- **Sem rede externa (fixo):** fontes e demais assets são servidos pelo próprio app. Nenhum CDN,
  fonte remota, script ou imagem de terceiros.
- O app não grava em `localStorage`/`sessionStorage` (FD-20); tema escuro só por
  `prefers-color-scheme`, sem alternância manual.

Fora do escopo desta versão — não desenhar para isto: compartilhamento, subtarefas, anexos,
comentários, notificações, recorrência, múltiplas listas/categorias, papéis administrativos,
login social, 2FA e "esqueci minha senha".

## Brand Commitments

Nome: **TodoList**. Não há logotipo, identidade de marca nem voz formalmente definidos.

## Evidence on Hand

- Regras de negócio rastreáveis (`RN-*`) em [REGRAS-DE-NEGOCIO.md](REGRAS-DE-NEGOCIO.md).
- Auditoria de acessibilidade em [docs/acessibilidade.md](docs/acessibilidade.md) e os critérios
  em [tarefas/frontend/FE-21-acessibilidade-responsividade.md](tarefas/frontend/FE-21-acessibilidade-responsividade.md).
- Suíte E2E com `axe-core` em [frontend/e2e/](frontend/e2e/).

Não existem — e não devem ser inventados: depoimentos, clientes, números de uso, benchmarks,
preço, capturas de marketing.

## Product Principles

1. **O usuário final vem primeiro.** A demo e a avaliação nunca justificam piorar o uso real.
2. **Rigor acima de recurso.** Poucas coisas, feitas direito; escopo novo não entra para enfeitar.
3. **Falhar às claras.** Erro é dito com precisão e sem vazar informação; nada degrada em silêncio.
4. **Autossuficiente.** O app funciona sem depender de nada fora da própria origem.
5. **Cada um vê só o que é seu.** Uma lista, um dono; a interface nunca sugere o contrário.

## Accessibility & Inclusion

Referência prática atual: **WCAG 2.1 AA** — contraste ≥ 4.5:1, todo fluxo completável por
teclado, foco movido a cada mudança de rota, alvos de toque ≥ 44 px em 360 px, zoom de texto a
200%, `prefers-reduced-motion` respeitado, nenhuma informação só por cor ou ícone. Violação
crítica ou séria do `axe-core` derruba o build.

Em aberto: a auditoria com leitor de tela (NVDA/VoiceOver) não foi feita (FE-21, CA-11 a CA-14).
