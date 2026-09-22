# BE-40 — JWT RS256 validado no Gateway e `Persisted` como padrão

> **Emenda ao T2 (21/09/2026).** O enunciado do T2 mudou: exige middleware/filtro de autenticação **no próprio API Gateway** validando o JWT (requisito 6 de `t2.md`), e proíbe qualquer dado em memória na demonstração (requisito 4 de `t2.md`). Esta task refaz o desenho de **D-31** (chave só no Identity, validação por `ValidateToken` gRPC) para **D-38** (chave assimétrica, validação local no Gateway) e fecha **D-39** (banco real por padrão). Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).

| | |
|---|---|
| **Domínio** | Autenticação / Borda |
| **Serviço** | Identity, Gateway |
| **Depende de** | [BE-08](BE-08-emissao-jwt.md), [BE-34](BE-34-validate-token-real.md), [BE-36](BE-36-api-gateway.md) |
| **Bloqueia** | [BE-42](BE-42-nginx-mesma-origem.md), o recorte FE do T2 (front consome `/api` autenticado via Gateway) |
| **Regras cobertas** | RN-AUTH-08, RN-AUTH-09, RN-AUTH-11, RN-AUTZ-04 |
| **Estimativa** | M |

## Objetivo

O Gateway valida o JWT **localmente**, no próprio middleware de autenticação (`AddJwtBearer`), sem perguntar ao Identity a cada requisição. O Identity assina com uma chave privada RSA que nunca sai dele; o Gateway só tem a chave pública correspondente. Os dados de demonstração vêm só do Postgres — `Persisted` é o padrão do Identity, e `InMemory` fica restrito a testes.

## Escopo

### Inclui

**Identity — `JwtOptions` (substitui o desenho de [BE-08](BE-08-emissao-jwt.md))**

| Chave | Antes (HS256) | Agora (RS256) |
|---|---|---|
| `Jwt:Issuer` | obrigatório | obrigatório (sem mudança) |
| `Jwt:Audience` | obrigatório | obrigatório (sem mudança) |
| `Jwt:SigningKey` | obrigatório, ≥ 32 bytes | **removida** |
| `Jwt:PrivateKeyPath` | — | **nova**, obrigatória — caminho de um arquivo PEM PKCS#8 de chave privada RSA |
| `Jwt:AccessTokenMinutes` | 15 (D-02) | sem mudança |
| `Jwt:RefreshTokenDays` | 7 (D-10) | sem mudança (ainda não emitido, BE-10) |

- Validação na inicialização (`IValidatableObject`, mesmo padrão de antes): o arquivo em `PrivateKeyPath` **existe**, é **legível** (sem exceção de permissão), é **parseável** como PEM PKCS8 e a chave é **RSA com no mínimo 2048 bits** (`RSA.KeySize >= 2048`). Falha em qualquer uma dessas quatro condições **falha a inicialização** com uma mensagem que nomeia `Jwt:PrivateKeyPath` — **nunca** o conteúdo do arquivo, nem parte dele, nem o caminho absoluto resolvido se ele tiver sido logado por engano em outro lugar.
- `kid` no header do JWT: derivado do **thumbprint da chave pública** (RFC 7638, JWK Thumbprint — SHA-256 sobre a forma canônica do JWK RSA público `{e, kty, n}`), calculado uma vez na inicialização e reaproveitado em todo token emitido. Um único `kid` por processo — sem rotação nesta etapa (ver Notas técnicas).
- Algoritmo **só RS256** — `SecurityAlgorithms.RsaSha256`, sem caminho de fallback para HS256 em nenhuma configuração ou ambiente.
- `ITokenService`/`JwtTokenService` (BE-08) passa a assinar com `RsaSecurityKey` construída a partir da chave privada carregada, em vez de `SymmetricSecurityKey`.
- `ValidateToken` ([BE-34](BE-34-validate-token-real.md)) **continua existindo** no Identity e continua validando — agora com a parte pública da mesma chave — mas **deixa de ter consumidor**: o Gateway não o chama mais (ver abaixo). Fica como capacidade do Identity (ex.: para uso interno futuro), não como o mecanismo de autenticação do sistema.

**Gateway — nova seção `Jwt` e `AddJwtBearer` local**

| Chave | Regra |
|---|---|
| `Jwt:Issuer` | obrigatório, mesmo valor do Identity |
| `Jwt:Audience` | obrigatório, mesmo valor do Identity |
| `Jwt:PublicKeyPath` | obrigatório — caminho de um arquivo PEM **SubjectPublicKeyInfo** (a chave pública correspondente à privada do Identity) |

- Validação na inicialização, espelhando a do Identity: arquivo existe, é legível, é parseável como PEM SPKI e é RSA (a checagem de bits mínimos não se aplica aqui — o Gateway não gera a chave, só a consome; se ela for fraca, o problema já falhou na inicialização do Identity).
- `builder.Services.AddAuthentication().AddJwtBearer(...)` substitui o esquema `"IdentityToken"`/`IdentityTokenAuthenticationHandler` inteiro. Configuração do `TokenValidationParameters`:
  - `ValidAlgorithms = [SecurityAlgorithms.RsaSha256]` — recusa qualquer outro algoritmo, inclusive `none` e HS256, mesmo que o token venha com assinatura sintaticamente válida para outro algoritmo;
  - `ValidateIssuer = true`, `ValidIssuer` = `Jwt:Issuer`;
  - `ValidateAudience = true`, `ValidAudience` = `Jwt:Audience`;
  - `ValidateLifetime = true`;
  - `IssuerSigningKey` = a `RsaSecurityKey` pública carregada de `Jwt:PublicKeyPath` (`ValidateIssuerSigningKey = true`);
  - `RequireSignedTokens = true`, `RequireExpirationTime = true`;
  - `ClockSkew = TimeSpan.Zero` (mesma razão de BE-08: um access token de 15 min não pode ganhar 5 de tolerância);
  - `MapInboundClaims = false` — sem isso, o handler renomeia `sub` para o URI longo de claim do .NET (`ClaimTypes.NameIdentifier`), e o código que lê o claim `sub` (interceptor `x-user-id`) pararia de encontrá-lo silenciosamente.
- `IdentityTokenAuthenticationHandler` e o esquema `"IdentityToken"` (`Authentication/IdentityAuthenticationDefaults.cs`, `Authentication/IdentityTokenAuthenticationHandler.cs`) são **removidos**. A chamada `ValidateToken` via `IIdentityBackend` sai do caminho de toda requisição autenticada — ela deixa de existir no `Backends/` do Gateway (o cliente gRPC do Identity continua existindo só para `POST /api/auth/login`).
- A *fallback policy* (opt-out, usuário autenticado por padrão) **permanece exatamente como está** ([BE-36](BE-36-api-gateway.md)) — só o mecanismo por trás dela muda, de `AuthenticationHandler` customizado para `AddJwtBearer`.
- O 401 continua `ProblemDetails` com `errorCode=auth.unauthorized`, sem revelar o motivo (token ausente vs. expirado vs. assinatura inválida vs. claims erradas) — agora produzido via `JwtBearerEvents.OnChallenge`, que sobrescreve a resposta padrão do middleware (que, por si, varia o texto conforme a causa) para escrever o mesmo corpo que `IdentityTokenAuthenticationHandler.HandleChallengeAsync` escrevia antes.
- O `x-user-id` repassado ao Tasks (interceptor de cliente, D-34) continua vindo do claim `sub` do `ClaimsPrincipal` — a fonte do claim muda (token validado localmente em vez de resposta de `ValidateToken`), o nome do claim e o código que o lê **não mudam**.

**Consequência para o caminho de falha (documentar em [BE-39](BE-39-verificacao-t2.md))**

- Com o Identity **fora do ar**, `POST /api/tasks` com um token válido emitido antes da queda **continua** dando **503 + `Retry-After`** — mas agora por outro motivo: o Gateway valida o token sozinho (não precisa do Identity para isso) e a chamada chega ao Tasks; é o Tasks que, ao chamar `ValidateUser` no Identity para confirmar que o dono existe e está ativo (BE-28), recebe `Unavailable` e devolve `Unavailable`/503 pelo mapeamento de erro (D-35). O 503 nesse caminho não é mais "não consegui validar o token" — é "não consegui confirmar o dono".
- `POST /api/auth/login` com o Identity fora do ar continua dando **503**: o RPC `Login` não muda, e o Gateway ainda depende do Identity para autenticar (emitir o token é a única coisa que só o Identity faz).
- Essa é uma mudança de **causa**, não de status observado — os CAs de disponibilidade de [BE-36](BE-36-api-gateway.md) (CA-13, CA-24) continuam válidos como comportamento externo, mas a nota técnica que os explica muda: **CA-13 é superado por esta task** (ver seção de emenda em BE-36).

**Configuração — `UserStore:Provider` (D-39)**

- `appsettings.json` do Identity: `UserStore:Provider` passa de `"InMemory"` para **`"Persisted"`** como padrão.
- `InMemory` **fica restrito a testes** — só configurado explicitamente (ex.: `appsettings.Testing.json`, `WebApplicationFactory` de teste de integração) e **nenhum ambiente de deploy** (VM, compose, T3) usa `InMemory` em nenhuma camada, nem como fallback silencioso.

**Chaves**

- **VM:** geradas na própria VM, nunca fora dela e nunca copiadas de um ambiente de teste:

  ```bash
  openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out /etc/todolist/jwt/private.pem
  openssl pkey -in /etc/todolist/jwt/private.pem -pubout -out /etc/todolist/jwt/public.pem
  ```

  - **Atenção (revisão do tech lead):** as três units rodam hoje como o **mesmo** usuário `todolist` (`deploy/todolist-*.service`), então permissão de arquivo por dono **não** isola a chave privada do Gateway e do Tasks. Por isso:
    - `private.pem` fica com dono `root`, modo **`0400`**, e chega **só** à unit do Identity via **`LoadCredential=jwt-private:/etc/todolist/jwt/private.pem`** do systemd; o Identity lê de `Jwt__PrivateKeyPath=${CREDENTIALS_DIRECTORY}/jwt-private` (o systemd expõe a cópia apenas ao processo daquela unit);
    - `public.pem` fica com modo `0444` (não é segredo), lida pelo Gateway direto do caminho (`Jwt__PublicKeyPath=/etc/todolist/jwt/public.pem`), ou pelo mesmo mecanismo `LoadCredential=` por simetria;
  - **nunca versionadas** — `*.pem` entra no `.gitignore` da raiz.
- **Dev local (`dotnet run`) e testes:** o README orienta gerar o par com `openssl` numa pasta ignorada pelo git (`.secrets/jwt/`); os testes de integração geram o par RSA **em memória** no fixture e gravam num arquivo temporário (substituem o `TestJwtSigningKeySetup` do HS256) — nenhuma chave de teste é versionada.
- **Compose:** as chaves vivem numa pasta local ignorada pelo git (ex.: `.secrets/jwt/`), montada **read-only** nos containers de Identity e Gateway (`private.pem` só no Identity, `public.pem` nos dois — o Gateway só precisa da pública).
- **T3:** Secret Manager, montado como arquivo (volume) no Cloud Run — não como variável de ambiente com o conteúdo do PEM inline (evita o PEM aparecer em `gcloud run services describe` ou em log de deploy).
- **Rotação de chave fica fora do escopo desta task** — um único `kid` por processo, sem endpoint JWKS, sem chave secundária de transição. Se a chave precisar trocar, é reiniciar os dois serviços com o par novo — todo token emitido antes vira inválido, aceitável nesta escala (mesma lógica de "trocar a chave invalida todo token emitido antes" que já valia para o HS256 de BE-37).

### Não inclui

- Endpoint JWKS (`/.well-known/jwks.json`) — a chave pública chega ao Gateway por arquivo, não por descoberta HTTP. Fica registrado como melhoria possível se um dia houver mais de um consumidor da chave pública.
- Rotação de chave, múltiplos `kid` simultâneos, período de graça de chave antiga.
- Qualquer mudança no formato dos claims do token (`sub`, `email`, `jti`, `iat`, `exp`, `iss`, `aud` continuam os mesmos de BE-08) — só o algoritmo e onde a validação acontece mudam.
- Cache de resultado de validação no Gateway — não existe mais "resultado" para cachear: a validação de assinatura/claims é local e barata (é exatamente o motivo de D-38 substituir D-31 aqui).
- Bearer no pipeline HTTP do próprio Identity (segue fora do recorte do T2, D-36) — esta task troca o *algoritmo* de assinatura e o *lugar* onde a validação de borda acontece, não introduz autenticação HTTP no Identity.

## Notas técnicas

- **Por que RS256 agora, e não HS256 continuado.** A razão histórica de D-31 (HS256 porque só o Identity validava) deixou de valer no instante em que o enunciado passou a exigir middleware de JWT **no Gateway**: alguém fora do Identity precisa verificar assinatura. Com HS256, isso significaria distribuir a chave simétrica — que também assina — ao Gateway, tornando-o um segundo emissor em potencial. RS256 resolve exatamente esse problema: o Gateway ganha só a metade que verifica, nunca a que assina. É a migração que o próprio BE-08 já previa ("migrar para RS256 se houver mais de um consumidor") e que D-31 explicitamente adiou por não haver, até agora, mais de um consumidor real.
- **Por que a assimetria continua sendo o ponto central, não um detalhe de biblioteca.** O espírito de D-31 — "quem pode emitir é só o Identity" — não muda. O que muda é o mecanismo: em vez de "só o Identity pode *verificar* (e portanto perguntamos a ele)", passa a ser "só o Identity tem a chave que *assina*; qualquer um pode verificar com a pública, sem isso virar poder de emitir". A propriedade de segurança que importava (nenhum outro serviço forja token) se mantém; a que deixa de valer (uma única chamada de rede concentra a validação) é a que o enunciado novo tornou incompatível com o requisito de middleware no Gateway.
- **`RequireSignedTokens`/`RequireExpirationTime` são redundantes com `RS256`+`ValidateLifetime`, mas explícitos por defesa em profundidade** — a mesma lógica de listar `ValidAlgorithms` mesmo sabendo que o Identity só emite RS256: um token forjado sem assinatura, ou com `exp` ausente, não deve sobreviver a uma mudança futura e descuidada de configuração.
- **`MapInboundClaims = false` é fácil de esquecer e cala silenciosamente.** Sem ele, o pipeline continua "funcionando" — o token é validado, a autenticação passa — mas o claim que o interceptor de `x-user-id` procura (`sub`) não existe mais no `ClaimsPrincipal` (virou `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`), e toda chamada autenticada ao Tasks chega **sem** `x-user-id`. Esse é o defeito mais provável desta task e precisa de teste dedicado (ver CAs).
- **Por que a mensagem de erro de configuração nunca nomeia o conteúdo da chave.** Mesmo padrão de BE-08/D-31: uma mensagem de erro que ecoasse trechos do PEM (mesmo que truncados) viraria vazamento de material de assinatura em log de inicialização — que é lido por muito mais gente do que o segredo em si.
- **Por que `InMemory` unicamente em teste, e não "desligado por padrão, mas disponível".** O requisito 4 do `t2.md` novo é explícito: dados em memória zeram o requisito de banco **se aparecerem na demonstração**. Deixar `InMemory` disponível como opção de configuração de produção é um risco de operação (bastaria alguém esquecer `UserStore:Provider=Persisted` num `.env` novo) — por isso ele só existe onde não há demonstração ao vivo: a suíte de testes.

## Critérios de aceite

### Identity — emissão RS256

- [ ] **CA-01** — Um token emitido por `ITokenService.GenerateAccessToken` tem o header `alg=RS256` e um `kid` presente.
- [ ] **CA-02** — O `kid` é estável entre chamadas (mesmo processo, mesma chave) e seu valor corresponde ao thumbprint RFC 7638 calculado a partir do `public.pem` correspondente.
- [ ] **CA-03** — Subir o Identity sem `Jwt:PrivateKeyPath` falha na inicialização, com mensagem que nomeia `Jwt:PrivateKeyPath` e não expõe conteúdo de arquivo nenhum.
- [ ] **CA-04** — Subir com `Jwt:PrivateKeyPath` apontando para um arquivo inexistente falha na inicialização.
- [ ] **CA-05** — Subir com um arquivo que existe mas não é um PEM PKCS8 válido (ex.: texto arbitrário) falha na inicialização.
- [ ] **CA-06** — Subir com uma chave RSA de 1024 bits falha na inicialização, com mensagem indicando o tamanho mínimo exigido (2048).
- [ ] **CA-07** — `ValidateToken` (BE-34) continua funcionando fim a fim com a nova chave RS256 — token emitido é validado com sucesso pelo próprio RPC, usando a parte pública.

### Gateway — `AddJwtBearer`

- [ ] **CA-08** — `POST /api/tasks` com um token HS256 (assinado com qualquer chave simétrica, inclusive uma "parecida") é rejeitado com **401**.
- [ ] **CA-09** — Um token com `alg=none` e sem assinatura é rejeitado com **401**.
- [ ] **CA-10** — Um token RS256 assinado por **outra** chave privada (não a do Identity configurado) é rejeitado com **401**.
- [ ] **CA-11** — Um token expirado é rejeitado com **401**, inclusive 1 segundo após `exp` (`ClockSkew` zero).
- [ ] **CA-12** — Um token com `iss` ou `aud` diferentes do configurado no Gateway é rejeitado com **401**.
- [ ] **CA-13** — Um token RS256 válido, emitido pelo Identity com a chave configurada, é aceito e uma criação de tarefa completa retorna **201**.
- [ ] **CA-14** — O `x-user-id` que chega ao Tasks (metadata gRPC) é igual ao claim `sub` do token — verificado com um token cujo `sub` é conhecido, contra o fake gRPC do Tasks (regressão do risco de `MapInboundClaims`).
- [ ] **CA-15** — O corpo do 401 é idêntico (`errorCode=auth.unauthorized`, mesmo formato de `ProblemDetails`) para todos os casos de CA-08 a CA-12 — nenhum distingue a causa.
- [ ] **CA-16** — Subir o Gateway sem `Jwt:PublicKeyPath`, ou apontando para arquivo inexistente/ilegível/não-PEM, falha na inicialização.

### Arquitetura e configuração

- [ ] **CA-17** — Nenhum `appsettings*.json`/variável de ambiente do Gateway contém `Jwt:SigningKey` ou `Jwt:PrivateKeyPath` — só `Jwt:Issuer`, `Jwt:Audience`, `Jwt:PublicKeyPath` (varredura).
- [ ] **CA-18** — Nenhum `appsettings*.json`/variável de ambiente do Tasks Service contém qualquer chave `Jwt:*` — o Tasks continua sem saber nada sobre tokens (varredura, mesmo espírito de BE-08 CA-14/CA-15).
- [ ] **CA-19** — `IdentityTokenAuthenticationHandler` e o esquema `"IdentityToken"` não existem mais no assembly do Gateway (verificado por ausência do tipo/arquivo).
- [ ] **CA-20** — Nenhuma chamada `ValidateToken` (via `IIdentityBackend`) acontece no caminho de uma requisição autenticada do Gateway — verificado no fake do cliente gRPC do Identity (0 invocações em um cenário de sucesso completo).
- [ ] **CA-21** — `UserStore:Provider` no `appsettings.json` do Identity tem o valor **`Persisted`**.
- [ ] **CA-25** — Na VM, o arquivo da chave privada não é legível pelo usuário `todolist` (`sudo -u todolist cat /etc/todolist/jwt/private.pem` falha), e só a unit do Identity a recebe via `LoadCredential=` — verificação manual registrada no PR.
- [ ] **CA-22** — `InMemory` só aparece configurado em arquivos/variáveis usados pela suíte de testes (`appsettings.Testing.json`, fixtures) — nenhum `*.env`/`appsettings.json` de deploy o referencia.

### Caminho de falha (documentado em BE-39)

- [ ] **CA-23** — Com o Identity fora do ar, `POST /api/tasks` com um token RS256 válido (emitido antes da queda) devolve **503** com `Retry-After` — a chamada chega ao Tasks, que falha ao chamar `ValidateUser`.
- [ ] **CA-24** — Com o Identity fora do ar, `POST /api/auth/login` devolve **503** com `Retry-After`.

## Testes obrigatórios

- Identity — unidade: `JwtOptions.Validate` (CA-03 a CA-06), `JwtTokenService` com chave de teste gerada em memória (CA-01, CA-02, CA-07).
- Gateway — integração (`WebApplicationFactory`, chaves de teste geradas no fixture): CA-08 a CA-16, com um conjunto de tokens forjados (HS256, `none`, outra chave RSA, expirado, `iss`/`aud` errados) gerado no próprio teste — nenhum desses tokens é um arquivo versionado.
- Teste de arquitetura/varredura: CA-17, CA-18, CA-19.
- Integração com fake gRPC: CA-14, CA-20, CA-23, CA-24.
- Regressão: a suíte completa de BE-08 e BE-34 é reexecutada e ajustada onde citava HS256/`SigningKey` — nenhum teste antigo deve continuar referenciando uma chave simétrica.

## Decisões em aberto

- **D-38** — JWT RS256; chave privada só no Identity, pública no Gateway, Tasks sem nenhuma chave. Emenda a D-31. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
- **D-39** — `Persisted` como padrão do `UserStore`; `InMemory` restrito a testes. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
