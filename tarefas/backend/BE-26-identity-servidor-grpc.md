# BE-26 — Identity Service: servidor gRPC (`ValidateUser`)

| | |
|---|---|
| **Domínio** | Identidade / Integração |
| **Serviço** | Identity (Microsserviço B) |
| **Depende de** | [BE-25](BE-25-contrato-grpc-identity.md); usa [BE-04](BE-04-dominio-usuario.md) quando disponível |
| **Bloqueia** | [BE-28](BE-28-validacao-dono-grpc.md), [BE-31](BE-31-verificacao-t1.md), [BE-34](BE-34-validate-token-real.md) |
| **Regras cobertas** | RN-USER-01, RN-USER-04, RN-AUTH-06, RN-AUTH-07 |
| **Estimativa** | M |

## Objetivo

O Identity Service atende chamadas gRPC em uma porta HTTP/2 dedicada e responde `ValidateUser` a partir do seu próprio store de usuários, dizendo se o usuário existe, se está ativo e qual o seu nome de exibição.

## Escopo

### Inclui

- Pacote **`Grpc.AspNetCore`** em `TodoList.Identity.Api`.
- Referência ao `.proto` compartilhado com `GrpcServices="Server"` ([BE-25](BE-25-contrato-grpc-identity.md)).
- Classe `IdentityGrpcService : IdentityService.IdentityServiceBase` em `TodoList.Identity.Api`, implementando:
  - **`ValidateUser(ValidateUserRequest)`**:
    1. tenta interpretar `user_id` como `Guid`; formato inválido → responde `exists=false, active=false, display_name=""` (**não** lança, **não** devolve erro gRPC — id malformado é "não existe", não falha de servidor);
    2. consulta o store de usuários pelo id;
    3. não encontrado → `exists=false, active=false, display_name=""`;
    4. encontrado → `exists=true`, `active` conforme o estado do usuário (RN-USER-01, RN-USER-04), `display_name` conforme RN-AUTH-07.
  - **`ValidateToken(ValidateTokenRequest)`** como **stub**: responde sempre `valid=false, user_id=""`. **Sem** validação de JWT real nesta etapa — mas com o comentário no código registrando que este é o **único** caminho de validação de token para quem está fora do Identity (**D-31**), e que a implementação real reutilizará o validador de [BE-08](BE-08-emissao-jwt.md) sem expor a chave.
- Abstração `IUserLookup` (ou o repositório de usuário já existente) na `Application` do Identity, com duas implementações possíveis:
  - **persistida**, sobre o `IdentityDbContext` ([BE-02](BE-02-persistencia-base.md)), quando [BE-04](BE-04-dominio-usuario.md)/[BE-07](BE-07-cadastro-usuario.md) já estiverem prontos — este é o caminho preferido;
  - **seed em memória**, com **1 a 2 usuários fixos** (um ativo, um inativo), selecionada por configuração enquanto a persistência de usuário não estiver ligada.
- Registro no `Program.cs`, em estilo **Minimal API**: `builder.Services.AddGrpc()` e `app.MapGrpcService<IdentityGrpcService>()`.
- Endpoint Kestrel dedicado ao gRPC servindo **HTTP/2** (`HttpProtocols.Http2`), separado do endpoint HTTP/1.1 que atende a API REST de autenticação — porta e protocolo vindos de configuração ([BE-30](BE-30-configuracao-enderecos-grpc.md)).
- Log estruturado da chamada recebida: `userId`, `exists`, `active`, duração ([BE-24](BE-24-observabilidade-ci.md)).

### Não inclui

- Validação real de JWT — o stub de `ValidateToken` é intencional e está especificado acima.
- Qualquer RPC além dos dois do contrato.
- Nova camada de dados: se não houver persistência de usuário ligada, usa-se o **seed em memória**; **NÃO DEVE** ser criado um esquema de banco só para esta task.
- Autenticação/autorização **na porta gRPC** — ela não é exposta publicamente nesta etapa.

## Notas técnicas

- **Por que `exists` e `active` são campos separados.** Colapsar em um `valid` único perderia a distinção entre "usuário nunca existiu" e "usuário existe mas está inativo" (RN-USER-04) — que são situações diferentes para quem chama e podem exigir mensagens diferentes no futuro.
- **Por que id inválido não é erro gRPC.** Um `INVALID_ARGUMENT` obrigaria o cliente a tratar dois caminhos de falha (resposta negativa e exceção) para o mesmo desfecho de negócio: a tarefa não pode ser criada. Erro gRPC fica reservado para o que é realmente falha de infraestrutura.
- **A resposta nunca revela mais do que o pedido.** `display_name` é o único dado de usuário que atravessa a fronteira, e existe só para o Tasks poder exibir/registrar o dono. E-mail, hash de senha e qualquer outro campo **NÃO DEVEM** aparecer na resposta.
- **`display_name` de usuário inexistente é string vazia**, nunca `null` — proto3 não distingue os dois, e assumir `null` é fonte garantida de `NullReferenceException` do lado cliente.
- O endpoint gRPC precisa de HTTP/2 **sem TLS** em desenvolvimento (h2c). Configurar o endpoint Kestrel como `Http2` é a forma correta; o switch `System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport` é remendo do lado cliente e **NÃO DEVE** ser a solução primária. Ver [BE-31](BE-31-verificacao-t1.md).
- A implementação em memória é **de demonstração**, não um mock de teste: ela roda no processo real e é selecionada por configuração explícita, com log de aviso na inicialização deixando claro que o store persistido não está em uso.

## Critérios de aceite

### Servidor

- [ ] **CA-01** — O Identity sobe e aceita conexões gRPC na porta configurada, em HTTP/2.
- [ ] **CA-02** — A porta REST e a porta gRPC são endpoints distintos e ambos funcionam simultaneamente.
- [ ] **CA-03** — Uma ferramenta de linha de comando (`grpcurl` ou cliente de teste) consegue invocar `ValidateUser` e receber resposta — comprovando que o serviço está exposto, não só compilando.
- [ ] **CA-04** — Nenhum Controller foi introduzido; o registro é `AddGrpc()` + `MapGrpcService<>()` no `Program.cs`.

### `ValidateUser`

- [ ] **CA-05** — Usuário existente e **ativo** → `exists=true`, `active=true`, `display_name` preenchido (RN-USER-01, RN-AUTH-07).
- [ ] **CA-06** — Usuário existente e **inativo** → `exists=true`, `active=false` (RN-USER-04).
- [ ] **CA-07** — Usuário inexistente → `exists=false`, `active=false`, `display_name=""` (string vazia, não `null`).
- [ ] **CA-08** — `user_id` malformado (`"abc"`, string vazia) → mesma resposta negativa do CA-07, **status gRPC `OK`**, sem exceção e sem 500.
- [ ] **CA-09** — A resposta não contém e-mail, hash de senha nem qualquer campo de usuário além dos três do contrato.
- [ ] **CA-10** — A chamada gera uma entrada de log estruturado com `userId`, `exists`, `active` e duração — **sem** dado sensível.

### `ValidateToken` (stub)

- [ ] **CA-11** — `ValidateToken` responde `valid=false`, `user_id=""` para qualquer entrada, inclusive um token válido de verdade. — **superado por [BE-34](BE-34-validate-token-real.md)** no T2.
- [ ] **CA-12** — Não existe nenhuma lógica de validação de JWT no caminho do stub (verificado em revisão) — o comportamento é declaradamente provisório e está anotado como tal no código. — **superado por [BE-34](BE-34-validate-token-real.md)** no T2.

### Store de usuários

- [ ] **CA-13** — Com a persistência ligada, `ValidateUser` reflete o estado real do banco: desativar um usuário muda a resposta de `active=true` para `active=false` sem reiniciar o serviço.
- [ ] **CA-14** — Com o seed em memória, existem exatamente dois usuários (um ativo, um inativo), com ids fixos e documentados no README.
- [ ] **CA-15** — Subir com o seed em memória emite log de **aviso** na inicialização deixando explícito que o store persistido não está em uso.

## Testes obrigatórios

- Unidade: `IdentityGrpcService.ValidateUser` com `IUserLookup` substituído — CA-05 a CA-09.
- Unidade: `ValidateToken` — CA-11.
- Integração: servidor gRPC real subido por `WebApplicationFactory` com um cliente gRPC de teste — CA-01, CA-03, CA-08, CA-13.
- CA-08 é obrigatório: id malformado é a entrada mais provável vinda de um cliente externo, e derrubar o servidor com ela seria falha de disponibilidade, não de validação.
