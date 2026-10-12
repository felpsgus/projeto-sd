# CLAUDE.md

Instruções para o Claude Code neste repositório.

## Delegação: mudanças extensas vão para subagentes Sonnet

**Toda modificação de código mais extensa DEVE ser delegada a subagentes Sonnet.** O Claude
principal atua como *tech lead*: entende o problema, escreve o briefing, delega, revisa o
resultado e verifica (build + testes). Não escreve o código ele mesmo.

- **Extensa** = mais de um arquivo, uma task `BE-*`/`FE-*` inteira, uma etapa/onda de plano,
  uma feature nova ou um refactor que atravessa camadas.
- **Direto** = correção de uma linha, ajuste de texto, leitura, diagnóstico, resposta a pergunta.
- Tarefas independentes entre si vão para subagentes em paralelo; tarefas dependentes vão em
  sequência, com revisão entre elas.
- **Toda revisão usa a skill `ponytail:ponytail-review`** — do resultado de um subagente, de um
  diff, de uma branch ou de um PR. Ela caça complexidade desnecessária e roda **além** da
  verificação de correção (build + testes), não no lugar dela.
- Cada briefing de subagente aponta [CONVENCOES-CODIGO.md](CONVENCOES-CODIGO.md) e o arquivo da
  task em [tarefas/](tarefas/) — o subagente começa sem contexto nenhum.

## O que é o projeto

TodoList distribuído: **três serviços .NET 10** + **frontend Angular 22**, atrás de nginx na
mesma origem.

```
frontend (Angular + nginx)  →  Gateway (REST, única borda pública)
                                  ├─ gRPC →  Identity  (autoridade de usuário/token)
                                  └─ gRPC →  Tasks     (CRUD de tarefas)
                                               Postgres: schema identity + schema tasks
```

Por serviço, as dependências apontam sempre para dentro:
`Api → Infrastructure → Application → Domain → SharedKernel`. Identity e Tasks **não têm
referência de projeto entre si** — só `contracts/*/v1/*.proto` e o `SharedKernel`.

## Comandos

```powershell
dotnet restore; dotnet build          # deve passar SEM warnings (analyzers = erro)
dotnet test                           # todos os projetos de teste
dotnet test --filter "Category!=Docker"   # sem Docker (Testcontainers fora)
dotnet format --verify-no-changes      # estilo, sem aplicar

cd frontend; npm ci; npm start         # dev server
npm test; npm run lint                 # Vitest + ESLint

docker compose up -d                   # stack completa; frontend em http://localhost
```

SDK fixado em [global.json](global.json) (10.0.202). Props comuns em
[Directory.Build.props](Directory.Build.props) — não repetir `TargetFramework`/`Nullable` por csproj.

## Regras do repositório

- **Convenções de código**: [CONVENCOES-CODIGO.md](CONVENCOES-CODIGO.md) é normativo. Minimal APIs
  (não Controllers), `IOptions<T>` validado na inicialização, `Result<T>` para erro de negócio,
  Angular signals-first/zoneless/OnPush.
- **Regras de negócio**: [REGRAS-DE-NEGOCIO.md](REGRAS-DE-NEGOCIO.md) (`RN-*`). Decisões fechadas
  (`D-*`/`FD-*`) em [tarefas/README.md](tarefas/README.md) e nos `DECISOES-PENDENTES.md`.
- **Tasks**: uma por arquivo em [tarefas/backend/](tarefas/backend/) e
  [tarefas/frontend/](tarefas/frontend/), com critérios de aceite (`CA-*`). Implementar a task que
  foi pedida — nunca adiantar a próxima sem pedido explícito.
- **Endereço, porta e protocolo são configuração**, nunca literal em código. Há teste de
  arquitetura que falha se aparecer literal fora de `appsettings*.json`. Sobrescrita por variável
  de ambiente: `:` vira `__` (`Identity__GrpcAddress`).
- **Nenhum segredo versionado**. Chaves e connection strings vêm de variável de ambiente ou
  user-secrets.
- **Bug corrigido ganha teste de regressão** — falha antes, passa depois.
- Commits em **Conventional Commits**; branches curtas `feat/...`, `fix/...`.

## Armadilhas

- **Migrations: Identity sempre antes do Tasks** — a FK cruzada depende das tabelas de `identity`.
- **Fail-closed (D-28)**: Identity inalcançável → `503`, a tarefa não é criada. Não "degradar".
- **`UserStore:Provider=Persisted` é o padrão** (D-39); `InMemory` só em teste.
- **Gateway é a única borda pública** (D-32): Identity e Tasks não expõem `ports:` no compose.
- JWT **RS256** (D-38): chave privada só no Identity, pública só no Gateway. O Gateway valida
  local, não chama `ValidateToken`.
- Kestrel configurado só em `Development` em alguns pontos — ao rodar em outro ambiente, passar
  endereço por variável de ambiente.

O [README.md](README.md) tem o roteiro de verificação de cada etapa; [deploy/](deploy/) tem os
runbooks de GCP e os scripts de demo.

## Agent skills

### Issue tracker

Issues novas vivem no GitHub Issues de `felpsgus/projeto-sd` (via `gh`); as tasks `BE-*`/`FE-*`
existentes continuam em [tarefas/](tarefas/). See `docs/agents/issue-tracker.md`.

### Triage labels

Os cinco labels canônicos, sem renomear: `needs-triage`, `needs-info`, `ready-for-agent`,
`ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: um `CONTEXT.md` na raiz (ainda não existe, criado sob demanda) e ADRs em
[docs/adr/](docs/adr/). See `docs/agents/domain.md`.
