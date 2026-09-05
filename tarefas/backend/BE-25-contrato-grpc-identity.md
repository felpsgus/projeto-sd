# BE-25 — Contrato gRPC compartilhado (`identity.proto`)

| | |
|---|---|
| **Domínio** | Contratos / Integração |
| **Serviço** | ambos (o `.proto` é a fronteira entre eles) |
| **Depende de** | [BE-01](BE-01-fundacao-solution.md) |
| **Bloqueia** | [BE-26](BE-26-identity-servidor-grpc.md), [BE-27](BE-27-tasks-cliente-grpc.md), [BE-28](BE-28-validacao-dono-grpc.md) |
| **Regras cobertas** | habilita RN-AUTZ-01, RN-USER-01, RN-USER-04 |
| **Estimativa** | P |

## Objetivo

Existe um contrato Protocol Buffers versionado no repositório, único e compartilhado, que descreve tudo o que o Tasks Service pode pedir ao Identity Service — e os dois lados geram código a partir **do mesmo arquivo**.

## Escopo

### Inclui

- O arquivo `contracts/identity/v1/identity.proto`, com o conteúdo canônico abaixo:

  ```proto
  syntax = "proto3";

  option csharp_namespace = "TodoList.Contracts.Identity.V1";

  package identity.v1;

  service IdentityService {
    // Valida o dono antes de criar a tarefa (RN-AUTZ-01, RN-USER-04).
    rpc ValidateUser (ValidateUserRequest) returns (ValidateUserResponse);

    // Único caminho para validar um token fora do Identity: a chave de
    // assinatura não sai daqui (D-31). Stub nesta etapa; usado pelo
    // API Gateway na etapa seguinte.
    rpc ValidateToken (ValidateTokenRequest) returns (ValidateTokenResponse);
  }

  message ValidateUserRequest {
    string user_id = 1;
  }

  message ValidateUserResponse {
    bool   exists       = 1;
    bool   active       = 2;
    string display_name = 3;
  }

  message ValidateTokenRequest {
    string access_token = 1;
  }

  message ValidateTokenResponse {
    bool   valid   = 1;
    string user_id = 2;
  }
  ```

- Comentário em **cada** RPC e **cada** campo explicando o significado de negócio e a RN de origem — o `.proto` é a documentação do contrato, equivalente ao OpenAPI da borda ([BE-24](BE-24-observabilidade-ci.md), CA-22b).
- Referência ao arquivo nos dois `.csproj` que precisam dele, cada um com o seu papel:
  - `TodoList.Identity.Api.csproj` → `<Protobuf Include="..\..\..\contracts\identity\v1\identity.proto" GrpcServices="Server" />`
  - `TodoList.Tasks.Infrastructure.csproj` → o **mesmo** caminho, com `GrpcServices="Client"`
- Pacote `Grpc.Tools` (`PrivateAssets="all"`) nos projetos que geram código.

### Não inclui

- Implementação do servidor ([BE-26](BE-26-identity-servidor-grpc.md)) ou do cliente ([BE-27](BE-27-tasks-cliente-grpc.md)).
- Qualquer RPC, mensagem ou campo além dos quatro tipos acima. **NÃO DEVEM** ser adicionados RPCs "por precaução".
- Um projeto de contratos publicado como pacote — o `.proto` é referenciado por caminho relativo (decisão **D-29**).

## Notas técnicas

- **`csharp_namespace` ajustado à solution:** `TodoList.Contracts.Identity.V1`, não `TodoApp.*`.
- **`ValidateToken` é stub nesta etapa, mas não é decorativo.** Ele é o mecanismo pelo qual **qualquer serviço que não seja o Identity** valida um token (**D-31**) — o Identity é o único que guarda a chave de assinatura, e com HS256 quem valida também consegue assinar. Sem este RPC, o único jeito de o Gateway validar um token seria receber a chave e virar um segundo emissor. A implementação real fica para a etapa do Gateway; aqui ele é declarado e responde `valid=false`.
- **`package identity.v1` e a pasta `v1/`:** o contrato entre serviços **é** versionado, ao contrário da API REST de borda (**D-22**). A razão é diferente: aqui os dois lados podem ser implantados em momentos distintos, e uma mudança incompatível precisa de um caminho de convivência.
- **Um arquivo, dois `GrpcServices`.** Duplicar o `.proto` (uma cópia por serviço) é o erro clássico: as duas cópias divergem em silêncio e o bug só aparece em runtime, como campo vazio. O caminho relativo é feio e é o preço de ter uma fonte única.
- `user_id` trafega como `string` (representação textual do `Guid`), não como `bytes`: legível no log e nas ferramentas de inspeção, ao custo de alguns bytes irrelevantes nesta escala.
- Campos numerados de forma estável: um número de campo já usado **NÃO DEVE** ser reaproveitado para outro significado.

## Critérios de aceite

- [ ] **CA-01** — `contracts/identity/v1/identity.proto` existe, está versionado e é o **único** `.proto` do repositório.
- [ ] **CA-02** — `dotnet build` gera os tipos C# nos dois lados a partir desse arquivo, sem cópia local em nenhum dos projetos.
- [ ] **CA-03** — Os tipos gerados ficam no namespace `TodoList.Contracts.Identity.V1`.
- [ ] **CA-04** — `TodoList.Identity.Api` gera **apenas** o lado servidor; `TodoList.Tasks.Infrastructure` gera **apenas** o lado cliente (verificável pelos tipos disponíveis em cada assembly).
- [ ] **CA-05** — O serviço declara exatamente dois RPCs: `ValidateUser` e `ValidateToken`. Nenhum a mais.
- [ ] **CA-06** — Cada RPC e cada campo tem comentário no `.proto`.
- [ ] **CA-07** — Alterar o `.proto` e recompilar propaga a mudança para os dois serviços em um único build — comprovado adicionando temporariamente um campo e vendo-o aparecer dos dois lados.
- [ ] **CA-08** — Nenhum projeto de `Domain` ou `Application` referencia o `.proto` nem os tipos gerados (teste de arquitetura). O código gerado é assunto da borda.

## Testes obrigatórios

- Arquitetura: CA-08 — é o critério que impede o contrato de rede vazar para dentro do domínio.
- Compilação: CA-02 a CA-05 são verificados pelo próprio build; documentar no PR como foram conferidos.

## Decisões em aberto

- **D-29** — `.proto` na pasta `contracts/` da raiz, referenciado por caminho relativo. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
