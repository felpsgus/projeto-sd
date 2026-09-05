# FE-23 — CI, cobertura, build e segurança

| | |
|---|---|
| **Domínio** | Qualidade |
| **Depende de** | [FE-01](FE-01-fundacao-workspace.md) (o esqueleto entra cedo; a task fecha ao final) |
| **Bloqueia** | o merge de todas as demais |
| **Regras cobertas** | nenhuma de negócio — implementa as seções 4, 5 e 6 de `CONVENCOES-CODIGO.md` |
| **Estimativa** | M |

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

- [ ] **CA-01** — Um PR com erro de compilação **falha** o pipeline.
- [ ] **CA-02** — Um PR com violação de lint ou formatação **falha**.
- [ ] **CA-03** — Um PR com teste quebrado **falha**.
- [ ] **CA-04** — O pipeline usa `npm ci`; nenhuma etapa usa `npm install`.
- [ ] **CA-05** — A versão do Node no CI casa com o `.nvmrc`.
- [ ] **CA-06** — O pipeline completo executa em tempo aceitável (alvo: < 10 min), documentado no PR.

### Cobertura

- [ ] **CA-07** — O relatório é publicado como artefato e o resumo aparece no PR.
- [ ] **CA-08** — Um PR que derruba a cobertura global abaixo de **75%** **falha** — comprovado com um PR de teste.
- [ ] **CA-09** — Um PR que derruba serviços/estado abaixo de **80%** **falha**.
- [ ] **CA-10** — Queda em relação ao baseline é sinalizada no PR mesmo acima do piso.
- [ ] **CA-11** — As exclusões estão declaradas e visíveis no relatório, não inflando o número em silêncio.

### Build

- [ ] **CA-12** — O build de produção conclui sem warnings e gera artefato publicável.
- [ ] **CA-13** — Estourar o `budget` de bundle **falha** o build — comprovado uma vez.
- [ ] **CA-14** — O relatório de build confirma que as features estão em chunks separados do bundle inicial.

### Segurança

- [ ] **CA-15** — **Teste antivazamento**: nenhuma senha ou token aparece em `localStorage`, `sessionStorage`, URL, `console` ou DOM em nenhum dos fluxos exercitados (RN-AUTH-05, RN-AUTH-20). **Este teste é o entregável central da task.**
- [ ] **CA-16** — Uma dependência com vulnerabilidade de severidade alta **falha** o build — comprovado adicionando um pacote vulnerável temporariamente.
- [ ] **CA-17** — Um segredo commitado no diff **falha** o build — comprovado com um segredo falso.
- [ ] **CA-18** — Nenhum segredo existe em `environments/` (verificado por varredura).
- [ ] **CA-19** — Os sourcemaps de produção **não** são publicados junto com o bundle.
- [ ] **CA-20** — Os cabeçalhos de segurança recomendados estão especificados em `docs/seguranca-frontend.md`, com a CSP proposta.
- [ ] **CA-21** — O build de produção não emite `console.log` de payload de request ou de estado.

### Documentação

- [ ] **CA-22** — `.browserslistrc` declara a matriz de navegadores suportados, coerente com a de [FE-22](FE-22-testes-e2e.md).
- [ ] **CA-23** — O `README.md` do frontend permite a uma pessoa nova instalar, rodar, testar e gerar o build de produção seguindo apenas o que está escrito.
- [ ] **CA-24** — Nenhum teste está `skip` sem justificativa escrita, e não há teste flaky conhecido em aberto ao fechar a task.

## Testes obrigatórios

- CA-15 é teste automatizado real, não revisão manual.
- Os gates (CA-01 a CA-03, CA-08, CA-09, CA-13, CA-16, CA-17) são validados **provocando a falha** uma vez, em PR descartável — um gate nunca exercitado é um gate que não funciona.
