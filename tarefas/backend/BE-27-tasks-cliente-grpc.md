# BE-27 — Tasks Service: cliente gRPC do Identity

| | |
|---|---|
| **Domínio** | Tarefas / Integração |
| **Serviço** | Tasks (Microsserviço A) |
| **Depende de** | [BE-25](BE-25-contrato-grpc-identity.md), [BE-26](BE-26-identity-servidor-grpc.md) |
| **Bloqueia** | [BE-28](BE-28-validacao-dono-grpc.md), [BE-31](BE-31-verificacao-t1.md) |
| **Regras cobertas** | habilita RN-AUTZ-01, RN-USER-04 |
| **Estimativa** | M |

## Objetivo

O Tasks Service consegue perguntar ao Identity, por gRPC, se um usuário existe e está ativo — através de uma abstração de `Application` que não deixa nenhum tipo gerado pelo Protobuf entrar no domínio.

## Escopo

### Inclui

- Pacotes em `TodoList.Tasks.Infrastructure`: **`Grpc.Net.Client`**, **`Grpc.Net.ClientFactory`**, **`Google.Protobuf`**, **`Grpc.Tools`** (`PrivateAssets="all"`).
- Referência ao **mesmo** `.proto` de `contracts/identity/v1/`, com `GrpcServices="Client"`.
- **Abstração na `Application`** do Tasks:

  ```csharp
  public interface IIdentityGateway
  {
      Task<UserValidation> ValidateUserAsync(Guid userId, CancellationToken ct);
  }

  public sealed record UserValidation(bool Exists, bool Active, string DisplayName);
  ```

  `UserValidation` é um tipo **próprio da Application**, não o `ValidateUserResponse` gerado.
- **Implementação na `Infrastructure`**: `GrpcIdentityGateway : IIdentityGateway`, que recebe o cliente tipado gerado, faz a chamada e **traduz** a resposta para `UserValidation`.
- Registro do cliente tipado por `AddGrpcClient<IdentityService.IdentityServiceClient>`, com endereço vindo de configuração — `Identity:GrpcAddress` ([BE-30](BE-30-configuracao-enderecos-grpc.md)). **Nenhuma URL ou porta hardcoded**, em nenhum ponto do código.
- **Deadline** em toda chamada, vindo de configuração (`Identity:GrpcTimeoutSeconds`, padrão **2 s**): uma chamada sem prazo transforma o Identity lento em Tasks travado.
- Tradução das falhas de transporte em resultado de negócio: `RpcException` é capturada na `Infrastructure` e convertida no `Error` correspondente do catálogo do Tasks (`identity.unavailable`) — a `Application` **NÃO DEVE** capturar `RpcException`. Comportamento e status decididos em [BE-28](BE-28-validacao-dono-grpc.md) (**D-28**).
- Propagação do `traceId` da requisição HTTP para o Identity via metadata gRPC ([BE-24](BE-24-observabilidade-ci.md)).
- Log estruturado da chamada de saída: `userId`, `StatusCode` do gRPC, duração.

### Não inclui

- O uso da abstração no caso de uso de criação — [BE-28](BE-28-validacao-dono-grpc.md).
- Retentativa, circuit breaker ou cache de resposta. Nesta etapa a chamada é única e direta; resiliência é assunto de outra etapa.
- Qualquer chamada ao `ValidateToken` — ele é stub no servidor ([BE-26](BE-26-identity-servidor-grpc.md)) e o cliente não o invoca.

## Notas técnicas

- **Por que a abstração existe.** Sem ela, `ValidateUserResponse` (tipo gerado, com `Google.Protobuf` atrás) atravessaria a `Application` e provavelmente o `Domain`, prendendo a regra de negócio ao formato do transporte. Com ela, trocar gRPC por outra coisa é reescrever uma classe da `Infrastructure`. É a mesma regra que já vale para `DbContext` ([BE-02](BE-02-persistencia-base.md), CA-12).
- **`AddGrpcClient` e não `new GrpcChannel`.** O canal é caro de criar e feito para ser reutilizado; a fábrica cuida do ciclo de vida e do `HttpMessageHandler`, do mesmo jeito que `IHttpClientFactory`. Instanciar canal por chamada é o erro que aparece como esgotamento de porta sob carga.
- **O deadline é obrigatório, não uma otimização.** Sem ele o padrão é esperar indefinidamente: uma indisponibilidade do Identity vira acúmulo de requisições no Tasks e derruba os dois serviços em vez de um.
- **h2c em desenvolvimento:** com o Identity em `http://` sem TLS, a configuração correta é o endpoint Kestrel do Identity declarado como `Http2` ([BE-26](BE-26-identity-servidor-grpc.md)). O switch `System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport` fica como último recurso documentado, não como solução adotada.
- `RpcException` capturada apenas na borda: deixá-la subir até a `Application` traria o vocabulário do transporte para dentro do caso de uso.

## Critérios de aceite

### Contrato interno

- [ ] **CA-01** — `IIdentityGateway` e `UserValidation` estão em `TodoList.Tasks.Application`; `GrpcIdentityGateway` está em `TodoList.Tasks.Infrastructure`.
- [ ] **CA-02** — Nenhum tipo gerado a partir do `.proto` aparece em `TodoList.Tasks.Application` ou `TodoList.Tasks.Domain` — **teste de arquitetura**, não revisão.
- [ ] **CA-03** — Nenhuma referência a `Grpc.*` ou `Google.Protobuf` nos `.csproj` de `Domain` e `Application` do Tasks.

### Chamada

- [ ] **CA-04** — Com o Identity no ar, `ValidateUserAsync` de um usuário ativo devolve `Exists=true, Active=true` e o nome de exibição.
- [ ] **CA-05** — A resposta negativa do Identity (`exists=false`) chega como `UserValidation(false, false, "")`, sem exceção.
- [ ] **CA-06** — O endereço do Identity vem de `Identity:GrpcAddress`; uma varredura do código não encontra `localhost`, `http://` nem número de porta literal fora de `appsettings*.json` ([BE-30](BE-30-configuracao-enderecos-grpc.md)).
- [ ] **CA-07** — Alterar `Identity:GrpcAddress` para um endereço diferente faz a chamada ir para lá, **sem recompilar**.

### Falha e limites

- [ ] **CA-08** — Com o Identity desligado, a chamada falha em no máximo `Identity:GrpcTimeoutSeconds` e a `RpcException` **não** escapa da `Infrastructure`.
- [ ] **CA-09** — Com o Identity respondendo mais lento que o deadline, a chamada é cancelada no prazo — não fica pendurada.
- [ ] **CA-10** — O `CancellationToken` da requisição HTTP é repassado à chamada gRPC: cancelar a requisição do cliente cancela a chamada ao Identity.
- [ ] **CA-11** — O canal gRPC é reutilizado entre chamadas (nenhum `GrpcChannel.ForAddress` no corpo de um método de chamada).

### Observabilidade

- [ ] **CA-12** — Cada chamada produz log com `userId`, `StatusCode` do gRPC e duração.
- [ ] **CA-13** — O `traceId` da requisição HTTP chega ao log do Identity pela metadata gRPC — os dois lados são correlacionáveis ([BE-24](BE-24-observabilidade-ci.md), CA-07c).

## Testes obrigatórios

- Arquitetura: CA-02 e CA-03 — são o que garante que a Clean Architecture sobreviveu à introdução do gRPC.
- Unidade: `GrpcIdentityGateway` com o cliente gerado substituído — CA-04, CA-05, CA-08 (tradução de `RpcException`).
- Integração: cliente real contra o servidor real de [BE-26](BE-26-identity-servidor-grpc.md) subido em `WebApplicationFactory` — CA-04, CA-07, CA-09, CA-13.
- CA-08 e CA-09 são obrigatórios: são o comportamento do sistema no dia em que o Identity cair, e não têm como ser verificados manualmente na apresentação.

## Decisões em aberto

- **D-28** — Comportamento na indisponibilidade do Identity. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md) e [BE-28](BE-28-validacao-dono-grpc.md).
