# BE-23 — Expurgo de tarefas removidas (retenção)

| | |
|---|---|
| **Domínio** | Tarefas / Operação |
| **Depende de** | [BE-21](BE-21-remover-tarefa.md), [BE-10](BE-10-refresh-token-rotacao.md) |
| **Bloqueia** | — |
| **Regras cobertas** | RN-TASK-13 (segunda metade: "removida definitivamente após período") |
| **Estimativa** | P |

## Objetivo

Tarefas soft-deleted são apagadas definitivamente depois do período de retenção, e tokens vencidos não se acumulam indefinidamente.

## Escopo

### Inclui

- `BackgroundService` `DataRetentionWorker` (D-13) que, em intervalo configurável:
  1. apaga fisicamente `TodoTask` com `DeletedAt < agora - Tasks:SoftDeleteRetentionDays`;
  2. apaga `RefreshToken` expirados ou revogados há mais de `Auth:TokenRetentionDays`;
  3. apaga registros de tentativa de login (BE-12) mais antigos que a janela relevante.
- `RetentionOptions`:

  | Chave | Padrão |
  |---|---|
  | `Retention:Enabled` | `true` |
  | `Retention:IntervalHours` | 24 |
  | `Tasks:SoftDeleteRetentionDays` | **30** (D-12) |
  | `Auth:TokenRetentionDays` | 30 |
  | `Retention:BatchSize` | 500 |

- Exclusão em **lotes** (`ExecuteDeleteAsync` com `Take`), respeitando `CancellationToken` do shutdown.
- Log estruturado por execução: quantos registros de cada tipo foram removidos, duração, e erro (se houver).
- Endpoint administrativo? **Não** — não há papel de admin nesta versão. O disparo manual, se necessário, é feito por configuração/reinício.

### Não inclui

- Restauração de tarefa removida.
- Arquivamento/exportação antes do expurgo.

## Notas técnicas

- Esta é **a única** parte do sistema, junto com BE-16, autorizada a usar `IgnoreQueryFilters()` sobre `TodoTask` — o filtro global esconde exatamente as linhas que o worker precisa ver.
- Uma falha no worker **não pode derrubar a aplicação**: cada ciclo é envolvido em try/catch com log de erro, e o próximo ciclo tenta de novo.
- Lotes existem para não travar tabela nem estourar transação em um expurgo grande (ex.: primeira execução após meses).
- Múltiplas instâncias da API rodando o mesmo worker é aceitável aqui: a exclusão é idempotente (`DELETE WHERE` sobre linhas que já sumiram não faz nada). Registrar isso; se virar problema, adotar lock distribuído.
- `TimeProvider` para todo o cálculo de corte — o teste avança o relógio.

## Critérios de aceite

- [ ] **CA-01** — Uma tarefa com `DeletedAt` de 31 dias atrás é **apagada fisicamente** pelo worker (RN-TASK-13).
- [ ] **CA-02** — Uma tarefa com `DeletedAt` de 29 dias atrás **permanece** no banco.
- [ ] **CA-03** — Uma tarefa **não** removida (`DeletedAt` nulo) nunca é tocada, por mais antiga que seja.
- [ ] **CA-04** — O corte usa `Tasks:SoftDeleteRetentionDays`: mudar para 1 dia faz o expurgo alcançar tarefas removidas ontem, sem alteração de código.
- [ ] **CA-05** — Refresh tokens expirados/revogados além da retenção são removidos; os ainda válidos **não** são.
- [ ] **CA-06** — Registros de tentativa de login antigos são removidos sem afetar bloqueios **ativos** (uma conta bloqueada continua bloqueada após o expurgo).
- [ ] **CA-07** — Com mais registros que `Retention:BatchSize`, o worker processa em múltiplos lotes até esvaziar o backlog, sem uma transação única gigante.
- [ ] **CA-08** — Uma exceção durante o ciclo é **logada** e **não** derruba a aplicação; o ciclo seguinte executa normalmente.
- [ ] **CA-09** — Com `Retention:Enabled = false`, o worker não remove nada.
- [ ] **CA-10** — No shutdown da aplicação, o worker encerra respeitando o `CancellationToken`, sem deixar transação aberta.
- [ ] **CA-11** — Cada execução emite um log estruturado com as contagens removidas por tipo e a duração.
- [ ] **CA-12** — Rodar o worker duas vezes seguidas é idempotente: a segunda execução remove zero registros e não gera erro.
- [ ] **CA-13** — O expurgo de tarefas de um usuário não afeta as de outro.

## Testes obrigatórios

- Integração com `TimeProvider` fake e Testcontainers: CA-01 a CA-07, CA-09, CA-12, CA-13. **Nenhum teste espera tempo real.**
- Unidade: tratamento de erro do ciclo — CA-08.
- A lógica de expurgo fica numa classe testável separada do `BackgroundService`, para que os testes não precisem hospedar o worker.

## Decisões em aberto

- **D-12** — Período de retenção. Padrão provisório: 30 dias. **Precisa de confirmação de produto** — a RN-TASK-13 não define o valor.
- **D-13** — `BackgroundService` in-process. Padrão provisório.
