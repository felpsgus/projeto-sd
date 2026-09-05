# BE-16 — Exclusão da própria conta

| | |
|---|---|
| **Domínio** | Usuário |
| **Serviço** | Identity (a remoção das tarefas é feita pelo banco, em cascata) |
| **Depende de** | [BE-02](BE-02-persistencia-base.md), [BE-05](BE-05-dominio-tarefa.md), [BE-10](BE-10-refresh-token-rotacao.md), [BE-13](BE-13-protecao-endpoints.md) |
| **Bloqueia** | — |
| **Regras cobertas** | RN-USER-05, RN-AUTH-19 |
| **Estimativa** | M |

## Objetivo

O usuário apaga a própria conta; as tarefas dele somem junto e nenhuma sessão sobrevive.

## Escopo

### Inclui

- **`DELETE /api/me`** (autenticado).
- Request: `{ "password": "string" }` — confirmação de senha (D-19), mesma proteção da troca de senha.
- Caso de uso `DeleteAccountHandler`, no **Identity Service**, em **uma transação**:
  1. verifica a senha → falha → erro, nada é apagado;
  2. remove todos os refresh tokens e registros de tentativa de login do usuário;
  3. remove o usuário — e a remoção das tarefas acontece **em cascata, no banco** (ver abaixo).
- Resposta **204 No Content**.
- Mapeamento EF com `OnDelete(DeleteBehavior.Cascade)` de `User` para `RefreshToken` e `LoginAttempt` — ambos no schema `identity`, portanto no modelo do próprio contexto.
- **As tarefas são removidas pela FK `tasks.tasks.owner_id → identity.users(id)` com `ON DELETE CASCADE`** ([BE-02](BE-02-persistencia-base.md), CA-02c/CA-16). O handler **não** enumera nem apaga tarefas: ele não tem — e não deve ter — acesso ao modelo do Tasks Service (**D-27**).

### Não inclui

- Anonimização (alternativa de D-05, não adotada).
- Exclusão de conta por administrador — não há papel de admin nesta versão.
- Período de arrependimento / conta em "pending deletion".

## Notas técnicas

- **Por que a cascata do banco, e não uma chamada ao Tasks Service.** O Identity precisaria de um RPC novo (`DeleteUserTasks`) e a comunicação passaria a ser bidirecional — Tasks→Identity **e** Identity→Tasks —, com o problema de consistência de sempre: se a segunda chamada falha depois de a primeira ter sido confirmada, sobra lixo e ninguém reconcilia. Com banco único (**D-27**), a cascata resolve isso de forma atômica e sem RPC novo. É o principal ganho concreto daquela decisão.
- **A cascata alcança as tarefas soft-deleted automaticamente.** O `ON DELETE CASCADE` opera sobre linhas, não sobre o filtro global de query do EF — que é um detalhe da camada de aplicação e não existe para o banco. O CA-05 continua sendo obrigatório, mas agora verifica um comportamento que se ganha de graça, em vez de um passo que era fácil esquecer.
- **A verificação de CA-04 e CA-05 precisa ignorar o filtro global** ao consultar o resultado (`IgnoreQueryFilters()`), senão o teste passa mesmo com tarefas remanescentes — o filtro esconderia justamente as soft-deleted que deveriam ter sumido.
- A transação do handler cobre `users`, `refresh_tokens` e `login_attempts`; a cascata em `tasks.tasks` roda dentro da mesma transação do banco, porque é o mesmo banco. Atomicidade preservada (CA-11).
- Após o `DELETE`, o access token corrente continua criptograficamente válido por até 15 min, mas qualquer requisição que carregue o usuário falha — ver CA-09.
- Operação irreversível: a resposta de sucesso não devolve nada e a documentação OpenAPI deve deixar isso explícito.

## Critérios de aceite

- [ ] **CA-01** — `DELETE /api/me` com a senha correta retorna **204**.
- [ ] **CA-02** — Após a exclusão, o login com aquelas credenciais retorna **401** (`auth.invalid_credentials`) — indistinguível de e-mail inexistente (RN-AUTH-09).
- [ ] **CA-03** — O usuário não existe mais na tabela de usuários.
- [ ] **CA-04** — **Todas** as tarefas do usuário foram removidas do banco (RN-USER-05) — consultado em `tasks.tasks` com `IgnoreQueryFilters()`.
- [ ] **CA-05** — Tarefas que estavam com **soft delete** também foram removidas. Nenhum registro órfão permanece.
- [ ] **CA-05b** — A remoção das tarefas acontece **sem** o Identity Service chamar o Tasks Service: nenhum RPC novo foi adicionado ao contrato ([BE-25](BE-25-contrato-grpc-identity.md), CA-05 continua valendo — dois RPCs, nenhum a mais).
- [ ] **CA-06** — Todos os refresh tokens do usuário foram removidos: nenhum renova (RN-AUTH-19).
- [ ] **CA-07** — Os registros de tentativa de login daquele e-mail foram removidos.
- [ ] **CA-08** — Tarefas e sessões de **outros usuários** permanecem intactas (verificado com uma segunda conta povoada no mesmo teste).
- [ ] **CA-09** — Uma requisição feita com o access token do usuário excluído, ainda dentro da validade, retorna **401** — não 500 e não 200 com dados vazios.
- [ ] **CA-10** — Senha incorreta retorna erro e **nada** é apagado: usuário, tarefas e sessões continuam íntegros.
- [ ] **CA-11** — Se qualquer etapa falhar, **nada** é apagado (atomicidade — testado forçando falha no meio da transação).
- [ ] **CA-12** — Requisição sem autenticação retorna **401**.
- [ ] **CA-13** — O e-mail liberado pode ser usado num **novo cadastro** depois da exclusão.
- [ ] **CA-14** — Nenhum log contém a senha enviada.

## Testes obrigatórios

- Integração (é onde esta task realmente se prova): CA-01 a CA-10, CA-12, CA-13. O cenário base cria usuário com tarefas ativas, tarefas soft-deleted, duas sessões, e uma segunda conta de controle.
- Integração: atomicidade — CA-11.
- Unidade: `DeleteAccountHandler` — CA-10.

## Decisões em aberto

- **D-05** — Apagar vs. anonimizar. Padrão adotado: apagar.
- **D-19** — Exigir confirmação de senha. Padrão provisório: sim.
- **D-27** — Banco único com schema por serviço; é a decisão que torna esta task viável sem RPC novo. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
