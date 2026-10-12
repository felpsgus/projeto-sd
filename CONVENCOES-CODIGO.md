# Convenções de Código

> Regras que a base de código deve seguir. Stack: **.NET 10 (backend)** + **Angular 22 (frontend)**.
> Nível de rigor: **moderado (padrão de time)** — o objetivo é consistência e qualidade sustentável, não perfeccionismo.

Este documento é normativo. Palavras como **DEVE**, **NÃO DEVE** e **PODE** seguem o sentido de obrigatoriedade, proibição e opcionalidade. Toda exceção precisa ser justificada no PR.

---

## 1. Versões de ferramentas

As versões abaixo são o piso da base de código. Atualizações de patch são bem-vindas; mudanças de major exigem discussão em equipe.

### Backend

| Ferramenta | Versão | Observação |
|---|---|---|
| .NET SDK / Runtime | **10.x (LTS)** | Suporte até nov/2028. É o alvo de todos os projetos. |
| Linguagem | **C# 14** | Habilitado por padrão no SDK do .NET 10. |
| ASP.NET Core | **10.x** | Acompanha o runtime. |
| Entity Framework Core | **10.x** | Alinhado ao runtime. |
| IDE | **Visual Studio 2026**, **Rider** ou **VS Code + C# Dev Kit** | Livre escolha, desde que respeite `.editorconfig`. |

- O `TargetFramework` de todos os projetos **DEVE** ser `net10.0`.
- A versão da linguagem **NÃO DEVE** ser fixada manualmente (`<LangVersion>`) — usa-se o default do SDK.
- A versão do SDK **DEVE** ser fixada via `global.json` na raiz do repositório para garantir builds reprodutíveis.

### Frontend

| Ferramenta | Versão | Observação |
|---|---|---|
| Angular | **22.x (estável)** | Suporte ativo até ~jun/2028. |
| Angular CLI | **22.x** | Precisa casar com o major do `@angular/core`. |
| TypeScript | **~5.9** | Faixa suportada pelo Angular 22. |
| Node.js | **22 LTS** ou **24 LTS** | Não usar versões ímpares/Current em CI. |
| Gerenciador de pacotes | **npm** | `package-lock.json` versionado; nada de pnpm/yarn no repositório. |

- A versão do Node **DEVE** ser fixada via `.nvmrc` (ou `volta`/`engines` no `package.json`).
- O `package-lock.json` **DEVE** ser commitado e o CI **DEVE** instalar com `npm ci` (nunca `npm install` no pipeline).
- Dependências **NÃO DEVEM** usar faixa aberta (`^`/`~`) apenas no papel: o `package-lock.json` é a fonte da verdade.

---

## 2. Paradigmas e arquitetura

### 2.1 Backend (.NET)

**Arquitetura em camadas / Clean Architecture (versão pragmática).** Quatro projetos, com dependências apontando sempre para dentro:

```
Domain          → entidades, value objects, regras de negócio puras (sem dependências externas)
Application     → casos de uso, DTOs, interfaces, validações
Infrastructure  → EF Core, acesso a dados, serviços externos, implementações
Api             → controllers/endpoints, DI, configuração, middleware
```

Regras:

- **SOLID** como guia; na prática, priorize responsabilidade única e inversão de dependência.
- Toda dependência **DEVE** ser resolvida por **injeção de dependência**. Nada de `new` para serviços ou repositórios.
- I/O **DEVE** ser `async`/`await` de ponta a ponta. Nunca `.Result` ou `.Wait()` (risco de deadlock).
- **Nullable reference types** habilitado (`<Nullable>enable</Nullable>`) em todos os projetos.
- A camada de API **NÃO DEVE** expor entidades de domínio — sempre via **DTOs** de request/response.
- Validação de entrada com **FluentValidation** (ou Data Annotations em casos simples).
- Erros esperados de negócio **DEVEM** usar um padrão de resultado (ex.: `Result<T>`); exceções ficam para falhas realmente excepcionais.
- **Logging estruturado** obrigatório (ex.: Serilog). Nada de `Console.WriteLine`. Nunca logar dados sensíveis (senhas, tokens, PII).
- Configuração via `IOptions<T>` tipado, nunca lendo `IConfiguration` espalhado pelo código.
- Endpoints **DEVEM** usar **Minimal APIs**, organizados por feature com `MapGroup` e classes de endpoint (padrão de extensão `IEndpointRouteBuilder`). Não misturar com Controllers.
- Analyzers do Roslyn habilitados; warnings de análise tratados como algo a resolver, não ignorar.

### 2.2 Frontend (Angular)

Angular 22 é **signals-first e zoneless** — as convenções refletem isso.

- **Standalone components** por padrão (sem NgModules para features novas).
- **Signals** para estado reativo; `computed()` para estado derivado; `effect()` com parcimônia.
- **`ChangeDetectionStrategy.OnPush`** em todo componente (alinhado à arquitetura zoneless).
- Evitar `subscribe()` manual. Preferir `async` pipe, `httpResource`/`rxResource` ou APIs baseadas em signal. Se subscrever, gerenciar o ciclo de vida (`takeUntilDestroyed`).
- Formulários: usar **Signal Forms** (estável no v22) para telas novas; Reactive Forms tipados são aceitáveis no legado.
- Separação **container/presentational** (smart/dumb): componentes de apresentação recebem dados via `input()` e emitem via `output()`, sem acessar serviços diretamente.
- **Lazy loading** de rotas por feature.
- Estado compartilhado: serviços com signals para casos simples; **NgRx SignalStore** apenas quando a complexidade justificar. Não introduzir gerenciador de estado global "por precaução".
- `HttpClient` com respostas tipadas e **interceptors** para auth, erros e logging transversais.
- **TypeScript em modo estrito** (`strict: true`); `any` é proibido salvo justificativa no código.

### 2.3 Transversal

- **DRY com bom senso**: duplicação pequena e ocasional é melhor que abstração errada e precoce.
- Funções/métodos pequenos e com nome descritivo. Evitar métodos com mais de ~30–40 linhas.
- **Sem números mágicos** e sem strings soltas de configuração — use constantes/enums.
- Código morto **DEVE** ser removido, não comentado.

---

## 3. Estilo e formatação

A formatação **não é assunto de PR** — é automatizada.

- **`.editorconfig`** único na raiz, respeitado por todas as IDEs.
- Backend: `dotnet format` valida estilo; StyleCop/Analyzers configurados no `.editorconfig`.
- Frontend: **ESLint** (regras de `@angular-eslint`) + **Prettier**. Prettier decide formatação, ESLint decide qualidade.
- Convenções de nomenclatura:
  - C#: `PascalCase` para tipos/métodos/propriedades, `camelCase` para locais/parâmetros, `_camelCase` para campos privados, `IPascalCase` para interfaces.
  - TS/Angular: `PascalCase` para classes/componentes, `camelCase` para variáveis/métodos, `kebab-case` para seletores e nomes de arquivo, `SCREAMING_SNAKE_CASE` para constantes globais.
- Idioma do código: **inglês** para identificadores; comentários podem ser em português.
- Comentários explicam **o porquê**, não o **o quê**. Código claro dispensa comentário óbvio.

---

## 4. Testes

Testes fazem parte da definição de "pronto". Um PR com código novo sem testes correspondentes **NÃO DEVE** ser aprovado (salvo exceção justificada — ex.: spike, hotfix documentado).

### 4.1 Pirâmide de testes

Priorize a base da pirâmide: **muitos testes de unidade, alguns de integração, poucos E2E**.

### 4.2 Backend

| Tipo | Ferramentas | O que cobre |
|---|---|---|
| Unidade | **xUnit** + **FluentAssertions** + **NSubstitute** (ou Moq) | Regras de domínio e casos de uso, isolados. |
| Integração | **WebApplicationFactory** + **Testcontainers** | API + banco real (Postgres/SQL Server em container). |

- Padrão **AAA** (Arrange–Act–Assert) explícito.
- Nome do teste descreve cenário e expectativa: `Metodo_Cenario_ResultadoEsperado`.
- Testes **DEVEM** ser determinísticos e independentes (sem ordem, sem estado compartilhado, sem `Thread.Sleep`).
- Lógica de negócio (Domain/Application) **DEVE** ter cobertura de unidade. Infra pura (mapeamentos triviais) não precisa de teste dedicado.

### 4.3 Frontend

| Tipo | Ferramentas | O que cobre |
|---|---|---|
| Unidade / componente | **Vitest** (padrão no Angular 22) + **Angular Testing Library** | Componentes, signals, serviços, pipes. |
| E2E | **Playwright** | Fluxos críticos do usuário. |

- Testar **comportamento**, não detalhe de implementação. Consultar o DOM pelo que o usuário vê (texto, papel/role), não por seletores frágeis.
- Serviços e lógica de signals **DEVEM** ser testados isoladamente.
- E2E cobre apenas os caminhos críticos (login, checkout, fluxos que quebram o negócio) — não cada tela.

### 4.4 Regras gerais

- Todo **bug corrigido** ganha um teste que falha antes e passa depois (teste de regressão).
- Testes rodam no CI a cada PR e **DEVEM** estar verdes para merge.
- Testes lentos ou instáveis (*flaky*) são tratados como bug: corrigir ou isolar, nunca "re-rodar até passar".

---

## 5. Cobertura de testes

A cobertura é uma **ferramenta de diagnóstico, não uma meta em si**. Cobertura alta com testes ruins não vale nada; use-a para achar buracos, não para inflar número.

### Metas (rigor moderado)

| Escopo | Meta de cobertura (linha) |
|---|---|
| **Global (backend + frontend)** | **≥ 75%** |
| **Domínio / regras de negócio (backend)** | **≥ 85%** |
| **Serviços e lógica de estado (frontend)** | **≥ 80%** |
| Código gerado, DTOs, `Program.cs`, mappings triviais | **excluído da métrica** |

Regras:

- **NÃO** perseguir 100%. Acima de ~85–90% o custo cresce e o valor cai.
- O gate de cobertura no CI **DEVE** falhar o build se cair **abaixo do piso global**, e **NÃO DEVE** permitir queda em relação ao baseline sem justificativa (evita erosão silenciosa).
- Ferramentas:
  - Backend: **Coverlet** para coletar + **ReportGenerator** para o relatório.
  - Frontend: cobertura nativa do **Vitest** (Istanbul/V8).
- O relatório de cobertura **DEVE** ser publicado como artefato do CI e visível no PR.
- Arquivos de configuração, bootstrap e código sem lógica **DEVEM** ser explicitamente excluídos para não distorcer a métrica.

---

## 6. Qualidade e fluxo de trabalho

- **Branches**: trunk-based com branches curtas de feature (`feat/...`, `fix/...`). Nada de branches longevas.
- **Commits**: seguir **Conventional Commits** (`feat:`, `fix:`, `refactor:`, `test:`, `chore:`...).
- **Pull Requests**:
  - Pequenos e focados (idealmente < 400 linhas de mudança).
  - Pelo menos **1 aprovação** de outro dev.
  - CI verde obrigatório: build + lint + testes + gate de cobertura.
- **Segurança**:
  - **Nenhum segredo** no repositório (chaves, senhas, connection strings). Usar variáveis de ambiente / secret manager.
  - Varredura de dependências (`dotnet list package --vulnerable`, `npm audit`) no CI.
- **Documentação**:
  - `README` mantido atualizado (como rodar, testar, subir).
  - Decisões arquiteturais relevantes registradas como **ADR** (Architecture Decision Record).
  - APIs públicas com documentação (XML docs no backend, OpenAPI/Swagger exposto).

---

## 7. Definição de pronto (checklist de PR)

Uma tarefa só está pronta quando:

- [ ] Código segue os paradigmas das seções 2 e 3.
- [ ] Lint e formatação passam automaticamente.
- [ ] Há testes de unidade (e integração/E2E quando aplicável).
- [ ] Cobertura respeita os pisos da seção 5.
- [ ] CI totalmente verde.
- [ ] Sem segredos, sem código morto, sem `TODO` órfão.
- [ ] Documentação/ADR atualizados quando a mudança justifica.
- [ ] Revisado e aprovado por ao menos um par.

---

*As versões de ferramentas citadas refletem os releases LTS/estáveis vigentes em agosto de 2026 (.NET 10, Angular 22). Revise este documento a cada ciclo de major das ferramentas.*
