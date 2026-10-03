# BE-34 — `ValidateToken` real

| | |
|---|---|
| **Domínio** | Autenticação / Integração |
| **Serviço** | Identity |
| **Depende de** | [BE-08](BE-08-emissao-jwt.md), [BE-32](BE-32-contratos-grpc-t2.md) |
| **Bloqueia** | [BE-36](BE-36-api-gateway.md) |
| **Regras cobertas** | RN-AUTH-11 |
| **Estimativa** | P |

## Objetivo

`ValidateToken` deixa de ser stub e passa a validar de verdade um access token JWT, sendo o único caminho de validação para quem está fora do Identity (D-31).

## Escopo

### Inclui

- Substituição do stub de `IdentityGrpcService.ValidateToken` ([BE-26](BE-26-identity-servidor-grpc.md), CA-11/CA-12) por uma implementação real:
  1. reutiliza os `TokenValidationParameters` já configurados em [BE-08](BE-08-emissao-jwt.md) (mesmo issuer, audience, chave e `ClockSkew = TimeSpan.Zero`) — **não** cria uma segunda configuração de validação;
  2. valida assinatura, `iss`, `aud` e `exp` do `access_token` recebido;
  3. token válido → `valid=true`, `user_id` extraído do claim `sub`;
  4. token vazio, malformado, expirado ou com assinatura inválida → `valid=false`, `user_id=""`, com status gRPC **`OK`** — nunca exceção, nunca status de erro gRPC (mesmo estilo de `ValidateUser`/`Login`, BE-26/BE-33).
- Log do resultado (`valid`, motivo da rejeição quando houver) — **o token em si nunca aparece em log**, em nenhum nível.
- Consumidor esperado: o middleware de autenticação do **API Gateway** ([BE-36](BE-36-api-gateway.md)), que chama `ValidateToken` a cada requisição de entrada que exige sessão.

### Não inclui

- Emissão de token (BE-08) ou login (BE-33) — esta task só valida.
- Cache do resultado de validação — se a latência incomodar, é trabalho de BE-36 ou de uma task futura, não desta.
- Verificação de `IsActive` do usuário (ver notas técnicas — decisão explícita de **não** fazer isso aqui).
- Qualquer mudança no contrato além do que BE-32 já declarou (`ValidateToken` continua com a mesma assinatura de RPC).

## Notas técnicas

- **`ValidateToken` NÃO verifica se o usuário está ativo (RN-USER-04).** Essa checagem continua acontecendo só na criação da tarefa, via `ValidateUser` ([BE-28](BE-28-validacao-dono-grpc.md)) — decisão deliberada, não omissão:
  - fazer as duas checagens em `ValidateToken` significaria uma consulta ao store de usuários **a cada requisição autenticada** que passa pelo Gateway, não só nas que criam tarefa;
  - a validação de assinatura/expiração de um JWT é local e barata; checar `IsActive` exigiria uma leitura de banco por chamada, o que o desenho de D-31 (uma chamada de rede por requisição de entrada) já tenta manter enxuta;
  - **consequência aceita:** um usuário desativado entre a emissão do token e a checagem em BE-28 continua **passando na autenticação** pelo tempo de vida restante do access token — até **15 minutos** (RN-AUTH-11/D-02) — mas **não consegue criar tarefa**, porque `ValidateUser` (BE-28) checa `active` a cada criação. Autenticar sem poder agir é o limite aceito; qualquer ação que precise saber se o usuário está ativo de verdade passa por BE-28, não por esta task.
- Reaproveitar os `TokenValidationParameters` de BE-08 (não recriar): no T2 este RPC é o **único** usuário deles, porque o Bearer no pipeline do Identity ficou fora do recorte (D-36). Quando o Bearer entrar, ele **DEVE** consumir o mesmo objeto — mesma fonte de verdade, sem duplicar `ClockSkew`, issuer ou audience em dois lugares.
- **Armadilha de tempo:** o `JsonWebTokenHandler` decide expiração pelo relógio do sistema, não pelo `TimeProvider` injetado. Para o CA-04 ser verificável com `FakeTimeProvider`, a checagem de vida útil **DEVE** consultar o `TimeProvider` — via `TokenValidationParameters.LifetimeValidator` que compara `exp` com `timeProvider.GetUtcNow()`, ou pela propriedade de tempo que a versão do pacote oferecer. Um teste que só passa porque o token foi gerado com `exp` no passado real não prova nada.
- Erro de validação (motivo: expirado, assinatura inválida, claims incorretas) fica só no log estruturado, nunca no `ValidateTokenResponse` — o contrato (BE-32) só tem `valid`/`user_id`, e não há necessidade de expor o motivo para o chamador.

## Critérios de aceite

- [x] **CA-01** — Um token gerado por `ITokenService.GenerateAccessToken` (BE-08) e passado a `ValidateToken` retorna `valid=true` com `user_id` igual ao `sub` do token.
- [ ] **CA-02** — Token vazio (`access_token=""`) retorna `valid=false, user_id=""`, status `OK`.
- [ ] **CA-03** — Token malformado (string arbitrária, não-JWT) retorna `valid=false, user_id=""`, status `OK`, sem exceção.
- [x] **CA-04** — Token expirado retorna `valid=false` — verificado com `FakeTimeProvider` avançado para **1 segundo após** o `exp` do token (`ClockSkew` zero, mesmo padrão de BE-08 CA-07).
- [x] **CA-05** — Token assinado com outra chave (ou com payload alterado, quebrando a assinatura) retorna `valid=false`.
- [x] **CA-06** — Token com `iss` ou `aud` diferentes do configurado retorna `valid=false`.
- [x] **CA-07** — `ValidateToken` nunca lança exceção nem devolve status gRPC de erro para nenhuma entrada malformada — só para falha real de infraestrutura.
- [x] **CA-08** — O token recebido não aparece em nenhuma entrada de log, inclusive `Debug`, em nenhum dos cenários acima.
- [x] **CA-09** — `ValidateToken` **não** consulta o store de usuários (`IUserLookup`/`IUserRepository`) — verificado em revisão/teste de que nenhuma chamada a esse tipo é feita durante a validação.
- [ ] **CA-10** — Um usuário desativado depois de logar continua com `valid=true` em `ValidateToken` até o token expirar (documentado como consequência aceita, não regressão) — verificado desativando o usuário entre a emissão e a chamada, dentro da janela de validade do token.

## Testes obrigatórios

- Unidade: validação com `FakeTimeProvider` — CA-01, CA-04, CA-05, CA-06.
- Unidade/Integração: entradas malformadas — CA-02, CA-03, CA-07.
- Integração: servidor gRPC real — CA-01, CA-08, CA-09, CA-10.

## Decisões em aberto

- **D-31** — A chave de assinatura não sai do Identity; `ValidateToken` é o único caminho de validação externa. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).

> **Emenda (21/09/2026).** Com [BE-40](BE-40-jwt-rs256-e-persisted-padrao.md)/**D-38**, `ValidateToken` passa a validar com a parte pública de uma chave **RS256** (era HS256, mesma chave de BE-08) — os CAs desta task continuam válidos como comportamento (CA-01 a CA-10 seguem verdadeiros, só a álgebra de assinatura por trás muda), mas o **consumidor principal muda**: o **Gateway deixa de chamar `ValidateToken`** — ele passa a validar o JWT localmente via `AddJwtBearer` com a chave pública. `ValidateToken` continua existindo e correto no Identity, mas sem chamador ativo na etapa do T2; a nota técnica "consumidor esperado: o middleware de autenticação do API Gateway" (acima) está **superada por BE-40/D-38**.

## Auditoria dos critérios (03/10/2026)

Critérios conferidos contra o código em 03/10/2026. Marcados: 7 de 10.

O RPC `ValidateToken` não existe mais: D-38 (BE-40) o substituiu por RS256 validado localmente no Gateway (`AddJwtBearer`, `src/Gateway/TodoList.Gateway.Api/Authentication/ServiceCollectionExtensions.cs`). Os critérios abaixo foram marcados por terem o OBJETIVO cumprido nesse desenho, não pelo RPC.

| CA | Situação | Evidência / motivo |
|---|---|---|
| CA-01 | atendido em outro lugar | Token válido do Identity é aceito pelo Gateway: `RealChainFixture`/`LogLeakageTests` (token real do login usado em rotas protegidas) e `JwtTestTokens` nos testes de tarefas. |
| CA-04 | atendido em outro lugar | `AuthenticationTests.CreateTask_TokenExpirado_Retorna401MesmoUmSegundoDepoisDoExp` (ClockSkew zero). |
| CA-05 | atendido em outro lugar | `AuthenticationTests.CreateTask_TokenAssinadoPorOutraChaveRsa_Retorna401` (+ HS256 e `alg=none`). |
| CA-06 | atendido em outro lugar | `AuthenticationTests.CreateTask_TokenComIssuerErrado_Retorna401` e `..._TokenComAudienceErrada_Retorna401`. |
| CA-07 | atendido em outro lugar | Toda causa de rejeição vira o mesmo 401 sem exceção: `AuthenticationTests.CreateTask_TodasAsCausasDe401_DevolvemOMesmoCorpoComTraceIdProprio`. |
| CA-08 | atendido em outro lugar | `LogLeakageTests.FluxoCompleto_NenhumSegredoApareceEmNenhumLogDosDoisServicos` (access e refresh tokens fora dos logs). |
| CA-09 | atendido em outro lugar | O Gateway não consulta o Identity para autenticar; `Gateway.UnitTests/ArchitectureTests` garante que o esquema/handler antigo sumiu. |
| CA-02 | em aberto | Não há teste de token vazio (`Bearer ` sem valor) no Gateway; só o caso sem header (`CreateTask_SemAuthorizationHeader_...`). |
| CA-03 | em aberto | Não há teste com string arbitrária não-JWT em `Authorization`; só HS256/`alg=none`/chave errada (JWTs bem formados). |
| CA-10 | em aberto | O comportamento existe por desenho (sem consulta ao store; D-41, `SessionEndpointsTests` linha ~211 mostra token pós-logout aceito), mas nenhum teste desativa o usuário entre emissão e chamada. |
