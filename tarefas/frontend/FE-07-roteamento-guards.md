# FE-07 — Roteamento, guards e lazy loading

| | |
|---|---|
| **Domínio** | Autorização |
| **Depende de** | [FE-05](FE-05-estado-sessao.md), [FE-06](FE-06-interceptor-auth-refresh.md) |
| **Bloqueia** | todas as features |
| **Regras cobertas** | RN-AUTZ-04 |
| **Estimativa** | M |

> **Recorte do T2 (21/09/2026):** entra **quase integral**, com o mapa de rotas reduzido ao recorte: `/login` (pública), `/tasks` (autenticada, rota inicial pós-login), `/tasks/new` (autenticada) e `/**` (404). Sem `/register`, `/account`, `/account/password`, `/tasks/:id/edit`. `authGuard`, `guestGuard`, lazy loading e a validação de `returnUrl` contra open redirect entram integrais — são baratos e a proteção de segurança (CA-09) não deve ser cortada. Sem dependência de backend além da sessão de [FE-05](FE-05-estado-sessao.md)/[FE-06](FE-06-interceptor-auth-refresh.md).

## Objetivo

Rotas autenticadas são inacessíveis a visitantes, rotas públicas não são exibidas a quem já está logado, e cada feature é carregada sob demanda.

## Escopo

### Inclui

- Mapa de rotas:

  | Rota | Acesso | Feature |
  |---|---|---|
  | `/login` | visitante | auth |
  | `/register` | visitante | auth |
  | `/tasks` | autenticado | tasks (rota inicial pós-login) |
  | `/tasks/new` | autenticado | tasks |
  | `/tasks/:id/edit` | autenticado | tasks |
  | `/account` | autenticado | account |
  | `/account/password` | autenticado | account |
  | `/**` | qualquer | página 404 |

- **`authGuard`** (`CanActivateFn`): bloqueia visitante, redireciona para `/login` com `returnUrl`.
- **`guestGuard`** (`CanActivateFn`): redireciona quem já está autenticado de `/login` e `/register` para `/tasks`.
- Ambos **aguardam** o bootstrap de sessão: enquanto `status === 'unknown'` ([FE-05](FE-05-estado-sessao.md)), o guard não decide — não redireciona para o login antes de saber se há sessão.
- **Lazy loading por feature** com `loadChildren`/`loadComponent` — `auth`, `account` e `tasks` em chunks separados.
- Redirecionamento pós-login para a `returnUrl`, com **validação**: só rotas internas da própria aplicação são aceitas.
- **`canDeactivate`** genérico para formulários com alterações não salvas (usado por FE-17 e FE-18).
- Rota 404 dentro do layout apropriado (autenticado ou público, conforme o estado).

### Não inclui

- Papéis/permissões — não existem nesta versão (seção 2 das regras de negócio).
- As telas em si.

## Notas técnicas

- **Guard não é segurança** — é navegação. A garantia real de RN-AUTZ-04 está no backend ([BE-13](../backend/BE-13-protecao-endpoints.md)). Um guard driblado no cliente não dá acesso a dado nenhum; ele apenas evita telas quebradas. Isso precisa estar claro para ninguém tratar o guard como controle de acesso.
- **A `returnUrl` é um vetor de open redirect.** `?returnUrl=https://site-malicioso/` levaria o usuário para fora após o login. Aceitar apenas caminhos relativos que resolvam dentro da aplicação — CA-08 e CA-09.
- O guard esperando o bootstrap é o que evita o pisca-pisca de login em cada `F5`. Implementar como sinal assíncrono resolvido, não como leitura imediata do signal.
- Rota inicial após login é `/tasks` — é o que o usuário veio fazer.

## Critérios de aceite

### Proteção

- [x] **CA-01** — Visitante acessando `/tasks` diretamente pela URL é levado a `/login` (RN-AUTZ-04).
- [x] **CA-02** — Visitante acessando `/account`, `/tasks/new` e `/tasks/:id/edit` também é levado a `/login`.
- [x] **CA-03** — Usuário autenticado acessando `/login` ou `/register` é levado a `/tasks`.
- [x] **CA-04** — Usuário autenticado navega livremente entre as rotas protegidas.
- [x] **CA-05** — Um teste enumera **todas** as rotas declaradas e falha se alguma rota que não seja `/login`, `/register` ou `/**` estiver **sem** `authGuard` — assim, uma rota nova esquecida quebra o build.

### Bootstrap

- [x] **CA-06** — Recarregar `/tasks` com sessão válida **não** passa pela tela de login em nenhum instante.
- [x] **CA-07** — Durante o bootstrap, a navegação aguarda; não há redirecionamento prematuro.

### returnUrl

- [x] **CA-08** — Visitante que tenta `/tasks/abc/edit` é levado ao login e, após autenticar, volta **para aquela rota**.
- [x] **CA-09** — `returnUrl` apontando para host externo (`https://exemplo.com`, `//exemplo.com`, `javascript:...`) é **ignorada**; o usuário vai para `/tasks`.
- [x] **CA-10** — `returnUrl` de rota interna inexistente leva à página 404 dentro da aplicação, não a um erro.

### Lazy loading

- [x] **CA-11** — O bundle inicial **não** contém o código das features `tasks` e `account` (verificado no relatório de build).
- [x] **CA-12** — Cada feature é carregada no primeiro acesso à sua rota, e o carregamento exibe indicador em vez de tela em branco. *(Atendido em 03/10/2026, issue #13: o componente raiz mostra `<app-loading>` entre `RouteConfigLoadStart` e o fim da carga. Coberto por `app.spec.ts`.)*
- [x] **CA-13** — Falha ao carregar um chunk (rede caiu) exibe mensagem com opção de tentar novamente, não uma tela morta. *(Atendido em 03/10/2026, issue #13: em `NavigationError` o componente raiz mostra a mensagem e "Tentar novamente", que refaz a navegação para a mesma URL. Não exercitado em navegador com a rede cortada.)*

### Navegação

- [x] **CA-14** — Rota inexistente exibe a página 404 com link para voltar.
- [x] **CA-15** — O `canDeactivate` avisa antes de sair de um formulário com alterações não salvas, e permite cancelar a saída.
- [x] **CA-16** — Após o login, o usuário sem `returnUrl` cai em `/tasks`.

## Testes obrigatórios

- Unidade: `authGuard` e `guestGuard` com `SessionStore` simulado nos três estados (`unknown`, `authenticated`, `anonymous`) — CA-01 a CA-04, CA-07.
- **Unidade: CA-09 (open redirect) — teste de segurança obrigatório**, com todos os formatos maliciosos listados.
- Enumeração de rotas: CA-05.
- Build: CA-11 verificado no relatório de bundle do CI.

## Auditoria dos critérios (03/10/2026)

Critérios conferidos contra o código em 03/10/2026. Marcados: 10 de 16.

| CA | Situação | Evidência / motivo |
|---|---|---|
| CA-05 | em aberto | Não existe teste que enumere `routes` e exija `authGuard` nas rotas protegidas. Pela leitura de `app.routes.ts`, todas estão sob `authGuard`. |
| CA-08 | em aberto | A metade "vai ao login com `returnUrl`" é testada (`auth.guard.spec.ts`); a volta à rota após autenticar (`LoginComponent` navega a `resolveReturnUrl`) não tem teste. |
| CA-10 | em aberto | Sem teste de `returnUrl` interno inexistente caindo na 404 (a rota curinga existe). |
| CA-11 | atendido | Relatório do `npm run build` em 03/10/2026: chunks lazy `tasks-page-component`, `account-component`, `login-component` etc. fora do bundle inicial (294 kB). |
| CA-12 | em aberto | As rotas são lazy (`loadComponent`), mas não há indicador de carregamento de chunk: o `app-loading` do `App` só aparece com sessão `unknown`. |
| CA-13 | em aberto | Não há tratamento de falha de chunk (nenhum `withNavigationErrorHandler` nem mensagem com "tentar novamente"). |
| CA-15 | em aberto | `unsavedChangesGuard` + `EditTaskComponent.canDeactivate()` (usa `window.confirm`) existem, mas nenhum teste os exercita. |
