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

- [x] **CA-01** — `contracts/identity/v1/identity.proto` existe, está versionado e é o **único** `.proto` do repositório.
- [x] **CA-02** — `dotnet build` gera os tipos C# nos dois lados a partir desse arquivo, sem cópia local em nenhum dos projetos.
- [x] **CA-03** — Os tipos gerados ficam no namespace `TodoList.Contracts.Identity.V1`.
- [x] **CA-04** — `TodoList.Identity.Api` gera **apenas** o lado servidor; `TodoList.Tasks.Infrastructure` gera **apenas** o lado cliente (verificável pelos tipos disponíveis em cada assembly).
- [x] ~~**CA-05** — O serviço declara exatamente dois RPCs: `ValidateUser` e `ValidateToken`. Nenhum a mais.~~ **Substituído (04/10/2026)** pelas D-36 e D-38: o contrato ganhou login, sessão e conta (dez RPCs) e `ValidateToken` foi removido, porque o Gateway valida o JWT localmente.
- [x] **CA-06** — Cada RPC e cada campo tem comentário no `.proto`. *(Atendido em 03/10/2026, issue #4; garantido por `ArchitectureTests.ContratosProto_TodoRpcECampoTemComentario`.)*
- [x] **CA-07** — Alterar o `.proto` e recompilar propaga a mudança para os dois serviços em um único build — comprovado adicionando temporariamente um campo e vendo-o aparecer dos dois lados. *(provado em 06/10/2026: um campo temporário `prova_ca07 = 99` em `RefreshSessionResponse` e um único `dotnet build` geraram a propriedade `ProvaCa07` nos três projetos que compilam o contrato hoje, Identity.Api (servidor), Gateway.Api e Tasks.Infrastructure (clientes); campo removido e build refeito sem sobra.)*
- [x] **CA-08** — Nenhum projeto de `Domain` ou `Application` referencia o `.proto` nem os tipos gerados (teste de arquitetura). O código gerado é assunto da borda.

## Testes obrigatórios

- Arquitetura: CA-08 — é o critério que impede o contrato de rede vazar para dentro do domínio.
- Compilação: CA-02 a CA-05 são verificados pelo próprio build; documentar no PR como foram conferidos.

## Decisões em aberto

- **D-29** — `.proto` na pasta `contracts/` da raiz, referenciado por caminho relativo. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).

## Auditoria dos critérios (03/10/2026)

Critérios conferidos contra o código em 03/10/2026. Marcados: 5 de 8.

| CA | Situação | Evidência / motivo |
|---|---|---|
| CA-01 | atendido (texto desatualizado) | `contracts/identity/v1/identity.proto` existe e é a fonte única do contrato do Identity, sem cópias (cinco `.csproj` o referenciam por caminho relativo). A frase "único `.proto` do repositório" está superada por BE-32/D-34: existe também `contracts/tasks/v1/tasks.proto`. |
| CA-04 | atendido (com ressalva) | `TodoList.Identity.Api` usa `GrpcServices="Server"`; `TodoList.Tasks.Infrastructure` usa `Client`. Hoje o Gateway (`Client`) e o projeto de fakes de teste (`Server`) também consomem o arquivo. Sem teste automatizado — evidência é a leitura dos `.csproj`. |
| CA-05 | em aberto — superado por D-36/D-38 | O contrato não declara mais "exatamente dois RPCs": hoje tem dez (`ValidateUser`, `Login`, `RefreshSession`, `Logout`, `LogoutAll`, `Register`, `GetProfile`, `UpdateProfile`, `ChangePassword`, `DeleteAccount`) e `ValidateToken` **foi removido** (o Gateway valida o JWT localmente com chave pública, D-38). |
| CA-06 | em aberto — parcial | Todos os RPCs e quase todos os campos têm comentário, mas faltam em campos de `RegisterResponse.display_name`, `ProfileResponse` (id, email, display_name, created_at), `UpdateProfileRequest.user_id`, `ChangePasswordRequest` (user_id, new_password) e `DeleteAccountRequest.user_id`; várias mensagens e valores de enum não têm comentário próprio. |
| CA-07 | em aberto — não verificável por código | Exige alterar temporariamente o `.proto` e observar o campo nos dois lados (prova manual). A estrutura a sustenta (mesmo arquivo em todos os `.csproj`), mas não há teste. |
| CA-08 | atendido em outro lugar | `ArchitectureTests.DomainEApplication_NaoReferenciamOProtoNemOsTiposGerados` em Identity e Tasks (`TodoList.Identity.UnitTests`, `TodoList.Tasks.UnitTests`). |

## Emenda (03/10/2026) — usuário inativo removido

O contrato perde o campo `active` de `ValidateUserResponse` e a menção a RN-USER-04 no comentário do RPC (issue #16). A resposta de `ValidateUser` diz apenas se o usuário existe e qual o nome de exibição.
