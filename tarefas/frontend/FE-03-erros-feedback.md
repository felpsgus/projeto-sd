# FE-03 — Erros, feedback e estados de UI

| | |
|---|---|
| **Domínio** | Infraestrutura / UX |
| **Depende de** | [FE-02](FE-02-contratos-camada-http.md) |
| **Bloqueia** | todas as telas |
| **Regras cobertas** | habilita RN-AUTH-09, RN-AUTZ-03, RN-TASK-15 e toda mensagem de erro |
| **Estimativa** | M |

> **Recorte do T2 (21/09/2026):** entra com um **acréscimo**: um indicador discreto do último status HTTP recebido (ex.: um chip no canto da tela, ligado por flag de `environment`), pensado só para a demo — a plateia enxerga 400/401/201 sem abrir o DevTools. O mapa de erro→mensagem cobre só os códigos que o backend do T2 emite: `auth.invalid_credentials` (401 de login), `auth.unauthorized` (401 de token ausente/inválido/expirado, ver [FE-06](FE-06-interceptor-auth-refresh.md)) e os erros de campo do 400 de criação de tarefa. **Fica para depois:** `auth.too_many_attempts`, `task.active_limit_reached` e os demais códigos que dependem de endpoints fora do T2. Depende de [BE-36](../backend/BE-36-api-gateway.md).
>
> **Formato real do 401 extraído do Gateway** (`Authentication/IdentityTokenAuthenticationHandler.cs`): `ProblemDetails` com `status: 401`, `title: "Não autenticado."`, `detail: "Autenticação ausente, inválida ou expirada."` e `extensions.errorCode: "auth.unauthorized"` — o mesmo corpo para token ausente, inválido ou expirado (CA-12 de BE-36); o 401 de credencial de login usa `errorCode: "auth.invalid_credentials"` em vez disso.

## Objetivo

Todo erro vindo da API vira uma mensagem em português compreensível, exibida de forma consistente — e nenhuma tela precisa interpretar status HTTP na mão.

## Escopo

### Inclui

- **Interceptor de erro HTTP** que converte `HttpErrorResponse` em um tipo interno `AppError`:

  ```ts
  { code: ApiErrorCode | 'network' | 'unknown', message: string, fieldErrors?: Record<string, string[]>, status: number, traceId?: string }
  ```

- **Mapa de código → mensagem pt-BR**, centralizado (FD-03). Nenhuma string de erro escrita dentro de componente. Exemplos:

  | Código do backend | Mensagem exibida |
  |---|---|
  | `auth.invalid_credentials` | "E-mail ou senha inválidos." |
  | `auth.email_already_registered` | "Este e-mail já está cadastrado." |
  | `auth.too_many_attempts` | "Muitas tentativas. Tente novamente em {tempo}." |
  | `task.active_limit_reached` | "Você atingiu o limite de {limite} tarefas ativas." |
  | *(404 em recurso de tarefa)* | "Tarefa não encontrada." |
  | *(sem correspondência)* | "Não foi possível concluir a operação. Tente novamente." |

- Tratamento de casos sem `ProblemDetails`: erro de rede (`status 0`), timeout, resposta não-JSON, 5xx — todos viram `AppError` com mensagem genérica. **Nunca** exibir texto cru do servidor.
- **Erros de campo**: o `ProblemDetails` de validação (400) traz erros por campo ([BE-03](../backend/BE-03-result-erros-validacao.md)/CA-04); o mapeamento os entrega em `fieldErrors` para os formulários exibirem junto ao input correspondente.
- Componente de **notificação** (toast/snackbar) para erros e sucessos de ação, com `aria-live` apropriado.
- Componentes compartilhados de estado de tela, usados por toda feature:
  - `<app-loading>` — indicador de carregamento;
  - `<app-empty-state>` — vazio, com mensagem e ação opcional;
  - `<app-error-state>` — erro, com botão de **tentar novamente**.
- Utilitário de estado assíncrono baseado em signals (`idle | loading | success | error`) para as telas não reinventarem o controle.

### Não inclui

- Renovação de token em 401 (FE-06) — o interceptor de erro **não** trata 401 de sessão; ele é encadeado depois do de autenticação.
- Textos específicos de cada tela — ficam nas suas tasks, mas sempre no arquivo central de mensagens.

## Notas técnicas

- **Ordem dos interceptors importa e é fonte comum de bug:** auth (anexa token) → refresh (trata 401) → erro (traduz o resto). O interceptor de erro nunca deve "consumir" um 401 que o de refresh precisaria reprocessar.
- O `traceId` do `ProblemDetails` é preservado no `AppError` e exibido de forma discreta na mensagem de erro genérica — é o que torna um relato de usuário investigável no log do backend (CA-02 de [BE-24](../backend/BE-24-observabilidade-ci.md)).
- **RN-AUTH-09 e RN-AUTZ-03 dependem desta task não ser "esperta":** se o mapa tentar diferenciar "e-mail não existe" de "senha errada", ou "tarefa de outro usuário" de "tarefa inexistente", quebra a regra que o backend cuidou de manter. O mapa traduz o código recebido, e nada além.
- Mensagem com parâmetro (`{tempo}`, `{limite}`) vem do próprio `ProblemDetails`; não hardcodar "15 minutos" nem "500" no frontend — os valores são configuráveis no backend.

## Critérios de aceite

- [x] **CA-01** — Um erro 400 de validação produz `fieldErrors` com os campos e mensagens, e o formulário consegue exibi-los junto aos inputs corretos.
- [x] **CA-02** — Um código de erro conhecido é traduzido para a mensagem pt-BR correspondente.
- [x] **CA-03** — Um código de erro **desconhecido** cai na mensagem genérica, sem quebrar a tela e sem exibir o código cru ao usuário.
- [x] **CA-04** — Erro de rede (servidor inalcançável) exibe mensagem de conectividade, não "erro 0" nem tela em branco.
- [x] **CA-05** — Uma resposta 500 **nunca** exibe stack trace, nome de exceção ou detalhe interno — mesmo que o corpo os contivesse.
- [x] **CA-06** — Uma resposta que não é JSON válido é tratada sem lançar exceção não capturada.
- [ ] **CA-07** — O `traceId` é preservado no `AppError` e aparece na mensagem de erro genérica.
- [ ] **CA-08** — O toast de erro é anunciado por leitor de tela (`aria-live="assertive"`); o de sucesso usa `aria-live="polite"`.
- [ ] **CA-09** — O toast pode ser fechado pelo teclado e não some rápido demais para ser lido (mínimo configurável, ≥ 5 s para erro).
- [x] **CA-10** — `<app-error-state>` oferece "tentar novamente" e o clique reexecuta a operação que falhou.
- [ ] **CA-11** — Nenhuma string de mensagem de erro existe fora do arquivo central (verificado por busca no código).
- [x] **CA-12** — A ordem dos interceptors está declarada explicitamente e coberta por um teste que confirma o encadeamento.
- [x] **CA-13** — Mensagens com parâmetro usam o valor vindo da API, não um número fixo no frontend.
- [x] **CA-14** — Nenhum erro é enviado ao `console` em produção com dado sensível; o build de produção não faz `console.log` de payload de request.

## Testes obrigatórios

- Unidade: interceptor de erro com respostas simuladas — CA-01 a CA-07, CA-12.
- Componente (Testing Library): toast e estados de tela, consultando por texto e `role` — CA-08 a CA-10.

## Auditoria dos critérios (03/10/2026)

Critérios conferidos contra o código em 03/10/2026. Marcados: 10 de 14.

| CA | Situação | Evidência / motivo |
|---|---|---|
| CA-07 | em aberto | `traceId` é preservado e testado em `AppError` e anexado como "(ref.: id)" à mensagem genérica (`error-mapper.ts`), mas nenhum teste cobre a mensagem genérica com `traceId`. |
| CA-08 | em aberto | Não existe componente de toast/notificação; erros aparecem inline (`role="alert"`/`aria-live="assertive"` nos formulários). O objetivo é parcialmente coberto de outra forma, mas o critério fala de toast. |
| CA-09 | em aberto | Depende do toast (CA-08), inexistente: sem fechar por teclado nem tempo mínimo configurável. |
| CA-11 | em aberto | Há strings de erro fora de `error-messages.ts`: `'Verifique os campos destacados.'` em `error-mapper.ts`, `'Não foi possível carregar seu perfil.'` (`account.component.html`), `'Não foi possível carregar a tarefa.'` (`edit-task.component.html`) e "Este e-mail já está cadastrado." duplicado em `register.component.html`. |

Também ausente (não é CA): o utilitário de estado assíncrono `idle|loading|success|error`. CA-10 coberto por `tasks-page.component.spec.ts` ("exibe erro com tentar novamente e refaz a chamada"); CA-12 por `interceptor-order.spec.ts`; CA-14 pela ausência de qualquer `console.*` em `src/app`.
