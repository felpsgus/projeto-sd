# BE-33 — Login mínimo via gRPC

| | |
|---|---|
| **Domínio** | Autenticação |
| **Serviço** | Identity |
| **Depende de** | [BE-06](BE-06-hash-senha.md), [BE-08](BE-08-emissao-jwt.md), [BE-32](BE-32-contratos-grpc-t2.md) |
| **Bloqueia** | [BE-36](BE-36-api-gateway.md), [BE-39](BE-39-verificacao-t2.md) |
| **Regras cobertas** | RN-AUTH-08, RN-AUTH-09, RN-USER-04 |
| **Estimativa** | M |

## Objetivo

Um usuário ativo troca e-mail e senha, via RPC `Login`, por um access token válido — sem revelar por resposta ou tempo qual das três causas de falha (e-mail inexistente, senha errada, usuário inativo) ocorreu.

## Escopo

### Inclui

- **Este é um recorte de [BE-09](BE-09-login.md)** (D-36): só o access token, sem refresh token, sem cookie (BE-10) e sem bloqueio por tentativas (BE-12). BE-09 continua aberta para ser completada com o restante quando essas tasks entrarem.
- `LoginHandler` em `TodoList.Identity.Application`:
  1. normaliza o e-mail com `Email.Create` e busca o usuário via `IUserRepository.GetByEmailAsync` (já existe na interface — conferir antes de adicionar de novo);
  2. usuário não encontrado → executa `IPasswordHasher.Verify` contra um **hash dummy fixo** (não contra a senha informada de qualquer usuário real), só para consumir o mesmo tempo de CPU do caminho de sucesso — nunca pular a verificação (mesma armadilha documentada em BE-09, RN-AUTH-09);
  3. usuário encontrado → `IPasswordHasher.Verify(senhaInformada, user.PasswordHash)`;
  4. verifica `user.IsActive` (RN-USER-04);
  5. em qualquer desfecho negativo dos passos 2–4, retorna o mesmo resultado de falha — o handler tem **um único caminho de retorno de falha**, não um por causa;
  6. em sucesso, emite o access token via `ITokenService.GenerateAccessToken(user)` (BE-08).
- Exposto como `IdentityGrpcService.Login(LoginRequest) : Task<LoginResponse>`, no mesmo estilo de `ValidateUser`/`ValidateToken`: nunca lança, nunca devolve status gRPC de erro para credencial inválida — sempre `OK` com `succeeded=false` (BE-32).
- Log da tentativa: `userId` (quando resolvido), `succeeded`, `traceId` — mesmo padrão de log já usado em `ValidateUser` (`Log.ValidateUserCalled`). **Nunca** e-mail, senha, hash ou token no log.
- **Seed de demonstração (`DemoUserSeeder`) passa a gerar hash real:**
  - a senha de demonstração vem de `UserStore:DemoUserPassword`, lida em texto puro só na inicialização, nunca versionada — vem de variável de ambiente (mesmo padrão de `Jwt:SigningKey`, BE-08);
  - **obrigatória quando `UserStore:SeedDemoUsers=true`**: sem ela, a inicialização **falha** (mesmo padrão de BE-08 CA-02 para `Jwt:SigningKey` ausente) — o serviço não sobe com seed ligado e senha ausente, para não silenciosamente semear usuários que ninguém consegue autenticar;
  - `DemoUserSeeder` passa a chamar `IPasswordHasher.Hash(demoPassword)` no lugar de `PlaceholderPasswordHash`;
  - **o seed sincroniza a senha dos usuários de demonstração que já existem**: para cada um, se `IPasswordHasher.Verify(demoPassword, user.PasswordHash)` for `false`, o hash é regravado com `IPasswordHasher.Hash(demoPassword)` via `User.ChangePasswordHash`; se for `true`, nada é escrito (continua idempotente). É necessário porque o seed é idempotente por id (BE-26): sem isso, os dois usuários já semeados no T1 — com o placeholder, que não é hash válido e portanto nunca passa em `Verify` (BE-06 CA-03) — nunca ganhariam senha utilizável no T2. A mesma regra cobre a troca de `DemoUserPassword` entre implantações.
  - `PlaceholderPasswordHash` **deixa de existir no código** — a detecção acima não depende de conhecer o valor antigo, então nenhuma referência sobra depois desta task.

### Não inclui

- Refresh token, cookie, rotação (BE-10).
- Bloqueio por tentativas de login (BE-12).
- Qualquer endpoint REST de login no Identity — a borda REST é o **API Gateway**, `POST /api/auth/login` ([BE-36](BE-36-api-gateway.md)), que traduz a requisição HTTP para este RPC.

## Notas técnicas

- **Login exige `UserStore:Provider=Persisted`.** Com `UserStore:Provider=InMemory`, não existe senha nem hash associado aos usuários semeados em memória (`InMemoryUserLookup` só guarda `Active`/`DisplayName`, sem credencial) — não há o que verificar.
  - **Decisão adotada e a justificativa:** com `InMemory`, `Login` responde `succeeded=false` para qualquer entrada e loga um **aviso** (não erro) explicando que o modo não suporta autenticação. **Não falha a inicialização** e **não lança**, pelo mesmo motivo de sempre: uma falha de negócio não é uma exceção. A alternativa — falhar explicitamente ao subir com `InMemory` — foi descartada porque o modo `InMemory` continua servindo `ValidateUser` para cenários de demonstração do T1 que não envolvem login, e o README documenta essa limitação (`Login` sempre nega com `InMemory`) para quem for reproduzir o ambiente.
- O hash dummy do passo 2 é fixo e gerado uma vez (por exemplo, no início da aplicação ou como constante calculada), não recalculado por requisição — o único requisito é que o custo de `Verify` seja pago mesmo sem usuário.
- `IPasswordHasher` e `ITokenService` já são as abstrações de BE-06/BE-08; esta task não cria nenhuma abstração nova, só o caso de uso que as compõe.

## Critérios de aceite

- [ ] **CA-01** — Login com e-mail e senha corretos, usuário ativo, `Provider=Persisted` → `succeeded=true`, `access_token` preenchido e válido (aceito pela mesma validação de BE-08), `expires_at` e `user_id` corretos.
- [ ] **CA-02** — Senha incorreta → `succeeded=false`, sem `access_token`.
- [ ] **CA-03** — E-mail inexistente → resposta **idêntica** (mesmos campos, mesmos valores) à de CA-02.
- [ ] **CA-04** — Usuário inativo, senha correta → resposta **idêntica** à de CA-02 e CA-03 (RN-USER-04 + RN-AUTH-09).
- [ ] **CA-05** — O tempo de resposta para e-mail inexistente é da mesma ordem de grandeza do tempo para senha incorreta (hash dummy executado) — mesmo critério de BE-09 CA-08.
- [ ] **CA-06** — Nenhum log produzido pelo `Login` contém e-mail, senha, hash de senha ou o token emitido — só `userId` (quando resolvido), `succeeded` e `traceId`.
- [ ] **CA-07** — Com `UserStore:SeedDemoUsers=true` e `UserStore:DemoUserPassword` ausente, a inicialização do Identity **falha** com mensagem indicando a configuração faltante.
- [ ] **CA-08** — Com `UserStore:SeedDemoUsers=true` e `DemoUserPassword` presente, os dois usuários de demonstração autenticam com essa senha via `Login` (CA-01).
- [ ] **CA-09** — Rodar o seed sobre um banco que já tem os dois usuários com o hash placeholder do T1 regrava o hash de ambos para um hash real — verificado consultando o banco antes/depois. Rodar o seed de novo, com a mesma senha, **não** escreve nada (idempotência); com outra `DemoUserPassword`, regrava.
- [ ] **CA-10** — Nenhuma referência a `PlaceholderPasswordHash` sobra no código após esta task.
- [ ] **CA-11** — Com `UserStore:Provider=InMemory`, `Login` responde `succeeded=false` para qualquer entrada (inclusive credenciais que seriam válidas em `Persisted`) e o aviso de que o modo não suporta autenticação é emitido **uma única vez, na inicialização** — não a cada chamada. Limitação registrada no README.
- [ ] **CA-12** — `Login` nunca lança exceção nem devolve status gRPC diferente de `OK` por causa do conteúdo do request — inclusive e-mail vazio, malformado ou senha vazia, que resultam em `succeeded=false` como qualquer credencial inválida; erro de infraestrutura (ex.: banco indisponível) é o único caminho que pode propagar como falha de RPC.

## Testes obrigatórios

- Unidade: `LoginHandler` — CA-02 a CA-05 (com `TimeProvider`/`IPasswordHasher` de teste, custo reduzido — mesma exigência de BE-06), CA-11.
- Integração: servidor gRPC real — CA-01, CA-06, CA-12.
- Integração: inicialização do host — CA-07, CA-08.
- Integração: seed sobre banco pré-existente com placeholder — CA-09, CA-10 (busca textual no repositório confirmando ausência do símbolo).
- **Teste explícito comparando os corpos de resposta** dos três cenários de falha (CA-02/CA-03/CA-04) — mesmo guardião de RN-AUTH-09 já exigido em BE-09.

## Decisões em aberto

- **D-36** — Recorte do T2 para BE-09: só access token via gRPC, Gateway como borda REST. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
