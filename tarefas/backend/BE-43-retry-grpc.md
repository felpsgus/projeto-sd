# BE-43 — Retry gRPC nas leituras do Gateway

> **T3 (Cloud Run).** Na demo, o Tasks fica indisponível por instantes (cold start, redeploy, scale-to-zero) e o Gateway precisa **retentar sozinho** as leituras, em vez de devolver 503 ao primeiro tropeço. Parte 1 de BE-43: o lado do Gateway (CA-01 a CA-07). Parte 2: o lado do Tasks, cliente para o Identity (CA-08 e CA-09). Decisão em **D-44**.

| | |
|---|---|
| **Domínio** | Infraestrutura / Resiliência |
| **Serviço** | Gateway |
| **Depende de** | [BE-36](BE-36-api-gateway.md), [BE-41](BE-41-listar-e-consultar-tarefas-grpc.md) |
| **Regras cobertas** | nenhuma de negócio — reafirma D-28 (fail-closed: esgotadas as tentativas, `503` + `Retry-After`) |
| **Estimativa** | S |

## Objetivo

Os dois clientes gRPC do Gateway (Identity e Tasks) usam o retry nativo do `Grpc.Net.Client` (`ServiceConfig`/`RetryPolicy`, sem pacote novo), **apenas** para RPCs de leitura e **apenas** em `Unavailable`.

## Escopo

### Inclui

- `GrpcRetry.ForReadOnly(maxAttempts, params MethodName[])` monta um `ServiceConfig` com `MaxAttempts` configurável, `InitialBackoff = 200ms`, `MaxBackoff = 1s`, `BackoffMultiplier = 2`, `RetryableStatusCodes = { Unavailable }`.
- Aplicado por `.ConfigureChannel(...)` nos dois `AddGrpcClient` de `AddBackendGrpcClients`.
- `Backends:GrpcRetryMaxAttempts` (`BackendOptions.GrpcRetryMaxAttempts`, padrão 3, faixa 2 a 5 validada na inicialização; o grpc-dotnet rejeita `MaxAttempts` <= 1 ao criar o canal, então desligar o retry não é uma configuração suportada).
- Log de nível `Debug` na categoria que registra as retentativas, em `appsettings.json`.

### Não inclui

- Retry de escrita (`CreateTask`, `UpdateTask`, `CompleteTask`, `ReopenTask`, `DeleteTask`, `Register`, `Login`, `RefreshSession`, `Logout*`, `UpdateProfile`, `ChangePassword`, `DeleteAccount`).
- Circuit breaker, hedging, retry de outros códigos (`DeadlineExceeded`, `Internal`).

## Critérios de aceite

- [x] **CA-01** — `ListTasks_UnavailableUmaVez_RetentaERetorna200`: `Unavailable` na 1ª chamada, sucesso na 2ª; `200` e 2 chamadas ao Tasks.
- [x] **CA-02** — `ListTasks_SempreUnavailable_ParaEmTresTentativasERetorna503`: `503` com `Retry-After`; exatamente 3 chamadas.
- [x] **CA-03** — `CreateTask_Unavailable_NaoRetentaERetorna503`: `503`; exatamente 1 chamada.
- [x] **CA-04** — `GetProfile_UnavailableUmaVez_RetentaERetorna200` (`GET /api/me`): `200`; 2 chamadas ao Identity.
- [x] **CA-05** — `Register_Unavailable_NaoRetenta`: `503`; exatamente 1 chamada.
- [x] **CA-06** — As retentativas são logadas pela categoria **`Grpc.Net.Client.Internal.RetryCall`** (mensagem `Evaluated retry for failed gRPC call. Status code: 'Unavailable', Attempt: N, Retry: True|False`), em nível `Debug`; configurada em `Serilog:MinimumLevel:Override` do `appsettings.json` do Gateway (o Gateway usa Serilog, não `Logging:LogLevel`). **Confirmado** rodando os testes acima com `Serilog:MinimumLevel:Override:Grpc.Net.Client=Debug` e imprimindo o `SourceContext` de cada evento capturado. A categoria suposta de início (`Grpc.Net.Client.Internal.GrpcCall`, "Starting retry attempt") **não** é a que registra a decisão de retry.
- [x] **CA-07** — Lista fechada de RPCs com retry: Identity: `identity.v1.IdentityService/ValidateUser` e `/GetProfile`; Tasks: `tasks.v1.TasksService/ListTasks` e `/GetTask`. Nenhuma outra.
- [x] **CA-08** — `ValidateUserAsync_UnavailableUmaVez_RetentaERetornaExiste` (Tasks → Identity, `GrpcIdentityRetryTests`): `Unavailable` na 1ª chamada, `exists = true` na 2ª; o resultado indica que o usuário existe e o fake do Identity contou 2 chamadas. Só `identity.v1.IdentityService/ValidateUser` é retentada; `Identity:RetryMaxAttempts` (padrão 3, faixa 2 a 5 validada na inicialização), mesma categoria de log do CA-06 em `Serilog:MinimumLevel:Override` do Tasks.
- [x] **CA-09** — `ValidateUserAsync_SempreUnavailable_ParaEmTres`: fail-closed (D-28) inalterado, `IdentityUnavailableException` depois de exatamente 3 chamadas ao Identity.

## Notas técnicas

- O retry vale por **canal**; o `GatewayApiFactory` registra um segundo `AddGrpcClient` para o mesmo tipo (troca só endereço e handler primário), e o `ConfigureChannel` de produção continua valendo — confirmado pelos testes de CA-01/CA-02/CA-04.
- O `Retry-After` do `503` (D-28) é o de `GrpcErrorMapping`, inalterado: só aparece depois de esgotadas as tentativas.
- O Tasks tem a **sua cópia** de `GrpcRetry`, de propósito: são ~15 linhas e os serviços não compartilham projeto além do SharedKernel, que não depende de gRPC.

## Decisões

- **D-44** — retry só em leitura, só `Unavailable`, 3 tentativas, sem circuit breaker. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
