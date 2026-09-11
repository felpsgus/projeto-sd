# BE-38 — Containerização: Dockerfile por serviço e docker compose local

| | |
|---|---|
| **Domínio** | Infraestrutura |
| **Serviço** | todos (Identity, Tasks, Gateway) |
| **Depende de** | [BE-36](BE-36-api-gateway.md) |
| **Bloqueia** | T3 (preparo — sem task numerada nesta etapa) |
| **Regras cobertas** | nenhuma de negócio — empacota o que já existe, não introduz comportamento novo |
| **Estimativa** | M |

## Objetivo

Preparar o T3 já no T2: cada serviço tem um `Dockerfile`, e a stack inteira sobe com `docker compose` na máquina de desenvolvimento — fora do caminho crítico da demonstração do T2, que continua sendo a VM (BE-37).

## Escopo

### Inclui

- **`Dockerfile` em cada `src/*/TodoList.*.Api/`** (`src/Identity/TodoList.Identity.Api/Dockerfile`, `src/Tasks/TodoList.Tasks.Api/Dockerfile`, `src/Gateway/TodoList.Gateway.Api/Dockerfile`), cada um:
  - multi-stage: `mcr.microsoft.com/dotnet/sdk:10.0` para build/publish, `mcr.microsoft.com/dotnet/aspnet:10.0` como imagem final;
  - **restore em camada separada** — copia primeiro só os `.csproj`/`.props`/`.sln` necessários (incluindo `Directory.Build.props` e `global.json` da raiz e os `.csproj` dos projetos referenciados na cadeia de dependência do serviço), roda `dotnet restore`, e só depois copia o restante do código-fonte e roda `dotnet publish`. O objetivo é que uma alteração de código sem alteração de dependência reaproveite a camada de restore do cache do Docker;
  - **`USER app`** na imagem final (usuário não-root já presente na imagem base `aspnet:10.0`) — nenhum processo roda como root;
  - **porta 8080 por `Kestrel__Endpoints__*__Url`**, não por `EXPOSE` sozinho ou por `ASPNETCORE_URLS` implícito:
    - Gateway: `Kestrel__Endpoints__Http__Url=http://0.0.0.0:8080` (Http1, é a borda REST pública);
    - Identity e Tasks: o endpoint **`Grpc`** (Http2) escuta em `0.0.0.0:8080` — é essa porta que o Cloud Run injeta como `$PORT` no T3 — e o endpoint de health HTTP/1 (`/health`) fica numa porta interna diferente (ex.: `8081`), só alcançável de dentro da rede do compose, nunca publicada no host;
  - `ENV ASPNETCORE_ENVIRONMENT=Production` fixado na imagem.
- **`.dockerignore` na raiz** do repositório (um só, usado pelos três builds via `-f`): `bin/`, `obj/`, `artifacts/`, `.git/`, `tests/`, `.idea/`, `*.env`, e qualquer outro artefato de build/IDE já ignorado pelo `.gitignore` que não precise entrar no contexto.
- **`docker-compose.yml`** (o mesmo arquivo da raiz, editado, não substituído):
  - o serviço `postgres` **fica exatamente como está** — `docker compose up postgres` continua sendo o fluxo de desenvolvimento do dia a dia, sem exigir o perfil novo;
  - novo **perfil `full`** (`profiles: ["full"]` nos serviços novos) acrescenta:
    - `identity`, `tasks`, `gateway` — cada um construído com `build.context: .` (raiz) e `build.dockerfile` apontando para o `Dockerfile` do respectivo `src/*/TodoList.*.Api/`;
    - `migrate` — um serviço de vida curta usando a imagem `postgres:17-alpine` (a mesma já usada pelo serviço `postgres`), montando `artifacts/sql` e rodando `psql` contra os dois scripts **na ordem** `01-identity.sql` → `02-tasks.sql` (mesma ordem e mesma razão de BE-37/T1: a FK cruzada exige `identity.users` primeiro);
    - `migrate` **pressupõe** que `artifacts/sql/` já existe — gerado por `scripts/publish.ps1` (o compose não gera SQL). Sem os arquivos, `migrate` **DEVE** falhar com mensagem clara, não "passar" sem aplicar nada;
    - dependências: `identity`/`tasks` dependem de `postgres` saudável e de `migrate` ter terminado com sucesso (`depends_on` com `condition: service_completed_successfully` para `migrate`, `condition: service_healthy` para `postgres`); `gateway` depende de `identity` e `tasks` com **`condition: service_started`**;
    - **sem `healthcheck` HTTP nos containers .NET**: a imagem `aspnet:10.0` não traz `curl` nem `wget`, e instalar um só para isso aumenta a imagem e a superfície. Não faz falta: se um backend ainda estiver subindo, o Gateway responde 503 com `Retry-After` (fail-closed, **D-28**) e se recupera sozinho. No Cloud Run (T3), os probes usam o gRPC Health Checking Protocol, sem ferramenta no container (**D-37**);
  - **só o `gateway` publica porta no host** (`8080:8080`) — `identity` e `tasks` não têm `ports:` nenhuma, apenas participam da rede interna do compose, espelhando **D-32** também no ambiente de desenvolvimento em container;
  - segredos (connection strings, `Jwt__SigningKey`, `UserStore__DemoUserPassword`) vêm de um arquivo **`.env`** não versionado, referenciado por `env_file:` nos serviços do perfil `full`; um **`.env.example`** versionado ao lado do `docker-compose.yml` documenta as chaves esperadas, sem valores reais.
- **Tamanho de imagem medido e registrado**: rodar `docker build` das três imagens e anotar o tamanho final (`docker image ls`) na seção correspondente do `README.md` ou no PR desta task — não é um critério de tamanho máximo, é registro para comparação futura (ex.: se alguém tentar `chiseled` depois).

### Não inclui

- Publicar qualquer imagem em registry (Artifact Registry é T3).
- Rodar os containers na VM do T1/T2 — a VM continua no caminho por systemd (BE-37); os Dockerfiles daqui são validados **só** com `docker compose` na máquina de desenvolvimento.
- Cloud Run, `gcloud builds submit`, autenticação entre serviços via Google ID token — tudo isso é do preparo do T3, listado abaixo sem virar task nesta etapa.
- Resiliência de cliente gRPC (retry, circuit breaker) — fica registrada como preparo do T3, não implementada aqui.
- Qualquer mudança de comportamento nos três serviços — só empacotamento.

## Notas técnicas

- **Por que `aspnet:10.0` e não self-contained ou uma imagem chiseled.** Simplicidade: a imagem `aspnet` já resolve o runtime, é a mais documentada e a que menos surpresas traz para quem for reproduzir o build depois. Uma imagem `chiseled` (menor, sem shell) é uma otimização real, mas troca superfície de depuração por alguns MB — fica registrada como possível melhoria futura, não como parte do escopo desta task.
- **Por que o contexto de build é a raiz do repositório, e não a pasta do próprio serviço.** `contracts/*.proto` (compartilhado entre os três projetos, D-29), `Directory.Build.props` e `global.json` (raiz) precisam estar dentro do contexto para o `dotnet restore`/`dotnet publish` os enxergar — um contexto restrito a `src/Identity/TodoList.Identity.Api/` não alcança `../../../contracts`. Isso implica que o comando de build **não** é `docker build .` de dentro da pasta do serviço; é, a partir da raiz:

  ```bash
  docker build -f src/Identity/TodoList.Identity.Api/Dockerfile -t todolist-identity .
  docker build -f src/Tasks/TodoList.Tasks.Api/Dockerfile -t todolist-tasks .
  docker build -f src/Gateway/TodoList.Gateway.Api/Dockerfile -t todolist-gateway .
  ```

  Isso é documentado explicitamente no `README.md`/nos comentários do topo de cada `Dockerfile`, porque é o detalhe mais fácil de errar ao reproduzir o build fora do `docker compose` (que já resolve `context`/`dockerfile` sozinho).
- **Como isso vira o T3.** As imagens construídas aqui são as mesmas que, no T3, sobem para o Artifact Registry — não pela máquina do aluno (sem `gcloud` local), mas pelo **Cloud Shell**: um `git clone` do repositório dentro do Cloud Shell, seguido de `gcloud builds submit --config cloudbuild.yaml .` na raiz. **Não** `--tag`: esse atalho exige o `Dockerfile` na raiz do contexto, e aqui ele mora em `src/*/` — o `cloudbuild.yaml` (T3) declara um passo `docker build -f <caminho>/Dockerfile` por serviço, com a raiz como contexto, exatamente como os comandos acima. No Cloud Run, a variável `$PORT` é sempre `8080` — o mesmo valor já fixado nos `Kestrel__Endpoints__*__Url` desta task — e os dois backends gRPC (Identity, Tasks) precisam da flag `--use-http2` na implantação, para o Cloud Run negociar HTTP/2 sem TLS de borda perceptível ao container (o TLS externo é terminado pelo próprio Cloud Run).

### Preparo do T3 (fora do escopo desta task)

Registrado aqui como mapa do que vem a seguir, sem numerar nem criar arquivo de task — cada item vira uma task própria quando o T3 começar:

- **Cloud SQL para PostgreSQL** substituindo a `maquina-2-psd` (que precisa ser desligada ao final do T3): conexão por socket Unix `/cloudsql/<connection-name>` a partir do Cloud Run, e as migrations (os mesmos `sql/01-identity.sql`/`sql/02-tasks.sql` já gerados por `publish.ps1`) aplicadas pelo **Cloud SQL Studio** em vez de `psql` direto.
- **Deploy no Cloud Run**: o Gateway implantado como serviço público (`--allow-unauthenticated`); Identity e Tasks implantados com **`--no-allow-unauthenticated --use-http2`**, e a chamada interna do Gateway para eles autenticada com um **Google ID token** (obtido via a identidade de serviço do próprio Cloud Run), substituindo a confiança implícita de rede que D-32 assume na VM.
- **Resiliência nos clientes gRPC**: retry em `UNAVAILABLE` e circuit breaker via `Microsoft.Extensions.Http.Resilience`, para sustentar a demonstração de tolerância a falhas exigida pelo T3 (simular queda de um backend e mostrar o Gateway se recuperando ou degradando de forma controlada, não travando).
- **Relatório técnico e desligamento das VMs** (`maquina-1-psd` e `maquina-2-psd`), como exigido pelas entregas do T3.

## Critérios de aceite

- [ ] **CA-01** — As três imagens (`identity`, `tasks`, `gateway`) constroem com sucesso a partir da **raiz** do repositório, com o comando `docker build -f <caminho>/Dockerfile .` documentado.
- [ ] **CA-02** — Nenhuma das três imagens roda como root — `docker inspect --format '{{.Config.User}}'` mostra `app` (ou equivalente não-root) nas três.
- [ ] **CA-03** — Com `artifacts/sql/` gerado por `scripts/publish.ps1`, `docker compose --profile full up --build` sobe a stack inteira (postgres, migrate, identity, tasks, gateway) e o roteiro 401/400/201 de verificação (BE-39) passa executado contra `http://localhost:8080`.
- [ ] **CA-04** — Só a porta 8080 (do `gateway`) fica exposta no host depois do `up --profile full` — `docker compose ps` confirma que `identity` e `tasks` não têm mapeamento de porta publicada.
- [ ] **CA-05** — Nenhum segredo (senha de banco, `Jwt__SigningKey`, senha de demonstração) aparece embutido em nenhuma das três imagens — verificado por `docker history` e `docker inspect` das imagens finais, e por leitura dos três `Dockerfile` (segredos só entram via `env_file`/variável de ambiente do compose, nunca em `ENV` fixo ou `ARG` sem `--secret`).
- [ ] **CA-06** — `docker compose up postgres` continua subindo e funcionando exatamente como antes desta task, sem exigir o perfil `full` nem qualquer variável nova.
- [ ] **CA-07** — O tamanho final de cada uma das três imagens está registrado (README ou PR desta task).

## Testes obrigatórios

- Verificação manual (build e execução real de containers não são automatizáveis pela suíte de testes .NET): CA-01 a CA-04, CA-06, CA-07, executados na máquina de desenvolvimento e registrados no PR (saída dos comandos, tamanhos de imagem).
- CA-05 é verificável por inspeção (`docker history`/`docker inspect`) e por leitura direta dos três `Dockerfile` — documentar a checagem no PR.
- Regressão: a suíte de testes .NET (`dotnet test`) não muda de comportamento por causa desta task — nenhum código de aplicação é alterado, só arquivos de empacotamento.

## Decisões em aberto

- **D-32** — Gateway como única origem pública; replicado aqui como "só o gateway publica porta" no compose. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
- **D-37** — Cada backend expõe um endpoint HTTP/2 gRPC na porta que o Cloud Run injeta como `$PORT`, mais o gRPC Health Checking Protocol — referenciado aqui como a razão de a porta 8080 dos backends ser o endpoint `Grpc`, não o REST. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
