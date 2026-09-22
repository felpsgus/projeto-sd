# Frontend — Índice de tarefas

Stack alvo: **Angular 22 (standalone, signals-first, zoneless) / TypeScript ~5.9 estrito / Vitest + Angular Testing Library / Playwright**, conforme [CONVENCOES-CODIGO.md](../../CONVENCOES-CODIGO.md).

A entrada desta quebra são os **contratos de API** definidos nas tasks de [backend/](../backend/). Cada task de frontend referencia a task BE que produz o endpoint que ela consome.

> **Leia [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md) antes de começar.** Quatro decisões estruturais já estão fechadas e valem para toda a quebra: **FD-01** (refresh token em cookie `HttpOnly` — o frontend não armazena credencial alguma), **FD-16** (mesma origem: sem CORS, sem CSRF, mas `withCredentials` obrigatório), **FD-17** (header `X-Client-Date` para o cálculo de "atrasada") e **FD-18** (API sem versionamento).

## Ordem de execução

### Onda 0 — Fundação (bloqueia tudo)

| # | Tarefa | Est. |
|---|---|---|
| [FE-01](FE-01-fundacao-workspace.md) | Fundação do workspace Angular 22 | M |
| [FE-02](FE-02-contratos-camada-http.md) | Contratos de API e camada HTTP tipada | M |
| [FE-03](FE-03-erros-feedback.md) | Erros, feedback e estados de UI | M |
| [FE-04](FE-04-layout-design-base.md) | Layout, design base e acessibilidade | M |

### Onda 1 — Sessão

| # | Tarefa | Est. |
|---|---|---|
| [FE-05](FE-05-estado-sessao.md) | Estado de sessão e armazenamento de tokens | G |
| [FE-06](FE-06-interceptor-auth-refresh.md) | Interceptor de autenticação e renovação automática | G |
| [FE-07](FE-07-roteamento-guards.md) | Roteamento, guards e lazy loading | M |

### Onda 2 — Autenticação

| # | Tarefa | Est. |
|---|---|---|
| [FE-08](FE-08-tela-cadastro.md) | Tela de cadastro | M |
| [FE-09](FE-09-tela-login.md) | Tela de login | M |
| [FE-10](FE-10-logout.md) | Logout e encerramento de sessão | P |

### Onda 3 — Conta

| # | Tarefa | Est. |
|---|---|---|
| [FE-11](FE-11-perfil-usuario.md) | Perfil do usuário | P |
| [FE-12](FE-12-alteracao-senha.md) | Alteração de senha | M |
| [FE-13](FE-13-exclusao-conta.md) | Exclusão de conta | M |

### Onda 4 — Tarefas

| # | Tarefa | Est. |
|---|---|---|
| [FE-14](FE-14-servico-estado-tarefas.md) | Serviço e estado de tarefas | M |
| [FE-15](FE-15-listagem-paginacao.md) | Listagem e paginação | G |
| [FE-16](FE-16-filtros-busca-url.md) | Filtros, busca e sincronia com a URL | G |
| [FE-17](FE-17-criar-tarefa.md) | Criar tarefa | M |
| [FE-18](FE-18-editar-tarefa.md) | Editar tarefa | M |
| [FE-19](FE-19-concluir-reabrir.md) | Concluir e reabrir tarefa | M |
| [FE-20](FE-20-remover-tarefa.md) | Remover tarefa | P |

### Onda 5 — Qualidade

| # | Tarefa | Est. |
|---|---|---|
| [FE-21](FE-21-acessibilidade-responsividade.md) | Acessibilidade e responsividade | M |
| [FE-22](FE-22-testes-e2e.md) | Testes E2E com Playwright | M |
| [FE-23](FE-23-ci-build-seguranca.md) | CI, cobertura, build e segurança | M |

> Estimativa: **P** ≈ até 1 dia · **M** ≈ 1–2 dias · **G** ≈ 3+ dias.

## Grafo de dependências

```
FE-01 ──┬── FE-02 ──┬── FE-03 ──┬── FE-05 ── FE-06 ── FE-07 ──┬── FE-08
        │           │           │                             ├── FE-09 ── FE-10
        └── FE-04 ──┘           │                             │
                                │                             ├── FE-11 ── FE-12 ── FE-13
                                │                             │
                                └─────────────────────────────┴── FE-14 ──┬── FE-15 ── FE-16
                                                                          ├── FE-17 ── FE-18
                                                                          ├── FE-19
                                                                          └── FE-20
FE-21 acompanha da FE-04 em diante · FE-22 e FE-23 fecham ao final
```

## Dependência das tasks de backend

Uma task de frontend **não pode ser dada como pronta** antes da task de backend que a serve. Durante o desenvolvimento, o mock de API (FE-02) permite trabalhar em paralelo.

| Frontend | Depende do backend |
|---|---|
| FE-05, FE-06 | [BE-08](../backend/BE-08-emissao-jwt.md), [BE-10](../backend/BE-10-refresh-token-rotacao.md) |
| FE-08 | [BE-07](../backend/BE-07-cadastro-usuario.md) |
| FE-09 | [BE-09](../backend/BE-09-login.md), [BE-12](../backend/BE-12-bloqueio-tentativas-login.md) |
| FE-10 | [BE-11](../backend/BE-11-logout-revogacao.md) |
| FE-11 | [BE-14](../backend/BE-14-perfil-usuario.md) |
| FE-12 | [BE-15](../backend/BE-15-alteracao-senha.md) |
| FE-13 | [BE-16](../backend/BE-16-exclusao-conta.md) |
| FE-15, FE-16 | [BE-22](../backend/BE-22-listagem-tarefas.md) |
| FE-17 | [BE-17](../backend/BE-17-criar-tarefa.md) |
| FE-18 | [BE-18](../backend/BE-18-consultar-tarefa-autorizacao.md), [BE-19](../backend/BE-19-editar-tarefa.md) |
| FE-19 | [BE-20](../backend/BE-20-concluir-reabrir-tarefa.md) |
| FE-20 | [BE-21](../backend/BE-21-remover-tarefa.md) |

## Rastreabilidade — Regra de negócio → Tarefa

O frontend cobre as regras pelo **lado do usuário**: o que ele vê, informa e consegue fazer. A autoridade sobre a regra continua sendo o backend — a validação no cliente é conveniência, nunca garantia.

| RN | Descrição resumida | Tarefa |
|---|---|---|
| RN-AUTH-01 | Visitante cria conta | [FE-08](FE-08-tela-cadastro.md) |
| RN-AUTH-02 | E-mail único | [FE-08](FE-08-tela-cadastro.md) (tratar 409) |
| RN-AUTH-03 | Formato de e-mail | [FE-08](FE-08-tela-cadastro.md) |
| RN-AUTH-04 | Política de senha | [FE-08](FE-08-tela-cadastro.md), [FE-12](FE-12-alteracao-senha.md) |
| RN-AUTH-05 | Senha nunca exibida/persistida | [FE-08](FE-08-tela-cadastro.md), [FE-12](FE-12-alteracao-senha.md), [FE-23](FE-23-ci-build-seguranca.md) |
| RN-AUTH-07 | Nome padrão vindo do e-mail | [FE-08](FE-08-tela-cadastro.md) |
| RN-AUTH-08 | Login com e-mail + senha | [FE-09](FE-09-tela-login.md) |
| RN-AUTH-09 | Mensagem genérica de credencial | [FE-09](FE-09-tela-login.md) |
| RN-AUTH-10, RN-AUTH-11 | Par de tokens, access curto | [FE-05](FE-05-estado-sessao.md) |
| RN-AUTH-12 | Logout | [FE-10](FE-10-logout.md) |
| RN-AUTH-13 | Bloqueio por tentativas (429) | [FE-09](FE-09-tela-login.md) |
| RN-AUTH-14 a RN-AUTH-18 | Renovação, rotação, reuso, expiração | [FE-06](FE-06-interceptor-auth-refresh.md) |
| RN-AUTH-19 | Sessões revogadas → voltar ao login | [FE-06](FE-06-interceptor-auth-refresh.md), [FE-12](FE-12-alteracao-senha.md), [FE-13](FE-13-exclusao-conta.md) |
| RN-AUTH-20 | Refresh token com tratamento restrito | [FE-05](FE-05-estado-sessao.md) — cookie `HttpOnly` (**FD-01**) |
| RN-AUTH-21 | Trocar a própria senha | [FE-12](FE-12-alteracao-senha.md) |
| RN-USER-01 | Dados do usuário | [FE-11](FE-11-perfil-usuario.md) |
| RN-USER-02 | Editar nome de exibição | [FE-11](FE-11-perfil-usuario.md) |
| RN-USER-03 | E-mail não editável | [FE-11](FE-11-perfil-usuario.md) |
| RN-USER-04 | Inativo não autentica | [FE-09](FE-09-tela-login.md) |
| RN-USER-05 | Excluir a própria conta | [FE-13](FE-13-exclusao-conta.md) |
| RN-TASK-01 a RN-TASK-05 | Campos e validações da tarefa | [FE-17](FE-17-criar-tarefa.md), [FE-18](FE-18-editar-tarefa.md) |
| RN-TASK-06 a RN-TASK-09 | Estados, concluir, reabrir | [FE-19](FE-19-concluir-reabrir.md) |
| RN-TASK-10 | Criar informando o título | [FE-17](FE-17-criar-tarefa.md) |
| RN-TASK-11 | Editar tarefa | [FE-18](FE-18-editar-tarefa.md) |
| RN-TASK-12, RN-TASK-13 | Remover (some da lista) | [FE-20](FE-20-remover-tarefa.md) |
| RN-TASK-14 | Data de atualização visível | [FE-15](FE-15-listagem-paginacao.md), [FE-18](FE-18-editar-tarefa.md) |
| RN-TASK-15 | Limite de 500 tarefas ativas | [FE-17](FE-17-criar-tarefa.md) |
| RN-TASK-16 | Sinalizar "atrasada" | [FE-15](FE-15-listagem-paginacao.md) |
| RN-AUTZ-02, RN-AUTZ-03 | Só as próprias tarefas; 404 igual | [FE-14](FE-14-servico-estado-tarefas.md), [FE-18](FE-18-editar-tarefa.md) |
| RN-AUTZ-04 | Visitante não acessa tarefas | [FE-07](FE-07-roteamento-guards.md) |
| RN-LIST-01 | Lista só do usuário | [FE-15](FE-15-listagem-paginacao.md) |
| RN-LIST-02 a RN-LIST-05 | Filtros e busca | [FE-16](FE-16-filtros-busca-url.md) |
| RN-LIST-06 | Ordenação padrão | [FE-15](FE-15-listagem-paginacao.md) |
| RN-LIST-07 | Paginação | [FE-15](FE-15-listagem-paginacao.md) |

## Recorte do T2 (entrega de 22/10/2026)

> Registrado em 21/09/2026. O enunciado do T2 (`t2.md` na raiz) exige frontend obrigatório, interagindo **só** com o API Gateway, com a demo partindo do frontend e mostrando 400, 401 e 200/201 com persistência real. Esta seção recorta a quebra acima para caber nisso, sem reescrever as tasks — cada uma ganhou um bloco "Recorte do T2" logo após seu cabeçalho.
>
> **Decisões de contexto que mudam o desenho, mas não a numeração das tasks:**
> - **Angular 22**, conforme já definido acima. Pré-requisito: Node 22 LTS ou 24 LTS (o Node local hoje é v16 — atualizar antes de começar FE-01).
> - **Sessão só em memória, sem refresh token.** O backend do T2 emite só um access token ([BE-33](../backend/BE-33-login-minimo-grpc.md)/[BE-36](../backend/BE-36-api-gateway.md), decisão **D-36**) — não há refresh token, cookie `HttpOnly`, cadastro, logout no servidor ou perfil. Isso **simplifica** FE-05 e FE-06 em vez de complicá-los: não existe renovação para orquestrar. Ver **FD-20** (nova — o pedido original citava "FD-19", mas esse número já estava em uso; ver a nota de numeração na própria entrada) e a nota em **FD-01**, ambas em [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
> - **Mesma origem via nginx, não mais via API Gateway sozinho.** O nginx serve o build do Angular e faz proxy de `/api` para o Gateway (decisão de backend **D-40**, task [BE-42](../backend/BE-42-nginx-mesma-origem.md)) — sem CORS, preservando FD-16 e FD-01. Em dev, `ng serve` com `proxy.conf.json` reproduz a mesma origem apontando para `http://localhost:8080`. Ver emenda de **FD-16**.
> - **JWT RS256 validado localmente no Gateway** ([BE-40](../backend/BE-40-jwt-rs256-e-persisted-padrao.md), decisão **D-38**) — troca a validação via gRPC ao Identity (D-31, hoje implementada) por validação local da assinatura. Invisível para o frontend: o contrato de erro do 401 não muda.
> - **Escopo funcional: login, criar tarefa e listar tarefas.** Cadastro, perfil, troca de senha, exclusão de conta, editar/concluir/reabrir/remover tarefa, filtros e busca ficam fora.

| Task | Entra no T2? | Recorte | Depende de (BE) |
|---|---|---|---|
| [FE-01](FE-01-fundacao-workspace.md) | Sim | Integral, incluindo `.nvmrc` com Node 22/24 e o `proxy.conf.json` de dev | — |
| [FE-02](FE-02-contratos-camada-http.md) | Sim | Contratos só de login e tarefas (criar, listar, obter), mais o interceptor de `X-Client-Date` (FD-17) | [BE-33](../backend/BE-33-login-minimo-grpc.md)/[BE-36](../backend/BE-36-api-gateway.md), [BE-41](../backend/BE-41-listar-e-consultar-tarefas-grpc.md) |
| [FE-03](FE-03-erros-feedback.md) | Sim | Mapeamento do `ProblemDetails`, erros 400 nos campos e 401. Inclui um **indicador discreto do último status HTTP**, ligado por configuração, para a plateia ver 400/401/201 sem DevTools na demo | [BE-36](../backend/BE-36-api-gateway.md) |
| [FE-04](FE-04-layout-design-base.md) | Sim | Mínimo — só o suficiente para as três telas do recorte (login e lista/criar) | — |
| [FE-05](FE-05-estado-sessao.md) | Sim | Access token **só em memória** (signal); recarregar a página exige novo login. Sem bootstrap por refresh — não há refresh no T2 (FD-20) | [BE-33](../backend/BE-33-login-minimo-grpc.md)/[BE-36](../backend/BE-36-api-gateway.md) |
| [FE-06](FE-06-interceptor-auth-refresh.md) | Sim | Só anexa `Authorization: Bearer`. Um **401 limpa a sessão e leva ao login** com "sessão expirada". **Sem renovação automática**, proativa ou reativa | — |
| [FE-07](FE-07-roteamento-guards.md) | Sim | Guard de rota autenticada, login como rota pública e lazy loading | — |
| FE-08 | Não | Fora do T2 — sem cadastro (sem endpoint no backend do T2) | — |
| [FE-09](FE-09-tela-login.md) | Sim | Integral, menos o que depende de [BE-12](../backend/BE-12-bloqueio-tentativas-login.md) (bloqueio por tentativas/429) — o backend do T2 não tem esse endpoint | [BE-33](../backend/BE-33-login-minimo-grpc.md)/[BE-36](../backend/BE-36-api-gateway.md) |
| [FE-10](FE-10-logout.md) | Sim | Só logout local (descarta o token em memória) — sem `POST /api/auth/logout`, sem "sair de todos os dispositivos" (não há servidor a chamar) | — |
| FE-11 | Não | Fora do T2 — sem endpoint de perfil | — |
| FE-12 | Não | Fora do T2 — sem endpoint de troca de senha | — |
| FE-13 | Não | Fora do T2 — sem endpoint de exclusão de conta | — |
| [FE-14](FE-14-servico-estado-tarefas.md) | Sim | Parcial: `load` (sem filtros), `create`, `getById`; mutações ficam para FE-18 a FE-20 | [BE-36](../backend/BE-36-api-gateway.md), [BE-41](../backend/BE-41-listar-e-consultar-tarefas-grpc.md) |
| [FE-15](FE-15-listagem-paginacao.md) | Sim | Paginação simples, sem filtros | [BE-41](../backend/BE-41-listar-e-consultar-tarefas-grpc.md) |
| FE-16 | Não | Fora do T2 — sem filtros nem busca no recorte | — |
| [FE-17](FE-17-criar-tarefa.md) | Sim | Integral | [BE-36](../backend/BE-36-api-gateway.md) |
| FE-18 | Não | Fora do T2 — sem editar tarefa | — |
| FE-19 | Não | Fora do T2 — sem concluir/reabrir | — |
| FE-20 | Não | Fora do T2 — sem remover tarefa | — |
| [FE-21](FE-21-acessibilidade-responsividade.md) | Parcial | Só o básico de acessibilidade já exigido por FE-04 (skip link, landmarks, foco por navegação) — sem a auditoria transversal completa | — |
| FE-22 | Não | Fora do T2 — sem suíte E2E Playwright completa; a própria demo ao vivo cobre o papel de teste E2E na apresentação | — |
| [FE-23](FE-23-ci-build-seguranca.md) | Parcial | Só o build de produção (lint, test, build) — sem CI completo, sem gates de cobertura, sem varredura de dependências | — |

**Ajuste em relação à proposta original:** a task [FE-06](FE-06-interceptor-auth-refresh.md) ficou marcada como "sem renovação automática, proativa **ou reativa**", não só "sem renovação automática" — a leitura da task mostrou que ela também cobre o interceptor de autenticação em si (anexar `Authorization: Bearer`), que continua necessário no T2, e a distinção 401/403. Sem esse ajuste, a linha original ("só anexa Authorization; sem renovação automática") ficaria ambígua sobre se a renovação proativa (que dispara antes de qualquer 401 aparecer) também caía — e ela cai, porque depende de `accessTokenExpiresAt` vindo de um fluxo de refresh que não existe no T2. Além disso, a tabela original omitia **FE-05** e **FE-06** nas dependências de BE — completei com [BE-33/BE-36](../backend/BE-36-endpoints-t2.md), já que o login é a origem do token que essas duas tasks gerenciam. A tabela de FE-08/FE-11/FE-12/FE-13 (fora do T2) e FE-16/FE-18/FE-19/FE-20/FE-22 foi incluída explicitamente para não deixar as ausências implícitas.

## Regras válidas para toda tarefa de frontend

Além dos critérios específicos, todo PR cumpre a Definição de Pronto (seção 7 de `CONVENCOES-CODIGO.md`) e mais estas, que valem para **todo** componente entregue:

- [ ] `ChangeDetectionStrategy.OnPush` declarado.
- [ ] Componente **standalone**; nenhum NgModule novo.
- [ ] Estado por **signals**; derivações por `computed()`; `effect()` só com justificativa no código.
- [ ] Nenhum `subscribe()` manual sem `takeUntilDestroyed`.
- [ ] Nenhum `any` sem justificativa escrita ao lado.
- [ ] Componente de apresentação recebe por `input()` e emite por `output()`; não injeta serviço de dados.
- [ ] Testes consultam o DOM **pelo que o usuário vê** (texto, `role`, label), não por classe CSS ou `data-testid` frágil.
- [ ] Toda tela tem estado de **carregando**, **vazio** e **erro** tratados — não só o caminho feliz.
- [ ] Nenhuma senha ou token aparece no DOM, em `console`, ou em atributo de elemento.
