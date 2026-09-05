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

- [ ] **CA-01** — `GET /api/tasks/{id}` de uma tarefa própria retorna **200** com todos os campos do `TaskResponse`.
- [ ] **CA-02** — `isOverdue` vem calculado corretamente na resposta (RN-TASK-16).
- [ ] **CA-03** — `GET` de id inexistente retorna **404**.
- [ ] **CA-04** — `GET` de uma tarefa **de outro usuário** retorna **404** — nunca 403, nunca 200 (RN-AUTZ-02, RN-AUTZ-03).
- [ ] **CA-05** — Os corpos de CA-03 e CA-04 são **byte a byte idênticos**: mesmo `type`, `title`, `detail` e código de erro. Nenhum cabeçalho os distingue.
- [ ] **CA-06** — `GET` de tarefa própria **removida** (soft delete) retorna o mesmo **404**.
- [ ] **CA-07** — `GET` com id em formato inválido (`/api/tasks/abc`) retorna **400**, não 500.
- [ ] **CA-08** — `GET` sem token retorna **401** (RN-AUTZ-04).
- [ ] **CA-09** — O SQL gerado inclui o filtro de `owner_id` na consulta — a autorização não é feita em memória (verificável por log de query em teste, ou por inspeção do `IQueryable`).
- [ ] **CA-10** — Não existe, na camada de aplicação, nenhum método público que carregue uma `TodoTask` por id **sem** filtro de dono (verificado por revisão + teste de arquitetura sobre a superfície do repositório).
- [ ] **CA-11** — Um teste de integração parametrizado percorre **todos** os endpoints de tarefa que recebem `{id}` (`GET`, `PUT/PATCH`, `POST /complete`, `POST /reopen`, `DELETE`) e confirma que **cada um** retorna 404 para tarefa de outro usuário. Ao adicionar um endpoint novo com `{id}`, ele entra nesse teste.

## Testes obrigatórios

- Integração: CA-01 a CA-09.
- **Teste transversal CA-11** — é o guardião de RN-AUTZ-02/03 e deve ser mantido conforme BE-19/20/21 entrarem.
- Arquitetura/revisão: CA-10.
