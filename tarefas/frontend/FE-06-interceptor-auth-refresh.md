# FE-06 — Interceptor de autenticação e renovação automática

| | |
|---|---|
| **Domínio** | Autenticação |
| **Depende de** | [FE-05](FE-05-estado-sessao.md) · backend: [BE-10](../backend/BE-10-refresh-token-rotacao.md) |
| **Bloqueia** | FE-07 em diante |
| **Regras cobertas** | RN-AUTH-14, RN-AUTH-15, RN-AUTH-16, RN-AUTH-17, RN-AUTH-18, RN-AUTH-19 |
| **Estimativa** | G |

## Objetivo

O usuário nunca é interrompido pela expiração do access token: as requisições levam o token automaticamente e, quando ele expira, a renovação acontece de forma transparente — uma única vez, mesmo com várias requisições simultâneas.

## Escopo

### Inclui

- **Interceptor de autenticação**: anexa `Authorization: Bearer <accessToken>` a toda requisição para a API. **Não** anexa a `register`, `login` e `refresh`, nem a URLs fora do `apiBaseUrl`.
- **Renovação proativa** (FD-13): antes de enviar a requisição, se o access token expira em menos de N segundos (`refreshSkewSeconds`, padrão 30), renova primeiro. Evita o 401 no caminho feliz.
- **Renovação reativa**: resposta **401** em endpoint autenticado dispara refresh e **repete a requisição original** uma única vez.
- A chamada de refresh é `POST /api/auth/refresh` com **corpo vazio** e **`withCredentials: true`** (FD-01, FD-16): o navegador anexa o cookie sozinho e o frontend não manda token nenhum.
- **Single-flight**: N requisições que recebem 401 ao mesmo tempo disparam **um** refresh; as demais aguardam o resultado e são repetidas com o novo token. Sem isso, a rotação de refresh token do backend ([BE-10](../backend/BE-10-refresh-token-rotacao.md)) interpretaria as chamadas paralelas como **reuso** e derrubaria a sessão inteira.
- **Falha no refresh** (401 do endpoint de refresh — token expirado, revogado ou reusado): encerra a sessão com o motivo apropriado e redireciona para o login preservando a `returnUrl`.
- **Sem laço infinito**: uma requisição já repetida após refresh **não** dispara novo refresh se falhar de novo com 401.
- Distinção entre **401 de sessão** e **403 de permissão** — 403 não dispara refresh.
- Registro na ordem correta da cadeia: `auth` → `refresh` → `erro` (FE-03).

### Não inclui

- O botão de logout (FE-10).
- Armazenamento de token (FE-05).

## Notas técnicas

- **O single-flight é o item de maior risco desta task.** O backend usa refresh token de uso único com detecção de reuso (RN-AUTH-16/17). Uma implementação ingênua — cada 401 chamando `/refresh` por conta própria — faz três requisições paralelas apresentarem o **mesmo** refresh token, o backend detectar reuso e **derrubar a sessão inteira do usuário**. O sintoma em produção é "às vezes o app me desloga sozinho", e é difícil de reproduzir. CA-06 e CA-07 existem exclusivamente para travar isso.
- A renovação proativa reduz, mas **não elimina**, a necessidade da reativa: o relógio do cliente pode estar dessincronizado, e o backend valida com `ClockSkew` zero ([BE-08](../backend/BE-08-emissao-jwt.md)).
- **RN-AUTH-19 aparece aqui como consequência:** quando o backend revoga as sessões (troca de senha em outro dispositivo, logout-all), o próximo refresh falha — e o usuário precisa cair no login com uma mensagem clara, não numa tela quebrada.
- O access token **não** é decodificado no cliente para checar expiração: usa-se o `expiresAt` que a API devolveu. Confiar no `exp` do JWT decodificado convida a tratar o token como dado, e não como credencial opaca.
- Requisições que já estão em voo quando a sessão é encerrada devem ser canceladas, não repetidas.
- **`withCredentials: true` esquecido é o erro nº 1 deste desenho** (FD-16). Sem ele o cookie não é anexado, o backend responde 401 no refresh, e o sintoma é o usuário sendo deslogado sem motivo aparente — indistinguível de um refresh token inválido. CA-05b existe para pegar isso no teste, não em produção.
- O frontend **não** apaga o cookie em nenhum cenário: quem o apaga é o backend, na resposta do logout ou do 401 de refresh. Tentar apagá-lo por JavaScript é impossível (`HttpOnly`) e sinaliza mal-entendido do desenho.

## Critérios de aceite

### Anexação do token

- [ ] **CA-01** — Requisição a endpoint autenticado inclui `Authorization: Bearer <token>`.
- [ ] **CA-02** — Requisições a `register`, `login` e `refresh` **não** incluem o cabeçalho.
- [ ] **CA-03** — Requisições a URLs fora do `apiBaseUrl` (ex.: um asset externo) **não** recebem o token — ele nunca vaza para terceiros.
- [ ] **CA-04** — Sem sessão ativa, nenhuma requisição leva `Authorization` vazio ou `Bearer null`.

### Renovação

- [ ] **CA-05** — Com o access token expirando em menos de 30 s, a requisição dispara refresh **antes** de ser enviada e segue com o token novo (RN-AUTH-14).
- [ ] **CA-05b** — A chamada de refresh é feita com **`withCredentials: true`** e **corpo vazio**, sem nenhum campo de token (FD-01, FD-16).
- [ ] **CA-05c** — A chamada de refresh **não** inclui o header `Authorization` — o access token pode estar expirado e é irrelevante ali.
- [ ] **CA-06** — **Três requisições simultâneas** recebendo 401 disparam **exatamente uma** chamada a `/api/auth/refresh` — verificado contando as chamadas.
- [ ] **CA-07** — Nesse cenário, as três requisições são repetidas com o **novo** token e todas concluem com sucesso; a sessão **não** é encerrada (guarda contra a detecção de reuso de RN-AUTH-17).
- [ ] **CA-08** — Após um refresh bem-sucedido, o novo par de tokens é gravado no `SessionStore` (RN-AUTH-16).
- [ ] **CA-09** — A requisição original é repetida **uma única vez**; um segundo 401 na repetição **não** dispara novo refresh.
- [ ] **CA-10** — Não existe laço infinito em nenhum cenário de falha (teste com API que responde 401 sempre — a cadeia termina).

### Falha de sessão

- [ ] **CA-11** — Refresh token **expirado** (401 do `/refresh`) encerra a sessão e leva ao login com "sua sessão expirou" (RN-AUTH-18).
- [ ] **CA-12** — Refresh token **revogado** (troca de senha em outro dispositivo, logout-all) encerra a sessão com a mensagem de sessão encerrada (RN-AUTH-19).
- [ ] **CA-13** — Detecção de reuso no backend (RN-AUTH-17) leva ao mesmo encerramento controlado — não a uma tela de erro genérica ou travada.
- [ ] **CA-14** — Ao ser levado ao login por expiração, a rota que o usuário tentava acessar é preservada em `returnUrl`, e após novo login ele volta para lá.
- [ ] **CA-15** — Requisições em voo no momento do encerramento são canceladas; nenhuma delas exibe toast de erro depois do redirecionamento.

### Diferenciação

- [ ] **CA-16** — Um **403** não dispara refresh nem encerra a sessão.
- [ ] **CA-17** — Um **404** ou **409** não dispara refresh.
- [ ] **CA-18** — Um erro de rede (status 0) não dispara refresh nem encerra a sessão.
- [ ] **CA-19** — A ordem dos interceptors está coberta por teste: o de erro (FE-03) não intercepta o 401 antes do de refresh.

## Testes obrigatórios

- Unidade com `HttpTestingController`: CA-01 a CA-05, CA-08 a CA-10, CA-16 a CA-19.
- **CA-06 e CA-07 são obrigatórios e não podem ser adiados** — são a proteção contra o desligamento espúrio de sessão em produção.
- Unidade: CA-11 a CA-15 com cenários de falha do refresh.
- Cenário de sessão expirada também é coberto em E2E ([FE-22](FE-22-testes-e2e.md)).

## Decisões em aberto

- **FD-13** — Renovação proativa + reativa. Padrão adotado: ambas.
- **FD-01**, **FD-16** — ✅ decididas: refresh por cookie `HttpOnly`, mesma origem, `withCredentials`.
