---
name: TodoList
description: Lista de tarefas pessoal — superfície calma, fios finos, um único acento.
colors:
  azul-tinta: "#1f3fd8"
  sobre-tinta: "#ffffff"
  papel: "#fbfbfa"
  folha: "#ffffff"
  grafite: "#14161a"
  grafite-suave: "#5b606b"
  fio: "#e3e4e8"
  perigo: "#b42318"
  sucesso: "#15653f"
  azul-tinta-escuro: "#8aa0ff"
  sobre-tinta-escuro: "#0b0d12"
  papel-escuro: "#101114"
  folha-escuro: "#16181c"
  grafite-escuro: "#e9eaee"
  grafite-suave-escuro: "#9aa0ac"
  fio-escuro: "#26282e"
  perigo-escuro: "#ff6b5e"
  sucesso-escuro: "#4ade80"
typography:
  headline:
    fontFamily: "'Instrument Sans Variable', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"
    fontSize: "clamp(1.625rem, 1.4rem + 1vw, 2rem)"
    fontWeight: 600
    lineHeight: 1.25
  title:
    fontFamily: "'Instrument Sans Variable', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"
    fontSize: "clamp(1.25rem, 1.15rem + 0.5vw, 1.5rem)"
    fontWeight: 600
    lineHeight: 1.25
  subtitle:
    fontFamily: "'Instrument Sans Variable', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"
    fontSize: "clamp(1.0625rem, 1rem + 0.3vw, 1.1875rem)"
    fontWeight: 500
    lineHeight: 1.25
  body:
    fontFamily: "'Instrument Sans Variable', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"
    fontSize: "clamp(0.9375rem, 0.9rem + 0.2vw, 1.0625rem)"
    fontWeight: 400
    lineHeight: 1.55
  label:
    fontFamily: "'Instrument Sans Variable', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"
    fontSize: "clamp(0.8125rem, 0.78rem + 0.16vw, 0.9375rem)"
    fontWeight: 500
    lineHeight: 1.55
rounded:
  sm: "0.5rem"
spacing:
  3xs: "0.25rem"
  2xs: "0.5rem"
  xs: "0.75rem"
  sm: "1rem"
  md: "1.5rem"
  lg: "2.5rem"
  xl: "4rem"
components:
  button-primary:
    backgroundColor: "{colors.azul-tinta}"
    textColor: "{colors.sobre-tinta}"
    rounded: "{rounded.sm}"
    padding: "0.75rem 1.5rem"
  button-secondary:
    backgroundColor: "transparent"
    textColor: "{colors.grafite}"
    rounded: "{rounded.sm}"
    padding: "0.75rem 1.5rem"
  button-danger:
    textColor: "{colors.perigo}"
    rounded: "{rounded.sm}"
    padding: "0.75rem 1.5rem"
  input:
    backgroundColor: "{colors.folha}"
    textColor: "{colors.grafite}"
    rounded: "{rounded.sm}"
    padding: "0.75rem 1rem"
  auth-card:
    backgroundColor: "{colors.folha}"
    rounded: "{rounded.sm}"
    padding: "2.5rem"
    width: "24rem"
  dialog:
    backgroundColor: "{colors.folha}"
    textColor: "{colors.grafite}"
    rounded: "{rounded.sm}"
    padding: "1.5rem"
    width: "26rem"
---

# Design System: TodoList

Fonte normativa dos valores: [frontend/src/styles.scss](frontend/src/styles.scss). As chaves
`*-escuro` são o mesmo token sob `prefers-color-scheme: dark`; no código cada par é uma única
custom property (`--color-accent`, `--color-bg`…).

## Overview

**Creative North Star: "A Bancada Limpa"**

Uma mesa de trabalho sem nada sobrando. Só a ferramenta em uso está à vista, e um único acento
indica onde agir. A interface é contida e exata: superfície calma, fios de 1 px, sombra quase
ausente, e a hierarquia vem de peso tipográfico e espaço — nunca de cartões empilhados.

A densidade é confortável, não compacta: coluna única de leitura, linhas de lista separadas por
fio, espaço generoso entre blocos. Cor é rara e sempre significa algo: azul é ação, vermelho é
atraso ou erro, e o resto é neutro levemente frio.

O sistema funciona sem nada de fora da própria origem. A fonte é servida pelo app, não há
ícones de biblioteca nem imagens, e o tema escuro segue o sistema operacional sem botão e sem
persistência.

**Key Characteristics:**
- Um único acento (Azul Tinta); todo o resto é neutro.
- Fios de 1 px no lugar de cartões e sombras.
- Um só raio (0.5rem) em tudo que tem canto.
- Uma só família tipográfica, hierarquia por peso (400/500/600).
- Transições de 120 ms, só em `border-color` e `opacity`.
- Tema claro e escuro por `prefers-color-scheme`.

## Colors

Neutros levemente frios e uma única tinta azul; vermelho e verde são reservados a significado.

### Primary
- **Azul Tinta** (`azul-tinta`): a única cor de ação. Preenche o botão primário, colore links e o
  anel de foco, e desenha a régua de prioridade da tarefa. No escuro vira um azul claro
  (`azul-tinta-escuro`) com texto escuro por cima.
- **Sobre Tinta** (`sobre-tinta`): texto sobre o Azul Tinta e sobre o indicador de erro.

### Neutral
- **Papel** (`papel`): fundo da página.
- **Folha** (`folha`): superfície de cabeçalho, cartão de autenticação, campos e diálogo — um
  passo acima do Papel.
- **Grafite** (`grafite`): texto principal.
- **Grafite Suave** (`grafite-suave`): rótulos, metadados, texto de apoio, tarefa concluída. É
  também a cor para a qual a borda escurece no hover.
- **Fio** (`fio`): toda borda e todo divisor.

### Semantic
- **Perigo** (`perigo`): erro de formulário, aviso de falha, vencimento em atraso e confirmação
  destrutiva.
- **Sucesso** (`sucesso`): confirmação positiva — avisos de tarefa criada, atualizada, concluída
  ou reaberta, e "Nome atualizado.".

Superfícies tingidas nunca são um hex novo: são `color-mix` do token com a Folha — 8% para o
acento, 10% para perigo e sucesso.

### Named Rules
**A Regra da Tinta Única.** Só existe um acento. Prioridade, seleção e ação usam o Azul Tinta
variando espessura e opacidade; nunca se introduz um segundo matiz para diferenciar.

**A Regra do Vermelho Reservado.** Vermelho só aparece para atraso e erro. Remover uma tarefa
não é perigo — o botão da linha é neutro; só a confirmação no diálogo usa a cor de perigo.

**A Regra do Arquivo Único.** Nenhum valor de cor literal existe fora de `styles.scss`.

## Typography

**Display/Body Font:** Instrument Sans Variable (com system-ui, -apple-system, Segoe UI, Roboto)

**Character:** Uma sans humanista de eixo variável, servida pelo próprio app em dois arquivos
`.woff2` (latin e latin-ext, para os acentos do pt-BR). Uma família só; a voz muda por peso.

### Hierarchy
- **Headline** (600, `headline`, 1.25): o `<h1>` único de cada página.
- **Title** (600, `title`, 1.25): `<h2>` de seção e título de diálogo.
- **Subtitle** (500, `subtitle`, 1.25): título da tarefa na lista, nome do app no cabeçalho e
  `<h3>` (este em 600).
- **Body** (400, `body`, 1.55): texto corrido, valores e campos.
- **Label** (500, `label`, 1.55): rótulos de campo em Grafite Suave; em 400 serve a metadados,
  dicas, contadores e mensagens de erro.

Todos os tamanhos são `clamp()` fluidos, para servir de celular a projetor. Existe ainda um
degrau `--font-size-2xl` definido e hoje sem uso.

### Named Rules
**A Regra dos Três Pesos.** Só 400, 500 e 600. Ênfase é peso, nunca itálico, caixa alta ou cor.

**A Regra dos Números Tabulares.** Datas e contadores usam `.tabular-nums`; prosa corrida, nunca.

## Layout

Coluna única centralizada com largura máxima de 46rem (`--content-max-width`); formulários de
tarefa param em 34rem, o cartão de autenticação em 24rem e o diálogo em 26rem. Não há grade nem
barra lateral.

O layout autenticado é um cabeçalho de uma linha (nome do app à esquerda, conta e "Sair" à
direita) sobre a área de conteúdo, com `2.5rem` de respiro vertical. O layout público centraliza
um único cartão na tela.

Ritmo: `0.25rem` entre rótulo e campo, `1.5rem` entre campos, `2.5rem` entre seções, `4rem` de
respiro em estados vazio, de erro e de carregamento.

A adaptação é por `flex-wrap`, não por breakpoints: cabeçalho, filtros e ações quebram de linha
sozinhos. O único breakpoint de layout é `22.5rem`, onde os filtros empilham. Em tela pequena
(`max-width: 40rem`) ou toque (`pointer: coarse`), botões, links, campos e rótulos de
checkbox/radio ganham alvo mínimo de 2.75rem (44 px).

## Elevation & Depth

Plano. Profundidade vem de camada tonal (Folha sobre Papel) e de fios de 1 px, não de sombra.

### Shadow Vocabulary
- **Diálogo** (`box-shadow: 0 12px 32px color-mix(in srgb, var(--color-text) 18%, transparent)`):
  a única sombra do sistema, sobre um véu de 92% da cor do texto.

### Named Rules
**A Regra do Plano.** Nada flutua em repouso. Sombra só existe no diálogo modal, que de fato
está acima da página.

## Shapes

Um só raio: cantos suavemente arredondados (0.5rem) em botões, campos, cartão e diálogo. Bordas
são sempre 1 px sólido na cor Fio. Linhas de lista não têm canto nem caixa — só um fio embaixo.

Duas formas recorrentes carregam significado:
- **Barra lateral de 2 px** à esquerda, com fundo tingido e sem raio: avisos em linha (erro em
  vermelho, contexto em azul).
- **Régua de prioridade**: traço vertical à esquerda da tarefa, raio de 1 px, que engrossa
  (2, 4 e 6 px) e escurece de Baixa a Alta, numa coluna de largura fixa.

Círculos só aparecem no spinner e no ponto de 6 px que marca atraso.

## Components

Contidos e exatos: nada se destaca sem motivo.

### Buttons
- **Shape:** cantos suaves (0.5rem), peso 500, sem caixa alta.
- **Primary:** fundo Azul Tinta, texto Sobre Tinta, sem borda (`0.75rem 1.5rem`). Hover baixa a
  opacidade para 0.92.
- **Secondary:** transparente, texto Grafite, borda Fio. No hover a borda escurece para Grafite
  Suave. Serve a Cancelar, Sair, Remover e paginação; versões compactas usam
  `0.5rem 1rem` ou `0.25rem 0.5rem`.
- **Danger:** texto e borda Perigo sobre superfície tingida de perigo — nunca preenchimento
  sólido. Na confirmação destrutiva do diálogo e no botão "Excluir minha conta", dentro da
  seção de perigo.
- **Link:** botão sem caixa, Azul Tinta sublinhado, para ações secundárias da conta.
- **Disabled / Loading:** opacidade 0.6 (0.5 na paginação), cursor `not-allowed`, `aria-busy`
  enquanto envia. O envio de formulário só fica desabilitado durante o envio; um envio inválido
  mostra os erros e foca o primeiro campo com problema.
- **Ordem:** Cancelar primeiro, ação primária depois.
- **Focus:** contorno de 2 px Azul Tinta com 2 px de afastamento, em qualquer elemento.

### Cards / Containers
- **Cartão de autenticação:** Folha, borda Fio, raio 0.5rem, `2.5rem` de padding, sem sombra. É
  o único cartão do sistema.
- **Seção de perigo:** superfície tingida de perigo, borda Perigo, `1.5rem` de padding — isola a
  exclusão de conta longe do "Salvar".

### Inputs / Fields
- **Style:** rótulo acima (Label, Grafite Suave), campo com fundo Folha, borda Fio, raio 0.5rem
  e padding `0.75rem 1rem`.
- **Hover:** borda escurece para Grafite Suave.
- **Error:** borda Perigo via `aria-invalid="true"`; mensagem em Perigo abaixo, ligada por
  `aria-describedby`.
- **Apoio:** dica e contador de caracteres em Label 400, Grafite Suave.

### Navigation
Cabeçalho único: fundo Folha, fio embaixo, nome do app em Subtitle 600. O nome do usuário é um
link discreto em Grafite Suave que escurece e sublinha no hover; "Sair" é um botão secundário
compacto. Sem menu, sem abas.

### Linha de tarefa (signature)
Sem caixa: régua de prioridade à esquerda, o checkbox de concluir logo depois dela (1.25rem, na
cor do acento, alinhado à primeira linha do título), título em Subtitle 500, vencimento à direita
só quando existe, descrição, e uma linha de apoio em Grafite Suave com a prioridade por extenso
("Prioridade alta") e, se concluída, "Concluída em …". Editar e Remover ficam embaixo. A linha não
repete o estado nem a data de atualização. Concluir ou reabrir é confirmado na página, com
"Desfazer". Concluída: título em Grafite Suave, riscado na mesma cor. Atrasada: vencimento em
Perigo, peso 600, precedido de um ponto cheio e do rótulo "Atrasada" — a informação nunca
depende só da cor. Em tela pequena o vencimento desce para baixo do título.

### Adicionar rápido
No topo da lista, acima dos filtros: um campo ("Adicionar tarefa…") e um botão secundário
"Adicionar". Enter cria a tarefa com os padrões, limpa o campo e mantém o foco nele. "Nova
tarefa" continua sendo o único botão primário e leva ao formulário completo.

### Avisos em linha
Barra de 2 px à esquerda, fundo tingido, texto pequeno. Vermelho para erro de formulário, de
ação ou de página; azul para contexto (por exemplo, o motivo de ter voltado ao login); verde para
confirmação ("Tarefa criada.", "Tarefa … concluída." com o botão de texto "Desfazer").

### Diálogo de confirmação
Folha sem borda, com a única sombra do sistema, sobre o véu. Título, mensagem em Grafite Suave,
ações à direita: Cancelar (secundário) e confirmar (Danger). É também o aviso de alterações não
salvas dos formulários de tarefa.

### Estados de lista
Vazio, erro e carregando compartilham a forma: fio em cima e `4rem` de respiro. Vazio e erro
são centralizados; carregando fica alinhado à esquerda. O spinner é um anel de 1.125rem com o
topo em Azul Tinta, parado sob `prefers-reduced-motion`. O bloco de carregando só aparece na
primeira carga: depois, a lista anterior fica visível a 60% de opacidade enquanto a nova chega.

### Indicador de status HTTP
Pílula mostrando método, caminho e status da última requisição; fundo escuro invertido, ou
Perigo quando a resposta é erro. Fixa no canto inferior direito a partir de 40rem; abaixo disso
fica no fluxo, ao fim da página. Recurso de demonstração, desligado no build de produção.

## Do's and Don'ts

### Do:
- **Do** usar só custom properties de `styles.scss` para cor, espaço, raio e tipografia.
- **Do** derivar superfícies tingidas com `color-mix` sobre `--color-surface` (8% acento, 10%
  perigo/sucesso).
- **Do** separar itens e blocos com fio de 1 px na cor Fio.
- **Do** manter um único botão primário por tela.
- **Do** acompanhar toda cor com texto ou forma (rótulo "Atrasada", espessura da régua).
- **Do** limitar transições a `border-color` e `opacity`, em 120 ms.
- **Do** servir fontes e assets pela própria origem.
- **Do** conferir contraste ≥ 4.5:1 nos dois temas ao tocar em qualquer token de cor.

### Don't:
- **Don't** escrever cor literal (`#hex`, `rgb()`) fora de `styles.scss`.
- **Don't** introduzir um segundo acento nem cores de semáforo para prioridade.
- **Don't** usar vermelho em ação que não seja erro, atraso ou confirmação destrutiva.
- **Don't** envolver linhas de lista ou seções em cartões com sombra.
- **Don't** adicionar sombra fora do diálogo modal.
- **Don't** preencher botão destrutivo com vermelho sólido.
- **Don't** carregar fonte, ícone, script ou imagem de CDN.
- **Don't** criar alternância manual de tema nem gravar preferência em `localStorage`.
- **Don't** remover `outline` de foco sem substituto visível.
