# BE-11 — Logout e revogação de sessões

| | |
|---|---|
| **Domínio** | Autenticação |
| **Depende de** | [BE-09](BE-09-login.md), [BE-10](BE-10-refresh-token-rotacao.md) |
| **Bloqueia** | — |
| **Regras cobertas** | RN-AUTH-12, RN-AUTH-19 |
| **Estimativa** | P |

## Objetivo

O usuário encerra a própria sessão quando quiser, e o refresh token daquela sessão deixa de funcionar imediatamente.

## Escopo

### Inclui

- Endpoint **`POST /api/auth/logout`**, **autenticado**, **corpo vazio**: a sessão a encerrar é identificada pelo cookie `refreshToken` (decisão **D-20**).
- Comportamento: revoga o token do cookie e **toda a cadeia da sua `SessionId`**, com `RevokedReason = Logout` (RN-AUTH-12), e **apaga o cookie** na resposta (`Set-Cookie` com expiração no passado).
- Endpoint **`POST /api/auth/logout-all`**, autenticado, sem corpo: revoga **todas** as sessões do usuário (útil em "sair de todos os dispositivos"). Reusa `RevokeAllForUserAsync` de BE-10.
- Resposta **204 No Content** em ambos.
- **Idempotência**: logout de uma sessão já encerrada retorna 204, não erro.

### Não inclui

- Revogação por troca de senha (BE-15) e por exclusão de conta (BE-16) — consomem o mesmo serviço, mas são acionadas lá.
- Invalidação do access token já emitido — ver notas.

## Notas técnicas

- **Limitação conhecida e aceita:** o access token é um JWT autocontido e **continua válido até expirar** (≤ 15 min) mesmo após o logout. Invalidá-lo exigiria uma blocklist consultada a cada requisição, o que anularia o ganho do JWT. A janela de 15 minutos (RN-AUTH-11) é justamente a mitigação. **Registrar como ADR** — é a pergunta que aparece em toda revisão de segurança.
- **O cookie simplifica esta task** (D-20): o cliente não precisa mandar nada, e some a preocupação de valor sensível em query string. Em compensação, **apagar o cookie na resposta passa a ser obrigatório** — sem isso, o navegador continuaria enviando um token revogado a cada tentativa de refresh.
- Além de revogar no servidor e apagar o cookie, o `Path` e o `SameSite` usados no `Set-Cookie` de expiração **devem ser idênticos** aos do login. Atributos diferentes criam um segundo cookie em vez de apagar o primeiro — é o erro clássico de "o logout não funciona às vezes".
- Se o refresh token do cookie pertencer a **outro** usuário (cookie de uma sessão diferente do access token apresentado), a operação retorna 204 sem revogar nada — não confirma existência — mas registra alerta em log. Um usuário não pode encerrar a sessão de outro.

## Critérios de aceite

- [ ] **CA-01** — `POST /api/auth/logout` autenticado, com o cookie de sessão presente e **corpo vazio**, retorna **204**.
- [ ] **CA-01b** — A resposta apaga o cookie: `Set-Cookie` com expiração no passado e **os mesmos** `Path` e `SameSite` usados no login.
- [ ] **CA-02** — Após o logout, uma chamada a `/api/auth/refresh` com aquele cookie retorna **401** (RN-AUTH-12).
- [ ] **CA-03** — Após o logout, o refresh token fica com `RevokedAt` preenchido e `RevokedReason == Logout` no banco.
- [ ] **CA-04** — O logout revoga a **cadeia inteira** da sessão, não só o token apresentado: nenhum token daquela `SessionId` renova.
- [ ] **CA-05** — O logout de uma sessão **não** derruba as outras sessões do mesmo usuário (outro dispositivo continua renovando).
- [ ] **CA-06** — `POST /api/auth/logout-all` revoga **todas** as sessões: nenhum refresh token do usuário funciona depois (RN-AUTH-19).
- [ ] **CA-07** — Logout sem autenticação retorna **401**.
- [ ] **CA-08** — Logout **sem cookie**, ou com cookie inexistente/já revogado, retorna **204** (idempotente), sem erro.
- [ ] **CA-09** — Logout com um cookie pertencente a **outro** usuário retorna 204 **sem revogar** a sessão alheia — verificado consultando o banco e confirmando que a vítima ainda consegue renovar.
- [ ] **CA-10** — A tentativa de CA-09 gera uma entrada de log de nível `Warning` (sem o valor do token).
- [ ] **CA-11** — O access token emitido antes do logout continua funcionando até expirar — comportamento **documentado no ADR** e coberto por um teste que registra a expectativa (para que uma mudança futura seja consciente).
- [ ] **CA-12** — O refresh token não trafega em corpo nem em query string em nenhum endpoint (**D-20**) — só em cookie.
- [ ] **CA-13** — Após o logout, o navegador deixa de enviar o cookie em novas requisições a `/api/auth/*` (verificado em teste de integração que segue os `Set-Cookie` como um cliente real).

## Testes obrigatórios

- Integração: CA-01 a CA-09, CA-11 a CA-13.
- Unidade: handler de logout com repositório substituído — CA-04, CA-08, CA-09.
- **CA-01b e CA-13 são obrigatórios**: revogar no banco sem apagar o cookie deixa o cliente reenviando um token morto a cada renovação.
- ADR versionado em `docs/adr/` cobrindo a limitação de CA-11.

## Decisões em aberto

- **D-20** — ✅ decidida: sessão identificada por cookie, corpo vazio.
