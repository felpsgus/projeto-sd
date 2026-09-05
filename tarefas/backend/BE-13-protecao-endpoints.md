# BE-13 — Proteção de endpoints e identidade do usuário corrente

| | |
|---|---|
| **Domínio** | Autorização |
| **Serviço** | **Identity** (validação de token); no Tasks, apenas `ICurrentUser`/`IClientDate` |
| **Depende de** | [BE-08](BE-08-emissao-jwt.md) |
| **Bloqueia** | BE-14 a BE-22 |
| **Regras cobertas** | RN-AUTZ-04 |
| **Estimativa** | P |

## Objetivo

Todo endpoint é protegido **por padrão**; o acesso anônimo é a exceção declarada explicitamente. Os casos de uso obtêm o id do usuário corrente sem ler claims manualmente.

## Escopo

### Inclui

- **Fallback policy** de autorização exigindo usuário autenticado, aplicada globalmente **no Identity Service**. Endpoints públicos declaram `AllowAnonymous` **um a um**: `/health`, `/api/auth/register`, `/api/auth/login`, `/api/auth/refresh`, OpenAPI.
- **O Tasks Service NÃO valida JWT** (**D-31**). Ele não recebe `Jwt:SigningKey`, não registra autenticação Bearer, e não tem como verificar um token por conta própria — com chave simétrica, quem valida também assina, e o Tasks não pode ser promovido a emissor de tokens.
  - **De onde vem a identidade no Tasks:** do chamador. Hoje, do header `X-User-Id` do gatilho provisório ([BE-29](BE-29-gatilho-http-criar-tarefa.md), **D-30**); depois, do API Gateway, que valida o token chamando `ValidateToken` no Identity e repassa a identidade já verificada.
  - **Isso só é seguro com o Gateway na frente** (**D-32**): o Tasks confia em quem o chama, então ele **NÃO DEVE** ficar publicamente acessível.
  - A checagem do **dono** da tarefa continua sendo feita contra o Identity, por gRPC ([BE-28](BE-28-validacao-dono-grpc.md)) — é outra coisa, e independe de token.
- Abstração `ICurrentUser` em `Application` — **nos dois serviços**, com implementações diferentes (claim `sub` no Identity, header no Tasks):
  - `Guid Id` (lança se não autenticado — indica endpoint mal configurado);
  - `bool IsAuthenticated`.
  No **Identity**, a implementação lê o claim `sub` do `HttpContext`. No **Tasks**, lê a identidade repassada pelo chamador ([BE-29](BE-29-gatilho-http-criar-tarefa.md)). A `Application` dos dois consome a mesma abstração e não sabe a diferença.
- Abstração **`IClientDate`** em `Application` (decisão **D-18**), mesmo padrão:
  - `DateOnly Today` — a data local do usuário, usada para calcular `IsOverdue` e o filtro `overdue`.
  Implementação lê o header **`X-Client-Date: yyyy-MM-dd`** do `HttpContext`. Header ausente, malformado ou absurdo → **fallback silencioso para a data UTC** do `TimeProvider`, sem erro e sem 400.
- Respostas padronizadas:
  - sem token / token inválido / expirado → **401** com `ProblemDetails`;
  - autenticado mas sem permissão → **403**.
- Teste de guarda automatizado: enumera todos os endpoints mapeados e falha se algum estiver anônimo sem constar de uma **allowlist explícita** no próprio teste. Assim, um endpoint novo esquecido sem proteção quebra o build.

### Não inclui

- Autorização por propriedade de recurso (BE-18) — aqui é só "está autenticado?".
- Papéis/roles: não existem nesta versão (seção 2 das regras de negócio).

## Notas técnicas

- Segurança por **opt-out**, não opt-in. É a diferença entre esquecer de proteger (falha aberta) e esquecer de liberar (falha fechada, detectada no primeiro teste).
- **Por que a proteção não é simétrica entre os dois serviços.** Parece incoerente o Identity exigir token e o Tasks não. A razão é que a autenticação acontece **uma vez, na borda** — hoje no Identity para os endpoints de conta, amanhã no Gateway para tudo (**D-32**). Repeti-la no Tasks exigiria dar-lhe a chave de assinatura, o que criaria um segundo emissor de tokens (**D-31**) — mais superfície de ataque em troca de uma verificação redundante.
- `ICurrentUser.Id` lançar quando não autenticado é intencional: se um handler autenticado chega sem identidade, é bug de configuração, não erro de negócio (regra da seção 2.1 das convenções sobre exceção vs. `Result`).
- A camada `Application` **não** referencia `HttpContext` — só a abstração.
- **Por que `IClientDate` mora aqui:** é o mesmo padrão de `ICurrentUser` — um valor ambiente da requisição, lido do `HttpContext` e exposto à `Application` por abstração. Concentrá-lo em um lugar é o que evita repetir um parâmetro `today` nos cinco endpoints que devolvem `isOverdue`.
- **O fallback do `X-Client-Date` nunca é erro.** Um header ausente é o caso normal de um cliente que não é o nosso SPA (curl, Swagger, integração futura). Responder 400 quebraria essas chamadas por causa de um detalhe de apresentação. A data UTC é um padrão correto o bastante.
- O valor é apenas **informativo para exibição** — não autoriza nada, não filtra dados de outro usuário. Um cliente que mande uma data errada só vê o próprio selo de "atrasada" errado. Por isso não precisa de validação rígida.

## Critérios de aceite

- [ ] **CA-01** — Um endpoint novo, criado sem nenhum atributo, exige autenticação por padrão.
- [ ] **CA-02** — Requisição sem cabeçalho `Authorization` a um endpoint protegido retorna **401** com corpo `application/problem+json`.
- [ ] **CA-03** — Requisição com token expirado retorna **401**.
- [ ] **CA-04** — Requisição com token malformado ou assinatura inválida retorna **401**, nunca 500.
- [ ] **CA-05** — Requisição com token válido é processada normalmente.
- [ ] **CA-06** — `ICurrentUser.Id` dentro de um handler autenticado devolve o `Guid` do claim `sub`.
- [ ] **CA-07** — O teste de guarda enumera as rotas e **falha** quando um endpoint anônimo não listado na allowlist é adicionado (comprovado adicionando um endpoint temporário durante o desenvolvimento).
- [ ] **CA-08** — Os endpoints públicos do Identity permanecem acessíveis sem token: `/health`, `register`, `login`, `refresh` e a documentação OpenAPI.
- [ ] **CA-09** — `/api/auth/logout` **exige** autenticação (não está na allowlist).
- [ ] **CA-09b** — O Tasks Service sobe **sem nenhuma configuração `Jwt:*`** e sem registrar autenticação Bearer ([BE-08](BE-08-emissao-jwt.md), CA-15).
- [ ] **CA-09c** — O teste de guarda de rotas roda no Identity com a allowlist acima. No Tasks, o equivalente é o teste de [BE-29](BE-29-gatilho-http-criar-tarefa.md) CA-08, que garante que a identidade repassada só é aceita no modo provisório.
- [ ] **CA-10** — A camada `Application` não referencia `Microsoft.AspNetCore.*` (teste de arquitetura).
- [ ] **CA-11** — O corpo da resposta 401 não vaza detalhe do motivo (expirado vs. inválido vs. ausente).

### `IClientDate` (D-18)

- [ ] **CA-12** — Com `X-Client-Date: 2026-08-21`, `IClientDate.Today` devolve `2026-08-21`.
- [ ] **CA-13** — Sem o header, `Today` devolve a data UTC do `TimeProvider`, e a requisição é processada normalmente (**200**, não 400).
- [ ] **CA-14** — Header malformado (`"ontem"`, `"21/08/2026"`, `"2026-13-45"`, string vazia) cai no mesmo fallback, sem erro e sem exceção.
- [ ] **CA-15** — O valor é request-scoped: duas requisições simultâneas com headers diferentes enxergam cada uma a sua própria data.
- [ ] **CA-16** — `IClientDate` é lido de um único ponto; nenhum handler recebe `today` como parâmetro próprio (verificado por revisão).

## Testes obrigatórios

- Integração: CA-01 a CA-06, CA-08, CA-09, CA-11 a CA-15.
- Teste de guarda de rotas: CA-07 — **este teste é o entregável mais importante da task**.
- Arquitetura: CA-10.
- **CA-15 é obrigatório**: um `IClientDate` acidentalmente singleton faria um usuário ver o "atrasada" calculado com a data de outro.

## Decisões em aberto

- **D-18** — ✅ decidida: data local do usuário via header. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
- **D-31** — ✅ decidida: o Tasks não valida JWT; validação externa é por `ValidateToken`.
- **D-32** — ✅ decidida: o Gateway é a origem única, e os serviços não são publicamente acessíveis.
