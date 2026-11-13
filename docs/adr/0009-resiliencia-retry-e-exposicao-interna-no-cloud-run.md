# ADR-0009 — Cloud Run: retry só em leitura e identity/tasks com ingress interno

- **Status:** aceita — decisões **D-44** e **D-45** em `tarefas/backend/DECISOES-PENDENTES.md`
- **Contexto de regra:** D-28 (ADR-0006), D-32; tasks BE-43, BE-44, BE-45

## Contexto

No T3 os quatro serviços sobem no Cloud Run, onde instâncias somem por instantes (cold start, redeploy). Duas
perguntas: o que o Gateway faz quando uma chamada gRPC falha de forma transitória, e quem pode alcançar Identity
e Tasks (a D-32 manda a borda pública ser única). O fail-closed da D-28 (ADR-0006) continua valendo.

## Decisão

**Retry (D-44).** Os clientes gRPC usam a política nativa do `Grpc.Net.Client` (`ServiceConfig`/`RetryPolicy`),
sem biblioteca extra:

- só nas leituras (`ValidateUser`, `GetProfile`, `ListTasks`, `GetTask`); escrita nunca é retentada;
- só para `StatusCode.Unavailable`; `Backends:GrpcRetryMaxAttempts=3` (faixa 2 a 5), backoff de 200 ms a 1 s,
  multiplicador 2;
- esgotadas as tentativas, vale a D-28: `503` + `Retry-After`.

**Exposição (D-45).** `todolist-frontend` e `todolist-gateway` são públicos. `todolist-identity` e
`todolist-tasks` sobem com `--ingress internal`; o Gateway os alcança pelas URLs `*.run.app` via Direct VPC egress
e Private Google Access, h2c, **sem** ID token/IAM (por isso levam `--allow-unauthenticated`: a barreira é só o
ingress). O parâmetro `-Exposure Internal|Public` do `deploy.ps1` troca o modo se for preciso expor tudo.

## Alternativas consideradas

1. **Circuit breaker (Polly ou similar).** Complexidade sem ganho com três chamadores e uma demo. Rejeitada.
2. **Retentar escritas.** Um `CreateTask` que chegou ao Tasks mas perdeu a resposta seria duplicado. Rejeitada.
3. **IAM entre serviços (ID token).** Código novo de autenticação só para repetir o que o ingress interno já
   garante nesta escala. Rejeitada.

## Consequências

- Sem circuit breaker: com o Tasks fora por muito tempo, cada leitura gasta as 3 tentativas antes do 503.
- A não exposição é demonstrável: a URL interna aberta no navegador responde 404.
- Quem estiver dentro da VPC alcança Identity e Tasks sem credencial; aceitável aqui, e é o custo de não ter IAM.
- Trocar a exposição é um parâmetro, não uma mudança de código.
