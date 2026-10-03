# ADR-0005 — Um banco PostgreSQL, um schema por serviço

- **Status:** aceita — decisão **D-27** em `tarefas/backend/DECISOES-PENDENTES.md`
- **Contexto de regra:** RN-USER-05 (exclusão de conta), RN-USER-04; tasks BE-02, BE-16, BE-17

## Contexto

Identity e Tasks precisam de persistência própria, mas a exclusão de conta deve levar as tarefas junto e uma
tarefa não pode existir sem dono. Dois bancos tornavam isso inconsistente e dobravam a implantação.

## Decisão

Um banco `todolist` com **dois schemas**: `identity` (`users`, `refresh_tokens`, `login_attempts`) e `tasks`
(`tasks`). Cada `DbContext` tem o **seu** schema padrão e mapeia só as suas tabelas; o `TasksDbContext` não conhece
`identity.users`. Existe **uma** FK atravessando os schemas: `tasks.tasks.owner_id → identity.users(id)` com
`ON DELETE CASCADE`.

- O schema mantém a **posse** explícita: sem ele nada impede o Tasks de consultar `users` direto, e a comunicação
  gRPC viraria decorativa. Ler o usuário continua exigindo `ValidateUser`.
- A FK é a rede de segurança do banco (dono existe); a regra de negócio (dono **ativo**, RN-USER-04) é do gRPC.
- Cada serviço tem as suas migrations. **Identity sempre antes do Tasks**: a FK depende das tabelas de `identity`.

## Alternativas consideradas

1. **Dois bancos.** Isolamento total, mas exclusão de conta coordenada entre serviços e sem garantia estrutural de
   dono. Rejeitada nesta escala.
2. **Um schema compartilhado.** Posse difusa; rejeitada.

## Consequências

- Os dois serviços dependem do mesmo banco: mudar o tipo de `users.id` quebra a FK do Tasks e exige implantação
  coordenada.
- A exclusão de conta é uma operação só do Identity; o cascade remove as tarefas. O expurgo de retenção
  (ADR-0003) roda em cada serviço sobre o seu schema.
- Voltar a dois bancos exige exclusão coordenada e faz da validação de dono a única garantia de integridade.
