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

- [ ] **CA-01** — Toda saída de log é JSON estruturado com campos consultáveis, não texto livre.
- [ ] **CA-02** — Toda entrada de log de requisição tem `traceId`, e ele **coincide** com o `traceId` retornado no `ProblemDetails` de erro.
- [ ] **CA-03** — Em requisição autenticada, o log inclui o `userId`; em anônima, não inclui campo vazio ou lixo.
- [ ] **CA-04** — Um teste automatizado exercita cadastro, login, refresh, troca de senha e logout capturando **todo** o log produzido, e afirma que **nenhuma** senha, hash ou valor de token aparece na saída — em nenhum nível, inclusive `Debug`. **Este teste é o entregável central da task.**
- [ ] **CA-05** — Nenhum `Console.WriteLine` existe na base de código (verificado por analyzer ou grep no CI).
- [ ] **CA-06** — Uma exceção não tratada gera log de nível `Error` com stack trace **no log** — e resposta 500 genérica **sem** stack trace para o cliente.
- [ ] **CA-07** — `/health/live` responde sem tocar no banco; `/health/ready` reporta degradação quando o banco está indisponível — nos dois serviços.
- [ ] **CA-07b** — Toda entrada de log carrega o campo `service` com `identity` ou `tasks`.
- [ ] **CA-07c** — Uma criação de tarefa produz, no mesmo `traceId`, uma entrada no Tasks (chamada gRPC de saída) e uma no Identity (`ValidateUser` recebido) — comprovando a correlação entre os dois serviços.

### CI

- [ ] **CA-08** — Um PR com erro de compilação **falha** o pipeline.
- [ ] **CA-09** — Um PR com formatação fora do `.editorconfig` **falha** no passo de `dotnet format`.
- [ ] **CA-10** — Um PR com um teste quebrado **falha** o pipeline.
- [ ] **CA-11** — Os testes de integração rodam no CI com Testcontainers, em runner limpo.
- [ ] **CA-12** — O relatório de cobertura é publicado como artefato e o resumo aparece no PR.
- [ ] **CA-13** — Um PR que derruba a cobertura global abaixo de **75%** **falha** o build — comprovado com um PR de teste que adiciona código sem teste.
- [ ] **CA-14** — Um PR que derruba `Domain`/`Application` abaixo de **85%** **falha** o build.
- [ ] **CA-15** — Uma queda de cobertura em relação ao baseline, mesmo acima do piso, é sinalizada no PR.
- [ ] **CA-16** — `Program.cs`, migrations, DTOs e o código gerado a partir do `.proto` estão excluídos da métrica, e a exclusão é visível no relatório (não é um número inflado silenciosamente).
- [ ] **CA-16b** — O relatório de cobertura mostra Identity e Tasks como grupos separados, além do total.
- [ ] **CA-17** — Uma dependência com vulnerabilidade conhecida de severidade alta **falha** o build — comprovado adicionando temporariamente um pacote vulnerável.
- [ ] **CA-18** — Um segredo commitado no diff **falha** o build — comprovado com um segredo falso.
- [ ] **CA-19** — O pipeline completo executa em tempo aceitável (alvo: < 10 min), documentado no PR.
- [ ] **CA-20** — Nenhum teste é `[Skip]` sem justificativa escrita, e não há teste flaky conhecido em aberto ao fechar a task.

### Documentação

- [ ] **CA-21** — Todos os ADRs listados no escopo existem em `docs/adr/`, cada um com contexto, decisão e consequências.
- [ ] **CA-22** — A especificação OpenAPI cobre 100% dos endpoints, com todos os códigos de resposta possíveis documentados.
- [ ] **CA-22b** — O `.proto` de `contracts/identity/v1/` está documentado: cada RPC e cada campo com comentário explicando o significado (é o contrato entre os dois serviços, equivalente ao OpenAPI da borda).
- [ ] **CA-23** — Uma pessoa nova consegue clonar, subir o banco, subir **os dois serviços** e rodar todos os testes seguindo apenas o `README.md`.

## Testes obrigatórios

- CA-04 é um teste de integração real, não uma revisão manual.
- Os critérios de gate (CA-08 a CA-10, CA-13, CA-14, CA-17, CA-18) são validados **provocando a falha** uma vez, em PR descartável — um gate nunca exercitado é um gate que não funciona.
