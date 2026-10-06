# Frontend — TodoList

Angular 22, standalone, **zoneless**, signals, TypeScript estrito. Cadastro, login com sessão
por refresh token (cookie HttpOnly), tarefas (listar/filtrar/criar/editar/concluir/remover) e
conta (perfil, troca de senha, exclusão), falando só com o API Gateway pela mesma origem.
Plano e decisões: [`tarefas/frontend/README.md`](../tarefas/frontend/README.md) e
[`tarefas/frontend/DECISOES-PENDENTES.md`](../tarefas/frontend/DECISOES-PENDENTES.md).

## Pré-requisitos

- **Node 24.21.0** (fixado em [`.nvmrc`](.nvmrc); Node 22 LTS também funciona) e **npm >= 10**.
- Para o E2E: **Docker** com Compose e **PowerShell 7+** (`pwsh`, só para gerar as chaves JWT e o SQL
  das migrations na primeira vez). Para dev com backend real fora do Docker, ver o README da raiz.

## Instalar

```bash
npm ci
npx playwright install chromium   # só para o E2E (firefox/webkit: veja "E2E")
```

## Rodar em desenvolvimento

```bash
npm start
```

Abre em `http://localhost:4200`. O `proxy.conf.json` encaminha `/api/*` para
`http://localhost:8080` (o Gateway), reproduzindo a mesma origem que o nginx tem em produção — por
isso o código só usa caminhos relativos (`/api/...`), nunca uma URL absoluta do Gateway.

## Testar

```bash
npm test               # Vitest (jsdom), uma vez
npm run test:coverage  # idem + cobertura em coverage/ — FALHA abaixo dos pisos
npm run lint           # ESLint (src/ e e2e/)
npm run format:check   # Prettier
```

### Cobertura

Pisos em [`vitest.config.ts`](vitest.config.ts) (lido pelo builder via `runnerConfig`):
**>= 75%** de linhas global e **>= 80%** em `core/**`, `*.store.ts`, `*.guard.ts` e
`*.interceptor.ts`. Entram no cálculo todos os `.ts` de `src/app/**`, **exceto** `*.spec.ts`,
`**/models/**` (DTOs sem lógica), `app.config.ts` e `app.routes.ts` (declarativos), declarados em
`coverageExclude` no `angular.json`. `main.ts`, `environments/` e `testing/` ficam fora por estarem
fora de `src/app`. Relatório HTML em `coverage/frontend/index.html`. Cobertura é diagnóstico: use o
relatório para achar lógica de estado sem teste, não persiga 100%.

## Build de produção

```bash
npm run build
```

Gera `dist/frontend/browser/`, que o nginx serve como estático. Sem warnings; sem sourcemaps; o
bundle inicial tem orçamento (`angular.json` -> `budgets`: aviso 320 kB, erro 400 kB) e as features
são chunks lazy separados. Navegadores suportados: [`.browserslistrc`](.browserslistrc).

## E2E (Playwright)

Roda contra a **stack real em container** (nginx + build de produção + gateway + identity + tasks +
Postgres), nunca contra o `ng serve`. A porta 80 do host precisa estar livre.

```bash
npm run e2e:stack      # UM comando: gera chaves/SQL se faltarem, sobe o compose, espera ficar
                       # pronto e roda chromium + mobile-360. Aceita args: npm run e2e:stack -- -g "fluxo"
```

Com a stack já no ar (`docker compose --profile full up -d --build` na raiz):

```bash
npm run e2e            # chromium (desktop) + mobile-360 — o que roda em todo PR
npm run e2e:all        # + firefox e webkit (só local, fora do CI; `npx playwright install firefox webkit`)
E2E_BASE_URL=http://localhost npx playwright test e2e/leak.spec.ts   # um spec; base configurável
```

- `e2e/flows.spec.ts`: os 12 fluxos críticos de FE-22. `e2e/a11y.spec.ts`: `axe-core` em todas as
  telas + foco/teclado/360 px (FE-21). `e2e/leak.spec.ts`: antivazamento de senha/token (FE-23, CA-15).
- Cada teste cria o próprio usuário (e-mail por UUID); rodam em paralelo e repetidos sem limpeza.
  Sem `waitForTimeout` e sem retry — teste flaky é bug.
- Falha deixa screenshot, vídeo e trace em `test-results/`; relatório HTML em `playwright-report/`
  (`npx playwright show-report`).
- Fluxo 9 (renovação transparente) usa `page.clock` para saltar além da expiração do access token,
  sem esperar 15 minutos reais.

## Estrutura

```
src/app/
├── core/                  → serviços, interceptors, guards (singletons)
│   ├── api/               → ApiClient, AuthApi/TasksApi/UserApi, modelos, interceptor de X-Client-Date
│   ├── auth/              → SessionStore, SessionRefresher (single-flight + lock entre abas), guards, interceptors
│   ├── errors/            → AppError, mapa de mensagens, interceptor de erro
│   └── http-status/       → indicador discreto do último status HTTP (demo)
├── shared/                → ui (loading, diálogo de confirmação, etc.), layout (AuthLayout, AppShell), forms
├── features/              → auth (login, cadastro), tasks, account, not-found
└── app.config.ts, app.routes.ts
e2e/                       → Playwright (flows, a11y, leak) + support.ts
```

## Decisões relevantes para quem for mexer aqui

- **Sessão (FD-01/FD-20):** o access token vive só em memória (signal do `SessionStore`); o refresh
  token é cookie HttpOnly que o JS nunca vê. F5 restaura a sessão via `POST /api/auth/refresh`. O
  ESLint proíbe `localStorage`/`sessionStorage`/`document.cookie`/`refreshToken` fora de testes.
- **Mesma origem, sem CORS (FD-16):** nunca apontar para uma URL absoluta do Gateway; o
  `apiBaseUrl` do `environment` fica vazio de propósito.
- **`X-Client-Date` (FD-17/FD-19):** todo request à API leva a data local do usuário
  (`Intl.DateTimeFormat`, nunca `toISOString()`), para o backend calcular `isOverdue` no fuso certo.
- **CSP:** o nginx aplica `script-src 'self'`; por isso o build desliga `inlineCritical` (o `onload`
  inline do CSS crítico seria bloqueado). Cabeçalhos e CSP: [`docs/seguranca-frontend.md`](../docs/seguranca-frontend.md).
- **Acessibilidade:** auditoria e pendências em [`docs/acessibilidade.md`](../docs/acessibilidade.md).
