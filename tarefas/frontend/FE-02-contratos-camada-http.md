# FE-02 — Contratos de API e camada HTTP tipada

| | |
|---|---|
| **Domínio** | Infraestrutura |
| **Depende de** | [FE-01](FE-01-fundacao-workspace.md) |
| **Bloqueia** | FE-03 em diante |
| **Regras cobertas** | habilita todas as chamadas de API |
| **Estimativa** | M |

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

- [ ] **CA-01** — Existe um tipo TypeScript para o request e para a response de **cada** endpoint listado no escopo.
- [ ] **CA-02** — `TaskStatus` e `TaskPriority` são uniões de literais; um valor inválido (`'Urgente'`) **não compila**.
- [ ] **CA-03** — Nenhum componente ou serviço de feature monta URL de API: o `apiBaseUrl` aparece em **um** lugar (verificado por busca no código).
- [ ] **CA-04** — Trocar `apiBaseUrl` no environment redireciona todas as chamadas, sem alteração de código.
- [ ] **CA-05** — `HttpClient` é chamado sempre com genérico explícito; não há resposta tipada como `any` ou `unknown` não tratado.
- [ ] **CA-06** — A query string da listagem serializa corretamente: filtros ausentes **não** aparecem na URL, e `priority` repetido gera `priority=low&priority=high`.
- [ ] **CA-07** — `dueDate` trafega como `string` `yyyy-MM-dd` em request e response; nenhum `new Date()` é aplicado a ela na camada de API.
- [ ] **CA-08** — Um `TaskResponse` com `dueDate: "2026-01-01"` exibido em um navegador configurado em `UTC-3` mostra **1 de janeiro**, não 31 de dezembro.
- [ ] **CA-09** — O mock de API é ativável por flag e devolve os mesmos formatos de sucesso e de erro (`ProblemDetails`) do backend real.
- [ ] **CA-10** — Com o mock ativo, a aplicação sobe e navega sem nenhuma chamada de rede real (verificado com a rede desligada).
- [ ] **CA-11** — Um teste de contrato verifica os tipos contra os exemplos de payload documentados nas tasks BE — se o backend mudar o contrato, o teste quebra. Os payloads de exemplo ficam versionados em `src/testing/fixtures/`.
- [ ] **CA-12** — Nenhum código sensível (senha, token) é logado pela camada HTTP.

### `X-Client-Date` e cookie (FD-16, FD-17)

- [ ] **CA-13** — Toda requisição ao `apiBaseUrl` inclui `X-Client-Date` no formato `yyyy-MM-dd`.
- [ ] **CA-14** — Requisições a URLs fora do `apiBaseUrl` **não** recebem o header.
- [ ] **CA-15** — Com o relógio do navegador em `2026-08-20T21:30` local e fuso `UTC−3` (UTC já em `2026-08-21T00:30`), o header enviado é **`2026-08-20`** — a data **local**, não a UTC. Este é o teste que impede a reintrodução do bug de D-18.
- [ ] **CA-16** — O mesmo vale para o outro lado: em fuso `UTC+9`, o header reflete a data local, não a UTC.
- [ ] **CA-17** — As chamadas a `/api/auth/*` usam `withCredentials: true`; sem isso o cookie de refresh não é anexado (FD-16).
- [ ] **CA-18** — Nenhum tipo `RefreshRequest` existe, e `AuthTokensResponse` não declara `refreshToken` (FD-01) — verificado por compilação e busca no código.

## Testes obrigatórios

- Unidade: serialização de query string — CA-06.
- Unidade: `ApiClient` com `HttpTestingController` (ou `provideHttpClientTesting`) verificando URL, método, corpo e headers de cada chamada.
- **Unidade: CA-08, CA-15 e CA-16 — testes com fuso simulado, obrigatórios.** O deslocamento de dia por UTC é o bug mais fácil de introduzir aqui e o mais chato de descobrir em produção, porque só aparece nas últimas horas do dia.
- Contrato: CA-11.

## Decisões em aberto

- **FD-01**, **FD-16**, **FD-17**, **FD-19** — ✅ decididas. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
