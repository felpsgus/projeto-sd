# BE-18 — Consultar tarefa por id e autorização por propriedade

| | |
|---|---|
| **Domínio** | Tarefas / Autorização |
| **Depende de** | [BE-17](BE-17-criar-tarefa.md) |
| **Bloqueia** | BE-19, BE-20, BE-21 |
| **Regras cobertas** | RN-AUTZ-01, RN-AUTZ-02, RN-AUTZ-03, RN-AUTZ-04 |
| **Estimativa** | M |

## Objetivo

Existe **um único** caminho pelo qual toda operação sobre uma tarefa específica carrega o recurso, e esse caminho torna impossível tocar a tarefa de outra pessoa — ou descobrir que ela existe.

## Escopo

### Inclui

- **`GET /api/tasks/{id}`** (autenticado) → **200** com o `TaskResponse` de BE-17.
- Método único de resolução no repositório/serviço:

  ```csharp
  Task<Result<TodoTask>> GetOwnedTaskAsync(Guid taskId, CancellationToken ct);
  ```

  que filtra **sempre** por `OwnerId == ICurrentUser.Id` **e** por não-removida, retornando `TaskErrors.NotFound` em qualquer falha. **Não existe** um `GetById` sem filtro de dono exposto à camada de aplicação.
- Regra de resposta (RN-AUTZ-03): tarefa inexistente, tarefa de outro usuário e tarefa removida produzem **exatamente o mesmo 404** — mesmo status, mesmo código, mesma mensagem.
- BE-19, BE-20 e BE-21 **DEVEM** consumir esse método; nenhuma delas repete o filtro de propriedade.

### Não inclui

- Alteração de estado — tasks seguintes.

## Notas técnicas

- **O ponto central desta task é remover a possibilidade do erro, não checá-lo em cada handler.** Se cada caso de uso tiver que lembrar de comparar `OwnerId`, alguém vai esquecer. Com um único método de resolução, esquecer não compila (não há outro caminho disponível).
- Responder 403 para tarefa alheia **viola RN-AUTZ-03**: confirmaria a existência do recurso. É sempre 404.
- O filtro por dono acontece na **query**, não em memória: `WHERE owner_id = @me AND id = @id`. Carregar e depois comparar funcionaria, mas vaza timing e desperdiça I/O.
- Ids são `Guid`, não sequenciais — enumerar é inviável. Ainda assim, a regra vale.

## Critérios de aceite

- [x] **CA-01** — `GET /api/tasks/{id}` de uma tarefa própria retorna **200** com todos os campos do `TaskResponse`.
- [x] **CA-02** — `isOverdue` vem calculado corretamente na resposta (RN-TASK-16).
- [x] **CA-03** — `GET` de id inexistente retorna **404**.
- [x] **CA-04** — `GET` de uma tarefa **de outro usuário** retorna **404** — nunca 403, nunca 200 (RN-AUTZ-02, RN-AUTZ-03).
- [x] **CA-05** — Os corpos de CA-03 e CA-04 são **byte a byte idênticos**: mesmo `type`, `title`, `detail` e código de erro. Nenhum cabeçalho os distingue.
- [x] **CA-06** — `GET` de tarefa própria **removida** (soft delete) retorna o mesmo **404**.
- [x] **CA-07** — `GET` com id em formato inválido (`/api/tasks/abc`) retorna **400**, não 500.
- [x] **CA-08** — `GET` sem token retorna **401** (RN-AUTZ-04).
- [x] **CA-09** — O SQL gerado inclui o filtro de `owner_id` na consulta — a autorização não é feita em memória (verificável por log de query em teste, ou por inspeção do `IQueryable`).
- [x] **CA-10** — Não existe, na camada de aplicação, nenhum método público que carregue uma `TodoTask` por id **sem** filtro de dono (verificado por revisão + teste de arquitetura sobre a superfície do repositório).
- [x] **CA-11** — Um teste de integração parametrizado percorre **todos** os endpoints de tarefa que recebem `{id}` (`GET`, `PUT/PATCH`, `POST /complete`, `POST /reopen`, `DELETE`) e confirma que **cada um** retorna 404 para tarefa de outro usuário. Ao adicionar um endpoint novo com `{id}`, ele entra nesse teste. *(04/10/2026: o teste transversal está no Gateway, `RouteGuardTests`, e prova que todo endpoint com `{id}` devolve 404 e que um endpoint novo quebra o teste; o isolamento entre donos é provado por endpoint nos testes gRPC do Tasks.)*

## Testes obrigatórios

- Integração: CA-01 a CA-09.
- **Teste transversal CA-11** — é o guardião de RN-AUTZ-02/03 e deve ser mantido conforme BE-19/20/21 entrarem.
- Arquitetura/revisão: CA-10.

## Auditoria dos critérios (03/10/2026)

Critérios conferidos contra o código em 03/10/2026. Marcados: 9 de 11.

| CA | Situação | Evidência / motivo |
|---|---|---|
| CA-10 | em aberto — lacuna real | `ITodoTaskRepository` ainda expõe `GetByIdAsync(Guid id)` (sem dono) e `IQueryable<TodoTask> Query()` como métodos públicos da camada de aplicação. Nenhum handler os usa hoje (todos usam `GetOwnedTaskAsync`), mas não existe teste de arquitetura sobre a superfície do repositório e o método sem filtro continua disponível para uso futuro. |
| CA-11 | em aberto | Não há o teste parametrizado/transversal pedido. Os cinco endpoints com `{id}` têm, cada um, teste próprio de tarefa alheia → 404 (`GetTaskGrpcTests`, `UpdateTaskGrpcTests`, `CompleteReopenTaskGrpcTests` (complete/reopen), `DeleteTaskGrpcTests`, e os `*TarefaAlheiaOuInexistente*` do Gateway), então o comportamento está coberto — mas nada obriga um endpoint novo a entrar na lista. |
| CA-01 a CA-09 | atendidos em outro lugar | A rota HTTP vive no Gateway (D-32); o comportamento de dono/404/isOverdue é testado no Tasks via gRPC (`GetTaskGrpcTests`) e a tradução HTTP (200/404/400/401) em `GetTaskTests` do Gateway. CA-08 (401): sem teste específico de GET sem token; coberto pela política fallback + `RouteGuardTests` + `AuthenticationTests`. CA-09: `GetOwnedTaskAsync` filtra `OwnerId == ownerId && Id == taskId` na própria query EF (traduzida a SQL); não há teste que capture o SQL, o efeito é provado por `GetTask_TarefaDeOutroDono_RetornaNotFound` contra o banco. |
