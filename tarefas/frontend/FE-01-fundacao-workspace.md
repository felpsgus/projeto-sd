# FE-01 — Fundação do workspace Angular 22

| | |
|---|---|
| **Domínio** | Infraestrutura |
| **Depende de** | — |
| **Bloqueia** | todas as demais |
| **Regras cobertas** | nenhuma diretamente (habilita todas) |
| **Estimativa** | M |

## Objetivo

Existe um workspace Angular 22 zoneless que compila, roda, passa no lint e executa testes — com as versões de ferramenta fixadas e a estrutura de pastas definida.

## Escopo

### Inclui

- Workspace criado com **Angular CLI 22**, standalone (sem `AppModule`), **zoneless** (`provideZonelessChangeDetection()`, sem `zone.js` no `polyfills`).
- `.nvmrc` fixando **Node 22 LTS** (ou 24 LTS); `engines` no `package.json` coerente.
- `package-lock.json` versionado. **npm** como único gerenciador — nenhum `pnpm-lock.yaml` ou `yarn.lock` no repositório.
- `tsconfig.json` com `strict: true`, `noImplicitOverride`, `noPropertyAccessFromIndexSignature`, `noUncheckedIndexedAccess`, `strictTemplates` no `angularCompilerOptions`.
- **ESLint** (`@angular-eslint`) + **Prettier**: Prettier decide formatação, ESLint decide qualidade. Regra que **proíbe `any`** ativa como erro.
- **Vitest** configurado como runner de testes (padrão no Angular 22) + **Angular Testing Library**, com cobertura habilitada.
- Estrutura de pastas:

  ```
  src/app/
  ├── core/          → serviços de aplicação, interceptors, guards, tokens de DI (singletons)
  ├── shared/        → componentes/pipes/diretivas reutilizáveis e sem regra de negócio
  ├── features/
  │   ├── auth/      → cadastro, login, logout
  │   ├── account/   → perfil, senha, exclusão
  │   └── tasks/     → lista, criação, edição
  └── app.config.ts  → providers da aplicação
  ```

- `environments/` com `apiBaseUrl` por ambiente, **sem nenhum segredo**.
- Rota raiz redirecionando para a área autenticada e rota curinga (`**`) para uma página 404.
- Scripts de `package.json`: `start`, `build`, `test`, `test:coverage`, `lint`, `format:check`.
- `README.md` do frontend: pré-requisitos, como instalar, rodar, testar.

### Não inclui

- SSR / prerender (FD-04: não nesta versão).
- Camada HTTP (FE-02), layout (FE-04), qualquer feature.

## Notas técnicas

- **Zoneless não é opcional** — é o modelo do Angular 22 e a razão de `OnPush` ser exigido em todo componente. Deixar `zone.js` ativo "por segurança" esconde bugs de reatividade que só aparecem em produção.
- Convenções de nome de arquivo em `kebab-case`; seletores em `kebab-case` com prefixo do app.
- Idioma: identificadores em **inglês**, textos de interface em **pt-BR** (FD-03).
- `noUncheckedIndexedAccess` gera atrito no início e evita uma classe inteira de `undefined` em runtime. Manter ligado.

## Critérios de aceite

- [ ] **CA-01** — `npm ci && npm run build` conclui **sem warnings**.
- [ ] **CA-02** — `npm run lint` e `npm run format:check` passam.
- [ ] **CA-03** — `npm start` sobe a aplicação e ela renderiza sem erro no console do navegador.
- [ ] **CA-04** — `npm test` executa via Vitest e passa.
- [ ] **CA-05** — `npm run test:coverage` gera relatório de cobertura em `coverage/`.
- [ ] **CA-06** — A aplicação roda **zoneless**: `zone.js` não está nos polyfills e `provideZonelessChangeDetection()` está registrado. Uma busca por `zone.js` no bundle não encontra nada.
- [ ] **CA-07** — Um componente com signal atualizando fora de evento do Angular (ex.: `setTimeout`) **re-renderiza** corretamente — prova de que a reatividade por signals está funcionando sem zone.
- [ ] **CA-08** — Tentar usar `any` sem comentário de justificativa **falha** o lint.
- [ ] **CA-09** — Um erro de tipo em template (`strictTemplates`) **falha** o build — comprovado uma vez com um binding inválido.
- [ ] **CA-10** — Nenhum `NgModule` existe na base de código.
- [ ] **CA-11** — A versão do Node está fixada em `.nvmrc` e casa com a usada no CI.
- [ ] **CA-12** — `@angular/core` e `@angular/cli` estão no mesmo major (**22.x**) e o TypeScript está em `~5.9`.
- [ ] **CA-13** — Nenhum segredo, chave ou URL de produção com credencial está versionado em `environments/`.
- [ ] **CA-14** — Navegar para uma rota inexistente exibe a página 404, não uma tela em branco.
- [ ] **CA-15** — O `README.md` do frontend permite a uma pessoa nova instalar, rodar e testar seguindo apenas o que está escrito.

## Testes obrigatórios

- Teste de componente cobrindo CA-07 (reatividade zoneless) — pequeno, mas é o que prova que a fundação está correta.
- Teste de smoke: a aplicação inicializa e a rota raiz renderiza.
- CA-08 e CA-09 são validados **provocando a falha** uma vez, em commit descartável.

## Decisões em aberto

- **FD-04** — SSR. Padrão adotado: não.
