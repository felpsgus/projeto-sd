# Frontend — TodoList (T2)

Angular 22, standalone, **zoneless**, TypeScript estrito. Recorte do T2: login, criar
tarefa e listar tarefas, falando só com o API Gateway. Ver
[`tarefas/frontend/README.md`](../tarefas/frontend/README.md) para o plano completo e
[`tarefas/frontend/DECISOES-PENDENTES.md`](../tarefas/frontend/DECISOES-PENDENTES.md)
para as decisões de arquitetura (FD-01, FD-16, FD-17, FD-19, FD-20).

## Pré-requisitos

- **Node 24.21.0** (fixado em [`.nvmrc`](.nvmrc); Node 22 LTS também funciona) e **npm ≥ 10**.
- Backend do T2 no ar em `http://localhost:8080` **só** se você quiser exercitar chamadas
  reais durante o desenvolvimento — o `ng serve` funciona e compila sem o backend.

## Instalar

```bash
npm ci
```

## Rodar em desenvolvimento

```bash
npm start
```

Abre em `http://localhost:4200`. O `proxy.conf.json` encaminha `/api/*` para
`http://localhost:8080` (o Gateway), reproduzindo em dev a mesma origem que o nginx terá
em produção (BE-42) — por isso o código só usa caminhos relativos (`/api/...`), nunca uma
URL absoluta do Gateway.

## Testar

```bash
npm test              # Vitest, uma vez
npm run test:coverage # com relatório de cobertura em coverage/
```

## Lint e formatação

```bash
npm run lint
npm run format:check
```

## Build de produção

```bash
npm run build
```

Gera `dist/frontend/browser/`, que o nginx (BE-42) serve como estático.

## Estrutura

```
src/app/
├── core/                  → serviços de aplicação, interceptors, guards (singletons)
│   ├── api/               → ApiClient, AuthApi, TasksApi, modelos, interceptor de X-Client-Date
│   ├── auth/              → SessionStore (sessão em memória, FD-20), guards, interceptor de auth
│   ├── errors/             → AppError, mapa de mensagens, interceptor de erro
│   └── http-status/       → indicador discreto do último status HTTP (para a demo)
├── shared/
│   ├── ui/                → componentes de apresentação (loading, empty-state, error-state, form-field-error, indicador de status)
│   └── layout/            → AuthLayout (público) e AppShell (autenticado)
├── features/
│   ├── auth/login/        → tela de login
│   ├── tasks/             → PONTO DE EXTENSÃO da próxima onda — ver features/tasks/README.md
│   └── not-found/         → página 404
└── app.config.ts, app.routes.ts
```

## O que entra e o que não entra no T2

Só login, criar tarefa e listar tarefas. Sem cadastro, perfil, troca de senha, exclusão
de conta, editar/concluir/reabrir/remover tarefa, filtros ou busca — ver a tabela
"Recorte do T2" em `tarefas/frontend/README.md`. A pasta `features/tasks/` já existe com
um placeholder mínimo na rota `/tasks`; a listagem e a criação de tarefas são da próxima
onda (ver `src/app/features/tasks/README.md`).

## Decisões relevantes para quem for mexer aqui

- **Sessão só em memória (FD-20):** o access token vive num signal do `SessionStore`,
  nunca em `localStorage`/`sessionStorage`/cookie. Recarregar a página (F5) exige novo
  login — é esperado, não é bug.
- **Sem refresh token no T2:** o interceptor de autenticação só anexa `Authorization:
  Bearer` e, num 401, encerra a sessão e leva ao login. Não existe renovação automática.
- **Mesma origem, sem CORS (FD-16):** nunca apontar para uma URL absoluta do Gateway nem
  configurar CORS — o `apiBaseUrl` do `environment` fica vazio de propósito.
- **`X-Client-Date` (FD-17/FD-19):** todo request à API leva a data local do usuário
  (`Intl.DateTimeFormat`, nunca `toISOString()`), para o backend calcular `isOverdue` no
  fuso certo.
