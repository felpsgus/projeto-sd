# BE-01 — Fundação da solution, tooling e qualidade

| | |
|---|---|
| **Domínio** | Infraestrutura |
| **Serviço** | ambos (Identity e Tasks) |
| **Depende de** | — |
| **Bloqueia** | todas as demais |
| **Regras cobertas** | nenhuma diretamente (habilita todas) |
| **Estimativa** | M |

## Objetivo

Existe uma solution .NET 10 compilando, com **dois serviços** (Identity e Tasks), cada um com os quatro projetos da Clean Architecture, um kernel compartilhado, a pasta do contrato gRPC compartilhado, regras de estilo automatizadas e **duas** APIs que sobem e respondem a um health check.

## Escopo

### Inclui

- `global.json` fixando o SDK **10.x**.
- Solution `TodoList.sln` organizada em **dois serviços independentes** mais o código compartilhado:

  ```
  TodoList.sln
  ├── contracts/
  │   └── identity/v1/identity.proto        ← contrato gRPC compartilhado (BE-25)
  ├── src/
  │   ├── Shared/
  │   │   └── TodoList.SharedKernel         ← Result<T>, Error, ErrorType (BE-03). Sem pacote externo.
  │   ├── Identity/                         ← Microsserviço B — servidor gRPC
  │   │   ├── TodoList.Identity.Domain      ← referencia apenas SharedKernel
  │   │   ├── TodoList.Identity.Application
  │   │   ├── TodoList.Identity.Infrastructure
  │   │   └── TodoList.Identity.Api
  │   └── Tasks/                            ← Microsserviço A — cliente gRPC
  │       ├── TodoList.Tasks.Domain         ← referencia apenas SharedKernel
  │       ├── TodoList.Tasks.Application
  │       ├── TodoList.Tasks.Infrastructure
  │       └── TodoList.Tasks.Api
  └── tests/
      ├── TodoList.Identity.UnitTests · TodoList.Identity.IntegrationTests
      └── TodoList.Tasks.UnitTests    · TodoList.Tasks.IntegrationTests
  ```

  - Dentro de **cada** serviço vale o mesmo grafo: `Api → Infrastructure → Application → Domain → SharedKernel`.
  - **Nenhum projeto de um serviço referencia projeto do outro.** Em código, a única coisa que os dois compartilham é `SharedKernel` e o `.proto` de `contracts/` (decisão **D-26**). Em dados, compartilham um banco com um schema para cada (**D-27**, [BE-02](BE-02-persistencia-base.md)).
  - `Identity.Api` e `Tasks.Api` são Web APIs (Minimal APIs) com `Program.cs` próprio, portas próprias e `appsettings` próprios.
- Divisão de responsabilidades entre os serviços, refletida nas demais tasks:
  - **Identity Service** — usuários, credenciais e tokens (BE-04, BE-06 a BE-16). Expõe também o servidor gRPC (BE-26).
  - **Tasks Service** — tarefas (BE-05, BE-17 a BE-23). Consome o Identity por gRPC (BE-27, BE-28).
- `Directory.Build.props` na raiz com `TargetFramework=net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors` para analyzers relevantes e `EnableNETAnalyzers=true`.
- `.editorconfig` único na raiz com as convenções de nomenclatura da seção 3 de `CONVENCOES-CODIGO.md`.
- `.gitignore` para .NET.
- Padrão de organização de endpoints: interface/classe base de feature usando `IEndpointRouteBuilder` + `MapGroup`, com um registrador que descobre e mapeia todos os grupos.
- Configuração tipada com `IOptions<T>` e validação na inicialização (`ValidateOnStart`), **em cada serviço**.
- OpenAPI/Swagger exposto em ambiente de desenvolvimento, **em cada serviço**.
- Endpoint `GET /health` (liveness) anônimo, **em cada serviço**.
- Portas de desenvolvimento fixadas em `appsettings.Development.json`, sem conflito entre os dois serviços — e **nunca** hardcoded no código (ver BE-30):

  | Serviço | Endpoint | Porta padrão em dev | Protocolo |
  |---|---|---|---|
  | Identity | HTTP/REST | 5080 | HTTP/1.1 |
  | Identity | gRPC | 5081 | **HTTP/2 (h2c)** |
  | Tasks | HTTP/REST | 5100 | HTTP/1.1 |

- `README.md` na raiz com: pré-requisitos, como restaurar, **como subir os dois serviços**, testar.

### Não inclui

- Banco de dados (BE-02), autenticação (BE-08), pipeline de CI (BE-24).
- O contrato `.proto` em si e a implementação gRPC — apenas a **pasta** `contracts/` e o esqueleto dos projetos. Conteúdo em BE-25 a BE-28.
- Qualquer regra de negócio.

## Notas técnicas

- `<LangVersion>` **NÃO DEVE** ser fixado — usa-se o default do SDK (C# 14).
- `Domain` não pode referenciar `Microsoft.*` além do BCL, nem qualquer projeto além de `TodoList.SharedKernel`. Isso é verificado por teste de arquitetura (ver abaixo).
- Nada de Controllers. Somente Minimal APIs com `MapGroup` — nos **dois** serviços.
- **Por que dois serviços e não um.** A separação Identity/Tasks é o que torna a comunicação interna via gRPC uma comunicação real entre microsserviços, e não uma chamada de método dentro do mesmo processo. É a decisão estrutural que sustenta BE-25 a BE-31.
- **Por que `SharedKernel` e não duplicação.** `Result<T>`/`Error` (BE-03) é contrato de forma, não de negócio: duplicá-lo faria os dois serviços divergirem em como reportam erro. Ele **NÃO DEVE** crescer para além disso — nada de entidade, DTO de negócio ou regra compartilhada entre Identity e Tasks (decisão **D-26**).
- Nenhum segredo em `appsettings.json`. Valores sensíveis vêm de variável de ambiente / user-secrets em dev.

## Critérios de aceite

- [ ] **CA-01** — `dotnet build` na raiz conclui **sem warnings**, compilando os dois serviços.
- [ ] **CA-02** — `dotnet format --verify-no-changes` passa sem alterações pendentes.
- [ ] **CA-03** — `dotnet run --project src/Identity/TodoList.Identity.Api` e `dotnet run --project src/Tasks/TodoList.Tasks.Api` sobem **as duas** aplicações simultaneamente, sem conflito de porta, e `GET /health` responde **200** em cada uma.
- [ ] **CA-04** — Em ambiente `Development`, a UI de OpenAPI está acessível nos dois serviços e lista o respectivo endpoint de health.
- [ ] **CA-05** — Em **cada** serviço o grafo de dependências é exatamente `Api → Infrastructure → Application → Domain → SharedKernel`; nenhuma seta aponta no sentido inverso.
- [ ] **CA-06** — `TodoList.Identity.Domain.csproj` e `TodoList.Tasks.Domain.csproj` não declaram nenhum `PackageReference` e declaram **um único** `ProjectReference`: `TodoList.SharedKernel`.
- [ ] **CA-06b** — `TodoList.SharedKernel.csproj` não declara nenhum `PackageReference` nem `ProjectReference`.
- [ ] **CA-06c** — **Nenhum** projeto de `src/Identity` referencia projeto de `src/Tasks`, e vice-versa (teste de arquitetura). Esta é a garantia de que a comunicação entre os dois só pode acontecer pela rede.
- [ ] **CA-07** — Todos os `.csproj` resolvem `net10.0` e `Nullable=enable` a partir do `Directory.Build.props` (nenhum projeto redefine localmente).
- [ ] **CA-08** — Subir qualquer um dos serviços com uma seção de configuração obrigatória ausente **falha na inicialização** com mensagem clara, não em runtime na primeira requisição.
- [ ] **CA-09** — `dotnet test` executa e passa nos quatro projetos de teste (ainda que com poucos testes).
- [ ] **CA-10** — O `README.md` da raiz permite a uma pessoa nova subir **os dois serviços** e rodar os testes seguindo apenas o que está escrito.
- [ ] **CA-11** — Existe a pasta `contracts/identity/v1/` versionada, ainda que o `.proto` só ganhe conteúdo em BE-25.

## Testes obrigatórios

- Teste de arquitetura (NetArchTest ou equivalente) validando CA-05, CA-06, CA-06b e **CA-06c**.
- Teste de integração com `WebApplicationFactory` cobrindo `GET /health` → 200, **um por serviço**.
