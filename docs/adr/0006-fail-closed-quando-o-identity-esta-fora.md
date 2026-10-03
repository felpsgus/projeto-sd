# ADR-0006 — Identity indisponível: fail-closed com 503

- **Status:** aceita — decisão **D-28** em `tarefas/backend/DECISOES-PENDENTES.md`
- **Contexto de regra:** RN-AUTZ-01; tasks BE-27, BE-28

## Contexto

Para criar uma tarefa o Tasks valida o dono no Identity (`ValidateUser`: o usuário existe?). Se o Identity está
inalcançável, o Tasks pode criar assumindo o dono válido (fail-open) ou recusar (fail-closed).

## Decisão

**Recusar.** O Tasks responde com erro de indisponibilidade, nada é persistido, e o Gateway devolve
`503` + `Retry-After` + `errorCode: identity.unavailable`. O prazo da chamada é `Identity:GrpcTimeoutSeconds`
(padrão 2 s). Nunca é traduzido em 401 nem em "sucesso degradado".

- A FK de ADR-0005 é só rede de segurança: o dono pode ter sido **excluído há instantes**, e só o Identity sabe.
  O Tasks confirma a existência antes de criar; se o Identity não responde, recusa com 503 em vez de criar.
- No `/health/ready` do Tasks, o alcance do Identity aparece como **Degraded** (HTTP 200): o serviço está de pé e
  atende leituras; só a criação falha. O probe gRPC do Cloud Run (D-37) olha só o banco e não é derrubado por isso.
- Erros de validação e de negócio continuam sendo 400/404/409; só `Unavailable`/`DeadlineExceeded` vira 503.

## Alternativas consideradas

1. **Fail-open apoiado na FK.** Tarefas passam a ser criadas sem confirmar o dono e alguém decide depois o que fazer
   com elas. Rejeitada.
2. **Cache do último `ValidateUser`.** Mascara a indisponibilidade, mas guarda estado que pode estar velho
   (usuário excluído). Fica como otimização futura, não como política.

## Consequências

- Identity fora do ar impede a criação de tarefas, mesmo de usuários válidos. Nesta escala é preferível a
  criar, em silêncio, tarefa de um dono possivelmente excluído.
- Todo teste de "serviço fora do ar" afirma 503 e a ausência de linha no banco, não só o status.

> **Emenda (03/10/2026, issue #16).** O usuário não tem mais estado ativo/inativo (RN-USER-04 removida): a justificativa passou de "dono inativo" para "dono possivelmente excluído". A decisão (fail-closed) não mudou.
