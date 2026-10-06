# BE-24 — Observabilidade, CI, gate de cobertura e segurança

| | |
|---|---|
| **Domínio** | Infraestrutura / Qualidade |
| **Serviço** | ambos |
| **Depende de** | [BE-01](BE-01-fundacao-solution.md) (o esqueleto entra cedo; a task fecha ao final) |
| **Bloqueia** | o merge de todas as demais |
| **Regras cobertas** | nenhuma de negócio — implementa as seções 4, 5 e 6 de `CONVENCOES-CODIGO.md` |
| **Estimativa** | M |

## Objetivo

Todo PR passa por um pipeline que compila, verifica estilo, roda os testes, mede cobertura contra os pisos definidos e varre dependências vulneráveis — e a aplicação em execução produz log estruturado utilizável.

> **Como executar esta task:** o pipeline mínimo (build + testes) entra logo após BE-01. Os gates de cobertura e as varreduras são ligados progressivamente. A task só é dada como pronta quando todos os critérios abaixo estiverem ativos e **falhando o build** quando devem falhar.

## Escopo

### Inclui

**Observabilidade**

- **Serilog** com log estruturado em JSON, saída para console (o agregador consome de lá), configurado **nos dois serviços**.
- Enriquecimento: `traceId`/`spanId`, `userId` (quando autenticado), ambiente, versão da aplicação e **`service`** (`identity` | `tasks`) — sem esse campo, dois serviços logando no mesmo console viram um log só, ilegível.
- Middleware de log de requisição: método, rota, status, duração. Corpo **não** é logado.
- **Log da chamada gRPC** entre os serviços ([BE-27](BE-27-tasks-cliente-grpc.md)): no Tasks, uma entrada de saída (`ValidateUser`, `userId`, duração, `StatusCode` do gRPC); no Identity, uma entrada de entrada com o resultado (`exists`, `active`). É o que evidencia a ida e volta na demonstração do T1 ([BE-31](BE-31-verificacao-t1.md)).
- **Redação obrigatória**: `password`, `currentPassword`, `newPassword`, `accessToken`, `refreshToken`, `Authorization`, `passwordHash` — nunca aparecem em log, em nenhum nível.
- Níveis: `Information` para fluxo normal, `Warning` para erro de negócio relevante (bloqueio de login, reuso de refresh token, tentativa de acesso a recurso alheio), `Error` para exceção não tratada.
- Health checks: `/health/live` e `/health/ready` (banco) **em cada serviço**, consumíveis por orquestrador. O `/health/ready` do **Tasks** também reporta o alcance do Identity por gRPC — degradado, não fora do ar.

**CI (GitHub Actions ou equivalente)**

- Job único por PR, cobrindo a solution inteira (os dois serviços): restore → build (`--no-restore`, warnings como erro) → `dotnet format --verify-no-changes` → testes de unidade → testes de integração (Docker disponível para Testcontainers) → cobertura → varredura de dependências.
- A cobertura é agregada da solution, mas o relatório **DEVE** permitir ver Identity e Tasks separadamente — cobertura alta em um serviço não pode mascarar buraco no outro.
- Coleta com **Coverlet**, relatório com **ReportGenerator**, publicado como **artefato** e visível no PR.
- **Gate de cobertura**:
  - falha se o total global cair abaixo de **75%**;
  - falha se `Domain` + `Application` caírem abaixo de **85%**;
  - falha se houver **queda em relação ao baseline** da branch principal sem justificativa registrada.
- Exclusões da métrica, declaradas explicitamente: `Program.cs` (de **cada** serviço), migrations, DTOs sem lógica, mapeamentos triviais e **código gerado pelo `Grpc.Tools` a partir do `.proto`** — que é volumoso e inflaria a métrica sem significar nada.
- `dotnet list package --vulnerable --include-transitive` falhando o build em severidade alta/crítica.
- Varredura de segredos no diff (gitleaks ou equivalente).

**Documentação**

- `docs/adr/` com os ADRs produzidos pelas tasks: **separação em dois serviços e comunicação por gRPC (BE-01/BE-25)**, **banco único com schema por serviço (BE-02, D-27)**, **fail-closed na indisponibilidade do Identity (BE-28, D-28)**, **JWT HS256 com a chave restrita ao Identity e validação externa por `ValidateToken` (BE-08, D-31)**, access token não revogável (BE-11), lockout contando e-mails inexistentes (BE-12), semântica de `PUT` (BE-19), soft delete e retenção (BE-21/BE-23).
- OpenAPI completo: todos os endpoints com descrição, exemplos e códigos de resposta documentados.
- `README.md` da raiz atualizado ao fim de cada task.

### Não inclui

- Deploy, infraestrutura de nuvem, ambientes.
- APM/tracing distribuído completo (OpenTelemetry). Agora **há** mais de um serviço, então isso deixou de ser hipotético — mas continua fora do escopo desta versão. O mínimo exigido aqui é a propagação do `traceId` do Tasks para o Identity pela metadata gRPC, para que os dois lados de uma mesma requisição sejam correlacionáveis no log.

## Critérios de aceite

### Log

- [x] **CA-01** — Toda saída de log é JSON estruturado com campos consultáveis, não texto livre.
- [x] **CA-02** — Toda entrada de log de requisição tem `traceId`, e ele **coincide** com o `traceId` retornado no `ProblemDetails` de erro.
- [x] **CA-03** — Em requisição autenticada, o log inclui o `userId`; em anônima, não inclui campo vazio ou lixo.
- [x] **CA-04** — Um teste automatizado exercita cadastro, login, refresh, troca de senha e logout capturando **todo** o log produzido, e afirma que **nenhuma** senha, hash ou valor de token aparece na saída — em nenhum nível, inclusive `Debug`. **Este teste é o entregável central da task.**
- [x] **CA-05** — Nenhum `Console.WriteLine` existe na base de código (verificado por analyzer ou grep no CI).
- [x] **CA-06** — Uma exceção não tratada gera log de nível `Error` com stack trace **no log** — e resposta 500 genérica **sem** stack trace para o cliente.
- [x] **CA-07** — `/health/live` responde sem tocar no banco; `/health/ready` reporta degradação quando o banco está indisponível — nos dois serviços.
- [x] **CA-07b** — Toda entrada de log carrega o campo `service` com `identity` ou `tasks`.
- [x] **CA-07c** — Uma criação de tarefa produz, no mesmo `traceId`, uma entrada no Tasks (chamada gRPC de saída) e uma no Identity (`ValidateUser` recebido) — comprovando a correlação entre os dois serviços.

### CI

- [x] **CA-08** — Um PR com erro de compilação **falha** o pipeline. *(PR descartável de 06/10/2026, #24, run 37540970999: o job `backend` falhou em `dotnet build` com CS0029.)*
- [x] **CA-09** — Um PR com formatação fora do `.editorconfig` **falha** no passo de `dotnet format`. *(PR descartável de 06/10/2026, #18, run 37540904718: o job `backend` falhou no passo `dotnet format --verify-no-changes` com erro WHITESPACE; o build tinha passado.)*
- [x] **CA-10** — Um PR com um teste quebrado **falha** o pipeline. *(PR descartável de 06/10/2026, #19, run 37540911839: asserção invertida num teste de unidade derrubou o passo de testes.)*
- [x] **CA-11** — Os testes de integração rodam no CI com Testcontainers, em runner limpo. *(run 37536466701 do PR #17, 06/10/2026: o job `backend` rodou a suíte inteira com Testcontainers no `ubuntu-latest` e passou em 2m54s.)*
- [x] **CA-12** — O relatório de cobertura é publicado como artefato e o resumo aparece no PR. *(run 37536466701 do PR #17, 06/10/2026: artefato `backend-coverage-report` publicado e resumo gravado no *Job summary* do run; não há comentário no PR.)*
- [x] **CA-13** — Um PR que derruba a cobertura global abaixo de **75%** **falha** o build — comprovado com um PR de teste que adiciona código sem teste. *(PR descartável de 06/10/2026, #20, run 37540920139: 900 linhas sem teste no Gateway; `GATE FALHOU: cobertura global 61.5% < 75%`.)*
- [x] **CA-14** — Um PR que derruba `Domain`/`Application` abaixo de **85%** **falha** o build. *(PR descartável de 06/10/2026, #25, run 37543960423: 220 linhas sem teste em `Tasks.Application`; `GATE FALHOU: Domain+Application 78.46% < 85%`, com o global em 89,7%. A primeira tentativa, no PR #21, passou indevidamente: o `coverlet.collector` 6.0.4 não instrumentava no Linux os assemblies que referenciam `Microsoft.Extensions.Logging.Abstractions` e tirava quatro deles do relatório sem avisar. Corrigido com a versão 10.1.0, e `check-coverage.ps1` agora falha se um projeto de `src/` faltar no relatório.)*
- [x] **CA-15** — Uma queda de cobertura em relação ao baseline, mesmo acima do piso, é sinalizada no PR. *(PR descartável de 06/10/2026, #22, run 37540932508: 14 linhas sem teste; o pipeline passou com a anotação `Cobertura do backend caiu: global 92.1% < baseline 94.1%`. Depois da correção do coverlet (ver CA-14) o runner mede os mesmos números da máquina local e o baseline foi refeito: 94,7% global e 97,79% em Domain+Application.)*
- [x] **CA-16** — `Program.cs`, migrations, DTOs e o código gerado a partir do `.proto` estão excluídos da métrica, e a exclusão é visível no relatório (não é um número inflado silenciosamente).
- [x] **CA-16b** — O relatório de cobertura mostra Identity e Tasks como grupos separados, além do total.
- [x] **CA-17** — Uma dependência com vulnerabilidade conhecida de severidade alta **falha** o build — comprovado adicionando temporariamente um pacote vulnerável. *(PR descartável de 06/10/2026, #23, run 37540940187: `Newtonsoft.Json 12.0.3` derrubou o `dotnet restore` com NU1903 (vulnerabilidade alta como erro), antes mesmo do passo `check-vulnerable.ps1`.)*
- [x] **CA-18** — Um segredo commitado no diff **falha** o build — comprovado com um segredo falso. *(PR descartável de 06/10/2026, #24, run 37540970999: uma chave genérica inventada em `prova-segredo.txt` derrubou o job `secrets` (gitleaks, `leaks found: 1`). O gitleaks varre todas as branches do remoto, então a branch de prova também derrubou o `secrets` do PR #23 enquanto existiu.)*
- [x] **CA-19** — O pipeline completo executa em tempo aceitável (alvo: < 10 min), documentado no PR. *(run 37536466701 do PR #17, 06/10/2026: os quatro jobs em paralelo, 5m46s do início ao fim; o mais longo é o `e2e`, 5m41s.)*
- [x] **CA-20** — Nenhum teste é `[Skip]` sem justificativa escrita, e não há teste flaky conhecido em aberto ao fechar a task.

### Documentação

- [x] **CA-21** — Todos os ADRs listados no escopo existem em `docs/adr/`, cada um com contexto, decisão e consequências.
- [x] **CA-22** — A especificação OpenAPI cobre 100% dos endpoints, com todos os códigos de resposta possíveis documentados.
- [x] **CA-22b** — O `.proto` de `contracts/identity/v1/` está documentado: cada RPC e cada campo com comentário explicando o significado (é o contrato entre os dois serviços, equivalente ao OpenAPI da borda).
- [ ] **CA-23** — Uma pessoa nova consegue clonar, subir o banco, subir **os dois serviços** e rodar todos os testes seguindo apenas o `README.md`.

## Testes obrigatórios

- CA-04 é um teste de integração real, não uma revisão manual.
- Os critérios de gate (CA-08 a CA-10, CA-13, CA-14, CA-17, CA-18) são validados **provocando a falha** uma vez, em PR descartável — um gate nunca exercitado é um gate que não funciona.

## Nota de execução — 03/10/2026 (partes Observabilidade e Documentação)

A task foi escrita com dois serviços e JWT HS256; esta entrega segue o estado atual. Desvios e decisões:

- **Três serviços** (`gateway`, `identity`, `tasks`), não dois. O campo `service` e o log JSON valem para os três.
  O Gateway não referencia o `SharedKernel` (D-33), então `StructuredLogging.cs` é **compilado também** no Gateway por
  `Compile Include` com link — um código só, sem `ProjectReference`.
- **JWT RS256** (D-38, ADR-0007), não HS256 + `ValidateToken` (D-31): o ADR da decisão é o 0007.
- **`/health` mantido**; `/health/live` é alias dele (scripts de deploy e docs usam `/health`). O Gateway só tem
  liveness. O check do Identity no `/health/ready` do Tasks é `Degraded` e fica fora do probe gRPC do Cloud Run (D-37).
- **Sem OpenTelemetry**: a correlação usa o `traceparent` W3C que o ASP.NET Core/Grpc.Net.Client já propagam; o
  `traceId` do `ProblemDetails` e o do log vêm do mesmo `Activity` (`HttpContext.GetTraceId()`).
- **`userId` no log**: Gateway pelo `sub` do token; Tasks pela metadata `x-user-id`. O Identity não recebe
  `x-user-id` (o Gateway só o repassa ao Tasks), então suas entradas não têm `userId`.
- **Reuso de refresh token** não é distinguível no gRPC (RN-AUTH-17, a `Application` não tem logger): todo
  `RefreshSession` recusado vira `Warning`. Bloqueio de login também é `Warning`.
- Logs antigos com `traceId={TraceId}` no template (propriedade `TraceId`, que carrega o `traceparent` completo)
  foram mantidos; o campo `traceId` (minúsculo, só o id) vem do enricher e é o que se filtra.
- O ADR de PUT é o 0008; os de serviços/gRPC, schema, fail-closed e JWT são 0004 a 0007.
- **Em aberto:** CA-08 a CA-20 (CI, cobertura, varredura — outra etapa) e **CA-23** (um novo desenvolvedor clonar e
  rodar tudo só pelo README — não foi exercitado de ponta a ponta nesta entrega).

## Nota de execução — 03/10/2026 (CI e cobertura)

workflow escrito e cada passo executado localmente; falta a primeira execução real no GitHub e a prova de falha em PR descartável.

- Arquivos: `.github/workflows/ci.yml`, `coverlet.runsettings`, `.config/dotnet-tools.json` (reportgenerator + dotnet-ef), `scripts/{coverage,check-coverage,check-vulnerable}.ps1`, `.gitleaks.toml`. Pisos e exclusões: README, "CI e gates".
- Marcados: CA-16 (exclusões declaradas em `coverlet.runsettings`), CA-16b (relatório por assembly), CA-20 (nenhum `Skip`; o teste de timing de login virou determinístico — verifica que o Verify roda também para e-mail inexistente, sem medir tempo de parede).
- Em aberto, exigem execução no GitHub: CA-08 a CA-11, CA-13, CA-14, CA-17, CA-18, CA-19 e CA-12 (publicação do artefato e do resumo no PR). Local: gate comprovado falhando com piso 99 e passando com 75/85.
- **Pendente, não implementado:** CA-15 (queda de cobertura em relação ao baseline) — exigiria guardar o resumo da `main` como artefato e comparar no PR.
