# FE-23 — CI, cobertura, build e segurança

| | |
|---|---|
| **Domínio** | Qualidade |
| **Depende de** | [FE-01](FE-01-fundacao-workspace.md) (o esqueleto entra cedo; a task fecha ao final) |
| **Bloqueia** | o merge de todas as demais |
| **Regras cobertas** | nenhuma de negócio — implementa as seções 4, 5 e 6 de `CONVENCOES-CODIGO.md` |
| **Estimativa** | M |

> **Recorte do T2 (21/09/2026):** entra **só o build de produção**: `lint` → `format:check` → `test` → `build`. **Fica para depois:** pipeline de CI completo, gates de cobertura (75%/80%), varredura de dependências (`npm audit`), varredura de segredos, teste antivazamento automatizado, `budgets` de bundle, cabeçalhos de segurança documentados. Sem dependência de backend.

## Objetivo

Todo PR do frontend passa por um pipeline que compila, verifica lint e formatação, roda os testes, mede cobertura contra os pisos definidos e varre dependências vulneráveis — e o build de produção é publicável.

> **Como executar esta task:** o pipeline mínimo (build + testes) entra logo após FE-01; os gates são ligados progressivamente. A task só fecha quando todos os critérios estiverem ativos e **falhando o build** quando devem falhar.

## Escopo

### Inclui

**CI**

- Job por PR: `npm ci` → `lint` → `format:check` → `test` (Vitest com cobertura) → `build` de produção → E2E ([FE-22](FE-22-testes-e2e.md), Chromium) → varredura de dependências.
- **`npm ci` obrigatório** — nunca `npm install` no pipeline (convenção 1).
- Node fixado pelo `.nvmrc`, coerente entre CI e desenvolvimento.
- Cache de dependências para manter o pipeline rápido.

**Cobertura**

- Cobertura nativa do Vitest (V8/Istanbul), publicada como artefato e resumida no PR.
- Gates (seção 5 das convenções):
  - **≥ 75%** global;
  - **≥ 80%** em serviços e lógica de estado (`core/`, `*/data/`, stores, guards, interceptors);
  - falha em **queda em relação ao baseline** da branch principal sem justificativa.
- Exclusões declaradas: `main.ts`, `app.config.ts`, arquivos de environment, modelos/DTOs sem lógica, arquivos de rota puramente declarativos.

**Build**

- Build de produção com otimização, `budgets` configurados no `angular.json` — estourar o orçamento **falha** o build.
- Verificação de que as features estão em chunks separados ([FE-07](FE-07-roteamento-guards.md)/CA-11).
- `sourceMap` de produção gerado mas **não publicado** junto com o bundle.
- Matriz de navegadores suportados declarada em `.browserslistrc`.

**Segurança**

- `npm audit --audit-level=high` falhando o build.
- Varredura de segredos no diff (gitleaks ou equivalente).
- Verificação de que `environments/` não contém segredo.
- **Cabeçalhos de segurança** documentados para o servidor que hospedará o app: `Content-Security-Policy`, `X-Content-Type-Options`, `Referrer-Policy`, `X-Frame-Options`.
- **Teste automatizado antivazamento**: exercita cadastro, login, troca de senha e exclusão de conta, e afirma que nenhuma senha ou token aparece em `localStorage`, `sessionStorage`, URL, `console` ou DOM (RN-AUTH-05, RN-AUTH-20).

### Não inclui

- Deploy e infraestrutura de hospedagem.
- Monitoramento de erros em produção (Sentry e similares) — task futura.

## Notas técnicas

- **O teste antivazamento (CA-15) é o critério de maior valor desta task.** Ele é a única verificação automatizada de RN-AUTH-05 e RN-AUTH-20 no cliente, e cobre um erro que passa por qualquer revisão de código: um `console.log(form.value)` esquecido, um campo em `localStorage` "só para debug".
- **Orçamento de bundle não é firula:** sem `budgets`, o bundle cresce sem que ninguém perceba até o app ficar lento em rede móvel. Definir o teto agora, quando o app é pequeno, e ajustá-lo conscientemente.
- **Cobertura é diagnóstico, não meta** (seção 5 das convenções). Não perseguir 100%; usar o relatório para achar lógica de estado sem teste — que é onde os bugs realmente moram.
- Publicar sourcemap junto com o bundle entrega o código-fonte legível a qualquer visitante. Gerar para o rastreamento de erro, guardar em outro lugar.
- CSP precisa ser definida junto com quem hospeda; o entregável aqui é a **especificação documentada**, não a configuração do servidor.

## Critérios de aceite

### Pipeline

- [x] **CA-01** — Um PR com erro de compilação **falha** o pipeline. *(PR descartável de 06/10/2026, #24, run 37540970999: erro de tipo num arquivo de `src/app` derrubou o passo `npm run build`; lint e testes passaram, porque nenhum spec importava o arquivo.)*
- [x] **CA-02** — Um PR com violação de lint ou formatação **falha**. *(PR descartável de 06/10/2026, #18, run 37540904718: `console.log` derrubou `npm run lint` pela regra `no-console`. Formatação: o run 37533506502 do PR #17 falhou em `format:check` por um spec fora do Prettier.)*
- [x] **CA-03** — Um PR com teste quebrado **falha**. *(PR descartável de 06/10/2026, #19, run 37540911839: asserção invertida em `error-mapper.spec.ts` derrubou `npm run test:coverage`.)*
- [x] **CA-04** — O pipeline usa `npm ci`; nenhuma etapa usa `npm install`.
- [x] **CA-05** — A versão do Node no CI casa com o `.nvmrc`.
- [x] **CA-06** — O pipeline completo executa em tempo aceitável (alvo: < 10 min), documentado no PR. *(run 37536466701 do PR #17, 06/10/2026: job `frontend` em 49s; pipeline inteiro em 5m46s.)*

### Cobertura

- [x] **CA-07** — O relatório é publicado como artefato e o resumo aparece no PR. *(run 37536466701 do PR #17, 06/10/2026: artefato `frontend-coverage` publicado e resumo gravado no *Job summary* do run; não há comentário no PR.)*
- [x] **CA-08** — Um PR que derruba a cobertura global abaixo de **75%** **falha** — comprovado com um PR de teste. *(PR descartável de 06/10/2026, #20, run 37540920139: `Coverage for lines (68.63%) does not meet global threshold (75%)`.)*
- [x] **CA-09** — Um PR que derruba serviços/estado abaixo de **80%** **falha**. *(PR descartável de 06/10/2026, #21, run 37540926383: `Coverage for lines (74.19%) does not meet "src/app/core/**/*.ts" threshold (80%)`, com o global ainda em 87%.)*
- [x] **CA-10** — Queda em relação ao baseline é sinalizada no PR mesmo acima do piso. *(PR descartável de 06/10/2026, #22, run 37540932508: o pipeline passou com a anotação `Cobertura de linhas do frontend caiu: 93.05% < baseline 93.81%`.)*
- [ ] **CA-11** — As exclusões estão declaradas e visíveis no relatório, não inflando o número em silêncio.

### Build

- [x] **CA-12** — O build de produção conclui sem warnings e gera artefato publicável.
- [x] **CA-13** — Estourar o `budget` de bundle **falha** o build — comprovado uma vez.
- [x] **CA-14** — O relatório de build confirma que as features estão em chunks separados do bundle inicial.

### Segurança

- [x] **CA-15** — **Teste antivazamento**: nenhuma senha ou token aparece em `localStorage`, `sessionStorage`, URL, `console` ou DOM em nenhum dos fluxos exercitados (RN-AUTH-05, RN-AUTH-20). **Este teste é o entregável central da task.**
- [x] **CA-16** — Uma dependência com vulnerabilidade de severidade alta **falha** o build — comprovado adicionando um pacote vulnerável temporariamente. *(PR descartável de 06/10/2026, #23, run 37540940187: `lodash 4.17.20` derrubou `npm audit --audit-level=high` (GHSA-35jh-r3h4-6jhm).)*
- [x] **CA-17** — Um segredo commitado no diff **falha** o build — comprovado com um segredo falso. *(PR descartável de 06/10/2026, #24, run 37540970999: o job `secrets` falhou com a chave inventada (gitleaks, `leaks found: 1`).)*
- [x] **CA-18** — Nenhum segredo existe em `environments/` (verificado por varredura).
- [x] **CA-19** — Os sourcemaps de produção **não** são publicados junto com o bundle.
- [x] **CA-20** — Os cabeçalhos de segurança recomendados estão especificados em `docs/seguranca-frontend.md`, com a CSP proposta.
- [x] **CA-21** — O build de produção não emite `console.log` de payload de request ou de estado. *(06/10/2026: regra `no-console` no ESLint, permitindo só `warn`/`error`; o gate de lint do CI cobre. Provado com um `console.log` temporário que fez `npm run lint` falhar.)*

### Documentação

- [x] **CA-22** — `.browserslistrc` declara a matriz de navegadores suportados, coerente com a de [FE-22](FE-22-testes-e2e.md).
- [x] **CA-23** — O `README.md` do frontend permite a uma pessoa nova instalar, rodar, testar e gerar o build de produção seguindo apenas o que está escrito.
- [x] **CA-24** — Nenhum teste está `skip` sem justificativa escrita, e não há teste flaky conhecido em aberto ao fechar a task.

## Testes obrigatórios

- CA-15 é teste automatizado real, não revisão manual.
- Os gates (CA-01 a CA-03, CA-08, CA-09, CA-13, CA-16, CA-17) são validados **provocando a falha** uma vez, em PR descartável — um gate nunca exercitado é um gate que não funciona.

## Nota de 03/10/2026 — entregas locais (sem o workflow de CI)

- **Cobertura:** gates em `frontend/vitest.config.ts` (via `runnerConfig`) + `coverageInclude/Exclude` no `angular.json`; `npm run test:coverage` falha abaixo do piso (provado localmente subindo os pisos). Atual: linhas 90,8% global; `core/**` 98,1%. Os CAs de CI (CA-01..11) ficam abertos: o relatório como artefato/resumo no PR, o gate comprovado em PR descartável e a comparação com baseline dependem do workflow.
- **Build:** bundle inicial 294,8 kB; orçamento aviso 320 kB / erro 400 kB (estouro comprovado baixando o teto temporariamente, revertido); nenhum `.map` em `dist/`; `.browserslistrc` (últimas 2 versões de Chrome/Edge/Firefox/Safari/iOS).
- **Segurança:** `docs/seguranca-frontend.md`; `X-Frame-Options` acrescentado ao nginx (CSP, `nosniff` e `Referrer-Policy` já existiam). `npm audit --audit-level=high` **falha**: `@angular/router` (alta, só SSR) e `piscina` (crítica, via `@angular/build`, só build) — correção exige atualizar o Angular; **não aplicada**. CA-16 (gate de audit provado no CI), CA-17 (varredura de segredos), CA-18 (varredura de `environments/`) e CA-21 (sem `console.log` de payload no bundle) **não verificados** — CI/varredura pendentes.
- Teste antivazamento: `frontend/e2e/leak.spec.ts` (passa em Chromium). Firefox/WebKit não executados.

## Nota de execução — 03/10/2026 (CI)

workflow escrito e cada passo executado localmente; falta a primeira execução real no GitHub e a prova de falha em PR descartável.

- Marcados: CA-04 (workflow só usa `npm ci`), CA-05 (`node-version-file: frontend/.nvmrc`), CA-18 (sem segredo em `environments/`; gitleaks sobre o histórico limpo).
- Em aberto, exigem execução no GitHub: CA-01 a CA-03, CA-06, CA-07, CA-08, CA-09, CA-16, CA-17. `npm audit --audit-level=high` passa localmente depois de subir o Angular para 22.2.1 (corrige o DoS do `@angular/router` e o `piscina` crítico).
- **Pendente, não implementado:** CA-10 (queda de cobertura em relação ao baseline). CA-11 e CA-21 seguem como estavam.
