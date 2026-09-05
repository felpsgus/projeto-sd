# BE-10 — Refresh token com rotação e detecção de reuso

| | |
|---|---|
| **Domínio** | Autenticação |
| **Depende de** | [BE-08](BE-08-emissao-jwt.md) |
| **Bloqueia** | BE-09, BE-11, BE-15, BE-16 |
| **Regras cobertas** | RN-AUTH-10, RN-AUTH-14, RN-AUTH-15, RN-AUTH-16, RN-AUTH-17, RN-AUTH-18, RN-AUTH-20 |
| **Estimativa** | G |

## Objetivo

A sessão sobrevive à expiração do access token: o usuário troca um refresh token válido por um novo par de tokens, e um refresh token só serve **uma vez**. Reuso de token derruba a sessão.

## Escopo

### Inclui

- Entidade `RefreshToken` em `Domain`:

  | Propriedade | Regra |
  |---|---|
  | `Id` | `Guid` |
  | `UserId` | dono |
  | `TokenHash` | **hash** do token, nunca o valor em claro (RN-AUTH-20) |
  | `SessionId` | agrupa a cadeia de rotações de uma mesma sessão (D-15) |
  | `ExpiresAt` | `CreatedAt + Jwt:RefreshTokenDays` (RN-AUTH-15) |
  | `ConsumedAt` | quando foi usado para renovar (`null` = ainda utilizável) |
  | `RevokedAt` + `RevokedReason` | revogação explícita (`Logout`, `PasswordChanged`, `ReuseDetected`, `AccountDeleted`) |
  | `ReplacedByTokenId` | encadeia a rotação, para auditoria |
  | `CreatedAt` | auditoria |

- Geração do valor do token: **≥ 32 bytes de aleatoriedade criptográfica** (`RandomNumberGenerator`), codificado em Base64Url. **Não é JWT** — é opaco.
- Endpoint **`POST /api/auth/refresh`**, anônimo (o access token já pode estar expirado). **Corpo vazio**: o token vem do cookie `refreshToken`, lido de `Request.Cookies` (decisão **D-20**). O endpoint **não** aceita o token por corpo, query string ou header customizado — há um único caminho de entrada.
- Caso de uso `RefreshTokenHandler`:
  1. localiza o token pelo hash;
  2. se **não existe** → falha;
  3. se **expirado** → falha (RN-AUTH-18);
  4. se **revogado** → falha;
  5. se **já consumido** → **detecção de reuso**: revoga **toda a cadeia da `SessionId`** e falha (RN-AUTH-17);
  6. caso válido: marca como consumido, emite **novo** refresh token na mesma `SessionId` (RN-AUTH-16) e um novo access token, encadeando `ReplacedByTokenId`.
- Serviço `IRefreshTokenService` com `IssueAsync(userId, sessionId?)`, `RedeemAsync(token)`, `RevokeSessionAsync(sessionId, reason)`, `RevokeAllForUserAsync(userId, reason)` — este último é o que BE-11, BE-15 e BE-16 consomem.
- Índice único em `TokenHash`; índice em `(UserId, SessionId)`.
- Resposta de sucesso: **200** com `{ "accessToken", "expiresAt" }` no corpo, e o **novo** refresh token emitido por `Set-Cookie` com os mesmos atributos do login (`HttpOnly; Secure; SameSite=Strict; Path=/api/auth`).
- **Todas** as falhas de refresh retornam **401** com o mesmo código `auth.invalid_refresh_token` — não revelar qual das cinco condições ocorreu — e **apagam o cookie** (`Set-Cookie` com expiração no passado), para o cliente não reenviar um token morto indefinidamente.

### Não inclui

- Logout (BE-11).

## Notas técnicas

- **Por que armazenar hash e não o token:** um dump do banco não permite forjar sessões. Usar SHA-256 aqui é suficiente (o token já tem entropia alta; não precisa de hash lento como o de senha).
- **D-20 — o cookie é o que cumpre a RN-AUTH-20.** Com o token no corpo, o frontend seria obrigado a guardá-lo onde JavaScript alcança, e um XSS exfiltraria uma credencial de 7 dias. Com `HttpOnly`, o navegador o envia sozinho e nenhum script lê o valor. Vale registrar o limite: um XSS ainda consegue *chamar* `/api/auth/refresh` na própria origem e obter um access token — o que ele não consegue é levar a credencial de longa duração embora.
- **Sem CSRF token nesta versão** porque `SameSite=Strict` + mesma origem (**D-21**) impedem que outro site dispare a chamada com o cookie anexado. **Se a hospedagem mudar** para domínios distintos, o cookie precisa de `SameSite=None` e a proteção CSRF volta a ser obrigatória — este é o ponto do sistema que mais depende de D-21.
- **A detecção de reuso (RN-AUTH-17) é o coração desta task.** Sem ela, a rotação não protege nada: um token roubado continuaria funcionando em paralelo ao legítimo. Ao ver um token já consumido, assume-se vazamento e derruba-se a cadeia inteira.
- Concorrência: dois refreshes simultâneos com o mesmo token **não podem** ambos ter sucesso. Garantir com atualização condicional (`UPDATE ... WHERE ConsumedAt IS NULL`) ou transação com nível de isolamento adequado — não basta ler-verificar-escrever.
- `TimeProvider` para expiração; nenhum teste usa `Thread.Sleep`.
- O expurgo de tokens expirados/consumidos antigos entra em [BE-23](BE-23-expurgo-tarefas-removidas.md).

## Critérios de aceite

### Fluxo feliz

- [ ] **CA-01** — `POST /api/auth/refresh` com o cookie válido e **corpo vazio** retorna **200** com novo access token no corpo e novo refresh token em `Set-Cookie`.
- [ ] **CA-02** — O novo access token é aceito por um endpoint protegido.
- [ ] **CA-03** — O novo refresh token é **diferente** do apresentado (RN-AUTH-16).
- [ ] **CA-03b** — O corpo da resposta **não contém** o refresh token (**D-20**).
- [ ] **CA-03c** — Enviar o refresh token no **corpo**, em **query string** ou em header customizado **não** autentica a renovação: sem o cookie, retorna 401. Há um único caminho de entrada.
- [ ] **CA-04** — A renovação funciona **sem** enviar e-mail ou senha (RN-AUTH-14) e **sem** access token válido no cabeçalho.
- [ ] **CA-05** — A renovação funciona mesmo com o access token anterior **já expirado** (cenário real de uso).
- [ ] **CA-06** — A `SessionId` permanece a mesma através de N rotações consecutivas.
- [ ] **CA-07** — `ExpiresAt` do refresh token é `CreatedAt + Jwt:RefreshTokenDays` (7 dias por padrão) e é **maior** que a expiração do access token (RN-AUTH-15).

### Uso único e reuso

- [ ] **CA-08** — Usar o **mesmo** refresh token duas vezes: a primeira sucede, a segunda retorna **401** (RN-AUTH-16).
- [ ] **CA-09** — Após a detecção de reuso, **toda a cadeia daquela sessão** fica revogada: o refresh token mais recente, obtido legitimamente, também para de funcionar (RN-AUTH-17).
- [ ] **CA-10** — Após a detecção de reuso, o usuário consegue voltar a operar **apenas** fazendo login novamente.
- [ ] **CA-11** — A detecção de reuso **não** afeta outras sessões do mesmo usuário (outro dispositivo continua funcionando — D-15).
- [ ] **CA-12** — Dois refreshes **concorrentes** com o mesmo token: exatamente um retorna 200 e o outro 401; o banco não fica com dois tokens ativos derivados do mesmo pai.

### Expiração e revogação

- [ ] **CA-13** — Refresh token expirado retorna **401**; o usuário precisa de novo login (RN-AUTH-18).
- [ ] **CA-14** — Refresh token revogado (por qualquer motivo) retorna **401**.
- [ ] **CA-15** — Token inexistente/malformado retorna **401**, nunca 500.
- [ ] **CA-16** — Os corpos de resposta de CA-08, CA-13, CA-14 e CA-15 são **idênticos** — o cliente não distingue expirado de revogado de inexistente.

### Segurança e armazenamento

- [ ] **CA-17** — A coluna do banco guarda o **hash**: uma busca pelo valor em claro do token não encontra nenhuma linha (RN-AUTH-20).
- [ ] **CA-18** — O valor em claro do refresh token não aparece em nenhum log, em nenhum nível — nem no log de requisição, que não deve registrar cabeçalhos de cookie.
- [ ] **CA-18b** — O cookie emitido tem `HttpOnly`, `Secure`, `SameSite=Strict` e `Path=/api/auth` (**D-20**), verificados no `Set-Cookie`.
- [ ] **CA-18c** — Toda resposta **401** do refresh apaga o cookie, emitindo `Set-Cookie` com expiração no passado.
- [ ] **CA-19** — Dois refresh tokens gerados nunca colidem, e o valor tem ≥ 32 bytes de entropia (verificado no gerador).
- [ ] **CA-20** — `TokenHash` tem índice único no banco.
- [ ] **CA-21** — A resposta de refresh traz `Cache-Control: no-store`.

## Testes obrigatórios

- Unidade: `RefreshTokenHandler` cobrindo as cinco condições de falha e a rotação — CA-03, CA-06 a CA-11, CA-13 a CA-16.
- Integração: fluxo completo login → refresh → refresh, com `TimeProvider` avançado — CA-01, CA-02, CA-04, CA-05, CA-07.
- Integração: **teste de concorrência** com duas chamadas paralelas — CA-12. Este teste não é opcional.
- Integração: CA-17, CA-20.

## Decisões em aberto

- **D-10** — Duração do refresh token. Padrão: 7 dias, configurável.
- **D-11** — Rotação com uso único. Padrão: sim.
- **D-15** — Múltiplas sessões simultâneas. Padrão provisório: sim, uma cadeia por login.
- **D-20** — ✅ decidida: refresh token em cookie `HttpOnly`, fora do corpo JSON.
- **D-21** — ✅ decidida: mesma origem → `SameSite=Strict` e sem CSRF token. **Reavaliar se a hospedagem mudar.**
