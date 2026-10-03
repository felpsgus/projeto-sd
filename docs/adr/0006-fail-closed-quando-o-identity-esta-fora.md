# ADR-0006 — Identity indisponível: fail-closed com 503

- **Status:** aceita — decisão **D-28** em `tarefas/backend/DECISOES-PENDENTES.md`
- **Contexto de regra:** RN-USER-04, RN-AUTZ-01; tasks BE-27, BE-28

## Contexto

Para criar uma tarefa o Tasks valida o dono no Identity (`ValidateUser`: existe? ativo?). Se o Identity está
inalcançável, o Tasks pode criar assumindo o dono válido (fail-open) ou recusar (fail-closed).

## Decisão

**Recusar.** O Tasks responde com erro de indisponibilidade, nada é persistido, e o Gateway devolve
`503` + `Retry-After` + `errorCode: identity.unavailable`. O prazo da chamada é `Identity:GrpcTimeoutSeconds`
(padrão 2 s). Nunca é traduzido em 401 nem em "sucesso degradado".

- A FK de ADR-0005 impede dono **inexistente**, mas não dono **inativo**: o registro continua em `identity.users`.
  Sem o Identity, o Tasks não sabe o estado e criaria uma tarefa que a regra proíbe.
- No `/health/ready` do Tasks, o alcance do Identity aparece como **Degraded** (HTTP 200): o serviço está de pé e
  atende leituras; só a criação falha. O probe gRPC do Cloud Run (D-37) olha só o banco e não é derrubado por isso.
- Erros de validação e de negócio continuam sendo 400/404/409; só `Unavailable`/`DeadlineExceeded` vira 503.

## Alternativas consideradas

1. **Fail-open apoiado na FK.** Tarefas de usuário inativo passam a existir e alguém decide depois o que fazer
   com elas. Rejeitada.
2. **Cache do último `ValidateUser`.** Mascara a indisponibilidade, mas guarda estado que pode estar velho
   (usuário desativado). Fica como otimização futura, não como política.

## Consequências

- Identity fora do ar impede a criação de tarefas, mesmo de usuários válidos. Nesta escala é preferível a violar
  RN-USER-04 em silêncio.
- Todo teste de "serviço fora do ar" afirma 503 e a ausência de linha no banco, não só o status.
