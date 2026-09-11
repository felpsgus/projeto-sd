# BE-09 — Login

| | |
|---|---|
| **Domínio** | Autenticação |
| **Depende de** | [BE-06](BE-06-hash-senha.md), [BE-07](BE-07-cadastro-usuario.md), [BE-08](BE-08-emissao-jwt.md), [BE-10](BE-10-refresh-token-rotacao.md) |
| **Bloqueia** | BE-11, BE-12, todos os fluxos autenticados |
| **Regras cobertas** | RN-AUTH-08, RN-AUTH-09, RN-AUTH-10, RN-USER-04 |
| **Estimativa** | M |

> **T2 (D-36):** [BE-33](BE-33-login-minimo-grpc.md) entrega um **recorte** desta task — só o access token, via RPC gRPC `Login` no Identity, com o **API Gateway** como borda REST. Refresh token, cookie (D-20) e o restante do escopo abaixo continuam **aqui**, em aberto até essas tasks entrarem. A rota `POST /api/auth/login` descrita abaixo passa a viver no **Gateway** (D-32), que recebe o JSON, chama o RPC `Login` e traduz a resposta — o Identity não expõe mais este endpoint REST diretamente.

## Objetivo

Um usuário ativo troca e-mail + senha por um par de tokens (access + refresh) e passa a ter uma sessão autenticada.

## Escopo

### Inclui

- Endpoint **`POST /api/auth/login`**, anônimo.
- Request: `{ "email": "string", "password": "string" }`.
- Caso de uso `LoginHandler`:
  1. normaliza o e-mail e busca o usuário;
  2. verifica a senha com `IPasswordHasher.Verify`;
  3. verifica se o usuário está ativo (RN-USER-04);
  4. emite access token (BE-08) e refresh token (BE-10);
  5. persiste o refresh token da nova sessão.
- Response **200** — o refresh token **não** aparece no corpo (decisão **D-20**):

  ```json
  {
    "accessToken": "string",
    "expiresAt": "iso-8601",
    "user": { "id": "guid", "displayName": "string", "email": "string" }
  }
  ```

  ```
  Set-Cookie: refreshToken=<opaco>; HttpOnly; Secure; SameSite=Strict; Path=/api/auth; Max-Age=<7 dias>
  ```

- **Resposta única de falha** (RN-AUTH-09): e-mail inexistente, senha errada e usuário inativo produzem **exatamente** a mesma resposta — **401** com `auth.invalid_credentials` e mensagem "E-mail ou senha inválidos".

### Não inclui

- Bloqueio por tentativas (BE-12) — este endpoint será estendido lá.
- Renovação (BE-10) e logout (BE-11).

## Notas técnicas

- **RN-AUTH-09 é a regra mais fácil de quebrar acidentalmente.** Três armadilhas a evitar:
  1. **Corpo diferente** por cenário — resolvido por um único caminho de erro no handler.
  2. **Status diferente** (ex.: 403 para inativo) — proibido, sempre 401.
  3. **Tempo de resposta diferente**: se o e-mail não existe, pular o `Verify` responde muito mais rápido e revela a existência da conta. O handler **DEVE** executar um hash dummy quando o usuário não for encontrado, para equalizar o tempo.
- **RN-USER-04**: usuário inativo não autentica — e não recebe mensagem específica sobre isso.
- Log: registrar tentativa de login com o e-mail **e o resultado**, sem a senha. Isso alimenta BE-12.
- O endpoint **não** deve ser cacheável (`Cache-Control: no-store`).
- **D-20 — refresh token em cookie:** o access token continua no corpo, porque o cliente precisa lê-lo para montar o header `Authorization`; sua vida de 15 minutos é a mitigação. O refresh token, de 7 dias, vai em cookie `HttpOnly` justamente para que o JavaScript **nunca** o leia (RN-AUTH-20). `Path=/api/auth` impede que ele viaje em toda requisição da API. `SameSite=Strict` é viável porque front e API estão na mesma origem (**D-21**) — e é o que dispensa proteção CSRF nesta versão.
- `Secure` sempre ligado, inclusive em desenvolvimento sobre HTTPS local. Um cookie de credencial trafegando em texto claro anula o resto.

## Critérios de aceite

- [ ] **CA-01** — Login com credenciais corretas retorna **200** com `accessToken` e `expiresAt` preenchidos, e emite o cookie `refreshToken`.
- [ ] **CA-01b** — O corpo da resposta **não contém** o refresh token em nenhum campo (**D-20**, RN-AUTH-20).
- [ ] **CA-01c** — O cookie vem com `HttpOnly`, `Secure`, `SameSite=Strict` e `Path=/api/auth` — os quatro atributos verificados no cabeçalho `Set-Cookie`.
- [ ] **CA-01d** — O `Max-Age`/`Expires` do cookie corresponde a `Jwt:RefreshTokenDays`.
- [ ] **CA-02** — O `accessToken` retornado é aceito por um endpoint protegido.
- [ ] **CA-03** — O login funciona com o e-mail em qualquer combinação de maiúsculas/minúsculas.
- [ ] **CA-04** — Senha incorreta retorna **401** com código `auth.invalid_credentials`.
- [ ] **CA-05** — E-mail inexistente retorna **401** com **corpo byte a byte idêntico** ao de CA-04.
- [ ] **CA-06** — Usuário inativo, com senha **correta**, retorna **401** com o mesmo corpo de CA-04 e CA-05 — nunca 403, nunca mensagem sobre conta desativada (RN-USER-04 + RN-AUTH-09).
- [ ] **CA-07** — A resposta de erro **não** revela se o e-mail existe, em nenhum campo (`detail`, `title`, `type`, cabeçalho).
- [ ] **CA-08** — O tempo de resposta para e-mail inexistente é da mesma ordem de grandeza do tempo para senha incorreta (hash dummy executado) — verificado por teste comparando medianas de N execuções com tolerância larga, ou por asserção de que o caminho de hash dummy foi invocado.
- [ ] **CA-09** — A resposta **não** contém hash de senha nem qualquer campo além do contrato acima.
- [ ] **CA-10** — Cada login cria uma **nova** sessão/refresh token; dois logins do mesmo usuário produzem refresh tokens diferentes e **ambos válidos** (D-15, múltiplas sessões).
- [ ] **CA-10b** — Respostas de **falha** (401, 429) **não** emitem `Set-Cookie` — só o login bem-sucedido cria sessão.
- [ ] **CA-11** — Requisição com `email` ou `password` ausentes retorna **400** (validação), distinguível do 401.
- [ ] **CA-12** — Nenhum log produzido pelo login contém a senha.
- [ ] **CA-13** — A resposta traz `Cache-Control: no-store`.

## Testes obrigatórios

- Unidade: `LoginHandler` — CA-04, CA-05, CA-06, CA-10, e a invocação do hash dummy (CA-08).
- Integração: CA-01 a CA-03, CA-07, CA-09, CA-11 a CA-13.
- **Teste explícito comparando os corpos de resposta** dos três cenários de falha (CA-05/CA-06) — é o guardião de RN-AUTH-09.
