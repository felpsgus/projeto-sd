# FE-05 — Estado de sessão e armazenamento de tokens

| | |
|---|---|
| **Domínio** | Autenticação |
| **Depende de** | [FE-02](FE-02-contratos-camada-http.md), [FE-03](FE-03-erros-feedback.md) · backend: [BE-08](../backend/BE-08-emissao-jwt.md), [BE-10](../backend/BE-10-refresh-token-rotacao.md) |
| **Bloqueia** | FE-06 em diante |
| **Regras cobertas** | RN-AUTH-10, RN-AUTH-11, RN-AUTH-20 |
| **Estimativa** | G |

> **FD-01 está decidida:** o refresh token vive em cookie `HttpOnly` e o frontend **nunca o vê**. Isso simplificou esta task — não há `TokenStorage`, não há escolha de armazenamento, não há nada a persistir.

## Objetivo

Existe uma única fonte de verdade sobre "quem está logado", exposta por signals — e o frontend não guarda credencial nenhuma em disco.

## Escopo

### Inclui

- `SessionStore` em `core/auth/`, serviço com signals (FD-05):

  | Membro | Tipo | Observação |
  |---|---|---|
  | `user` | `Signal<UserResponse \| null>` | usuário autenticado |
  | `isAuthenticated` | `Signal<boolean>` | `computed` a partir de `user` + token válido |
  | `accessTokenExpiresAt` | `Signal<Date \| null>` | vindo de `expiresAt` da API |
  | `status` | `Signal<'unknown' \| 'authenticated' \| 'anonymous'>` | `unknown` durante o bootstrap |
  | `startSession(tokens, user)` | ação | após login |
  | `updateTokens(tokens)` | ação | após refresh |
  | `endSession(reason)` | ação | logout, expiração, revogação |

- **Nenhum armazenamento de credencial no frontend** (FD-01):
  - o **refresh token** é um cookie `HttpOnly` gerido inteiramente pelo backend — o código do frontend não o lê, não o escreve e não sabe o seu valor;
  - o **access token fica em memória** (signal), nunca em `localStorage`/`sessionStorage` — sua vida é de 15 minutos e ele é reobtido por refresh.
  **Nenhuma feature, componente ou interceptor acessa `localStorage`, `sessionStorage` ou `document.cookie`** para fins de sessão.
- **Bootstrap de sessão**: ao carregar a aplicação, tenta restaurar a sessão (via refresh) **antes** de renderizar as rotas. Enquanto isso, `status === 'unknown'` e a aplicação mostra um estado de carregamento — não a tela de login.
- `endSession(reason)` limpa tudo que o frontend controla: signals e estado de feature em memória. O cookie é apagado **pelo backend**, na resposta do logout ([BE-11](../backend/BE-11-logout-revogacao.md)) ou na falha do refresh ([BE-10](../backend/BE-10-refresh-token-rotacao.md)) — o frontend não tem como apagá-lo sozinho, e não precisa. Motivos: `user_logout`, `session_expired`, `session_revoked`, `account_deleted`.
- Sincronia entre abas: uma sessão encerrada em uma aba encerra nas demais (evento de `storage` ou `BroadcastChannel`).

### Não inclui

- A chamada de refresh em si e o tratamento de 401 (FE-06).
- Telas de login/logout (FE-09, FE-10).

## Notas técnicas

- **O access token em memória é uma escolha de segurança, não um detalhe.** Guardá-lo em `localStorage` amplia a janela de roubo por XSS de 15 minutos para "até alguém limpar o navegador".
- **O bootstrap evita o pior defeito de UX desta arquitetura:** sem ele, todo `F5` numa página autenticada pisca a tela de login antes de restaurar a sessão. CA-06 e CA-07 tratam disso.
- **O bootstrap é o que faz o cookie valer a pena.** Como o access token some a cada reload (está em memória), a restauração da sessão é uma chamada a `/api/auth/refresh` — que funciona porque o navegador anexa o cookie sozinho, sem o frontend ter guardado nada. É o mesmo mecanismo do refresh normal ([FE-06](FE-06-interceptor-auth-refresh.md)), aproveitado na inicialização.
- `status: 'unknown'` existe para que os guards (FE-07) **esperem** em vez de redirecionar para o login prematuramente.
- Nenhum `effect()` para reagir a mudança de sessão em componente — use `computed()`. `effect()` só onde há efeito colateral real (limpar timer, fechar canal).
- O usuário exibido no shell vem de `SessionStore.user`, populado no login e revalidado por `GET /api/me` quando a sessão é restaurada.

## Critérios de aceite

### Estado

- [ ] **CA-01** — Após `startSession`, `isAuthenticated` é `true` e `user` traz os dados retornados pela API.
- [ ] **CA-02** — Após `endSession`, `isAuthenticated` é `false`, `user` é `null` e nenhum token permanece em memória ou em storage.
- [ ] **CA-03** — `isAuthenticated` é `computed`, não um signal escrito manualmente em vários pontos.
- [ ] **CA-04** — `accessTokenExpiresAt` reflete o `expiresAt` retornado pela API, não um cálculo local de "agora + 15 min".
- [ ] **CA-05** — Um componente que lê `session.user()` re-renderiza quando a sessão muda, sem `subscribe` e sem `effect`.

### Bootstrap e persistência

- [ ] **CA-06** — Recarregar a página (`F5`) numa rota autenticada **mantém** o usuário logado, sem exibir a tela de login em nenhum momento.
- [ ] **CA-07** — Durante o bootstrap, `status` é `'unknown'` e a aplicação exibe carregamento — não conteúdo autenticado nem tela de login.
- [ ] **CA-08** — Se a restauração falhar (refresh inválido/expirado), `status` vira `'anonymous'` e o usuário vai para o login **sem** mensagem de erro alarmante.
- [ ] **CA-09** — O access token **não** é encontrado em `localStorage` nem em `sessionStorage` em nenhum momento (verificado por teste que inspeciona os storages após o login).

### Ausência de armazenamento (FD-01)

- [ ] **CA-10** — Após um login bem-sucedido, `localStorage` e `sessionStorage` estão **vazios** de qualquer valor de credencial (teste que inspeciona ambos por completo, não só chaves conhecidas).
- [ ] **CA-11** — Nenhum acesso a `localStorage`, `sessionStorage` ou `document.cookie` para fins de sessão existe na base de código (verificado por busca e por regra de lint).
- [ ] **CA-12** — `document.cookie` **não** contém o refresh token: ele é `HttpOnly` e invisível a JavaScript (RN-AUTH-20). Teste que lê `document.cookie` após o login e confirma a ausência.
- [ ] **CA-13** — Não existe nenhum campo, tipo ou variável chamado `refreshToken` no código do frontend — o valor nunca transita por JavaScript.

### Encerramento

- [ ] **CA-14** — `endSession` limpa também o estado de features (lista de tarefas em memória), para que outro usuário logando na mesma aba não veja dados do anterior.
- [ ] **CA-15** — Encerrar a sessão em uma aba encerra nas demais abas abertas.
- [ ] **CA-16** — Cada motivo de encerramento produz a mensagem adequada: logout voluntário não exibe erro; expiração exibe "sua sessão expirou"; revogação exibe "sua sessão foi encerrada, entre novamente".

## Testes obrigatórios

- Unidade: `SessionStore` — CA-01 a CA-04, CA-14, CA-16.
- Unidade: bootstrap com API simulada (sucesso e falha) — CA-06 a CA-08.
- **Unidade: CA-09 a CA-13 são testes de segurança que inspecionam storage, cookie e código; não podem ser substituídos por revisão manual.** São a única verificação automatizada da RN-AUTH-20 no cliente.
- Componente: CA-05 com Testing Library.

## Decisões em aberto

- **FD-01** — ✅ decidida: refresh token em cookie `HttpOnly`; o frontend não armazena credencial alguma.
- **FD-05** — Estado por serviços com signals.
