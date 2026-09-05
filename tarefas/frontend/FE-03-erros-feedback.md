# FE-03 — Erros, feedback e estados de UI

| | |
|---|---|
| **Domínio** | Infraestrutura / UX |
| **Depende de** | [FE-02](FE-02-contratos-camada-http.md) |
| **Bloqueia** | todas as telas |
| **Regras cobertas** | habilita RN-AUTH-09, RN-AUTZ-03, RN-TASK-15 e toda mensagem de erro |
| **Estimativa** | M |

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

- [ ] **CA-01** — Um erro 400 de validação produz `fieldErrors` com os campos e mensagens, e o formulário consegue exibi-los junto aos inputs corretos.
- [ ] **CA-02** — Um código de erro conhecido é traduzido para a mensagem pt-BR correspondente.
- [ ] **CA-03** — Um código de erro **desconhecido** cai na mensagem genérica, sem quebrar a tela e sem exibir o código cru ao usuário.
- [ ] **CA-04** — Erro de rede (servidor inalcançável) exibe mensagem de conectividade, não "erro 0" nem tela em branco.
- [ ] **CA-05** — Uma resposta 500 **nunca** exibe stack trace, nome de exceção ou detalhe interno — mesmo que o corpo os contivesse.
- [ ] **CA-06** — Uma resposta que não é JSON válido é tratada sem lançar exceção não capturada.
- [ ] **CA-07** — O `traceId` é preservado no `AppError` e aparece na mensagem de erro genérica.
- [ ] **CA-08** — O toast de erro é anunciado por leitor de tela (`aria-live="assertive"`); o de sucesso usa `aria-live="polite"`.
- [ ] **CA-09** — O toast pode ser fechado pelo teclado e não some rápido demais para ser lido (mínimo configurável, ≥ 5 s para erro).
- [ ] **CA-10** — `<app-error-state>` oferece "tentar novamente" e o clique reexecuta a operação que falhou.
- [ ] **CA-11** — Nenhuma string de mensagem de erro existe fora do arquivo central (verificado por busca no código).
- [ ] **CA-12** — A ordem dos interceptors está declarada explicitamente e coberta por um teste que confirma o encadeamento.
- [ ] **CA-13** — Mensagens com parâmetro usam o valor vindo da API, não um número fixo no frontend.
- [ ] **CA-14** — Nenhum erro é enviado ao `console` em produção com dado sensível; o build de produção não faz `console.log` de payload de request.

## Testes obrigatórios

- Unidade: interceptor de erro com respostas simuladas — CA-01 a CA-07, CA-12.
- Componente (Testing Library): toast e estados de tela, consultando por texto e `role` — CA-08 a CA-10.
