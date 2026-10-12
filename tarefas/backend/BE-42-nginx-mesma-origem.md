# BE-42 — nginx como servidor de mesma origem (SPA + proxy `/api`)

> **Nova task, emenda ao T2 (21/09/2026).** O enunciado novo exige frontend obrigatório falando **só** com o Gateway. A decisão do usuário (21/09/2026) é servir o Angular e o `/api` pela **mesma origem** via **nginx** — preservando FD-16/FD-01 (mesma origem, cookie/`SameSite` sem CORS) mesmo com um processo estático na frente do Gateway. O nginx **não** é um segundo gateway: nenhuma regra de negócio, autenticação ou validação vive nele (**D-40**).

| | |
|---|---|
| **Domínio** | Infraestrutura / Borda |
| **Serviço** | todos (efeito sobre Gateway; nginx não é um serviço de aplicação) |
| **Depende de** | [BE-36](BE-36-api-gateway.md), [BE-37](BE-37-deploy-t2-vm.md), [BE-40](BE-40-jwt-rs256-e-persisted-padrao.md), fundação FE ([FE-01](../frontend/FE-01-fundacao-workspace.md)) |
| **Bloqueia** | [BE-39](BE-39-verificacao-t2.md) (o roteiro de apresentação passa a partir pelo front, servido por este nginx) |
| **Regras cobertas** | nenhuma de negócio — reafirma D-21/D-32/FD-16 numa topologia com um processo estático a mais |
| **Estimativa** | M |

## Objetivo

Na VM, uma única origem pública (porta 80) serve o Angular compilado e repassa `/api/*` ao Gateway — o navegador nunca vê duas origens, o Gateway continua sendo o único ponto de entrada da **API**, e o nginx é só um servidor estático com proxy, sem lógica própria.

## Escopo

### Inclui

**Papel do nginx (D-40)**

- Serve os arquivos estáticos do build do Angular (`index.html`, JS/CSS com hash).
- Faz proxy reverso de `/api/*` para o Gateway.
- **Não** autentica, **não** valida payload, **não** decide nada de negócio — se o Gateway cair, o nginx devolve o erro do proxy (502/504), não substitui a resposta por conta própria. O Gateway continua sendo o único ponto de entrada da **API** (D-32); o nginx é o único ponto de entrada do **tráfego HTTP** da VM, um nível abaixo.

**VM — `deploy/nginx/todolist.conf`** (novo arquivo, versionado)

- `listen 80;`
- `root` apontando para o diretório onde o build do Angular é publicado na VM (mesmo padrão de destino usado pelos outros artefatos de `scripts/publish.ps1`/`install-on-vm.sh`).
- `try_files $uri $uri/ /index.html;` — toda rota profunda do SPA (`/tasks`, `/login`, etc.) recarrega para `index.html` em vez de 404, deixando o roteador do Angular resolver.
- `location /api/ { proxy_pass http://127.0.0.1:8080; ... }`:
  - `proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;`
  - `proxy_set_header X-Forwarded-Proto $scheme;`
  - `proxy_set_header Host $host;`
  - **sem** remover ou sobrescrever o header `traceparent` — ele precisa atravessar o proxy intacto para a correlação de log ponta a ponta (BE-39 CA-08) continuar valendo com o nginx no meio.
- Cache de resposta:
  - `index.html` servido com `Cache-Control: no-cache` (o SPA shell precisa ser revalidado a cada carga, para pegar deploy novo);
  - assets com hash no nome (padrão do build do Angular) servidos com `Cache-Control: immutable` — o hash no nome já garante que um conteúdo novo tem URL nova.
- Headers de segurança básicos: `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin` (ou equivalente conservador), e uma **CSP básica** compatível com o Angular (permite os recursos que o build do Angular efetivamente carrega — script/style/img/connect da própria origem; sem `unsafe-eval` a menos que o build exija). A CSP é registrada como ponto de atenção: um Angular buildado em modo produção com AOT normalmente não precisa de `unsafe-eval`; se o build exigir, documentar o motivo no PR em vez de relaxar a política sem registro.
- **O Gateway passa a escutar em `127.0.0.1:8080`** na VM (era `0.0.0.0:8080`, BE-37) — só o nginx (mesma máquina) alcança essa porta agora; o navegador nunca fala com ela diretamente.
- **`ForwardedHeaders`** habilitado no Gateway (`UseForwardedHeaders`), configurado com o **único** proxy conhecido `127.0.0.1` (`KnownProxies`) — sem isso, o Gateway veria toda requisição como vinda de `127.0.0.1` em vez do IP real do cliente, o que degrada qualquer log ou decisão futura baseada em IP de origem.
- **Firewall da VM:**
  - abrir `tcp:80` (nova regra ou ajuste da existente) para `0.0.0.0/0` — é a nova porta pública única;
  - **fechar/remover** a regra pública de 8080 (`todolist-allow-gateway`, criada em BE-37) — o Gateway deixa de ser alcançável diretamente de fora, o nginx é quem fala com o mundo agora.
- **`install-on-vm.sh` instala o nginx e o site:** pacote `nginx` (ou equivalente da distro da VM), cópia de `deploy/nginx/todolist.conf` para o local que o nginx espera (ex.: `/etc/nginx/sites-available/` + link em `sites-enabled/`, ou `/etc/nginx/conf.d/`, conforme a distro já em uso pela VM), `nginx -t` antes de recarregar, e `systemctl reload nginx` (ou `enable --now` na primeira instalação) — mesma filosofia idempotente dos outros passos de `install-on-vm.sh`.
- **`scripts/publish.ps1` passa a incluir o build do front** (`ng build` de produção, ou equivalente do workspace Angular de FE-01) no tarball publicado, ao lado dos três `publish/<serviço>/` já existentes — um `publish/frontend/` com os arquivos estáticos gerados.

**Container — preparo do T3 (`frontend/Dockerfile`)**

- Multi-stage:
  - stage de build: `node:22`, roda o build de produção do Angular (workspace de FE-01);
  - stage final: **`nginxinc/nginx-unprivileged`**, escutando na **8080** (a imagem non-root já espera portas ≥ 1024 — coerente com "nenhum processo roda como root", mesmo princípio de [BE-38](BE-38-containerizacao.md) CA-02 aplicado aqui).
- O upstream do `/api` **não é fixo no `nginx.conf`** — vem de uma **variável de ambiente** (`GATEWAY_UPSTREAM`), processada pelo mecanismo `/etc/nginx/templates` + `envsubst` **da própria imagem oficial** (o template `.conf.template` vira `.conf` na inicialização do container, com `${GATEWAY_UPSTREAM}` substituído). Isso é o que permite o mesmo Dockerfile apontar para `http://gateway:8080` no compose local e para uma URL do Cloud Run no T3, sem rebuild de imagem.

**No T3 (Cloud Run) — registrado como decisão em aberto, não implementado aqui**

- O proxy do nginx aponta para a URL **https** do Gateway, com `proxy_ssl_server_name on;` e o `Host` do upstream preservado (Cloud Run roteia por `Host`/SNI).
- **Em aberto:** se o Gateway continua público no T3 ou passa a receber tráfego só do nginx (que passaria a ser o único serviço `--allow-unauthenticated`) é decisão do T3, **não** desta task — registrada aqui como pendência explícita para não ser esquecida quando o T3 começar.

### Não inclui

- Qualquer alteração de comportamento do Gateway além de escutar em `127.0.0.1` e configurar `ForwardedHeaders` — nenhuma rota, validação ou autenticação muda.
- TLS na VM — o nginx segue em HTTP puro na porta 80 nesta etapa; TLS é considerado só se a apresentação exigir (não é requisito do `t2.md`).
- Rodar o `frontend/Dockerfile` na VM — a VM continua com nginx nativo + systemd para os três serviços .NET (mesmo padrão de BE-37/BE-38: Docker é preparo do T3, validado só localmente).
- Rate limiting, cache de API, compressão avançada no nginx — fora do escopo; o nginx aqui é deliberadamente simples.
- Decidir a topologia de rede do T3 (Gateway público ou não) — registrado como pendência, não decidido.

## Notas técnicas

- **Por que o nginx não é "mais um gateway".** A tentação natural, com um proxy reverso na frente do Gateway, é começar a colocar regra ali — um redirect condicional, uma checagem de header. **D-40** existe para vetar isso explicitamente: o nginx serve arquivo estático e repassa bytes; toda decisão (autenticação, validação, tradução de protocolo) continua exclusivamente no Gateway (D-32/D-33). No dia em que uma regra aparecer no `todolist.conf`, ela está no lugar errado — o mesmo princípio que D-33 já aplica ao Gateway em relação a Identity/Tasks, um nível abaixo.
- **Por que `try_files ... /index.html` é obrigatório, e não um detalhe de conveniência.** Um SPA com roteamento no cliente (Angular Router) não tem arquivo físico para `/tasks` ou `/login` — só existe `index.html` mais o roteador JavaScript. Sem o fallback, recarregar a página numa rota profunda (F5 em `/tasks`) devolve 404 do nginx antes mesmo do Angular carregar. É o caso de teste mais fácil de esquecer porque a navegação **dentro** da aplicação (sem F5) nunca aciona esse caminho.
- **Por que o `traceparent` não pode ser tocado pelo proxy.** BE-39 depende do mesmo `traceId` atravessando Gateway → Tasks → Identity para a demonstração de correlação de log; um proxy na frente que não repassasse esse header (ou o único cenário mais sutil: um `proxy_pass` que reescreve headers por engano) quebraria essa correlação sem nenhum sintoma visível na resposta HTTP — só apareceria ao tentar montar a evidência de log da apresentação.
- **Por que `127.0.0.1` como único proxy conhecido, e não `ForwardedHeaders` aberto.** `ForwardedHeaders` sem `KnownProxies` restrito aceita o header `X-Forwarded-For` de **qualquer** chamador direto — inclusive alguém que, por uma falha de firewall, alcançasse a porta 8080 diretamente e forjasse a própria origem aparente. Restringir a `127.0.0.1` (o único lugar de onde o Gateway aceita conexão depois desta task) fecha esse caminho.
- **Por que o upstream do container é variável de ambiente, e a da VM é fixa (`127.0.0.1:8080`).** São dois ambientes com necessidades diferentes: na VM, os três processos vivem na mesma máquina para sempre (é a topologia da VM); no container (preparo do T3), o Gateway pode estar em outro host/serviço, e o T3 já vai precisar trocar esse endereço para uma URL do Cloud Run sem reconstruir a imagem — fixar `127.0.0.1` no Dockerfile empurraria esse retrabalho para dentro do T3.

## Critérios de aceite

- [x] **CA-01** — Uma rota profunda do SPA (ex.: `http://<IP>/tasks`) recarregada diretamente no navegador (F5) devolve o `index.html` do Angular, não um 404 do nginx.
- [ ] **CA-02** — `GET http://<IP>/api/tasks` (com token válido) chega ao Gateway e responde como se chamado diretamente — o `traceparent` de entrada é o **mesmo** que aparece no log do Gateway para essa requisição.
- [ ] **CA-03** — As portas **8080, 5080, 5081, 5100, 5101** não respondem a partir de **fora** da VM — verificado por `curl --max-time 3` contra o IP externo em cada uma, recusa de conexão ou timeout em todas (substitui e amplia a verificação equivalente de BE-37 CA-03, que cobria só 5080/5081/5100/5101; agora 8080 entra na lista de portas fechadas).
- [ ] **CA-04** — A porta **80** responde a partir de fora da VM, servindo o `index.html` do Angular.
- [x] **CA-05** — O navegador, ao usar a aplicação (DevTools → Network), nunca faz um preflight `OPTIONS` de CORS — todas as chamadas de `/api/*` são vistas como mesma origem.
- [x] ~~**CA-06** — `nginx -t` valida a configuração sem erro antes de qualquer `reload`/`restart` do `install-on-vm.sh`.~~ **Substituído (04/10/2026)** pelo roteiro de 25/09/2026: o nginx roda em container (`frontend/nginx.conf.template`) e `install-on-vm.sh` não existe; uma configuração inválida impede o container de subir.
- [x] **CA-07** — Recarregar `index.html` sempre busca a versão mais nova do servidor (header `Cache-Control: no-cache` presente na resposta), enquanto um asset com hash no nome vem com `Cache-Control` indicando cacheável de forma imutável.
- [ ] **CA-08** — Os headers de segurança (`X-Content-Type-Options`, `Referrer-Policy`, CSP) estão presentes na resposta do `index.html`, e a aplicação Angular carrega e funciona sob essa CSP (nenhum recurso bloqueado no console do navegador).
- [x] **CA-09** — `frontend/Dockerfile` builda a imagem com sucesso a partir da raiz do repositório (mesmo padrão de comando documentado dos outros três Dockerfiles, BE-38).
- [x] **CA-10** — Trocar `GATEWAY_UPSTREAM` no container (variável de ambiente) muda o destino do proxy sem rebuild de imagem — verificado subindo o container duas vezes com valores diferentes.
- [x] **CA-11** — `docker inspect` confirma que o container do `frontend/Dockerfile` não roda como root.

## Testes obrigatórios

- Verificação manual na VM real (proxy reverso e firewall não são automatizáveis pela suíte .NET): CA-01 a CA-08, executados e registrados no PR (saída de `curl`, prints do DevTools, saída de `nginx -t`).
- Verificação manual do container (`docker build`/`docker run` local): CA-09 a CA-11.
- Regressão: o roteiro de verificação de [BE-39](BE-39-verificacao-t2.md) (401/400/201, mesmo `traceId`) é reexecutado **através** do nginx (`http://<IP>/api/...`), não mais direto no Gateway — é o que prova CA-02 em condição real de demonstração.

## Decisões em aberto

- **D-40** — O nginx é servidor estático com proxy de `/api`, implementando FD-16 (mesma origem) com processos separados; não é um segundo gateway. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
- **Pendência de T3** — Se o Gateway continua público (`--allow-unauthenticated`) no Cloud Run, ou passa a aceitar tráfego só do serviço de frontend, fica para quando o T3 começar.

## Auditoria dos critérios (03/10/2026)

Critérios conferidos contra o código em 03/10/2026. Marcados: 6 de 11. Não há teste automatizado do próprio nginx; o que foi marcado se apoia na configuração (`frontend/nginx.conf.template`, `frontend/Dockerfile`, `docker-compose.yml`) e, quando citado, na suíte Playwright (`frontend/e2e`, `baseURL` padrão `http://localhost`, ou seja, atravessa o nginx). A stack do compose estava parada na auditoria.

| CA | Situação | Evidência / motivo |
|---|---|---|
| CA-01 | atendido (por configuração + e2e) | `location / { try_files $uri $uri/ /index.html; }` em `nginx.conf.template`; `e2e/a11y.spec.ts` abre rotas profundas (`/account/password`, `/tasks/<id>/edit`) direto pelo nginx. Sem teste dedicado de F5. |
| CA-02 | em aberto | O template não sobrescreve `traceparent` (o nginx o repassa por padrão) e o README de deploy afirma correlação em campo, mas nenhum teste automatizado prova que o `traceparent` de entrada é o do log do Gateway através do nginx. |
| CA-03 | em aberto (não verificável) | Portas 8080/5080/5081/5100/5101 fechadas para fora: depende do firewall/IP externo da VM. Por configuração, `docker-compose.yml` e `deploy/docker-compose.prod.yml` não publicam `ports:` do gateway/identity/tasks. |
| CA-04 | em aberto (não verificável) | Porta 80 respondendo de fora da VM; `deploy/README.md` seção 13 registra verificação de campo em 30/09, sem prova aqui. |
| CA-05 | atendido por desenho | Chamadas relativas (`apiBaseUrl` vazio, FD-16) e nenhum CORS configurado no Gateway; mesma origem não gera preflight. Não há asserção de ausência de `OPTIONS` nos e2e. |
| CA-06 | em aberto (superado) | `install-on-vm.sh` não existe mais; o nginx roda em container (`nginxinc/nginx-unprivileged`) e não há passo `nginx -t` antes de reload. Uma configuração inválida derruba o container ao subir, em vez de ser validada antes. |
| CA-07 | atendido | `location = /index.html` com `Cache-Control: no-cache`; assets com hash com `public, max-age=31536000, immutable` em `nginx.conf.template`. |
| CA-08 | em aberto (parcial) | Headers `X-Content-Type-Options`, `Referrer-Policy`, `X-Frame-Options` e CSP estão em `/index.html` e nos assets (`nginx.conf.template`). Falta evidência de que a aplicação roda sem violação de CSP no console: nenhum e2e escuta `securitypolicyviolation`. |
| CA-09 | atendido | `frontend/Dockerfile` (multi-stage node, nginx-unprivileged); imagem `todolist-frontend:latest` (83 MB) construída e publicada no registry local. O comando de build do frontend não aparece no `README.md` como o dos outros três. |
| CA-10 | atendido (por configuração) | `NGINX_ENVSUBST_FILTER` e `set $api_upstream "http://${GATEWAY_UPSTREAM}"` no template; valor vindo do ambiente em `docker-compose.yml` e `deploy/docker-compose.prod.yml`. A verificação com dois valores diferentes não foi reexecutada. |
| CA-11 | atendido | `docker inspect todolist-frontend:latest` mostra `User=101` (não-root). |
