# BE-36 — API Gateway

| | |
|---|---|
| **Domínio** | Borda / Integração |
| **Serviço** | Gateway |
| **Depende de** | [BE-32](BE-32-contratos-grpc-t2.md), [BE-34](BE-34-validate-token-real.md), [BE-35](BE-35-tasks-servidor-grpc.md), [BE-33](BE-33-login-minimo-grpc.md) |
| **Bloqueia** | [BE-37](BE-37-deploy-t2-vm.md), [BE-38](BE-38-containerizacao.md), [BE-39](BE-39-verificacao-t2.md) |
| **Regras cobertas** | RN-AUTH-08, RN-AUTH-09, RN-TASK-10, RN-TASK-02 a RN-TASK-07, RN-AUTZ-04 |
| **Estimativa** | G |

## Objetivo

Existe um único ponto público — o API Gateway — que recebe REST/JSON do navegador, valida payload e autenticação na borda, e traduz cada chamada para gRPC contra Identity e Tasks (**D-32**).

## Escopo

### Inclui

Projeto **`src/Gateway/TodoList.Gateway.Api`** (Web SDK, Minimal API), adicionado à `TodoList.sln`. Referencia **só** `contracts/identity/v1/identity.proto` e `contracts/tasks/v1/tasks.proto` como `GrpcServices="Client"` — **nenhuma** referência de projeto a `TodoList.Identity.*` ou `TodoList.Tasks.*` (mesmo espírito de **D-26**, um nível acima). Pacotes: `Grpc.Net.ClientFactory`, `Grpc.Tools`, `Google.Protobuf`, `FluentValidation`, `Microsoft.AspNetCore.OpenApi` + `Scalar.AspNetCore`.

| Pasta | Conteúdo |
|---|---|
| `Endpoints/` | `IEndpointRouteHandler` + `MapEndpoints`, no mesmo padrão de `TodoList.Tasks.Api.Endpoints`; `HealthEndpoints` (`/health`, `AllowAnonymous`); `AuthEndpoints` — `POST /api/auth/login` (`AllowAnonymous`) → **200** `{accessToken, expiresAt}` ou **401** `auth.invalid_credentials` genérico (RN-AUTH-09, sem distinguir e-mail de senha errada); `TaskEndpoints` — `POST /api/tasks` (autenticado) → **201** + `Location: /api/tasks/{id}` |
| `Authentication/` | `IdentityTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>`, esquema `"IdentityToken"` |
| `Validation/` | `ValidationFilter<T>` (endpoint filter, mesmo padrão de `TodoList.Tasks.Api.Validation.ValidationFilter`); `CreateTaskHttpRequestValidator` (título 1–200 após trim, descrição ≤ 2000, prioridade dentre os valores válidos, `dueDate` em `yyyy-MM-dd`); `LoginHttpRequestValidator` (e-mail e senha não vazios) |
| `Backends/` | Registro dos `GrpcClient` tipados (Identity e Tasks); interceptor de **cliente**; `TaskTranslation` (DTO JSON ↔ mensagens proto, nos dois sentidos) |
| `ErrorHandling/` | `GlobalExceptionHandler` (`ProblemDetails`, nunca stack trace no corpo); `GrpcErrorMapping` (`RpcException` → HTTP, tabela abaixo) |
| `Contracts/` | DTOs HTTP (`CreateTaskHttpRequest`, `TaskHttpResponse`, `LoginHttpRequest`, `LoginHttpResponse`, etc.) — o tipo gerado do proto **nunca** é serializado direto na resposta |

**Autenticação** (`IdentityTokenAuthenticationHandler`):

- lê o `Bearer` de `Authorization`; token ausente ou mal formado → falha de autenticação (o `challenge` cuida da resposta);
- com token presente, chama `ValidateToken` por gRPC no Identity ([BE-34](BE-34-validate-token-real.md)) e, se válido, monta o `ClaimsPrincipal` com o claim `sub` (mesmo nome de claim que o Identity usa hoje, [BE-08](BE-08-emissao-jwt.md));
- a *fallback policy* de autorização exige usuário autenticado (opt-out, mesmo desenho de [BE-13](BE-13-protecao-endpoints.md)); `AllowAnonymous` só em `/health`, `POST /api/auth/login` e OpenAPI/Scalar;
- o `challenge` (`HandleChallengeAsync`) escreve **401** `application/problem+json` sem detalhar o motivo (token ausente vs. expirado vs. inválido) — mesma regra de [BE-13](BE-13-protecao-endpoints.md) CA-11;
- Identity inalcançável **durante a validação do token** → **503** com `Retry-After` (fail-closed, mesmo princípio de **D-28**) — **nunca** 401: um 401 nesse caso diria ao usuário "sua sessão é inválida" quando na verdade é o Identity que está fora, e o cliente tentaria logar de novo à toa.

**Clientes gRPC** (`Backends/`):

- `AddGrpcClient<IdentityService.IdentityServiceClient>` e `AddGrpcClient<TasksService.TasksServiceClient>`, endereços de `Backends:IdentityGrpcAddress` / `Backends:TasksGrpcAddress`, validados como URI absoluta no início (`ValidateOnStart`, mesmo padrão de `src/Tasks/TodoList.Tasks.Infrastructure/Identity/ServiceCollectionExtensions.cs`), aceitando `https://` (preparo para Cloud Run, **D-37**) além de `http://` (h2c local);
- deadline configurável por backend (`Backends:IdentityGrpcTimeoutSeconds`, `Backends:TasksGrpcTimeoutSeconds`);
- interceptor de **cliente** que, em toda chamada de saída, propaga o `traceparent` corrente (automático via `Activity`, sem código adicional além de garantir que a propagação W3C está habilitada), acrescenta `x-user-id` a partir do claim `sub` do `ClaimsPrincipal` autenticado (quando existir) e repassa `x-client-date` do header `X-Client-Date` do request de entrada (**D-18**, mesmo contrato de header que o Tasks já lê).

**Pipeline** (`Program.cs`):

- ordem: `GlobalExceptionHandler` → `UseAuthentication` → `UseAuthorization` → endpoints, com `ValidationFilter<T>` aplicado ao endpoint **antes** de qualquer chamada gRPC;
- requisição sem token → **401**, mesmo com payload de `POST /api/tasks` inválido — autenticação vence validação de payload na ordem de checagem;
- requisição com token válido e payload inválido → **400**, **sem** chamar o Tasks (nenhum RPC `CreateTask` disparado);
- JSON malformado ou enum desconhecido no corpo (ex.: `"priority": "Urgente"`) → **400** `ProblemDetails`, tratado pelo `GlobalExceptionHandler` ou pelo binder de Minimal API — nunca 500;
- enums trafegam como string (`JsonStringEnumConverter`, mesmo padrão de [BE-17](BE-17-criar-tarefa.md));
- Kestrel: um único endpoint `http://0.0.0.0:8080` (**D-37** — porta única vira `$PORT` no Cloud Run);
- nenhuma chave `Jwt:*` em `appsettings*.json` do Gateway (**D-31** — a chave de assinatura não sai do Identity).

**Mapeamento de erro gRPC → HTTP** (`GrpcErrorMapping`, **D-35**):

| `RpcException.StatusCode` | HTTP | Observação |
|---|---|---|
| `InvalidArgument` | 400 | o JSON do trailer `validation-errors` ([BE-35](BE-35-tasks-servidor-grpc.md) CA-03) vira o `errors` de um `ValidationProblemDetails` reconstruído |
| `NotFound` | 404 | — |
| `FailedPrecondition` | 409 | cobre `owner_inactive` e `active_limit_reached` |
| `Unauthenticated` | 401 | não deveria ocorrer no caminho normal (o Gateway já validou o token antes de chamar o Tasks) — tratado defensivamente |
| `Unavailable` / `DeadlineExceeded` | 503 | com `Retry-After` |
| qualquer outro (`Internal`, etc.) | 500 | `ProblemDetails` genérico, sem detalhe de transporte |

O `errorCode` do trailer `error-code` alimenta `ProblemDetails.extensions.errorCode`, no mesmo formato que o front já consome de Identity/Tasks hoje — o contrato de erro do frontend **não muda** com a introdução do Gateway.

### Não inclui

- Qualquer lógica de negócio de tarefas ou de usuário — o Gateway só traduz e delega; a decisão é sempre do backend chamado.
- Cache de resposta de `ValidateToken` — cada requisição autenticada faz uma chamada gRPC (mesma postura de não-cache de [BE-28](BE-28-validacao-dono-grpc.md) para `ValidateUser`).
- Refresh token, cookie `HttpOnly`, logout — fora do recorte de login do T2 (**D-36**); ficam para uma etapa futura de paridade com [BE-09](BE-09-login.md)/[BE-10](BE-10-refresh-token-rotacao.md)/[BE-11](BE-11-logout-revogacao.md).
- Servir o frontend Angular — esta task cobre só a API; hospedar o SPA pelo Gateway é decisão de outra task/etapa.
- Rate limiting, CORS ou proteção CSRF — não mudam com esta task (**D-21**/**D-32** continuam valendo: uma única origem pública).

## Notas técnicas

- **Por que `AuthenticationHandler`, e não middleware cru.** É o mecanismo idiomático do pipeline de autenticação do ASP.NET Core — integra com `[Authorize]`/`RequireAuthorization`, com a *fallback policy* e com `HandleChallengeAsync`/`HandleForbiddenAsync` sem reinventar a resposta 401/403; e é testável isoladamente (`TestServer` com o esquema registrado), ao contrário de um middleware que decide "autenticado ou não" por conta própria e não se integra ao restante do pipeline de autorização.
- **Por que autenticação antes de validação.** Autenticação responde "quem é você" e barra o acesso; validação responde "o que você mandou está bem formado". Fazer validação primeiro gastaria ciclo processando payload de um chamador que nem deveria estar ali, e um teste de payload malformado sem token teria resultado ambíguo (400 ou 401?) se a ordem não fosse fixa. A ordem também barra qualquer chamada gRPC de sair antes de a identidade estar resolvida.
- **Por que a validação na borda duplica a do Tasks.** Defesa em profundidade: o Gateway rejeita cedo o que já dá para rejeitar sem gastar uma chamada de rede, mas o Tasks continua sendo a autoridade — um chamador gRPC direto (testes, outro consumidor futuro) não pode confiar só no Gateway. As duas cópias das regras de `title`/`description`/`priority`/`dueDate` **DEVEM** ficar visivelmente próximas (mesmos limites que RN-TASK-02/03/04) para não divergir em silêncio.
- **Por que o Gateway não tem `Jwt:*`.** É a mesma razão de **D-31** aplicada de novo: se o Gateway ganhasse a chave de assinatura para validar localmente, ele se tornaria um segundo lugar capaz de forjar token com HS256. `ValidateToken` via gRPC é o único caminho, mesmo pagando o custo de uma chamada de rede por requisição autenticada.
- **Gateway como única porta pública (D-32).** Identity e Tasks continuam confiando no chamador (`x-user-id`, **D-34**) — isso só é seguro enquanto nada além do Gateway alcançar essas portas gRPC. Ao implantar ([BE-37](BE-37-deploy-t2-vm.md)/[BE-38](BE-38-containerizacao.md)), os dois serviços de backend **NÃO DEVEM** ficar publicamente acessíveis.
- **Por que o login do T2 é um recorte de BE-09 (D-36).** O RPC `Login` do Identity ([BE-32](BE-32-contratos-grpc-t2.md), [BE-33](BE-33-login-minimo-grpc.md)) devolve só o access token — sem refresh, sem cookie. O Gateway espelha esse recorte: a resposta de `POST /api/auth/login` também não inclui refresh nem seta cookie. Paridade completa com BE-09/BE-10 fica para depois do T2.

## Critérios de aceite

### Endpoint público e tradução (requisito 1 e 4 de `t2.md`)

- [ ] **CA-01** — `POST /api/tasks` com token válido e payload válido devolve **201** com `Location: /api/tasks/{id}` e o corpo do `TaskHttpResponse` — a chamada efetivamente atravessou gRPC até o Tasks (verificado com o Tasks real ou um fake instrumentado).
- [ ] **CA-02** — `POST /api/auth/login` com credenciais válidas devolve **200** com `{accessToken, expiresAt}`; nenhum outro campo sensível no corpo.
- [ ] **CA-03** — `POST /api/auth/login` com credenciais inválidas devolve **401** com `auth.invalid_credentials` — mesma resposta, byte a byte, para e-mail inexistente, senha errada e usuário inativo (RN-AUTH-09, RN-USER-04; o Gateway só vê `succeeded=false`, [BE-33](BE-33-login-minimo-grpc.md)).
- [ ] **CA-04** — O tipo gerado do `.proto` (`Contracts.Tasks.V1.*`, `Contracts.Identity.V1.*`) nunca aparece serializado na resposta HTTP — só os DTOs de `Contracts/` (verificado por inspeção do corpo de resposta serializado).

### Validação na borda (requisito 2 de `t2.md`)

- [ ] **CA-05** — `POST /api/tasks` com token válido e `title` ausente/vazio devolve **400** apontando o campo, e **nenhuma** chamada gRPC ao Tasks ocorre (verificado: `CreateTask` não invocado no fake).
- [ ] **CA-06** — `POST /api/tasks` com `priority: "Urgente"` (fora do enum) devolve **400**, não 500.
- [ ] **CA-07** — `POST /api/tasks` com JSON malformado (chave faltando aspas, vírgula sobrando) devolve **400** `ProblemDetails`, não 500.
- [ ] **CA-08** — `dueDate` fora do formato `yyyy-MM-dd` devolve **400**.

### Segurança (requisito 3 de `t2.md`)

- [ ] **CA-09** — `POST /api/tasks` sem `Authorization` devolve **401**, mesmo com corpo válido.
- [ ] **CA-10** — `POST /api/tasks` com token expirado ou assinatura inválida devolve **401**.
- [ ] **CA-11** — `POST /api/tasks` sem token **e** com payload inválido devolve **401** (não 400) — a ordem autenticação-antes-de-validação é observável.
- [ ] **CA-12** — O corpo do 401 não distingue "token ausente" de "token inválido"/"expirado" (mesma regra de [BE-13](BE-13-protecao-endpoints.md) CA-11).
- [ ] **CA-13** — Com o Identity inalcançável no momento de `ValidateToken`, a requisição autenticada devolve **503** com `Retry-After` — nunca 401.
- [ ] **CA-14** — `/health`, `POST /api/auth/login` e a documentação OpenAPI/Scalar permanecem acessíveis sem token; todo o resto exige token por padrão (teste de guarda de rotas com allowlist explícita, mesmo padrão de [BE-13](BE-13-protecao-endpoints.md) CA-07).
- [ ] **CA-15** — Nenhuma chave `Jwt:*` existe em nenhum `appsettings*.json` do Gateway nem é lida no código (varredura).

### Mapeamento de erro (D-35)

- [ ] **CA-16** — `RpcException` com `NotFound` do Tasks (dono inexistente) vira **404** no Gateway, com `errorCode` do trailer preservado no `ProblemDetails`.
- [ ] **CA-17** — `RpcException` com `FailedPrecondition` (dono inativo **ou** limite de tarefas ativas) vira **409**, com o `errorCode` distinguindo os dois casos.
- [ ] **CA-18** — `RpcException` com `Unavailable`/`DeadlineExceeded` de qualquer backend vira **503** com `Retry-After`.
- [ ] **CA-19** — Nenhuma resposta de erro do Gateway expõe stack trace, endereço interno de gRPC ou mensagem crua de `RpcException`.

### Arquitetura

- [ ] **CA-20** — O assembly de `TodoList.Gateway.Api` não referencia (direta ou transitivamente, via `.csproj`) `TodoList.Identity.*` nem `TodoList.Tasks.*` — teste de arquitetura.
- [ ] **CA-21** — `TodoList.Gateway.Api.csproj` referencia só os `.proto` de `contracts/identity/v1/` e `contracts/tasks/v1/`, ambos com `GrpcServices="Client"`.
- [ ] **CA-22** — Alterar `Backends:IdentityGrpcAddress`/`Backends:TasksGrpcAddress` para outro endereço redireciona a chamada sem recompilar.
- [ ] **CA-23** — Removendo o valor de `Backends:IdentityGrpcAddress` (ou colocando algo que não é URI absoluta), a aplicação falha na inicialização (`ValidateOnStart`), não na primeira requisição.

### Disponibilidade e rastreabilidade

- [ ] **CA-24** — `POST /api/auth/login` com o Identity inalcançável devolve **503** com `Retry-After` — nunca 401: "não consegui perguntar" não é "credencial inválida" (mesmo princípio do CA-13).
- [ ] **CA-25** — O `traceId` da requisição HTTP de entrada chega às duas chamadas gRPC de saída (`ValidateToken` e `CreateTask`) na metadata `traceparent` — verificado no fake dos clientes. É o que permite, na demonstração ([BE-39](BE-39-verificacao-t2.md)), seguir uma requisição pelos logs dos três serviços.
- [ ] **CA-26** — Cada chamada gRPC de saída do Gateway gera **uma** linha de log estruturado com backend, RPC, `StatusCode`, duração e `traceId` na própria mensagem — mesmo padrão de `GrpcIdentityGateway` no Tasks ([BE-27](BE-27-tasks-cliente-grpc.md)). **Nunca** o token, a senha ou o corpo da requisição.

## Testes obrigatórios

- Projetos `tests/TodoList.Gateway.UnitTests` e `tests/TodoList.Gateway.IntegrationTests`.
- Unidade: `CreateTaskHttpRequestValidator`, `LoginHttpRequestValidator` — CA-05, CA-06, CA-08.
- Unidade: `GrpcErrorMapping` — cobertura de todos os `StatusCode` da tabela — CA-16 a CA-19.
- Integração (`WebApplicationFactory`, clientes gRPC de Identity/Tasks trocados por fakes no DI): CA-01 a CA-03, CA-07, CA-09 a CA-14, CA-24 a CA-26.
- Teste de arquitetura: CA-20, CA-21.
- Teste de guarda de rotas com allowlist de anônimos: CA-14.
- CA-05 e CA-11 são obrigatórios: são o que prova que "validação na borda" e "autenticação antes de validação" não são só descrição em texto, e nenhum dos dois é demonstrável com segurança ao vivo sem o teste automatizado cobrindo o caminho de falha.

## Decisões em aberto

- **D-31** — ✅ decidida: nenhuma chave de assinatura fora do Identity; o Gateway valida token só via `ValidateToken`.
- **D-32** — ✅ decidida: o Gateway é a única origem pública.
- **D-34** — Identidade repassada a Identity/Tasks via metadata gRPC (`x-user-id`, `x-client-date`), nunca no corpo.
- **D-35** — Mapeamento `ErrorType`/`StatusCode` gRPC → HTTP e `errorCode` via trailer.
- **D-36** — Login do T2 é recorte de [BE-09](BE-09-login.md): só access token, sem refresh/cookie.
- **D-37** — Porta única HTTP (aqui, REST) por serviço, alinhada ao Cloud Run.
