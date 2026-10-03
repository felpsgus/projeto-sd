# FE-02 — Contratos de API e camada HTTP tipada

| | |
|---|---|
| **Domínio** | Infraestrutura |
| **Depende de** | [FE-01](FE-01-fundacao-workspace.md) |
| **Bloqueia** | FE-03 em diante |
| **Regras cobertas** | habilita todas as chamadas de API |
| **Estimativa** | M |

> **Recorte do T2 (21/09/2026):** entra **parcial**. Tipos e serviços só de login (`LoginRequest`/`LoginHttpResponse`, sem `RegisterRequest`/`UserResponse`/`ChangePasswordRequest`/`DeleteAccountRequest`) e de tarefas (criar, listar, obter — sem `UpdateTaskRequest`). Mantém o interceptor de `X-Client-Date` (FD-17) e `ProblemDetails`/catálogo de erros do recorte. **Fica para depois:** mock de API completo para os fluxos fora de escopo, `withCredentials`/cookie de refresh (não existem no T2 — não há `RefreshRequest`, ver nota abaixo). Depende de [BE-33](../backend/BE-33-login-minimo-grpc.md)/[BE-36](../backend/BE-36-api-gateway.md) (login e criar tarefa) e de [BE-41](../backend/BE-41-listar-e-consultar-tarefas-grpc.md) (listar/obter tarefa).
>
> **Contrato real extraído do Gateway (`src/Gateway/TodoList.Gateway.Api/Contracts/`):** `LoginHttpRequest { email, password }` (nomes exatos: `Email`, `Password`, serializados em camelCase pelo Gateway); resposta 200 `LoginHttpResponse { accessToken, expiresAt }` — **sem `refreshToken`**, o que já é compatível com FD-01 no T2. `CreateTaskHttpRequest { title, description, priority, dueDate }`, todos string (inclusive `priority`/`dueDate`, para caírem em erro de validação por campo em vez de erro de binding). Resposta de erro 400 usa `Results.ValidationProblem`, cujas chaves de `errors` são o **nome C# da propriedade em PascalCase** (`Title`, `Description`, `Priority`, `DueDate`), não camelCase — o mapeamento de FE-03 precisa casar com essa grafia exata.

## Objetivo

Todo endpoint do backend tem um tipo TypeScript correspondente e um único ponto de acesso HTTP. Nenhuma feature monta URL ou tipa resposta na mão.

## Escopo

### Inclui

- **Tipos do contrato** em `core/api/models/`, espelhando o que as tasks BE definem:

  | Tipo | Origem |
  |---|---|
  | `RegisterRequest`, `UserResponse` | [BE-07](../backend/BE-07-cadastro-usuario.md), [BE-14](../backend/BE-14-perfil-usuario.md) |
  | `LoginRequest`, `AuthTokensResponse` | [BE-09](../backend/BE-09-login.md) — **sem** `refreshToken`: ele vive só no cookie (FD-01) |
  | `ChangePasswordRequest`, `DeleteAccountRequest` | [BE-15](../backend/BE-15-alteracao-senha.md), [BE-16](../backend/BE-16-exclusao-conta.md) |
  | `TaskResponse`, `CreateTaskRequest`, `UpdateTaskRequest` | [BE-17](../backend/BE-17-criar-tarefa.md), [BE-19](../backend/BE-19-editar-tarefa.md) |
  | `TaskListQuery`, `PagedResult<T>` | [BE-22](../backend/BE-22-listagem-tarefas.md) |
  | `ProblemDetails` | [BE-03](../backend/BE-03-result-erros-validacao.md) |

- Enums/uniões literais: `TaskStatus = 'Pending' \| 'Completed'`, `TaskPriority = 'Low' \| 'Medium' \| 'High'`. **Nenhuma string solta** de status ou prioridade em componente.
- Catálogo de **códigos de erro** do backend como união de literais (`'auth.invalid_credentials' | 'task.active_limit_reached' | ...`) — consumido por FE-03.
- `ApiClient` em `core/api/`: envolve `HttpClient`, aplica `apiBaseUrl` do environment, expõe métodos tipados. **Nenhuma feature concatena URL.**
- Serviços de recurso por domínio (`AuthApi`, `AccountApi`, `TasksApi`), cada um com métodos que retornam tipos do contrato.
- Construção de query string tipada para a listagem, incluindo o parâmetro **repetível** `priority`.
- **Interceptor `X-Client-Date`** (decisão **FD-17**): anexa a data local do usuário, formatada `yyyy-MM-dd`, a toda requisição destinada ao `apiBaseUrl`. É o que faz o backend calcular `isOverdue` no fuso certo, nos cinco endpoints, sem repetição.
- **`withCredentials: true`** nas chamadas a `/api/auth/*` (**FD-16**), para o cookie de refresh ser anexado.
- **Mock de API** para desenvolvimento e testes: implementação alternativa dos serviços, ativável por flag de environment, permitindo trabalhar no frontend antes do endpoint existir.
- `provideHttpClient(withFetch(), withInterceptors([...]))` registrado em `app.config.ts`.

### Não inclui

- Interceptor de autenticação (FE-06) e de erro (FE-03) — a lista de interceptors é criada aqui, com apenas o de `X-Client-Date`.
- Cache ou estado de dados (FE-14).

## Notas técnicas

- **Os tipos são escritos à mão a partir das tasks BE, não gerados** nesta versão — o OpenAPI só fica completo ao fim de [BE-24](../backend/BE-24-observabilidade-ci.md). Quando estiver, avaliar geração automática; até lá, CA-11 é o que impede a divergência.
- Datas: `dueDate` é `string` no formato `yyyy-MM-dd` (data pura, **sem** fuso). Converter para `Date` no cliente causaria deslocamento de um dia dependendo do fuso do navegador — **não fazer**. Manter como string e formatar para exibição.
- `createdAt`/`updatedAt`/`completedAt` são ISO-8601 em UTC; aí sim `Date` é apropriado para exibição local.
- Nada de `any` nas respostas: `HttpClient` sempre com genérico explícito.
- O mock não é um "backend paralelo" — ele devolve dados fixos e os mesmos formatos de erro. Se começar a ter regra própria, virou dívida.
- **O `X-Client-Date` tem a mesma armadilha do `dueDate`** (FD-19): `new Date().toISOString().slice(0,10)` devolve a data **UTC**, não a local — e em UTC−3, depois das 21:00, manda o dia seguinte, reintroduzindo exatamente o bug que a decisão D-18 resolveu. Montar a partir dos componentes locais (`getFullYear`/`getMonth`/`getDate`) ou de `Intl.DateTimeFormat` com `en-CA`.
- **Não existe tipo `RefreshRequest`** e `AuthTokensResponse` **não tem** campo `refreshToken` (FD-01). Se um deles aparecer no código, é sinal de que alguém está reintroduzindo o contrato antigo.

## Critérios de aceite

- [x] **CA-01** — Existe um tipo TypeScript para o request e para a response de **cada** endpoint listado no escopo.
- [x] **CA-02** — `TaskStatus` e `TaskPriority` são uniões de literais; um valor inválido (`'Urgente'`) **não compila**.
- [x] **CA-03** — Nenhum componente ou serviço de feature monta URL de API: o `apiBaseUrl` aparece em **um** lugar (verificado por busca no código).
- [x] **CA-04** — Trocar `apiBaseUrl` no environment redireciona todas as chamadas, sem alteração de código.
- [x] **CA-05** — `HttpClient` é chamado sempre com genérico explícito; não há resposta tipada como `any` ou `unknown` não tratado.
- [x] **CA-06** — A query string da listagem serializa corretamente: filtros ausentes **não** aparecem na URL, e `priority` repetido gera `priority=low&priority=high`.
- [x] **CA-07** — `dueDate` trafega como `string` `yyyy-MM-dd` em request e response; nenhum `new Date()` é aplicado a ela na camada de API.
- [ ] **CA-08** — Um `TaskResponse` com `dueDate: "2026-01-01"` exibido em um navegador configurado em `UTC-3` mostra **1 de janeiro**, não 31 de dezembro.
- [x] ~~**CA-09** — O mock de API é ativável por flag e devolve os mesmos formatos de sucesso e de erro (`ProblemDetails`) do backend real.~~ **Substituído (03/10/2026, issue #15):** não há mock de API no aplicativo. A stack inteira sobe com `docker compose --profile full up -d`, e um modo de dados simulados no frontend contraria o requisito do T2 de não usar mocks (D-39). Mocks existem só nos specs, via `HttpTestingController`.
- [x] ~~**CA-10** — Com o mock ativo, a aplicação sobe e navega sem nenhuma chamada de rede real (verificado com a rede desligada).~~ **Substituído (03/10/2026, issue #15):** sem mock por flag (ver CA-09).
- [ ] **CA-11** — Um teste de contrato verifica os tipos contra os exemplos de payload documentados nas tasks BE — se o backend mudar o contrato, o teste quebra. Os payloads de exemplo ficam versionados em `src/testing/fixtures/`.
- [x] **CA-12** — Nenhum código sensível (senha, token) é logado pela camada HTTP.

### `X-Client-Date` e cookie (FD-16, FD-17)

- [x] **CA-13** — Toda requisição ao `apiBaseUrl` inclui `X-Client-Date` no formato `yyyy-MM-dd`.
- [x] **CA-14** — Requisições a URLs fora do `apiBaseUrl` **não** recebem o header.
- [x] **CA-15** — Com o relógio do navegador em `2026-08-20T21:30` local e fuso `UTC−3` (UTC já em `2026-08-21T00:30`), o header enviado é **`2026-08-20`** — a data **local**, não a UTC. Este é o teste que impede a reintrodução do bug de D-18.
- [x] **CA-16** — O mesmo vale para o outro lado: em fuso `UTC+9`, o header reflete a data local, não a UTC.
- [ ] **CA-17** — As chamadas a `/api/auth/*` usam `withCredentials: true`; sem isso o cookie de refresh não é anexado (FD-16).
- [x] **CA-18** — Nenhum tipo `RefreshRequest` existe, e `AuthTokensResponse` não declara `refreshToken` (FD-01) — verificado por compilação e busca no código.

## Testes obrigatórios

- Unidade: serialização de query string — CA-06.
- Unidade: `ApiClient` com `HttpTestingController` (ou `provideHttpClientTesting`) verificando URL, método, corpo e headers de cada chamada.
- **Unidade: CA-08, CA-15 e CA-16 — testes com fuso simulado, obrigatórios.** O deslocamento de dia por UTC é o bug mais fácil de introduzir aqui e o mais chato de descobrir em produção, porque só aparece nas últimas horas do dia.
- Contrato: CA-11.

## Decisões em aberto

- **FD-01**, **FD-16**, **FD-17**, **FD-19** — ✅ decididas. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).

## Auditoria dos critérios (03/10/2026)

Critérios conferidos contra o código em 03/10/2026. Marcados: 13 de 18.

| CA | Situação | Evidência / motivo |
|---|---|---|
| CA-03 | atendido com ressalva | `apiBaseUrl` é lido em `ApiClient.resolve` e em `isApiRequest` (`api-url.util.ts`), ambos em `core/api`; nenhuma feature monta URL nem injeta `HttpClient`. |
| CA-08 | em aberto | `formatDueDate` (`features/tasks/task-date.util.ts`) formata por string e independe de fuso, mas não há teste com fuso simulado (UTC-3): o teste obrigatório do critério não existe. |
| CA-09 | em aberto | Não existe mock de API ativável por flag (`environment` só tem `apiBaseUrl`, `refreshSkewSeconds`, `showHttpStatusIndicator`); mocks só existem em specs via `HttpTestingController`. |
| CA-10 | em aberto | Depende do mock de API (CA-09), inexistente. |
| CA-11 | em aberto | As fixtures de `src/testing/fixtures/` existem, mas `TASK_RESPONSE_FIXTURE` e `LOGIN_RESPONSE_FIXTURE` não são usadas por nenhum spec; só `PROFILE_RESPONSE_FIXTURE` entra em teste. Não há teste de contrato que quebre se o backend mudar o payload de tarefa/login. |
| CA-17 | em aberto | `withCredentials: true` só em `refresh`, `logout` e `logout-all` (`auth-api.service.ts`); `login` e `register` não o enviam (funciona por ser mesma origem, FD-16, mas o critério diz "as chamadas a `/api/auth/*`"). Os testes só cobrem refresh/logout. |

CA-04 atendido por desenho (`ApiClient.resolve` único), sem teste que troque o environment. CA-06 coberto por `tasks-api.service.spec.ts` (params ausentes) e `tasks-page.component.spec.ts` (`priority=low&priority=high`).
