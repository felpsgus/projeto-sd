# T1 — Ajustes nas Tarefas de Geração de Código (Comunicação gRPC)

> **Para:** Claude Code (execução na máquina do desenvolvedor)
> **Escopo:** Trabalho 1 da disciplina — dois microsserviços de backend conversando via gRPC.
> **Domínio:** Todo List (.NET 10 backend / Angular 22 frontend).
> **Recorte escolhido:** **Identity Service** (servidor gRPC) ↔ **Tasks Service** (cliente gRPC).

**Atenção ao enquadramento:** nenhum código foi gerado ainda. O que existe é o **conjunto de tarefas de geração de código** mais os documentos de governança. Este documento instrui você a **ajustar essas tarefas** para que, quando o código for gerado, ele produza dois microsserviços comunicando via gRPC — e **não** um backend único.

Este documento é **normativo**. Onde estiver **DEVE** / **NÃO DEVE**, trate como regra de time. Onde houver ambiguidade sobre como as tarefas estão organizadas, siga a **Etapa 0 (Descoberta)** antes de editar qualquer arquivo.

---

## 0. Descoberta (obrigatória antes de qualquer edição)

Antes de mutar arquivos, você **DEVE**:

1. Ler `CONVENCOES-CODIGO.md` e `REGRAS-DE-NEGOCIO.md` e tratá-los como fonte de verdade.
2. Localizar e ler os **documentos de tarefas de geração de código** já existentes. Mapear:
   - Como estão organizados (um arquivo ou vários? por camada? por RN?).
   - Qual **convenção de numeração/prefixo** usam para tarefas (ex.: `RN-XXX-NN`, `TASK-XX`, ou outro). Você **DEVE** seguir exatamente esse padrão ao inserir novas tarefas.
   - Se as tarefas atuais **assumem um backend único** (uma só API de todo list). Este é o cenário provável e é justamente o que precisa ser ajustado.
3. Produzir um **plano curto** (5–10 linhas): quais tarefas existentes serão editadas, quais serão inseridas, e onde. **Só então** edite.

**Premissa assumida** (corrija se a descoberta indicar o contrário): as tarefas atuais descrevem um backend único. Elas **DEVEM** ser ajustadas para gerar **dois serviços** na mesma solution.

---

## 1. Decisão estrutural que as tarefas DEVEM refletir

O conjunto de tarefas **DEVE** passar a produzir, no backend:

- **Identity Service — servidor gRPC (Microsserviço B).** Dono de usuários e tokens (modelo dual-token já descrito em `REGRAS-DE-NEGOCIO.md`).
- **Tasks Service — cliente gRPC (Microsserviço A).** Dono das tarefas (ciclo Pending/Completed, soft delete, limite de 500, isolamento por usuário). Antes de registrar uma tarefa, chama o Identity via gRPC para validar o dono.

**Fluxo-alvo (a demo do T1):**

```
POST /tasks (gatilho HTTP no Tasks, temporário)
        │
        ▼
Tasks Service ──gRPC ValidateUser(user_id)──▶ Identity Service
        ◀──── {exists, active, display_name} ───────
        │
        ▼
   grava a tarefa (se válido)  |  rejeita (se inválido)
```

O gatilho HTTP serve só para disparar a comunicação na apresentação; a **comunicação avaliada é o A→B em gRPC**. Esse endpoint vira o alvo do API Gateway no T2 — as tarefas **NÃO DEVEM** descartá-lo.

---

## 2. Como editar o conjunto de tarefas

Você **DEVE**:

1. **Reconciliar as tarefas existentes:** qualquer tarefa que assuma um único projeto backend, um único `Program.cs`, ou não preveja a separação Identity/Tasks **DEVE** ser ajustada para o modelo de dois serviços.
2. **Inserir as novas tarefas de T1** listadas na Etapa 3, **no mesmo formato, estilo e convenção de numeração** das tarefas existentes (descobertos na Etapa 0). Mantenha a mesma linguagem normativa dos documentos atuais.
3. **Preservar a rastreabilidade às RNs:** onde uma tarefa toca uma regra de negócio (isolamento por usuário, criação de tarefa, limite de 500), **DEVE** citar o **ID real** da RN em `REGRAS-DE-NEGOCIO.md`. Não invente números de RN.
4. **NÃO DEVE** gerar código nesta etapa. O produto é o conjunto de tarefas atualizado.

---

## 3. Tarefas de T1 a incorporar

Insira as tarefas abaixo (adaptando prefixo/numeração ao padrão existente). Cada item traz o **objetivo** e o **conteúdo técnico que a tarefa DEVE carregar**, para que a geração de código futura seja precisa.

**3.1 — Estrutura da solution (dois serviços + contrato compartilhado).**
Definir a solution com os projetos **Identity Service** e **Tasks Service**, e o local do `.proto` compartilhado (ex.: `contracts/identity/v1/identity.proto`). Ajustar tarefas de setup existentes que hoje pressupõem projeto único.

**3.2 — Definição do contrato `.proto`.**
Criar o contrato canônico (ver Etapa 4). RPC `ValidateUser` completo; `ValidateToken` como stub (foreshadow do T2). Nenhum RPC além destes no T1.

**3.3 — Identity Service (servidor gRPC).**
A tarefa **DEVE** especificar:
- Pacote `Grpc.AspNetCore`.
- Referência ao `.proto` no `.csproj` com `GrpcServices="Server"`.
- Implementação de `ValidateUser` sobre o store de usuário existente; se ainda não houver persistência de usuário ligada, usar **seed em memória** (1–2 usuários) — suficiente para a demo, sem inventar camada de dados nova.
- `ValidateToken` como stub mínimo (ex.: `valid=false`); **sem** validação de JWT real no T1.
- Registro no `Program.cs` em estilo **Minimal API**: `builder.Services.AddGrpc()` + `app.MapGrpcService<...>()`.
- Endpoint Kestrel servindo **HTTP/2** (`HttpProtocols.Http2`).

**3.4 — Tasks Service (cliente gRPC).**
A tarefa **DEVE** especificar:
- Pacotes `Grpc.Net.Client`, `Google.Protobuf`, `Grpc.Tools`.
- Referência ao **mesmo** `.proto` com `GrpcServices="Client"`.
- Cliente tipado via `AddGrpcClient<...>` com endereço vindo de configuração (`Identity:GrpcAddress`), **nunca** hardcoded.
- Adaptador na borda (Clean Architecture pragmática): interface no Application (ex.: `IIdentityGateway`), implementação gRPC na Infrastructure. Tipos gerados **NÃO DEVEM** vazar para o domínio.

**3.5 — Validação de dono na criação de tarefa.**
O caso de uso de criação **DEVE** chamar `ValidateUser(ownerId)` antes de persistir. Se `exists=false` ou `active=false`, rejeitar. Citar os IDs reais das RNs de ownership/isolamento e de criação. Manter as RNs já documentadas (limite de 500, isolamento, estados Pending/Completed).

**3.6 — Gatilho temporário `POST /tasks`.**
Endpoint Minimal API que dispara o caso de uso de criação. Sem autenticação no T1 (isso é T2), mas estruturado para receber o token depois.

**3.7 — Configuração por ambiente.**
Endereços/portas em `appsettings*.json`, sobrescrevíveis por variáveis de ambiente (ex.: `Identity__GrpcAddress`). Prepara o T3 (Cloud Run injeta env vars). Sem URL/porta hardcoded.

**3.8 — Verificação e critérios de aceite.**
A tarefa **DEVE** exigir uma seção **"Rodando o T1"** no README com:
- Caminho de sucesso: `POST /tasks` com usuário existente/ativo → Tasks chama `ValidateUser` via gRPC → grava → **201** com a tarefa.
- Caminho de falha: `POST /tasks` com usuário inexistente → `exists=false` → rejeição (por conta da resposta gRPC, não de validação local).
- Log do Tasks evidenciando a ida e volta ao Identity.
- **Nota (gotcha HTTP/2 local):** se o Identity rodar em `http://` sem TLS (h2c), documentar o ajuste (endpoint Kestrel como `Http2`, ou o switch `System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport`). Preferir configurar o endpoint como Http2.

---

## 4. Contrato `.proto` canônico

```proto
syntax = "proto3";

// Ajuste o namespace à convenção de nomes da solution.
option csharp_namespace = "TodoApp.Contracts.Identity.V1";

package identity.v1;

service IdentityService {
  // Requerido para o T1: valida o dono antes de criar a tarefa.
  rpc ValidateUser (ValidateUserRequest) returns (ValidateUserResponse);

  // Preparo para o T2 (middleware 401). Stub no T1.
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

---

## 5. Convenções a preservar nas tarefas

- **Minimal APIs** apenas. As tarefas **NÃO DEVEM** introduzir Controllers.
- Clean Architecture pragmática: gRPC gerado fica na borda (Infrastructure); domínio limpo.
- `npm` intacto; **NÃO DEVE** alterar o gerenciador de pacotes.
- **Frontend Angular NÃO DEVE ser tocado** no T1 (nem no código, nem nas tarefas dele).
- Linguagem, nomes e numeração seguem `CONVENCOES-CODIGO.md` e o padrão das tarefas existentes.

---

## 6. Fora de escopo (NÃO incluir nas tarefas de T1)

- **NÃO** o API Gateway REST nem o middleware `401` — isso é **T2**.
- **NÃO** Dockerfiles, Artifact Registry ou Cloud Run — isso é **T3**.
- **NÃO** tocar no frontend.
- **NÃO** RPCs, serviços ou tabelas além do fluxo acima.
- **NÃO** validação de JWT real (só o stub de `ValidateToken`).

---

## 7. Entregável desta etapa

Ao concluir, você **DEVE** entregar:

1. O conjunto de tarefas **atualizado**: tarefas existentes reconciliadas para o modelo de dois serviços + as tarefas de T1 (3.1 a 3.8) inseridas no padrão da casa.
2. Um **changelog curto** ao final: quais tarefas foram editadas, quais foram criadas, e os IDs de RN referenciados.
3. Confirmação de que **nenhum código foi gerado** — apenas as tarefas foram ajustadas.
