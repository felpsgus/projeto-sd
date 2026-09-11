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

- [ ] **CA-01** — Um token gerado é validado com sucesso pelo pipeline da própria API.
- [ ] **CA-02** — Subir a aplicação sem `Jwt:SigningKey`, sem `Issuer` ou sem `Audience` **falha na inicialização** com mensagem indicando a chave faltante.
- [ ] **CA-03** — Subir com `Jwt:SigningKey` menor que 32 bytes falha na inicialização.
- [ ] **CA-04** — O token contém `sub` igual ao `Id` do usuário e `email` igual ao e-mail normalizado.
- [ ] **CA-05** — O token **não** contém o hash da senha nem qualquer dado sensível (inspeção do payload decodificado).
- [ ] **CA-06** — `exp - iat` corresponde a `Jwt:AccessTokenMinutes` (verificado com o valor padrão de 15 e com um valor alternativo configurado).
- [ ] **CA-07** — Um token expirado é **rejeitado** — inclusive 1 segundo após a expiração (`ClockSkew` zero), verificado avançando o `TimeProvider`.
- [ ] **CA-08** — Um token assinado com outra chave é rejeitado com **401**.
- [ ] **CA-09** — Um token com `iss` ou `aud` diferentes do configurado é rejeitado com **401**.
- [ ] **CA-10** — Um token com payload alterado (assinatura quebrada) é rejeitado com **401**.
- [ ] **CA-11** — Cada token emitido tem um `jti` distinto.
- [ ] **CA-12** — O Swagger em `Development` oferece o botão de autorização Bearer e consegue chamar um endpoint protegido.
- [ ] **CA-13** — Nenhum token aparece em log, nem em nível `Debug`.
- [ ] **CA-14** — `Jwt:SigningKey` está configurada **apenas** no Identity Service: nenhum `appsettings*.json`, variável de ambiente ou `.csproj` do Tasks Service a referencia (**D-31**). Verificado por varredura no CI, junto das de [BE-24](BE-24-observabilidade-ci.md).
- [ ] **CA-15** — O Tasks Service **não** registra autenticação JWT Bearer e sobe normalmente sem nenhuma chave `Jwt:*` definida.

## Testes obrigatórios

- Unidade: `TokenService` com `TimeProvider` fake — CA-04, CA-05, CA-06, CA-11.
- Integração: validação no pipeline — CA-01, CA-07 a CA-10.
- Integração: falha de inicialização por configuração ausente — CA-02, CA-03.

## Decisões em aberto

- **D-02** — Duração do access token. Padrão adotado: 15 minutos, configurável.
- **D-31** — ✅ decidida: a chave de assinatura não sai do Identity; validação externa é por `ValidateToken`. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
