# BE-03 — `Result<T>`, tratamento de erros HTTP e validação

| | |
|---|---|
| **Domínio** | Infraestrutura / Application |
| **Serviço** | ambos (o tipo mora em `TodoList.SharedKernel`) |
| **Depende de** | [BE-01](BE-01-fundacao-solution.md) |
| **Bloqueia** | todos os casos de uso |
| **Regras cobertas** | habilita RN-AUTH-09, RN-AUTZ-03 e toda mensagem de erro de negócio |
| **Estimativa** | M |

## Objetivo

Erro de negócio e erro técnico têm caminhos distintos e previsíveis: o primeiro é um valor de retorno (`Result<T>`), o segundo é exceção. Ambos chegam ao cliente como `ProblemDetails` consistente, sem vazar detalhe interno.

## Escopo

### Inclui

- `Result` e `Result<T>` em **`TodoList.SharedKernel`** (decisão **D-26**) — para que Identity e Tasks reportem erro da mesma forma sem um referenciar o outro —, imutáveis, com:
  - estado de sucesso/falha;
  - um `Error` com **código** (`string`, estável, ex.: `auth.invalid_credentials`), **mensagem** legível e **tipo** (`Validation`, `NotFound`, `Conflict`, `Unauthorized`, `Forbidden`, `TooManyRequests`, `Failure`);
  - impossibilidade de acessar `Value` de um resultado de falha (lança se tentar — é bug de programação, não de negócio).
- Catálogo de erros por domínio, **dentro do serviço dono do domínio** — `AuthErrors`/`UserErrors` na `Application` do Identity, `TaskErrors` na `Application` do Tasks. **Sem strings soltas** nos handlers e **sem catálogo compartilhado**: só o tipo `Error` é comum, não o vocabulário de negócio.
- Mapeamento `ErrorType` → status HTTP:

  | ErrorType | HTTP |
  |---|---|
  | `Validation` | 400 |
  | `Unauthorized` | 401 |
  | `Forbidden` | 403 |
  | `NotFound` | 404 |
  | `Conflict` | 409 |
  | `TooManyRequests` | 429 |
  | `Failure` | 500 |

- Extensão que converte `Result`/`Result<T>` em `IResult` de Minimal API, para os endpoints não repetirem o `switch`. Aplicada nos dois serviços; mora em `SharedKernel` apenas se não arrastar dependência de negócio, caso contrário duplica-se a extensão (poucas linhas) em cada `Api`.
- **FluentValidation** integrado: validadores por request DTO, executados por um comportamento/filtro de endpoint **antes** do handler. Falha de validação → 400 com `ProblemDetails` contendo os erros por campo.
- Handler global de exceções (`IExceptionHandler`) **em cada `Api`**, devolvendo 500 com `ProblemDetails` genérico, um `traceId` correlacionável, e **nunca** stack trace ou mensagem de exceção fora de `Development`.
- Todo `ProblemDetails` inclui `traceId`.

### Não inclui

- Validadores específicos de cadastro/tarefa — cada um entra na sua task.
- Logging estruturado (BE-24), embora o handler global já deva logar a exceção.

## Notas técnicas

- Regra da seção 2.1 das convenções: **erro esperado de negócio usa `Result<T>`; exceção fica para o excepcional.** Um e-mail duplicado é `Result` de falha, não `DuplicateEmailException`.
- Códigos de erro são contrato com o frontend: uma vez publicados, mudam com versionamento, não por refactor.
- A mensagem exposta ao cliente é sempre a do catálogo, nunca uma interpolação com dado interno.

## Critérios de aceite

- [ ] **CA-01** — `Result<T>.Value` em um resultado de falha lança; `Result.Error` em um sucesso lança. Ambos cobertos por teste.
- [ ] **CA-02** — Cada `ErrorType` mapeia para o status HTTP da tabela acima, verificado por teste parametrizado cobrindo **todos** os valores do enum.
- [ ] **CA-03** — Toda resposta de erro da API tem `Content-Type: application/problem+json` e inclui `type`, `title`, `status`, `detail` e `traceId`.
- [ ] **CA-04** — Um request inválido retorna **400** com a lista de erros **por campo**, não uma mensagem única concatenada.
- [ ] **CA-05** — Uma exceção não tratada em um handler retorna **500** com corpo genérico; em ambiente não-`Development` o corpo **não** contém nome de tipo, stack trace nem mensagem da exceção original.
- [ ] **CA-06** — O `traceId` da resposta de erro corresponde ao da entrada de log gerada para aquela requisição.
- [ ] **CA-07** — Não existe nenhuma string literal de mensagem de erro fora do catálogo de erros (verificado em revisão; opcionalmente por analyzer).
- [ ] **CA-08** — Um endpoint que retorna `Result` de falha do tipo `NotFound` responde 404 **sem** o handler precisar escrever o `switch` manualmente.
- [ ] **CA-09** — Os dois serviços produzem `ProblemDetails` com a mesma forma para o mesmo `ErrorType`, comprovado por teste de integração em cada um.
- [ ] **CA-10** — `TodoList.SharedKernel` contém apenas `Result`, `Result<T>`, `Error` e `ErrorType` — nenhuma entidade, DTO de negócio ou regra (**D-26**). Verificado por revisão e por teste de arquitetura sobre os tipos públicos do assembly.

## Testes obrigatórios

- Unidade: contratos de `Result`/`Result<T>` (CA-01), mapeamento de erros (CA-02).
- Integração: endpoint de exemplo cobrindo CA-03, CA-04, CA-05, CA-08, CA-09 — **em cada serviço**.
- Arquitetura: CA-10.

## Decisões em aberto

- **D-26** — `SharedKernel` como único código compartilhado entre os serviços. Ver [DECISOES-PENDENTES.md](DECISOES-PENDENTES.md).
