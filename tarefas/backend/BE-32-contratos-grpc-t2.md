# BE-32 — Contratos gRPC do T2 (`Login` no Identity + `tasks.proto` novo)

| | |
|---|---|
| **Domínio** | Contratos / Integração |
| **Serviço** | ambos + Gateway (o `.proto` é a fronteira dos três) |
| **Depende de** | [BE-25](BE-25-contrato-grpc-identity.md), [BE-31](BE-31-verificacao-t1.md) |
| **Bloqueia** | [BE-33](BE-33-login-minimo-grpc.md), [BE-34](BE-34-validate-token-real.md), [BE-35](BE-35-tasks-servidor-grpc.md), [BE-36](BE-36-api-gateway.md) |
| **Regras cobertas** | habilita RN-AUTH-08, RN-AUTH-09, RN-TASK-01, RN-TASK-04, RN-TASK-10 |
| **Estimativa** | P |

## Objetivo

Os contratos Protocol Buffers do T2 existem e compilam: `identity.proto` ganha `Login` de forma compatível, e um `tasks.proto` novo declara `CreateTask` para o Gateway chamar o Tasks Service por gRPC.

## Escopo

### Inclui

- **`contracts/identity/v1/identity.proto`** recebe um novo RPC, sem alterar nada do que já existe:

  ```proto
  service IdentityService {
    rpc ValidateUser (ValidateUserRequest) returns (ValidateUserResponse);
    rpc ValidateToken (ValidateTokenRequest) returns (ValidateTokenResponse);

    // Login (RN-AUTH-08): troca e-mail + senha por um access token. Recorte
    // de BE-09 para o T2 (D-36) — só o access token, sem refresh/cookie.
    rpc Login (LoginRequest) returns (LoginResponse);
  }

  message LoginRequest {
    // E-mail informado pelo usuário, normalizado no Identity antes da busca.
    string email = 1;
    // Senha em texto puro — trafega só nesta chamada interna, nunca em log.
    string password = 2;
  }

  message LoginResponse {
    // Falso para e-mail inexistente, senha errada ou usuário inativo —
    // as três causas são indistinguíveis (RN-AUTH-09). Nunca status de
    // erro gRPC para credencial inválida, só para falha de infraestrutura.
    bool succeeded = 1;
    // Access token JWT, presente só quando succeeded=true.
    string access_token = 2;
    // Expiração do access token, presente só quando succeeded=true.
    google.protobuf.Timestamp expires_at = 3;
    // Id do usuário autenticado, presente só quando succeeded=true.
    string user_id = 4;
  }
  ```

  - Requer `import "google/protobuf/timestamp.proto";` no topo do arquivo.
  - **Números de campo existentes em `ValidateUserRequest/Response` e `ValidateTokenRequest/Response` NÃO DEVEM mudar.**
  - Os comentários de `ValidateToken` são atualizados: ele deixa de ser stub e passa a ter implementação real ([BE-34](BE-34-validate-token-real.md)) — o comentário "Stub nesta etapa" ([BE-25](BE-25-contrato-grpc-identity.md)) é removido e substituído por uma referência a BE-34.

- **`contracts/tasks/v1/tasks.proto`**, novo arquivo:

  ```proto
  syntax = "proto3";

  option csharp_namespace = "TodoList.Contracts.Tasks.V1";

  package tasks.v1;

  import "google/protobuf/timestamp.proto";

  // Fronteira de rede entre o API Gateway (cliente) e o Tasks Service
  // (servidor) — análoga a identity.proto, mas para o domínio de tarefas.
  service TasksService {
    // Cria uma tarefa (RN-TASK-10) em nome do usuário identificado pela
    // metadata gRPC "x-user-id" (D-34) — nunca por campo desta mensagem.
    rpc CreateTask (CreateTaskRequest) returns (TaskReply);
  }

  message CreateTaskRequest {
    // Título da tarefa (RN-TASK-02): 1–200 caracteres, não só espaços.
    string title = 1;
    // Descrição opcional (RN-TASK-03): até 2000 caracteres. `optional`
    // distingue "não informado" de "informado como vazio" — proto3 sem
    // `optional` não faz essa distinção (ver notas técnicas).
    optional string description = 2;
    // Prioridade (RN-TASK-04). TASK_PRIORITY_UNSPECIFIED equivale a
    // "não informado" e o servidor aplica o padrão Média.
    TaskPriority priority = 3;
    // Data de vencimento opcional, no formato yyyy-MM-dd (RN-TASK-05).
    // String, não google.type.Date (ver notas técnicas).
    optional string due_date = 4;
  }

  enum TaskPriority {
    TASK_PRIORITY_UNSPECIFIED = 0;
    TASK_PRIORITY_LOW = 1;
    TASK_PRIORITY_MEDIUM = 2;
    TASK_PRIORITY_HIGH = 3;
  }

  enum TaskStatus {
    TASK_STATUS_UNSPECIFIED = 0;
    TASK_STATUS_PENDING = 1;
    TASK_STATUS_COMPLETED = 2;
  }

  message TaskReply {
    // Id gerado pelo Tasks Service.
    string id = 1;
    // Título da tarefa, como persistido.
    string title = 2;
    // Descrição, se houver. optional para distinguir ausente de vazio.
    optional string description = 3;
    // Prioridade efetiva (nunca UNSPECIFIED numa resposta).
    TaskPriority priority = 4;
    // Estado atual (RN-TASK-06). Nasce PENDING (RN-TASK-07).
    TaskStatus status = 5;
    // Data de vencimento, se houver, no formato yyyy-MM-dd.
    optional string due_date = 6;
    // Verdadeiro se vencida e ainda pendente (RN-TASK-16), calculado pelo
    // Tasks Service a partir de x-client-date (D-18/D-34).
    bool is_overdue = 7;
    // Data/hora de conclusão (RN-TASK-08), presente só se status=COMPLETED.
    google.protobuf.Timestamp completed_at = 8;
    // Data/hora de criação.
    google.protobuf.Timestamp created_at = 9;
    // Data/hora da última alteração (RN-TASK-14).
    google.protobuf.Timestamp updated_at = 10;
  }
  ```

  - **Sem campo de dono** (`owner_id`) em nenhuma mensagem — mesma razão de [BE-17](BE-17-criar-tarefa.md) CA-22 e de D-34: o dono é identidade, chega pela metadata gRPC `x-user-id`, nunca pelo corpo da chamada.
  - `TaskReply` espelha `TaskResponse` (`TodoList.Tasks.Application.Tasks.TaskResponse`): `Id`, `Title`, `Description`, `Priority`, `Status`, `DueDate`, `CompletedAt`, `IsOverdue`, `CreatedAt`, `UpdatedAt` — todos os campos de `TaskResponse` estão representados.

- Consumidores de cada `.proto`, cada um com o `GrpcServices` correspondente, todos por caminho relativo (D-29), sem cópia local:

  | Projeto | `identity.proto` | `tasks.proto` |
  |---|---|---|
  | `TodoList.Identity.Api` | Server | — |
  | `TodoList.Tasks.Infrastructure` | Client | — |
  | `TodoList.Tasks.Api` | — | Server |
  | `TodoList.Gateway.Api` | Client | Client |

  `TodoList.Gateway.Api` ([D-33](DECISOES-PENDENTES.md)) é o único projeto que referencia os **dois** arquivos — e não referencia nenhum projeto do Identity nem do Tasks, só os `.proto`.

- Pacote `Grpc.Tools` (`PrivateAssets="all"`) e `Google.Protobuf` (para `Timestamp`) nos quatro projetos que geram código a partir de qualquer um dos dois arquivos.

### Não inclui

- Implementação de `Login` no servidor ([BE-33](BE-33-login-minimo-grpc.md)) nem de `ValidateToken` real ([BE-34](BE-34-validate-token-real.md)).
- Implementação do servidor `TasksService` ([BE-35](BE-35-tasks-servidor-grpc.md)) nem do cliente no Gateway ([BE-36](BE-36-api-gateway.md)).
- Qualquer RPC, mensagem ou campo além dos especificados. **NÃO DEVEM** ser adicionados RPCs "por precaução" (mesma regra de BE-25).
- Endpoints de edição, consulta, listagem ou remoção de tarefa em `tasks.proto` — o T2 cobre só a criação (RN-TASK-10), que é o único fluxo exposto pelo Gateway nesta entrega.

## Notas técnicas

- **Data como `string`, não `google.type.Date`.** `google.type.Date` exigiria uma dependência de proto extra (`google/type/date.proto`, do pacote `googleapis`) só para representar `yyyy-MM-dd`. `string` no formato fixo é suficiente nesta escala e evita o pacote a mais — mesmo racional de BE-05/BE-17, que já tratam vencimento como data sem hora.
- **`google.protobuf.Timestamp` para os demais instantes.** É um tipo bem conhecido ("well-known type"), já incluso em `Google.Protobuf` (dependência transitiva de `Grpc.Tools`/`Grpc.AspNetCore`), sem pacote adicional — ao contrário de `google.type.Date`.
- **`optional` em proto3** (`optional string description`, `optional string due_date`) distingue campo ausente de campo presente-e-vazio. Sem `optional`, `""` e "não informado" são indistinguíveis no wire format, e o Tasks deixaria de receber exatamente o que o cliente mandou — o `CreateTaskRequest` da Application tem `Description`/`DueDate` anuláveis, e a tradução precisa preservar `null` como `null`. Qualquer normalização (ex.: vazio → `null`) continua sendo decisão do Tasks, não um efeito colateral do transporte.
- **`Login` segue o mesmo estilo de `ValidateUser`**: falha de negócio é resposta com `succeeded=false` e status gRPC `OK`, nunca exceção nem `UNAUTHENTICATED`. É a mesma razão de BE-26 CA-08 aplicada à RN-AUTH-09 — misturar "credencial inválida" com erro de transporte obrigaria o cliente (Gateway) a tratar dois caminhos para o mesmo desfecho.
- **Sem campo de dono em `tasks.proto`**, pela mesma razão registrada em BE-17 CA-22 e D-34: colocar o dono no corpo o transformaria em dado de negócio manipulável pelo cliente; ele é identidade e viaja fora do payload.
- `TaskReply` replica a forma de `TaskResponse` propositalmente — o Gateway não deveria precisar adivinhar o formato de saída, só traduzir gRPC → JSON campo a campo.

## Critérios de aceite

- [ ] **CA-01** — `dotnet build` gera código C# a partir de `identity.proto` nos três consumidores esperados: `TodoList.Identity.Api` (server), `TodoList.Tasks.Infrastructure` (client), `TodoList.Gateway.Api` (client) — e nenhuma cópia local do arquivo existe fora de `contracts/`.
- [ ] **CA-02** — `dotnet build` gera código C# a partir de `tasks.proto` em `TodoList.Tasks.Api` (server) e `TodoList.Gateway.Api` (client), sem cópia local.
- [ ] **CA-03** — A mudança em `identity.proto` é compatível: um cliente que só conhecia `ValidateUser`/`ValidateToken` (código gerado antes do T2) continua compilando e funcionando sem alteração — comprovado por BE-27/BE-28 continuarem passando sem modificação.
- [ ] **CA-04** — Nenhum número de campo de `ValidateUserRequest`, `ValidateUserResponse`, `ValidateTokenRequest` ou `ValidateTokenResponse` foi alterado em relação a BE-25.
- [ ] **CA-05** — Nenhuma mensagem de `tasks.proto` tem campo de dono/`owner_id`/`user_id` do lado do request de criação.
- [ ] **CA-06** — Nenhum dado sensível (senha em texto puro fora de `LoginRequest.password`, hash de senha) aparece em `LoginResponse` ou em `TaskReply`.
- [ ] **CA-07** — Cada RPC e cada campo dos dois arquivos tem comentário explicando o significado de negócio e, quando aplicável, a RN de origem.
- [ ] **CA-08** — `TodoList.Gateway.Api` não referencia nenhum projeto `TodoList.Identity.*` nem `TodoList.Tasks.*` — só os dois `.proto` (D-33), verificado por teste de arquitetura ou inspeção do `.csproj`.
- [ ] **CA-09** — `TaskReply` tem um campo correspondente a cada propriedade pública de `TaskResponse` (`Id`, `Title`, `Description`, `Priority`, `Status`, `DueDate`, `CompletedAt`, `IsOverdue`, `CreatedAt`, `UpdatedAt`).

## Testes obrigatórios

- Compilação: CA-01, CA-02, CA-08 são verificados pelo próprio build; documentar no PR como foram conferidos (mesmo padrão de BE-25).
- Não regressão: a suíte de testes de BE-27/BE-28 (cliente gRPC do Tasks para `ValidateUser`) roda sem alteração — é o teste de CA-03.

## Decisões em aberto

- **D-29** — `.proto` em `contracts/` na raiz, referenciado por caminho relativo — o mesmo padrão se estende a `tasks.proto`. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
- **D-33** — Formato do projeto `TodoList.Gateway.Api` (sem Domain/Application). Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
- **D-34** — Identidade do chamador via metadata gRPC `x-user-id`/`x-client-date`, nunca no corpo. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
