# Backend — Índice de tarefas

Stack alvo: **.NET 10 / C# 14 / ASP.NET Core Minimal APIs / EF Core 10 / PostgreSQL**, Clean Architecture em 4 projetos por serviço (`Domain → Application → Infrastructure → Api`), conforme [CONVENCOES-CODIGO.md](../../CONVENCOES-CODIGO.md).

## Dois serviços, não um

O backend é composto por **dois microsserviços independentes** que se comunicam por **gRPC** — decisão estrutural implementada em [BE-01](BE-01-fundacao-solution.md) e detalhada em [BE-25](BE-25-contrato-grpc-identity.md) a [BE-31](BE-31-verificacao-t1.md).

| Serviço | Papel | Dono de | Tasks |
|---|---|---|---|
| **Identity Service** | **servidor gRPC** | usuários, credenciais, tokens | BE-04, BE-06 a BE-16, [BE-26](BE-26-identity-servidor-grpc.md) |
| **Tasks Service** | **cliente gRPC** | tarefas | BE-05, BE-17 a BE-23, [BE-27](BE-27-tasks-cliente-grpc.md) a [BE-30](BE-30-configuracao-enderecos-grpc.md) |

Cada task traz no cabeçalho a linha **Serviço**, indicando onde ela vive.

O **Identity é a única autoridade sobre tokens** (**D-31**): a chave de assinatura não sai dele, e quem precisa validar um token chama `ValidateToken` por gRPC. O Tasks não valida JWT — ele recebe a identidade já verificada de quem o chama, o que só é seguro porque o **API Gateway é o único ponto público** e os dois serviços ficam atrás dele (**D-32**).

```
POST /api/tasks
        │
        ▼
Tasks Service ──gRPC ValidateUser(user_id)──▶ Identity Service
        ◀──── {exists, active, display_name} ───────
        │
        ▼
   grava a tarefa (se válido)  |  rejeita (se inválido)
```

Os dois serviços **não têm nenhuma referência de projeto entre si** (**D-26**): em código, a única fronteira entre eles é o contrato `contracts/identity/v1/identity.proto`. É isso que faz da validação do dono uma chamada de rede real, e não uma chamada de método.

Eles **compartilham um banco** `todolist`, com um **schema por serviço** — `identity` e `tasks` (**D-27**). Cada `DbContext` enxerga apenas o próprio schema, então o Tasks continua sem conseguir ler um usuário sem perguntar por gRPC. Uma FK cruzada (`tasks.owner_id → identity.users(id)`, `ON DELETE CASCADE`) garante a existência do dono e faz a exclusão de conta apagar as tarefas em cascata ([BE-16](BE-16-exclusao-conta.md)).

## Ordem de execução

As tarefas estão numeradas na ordem em que devem ser feitas. Tarefas na mesma "onda" não dependem entre si e podem ser paralelizadas.

### Onda 0 — Fundação (bloqueia tudo)

| # | Tarefa | Est. |
|---|---|---|
| [BE-01](BE-01-fundacao-solution.md) | Fundação da solution, tooling e qualidade | M |
| [BE-02](BE-02-persistencia-base.md) | Persistência base: EF Core, PostgreSQL, migrations, Testcontainers | M |
| [BE-03](BE-03-result-erros-validacao.md) | `Result<T>`, tratamento de erros HTTP e validação | M |

### Onda 1 — Domínio

| # | Tarefa | Est. |
|---|---|---|
| [BE-04](BE-04-dominio-usuario.md) | Domínio: entidade `User` | M |
| [BE-05](BE-05-dominio-tarefa.md) | Domínio: entidade `TodoTask` (estados e ciclo de vida) | G |
| [BE-06](BE-06-hash-senha.md) | Hashing de senha e política de senha | P |

### Onda 2 — Autenticação

| # | Tarefa | Est. |
|---|---|---|
| [BE-07](BE-07-cadastro-usuario.md) | Cadastro de usuário | M |
| [BE-08](BE-08-emissao-jwt.md) | Emissão e validação de access token (JWT) | M |
| [BE-09](BE-09-login.md) | Login | M |
| [BE-10](BE-10-refresh-token-rotacao.md) | Refresh token com rotação e detecção de reuso | G |
| [BE-11](BE-11-logout-revogacao.md) | Logout e revogação de sessões | P |
| [BE-12](BE-12-bloqueio-tentativas-login.md) | Bloqueio temporário por tentativas de login | M |
| [BE-13](BE-13-protecao-endpoints.md) | Proteção de endpoints e identidade do usuário corrente | P |

### Onda 3 — Conta do usuário

| # | Tarefa | Est. |
|---|---|---|
| [BE-14](BE-14-perfil-usuario.md) | Perfil: consultar e editar nome de exibição | P |
| [BE-15](BE-15-alteracao-senha.md) | Alteração de senha | M |
| [BE-16](BE-16-exclusao-conta.md) | Exclusão da própria conta | M |

### Onda 4 — Tarefas (CRUD)

| # | Tarefa | Est. |
|---|---|---|
| [BE-17](BE-17-criar-tarefa.md) | Criar tarefa (com limite de tarefas ativas) | M |
| [BE-18](BE-18-consultar-tarefa-autorizacao.md) | Consultar tarefa por id + autorização por propriedade | M |
| [BE-19](BE-19-editar-tarefa.md) | Editar tarefa | M |
| [BE-20](BE-20-concluir-reabrir-tarefa.md) | Concluir e reabrir tarefa | M |
| [BE-21](BE-21-remover-tarefa.md) | Remover tarefa (soft delete) | M |

### Onda 5 — Listagem e operação

| # | Tarefa | Est. |
|---|---|---|
| [BE-22](BE-22-listagem-tarefas.md) | Listagem: filtros, busca, ordenação e paginação | G |
| [BE-23](BE-23-expurgo-tarefas-removidas.md) | Expurgo de tarefas removidas (retenção) | P |
| [BE-24](BE-24-observabilidade-ci.md) | Observabilidade, CI, gate de cobertura e segurança | M |

### Onda 6 — Comunicação gRPC entre os serviços

Esta onda é o que transforma dois projetos na mesma solution em dois microsserviços de fato. [BE-25](BE-25-contrato-grpc-identity.md) e [BE-26](BE-26-identity-servidor-grpc.md) dependem apenas de [BE-01](BE-01-fundacao-solution.md) e **podem ser antecipadas**: o Identity serve `ValidateUser` a partir de um seed em memória enquanto a persistência de usuário não estiver pronta.

| # | Tarefa | Serviço | Est. |
|---|---|---|---|
| [BE-25](BE-25-contrato-grpc-identity.md) | Contrato gRPC compartilhado (`identity.proto`) | ambos | P |
| [BE-26](BE-26-identity-servidor-grpc.md) | Identity Service: servidor gRPC (`ValidateUser`) | Identity | M |
| [BE-27](BE-27-tasks-cliente-grpc.md) | Tasks Service: cliente gRPC do Identity | Tasks | M |
| [BE-28](BE-28-validacao-dono-grpc.md) | Validação do dono na criação de tarefa (via gRPC) | Tasks | M |
| [BE-29](BE-29-gatilho-http-criar-tarefa.md) | Gatilho HTTP temporário para `POST /api/tasks` | Tasks | P |
| [BE-30](BE-30-configuracao-enderecos-grpc.md) | Configuração por ambiente dos endereços e portas | ambos | P |
| [BE-31](BE-31-verificacao-t1.md) | Verificação da comunicação gRPC: roteiro e critérios | ambos | P |

> Estimativa: **P** ≈ até 1 dia · **M** ≈ 1–2 dias · **G** ≈ 3+ dias.

## Grafo de dependências

```
BE-01 ──┬── BE-02 ──┬── BE-04 ──┬── BE-06 ── BE-07 ── BE-09 ── BE-10 ── BE-11
        │           │           │                       │        │
        ├── BE-03 ──┘           │                    BE-08 ──── BE-12
        │                       │                       │
        │                       └── BE-05            BE-13 ──┬── BE-14 ── BE-15 ── BE-16
        │                            │                       │
        │                            └───────────────────────┴── BE-17 ── BE-18 ──┬── BE-19
        │                                                        │                ├── BE-20
        │                                                        │                ├── BE-21 ── BE-23
        │                                                        │                └── BE-22
        │                                                        │
        └── BE-25 ──┬── BE-26 (usa BE-04 quando pronto) ──┐      │
                    │                                     │      │
                    └── BE-27 ◀───────────────────────────┘      │
                             │                                   │
                             └── BE-28 ◀──────────────────────────
                                   │
                                   └── BE-29 ── BE-31
                                          BE-30 ─┘
BE-24 acompanha desde BE-01 e fecha ao final.
```

> **BE-27 e BE-28 também dependem de BE-03.** O gateway gRPC e a validação do
> dono devolvem `Result<T>` e consomem códigos do catálogo de erros (`identity.unavailable`,
> `owner.not_found`, `owner.inactive`); sem BE-03 essas tasks improvisam o próprio
> vocabulário de falha e depois precisam ser reescritas. A aresta não cabe no
> desenho acima sem embaralhá-lo, mas vale a mesma regra: **BE-03 antes de BE-27**.

## Rastreabilidade — Regra de negócio → Tarefa

| RN | Descrição resumida | Tarefa |
|---|---|---|
| RN-AUTH-01 | Auto-cadastro com e-mail e senha | [BE-07](BE-07-cadastro-usuario.md) |
| RN-AUTH-02 | E-mail único | [BE-04](BE-04-dominio-usuario.md), [BE-07](BE-07-cadastro-usuario.md) |
| RN-AUTH-03 | Formato de e-mail válido | [BE-04](BE-04-dominio-usuario.md), [BE-07](BE-07-cadastro-usuario.md) |
| RN-AUTH-04 | Senha ≥ 8, com letra e número | [BE-06](BE-06-hash-senha.md) |
| RN-AUTH-05 | Senha em hash, nunca retornada | [BE-06](BE-06-hash-senha.md) |
| RN-AUTH-06 | Usuário nasce ativo | [BE-04](BE-04-dominio-usuario.md), [BE-07](BE-07-cadastro-usuario.md), [BE-26](BE-26-identity-servidor-grpc.md) |
| RN-AUTH-07 | Nome de exibição padrão = parte antes do `@` | [BE-04](BE-04-dominio-usuario.md), [BE-07](BE-07-cadastro-usuario.md), [BE-26](BE-26-identity-servidor-grpc.md) |
| RN-AUTH-08 | Login com e-mail + senha | [BE-09](BE-09-login.md) |
| RN-AUTH-09 | Mensagem genérica de credencial inválida | [BE-09](BE-09-login.md) |
| RN-AUTH-10 | Emitir access + refresh token | [BE-08](BE-08-emissao-jwt.md), [BE-10](BE-10-refresh-token-rotacao.md) |
| RN-AUTH-11 | Access token expira em 15 min | [BE-08](BE-08-emissao-jwt.md) |
| RN-AUTH-12 | Logout invalida refresh token | [BE-11](BE-11-logout-revogacao.md) |
| RN-AUTH-13 | 5 tentativas → bloqueio de 15 min | [BE-12](BE-12-bloqueio-tentativas-login.md) |
| RN-AUTH-14 | Renovar sem credenciais | [BE-10](BE-10-refresh-token-rotacao.md) |
| RN-AUTH-15 | Refresh token dura 7 dias | [BE-10](BE-10-refresh-token-rotacao.md) |
| RN-AUTH-16 | Rotação: novo refresh a cada uso | [BE-10](BE-10-refresh-token-rotacao.md) |
| RN-AUTH-17 | Reuso detectado encerra a sessão | [BE-10](BE-10-refresh-token-rotacao.md) |
| RN-AUTH-18 | Refresh expirado exige novo login | [BE-10](BE-10-refresh-token-rotacao.md) |
| RN-AUTH-19 | Refresh tokens revogáveis | [BE-11](BE-11-logout-revogacao.md), [BE-15](BE-15-alteracao-senha.md), [BE-16](BE-16-exclusao-conta.md) |
| RN-AUTH-20 | Refresh token com tratamento restrito | [BE-10](BE-10-refresh-token-rotacao.md) |
| RN-AUTH-21 | Trocar a própria senha | [BE-15](BE-15-alteracao-senha.md) |
| RN-AUTH-22 | "Esqueci minha senha" fora do escopo | — (registrado em [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md), D-04) |
| RN-USER-01 | Campos do usuário | [BE-04](BE-04-dominio-usuario.md), [BE-26](BE-26-identity-servidor-grpc.md) |
| RN-USER-02 | Editar nome de exibição | [BE-14](BE-14-perfil-usuario.md) |
| RN-USER-03 | E-mail imutável | [BE-04](BE-04-dominio-usuario.md), [BE-14](BE-14-perfil-usuario.md) |
| RN-USER-04 | Inativo não autentica | [BE-09](BE-09-login.md), [BE-26](BE-26-identity-servidor-grpc.md), [BE-28](BE-28-validacao-dono-grpc.md) |
| RN-USER-05 | Excluir conta remove as tarefas | [BE-16](BE-16-exclusao-conta.md) |
| RN-TASK-01 | Campos da tarefa | [BE-05](BE-05-dominio-tarefa.md) |
| RN-TASK-02 | Título 1–200, não só espaços | [BE-05](BE-05-dominio-tarefa.md) |
| RN-TASK-03 | Descrição ≤ 2000 | [BE-05](BE-05-dominio-tarefa.md) |
| RN-TASK-04 | Prioridade Baixa/Média/Alta, padrão Média | [BE-05](BE-05-dominio-tarefa.md) |
| RN-TASK-05 | Vencimento passado aceito | [BE-05](BE-05-dominio-tarefa.md) |
| RN-TASK-06 | Estados Pendente/Concluída | [BE-05](BE-05-dominio-tarefa.md) |
| RN-TASK-07 | Nasce Pendente | [BE-05](BE-05-dominio-tarefa.md) |
| RN-TASK-08 | Concluir registra data | [BE-05](BE-05-dominio-tarefa.md), [BE-20](BE-20-concluir-reabrir-tarefa.md) |
| RN-TASK-09 | Reabrir limpa data | [BE-05](BE-05-dominio-tarefa.md), [BE-20](BE-20-concluir-reabrir-tarefa.md) |
| RN-TASK-10 | Criar informando ao menos o título | [BE-17](BE-17-criar-tarefa.md), [BE-29](BE-29-gatilho-http-criar-tarefa.md) |
| RN-TASK-11 | Editar campos da tarefa | [BE-19](BE-19-editar-tarefa.md) |
| RN-TASK-12 | Remover a própria tarefa | [BE-21](BE-21-remover-tarefa.md) |
| RN-TASK-13 | Remoção lógica | [BE-21](BE-21-remover-tarefa.md), [BE-23](BE-23-expurgo-tarefas-removidas.md) |
| RN-TASK-14 | Toda alteração atualiza `UpdatedAt` | [BE-05](BE-05-dominio-tarefa.md) |
| RN-TASK-15 | Máximo 500 tarefas ativas | [BE-17](BE-17-criar-tarefa.md), [BE-28](BE-28-validacao-dono-grpc.md) |
| RN-TASK-16 | "Atrasada" é derivada | [BE-05](BE-05-dominio-tarefa.md), [BE-22](BE-22-listagem-tarefas.md) |
| RN-AUTZ-01 | Tarefa pertence a um usuário | [BE-05](BE-05-dominio-tarefa.md), [BE-28](BE-28-validacao-dono-grpc.md) |
| RN-AUTZ-02 | Só manipula as próprias tarefas | [BE-18](BE-18-consultar-tarefa-autorizacao.md) |
| RN-AUTZ-03 | Tarefa de outro → "não encontrada" | [BE-18](BE-18-consultar-tarefa-autorizacao.md) |
| RN-AUTZ-04 | Toda operação exige sessão | [BE-13](BE-13-protecao-endpoints.md) |
| RN-LIST-01 | Lista só do usuário, não removidas | [BE-22](BE-22-listagem-tarefas.md) |
| RN-LIST-02 | Filtro por estado | [BE-22](BE-22-listagem-tarefas.md) |
| RN-LIST-03 | Filtro por prioridade | [BE-22](BE-22-listagem-tarefas.md) |
| RN-LIST-04 | Filtro por atrasadas | [BE-22](BE-22-listagem-tarefas.md) |
| RN-LIST-05 | Busca textual | [BE-22](BE-22-listagem-tarefas.md) |
| RN-LIST-06 | Ordenação padrão | [BE-22](BE-22-listagem-tarefas.md) |
| RN-LIST-07 | Paginação | [BE-22](BE-22-listagem-tarefas.md) |

**Cobertura:** 56 de 56 regras endereçadas (RN-AUTH-22 é um não-objetivo explícito).

As tasks da Onda 6 não introduzem regra de negócio nova: elas realocam a verificação de regras existentes para a fronteira entre os dois serviços. [BE-25](BE-25-contrato-grpc-identity.md), [BE-30](BE-30-configuracao-enderecos-grpc.md) e [BE-31](BE-31-verificacao-t1.md) são habilitadoras e não aparecem na tabela por RN própria.
