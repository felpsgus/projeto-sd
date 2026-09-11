# BE-35 — Tasks Service: servidor gRPC (`CreateTask`)

| | |
|---|---|
| **Domínio** | Tarefas / Borda |
| **Serviço** | Tasks |
| **Depende de** | [BE-32](BE-32-contratos-grpc-t2.md), [BE-28](BE-28-validacao-dono-grpc.md) |
| **Bloqueia** | [BE-36](BE-36-api-gateway.md), [BE-39](BE-39-verificacao-t2.md) |
| **Regras cobertas** | RN-TASK-10, RN-TASK-02 a RN-TASK-07, RN-TASK-15, RN-AUTZ-01, RN-AUTZ-04 |
| **Estimativa** | M |

## Objetivo

O Tasks Service passa a ser alcançável **só** por gRPC — `CreateTask` substitui `POST /api/tasks` — e o gatilho REST provisório de [BE-29](BE-29-gatilho-http-criar-tarefa.md) é removido (data de morte de **D-30**).

## Escopo

### Inclui

- Pacotes **`Grpc.AspNetCore`** e **`Grpc.AspNetCore.HealthChecks`** em `TodoList.Tasks.Api.csproj`.
- `<Protobuf Include="..\..\..\contracts\tasks\v1\tasks.proto" GrpcServices="Server" />` por caminho relativo, no mesmo padrão de **D-29**.
- `Api/Grpc/TasksGrpcService.cs` — `TasksGrpcService : TasksService.TasksServiceBase`, implementando `CreateTask`:
  1. converte `CreateTaskRequest` (proto) em `CreateTaskRequest` (Application, [BE-17](BE-17-criar-tarefa.md)) — mesmo nome em namespaces diferentes; a conversão explícita é o que impede o tipo gerado de vazar para dentro;
  2. valida com o `IValidator<CreateTaskRequest>` já existente ([BE-17](BE-17-criar-tarefa.md)) — falha de validação vira `RpcException` com `StatusCode.InvalidArgument` e os erros por campo nos trailers (ver `ResultGrpcStatus`, abaixo);
  3. chama `CreateTaskHandler.HandleAsync` — **sem alterar** a classe (CA-09);
  4. converte o `Result<TaskResponse>` em `TaskReply` (proto) via `ResultGrpcStatus`, ou levanta a `RpcException` correspondente ao erro.
- `Api/ResultMapping/ResultGrpcStatus.cs` — o espelho gRPC de `ResultHttpResults` ([BE-03](BE-03-result-erros-validacao.md)): mesma extensão `ToRpcStatus`/`ToGrpcResult` para `Result`/`Result<TValue>`, mesma tabela de `ErrorType` (agora para `StatusCode` gRPC, **D-35**), e o `error-code` do catálogo viajando no trailer `error-code` (não no corpo — o proto não tem campo para isso).
- Identidade, substituindo o mecanismo de **D-30**:
  - `Security/HeaderCurrentUser.cs` é renomeada para **`CallerIdentityCurrentUser`** e passa a ser a **única** implementação de `ICurrentUser` registrada — sem `if` de configuração no `Program.cs`. Lê o header `x-user-id`: como metadata gRPC é transportada como header HTTP/2, `IHttpContextAccessor` continua funcionando sem mudança de mecanismo, só de nome do header (**D-34**);
  - `Api/Grpc/RequireCallerIdentityInterceptor.cs` — interceptor de **servidor** (`Grpc.Core.Interceptors.Interceptor`) que substitui `RequireValidUserIdHeaderFilter`: se `x-user-id` estiver ausente ou não for `Guid` válido, responde `StatusCode.Unauthenticated` **antes** de `TasksGrpcService.CreateTask` ser invocado — nenhuma chamada ao Identity acontece nesse caminho;
  - `HttpContextClientDate` **não muda**: continua lendo `x-client-date` do `HttpContext`, case-insensitive, com o mesmo fallback de **D-18**.
- Kestrel: um único endpoint HTTP/2 dedicado a gRPC, `Grpc` em `http://0.0.0.0:5101` (`Protocols: "Http2"`, **D-37**). O endpoint `Http` existente em `5100` (`Protocols: "Http1"`) permanece **só** para `/health` (liveness/readiness HTTP, [BE-02](BE-02-persistencia-base.md)). `app.MapGrpcHealthChecksService()` expõe o gRPC Health Checking Protocol na porta `Grpc`, para o probe do Cloud Run (**D-37**, [BE-38](BE-38-containerizacao.md)).
- **Remoção** (data de morte de D-30):
  - `Endpoints/TaskEndpoints.cs`;
  - `Configuration/TasksCreationOptions.cs` e a chave `Tasks:AllowAnonymousCreate` de todo `appsettings*.json`;
  - `Security/NotYetAuthenticatedCurrentUser.cs`, `Security/RequireValidUserIdHeaderFilter.cs`, `Startup/StartupLog.cs`;
  - os comentários `TODO(dono: time Backend — BE-13; ...)` em `Program.cs` e em `HeaderCurrentUser.cs`/`ServiceCollectionExtensions.cs` que apontavam para esta task.
- Testes:
  - os testes de integração de `tests/TodoList.Tasks.IntegrationTests/Tasks/*` migram de `HttpClient` sobre `POST /api/tasks` para cliente gRPC sobre `WebApplicationFactory` — `GrpcChannel.ForAddress` com `HttpHandler = factory.Server.CreateHandler()`, mesmo padrão de `IdentityGrpcTestClient` (`tests/TodoList.Identity.IntegrationTests/IdentityGrpcTestClient.cs`);
  - `CreateTaskAnonymousModeTests` (ou equivalente do modo `AllowAnonymousCreate`) é **apagado**, não adaptado;
  - os testes unitários de `RequireValidUserIdHeaderFilter` e de `TaskEndpoints` são substituídos por testes unitários de `RequireCallerIdentityInterceptor` e de `ResultGrpcStatus`.

### Não inclui

- Mudança em `CreateTaskHandler`, no domínio ou no validador — a regra de negócio não muda, só o transporte (CA-09).
- `ValidateToken` real ou qualquer autenticação de token no Tasks — o Tasks continua confiando no chamador (**D-34**), quem valida token é o Identity ([BE-34](BE-34-validate-token-real.md)) a pedido do Gateway ([BE-36](BE-36-api-gateway.md)).
- Qualquer outro RPC de tarefa (editar, concluir, listar, remover) — fora do escopo do T2.
- Registro do Tasks como cliente gRPC de si mesmo ou qualquer chamada de teste fora de processo — os testes de integração seguem em `WebApplicationFactory`, como hoje.

## Notas técnicas

- **Por que o Tasks confia no chamador.** Igual a hoje (**D-30**), só que sem a flag: `x-user-id` não é reautenticado pelo Tasks porque ele **não é** a borda pública. Isso só é seguro com o Tasks fora do alcance do navegador — o Tasks **NÃO DEVE** ficar publicamente acessível (**D-32**, **D-34**).
- **Por que remover o REST agora, e não manter os dois.** Duas razões, não uma: (1) porta única HTTP/2 é o formato que o Cloud Run espera de um serviço interno (**D-37**) — manter HTTP/1.1 aberto sem propósito é superfície ociosa; (2) `Tasks:AllowAnonymousCreate` já tinha "data de morte" declarada em BE-29/D-30 no momento em que a identidade real chegasse na borda — o Gateway ([BE-36](BE-36-api-gateway.md)) é essa borda.
- **Regras de validação duplicadas entre Gateway e Tasks são aceitas.** O `CreateTaskHttpRequestValidator` do Gateway ([BE-36](BE-36-api-gateway.md)) espelha as mesmas regras do `IValidator<CreateTaskRequest>` do Tasks — defesa em profundidade, não fonte única. O Tasks continua sendo a **autoridade**: um Gateway mal configurado, ou um chamador gRPC direto (em ambiente de teste), ainda encontra a validação aqui.
- **Trace.** O `traceparent` recebido na metadata gRPC vira automaticamente o pai da `Activity` corrente no ASP.NET Core (propagação W3C já embutida no host) — o `traceId` que chega do Gateway continua o mesmo até a chamada ao Identity ([BE-27](BE-27-tasks-cliente-grpc.md), CA-13), sem código adicional nesta task.
- **Por que interceptor de servidor, e não `IEndpointRouteHandler`/filter como antes.** gRPC no ASP.NET Core não usa endpoint filters de Minimal API da mesma forma; a extensibilidade idiomática do lado servidor é `Interceptor` (`Grpc.Core.Interceptors`), registrado uma vez via `AddGrpc(options => options.Interceptors.Add<...>())` — equivalente funcional do filtro que ele substitui, mas no mecanismo correto do transporte.
- `error-code` no trailer, não no corpo: o `TaskReply` não tem (e não deve ganhar) um campo de erro — erro é sinalizado pelo `StatusCode` gRPC, com o código do catálogo como metadado (**D-35**), do mesmo jeito que hoje o HTTP usa o corpo `ProblemDetails.extensions.errorCode`.

## Critérios de aceite

### `CreateTask`

- [ ] **CA-01** — Chamada `CreateTask` válida com `x-user-id` de usuário ativo devolve `OK` e a tarefa é gravada com `OwnerId` igual ao valor de `x-user-id` (verificado no banco).
- [ ] **CA-02** — `TaskReply` espelha o `TaskResponse` de [BE-17](BE-17-criar-tarefa.md)/[BE-32](BE-32-contratos-grpc-t2.md) — sem campo de dono no proto (RN-AUTZ-01 sem exposição de `OwnerId` no contrato).
- [ ] **CA-03** — Request inválido (título vazio, descrição > 2000, `due_date` fora do formato) devolve `StatusCode.InvalidArgument` **antes de qualquer chamada ao Identity**, com trailer `error-code: validation.failed` e trailer `validation-errors` contendo um JSON `{ "campo": ["mensagem", ...] }` — o mesmo dicionário que o `ValidationProblem` REST devolvia, para o Gateway reconstruir o 400 sem perder o detalhe por campo (**D-35**).
- [ ] **CA-04** — Dono inexistente no Identity devolve `StatusCode.NotFound` (`task.owner_not_found`), sem gravação (herdado de [BE-28](BE-28-validacao-dono-grpc.md) CA-04).
- [ ] **CA-05** — Dono existente porém inativo devolve `StatusCode.FailedPrecondition` (`task.owner_inactive`), sem gravação (**D-35**; herdado de BE-28 CA-06).
- [ ] **CA-06** — Limite de 500 tarefas ativas excedido devolve `StatusCode.FailedPrecondition` (`task.active_limit_reached`) — código distinto de `task.owner_inactive` no trailer (herdado de BE-17 CA-14).
- [ ] **CA-07** — Com o Identity fora do ar, a chamada devolve `StatusCode.Unavailable` (`identity.unavailable`) dentro do deadline configurado, sem gravação (herdado de BE-28 CA-08/CA-09).

### Identidade

- [ ] **CA-08** — `x-user-id` ausente na metadata devolve `StatusCode.Unauthenticated`, e `TasksGrpcService.CreateTask` **nunca é invocado** (verificado: nenhuma chamada ao `IIdentityGateway` ocorre).
- [ ] **CA-09** — `x-user-id` presente mas não parseável como `Guid` devolve `StatusCode.Unauthenticated`, mesmo comportamento do CA-08.
- [ ] **CA-10** — `CallerIdentityCurrentUser` é a **única** implementação de `ICurrentUser` registrada no container do Tasks — não há `if`/factory condicional em `Program.cs` (verificado por revisão/teste de composição de DI).

### Remoção do REST

- [ ] **CA-11** — Nenhum endpoint REST de tarefa está mapeado no Tasks Service — teste que enumera as rotas HTTP do `WebApplicationFactory` e falha se qualquer rota além de `/health` (e `/health/ready`, se existir) aparecer.
- [ ] **CA-12** — A chave `Tasks:AllowAnonymousCreate` não existe em nenhum `appsettings*.json` nem é lida em nenhum ponto do código — varredura de texto no repositório.
- [ ] **CA-13** — `Configuration/TasksCreationOptions.cs`, `Security/NotYetAuthenticatedCurrentUser.cs`, `Security/RequireValidUserIdHeaderFilter.cs` e `Startup/StartupLog.cs` não existem mais no repositório.

### Infraestrutura e não regressão

- [ ] **CA-14** — O health check gRPC (`grpc.health.v1.Health/Check`) responde `SERVING` com o Tasks no ar e com o banco acessível.
- [ ] **CA-15** — `CreateTaskHandler` não sofreu nenhuma alteração de assinatura ou de comportamento — todos os critérios de [BE-17](BE-17-criar-tarefa.md) e [BE-28](BE-28-validacao-dono-grpc.md) continuam válidos, agora exercitados via gRPC.
- [ ] **CA-16** — O `traceId` da chamada `CreateTask` chega ao log de saída da chamada ao Identity ([BE-27](BE-27-tasks-cliente-grpc.md), CA-13) — mesmo `traceId` visto pelo Gateway, se um já tiver sido enviado na metadata.

## Testes obrigatórios

- Unidade: `RequireCallerIdentityInterceptor` — CA-08, CA-09.
- Unidade: `ResultGrpcStatus`/`ErrorTypeGrpcMapping` — cobertura de todos os `ErrorType` da tabela de **D-35**, incluindo `error-code` no trailer.
- Integração (`WebApplicationFactory` + `GrpcChannel`, padrão `IdentityGrpcTestClient`): CA-01 a CA-07, CA-14.
- Teste de composição de rotas: CA-11.
- Teste de arquitetura/varredura de texto: CA-12, CA-13.
- Não regressão: os testes unitários de `CreateTaskHandler` e do validador de [BE-17](BE-17-criar-tarefa.md)/[BE-28](BE-28-validacao-dono-grpc.md) continuam rodando sem alteração de arrange (CA-15).

## Decisões em aberto

- **D-30** — ✅ **fechada pelo T2**: o gatilho REST provisório é removido nesta task.
- **D-34** — Identidade do chamador via metadata `x-user-id`/`x-client-date`; o Tasks confia no Gateway (**D-32**).
- **D-35** — Mapeamento `ErrorType` → `StatusCode` gRPC e `error-code` no trailer.
- **D-37** — Endpoint único HTTP/2 por serviço + gRPC Health Checking Protocol, alinhado ao Cloud Run.
