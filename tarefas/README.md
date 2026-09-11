# Quebra de Tarefas — Todo List

Quebra das regras de negócio de [REGRAS-DE-NEGOCIO.md](../REGRAS-DE-NEGOCIO.md) em tarefas de implementação, respeitando [CONVENCOES-CODIGO.md](../CONVENCOES-CODIGO.md).

## Organização

```
tarefas/
├── README.md              ← este arquivo
├── backend/               ← .NET 10 / ASP.NET Core / EF Core — dois microsserviços
│   ├── README.md          ← índice, ordem de execução e rastreabilidade RN → task
│   ├── DECISOES-PENDENTES.md
│   └── BE-01 … BE-39      ← uma tarefa por arquivo (BE-32 a BE-39: API Gateway, T2)
└── frontend/              ← Angular 22 (standalone, signals-first, zoneless)
    ├── README.md          ← índice, dependência das tasks BE e rastreabilidade RN → task
    ├── DECISOES-PENDENTES.md
    └── FE-01 … FE-23
```

A entrada da quebra do frontend são os **contratos de API** definidos nas tasks BE. Cada task FE aponta a task BE que produz o endpoint que ela consome — e nenhuma task FE fecha antes da BE correspondente.

O backend é composto por **dois serviços** — **Identity** (servidor gRPC) e **Tasks** (cliente gRPC) —, comunicando-se por um contrato Protocol Buffers versionado. Eles compartilham **um banco**, com um schema por serviço (**D-27**). Cada task BE declara a qual serviço pertence. Ver [backend/README.md](backend/README.md). **Isso não muda nada para o frontend:** ele continua consumindo a API REST, e as tasks FE seguem válidas como estão.

A partir do T2, a API REST que o frontend consome é servida pelo **API Gateway** (D-32, D-33): ele autentica, valida o payload e traduz a chamada para gRPC. Identity e Tasks passam a ser alcançáveis só por gRPC, atrás dele. As rotas (`/api/auth/*`, `/api/tasks`) não mudam.

## Decisões estruturais já fechadas

Valem para as duas pontas e estão refletidas nas tasks. Detalhes em [backend/DECISOES-PENDENTES.md](backend/DECISOES-PENDENTES.md) e [frontend/DECISOES-PENDENTES.md](frontend/DECISOES-PENDENTES.md).

| Decisão | Resultado | Por quê |
|---|---|---|
| **Dois serviços** (D-26) | Identity (servidor gRPC) e Tasks (cliente gRPC), sem referência de projeto entre si | A comunicação interna precisa ser de rede, não chamada de método |
| **Banco único, schema por serviço** (D-27) | Banco `todolist` com schemas `identity` e `tasks`; FK cruzada com `ON DELETE CASCADE` | Torna a exclusão de conta (RN-USER-05) atômica sem RPC novo; o schema é o que mantém a posse das tabelas explícita |
| **Fail-closed** (D-28) | Identity inalcançável → `503`, tarefa não é criada | A FK garante que o dono existe, mas não que está **ativo** (RN-USER-04) — só o Identity sabe disso |
| **Refresh token** (D-20 / FD-01) | Cookie `HttpOnly; Secure; SameSite=Strict; Path=/api/auth` — fora do corpo JSON | Única forma de cumprir a RN-AUTH-20: o frontend nunca lê o valor |
| **Hospedagem** (D-21 / FD-16 / D-32) | Front e API na mesma origem — e essa origem é o **API Gateway**, único ponto público; Identity e Tasks ficam atrás dele | Dispensa CORS e token anti-CSRF mesmo com o backend dividido em dois serviços; em troca, `withCredentials` é obrigatório |
| **Autoridade sobre tokens** (D-31) | A chave de assinatura fica só no Identity; quem precisa validar chama `ValidateToken` por gRPC | Com HS256, quem valida também assina — distribuir a chave criaria um segundo emissor de tokens |
| **"Atrasada"** (D-18 / FD-17) | Data local do usuário via header `X-Client-Date` | Em UTC−3, o cálculo em UTC marcava a tarefa como atrasada às 21:00 do próprio dia do vencimento |
| **Versionamento** (D-22 / FD-18) | Sem `/v1` | Front e back são implantados juntos; não há consumidor externo |
| **Gateway** (D-33 a D-35) | Projeto único, sem regra de negócio; identidade repassada ao Tasks em metadata `x-user-id`; erros gRPC ↔ HTTP com `errorCode` no trailer | O Gateway autentica e traduz — o contrato REST visto pelo cliente continua o mesmo |
| **Login no T2** (D-36) | Só access token, via RPC `Login`; refresh, logout e bloqueio ficam para depois | Token real e definitivo, sem rota provisória a remover |

> **A decisão do fuso altera o texto das regras.** A RN-TASK-16 fala em "data atual"; passa a significar **data atual do usuário**. Vale corrigir [REGRAS-DE-NEGOCIO.md](../REGRAS-DE-NEGOCIO.md).

## Como ler uma tarefa

Cada documento tem a mesma estrutura:

| Seção | Para quê |
|---|---|
| **Cabeçalho** | domínio, **serviço** (nas tasks BE), dependências, regras de negócio cobertas, estimativa |
| **Objetivo** | uma frase: o que existe no fim da tarefa que não existia antes |
| **Escopo** | o que entra e — importante — o que **não** entra |
| **Notas técnicas** | decisões de implementação já fechadas, para não re-discutir no PR |
| **Critérios de aceite** | `CA-NN` verificáveis. A tarefa **só fecha com todos marcados** |
| **Testes obrigatórios** | o mínimo de teste exigido para o PR passar |
| **Decisões em aberto** | itens da seção 9 das regras de negócio que afetam a task |

## Regras válidas para toda tarefa

Além dos critérios específicos, **toda** task deve cumprir a Definição de Pronto (seção 7 de `CONVENCOES-CODIGO.md`):

- [ ] Build, lint (`dotnet format --verify-no-changes`) e testes verdes no CI.
- [ ] Cobertura respeita os pisos: **≥ 75%** global, **≥ 85%** em Domain/Application.
- [ ] Sem segredo versionado, sem código morto, sem `TODO` órfão.
- [ ] Commits em Conventional Commits; PR < ~400 linhas; 1 aprovação.
- [ ] Nenhuma entidade de domínio exposta pela API — sempre DTO.
- [ ] Nenhum dado sensível (senha, token, PII) em log.
