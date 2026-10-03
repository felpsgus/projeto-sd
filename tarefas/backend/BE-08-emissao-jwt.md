# BE-08 — Emissão e validação de access token (JWT)

| | |
|---|---|
| **Domínio** | Autenticação |
| **Serviço** | **Identity, exclusivamente** — a chave de assinatura não sai daqui (**D-31**) |
| **Depende de** | [BE-01](BE-01-fundacao-solution.md), [BE-04](BE-04-dominio-usuario.md) |
| **Bloqueia** | BE-09, BE-10, BE-12, BE-13, [BE-33](BE-33-login-minimo-grpc.md), [BE-34](BE-34-validate-token-real.md) |
| **Regras cobertas** | RN-AUTH-10 (parte do access token), RN-AUTH-11 |
| **Estimativa** | M |

> **T2:** o recorte que entra nesta etapa é `JwtOptions`, `ITokenService` (implementado com `JsonWebTokenHandler` de `Microsoft.IdentityModel.JsonWebTokens`) e os `TokenValidationParameters` compartilhados — são a base de [BE-33](BE-33-login-minimo-grpc.md) (emissão) e [BE-34](BE-34-validate-token-real.md) (validação real via `ValidateToken`). O registro de autenticação JWT Bearer no pipeline do Identity e o Swagger com botão de autorização (**CA-12**) ficam para quando o Identity tiver um endpoint REST protegido — hoje ele só expõe `/health` e gRPC, então não há pipeline HTTP autenticado para registrar ainda. A varredura de **CA-14** (nenhuma referência a `Jwt:*` fora do Identity) passa a incluir também o **API Gateway**: nenhum `appsettings*.json`, variável de ambiente ou `.csproj` do Gateway referencia `Jwt:SigningKey` nem qualquer outra chave `Jwt:*`.

## Objetivo

O **Identity Service** sabe emitir e validar um access token JWT de vida curta, com a configuração de assinatura e duração externalizada — e é o **único** serviço capaz de fazer as duas coisas.

## Escopo

### Inclui

- `JwtOptions` tipado (`IOptions<JwtOptions>`) com validação na inicialização:

  | Chave | Padrão | Regra |
  |---|---|---|
  | `Jwt:Issuer` | — | obrigatório |
  | `Jwt:Audience` | — | obrigatório |
  | `Jwt:SigningKey` | — | obrigatório, **≥ 32 bytes**, vem de variável de ambiente/secret |
  | `Jwt:AccessTokenMinutes` | **15** | 1–60 (RN-AUTH-11 / D-02) |
  | `Jwt:RefreshTokenDays` | **7** | usado por BE-10 (D-10) |

- Abstração `ITokenService` em `Application`, implementada em `Infrastructure`:
  - `AccessToken GenerateAccessToken(User user)` retornando o token e o `expiresAt`.
- Claims do access token: `sub` (id do usuário), `email`, `jti`, `iat`, `exp`, `iss`, `aud`. **Nada mais** — sem nome de exibição, sem dados que mudam.
- Autenticação JWT Bearer registrada no pipeline **do Identity**, com validação de issuer, audience, assinatura, tempo de vida e `ClockSkew` **zero** (o padrão de 5 minutos anularia a expiração curta de 15 min).
- Política de autorização padrão exigindo usuário autenticado, usada pelos endpoints autenticados do próprio Identity (BE-13).
- OpenAPI configurado com o esquema de segurança Bearer, para o Swagger permitir testar endpoints protegidos.

### Não inclui

- Refresh token (BE-10) — aqui só o access token.
- Endpoint de login (BE-09).
- **A implementação real de `ValidateToken`** ([BE-26](BE-26-identity-servidor-grpc.md)) — é ela que dá acesso à validação para quem está fora do Identity, e entra na etapa do API Gateway. Aqui se produz a capacidade de validar; expô-la por gRPC é outra task.
- **Qualquer distribuição da chave de assinatura para outro serviço** (**D-31**).

## Notas técnicas

- Algoritmo: **HS256** com chave simétrica, e a chave **fica só no Identity** (**D-31**).
- **A justificativa original mudou.** Ela era "emissor e validador são o mesmo serviço", o que deixou de valer com a separação em dois serviços. O algoritmo continua o mesmo por outra razão: em vez de distribuir a chave para o Tasks (ou para o Gateway), quem precisa validar um token **pergunta ao Identity** via `ValidateToken`. Assim continua havendo um único validador, e a premissa do HS256 volta a ser verdadeira — agora por desenho, não por acaso.
- **Chave simétrica é chave de emissão.** Com HS256, quem consegue validar consegue assinar: entregar `Jwt:SigningKey` a outro serviço o promove a emissor de tokens. É por isso que a chave não sai daqui, e não por excesso de zelo.
- Se algum dia a validação precisar ser local em vários serviços, o caminho é **RS256** com chave pública publicada — não HS256 compartilhado (**D-31**).
- A chave de assinatura **NUNCA** é versionada. `appsettings.json` traz a chave vazia ou ausente; a ausência **falha a inicialização** (CA-02).
- `ClockSkew = TimeSpan.Zero` é obrigatório: com o padrão do framework, um token de 15 minutos seria aceito por 20.
- `TimeProvider` é usado para calcular `exp` — permite testar expiração sem esperar.

## Critérios de aceite

- [x] **CA-01** — Um token gerado é validado com sucesso pelo pipeline da própria API.
- [x] **CA-02** — Subir a aplicação sem `Jwt:SigningKey`, sem `Issuer` ou sem `Audience` **falha na inicialização** com mensagem indicando a chave faltante.
- [x] **CA-03** — Subir com `Jwt:SigningKey` menor que 32 bytes falha na inicialização.
- [x] **CA-04** — O token contém `sub` igual ao `Id` do usuário e `email` igual ao e-mail normalizado.
- [x] **CA-05** — O token **não** contém o hash da senha nem qualquer dado sensível (inspeção do payload decodificado).
- [x] **CA-06** — `exp - iat` corresponde a `Jwt:AccessTokenMinutes` (verificado com o valor padrão de 15 e com um valor alternativo configurado).
- [x] **CA-07** — Um token expirado é **rejeitado** — inclusive 1 segundo após a expiração (`ClockSkew` zero), verificado avançando o `TimeProvider`.
- [x] **CA-08** — Um token assinado com outra chave é rejeitado com **401**.
- [x] **CA-09** — Um token com `iss` ou `aud` diferentes do configurado é rejeitado com **401**.
- [ ] **CA-10** — Um token com payload alterado (assinatura quebrada) é rejeitado com **401**.
- [x] **CA-11** — Cada token emitido tem um `jti` distinto.
- [x] **CA-12** — O Swagger em `Development` oferece o botão de autorização Bearer e consegue chamar um endpoint protegido. *(Atendido em 03/10/2026, issue #3: o OpenAPI do Gateway declara o esquema `Bearer` e o exige em toda operação sem `AllowAnonymous`; coberto por `OpenApiDocumentTests.Esquema_bearer_e_exigido_so_nas_operacoes_autenticadas`. A chamada pela tela do Scalar não foi exercitada em navegador.)*
- [x] **CA-13** — Nenhum token aparece em log, nem em nível `Debug`.
- [x] **CA-14** — `Jwt:SigningKey` está configurada **apenas** no Identity Service: nenhum `appsettings*.json`, variável de ambiente ou `.csproj` do Tasks Service a referencia (**D-31**). Verificado por varredura no CI, junto das de [BE-24](BE-24-observabilidade-ci.md).
- [x] **CA-15** — O Tasks Service **não** registra autenticação JWT Bearer e sobe normalmente sem nenhuma chave `Jwt:*` definida.

## Testes obrigatórios

- Unidade: `TokenService` com `TimeProvider` fake — CA-04, CA-05, CA-06, CA-11.
- Integração: validação no pipeline — CA-01, CA-07 a CA-10.
- Integração: falha de inicialização por configuração ausente — CA-02, CA-03.

## Decisões em aberto

- **D-02** — Duração do access token. Padrão adotado: 15 minutos, configurável.
- **D-31** — ✅ decidida: a chave de assinatura não sai do Identity; validação externa é por `ValidateToken`. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).

> **Emenda (21/09/2026).** O enunciado do T2 mudou e passa a exigir validação de JWT no middleware do **Gateway** — incompatível com HS256 (D-31 dependia de a validação externa passar sempre por `ValidateToken`). [BE-40](BE-40-jwt-rs256-e-persisted-padrao.md) substitui `Jwt:SigningKey` (HS256) por `Jwt:PrivateKeyPath` (RSA, RS256) e adiciona `kid`/thumbprint. **CA-02, CA-03, CA-08, CA-14 desta task são superados por BE-40/D-38**: a validação de configuração passa a ser sobre a chave privada RSA (existência, legibilidade, parseabilidade, ≥ 2048 bits), não sobre o tamanho em bytes de uma chave simétrica; "assinado com outra chave" (CA-08) passa a ser testado com outro par RSA, não outra string HS256; e a varredura de CA-14 passa a procurar `Jwt:PrivateKeyPath`/`Jwt:PublicKeyPath`, não `Jwt:SigningKey`. O texto e os CAs acima ficam como registro do desenho até 20/09/2026.

## Auditoria dos critérios (03/10/2026)

Critérios conferidos contra o código em 03/10/2026. Marcados: 13 de 15.

| CA | Situação | Evidência / motivo |
|---|---|---|
| CA-02 | atendido em outro lugar | HS256/`Jwt:SigningKey` foi substituído por RS256 (D-38). Falha na inicialização sem `Issuer`, `Audience` ou `PrivateKeyPath`: `Identity.UnitTests/Security/JwtOptionsValidationTests`. Falha no Gateway sem `PublicKeyPath`: `JwtStartupValidationTests`. |
| CA-03 | atendido em outro lugar | O mínimo de 32 bytes virou "RSA com pelo menos 2048 bits" (D-38): `Host_ComChaveRsaDe1024Bits_FalhaAoIniciarComMensagemIndicandoOMinimo`. |
| CA-10 | em aberto | Falta teste com payload adulterado ou token malformado. `AuthenticationTests` cobre HS256, `alg=none` e outra chave RSA, mas não um token com o payload alterado e a assinatura original. |
| CA-12 | em aberto | O Gateway não declara esquema de segurança Bearer no OpenAPI (nenhum `SecurityScheme` em `src/Gateway`). Scalar em Development não tem botão de autorização, e nada o testa. |
| CA-14 | atendido em outro lugar | Com D-38, a chave que importa é a privada: só o Identity a tem (`Jwt:PrivateKeyPath`). O Gateway recebe só a pública. `CodigoDoTasks_NaoReferenciaJwtSigningKey` varre `src/Tasks` e `deploy/tasks.env.example`. |
