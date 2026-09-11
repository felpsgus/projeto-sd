# Decisões pendentes — impacto no backend

As decisões da seção 9 de [REGRAS-DE-NEGOCIO.md](../../REGRAS-DE-NEGOCIO.md) têm **padrão adotado**, então nenhuma task está bloqueada. Este documento registra: (a) qual task implementa cada decisão, (b) como o padrão foi materializado, (c) o que muda se a decisão for revista.

> **Princípio:** todo valor numérico vindo de uma decisão em aberto **DEVE** ser configuração (`IOptions<T>`), nunca constante no código. Assim, mudar a decisão é mudar `appsettings`, não código.

---

## Decisões fechadas (20/08/2026)

Estas saíram do estado provisório. Estão aqui como registro; as tasks já refletem o resultado.

### D-18 ✅ — "Atrasada" usa a data local do usuário, não UTC

**Problema que motivou:** com "hoje" em UTC, um usuário em UTC−3 vê a tarefa marcada como atrasada às **21:00 do próprio dia do vencimento** — três horas antes de vencer, todo dia.

**Decisão:** o cliente informa a sua data local; o backend a usa para calcular `IsOverdue` e o filtro `overdue`.

**Implementação:** header `X-Client-Date: yyyy-MM-dd` enviado em toda requisição, lido por um `IClientDate` request-scoped ([BE-13](BE-13-protecao-endpoints.md)). Header ausente ou inválido → **fallback para a data UTC**, sem erro. Um único ponto de leitura serve os cinco endpoints que devolvem `isOverdue`.

**Consequência no documento de regras:** a RN-TASK-16 fala em "data atual". Passa a significar **data atual do usuário**. Vale corrigir o texto em [REGRAS-DE-NEGOCIO.md](../../REGRAS-DE-NEGOCIO.md).

**Afeta:** [BE-05](BE-05-dominio-tarefa.md), [BE-13](BE-13-protecao-endpoints.md), [BE-17](BE-17-criar-tarefa.md), [BE-22](BE-22-listagem-tarefas.md) e, por herança do mapeamento compartilhado, BE-18, BE-19 e BE-20.

### D-20 ✅ — Refresh token trafega em cookie `HttpOnly`, não no corpo JSON

**Decisão:** o refresh token deixa de aparecer no corpo de request/response. O backend o emite como cookie `HttpOnly; Secure; SameSite=Strict; Path=/api/auth` e o lê de `Request.Cookies`.

**Motivo:** é a única forma de cumprir a RN-AUTH-20 — com o token no corpo, o frontend é obrigado a guardá-lo onde JavaScript alcança, e um XSS rouba uma credencial de 7 dias. Ver **FD-01** em [frontend/DECISOES-PENDENTES.md](../frontend/DECISOES-PENDENTES.md).

**Limite honesto:** um XSS ainda consegue *chamar* `/api/auth/refresh` na própria origem e obter um access token. O que ele não consegue é exfiltrar a credencial de longa duração para usar depois, de outro lugar.

**Afeta:** [BE-09](BE-09-login.md), [BE-10](BE-10-refresh-token-rotacao.md), [BE-11](BE-11-logout-revogacao.md).

### D-21 ✅ — Frontend e API no mesmo servidor / mesma origem

**Consequências, todas simplificadoras:**

- `SameSite=Strict` é viável → **não há necessidade de token anti-CSRF** nesta versão;
- não há configuração de CORS a fazer, desde que o SPA seja servido pela mesma origem da API;
- o `Path=/api/auth` do cookie continua valendo, para ele não viajar em toda requisição.

**Se isso mudar** (front em CDN/domínio separado), volta a exigir `SameSite=None; Secure`, CORS com origem explícita e proteção CSRF — e BE-09/BE-10 crescem de escopo.

### D-22 ✅ — API sem versionamento

**Decisão:** rotas permanecem `/api/tasks`, `/api/auth/login` etc., sem `/v1`.

**Justificativa:** frontend e backend são implantados juntos, no mesmo servidor (D-21), por um time só. Não há consumidor externo para quebrar.

**Consequência aceita:** toda mudança incompatível de contrato exige implantação coordenada das duas pontas. Se algum dia surgir um segundo consumidor (app móvel, integração), versionar deixa de ser opcional — e a migração será mais cara do que teria sido agora.

| # | Decisão | Padrão | Task | Onde vive o valor | Se mudar |
|---|---|---|---|---|---|
| **D-01** | Auto-cadastro liberado | Sim | [BE-07](BE-07-cadastro-usuario.md) | — | Exigir convite/aprovação: nova regra no caso de uso de cadastro |
| **D-02** | Duração do access token | 15 min | [BE-08](BE-08-emissao-jwt.md) | `Jwt:AccessTokenMinutes` | Só configuração |
| **D-03** | Bloqueio por tentativas | 5 → 15 min | [BE-12](BE-12-bloqueio-tentativas-login.md) | `Lockout:MaxAttempts`, `Lockout:WindowMinutes` | Só configuração |
| **D-04** | "Esqueci minha senha" | Fora do escopo | — | — | Nova task (BE-25+): token de reset, expiração, envio |
| **D-05** | Exclusão de conta | Apagar conta + tarefas | [BE-16](BE-16-exclusao-conta.md) | — | Anonimizar: troca o handler, mantém contrato do endpoint |
| **D-06** | Vencimento no passado | Aceita, marca atrasada | [BE-05](BE-05-dominio-tarefa.md), [BE-17](BE-17-criar-tarefa.md) | — | Recusar: vira regra de validação no request |
| **D-07** | Remoção de tarefa | Soft delete | [BE-21](BE-21-remover-tarefa.md) | — | Hard delete: remove filtro global e a task [BE-23](BE-23-expurgo-tarefas-removidas.md) |
| **D-08** | Limite de tarefas ativas | 500 | [BE-17](BE-17-criar-tarefa.md) | `Tasks:MaxActivePerUser` | Só configuração (`null` desliga o limite) |
| **D-09** | Tamanho de página | **20** (adotado aqui) | [BE-22](BE-22-listagem-tarefas.md) | `Paging:DefaultPageSize`, `Paging:MaxPageSize` | Só configuração |
| **D-10** | Duração do refresh token | 7 dias | [BE-10](BE-10-refresh-token-rotacao.md) | `Jwt:RefreshTokenDays` | Só configuração |
| **D-11** | Rotação de refresh token | Sim, uso único | [BE-10](BE-10-refresh-token-rotacao.md) | — | Sem rotação: remove a detecção de reuso (RN-AUTH-17) |

## Decisões novas, levantadas pela quebra em tasks

Não constam do documento de regras e precisam de resposta do time. Cada uma tem um padrão provisório para não travar a implementação.

| # | Questão | Padrão provisório | Task afetada |
|---|---|---|---|
| **D-12** | Período de retenção de tarefa soft-deleted antes do expurgo definitivo (RN-TASK-13 diz "período definido", sem valor) | **30 dias**, em `Tasks:SoftDeleteRetentionDays` | [BE-23](BE-23-expurgo-tarefas-removidas.md) |
| **D-13** | O expurgo roda como job in-process (`BackgroundService`) ou tarefa externa agendada? | `BackgroundService` com intervalo configurável | [BE-23](BE-23-expurgo-tarefas-removidas.md) |
| **D-14** | O bloqueio por tentativas (D-03) conta por e-mail, por IP ou por ambos? | Por e-mail (como está escrito na RN-AUTH-13) | [BE-12](BE-12-bloqueio-tentativas-login.md) |
| **D-15** | Um refresh token pode existir por dispositivo (várias sessões simultâneas) ou é sessão única? | Múltiplas sessões: cada login cria uma cadeia própria | [BE-10](BE-10-refresh-token-rotacao.md) |
| **D-16** | Busca textual (RN-LIST-05) inclui a descrição? A RN diz "opcionalmente" | Sim, título **e** descrição, case-insensitive | [BE-22](BE-22-listagem-tarefas.md) |
| **D-17** | Banco de dados alvo | **PostgreSQL** (Npgsql + Testcontainers) | [BE-02](BE-02-persistencia-base.md) |
| **D-18** | ✅ **Decidida** — "Atrasada" usa a data local do usuário | ver *Decisões fechadas* acima | [BE-13](BE-13-protecao-endpoints.md) e demais |
| **D-19** | Exclusão de conta (RN-USER-05) exige confirmação de senha? | Sim — mesma proteção da troca de senha | [BE-16](BE-16-exclusao-conta.md) |
| **D-20** | ✅ **Decidida** — refresh token em cookie `HttpOnly` | ver *Decisões fechadas* acima | [BE-09](BE-09-login.md), [BE-10](BE-10-refresh-token-rotacao.md), [BE-11](BE-11-logout-revogacao.md) |
| **D-21** | ✅ **Decidida** — front e API na mesma origem | ver *Decisões fechadas* acima | transversal |
| **D-22** | ✅ **Decidida** — API sem versionamento | ver *Decisões fechadas* acima | transversal |

### Ainda em aberto, sem bloquear

Vale uma resposta do time em algum momento, mas nenhuma trava implementação:

| # | Questão | Padrão provisório | Task |
|---|---|---|---|
| **D-23** | Edição concorrente da mesma tarefa: aceitar "último escreve vence" ou usar `ETag`/`If-Match`? | Aceitar — o dano é limitado a uma tarefa e a chance é baixa nesta escala | [BE-19](BE-19-editar-tarefa.md) |
| **D-24** | Existe limite de taxa global na API, além do bloqueio de login? | Não nesta versão | — |
| **D-25** | Por quanto tempo o log fica retido? Ele contém e-mail e `userId`, e a exclusão de conta é física (D-05) | 30 dias, alinhado a `Auth:TokenRetentionDays` | [BE-24](BE-24-observabilidade-ci.md) |

---

## Decisões da separação em dois microsserviços

A quebra do backend em **Identity Service** (servidor gRPC) e **Tasks Service** (cliente gRPC) — decisão estrutural refletida em [BE-01](BE-01-fundacao-solution.md) e detalhada em [BE-25](BE-25-contrato-grpc-identity.md) a [BE-31](BE-31-verificacao-t1.md) — levantou as decisões abaixo. Todas têm padrão adotado; nenhuma trava implementação.

### D-26 — Código compartilhado entre os serviços: `TodoList.SharedKernel`

**Questão:** com dois serviços, `Result<T>`/`Error` ([BE-03](BE-03-result-erros-validacao.md)) precisa existir nos dois. Duplicar ou compartilhar?

**Padrão adotado:** um projeto `src/Shared/TodoList.SharedKernel`, sem pacote externo, referenciado pelo `Domain` de cada serviço. Contém **apenas** `Result`, `Result<T>`, `Error` e `ErrorType`.

**Por quê:** é contrato de forma, não de negócio. Duplicado, os dois serviços divergem em como reportam erro e a extensão que traduz `Result` em resposta HTTP passa a ter duas versões sutilmente diferentes.

**Limite explícito:** o `SharedKernel` **NÃO DEVE** receber entidade, DTO de negócio, catálogo de erros ou regra. Cada acréscimo ali é acoplamento entre serviços que deveriam ser independentes — e é assim que uma separação de serviços vira um monólito distribuído. Verificado por [BE-03](BE-03-result-erros-validacao.md) CA-10.

**Afeta:** [BE-01](BE-01-fundacao-solution.md), [BE-03](BE-03-result-erros-validacao.md).

### D-27 — Um banco único, com um schema por serviço

**Padrão adotado:** um banco PostgreSQL `todolist`, com **dois schemas**:

| Schema | Dono | Tabelas |
|---|---|---|
| `identity` | Identity Service | `users`, `refresh_tokens`, `login_attempts` |
| `tasks` | Tasks Service | `tasks` |

Cada `DbContext` é configurado com o **seu** schema padrão (`HasDefaultSchema`) e mapeia **apenas** as tabelas do seu serviço. `TasksDbContext` não tem `DbSet<User>` e não conhece a tabela `identity.users`; `IdentityDbContext` não conhece `tasks.tasks`.

Existe **uma** chave estrangeira cruzando os schemas: `tasks.tasks.owner_id → identity.users(id)`, com **`ON DELETE CASCADE`**.

**Por quê banco único:** dois bancos separados custavam caro em três frentes — a exclusão de conta (RN-USER-05) deixava de ser possível de forma consistente ([BE-16](BE-16-exclusao-conta.md)), o dono de uma tarefa deixava de ter garantia estrutural, e a implantação passava a exigir duas instâncias. Nada disso se pagava nesta escala.

**Por quê schema por serviço, e não um schema só:** o schema é o que mantém a **posse** das tabelas explícita. Sem ele, nada impede o Tasks de mapear `users` e consultar o usuário direto — e, no dia em que isso acontecer, a comunicação entre os serviços vira decorativa. Com schemas e contextos separados, ler o usuário continua exigindo a chamada gRPC.

**Consequência aceita:** os dois serviços passam a depender do mesmo banco. Uma migration do Identity que altere o tipo de `users.id` quebra a FK do Tasks, e a implantação dos dois precisa ser coordenada. É o mesmo acoplamento que **D-22** já assume entre front e back.

**O que a FK não substitui:** ela garante que o dono **existe**. Não diz se ele está **ativo** (RN-USER-04) nem qual o nome de exibição — isso continua vindo de `ValidateUser` via gRPC ([BE-28](BE-28-validacao-dono-grpc.md)). A FK é a rede de segurança do banco; a chamada gRPC é a regra de negócio, e é ela que produz uma resposta clara em vez de uma violação de constraint.

**Se mudar** (voltar a dois bancos): [BE-16](BE-16-exclusao-conta.md) precisa de exclusão coordenada entre serviços, e a validação de dono na criação passa a ser a **única** garantia de integridade.

**Afeta:** [BE-02](BE-02-persistencia-base.md), [BE-16](BE-16-exclusao-conta.md), [BE-17](BE-17-criar-tarefa.md), [BE-28](BE-28-validacao-dono-grpc.md).

### D-28 — Indisponibilidade do Identity: fail-closed com 503

**Questão:** o Identity está inalcançável no momento de criar uma tarefa. Criar assumindo o dono válido, ou recusar?

**Padrão adotado:** **recusar**. `503` com `identity.unavailable` e cabeçalho `Retry-After`. Nada é persistido. Deadline de **2 s** em `Identity:GrpcTimeoutSeconds`.

**Por quê, mesmo com a FK de D-27:** a FK impede tarefa de dono inexistente, mas **não** impede tarefa de dono **inativo** (RN-USER-04) — o registro em `identity.users` continua lá. Sem o Identity, o Tasks não tem como saber o estado do usuário, e criar assumindo "ativo" grava uma tarefa que a regra proibia.

**Consequência aceita:** o Identity fora do ar impede a criação de tarefas, mesmo de usuários perfeitamente válidos. Nesta escala é preferível a violar RN-USER-04 em silêncio.

**Se mudar** (fail-open apoiado só na FK): tarefas de usuário inativo passam a existir, e alguém precisa decidir o que fazer com elas depois.

**Registrar como ADR** ([BE-24](BE-24-observabilidade-ci.md)).

**Afeta:** [BE-27](BE-27-tasks-cliente-grpc.md), [BE-28](BE-28-validacao-dono-grpc.md).

### D-29 — `.proto` em `contracts/` na raiz, referenciado por caminho relativo

**Padrão adotado:** um único `contracts/identity/v1/identity.proto`, referenciado pelos dois `.csproj` que geram código, cada um com o seu `GrpcServices` (`Server` no Identity, `Client` no Tasks).

**Alternativas descartadas:** uma cópia por serviço (as cópias divergem em silêncio, e o defeito aparece em runtime como campo vazio) e um pacote NuGet de contratos (versionamento e publicação para dois serviços do mesmo repositório, sem consumidor externo).

**Consequência aceita:** caminho relativo com `..\..\..` nos `.csproj`. Feio, e o preço de ter uma fonte única.

**Afeta:** [BE-25](BE-25-contrato-grpc-identity.md), [BE-26](BE-26-identity-servidor-grpc.md), [BE-27](BE-27-tasks-cliente-grpc.md).

### D-30 — Identidade provisória no Tasks: `X-User-Id` sob flag

> **✅ Fechada no T2.** O gatilho REST foi removido por [BE-35](BE-35-tasks-servidor-grpc.md): o Tasks passou a ser alcançável só por gRPC, e a identidade chega pela metadata `x-user-id` que o Gateway preenche **depois** de validar o token (**D-34**). A flag `Tasks:AllowAnonymousCreate` deixou de existir. O texto abaixo fica como registro.

**Questão:** enquanto não há autenticação na borda do Tasks, de onde vem o dono da tarefa?

**Padrão adotado:** header `X-User-Id`, lido por uma implementação alternativa de `ICurrentUser`, ativada **apenas** com `Tasks:AllowAnonymousCreate=true` (padrão `false`).

**Por quê no header e não no corpo:** o dono é identidade, não dado de negócio. No corpo, viraria campo do modelo e violaria [BE-17](BE-17-criar-tarefa.md) CA-22, que teria de ser desfeito depois.

**Risco assumido:** com a flag ligada, qualquer um cria tarefa em nome de qualquer usuário. Aceitável em ambiente local e de demonstração, inaceitável em ambiente exposto — daí o padrão `false`, o aviso na inicialização e [BE-29](BE-29-gatilho-http-criar-tarefa.md) CA-08, que exige que o header seja ignorado no modo definitivo.

**Data de morte:** o modo cai quando a autenticação na borda entrar. O sinal de que isso aconteceu é o endpoint sair da allowlist do teste de guarda de rotas ([BE-13](BE-13-protecao-endpoints.md), CA-07).

**Afeta:** [BE-13](BE-13-protecao-endpoints.md), [BE-29](BE-29-gatilho-http-criar-tarefa.md).

### D-31 — A chave de assinatura nunca sai do Identity; quem valida token é `ValidateToken`

**O problema:** [BE-08](BE-08-emissao-jwt.md) escolheu **HS256** com chave simétrica, e a justificativa escrita era explícita: "emissor e validador são o mesmo serviço". Com a separação, isso deixou de ser verdade. Distribuir `Jwt:SigningKey` para o Tasks o transformaria de **validador** em **emissor em potencial**: com a chave simétrica, quem valida também consegue forjar. O próprio BE-08 já previa o remédio — "migrar para RS256 se houver mais de um consumidor".

**Padrão adotado:** nenhum consumidor a mais. O **Identity é a única autoridade sobre tokens** e a chave permanece só nele.

- O Identity emite e valida os próprios tokens localmente (BE-08 segue como está, dentro do Identity).
- Quem precisa validar um token **pergunta ao Identity**, pelo RPC `ValidateToken` já declarado no contrato ([BE-25](BE-25-contrato-grpc-identity.md)) — que deixa de ser apenas um espaço reservado e passa a ser o mecanismo.
- O **Tasks Service não valida JWT e não recebe `Jwt:SigningKey`.** Ele obtém a identidade do chamador, não do token.

**Por que não RS256:** resolveria o problema de forjar (o Tasks teria só a chave pública), mas exigiria distribuição de chave, rotação e provavelmente um endpoint JWKS — trabalho real para um sistema com um único validador de fato. O caminho por `ValidateToken` já estava no contrato e concentra a decisão em um lugar só.

**Consequência aceita:** validar token vira uma chamada de rede, com o custo e o modo de falha de uma. Como quem valida é o API Gateway (**D-32**), é **uma** chamada por requisição de entrada, não uma por serviço — e ela pode ganhar cache de vida curta se a latência incomodar.

**Se mudar** (validação local distribuída): aí sim RS256, com a chave pública publicada pelo Identity e o `ValidateToken` reduzido a caminho de exceção.

**Registrar como ADR**, substituindo o ADR de HS256 previsto em [BE-24](BE-24-observabilidade-ci.md) — a decisão continua HS256, mas por outra razão e com outro alcance.

**Afeta:** [BE-08](BE-08-emissao-jwt.md), [BE-13](BE-13-protecao-endpoints.md), [BE-25](BE-25-contrato-grpc-identity.md), [BE-26](BE-26-identity-servidor-grpc.md).

### D-32 — A origem única passa a ser o API Gateway; os serviços não são alcançáveis pelo navegador

**O problema:** **D-21** / **FD-16** fecharam "front e API na mesma origem", e disso dependem três coisas — `SameSite=Strict` sem token anti-CSRF, ausência de configuração de CORS, e o cookie de refresh com `Path=/api/auth` (**D-20**). Com dois serviços em portas diferentes, o frontend passaria a falar com **duas** origens, e as três premissas caem de uma vez.

**Padrão adotado:** **D-21 continua valendo, com a origem redefinida.** A origem única é o **API Gateway**; o frontend é servido por ele e fala **só** com ele. Identity e Tasks ficam atrás, alcançáveis apenas pelo Gateway, por gRPC.

```
navegador ──HTTPS/JSON──▶ API Gateway ──┬──gRPC──▶ Identity Service
   (origem única)                        └──gRPC──▶ Tasks Service
```

Com isso: `/api/auth/*` e `/api/tasks` voltam a ser rotas da **mesma** origem, `SameSite=Strict` segue viável, não há CORS a configurar e `Path=/api/auth` continua correto — sem nenhuma mudança nas tasks FE.

**Consequência que precisa de atenção na implantação:** os dois serviços passam a **confiar no chamador** para saber quem é o usuário (**D-31**, **D-30**). Isso só é seguro enquanto o Gateway for o único caminho até eles. Publicá-los com endpoint HTTPS aberto, sem o Gateway na frente, transformaria `X-User-Id` em falsificação de identidade trivial. **Ao levar isto para a nuvem, os serviços de backend NÃO DEVEM ficar publicamente acessíveis** — apenas o Gateway.

**Enquanto o Gateway não existe:** nada a fazer no frontend, que não é tocado nesta etapa. O acesso direto ao Tasks é o gatilho provisório de [BE-29](BE-29-gatilho-http-criar-tarefa.md), em ambiente local. **NÃO DEVE** ser implementado CORS "para funcionar por enquanto" — seria trabalho descartado e mascararia o desenho correto.

**Afeta:** [BE-29](BE-29-gatilho-http-criar-tarefa.md), transversal na etapa do Gateway e na de implantação. Do lado do frontend, ver **FD-16**.

---

## Decisões do API Gateway (T2)

A etapa do Gateway — [BE-32](BE-32-contratos-grpc-t2.md) a [BE-39](BE-39-verificacao-t2.md) — materializa o desenho de **D-31** e **D-32** e levantou as decisões abaixo. Todas estão fechadas (10/09/2026).

```
cliente ──HTTP/JSON──▶ Gateway :8080
                         ├─ autenticação ──gRPC ValidateToken──▶ Identity :5081
                         ├─ validação do payload (400)
                         └─ gRPC CreateTask (metadata x-user-id) ──▶ Tasks :5101
                                                                     └─gRPC ValidateUser─▶ Identity
```

### D-33 — O Gateway é um projeto só, sem Domain/Application

**Questão:** a convenção (seção 2.1 de [CONVENCOES-CODIGO.md](../../CONVENCOES-CODIGO.md)) prevê quatro projetos por serviço. O Gateway segue?

**Padrão adotado:** não. `src/Gateway/TodoList.Gateway.Api` é **um** projeto Web, organizado por pasta (`Endpoints/`, `Authentication/`, `Validation/`, `Backends/`, `ErrorHandling/`, `Contracts/`).

**Por quê:** o Gateway não tem regra de negócio nem persistência — ele autentica, valida o formato do payload e traduz protocolo. Domain e Application sairiam vazios, e Infrastructure seria só o registro dos clientes gRPC. Quatro projetos para isso é cerimônia, não arquitetura.

**Limite explícito:** o Gateway **NÃO DEVE** referenciar nenhum projeto do Identity ou do Tasks — nem `SharedKernel`. A única fronteira com os serviços são os `.proto` em `contracts/` (**D-29**). Verificado por teste de arquitetura ([BE-36](BE-36-api-gateway.md)). No dia em que uma regra de negócio aparecer no Gateway, ela está no lugar errado.

**Afeta:** [BE-36](BE-36-api-gateway.md), [BE-38](BE-38-containerizacao.md).

### D-34 — A identidade chega ao Tasks pela metadata gRPC `x-user-id`

**Questão:** o Gateway validou o token e sabe quem é o usuário. Como o Tasks fica sabendo?

**Padrão adotado:** metadata gRPC **`x-user-id`** (o `sub` do token) e **`x-client-date`** (repassado do request, **D-18**), preenchidos por um interceptor de cliente no Gateway. **Nunca** no corpo da mensagem — `tasks.proto` não tem campo de dono.

**Por quê:** é a continuação direta de **D-30**: o dono é identidade, não dado de negócio. Metadata gRPC é header HTTP/2, então a leitura no Tasks é a mesma que o header provisório já fazia via `IHttpContextAccessor` — o `CreateTaskHandler` não muda.

**Risco assumido — o mesmo de D-30, agora permanente:** o Tasks **confia no chamador**. Quem alcançar a porta gRPC do Tasks cria tarefa em nome de qualquer usuário. Por isso o Tasks **NÃO DEVE** ser publicamente acessível (**D-32**): na VM, a porta fica fechada no firewall; no Cloud Run (T3), o serviço é privado (`--no-allow-unauthenticated`) e só a conta de serviço do Gateway pode invocá-lo.

**Se mudar** (confiança zero entre serviços): o Gateway repassa o próprio JWT e o Tasks chama `ValidateToken` — uma chamada de rede a mais por requisição, em troca de não depender do isolamento de rede.

**Afeta:** [BE-35](BE-35-tasks-servidor-grpc.md), [BE-36](BE-36-api-gateway.md).

### D-35 — Mapeamento de erro gRPC ↔ HTTP, com o `errorCode` no trailer

**Questão:** o Tasks devolve `Result<T>` com `Error(Code, Message, Type)`. Como esse erro atravessa o gRPC e volta a ser um `ProblemDetails` HTTP no Gateway, sem perder o código do catálogo?

**Padrão adotado:**

| `ErrorType` (Tasks) | `StatusCode` gRPC | HTTP (Gateway) |
|---|---|---|
| `Validation` | `InvalidArgument` | 400 |
| `NotFound` | `NotFound` | 404 |
| `Conflict` | `FailedPrecondition` | 409 |
| `Unavailable` | `Unavailable` | 503 + `Retry-After` |
| `Failure` | `Internal` | 500 |
| — (sem identidade) | `Unauthenticated` | 401 |
| — (deadline estourado) | `DeadlineExceeded` | 503 + `Retry-After` |

O `Error.Code` (ex.: `task.owner_inactive`) viaja no trailer **`error-code`**; a mensagem, no `Status.Detail`. O Gateway reconstrói o `ProblemDetails` com o mesmo `errorCode` que o Tasks devolvia em REST — o contrato visto pelo cliente não muda.

**Por quê trailer e não `google.rpc.Status` com detalhes tipados:** o rich error model exige `Grpc.StatusProto` e mensagens de detalhe próprias — mais contrato para dois serviços do mesmo repositório. Um trailer string resolve o único dado que falta.

**Afeta:** [BE-35](BE-35-tasks-servidor-grpc.md), [BE-36](BE-36-api-gateway.md).

### D-36 — O login do T2 é um recorte de BE-09

**Questão:** o T2 exige **401** para token ausente ou inválido — então precisa existir token válido. O fluxo completo de autenticação (BE-06 a BE-12) cabe no prazo?

**Padrão adotado:** um recorte. Entram [BE-06](BE-06-hash-senha.md) (hash, escopo integral), [BE-08](BE-08-emissao-jwt.md) (emissão e validação de JWT, sem o Bearer no pipeline do Identity) e um RPC **`Login`** que troca e-mail + senha por **access token** ([BE-33](BE-33-login-minimo-grpc.md)), exposto pelo Gateway como `POST /api/auth/login`. **Ficam de fora:** refresh token e cookie ([BE-10](BE-10-refresh-token-rotacao.md)), logout ([BE-11](BE-11-logout-revogacao.md)), bloqueio por tentativas ([BE-12](BE-12-bloqueio-tentativas-login.md)) e cadastro ([BE-07](BE-07-cadastro-usuario.md)) — os usuários continuam vindo do seed de demonstração, agora com hash de senha real.

**Por quê login real e não um token de desenvolvimento:** um emissor de token "só para demo" seria mais uma rota provisória a remover — exatamente o tipo de dívida que D-30 acabou de pagar. O recorte é código definitivo: BE-09 completa o que falta **em cima** dele, sem reescrever.

**Consequência aceita:** sem refresh, a sessão dura o access token (15 min, **D-02**) e depois exige novo login. Para a demonstração e para o T3, é suficiente.

**Afeta:** [BE-06](BE-06-hash-senha.md), [BE-08](BE-08-emissao-jwt.md), [BE-09](BE-09-login.md), [BE-33](BE-33-login-minimo-grpc.md), [BE-34](BE-34-validate-token-real.md).

### D-37 — Cada backend tem uma porta HTTP/2 que vira a `$PORT` do Cloud Run

**Questão:** o Cloud Run (T3) roteia **uma** porta por serviço. O Identity hoje escuta em duas (5080 REST, 5081 gRPC) e o Tasks falava REST.

**Padrão adotado:** todo serviço de backend expõe um endpoint Kestrel **`Grpc`** com `Protocols=Http2` — Identity em `5081`, Tasks em `5101` localmente e na VM, e `8080` no container. É por ele que passa todo o tráfego entre serviços. O endpoint `Http` (Http1) continua existindo só para `/health` operado por humano (`curl`); ele não é necessário no Cloud Run. Para os probes, cada backend mapeia o **gRPC Health Checking Protocol** (`Grpc.AspNetCore.HealthChecks`), que o Cloud Run sabe consultar.

**Por quê não `Http1AndHttp2` na mesma porta:** sem TLS não há ALPN, e o Kestrel não negocia HTTP/2 em texto claro numa porta que também aceita HTTP/1.1. No Cloud Run, com `--use-http2`, o tráfego chega ao container como h2c — então a porta de serviço precisa ser `Http2` pura.

**Afeta:** [BE-35](BE-35-tasks-servidor-grpc.md), [BE-37](BE-37-deploy-t2-vm.md), [BE-38](BE-38-containerizacao.md).
