# ADR-0004 — Três serviços, comunicação por gRPC e o Gateway como única borda

- **Status:** aceita — decisões **D-29**, **D-32**, **D-33**, **D-34**, **D-35** em `tarefas/backend/DECISOES-PENDENTES.md`
- **Contexto de regra:** enunciado do T1/T2; tasks BE-01, BE-25, BE-32 a BE-36

## Contexto

O trabalho exige serviços separados que se comuniquem pela rede. Identity (usuário, senha, token) e Tasks
(tarefas) têm responsabilidades e ciclos de mudança diferentes; o navegador, por sua vez, precisa de **uma**
origem só (`SameSite=Strict` sem CORS, cookie de refresh com `Path=/api/auth`, D-20/D-21).

## Decisão

```
frontend (nginx) ─▶ Gateway (REST) ─┬─ gRPC ─▶ Identity   (autoridade de usuário/token)
                                    └─ gRPC ─▶ Tasks      (CRUD de tarefas) ─ gRPC ─▶ Identity (ValidateUser)
```

- **Três processos .NET.** O **Gateway** é a única borda pública (REST/JSON). Identity e Tasks falam só gRPC e
  **não** são publicados (D-32). O nginx só serve estático e faz `proxy_pass` de `/api` (D-40).
- **Contratos em `contracts/*/v1/*.proto`** (D-29): fonte única, referenciada por caminho relativo; cada projeto
  gera só o lado que usa (`Server`/`Client`). Identity e Tasks **não têm referência de projeto entre si** — só os
  `.proto` e o `SharedKernel` (D-26). O Gateway nem o `SharedKernel` referencia (D-33); teste de arquitetura vigia.
- **Gateway sem regra de negócio** (D-33): autentica, valida o formato do payload e traduz.
- **Identidade por metadata** (D-34): depois de validar o token, o Gateway põe o `sub` em `x-user-id`; o Tasks
  confia no chamador, por isso nunca é exposto.
- **Erros** (D-35): o status gRPC vira HTTP por uma tabela única (`InvalidArgument`→400, `NotFound`→404,
  `FailedPrecondition`→409, `Unauthenticated`→401, `Unavailable`/`DeadlineExceeded`→503) e o `error-code` do trailer
  vira `errorCode` no `ProblemDetails`.
- **Correlação**: o `traceparent` W3C viaja na chamada gRPC; o `traceId` é o mesmo no log dos serviços envolvidos
  e no `ProblemDetails` (BE-24).

## Alternativas consideradas

1. **Monólito.** Mais simples, mas não cumpre o enunciado de serviços distribuídos.
2. **REST entre os serviços.** Funciona, porém sem contrato tipado compartilhado; o `.proto` pega divergência na
   compilação, não em runtime.
3. **Cada serviço exposto ao navegador.** Duas origens: CORS, cookie e CSRF voltam a ser problema. Rejeitada.

## Consequências

- Uma chamada de rede a mais por operação; falhas parciais existem e precisam de política (ADR-0006).
- Quem alcançar a porta gRPC do Tasks cria tarefa em nome de qualquer usuário: **os backends não podem ter `ports:`
  públicos** (compose, firewall da VM, Cloud Run privado).
- Mudar um `.proto` é mudar o contrato entre processos: só campos novos e opcionais, sem renumerar.
